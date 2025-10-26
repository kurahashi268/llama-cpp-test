using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Windows.Forms; // For TextBox - or use System.Windows.Controls for WPF

namespace LlamaCpp.Service
{
    #region POSIX Interop
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
    #endregion

    #region Event Args
    /// <summary>
    /// Event arguments for streaming text updates
    /// </summary>
    public class StreamTextEventArgs : EventArgs
    {
        public string Text { get; set; }
        public int TokensGenerated { get; set; }
        public bool IsComplete { get; set; }
    }
    #endregion

    #region Service Configuration
    /// <summary>
    /// Configuration for LocalLLMService
    /// </summary>
    public class LLMServiceConfig
    {
        public string ChatbotPath { get; set; } = "./build/chatbot";
        public string DefaultSystemPrompt { get; set; } = "You are my best assistance.";
        public int InitializationDelayMs { get; set; } = 3000;
    }
    #endregion

    #region Main Service Class
    /// <summary>
    /// Local LLM Service - Easy interface for C++ LLM communication
    /// </summary>
    public class LocalLLMService : IDisposable
    {
        #region Constants
        private const string SharedMemoryName = "/llama_cpp_shared_mem";
        private const string SemReadyName = "/llama_cpp_sem_ready";
        private const string SemPromptsWrittenName = "/llama_cpp_sem_prompts_written";
        private const string SemResponseWrittenName = "/llama_cpp_sem_response_written";
        private const string SemChunkReadyName = "/llama_cpp_sem_chunk_ready";
        private const int SharedMemorySize = 45000;
        #endregion

        #region Private Fields
        private IntPtr _sharedMemoryPtr = IntPtr.Zero;
        private int _shmFd = -1;
        private IntPtr _semReady = IntPtr.Zero;
        private IntPtr _semPromptsWritten = IntPtr.Zero;
        private IntPtr _semResponseWritten = IntPtr.Zero;
        private IntPtr _semChunkReady = IntPtr.Zero;
        private Process _cppProcess;
        private bool _isInitialized = false;
        private bool _isDisposed = false;
        private readonly LLMServiceConfig _config;
        #endregion

        #region Events
        /// <summary>
        /// Fired during streaming mode for each update
        /// </summary>
        public event EventHandler<StreamTextEventArgs> OnStreamUpdate;
        #endregion

        #region Constructor
        public LocalLLMService(LLMServiceConfig config = null)
        {
            _config = config ?? new LLMServiceConfig();
        }
        #endregion

        #region Initialization
        /// <summary>
        /// Initialize the service and start C++ process
        /// </summary>
        public void Initialize()
        {
            if (_isInitialized)
            {
                throw new InvalidOperationException("Service already initialized");
            }

            try
            {
                // Start C++ process
                var startInfo = new ProcessStartInfo
                {
                    FileName = _config.ChatbotPath,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                _cppProcess = Process.Start(startInfo);
                if (_cppProcess == null)
                {
                    throw new Exception("Failed to start C++ chatbot process");
                }

                // Optional: Capture output for debugging
                _cppProcess.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        Debug.WriteLine($"[LLM C++] {e.Data}");
                };
                _cppProcess.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        Debug.WriteLine($"[LLM Error] {e.Data}");
                };
                _cppProcess.BeginOutputReadLine();
                _cppProcess.BeginErrorReadLine();

                // Wait for C++ to initialize
                Thread.Sleep(_config.InitializationDelayMs);

                // Connect to shared memory
                ConnectToSharedMemory();

                _isInitialized = true;
            }
            catch (Exception ex)
            {
                Cleanup();
                throw new Exception($"Failed to initialize LocalLLMService: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Initialize asynchronously
        /// </summary>
        public async Task InitializeAsync()
        {
            await Task.Run(() => Initialize());
        }

        private void ConnectToSharedMemory()
        {
            // Open shared memory
            _shmFd = PosixInterop.shm_open(SharedMemoryName, PosixInterop.O_RDWR, 0666);
            if (_shmFd < 0)
            {
                throw new Exception($"Failed to open shared memory: {Marshal.GetLastWin32Error()}");
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
                throw new Exception($"Failed to map shared memory: {Marshal.GetLastWin32Error()}");
            }

            // Open semaphores
            _semReady = PosixInterop.sem_open(SemReadyName, 0);
            _semPromptsWritten = PosixInterop.sem_open(SemPromptsWrittenName, 0);
            _semResponseWritten = PosixInterop.sem_open(SemResponseWrittenName, 0);
            _semChunkReady = PosixInterop.sem_open(SemChunkReadyName, 0);

            if (_semReady == IntPtr.Zero || _semPromptsWritten == IntPtr.Zero ||
                _semResponseWritten == IntPtr.Zero || _semChunkReady == IntPtr.Zero)
            {
                throw new Exception("Failed to open semaphores");
            }
        }
        #endregion

        #region Public API - Non-Streaming Mode
        /// <summary>
        /// Get response in normal mode (full response at once)
        /// </summary>
        /// <param name="userPrompt">User's question or input</param>
        /// <param name="systemPrompt">Optional system prompt (uses default if null)</param>
        /// <returns>Complete response text</returns>
        public string GetResponse(string userPrompt, string systemPrompt = null)
        {
            if (!_isInitialized)
                throw new InvalidOperationException("Service not initialized. Call Initialize() first.");

            systemPrompt = systemPrompt ?? _config.DefaultSystemPrompt;

            // Wait for C++ to be ready
            PosixInterop.sem_wait(_semReady);

            // Write request
            WriteRequest(systemPrompt, userPrompt, streamMode: false);

            // Signal C++
            PosixInterop.sem_post(_semPromptsWritten);

            // Wait for complete response
            PosixInterop.sem_wait(_semResponseWritten);

            // Read and return response
            return ReadResponse();
        }

        /// <summary>
        /// Get response asynchronously in normal mode
        /// </summary>
        public async Task<string> GetResponseAsync(string userPrompt, string systemPrompt = null)
        {
            return await Task.Run(() => GetResponse(userPrompt, systemPrompt));
        }
        #endregion

        #region Public API - Streaming Mode
        /// <summary>
        /// Get response in streaming mode (real-time updates)
        /// Subscribe to OnStreamUpdate event to receive updates
        /// </summary>
        /// <param name="userPrompt">User's question or input</param>
        /// <param name="systemPrompt">Optional system prompt (uses default if null)</param>
        /// <returns>Complete final response text</returns>
        public string GetResponseStreaming(string userPrompt, string systemPrompt = null)
        {
            if (!_isInitialized)
                throw new InvalidOperationException("Service not initialized. Call Initialize() first.");

            systemPrompt = systemPrompt ?? _config.DefaultSystemPrompt;

            // Wait for C++ to be ready
            PosixInterop.sem_wait(_semReady);

            // Write request with streaming enabled
            WriteRequest(systemPrompt, userPrompt, streamMode: true);

            // Signal C++
            PosixInterop.sem_post(_semPromptsWritten);

            // Process streaming updates
            string finalResponse = "";
            int lastUpdateCounter = 0;

            while (true)
            {
                // Wait for chunk signal
                PosixInterop.sem_wait(_semChunkReady);

                // Read current state
                var (response, updateCounter, isComplete, tokensGenerated) = ReadStreamingState();

                // Check if this is a new update
                if (updateCounter > lastUpdateCounter)
                {
                    finalResponse = response;
                    lastUpdateCounter = updateCounter;

                    // Fire event for UI update
                    OnStreamUpdate?.Invoke(this, new StreamTextEventArgs
                    {
                        Text = response,
                        TokensGenerated = tokensGenerated,
                        IsComplete = isComplete
                    });

                    if (isComplete)
                        break;
                }
            }

            // Wait for final completion signal
            PosixInterop.sem_wait(_semResponseWritten);

            return finalResponse;
        }

        /// <summary>
        /// Get response asynchronously in streaming mode
        /// </summary>
        public async Task<string> GetResponseStreamingAsync(string userPrompt, string systemPrompt = null)
        {
            return await Task.Run(() => GetResponseStreaming(userPrompt, systemPrompt));
        }
        #endregion

        #region Public API - Streaming to TextBox
        /// <summary>
        /// Get response with automatic TextBox updates (WinForms)
        /// </summary>
        /// <param name="textBox">TextBox control to update</param>
        /// <param name="userPrompt">User's question</param>
        /// <param name="systemPrompt">Optional system prompt</param>
        /// <returns>Complete response</returns>
        public async Task<string> GetResponseToTextBox(
            TextBox textBox,
            string userPrompt,
            string systemPrompt = null)
        {
            if (textBox == null)
                throw new ArgumentNullException(nameof(textBox));

            // Subscribe to streaming updates
            EventHandler<StreamTextEventArgs> handler = (s, e) =>
            {
                // Update TextBox on UI thread
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
                string result = await GetResponseStreamingAsync(userPrompt, systemPrompt);
                return result;
            }
            finally
            {
                OnStreamUpdate -= handler;
            }
        }

        /// <summary>
        /// Get response with automatic TextBox updates (WPF)
        /// </summary>
        /// <param name="textBox">WPF TextBox control</param>
        /// <param name="userPrompt">User's question</param>
        /// <param name="systemPrompt">Optional system prompt</param>
        /// <returns>Complete response</returns>
        public async Task<string> GetResponseToTextBoxWPF(
            System.Windows.Controls.TextBox textBox,
            string userPrompt,
            string systemPrompt = null)
        {
            if (textBox == null)
                throw new ArgumentNullException(nameof(textBox));

            // Subscribe to streaming updates
            EventHandler<StreamTextEventArgs> handler = (s, e) =>
            {
                // Update TextBox on UI thread
                textBox.Dispatcher.Invoke(() => textBox.Text = e.Text);
            };

            OnStreamUpdate += handler;

            try
            {
                string result = await GetResponseStreamingAsync(userPrompt, systemPrompt);
                return result;
            }
            finally
            {
                OnStreamUpdate -= handler;
            }
        }
        #endregion

        #region Helper Methods
        private void WriteRequest(string systemPrompt, string userPrompt, bool streamMode)
        {
            // Write system prompt at offset 0
            byte[] systemBytes = new byte[4096];
            if (!string.IsNullOrEmpty(systemPrompt))
            {
                byte[] temp = System.Text.Encoding.UTF8.GetBytes(systemPrompt);
                Array.Copy(temp, systemBytes, Math.Min(temp.Length, 4095));
            }
            Marshal.Copy(systemBytes, 0, _sharedMemoryPtr, 4096);

            // Write user prompt at offset 4096
            byte[] userBytes = new byte[4096];
            if (!string.IsNullOrEmpty(userPrompt))
            {
                byte[] temp = System.Text.Encoding.UTF8.GetBytes(userPrompt);
                Array.Copy(temp, userBytes, Math.Min(temp.Length, 4095));
            }
            Marshal.Copy(userBytes, 0, _sharedMemoryPtr + 4096, 4096);

            // Clear response buffer
            byte[] clearBytes = new byte[32768];
            Marshal.Copy(clearBytes, 0, _sharedMemoryPtr + 8192, 32768);

            // Write flags
            Marshal.WriteByte(_sharedMemoryPtr + 40960, 0); // shutdown_requested = false
            Marshal.WriteByte(_sharedMemoryPtr + 40961, streamMode ? (byte)1 : (byte)0); // stream_mode
            Marshal.WriteInt32(_sharedMemoryPtr + 40962, 0); // update_counter = 0
            Marshal.WriteByte(_sharedMemoryPtr + 40966, 0); // generation_complete = false
            Marshal.WriteInt32(_sharedMemoryPtr + 40967, 0); // tokens_generated = 0
        }

        private string ReadResponse()
        {
            byte[] responseBytes = new byte[32768];
            Marshal.Copy(_sharedMemoryPtr + 8192, responseBytes, 0, 32768);

            int length = Array.IndexOf(responseBytes, (byte)0);
            if (length < 0) length = 32768;

            return System.Text.Encoding.UTF8.GetString(responseBytes, 0, length);
        }

        private (string response, int updateCounter, bool isComplete, int tokensGenerated) ReadStreamingState()
        {
            string response = ReadResponse();
            int updateCounter = Marshal.ReadInt32(_sharedMemoryPtr + 40962);
            bool isComplete = Marshal.ReadByte(_sharedMemoryPtr + 40966) != 0;
            int tokensGenerated = Marshal.ReadInt32(_sharedMemoryPtr + 40967);

            return (response, updateCounter, isComplete, tokensGenerated);
        }
        #endregion

        #region Cleanup & Dispose
        private void Cleanup()
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

        public void Dispose()
        {
            if (_isDisposed) return;

            Cleanup();
            _isDisposed = true;
            GC.SuppressFinalize(this);
        }

        ~LocalLLMService()
        {
            Dispose();
        }
        #endregion
    }
    #endregion
}

