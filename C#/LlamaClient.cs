using System;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace LLamaService
{
    #region Event Args & Configuration
    /// <summary>
    /// Event arguments for streaming updates
    /// </summary>
    public class StreamUpdateEventArgs : EventArgs
    {
        /// <summary>The current accumulated text response</summary>
        public string Text { get; set; }

        /// <summary>Number of tokens generated so far</summary>
        public int TokensGenerated { get; set; }

        /// <summary>Whether generation is complete</summary>
        public bool IsComplete { get; set; }
    }

    /// <summary>
    /// Configuration options for LlamaClient
    /// </summary>
    public class LlamaClientConfig
    {
        /// <summary>Path to the chatbot executable (default: "./build/chatbot")</summary>
        public string ChatbotPath { get; set; } = "llm/llm.exe";

        /// <summary>Default system prompt to use if none is specified</summary>
        public string DefaultSystemPrompt { get; set; } = "You are a helpful medicine assistant.";

        /// <summary>
        /// Delay in milliseconds before connecting to shared memory (default: 3000).
        /// This gives the C++ process time to create shared memory objects.
        /// The actual model loading is handled by waiting for the ready signal.
        /// </summary>
        public int InitializationDelayMs { get; set; } = 3000;

        /// <summary>Enable debug output to console (default: false)</summary>
        public bool EnableDebugOutput { get; set; } = false;

        /// <summary>Timeout in milliseconds for semaphore waits (default: 300000 = 5 minutes, 0 = infinite)</summary>
        public int SemaphoreTimeoutMs { get; set; } = 300000;

        /// <summary>Enable automatic process health monitoring (default: true)</summary>
        public bool EnableProcessMonitoring { get; set; } = true;
    }
    #endregion

    #region Shared Memory Provider Interface
    /// <summary>
    /// Abstraction for platform-specific shared memory operations
    /// </summary>
    internal interface ISharedMemoryProvider : IDisposable
    {
        void Connect();
        bool WaitReady(int timeoutMs = -1);
        void SignalPromptsWritten();
        bool WaitResponseWritten(int timeoutMs = -1);
        bool WaitChunkReady(int timeoutMs = -1);
        void WriteRequest(string systemPrompt, string userPrompt, bool streamMode);
        void WriteShutdownRequest();
        string ReadResponse();
        (string response, int updateCounter, bool isComplete, int tokensGenerated) ReadStreamingState();
    }
    #endregion

    #region Windows Shared Memory Provider
    /// <summary>
    /// Windows implementation using MemoryMappedFile and Semaphore
    /// </summary>
    internal class WindowsSharedMemoryProvider : ISharedMemoryProvider
    {
        // IMPORTANT: Must match C++ main.cpp Config namespace (uses "Local\" not "Global\")
        private const string SharedMemoryName = "Local\\llama_cpp_shared_mem";
        private const string SemReadyName = "Local\\llama_cpp_sem_ready";
        private const string SemPromptsWrittenName = "Local\\llama_cpp_sem_prompts_written";
        private const string SemResponseWrittenName = "Local\\llama_cpp_sem_response_written";
        private const string SemChunkReadyName = "Local\\llama_cpp_sem_chunk_ready";
        
        // Memory layout offsets (must match C++ SharedMemoryData struct with #pragma pack(1))
        private const int SystemPromptOffset = 0;
        private const int UserPromptOffset = 4096;
        private const int ResponseOffset = 8192;
        private const int ShutdownRequestedOffset = 40960;
        private const int StreamModeOffset = 40961;
        private const int UpdateCounterOffset = 40962;
        private const int GenerationCompleteOffset = 40966;
        private const int TokensGeneratedOffset = 40967;
        private const int SharedMemorySize = 45000;  // Must be >= 40971 (actual struct size)

        private MemoryMappedFile _sharedMemory;
        private MemoryMappedViewAccessor _accessor;
        private Semaphore _semReady;
        private Semaphore _semPromptsWritten;
        private Semaphore _semResponseWritten;
        private Semaphore _semChunkReady;

        public void Connect()
        {
            try
            {
                // Open shared memory
                _sharedMemory = MemoryMappedFile.OpenExisting(SharedMemoryName);
                _accessor = _sharedMemory.CreateViewAccessor();
                
                // Validate shared memory size
                if (_accessor.Capacity < TokensGeneratedOffset + 4)
                {
                    throw new Exception(
                        $"Shared memory size ({_accessor.Capacity}) is too small. " +
                        $"Expected at least {TokensGeneratedOffset + 4} bytes.");
                }
            }
            catch (FileNotFoundException)
            {
                throw new Exception(
                    $"Shared memory '{SharedMemoryName}' not found.\n" +
                    "This usually means the C++ chatbot process:\n" +
                    "  1. Hasn't started yet (increase InitializationDelayMs)\n" +
                    "  2. Failed to start (check chatbot path and dependencies)\n" +
                    "  3. Isn't built for Windows (needs Windows shared memory APIs)\n" +
                    "  4. Crashed during startup (run chatbot.exe manually to check)");
            }

            try
            {
                // Open semaphores
                _semReady = Semaphore.OpenExisting(SemReadyName);
                _semPromptsWritten = Semaphore.OpenExisting(SemPromptsWrittenName);
                _semResponseWritten = Semaphore.OpenExisting(SemResponseWrittenName);
                _semChunkReady = Semaphore.OpenExisting(SemChunkReadyName);
            }
            catch (WaitHandleCannotBeOpenedException ex)
            {
                throw new Exception(
                    $"Failed to open semaphores.\n" +
                    "The C++ process created shared memory but not semaphores.\n" +
                    "This indicates the C++ process crashed during initialization.\n" +
                    "Details: {ex.Message}");
            }
        }

        public bool WaitReady(int timeoutMs = -1)
        {
            if (_semReady == null) return false;
            return timeoutMs < 0 ? _semReady.WaitOne() : _semReady.WaitOne(timeoutMs);
        }

        public void SignalPromptsWritten() => _semPromptsWritten?.Release();

        public bool WaitResponseWritten(int timeoutMs = -1)
        {
            if (_semResponseWritten == null) return false;
            return timeoutMs < 0 ? _semResponseWritten.WaitOne() : _semResponseWritten.WaitOne(timeoutMs);
        }

        public bool WaitChunkReady(int timeoutMs = -1)
        {
            if (_semChunkReady == null) return false;
            return timeoutMs < 0 ? _semChunkReady.WaitOne() : _semChunkReady.WaitOne(timeoutMs);
        }

        public void WriteRequest(string systemPrompt, string userPrompt, bool streamMode)
        {
            // Write system prompt
            byte[] systemBytes = new byte[4096];
            if (!string.IsNullOrEmpty(systemPrompt))
            {
                byte[] temp = System.Text.Encoding.UTF8.GetBytes(systemPrompt);
                Array.Copy(temp, systemBytes, Math.Min(temp.Length, 4095));
            }
            _accessor.WriteArray(SystemPromptOffset, systemBytes, 0, 4096);

            // Write user prompt
            byte[] userBytes = new byte[4096];
            if (!string.IsNullOrEmpty(userPrompt))
            {
                byte[] temp = System.Text.Encoding.UTF8.GetBytes(userPrompt);
                Array.Copy(temp, userBytes, Math.Min(temp.Length, 4095));
            }
            _accessor.WriteArray(UserPromptOffset, userBytes, 0, 4096);

            // Clear response buffer
            byte[] clearBytes = new byte[32768];
            _accessor.WriteArray(ResponseOffset, clearBytes, 0, 32768);

            // Write flags
            _accessor.Write(ShutdownRequestedOffset, (byte)0);
            _accessor.Write(StreamModeOffset, streamMode ? (byte)1 : (byte)0);
            _accessor.Write(UpdateCounterOffset, 0);
            _accessor.Write(GenerationCompleteOffset, (byte)0);
            _accessor.Write(TokensGeneratedOffset, 0);
        }

        public void WriteShutdownRequest()
        {
            _accessor.Write(ShutdownRequestedOffset, (byte)1);
        }

        public string ReadResponse()
        {
            byte[] responseBytes = new byte[32768];
            _accessor.ReadArray(ResponseOffset, responseBytes, 0, 32768);

            int length = Array.IndexOf(responseBytes, (byte)0);
            if (length < 0) length = 32768;

            return System.Text.Encoding.UTF8.GetString(responseBytes, 0, length);
        }

        public (string response, int updateCounter, bool isComplete, int tokensGenerated) ReadStreamingState()
        {
            string response = ReadResponse();
            int updateCounter = _accessor.ReadInt32(UpdateCounterOffset);
            bool isComplete = _accessor.ReadByte(GenerationCompleteOffset) != 0;
            int tokensGenerated = _accessor.ReadInt32(TokensGeneratedOffset);

            return (response, updateCounter, isComplete, tokensGenerated);
        }

        public void Dispose()
        {
            _accessor?.Dispose();
            _sharedMemory?.Dispose();
            _semReady?.Dispose();
            _semPromptsWritten?.Dispose();
            _semResponseWritten?.Dispose();
            _semChunkReady?.Dispose();
        }
    }
    #endregion
}

// IMPORTANT: Linux-specific code is in a SEPARATE file to avoid DLL loading issues on Windows
// See: LlamaClient.Linux.cs
namespace LLamaService.Linux
{
    #region Linux POSIX Interop (Only loaded on Linux)
    /// <summary>
    /// POSIX API interop for Linux shared memory and semaphores.
    /// This class is only instantiated on Linux platforms.
    /// </summary>
    internal static class PosixInterop
    {
        public const int O_RDWR = 2;
        public const int PROT_READ = 1;
        public const int PROT_WRITE = 2;
        public const int MAP_SHARED = 1;
        public static readonly IntPtr MAP_FAILED = new IntPtr(-1);

        [DllImport("librt.so.1", SetLastError = true)]
        public static extern int shm_open(string name, int oflag, uint mode);

        [DllImport("libc.so.6", SetLastError = true)]
        public static extern int close(int fd);

        [DllImport("libc.so.6", SetLastError = true)]
        public static extern IntPtr mmap(IntPtr addr, IntPtr length, int prot, int flags, int fd, IntPtr offset);

        [DllImport("libc.so.6", SetLastError = true)]
        public static extern int munmap(IntPtr addr, IntPtr length);

        [DllImport("libpthread.so.0", SetLastError = true)]
        public static extern IntPtr sem_open(string name, int oflag);

        [DllImport("libpthread.so.0", SetLastError = true)]
        public static extern int sem_close(IntPtr sem);

        [DllImport("libpthread.so.0", SetLastError = true)]
        public static extern int sem_wait(IntPtr sem);

        [DllImport("libpthread.so.0", SetLastError = true)]
        public static extern int sem_post(IntPtr sem);
    }

    /// <summary>
    /// Linux implementation using POSIX shared memory and semaphores.
    /// Only instantiated on Linux to avoid DLL loading on Windows.
    /// </summary>
    internal class LinuxSharedMemoryProvider : LLamaService.ISharedMemoryProvider
    {
        private const string SharedMemoryName = "/llama_cpp_shared_mem";
        private const string SemReadyName = "/llama_cpp_sem_ready";
        private const string SemPromptsWrittenName = "/llama_cpp_sem_prompts_written";
        private const string SemResponseWrittenName = "/llama_cpp_sem_response_written";
        private const string SemChunkReadyName = "/llama_cpp_sem_chunk_ready";
        
        // Memory layout offsets (must match C++ SharedMemoryData struct with #pragma pack(1))
        private const int SystemPromptOffset = 0;
        private const int UserPromptOffset = 4096;
        private const int ResponseOffset = 8192;
        private const int ShutdownRequestedOffset = 40960;
        private const int StreamModeOffset = 40961;
        private const int UpdateCounterOffset = 40962;
        private const int GenerationCompleteOffset = 40966;
        private const int TokensGeneratedOffset = 40967;
        private const int SharedMemorySize = 45000;  // Must be >= 40971 (actual struct size)

        private IntPtr _sharedMemoryPtr = IntPtr.Zero;
        private int _shmFd = -1;
        private IntPtr _semReady = IntPtr.Zero;
        private IntPtr _semPromptsWritten = IntPtr.Zero;
        private IntPtr _semResponseWritten = IntPtr.Zero;
        private IntPtr _semChunkReady = IntPtr.Zero;

        public void Connect()
        {
            // Open shared memory
            _shmFd = PosixInterop.shm_open(SharedMemoryName, PosixInterop.O_RDWR, 0666);
            if (_shmFd < 0)
            {
                int errorCode = Marshal.GetLastWin32Error();
                throw new Exception(
                    $"Failed to open shared memory '{SharedMemoryName}'. Error code: {errorCode}\n" +
                    "Make sure the C++ process is running and has created the shared memory.");
            }

            // Map shared memory
            _sharedMemoryPtr = PosixInterop.mmap(
                IntPtr.Zero,
                new IntPtr(SharedMemorySize),
                PosixInterop.PROT_READ | PosixInterop.PROT_WRITE,
                PosixInterop.MAP_SHARED,
                _shmFd,
                IntPtr.Zero
            );

            if (_sharedMemoryPtr == PosixInterop.MAP_FAILED)
            {
                throw new Exception($"Failed to map shared memory. Error code: {Marshal.GetLastWin32Error()}");
            }

            // Open semaphores
            _semReady = PosixInterop.sem_open(SemReadyName, 0);
            _semPromptsWritten = PosixInterop.sem_open(SemPromptsWrittenName, 0);
            _semResponseWritten = PosixInterop.sem_open(SemResponseWrittenName, 0);
            _semChunkReady = PosixInterop.sem_open(SemChunkReadyName, 0);

            if (_semReady == IntPtr.Zero || _semPromptsWritten == IntPtr.Zero ||
                _semResponseWritten == IntPtr.Zero || _semChunkReady == IntPtr.Zero)
            {
                throw new Exception("Failed to open one or more semaphores");
            }
        }

        [DllImport("libpthread.so.0", SetLastError = true)]
        private static extern int sem_timedwait(IntPtr sem, ref timespec abs_timeout);

        private struct timespec
        {
            public long tv_sec;
            public long tv_nsec;
        }

        public bool WaitReady(int timeoutMs = -1)
        {
            if (_semReady == IntPtr.Zero) return false;
            if (timeoutMs < 0)
            {
                return PosixInterop.sem_wait(_semReady) == 0;
            }
            return SemWaitTimeout(_semReady, timeoutMs);
        }

        public void SignalPromptsWritten() => PosixInterop.sem_post(_semPromptsWritten);

        public bool WaitResponseWritten(int timeoutMs = -1)
        {
            if (_semResponseWritten == IntPtr.Zero) return false;
            if (timeoutMs < 0)
            {
                return PosixInterop.sem_wait(_semResponseWritten) == 0;
            }
            return SemWaitTimeout(_semResponseWritten, timeoutMs);
        }

        public bool WaitChunkReady(int timeoutMs = -1)
        {
            if (_semChunkReady == IntPtr.Zero) return false;
            if (timeoutMs < 0)
            {
                return PosixInterop.sem_wait(_semChunkReady) == 0;
            }
            return SemWaitTimeout(_semChunkReady, timeoutMs);
        }

        private bool SemWaitTimeout(IntPtr sem, int timeoutMs)
        {
            var now = DateTimeOffset.UtcNow;
            var timeout = now.AddMilliseconds(timeoutMs);
            var ts = new timespec
            {
                tv_sec = timeout.ToUnixTimeSeconds(),
                tv_nsec = (timeout.Millisecond * 1000000)
            };
            return sem_timedwait(sem, ref ts) == 0;
        }

        public void WriteRequest(string systemPrompt, string userPrompt, bool streamMode)
        {
            // Write system prompt
            byte[] systemBytes = new byte[4096];
            if (!string.IsNullOrEmpty(systemPrompt))
            {
                byte[] temp = System.Text.Encoding.UTF8.GetBytes(systemPrompt);
                Array.Copy(temp, systemBytes, Math.Min(temp.Length, 4095));
            }
            Marshal.Copy(systemBytes, 0, _sharedMemoryPtr + SystemPromptOffset, 4096);

            // Write user prompt
            byte[] userBytes = new byte[4096];
            if (!string.IsNullOrEmpty(userPrompt))
            {
                byte[] temp = System.Text.Encoding.UTF8.GetBytes(userPrompt);
                Array.Copy(temp, userBytes, Math.Min(temp.Length, 4095));
            }
            Marshal.Copy(userBytes, 0, _sharedMemoryPtr + UserPromptOffset, 4096);

            // Clear response buffer
            byte[] clearBytes = new byte[32768];
            Marshal.Copy(clearBytes, 0, _sharedMemoryPtr + ResponseOffset, 32768);

            // Write flags
            Marshal.WriteByte(_sharedMemoryPtr + ShutdownRequestedOffset, 0);
            Marshal.WriteByte(_sharedMemoryPtr + StreamModeOffset, streamMode ? (byte)1 : (byte)0);
            Marshal.WriteInt32(_sharedMemoryPtr + UpdateCounterOffset, 0);
            Marshal.WriteByte(_sharedMemoryPtr + GenerationCompleteOffset, 0);
            Marshal.WriteInt32(_sharedMemoryPtr + TokensGeneratedOffset, 0);
        }

        public void WriteShutdownRequest()
        {
            Marshal.WriteByte(_sharedMemoryPtr + ShutdownRequestedOffset, 1);
        }

        public string ReadResponse()
        {
            byte[] responseBytes = new byte[32768];
            Marshal.Copy(_sharedMemoryPtr + ResponseOffset, responseBytes, 0, 32768);

            int length = Array.IndexOf(responseBytes, (byte)0);
            if (length < 0) length = 32768;

            return System.Text.Encoding.UTF8.GetString(responseBytes, 0, length);
        }

        public (string response, int updateCounter, bool isComplete, int tokensGenerated) ReadStreamingState()
        {
            string response = ReadResponse();
            int updateCounter = Marshal.ReadInt32(_sharedMemoryPtr + UpdateCounterOffset);
            bool isComplete = Marshal.ReadByte(_sharedMemoryPtr + GenerationCompleteOffset) != 0;
            int tokensGenerated = Marshal.ReadInt32(_sharedMemoryPtr + TokensGeneratedOffset);

            return (response, updateCounter, isComplete, tokensGenerated);
        }

        public void Dispose()
        {
            if (_sharedMemoryPtr != IntPtr.Zero && _sharedMemoryPtr != PosixInterop.MAP_FAILED)
            {
                PosixInterop.munmap(_sharedMemoryPtr, new IntPtr(SharedMemorySize));
                _sharedMemoryPtr = IntPtr.Zero;
            }

            if (_shmFd >= 0)
            {
                PosixInterop.close(_shmFd);
                _shmFd = -1;
            }

            if (_semReady != IntPtr.Zero) PosixInterop.sem_close(_semReady);
            if (_semPromptsWritten != IntPtr.Zero) PosixInterop.sem_close(_semPromptsWritten);
            if (_semResponseWritten != IntPtr.Zero) PosixInterop.sem_close(_semResponseWritten);
            if (_semChunkReady != IntPtr.Zero) PosixInterop.sem_close(_semChunkReady);

            _semReady = _semPromptsWritten = _semResponseWritten = _semChunkReady = IntPtr.Zero;
        }
    }
    #endregion
}

namespace LLamaService
{
    #region Main Client Class
    /// <summary>
    /// Modern, easy-to-use client for integrating llama.cpp with C# applications.
    /// Supports both streaming and non-streaming modes on Windows and Linux.
    /// </summary>
    /// <example>
    /// Basic usage:
    /// <code>
    /// using var client = new LlamaClient();
    /// await client.InitializeAsync();
    /// 
    /// // Non-streaming mode
    /// string response = await client.GenerateAsync("What is C++?");
    /// 
    /// // Streaming mode
    /// await client.GenerateStreamingAsync("Explain pointers", 
    ///     onUpdate: (text, tokens, isComplete) => Console.Write("."));
    /// </code>
    /// </example>
    public class LlamaClient : IDisposable
    {
        #region Private Fields
        private ISharedMemoryProvider _sharedMemory;
        private Process _cppProcess;
        private readonly LlamaClientConfig _config;
        private bool _isInitialized = false;
        private bool _isDisposed = false;
        private CancellationTokenSource _healthCheckCancellation;
        private Task _healthCheckTask;
        #endregion

        #region Events
        /// <summary>
        /// Fired during streaming mode for each token update.
        /// Subscribe to this event to receive real-time updates.
        /// </summary>
        public event EventHandler<StreamUpdateEventArgs> OnStreamUpdate;
        #endregion

        #region Constructor
        /// <summary>
        /// Creates a new LlamaClient instance
        /// </summary>
        /// <param name="config">Optional configuration. Uses defaults if null.</param>
        public LlamaClient(LlamaClientConfig config = null)
        {
            _config = config ?? new LlamaClientConfig();
        }
        #endregion

        #region Initialization
        /// <summary>
        /// Initialize the client and start the C++ chatbot process.
        /// Must be called before using Generate methods.
        /// </summary>
        public void Initialize()
        {
            if (_isInitialized)
            {
                throw new InvalidOperationException("Client is already initialized");
            }

            try
            {
                StartCppProcess();
                
                // Wait a bit for C++ process to create shared memory objects
                Thread.Sleep(_config.InitializationDelayMs);
                
                ConnectToSharedMemory();
                
                // Wait for C++ to finish loading the model and signal ready
                LogDebug("Waiting for C++ process to load model and signal ready...");
                bool ready = _sharedMemory.WaitReady(_config.SemaphoreTimeoutMs);
                if (!ready)
                {
                    throw new TimeoutException(
                        $"Timed out waiting for C++ process to be ready after {_config.SemaphoreTimeoutMs}ms. " +
                        "Model might be too large or system is under heavy load.");
                }
                LogDebug("C++ process is ready (model loaded)");
                
                _isInitialized = true;

                // Start health monitoring if enabled
                if (_config.EnableProcessMonitoring)
                {
                    StartProcessMonitoring();
                }

                LogDebug("LlamaClient initialized successfully");
            }
            catch (Exception ex)
            {
                Cleanup();
                throw new Exception($"Failed to initialize LlamaClient: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Initialize the client asynchronously
        /// </summary>
        /// <param name="cancellationToken">Optional cancellation token</param>
        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            await Task.Run(() => Initialize(), cancellationToken);
        }

        private void StartCppProcess()
        {
            // Check if file exists
            if (!System.IO.File.Exists(_config.ChatbotPath))
            {
                throw new Exception(
                    $"Chatbot executable not found: {_config.ChatbotPath}\n" +
                    $"Make sure the path is correct and the file exists.");
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = _config.ChatbotPath,
                UseShellExecute = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                CreateNoWindow = !_config.EnableDebugOutput
            };

            LogDebug($"Starting C++ process: {_config.ChatbotPath}");

            try
            {
                _cppProcess = Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                throw new Exception(
                    $"Failed to start C++ chatbot process.\n" +
                    $"Path: {_config.ChatbotPath}\n" +
                    $"Error: {ex.Message}", ex);
            }

            if (_cppProcess == null)
            {
                throw new Exception("Failed to start C++ chatbot process - Process.Start returned null");
            }

            LogDebug($"C++ process started (PID: {_cppProcess.Id})");

            // Set up output handlers (do this BEFORE BeginOutputReadLine)
            //try
            //{
            //    _cppProcess.OutputDataReceived += (s, e) =>
            //    {
            //        if (!string.IsNullOrEmpty(e.Data))
            //        {
            //            if (_config.EnableDebugOutput)
            //                Console.WriteLine($"[C++] {e.Data}");
            //        }
            //    };
            //    _cppProcess.ErrorDataReceived += (s, e) =>
            //    {
            //        if (!string.IsNullOrEmpty(e.Data))
            //        {
            //            if (_config.EnableDebugOutput)
            //                Console.WriteLine($"[C++ Error] {e.Data}");
            //        }
            //    };

            //    _cppProcess.BeginOutputReadLine();
            //    _cppProcess.BeginErrorReadLine();
            //}
            //catch (InvalidOperationException ex)
            //{
            //    throw new Exception(
            //        $"Failed to start output redirection for C++ process.\n" +
            //        $"This usually means the process failed to start.\n" +
            //        $"Path: {_config.ChatbotPath}\n" +
            //        $"Check if the file exists and has proper permissions.\n" +
            //        $"Details: {ex.Message}", ex);
            //}


            // Check if process crashed immediately
            Thread.Sleep(500);
            if (_cppProcess.HasExited)
            {
                throw new Exception(
                    $"C++ chatbot process exited immediately with code {_cppProcess.ExitCode}.\n" +
                    "This usually means:\n" +
                    "  1. Missing DLL dependencies (run 'dumpbin /dependents chatbot.exe' to check)\n" +
                    "  2. Model file not found\n" +
                    "  3. Incompatible executable (wrong architecture)\n" +
                    "Try running the chatbot.exe manually to see the error message.");
            }
        }

        private void ConnectToSharedMemory()
        {
            // Detect platform and create appropriate provider
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                LogDebug("Detected Windows platform - using MemoryMappedFile");
                _sharedMemory = new WindowsSharedMemoryProvider();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                LogDebug("Detected Linux platform - using POSIX shared memory");
                // Use reflection to create Linux provider to avoid loading on Windows
                var linuxProviderType = Type.GetType("LLamaService.Linux.LinuxSharedMemoryProvider, " +
                    System.Reflection.Assembly.GetExecutingAssembly().FullName);

                if (linuxProviderType == null)
                {
                    throw new Exception("Failed to load Linux shared memory provider");
                }

                _sharedMemory = (ISharedMemoryProvider)Activator.CreateInstance(linuxProviderType);
            }
            else
            {
                throw new PlatformNotSupportedException(
                    "LlamaClient currently supports Windows and Linux only. " +
                    $"Current platform: {RuntimeInformation.OSDescription}");
            }

            _sharedMemory.Connect();
        }
        #endregion

        #region Public API - Non-Streaming Mode
        /// <summary>
        /// Generate a response in non-streaming mode (returns complete response at once)
        /// </summary>
        /// <param name="userPrompt">The user's prompt/question</param>
        /// <param name="systemPrompt">Optional system prompt (uses default if null)</param>
        /// <returns>The complete generated response</returns>
        /// <exception cref="InvalidOperationException">If client is not initialized</exception>
        public string Generate(string userPrompt, string systemPrompt = null)
        {
            EnsureInitialized();

            systemPrompt = systemPrompt ?? _config.DefaultSystemPrompt;

            LogDebug($"Generating response (non-streaming): {userPrompt.Substring(0, Math.Min(50, userPrompt.Length))}...");

            // Wait for C++ to be ready
            if (!_sharedMemory.WaitReady(_config.SemaphoreTimeoutMs))
            {
                throw new TimeoutException("Timed out waiting for C++ process to be ready");
            }

            // Write request
            _sharedMemory.WriteRequest(systemPrompt, userPrompt, streamMode: false);

            // Signal C++
            _sharedMemory.SignalPromptsWritten();

            // Wait for complete response
            if (!_sharedMemory.WaitResponseWritten(_config.SemaphoreTimeoutMs))
            {
                throw new TimeoutException("Timed out waiting for response from C++ process");
            }

            // Read and return response
            string response = _sharedMemory.ReadResponse();
            LogDebug($"Response received ({response.Length} chars)");

            return response;
        }

        /// <summary>
        /// Generate a response asynchronously in non-streaming mode
        /// </summary>
        /// <param name="userPrompt">The user's prompt/question</param>
        /// <param name="systemPrompt">Optional system prompt (uses default if null)</param>
        /// <param name="cancellationToken">Optional cancellation token</param>
        /// <returns>The complete generated response</returns>
        public async Task<string> GenerateAsync(string userPrompt, string systemPrompt = null, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() => Generate(userPrompt, systemPrompt), cancellationToken);
        }
        #endregion

        #region Public API - Streaming Mode
        /// <summary>
        /// Generate a response in streaming mode with event-based updates.
        /// Subscribe to OnStreamUpdate event to receive real-time updates.
        /// </summary>
        /// <param name="userPrompt">The user's prompt/question</param>
        /// <param name="systemPrompt">Optional system prompt (uses default if null)</param>
        /// <returns>The complete final response</returns>
        public string GenerateStreaming(string userPrompt, string systemPrompt = null)
        {
            EnsureInitialized();

            systemPrompt = systemPrompt ?? _config.DefaultSystemPrompt;

            LogDebug($"Generating response (streaming): {userPrompt.Substring(0, Math.Min(50, userPrompt.Length))}...");

            // Wait for C++ to be ready
            if (!_sharedMemory.WaitReady(_config.SemaphoreTimeoutMs))
            {
                throw new TimeoutException("Timed out waiting for C++ process to be ready");
            }

            // Write request with streaming enabled
            _sharedMemory.WriteRequest(systemPrompt, userPrompt, streamMode: true);

            // Signal C++
            _sharedMemory.SignalPromptsWritten();

            // Process streaming updates
            string finalResponse = "";
            int lastUpdateCounter = 0;

            while (true)
            {
                // Wait for chunk signal
                if (!_sharedMemory.WaitChunkReady(_config.SemaphoreTimeoutMs))
                {
                    throw new TimeoutException("Timed out waiting for streaming chunk from C++ process");
                }

                // Read current state
                var (response, updateCounter, isComplete, tokensGenerated) = _sharedMemory.ReadStreamingState();

                // Check if this is a new update
                if (updateCounter > lastUpdateCounter)
                {
                    finalResponse = response;
                    lastUpdateCounter = updateCounter;

                    // Fire event for UI update
                    OnStreamUpdate?.Invoke(this, new StreamUpdateEventArgs
                    {
                        Text = response,
                        TokensGenerated = tokensGenerated,
                        IsComplete = isComplete
                    });

                    if (isComplete)
                    {
                        LogDebug($"Streaming complete ({tokensGenerated} tokens)");
                        break;
                    }
                }
            }

            // Note: In streaming mode, we already received all data via chunk_ready signals.
            // The response_written signal is used by C++ to return to the ready state,
            // but we don't need to wait for it here since generation is complete.
            // Wait for C++ to return to ready state (non-blocking with timeout)
            _sharedMemory.WaitResponseWritten(_config.SemaphoreTimeoutMs);

            return finalResponse;
        }

        /// <summary>
        /// Generate a response asynchronously in streaming mode with event-based updates
        /// </summary>
        /// <param name="userPrompt">The user's prompt/question</param>
        /// <param name="systemPrompt">Optional system prompt (uses default if null)</param>
        /// <param name="cancellationToken">Optional cancellation token</param>
        /// <returns>The complete final response</returns>
        public async Task<string> GenerateStreamingAsync(string userPrompt, string systemPrompt = null, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() => GenerateStreaming(userPrompt, systemPrompt), cancellationToken);
        }

        /// <summary>
        /// Generate a response in streaming mode with a callback function for updates.
        /// This is an alternative to using the OnStreamUpdate event.
        /// </summary>
        /// <param name="userPrompt">The user's prompt/question</param>
        /// <param name="onUpdate">Callback function(text, tokensGenerated, isComplete)</param>
        /// <param name="systemPrompt">Optional system prompt (uses default if null)</param>
        /// <returns>The complete final response</returns>
        public string GenerateStreaming(
            string userPrompt,
            Action<string, int, bool> onUpdate,
            string systemPrompt = null)
        {
            // Subscribe to event temporarily
            EventHandler<StreamUpdateEventArgs> handler = (s, e) =>
            {
                onUpdate?.Invoke(e.Text, e.TokensGenerated, e.IsComplete);
            };

            OnStreamUpdate += handler;

            try
            {
                return GenerateStreaming(userPrompt, systemPrompt);
            }
            finally
            {
                OnStreamUpdate -= handler;
            }
        }

        /// <summary>
        /// Generate a response asynchronously in streaming mode with a callback function
        /// </summary>
        /// <param name="userPrompt">The user's prompt/question</param>
        /// <param name="onUpdate">Callback function(text, tokensGenerated, isComplete)</param>
        /// <param name="systemPrompt">Optional system prompt (uses default if null)</param>
        /// <param name="cancellationToken">Optional cancellation token</param>
        /// <returns>The complete final response</returns>
        public async Task<string> GenerateStreamingAsync(
            string userPrompt,
            Action<string, int, bool> onUpdate,
            string systemPrompt = null,
            CancellationToken cancellationToken = default)
        {
            return await Task.Run(() => GenerateStreaming(userPrompt, onUpdate, systemPrompt), cancellationToken);
        }
        #endregion

        #region Public API - UI Integration Helpers
        /// <summary>
        /// Generate a response with automatic TextBox updates (WinForms).
        /// This is a convenience method that handles UI thread marshalling automatically.
        /// </summary>
        /// <param name="textBox">WinForms TextBox to update</param>
        /// <param name="userPrompt">The user's prompt/question</param>
        /// <param name="systemPrompt">Optional system prompt (uses default if null)</param>
        /// <param name="cancellationToken">Optional cancellation token</param>
        /// <returns>The complete final response</returns>
        public async Task<string> GenerateToTextBoxAsync(
            System.Windows.Forms.TextBox textBox,
            string userPrompt,
            string systemPrompt = null,
            CancellationToken cancellationToken = default)
        {
            if (textBox == null)
                throw new ArgumentNullException(nameof(textBox));

            EventHandler<StreamUpdateEventArgs> handler = (s, e) =>
            {
                if (textBox.InvokeRequired)
                {
                    textBox.Invoke(new Action(() => textBox.Text = e.Text));
                }
                else
                {
                    textBox.Text = e.Text;
                }
            };

            OnStreamUpdate += handler;

            try
            {
                return await GenerateStreamingAsync(userPrompt, systemPrompt, cancellationToken);
            }
            finally
            {
                OnStreamUpdate -= handler;
            }
        }

        /// <summary>
        /// Generate a response with automatic TextBox updates (WPF).
        /// This is a convenience method that handles UI thread marshalling automatically.
        /// </summary>
        /// <param name="textBox">WPF TextBox to update</param>
        /// <param name="userPrompt">The user's prompt/question</param>
        /// <param name="systemPrompt">Optional system prompt (uses default if null)</param>
        /// <returns>The complete final response</returns>
        //public async Task<string> GenerateToTextBoxWpfAsync(
        //    System.Windows.Controls.TextBox textBox,
        //    string userPrompt,
        //    string systemPrompt = null)
        //{
        //    if (textBox == null)
        //        throw new ArgumentNullException(nameof(textBox));

        //    EventHandler<StreamUpdateEventArgs> handler = (s, e) =>
        //    {
        //        textBox.Dispatcher.Invoke(() => textBox.Text = e.Text);
        //    };

        //    OnStreamUpdate += handler;

        //    try
        //    {
        //        return await GenerateStreamingAsync(userPrompt, systemPrompt);
        //    }
        //    finally
        //    {
        //        OnStreamUpdate -= handler;
        //    }
        //}

        /// <summary>
        /// Generate a response with automatic updates to any Action&lt;string&gt; handler.
        /// Useful for updating any UI control or custom handling.
        /// </summary>
        /// <param name="updateAction">Action to call with updated text</param>
        /// <param name="userPrompt">The user's prompt/question</param>
        /// <param name="systemPrompt">Optional system prompt (uses default if null)</param>
        /// <param name="cancellationToken">Optional cancellation token</param>
        /// <returns>The complete final response</returns>
        public async Task<string> GenerateWithUpdatesAsync(
            Action<string> updateAction,
            string userPrompt,
            string systemPrompt = null,
            CancellationToken cancellationToken = default)
        {
            return await GenerateStreamingAsync(userPrompt,
                (text, tokens, isComplete) => updateAction?.Invoke(text),
                systemPrompt,
                cancellationToken);
        }
        #endregion

        #region Helper Methods
        private void EnsureInitialized()
        {
            if (!_isInitialized)
            {
                throw new InvalidOperationException(
                    "LlamaClient is not initialized. Call Initialize() or InitializeAsync() first.");
            }
        }

        private void LogDebug(string message)
        {
            if (_config.EnableDebugOutput)
            {
                Console.WriteLine($"[LlamaClient] {message}");
            }
        }

        private void StartProcessMonitoring()
        {
            _healthCheckCancellation = new CancellationTokenSource();
            _healthCheckTask = Task.Run(async () =>
            {
                while (!_healthCheckCancellation.Token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(5000, _healthCheckCancellation.Token);

                        if (_cppProcess != null && _cppProcess.HasExited)
                        {
                            LogDebug($"WARNING: C++ process has exited unexpectedly with code {_cppProcess.ExitCode}");
                            _isInitialized = false;
                            break;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }, _healthCheckCancellation.Token);
        }

        /// <summary>
        /// Check if the C++ backend process is still running
        /// </summary>
        /// <returns>True if the process is alive and responsive</returns>
        public bool IsProcessAlive()
        {
            return _cppProcess != null && !_cppProcess.HasExited;
        }

        /// <summary>
        /// Get the exit code of the C++ process (only valid if process has exited)
        /// </summary>
        public int? GetProcessExitCode()
        {
            if (_cppProcess != null && _cppProcess.HasExited)
            {
                return _cppProcess.ExitCode;
            }
            return null;
        }
        #endregion

        #region Shutdown & Cleanup
        /// <summary>
        /// Request graceful shutdown of the C++ backend.
        /// This allows the backend to clean up resources properly.
        /// </summary>
        /// <param name="timeoutMs">Maximum time to wait for shutdown (default: 5000ms)</param>
        public void Shutdown(int timeoutMs = 5000)
        {
            if (!_isInitialized || _sharedMemory == null)
            {
                return;
            }

            try
            {
                LogDebug("Requesting graceful shutdown of C++ backend...");
                
                // Signal shutdown via shared memory
                _sharedMemory.WriteShutdownRequest();
                
                // Wake up the C++ process if it's waiting
                _sharedMemory.SignalPromptsWritten();

                // Wait for process to exit gracefully
                if (_cppProcess != null && !_cppProcess.HasExited)
                {
                    if (!_cppProcess.WaitForExit(timeoutMs))
                    {
                        LogDebug("Process did not exit gracefully, forcing termination...");
                        _cppProcess.Kill();
                    }
                    else
                    {
                        LogDebug("C++ backend shut down gracefully");
                    }
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Error during shutdown: {ex.Message}");
            }
            finally
            {
                _isInitialized = false;
            }
        }

        /// <summary>
        /// Async version of Shutdown
        /// </summary>
        /// <param name="timeoutMs">Maximum time to wait for shutdown (default: 5000ms)</param>
        /// <param name="cancellationToken">Optional cancellation token</param>
        public async Task ShutdownAsync(int timeoutMs = 5000, CancellationToken cancellationToken = default)
        {
            await Task.Run(() => Shutdown(timeoutMs), cancellationToken);
        }

        private void Cleanup()
        {
            // Stop health monitoring
            _healthCheckCancellation?.Cancel();
            try
            {
                _healthCheckTask?.Wait(1000);
            }
            catch { }
            _healthCheckCancellation?.Dispose();

            _sharedMemory?.Dispose();
            _sharedMemory = null;

            if (_cppProcess != null && !_cppProcess.HasExited)
            {
                try
                {
                    _cppProcess.Kill();
                }
                catch { }
            }

            _cppProcess?.Dispose();
            _cppProcess = null;
        }

        /// <summary>
        /// Dispose of the client and cleanup all resources
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed) return;

            Cleanup();
            GC.SuppressFinalize(this);
            _isDisposed = true;
        }

        ~LlamaClient()
        {
            Dispose();
        }
        #endregion
    }
    #endregion
}
