using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Diagnostics;

namespace LlamaCppIntegration.Streaming
{
    /// <summary>
    /// Updated shared memory structure with streaming support
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct SharedMemoryData
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 4096)]
        public string SystemPrompt;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 4096)]
        public string UserPrompt;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32768)]
        public string Response;

        [MarshalAs(UnmanagedType.I1)]
        public bool ShutdownRequested;

        // Streaming support
        [MarshalAs(UnmanagedType.I1)]
        public bool StreamMode;

        public int UpdateCounter;

        [MarshalAs(UnmanagedType.I1)]
        public bool GenerationComplete;

        public int TokensGenerated;
    }

    /// <summary>
    /// POSIX API interop for Linux
    /// </summary>
    public static class PosixInterop
    {
        // Constants
        public const int O_RDWR = 2;
        public static readonly IntPtr MAP_FAILED = new IntPtr(-1);

        // shm_open
        [DllImport("librt.so.1", SetLastError = true)]
        public static extern int shm_open(string name, int oflag, uint mode);

        // close
        [DllImport("libc.so.6", SetLastError = true)]
        public static extern int close(int fd);

        // mmap
        [DllImport("libc.so.6", SetLastError = true)]
        public static extern IntPtr mmap(IntPtr addr, IntPtr length, int prot, int flags, int fd, IntPtr offset);

        // munmap
        [DllImport("libc.so.6", SetLastError = true)]
        public static extern int munmap(IntPtr addr, IntPtr length);

        // sem_open
        [DllImport("libpthread.so.0", SetLastError = true)]
        public static extern IntPtr sem_open(string name, int oflag);

        // sem_close
        [DllImport("libpthread.so.0", SetLastError = true)]
        public static extern int sem_close(IntPtr sem);

        // sem_wait
        [DllImport("libpthread.so.0", SetLastError = true)]
        public static extern int sem_wait(IntPtr sem);

        // sem_post
        [DllImport("libpthread.so.0", SetLastError = true)]
        public static extern int sem_post(IntPtr sem);

        // Constants for mmap
        public const int PROT_READ = 1;
        public const int PROT_WRITE = 2;
        public const int MAP_SHARED = 1;
    }

    /// <summary>
    /// Event args for streaming updates
    /// </summary>
    public class StreamUpdateEventArgs : EventArgs
    {
        public string PartialResponse { get; set; }
        public int TokensGenerated { get; set; }
        public bool IsComplete { get; set; }
    }

    /// <summary>
    /// Client with streaming support
    /// </summary>
    public class LlamaCppStreamingClient : IDisposable
    {
        private const string SharedMemoryName = "/llama_cpp_shared_mem";
        private const string SemReadyName = "/llama_cpp_sem_ready";
        private const string SemPromptsWrittenName = "/llama_cpp_sem_prompts_written";
        private const string SemResponseWrittenName = "/llama_cpp_sem_response_written";
        private const string SemChunkReadyName = "/llama_cpp_sem_chunk_ready";
        private const int SharedMemorySize = 45000; // Increased for new fields

        private IntPtr _sharedMemoryPtr = IntPtr.Zero;
        private int _shmFd = -1;

        private IntPtr _semReady = IntPtr.Zero;
        private IntPtr _semPromptsWritten = IntPtr.Zero;
        private IntPtr _semResponseWritten = IntPtr.Zero;
        private IntPtr _semChunkReady = IntPtr.Zero;

        private Process? _cppProcess;
        private bool _isDisposed = false;

        // Event for streaming updates
        public event EventHandler<StreamUpdateEventArgs>? OnStreamUpdate;

        public void Initialize(string chatbotPath)
        {
            // Start C++ process
            var startInfo = new ProcessStartInfo
            {
                FileName = chatbotPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = false
            };

            _cppProcess = Process.Start(startInfo);
            if (_cppProcess == null)
            {
                throw new Exception("Failed to start C++ chatbot process");
            }

            // Redirect output
            _cppProcess.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    Console.WriteLine($"[C++] {e.Data}");
            };
            _cppProcess.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    Console.Error.WriteLine($"[C++ Error] {e.Data}");
            };
            _cppProcess.BeginOutputReadLine();
            _cppProcess.BeginErrorReadLine();

            Thread.Sleep(3000); // Wait for initialization

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
                PosixInterop.close(_shmFd);
                throw new Exception($"Failed to map shared memory: {Marshal.GetLastWin32Error()}");
            }

            // Open semaphores
            _semReady = PosixInterop.sem_open(SemReadyName, 0);
            _semPromptsWritten = PosixInterop.sem_open(SemPromptsWrittenName, 0);
            _semResponseWritten = PosixInterop.sem_open(SemResponseWrittenName, 0);
            _semChunkReady = PosixInterop.sem_open(SemChunkReadyName, 0);

            Console.WriteLine("Connected to C++ chatbot successfully!");
        }

        /// <summary>
        /// Send request in normal mode (full response at once)
        /// </summary>
        public string SendRequest(string userPrompt, string systemPrompt = "")
        {
            if (_sharedMemoryPtr == IntPtr.Zero)
                throw new InvalidOperationException("Client not initialized");

            // Wait for C++ ready
            Console.WriteLine("Waiting for C++ to be ready...");
            PosixInterop.sem_wait(_semReady);

            // Write prompts with stream_mode = false
            WriteToSharedMemory(systemPrompt ?? "", userPrompt ?? "", false, false);

            // Signal C++
            Console.WriteLine("Signaling C++ that prompts are ready...");
            PosixInterop.sem_post(_semPromptsWritten);

            // Wait for complete response
            Console.WriteLine("Waiting for complete response...");
            PosixInterop.sem_wait(_semResponseWritten);

            // Read response
            string response = ReadResponseFromSharedMemory();
            return response;
        }

        /// <summary>
        /// Send request in streaming mode (receive partial responses)
        /// </summary>
        public string SendStreamingRequest(string userPrompt, string systemPrompt = "")
        {
            if (_sharedMemoryPtr == IntPtr.Zero)
                throw new InvalidOperationException("Client not initialized");

            // Wait for C++ ready
            Console.WriteLine("Waiting for C++ to be ready...");
            PosixInterop.sem_wait(_semReady);

            // Write prompts with stream_mode = true
            WriteToSharedMemory(systemPrompt ?? "", userPrompt ?? "", true, false);

            // Signal C++
            Console.WriteLine("Signaling C++ that prompts are ready...");
            PosixInterop.sem_post(_semPromptsWritten);

            Console.WriteLine("Receiving streaming response...");

            string fullResponse = "";
            int lastUpdateCounter = 0;

            // Listen for streaming updates
            while (true)
            {
                // Wait for chunk ready signal
                PosixInterop.sem_wait(_semChunkReady);

                // Read current state
                var (response, updateCounter, isComplete, tokensGenerated) = ReadStreamingState();

                // Check if this is a new update
                if (updateCounter > lastUpdateCounter)
                {
                    fullResponse = response;
                    lastUpdateCounter = updateCounter;

                    // Fire event for UI update
                    OnStreamUpdate?.Invoke(this, new StreamUpdateEventArgs
                    {
                        PartialResponse = response,
                        TokensGenerated = tokensGenerated,
                        IsComplete = isComplete
                    });

                    Console.WriteLine($"[Token {tokensGenerated}] {response.Substring(Math.Max(0, response.Length - 50))}");

                    if (isComplete)
                    {
                        Console.WriteLine("\nGeneration complete!");
                        break;
                    }
                }
            }

            // Wait for final signal
            PosixInterop.sem_wait(_semResponseWritten);

            return fullResponse;
        }

        private void WriteToSharedMemory(string systemPrompt, string userPrompt, bool streamMode, bool shutdown)
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

            // Clear response at offset 8192
            byte[] clearBytes = new byte[32768];
            Marshal.Copy(clearBytes, 0, _sharedMemoryPtr + 8192, 32768);

            // Write shutdown flag at offset 40960
            Marshal.WriteByte(_sharedMemoryPtr + 40960, shutdown ? (byte)1 : (byte)0);

            // Write stream_mode at offset 40961
            Marshal.WriteByte(_sharedMemoryPtr + 40961, streamMode ? (byte)1 : (byte)0);

            // Initialize counters at offsets 40962, 40966, 40970
            Marshal.WriteInt32(_sharedMemoryPtr + 40962, 0); // update_counter
            Marshal.WriteByte(_sharedMemoryPtr + 40966, 0);  // generation_complete
            Marshal.WriteInt32(_sharedMemoryPtr + 40967, 0); // tokens_generated
        }

        private string ReadResponseFromSharedMemory()
        {
            byte[] responseBytes = new byte[32768];
            Marshal.Copy(_sharedMemoryPtr + 8192, responseBytes, 0, 32768);

            int length = Array.IndexOf(responseBytes, (byte)0);
            if (length < 0) length = 32768;

            return System.Text.Encoding.UTF8.GetString(responseBytes, 0, length);
        }

        private (string response, int updateCounter, bool isComplete, int tokensGenerated) ReadStreamingState()
        {
            // Read response
            string response = ReadResponseFromSharedMemory();

            // Read counters
            int updateCounter = Marshal.ReadInt32(_sharedMemoryPtr + 40962);
            bool isComplete = Marshal.ReadByte(_sharedMemoryPtr + 40966) != 0;
            int tokensGenerated = Marshal.ReadInt32(_sharedMemoryPtr + 40967);

            return (response, updateCounter, isComplete, tokensGenerated);
        }

        public void Shutdown()
        {
            if (_sharedMemoryPtr == IntPtr.Zero) return;

            try
            {
                PosixInterop.sem_wait(_semReady);
                WriteToSharedMemory("", "", false, true);
                PosixInterop.sem_post(_semPromptsWritten);
                _cppProcess?.WaitForExit(5000);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during shutdown: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;

            Shutdown();

            if (_sharedMemoryPtr != IntPtr.Zero && _sharedMemoryPtr != PosixInterop.MAP_FAILED)
            {
                PosixInterop.munmap(_sharedMemoryPtr, new IntPtr(SharedMemorySize));
            }

            if (_shmFd >= 0)
            {
                PosixInterop.close(_shmFd);
            }

            if (_semReady != IntPtr.Zero) PosixInterop.sem_close(_semReady);
            if (_semPromptsWritten != IntPtr.Zero) PosixInterop.sem_close(_semPromptsWritten);
            if (_semResponseWritten != IntPtr.Zero) PosixInterop.sem_close(_semResponseWritten);
            if (_semChunkReady != IntPtr.Zero) PosixInterop.sem_close(_semChunkReady);

            if (_cppProcess != null && !_cppProcess.HasExited)
            {
                _cppProcess.Kill();
            }
            _cppProcess?.Dispose();

            _isDisposed = true;
        }
    }

    /// <summary>
    /// Example usage
    /// </summary>
    class StreamingExample
    {
        static void Main(string[] args)
        {
            Console.WriteLine("=== LLama.cpp C# Streaming Example ===\n");

            using var client = new LlamaCppStreamingClient();

            try
            {
                // Initialize
                string chatbotPath = args.Length > 0 ? args[0] : "./build/chatbot";
                client.Initialize(chatbotPath);

                // Subscribe to streaming updates for real-time display
                client.OnStreamUpdate += (sender, e) =>
                {
                    // Update your UI here in real-time!
                    // e.PartialResponse contains the text so far
                    // e.TokensGenerated shows progress
                    // e.IsComplete indicates when done
                    
                    if (e.IsComplete)
                    {
                        Console.WriteLine($"\n✓ Complete! ({e.TokensGenerated} tokens)");
                    }
                };

                // Example 1: Normal mode (full response at once)
                Console.WriteLine("\n--- Example 1: Normal Mode ---");
                string response1 = client.SendRequest("What is C++? Answer briefly.");
                Console.WriteLine($"Response: {response1}\n");

                // Example 2: Streaming mode (real-time updates)
                Console.WriteLine("\n--- Example 2: Streaming Mode ---");
                Console.WriteLine("Watch the response appear token by token:\n");
                string response2 = client.SendStreamingRequest(
                    "Explain pointers in C++",
                    "You are a helpful programming tutor."
                );

                Console.WriteLine("\n\nAll requests completed!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }
    }
}

