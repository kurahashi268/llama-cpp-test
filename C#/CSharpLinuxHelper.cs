using System;
using System.Runtime.InteropServices;
using System.IO.MemoryMappedFiles;

namespace LlamaCppIntegration.Linux
{
    /// <summary>
    /// Linux POSIX API interop for shared memory and semaphores
    /// </summary>
    public static class PosixInterop
    {
        // Constants for shm_open
        public const int O_RDWR = 2;
        public const int O_CREAT = 0x40;

        // Constants for mmap
        public const int PROT_READ = 1;
        public const int PROT_WRITE = 2;
        public const int MAP_SHARED = 1;
        public static readonly IntPtr MAP_FAILED = new IntPtr(-1);

        // shm_open - Open POSIX shared memory
        [DllImport("librt.so.1", SetLastError = true)]
        public static extern int shm_open(string name, int oflag, uint mode);

        // shm_unlink - Remove POSIX shared memory
        [DllImport("librt.so.1", SetLastError = true)]
        public static extern int shm_unlink(string name);

        // close - Close file descriptor
        [DllImport("libc.so.6", SetLastError = true)]
        public static extern int close(int fd);

        // mmap - Map shared memory into process address space
        [DllImport("libc.so.6", SetLastError = true)]
        public static extern IntPtr mmap(IntPtr addr, IntPtr length, int prot, int flags, int fd, IntPtr offset);

        // munmap - Unmap shared memory
        [DllImport("libc.so.6", SetLastError = true)]
        public static extern int munmap(IntPtr addr, IntPtr length);

        // sem_open - Open POSIX semaphore
        [DllImport("libpthread.so.0", SetLastError = true)]
        public static extern IntPtr sem_open(string name, int oflag);

        // sem_close - Close POSIX semaphore
        [DllImport("libpthread.so.0", SetLastError = true)]
        public static extern int sem_close(IntPtr sem);

        // sem_wait - Wait on semaphore
        [DllImport("libpthread.so.0", SetLastError = true)]
        public static extern int sem_wait(IntPtr sem);

        // sem_post - Post to semaphore
        [DllImport("libpthread.so.0", SetLastError = true)]
        public static extern int sem_post(IntPtr sem);

        // sem_unlink - Remove POSIX semaphore
        [DllImport("libpthread.so.0", SetLastError = true)]
        public static extern int sem_unlink(string name);
    }

    /// <summary>
    /// Linux implementation of LlamaCpp client using POSIX APIs
    /// </summary>
    public class LlamaCppLinuxClient : IDisposable
    {
        private const string SharedMemoryName = "/llama_cpp_shared_mem";
        private const string SemReadyName = "/llama_cpp_sem_ready";
        private const string SemPromptsWrittenName = "/llama_cpp_sem_prompts_written";
        private const string SemResponseWrittenName = "/llama_cpp_sem_response_written";
        private const int SharedMemorySize = 40960; // Must match sizeof(SharedMemoryData) in C++

        private IntPtr _sharedMemoryPtr = IntPtr.Zero;
        private int _shmFd = -1;

        private IntPtr _semReady = IntPtr.Zero;
        private IntPtr _semPromptsWritten = IntPtr.Zero;
        private IntPtr _semResponseWritten = IntPtr.Zero;

        private System.Diagnostics.Process? _cppProcess;
        private bool _isDisposed = false;

        /// <summary>
        /// Starts the C++ chatbot process and connects to shared memory
        /// </summary>
        public void Initialize(string chatbotPath)
        {
            // Start the C++ process
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = chatbotPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = false // Set to true in production
            };

            _cppProcess = System.Diagnostics.Process.Start(startInfo);
            if (_cppProcess == null)
            {
                throw new Exception("Failed to start C++ chatbot process");
            }

            // Redirect output for debugging
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

            // Give the process time to initialize shared memory
            System.Threading.Thread.Sleep(3000);

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
            if (_semReady == IntPtr.Zero)
            {
                throw new Exception($"Failed to open ready semaphore: {Marshal.GetLastWin32Error()}");
            }

            _semPromptsWritten = PosixInterop.sem_open(SemPromptsWrittenName, 0);
            if (_semPromptsWritten == IntPtr.Zero)
            {
                throw new Exception($"Failed to open prompts_written semaphore: {Marshal.GetLastWin32Error()}");
            }

            _semResponseWritten = PosixInterop.sem_open(SemResponseWrittenName, 0);
            if (_semResponseWritten == IntPtr.Zero)
            {
                throw new Exception($"Failed to open response_written semaphore: {Marshal.GetLastWin32Error()}");
            }

            Console.WriteLine("Connected to C++ chatbot successfully!");
        }

        /// <summary>
        /// Sends a request to the C++ chatbot and gets the response
        /// </summary>
        public string SendRequest(string userPrompt, string systemPrompt = "")
        {
            if (_sharedMemoryPtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("Client not initialized");
            }

            // Wait for C++ to signal it's ready
            Console.WriteLine("Waiting for C++ to be ready...");
            if (PosixInterop.sem_wait(_semReady) != 0)
            {
                throw new Exception($"sem_wait failed: {Marshal.GetLastWin32Error()}");
            }

            // Write prompts to shared memory
            Console.WriteLine("Writing prompts to shared memory...");
            WriteToSharedMemory(systemPrompt ?? "", userPrompt ?? "", false);

            // Signal that prompts are written
            Console.WriteLine("Signaling C++ that prompts are ready...");
            if (PosixInterop.sem_post(_semPromptsWritten) != 0)
            {
                throw new Exception($"sem_post failed: {Marshal.GetLastWin32Error()}");
            }

            // Wait for response
            Console.WriteLine("Waiting for response...");
            if (PosixInterop.sem_wait(_semResponseWritten) != 0)
            {
                throw new Exception($"sem_wait failed: {Marshal.GetLastWin32Error()}");
            }

            // Read response from shared memory
            Console.WriteLine("Reading response from shared memory...");
            string response = ReadResponseFromSharedMemory();

            return response;
        }

        private void WriteToSharedMemory(string systemPrompt, string userPrompt, bool shutdown)
        {
            // Write system prompt at offset 0 (4096 bytes)
            byte[] systemBytes = new byte[4096];
            if (!string.IsNullOrEmpty(systemPrompt))
            {
                byte[] temp = System.Text.Encoding.UTF8.GetBytes(systemPrompt);
                Array.Copy(temp, systemBytes, Math.Min(temp.Length, 4095));
            }
            Marshal.Copy(systemBytes, 0, _sharedMemoryPtr, 4096);

            // Write user prompt at offset 4096 (4096 bytes)
            byte[] userBytes = new byte[4096];
            if (!string.IsNullOrEmpty(userPrompt))
            {
                byte[] temp = System.Text.Encoding.UTF8.GetBytes(userPrompt);
                Array.Copy(temp, userBytes, Math.Min(temp.Length, 4095));
            }
            Marshal.Copy(userBytes, 0, _sharedMemoryPtr + 4096, 4096);

            // Clear response area at offset 8192 (32768 bytes)
            byte[] clearBytes = new byte[32768];
            Marshal.Copy(clearBytes, 0, _sharedMemoryPtr + 8192, 32768);

            // Write shutdown flag at offset 40960 (1 byte as bool)
            Marshal.WriteByte(_sharedMemoryPtr + 40960, shutdown ? (byte)1 : (byte)0);
        }

        private string ReadResponseFromSharedMemory()
        {
            // Read response from offset 8192 (32768 bytes)
            byte[] responseBytes = new byte[32768];
            Marshal.Copy(_sharedMemoryPtr + 8192, responseBytes, 0, 32768);

            // Find null terminator
            int length = Array.IndexOf(responseBytes, (byte)0);
            if (length < 0) length = 32768;

            return System.Text.Encoding.UTF8.GetString(responseBytes, 0, length);
        }

        /// <summary>
        /// Requests shutdown of the C++ process
        /// </summary>
        public void Shutdown()
        {
            if (_sharedMemoryPtr == IntPtr.Zero) return;

            try
            {
                // Wait for C++ to be ready
                PosixInterop.sem_wait(_semReady);

                // Set shutdown flag
                WriteToSharedMemory("", "", true);

                // Signal C++
                PosixInterop.sem_post(_semPromptsWritten);

                // Wait for process to exit
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

            // Cleanup
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

            if (_semReady != IntPtr.Zero)
            {
                PosixInterop.sem_close(_semReady);
                _semReady = IntPtr.Zero;
            }

            if (_semPromptsWritten != IntPtr.Zero)
            {
                PosixInterop.sem_close(_semPromptsWritten);
                _semPromptsWritten = IntPtr.Zero;
            }

            if (_semResponseWritten != IntPtr.Zero)
            {
                PosixInterop.sem_close(_semResponseWritten);
                _semResponseWritten = IntPtr.Zero;
            }

            if (_cppProcess != null && !_cppProcess.HasExited)
            {
                _cppProcess.Kill();
            }
            _cppProcess?.Dispose();

            _isDisposed = true;
        }
    }

    /// <summary>
    /// Example usage for Linux
    /// </summary>
    class LinuxExample
    {
        static void Main(string[] args)
        {
            Console.WriteLine("=== LLama.cpp C# Linux Integration Example ===\n");

            using var client = new LlamaCppLinuxClient();

            try
            {
                // Initialize and start the C++ chatbot
                Console.WriteLine("Starting C++ chatbot...");
                string chatbotPath = args.Length > 0 ? args[0] : "./build/chatbot";
                client.Initialize(chatbotPath);

                // Example 1: Simple question
                Console.WriteLine("\n--- Example 1: Simple Question ---");
                string response1 = client.SendRequest("What is C++? Answer in one sentence.");
                Console.WriteLine($"Response: {response1}\n");

                // Example 2: With system prompt
                Console.WriteLine("\n--- Example 2: With System Prompt ---");
                string response2 = client.SendRequest(
                    "Explain pointers briefly.",
                    "You are a helpful programming tutor. Keep answers very concise."
                );
                Console.WriteLine($"Response: {response2}\n");

                Console.WriteLine("\n\nAll requests completed successfully!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }
    }
}

