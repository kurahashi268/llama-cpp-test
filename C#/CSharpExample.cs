using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Diagnostics;

namespace LlamaCppIntegration
{
    /// <summary>
    /// Shared memory structure that matches the C++ SharedMemoryData struct
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
    }

    /// <summary>
    /// Client for communicating with the C++ LLM chatbot via shared memory
    /// </summary>
    public class LlamaCppClient : IDisposable
    {
        private const string SharedMemoryName = "/llama_cpp_shared_mem";
        private const string SemReadyName = "llama_cpp_sem_ready";
        private const string SemPromptsWrittenName = "llama_cpp_sem_prompts_written";
        private const string SemResponseWrittenName = "llama_cpp_sem_response_written";

        private MemoryMappedFile? _sharedMemory;
        private MemoryMappedViewAccessor? _accessor;
        private Process? _cppProcess;

        // On Linux, semaphores are handled differently - you'll need P/Invoke
        // For Windows, you can use Semaphore class
        // This example shows the Windows approach

        private Semaphore? _semReady;
        private Semaphore? _semPromptsWritten;
        private Semaphore? _semResponseWritten;

        private bool _isDisposed = false;

        /// <summary>
        /// Starts the C++ chatbot process and initializes shared memory
        /// </summary>
        /// <param name="chatbotPath">Path to the chatbot executable</param>
        public void Initialize(string chatbotPath)
        {
            // Start the C++ process
            var startInfo = new ProcessStartInfo
            {
                FileName = chatbotPath,
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

            // Give the process time to initialize
            Thread.Sleep(2000);

            // For Windows: Create/open shared memory and semaphores
            // Note: Linux uses POSIX shared memory (/dev/shm) which requires different handling
            // You may need to use P/Invoke for sem_open on Linux

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                InitializeWindows();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                InitializeLinux();
            }
            else
            {
                throw new PlatformNotSupportedException("Only Windows and Linux are supported");
            }
        }

        private void InitializeWindows()
        {
            // Windows implementation using named shared memory
            _sharedMemory = MemoryMappedFile.OpenExisting("Global\\llama_cpp_shared_mem");
            _accessor = _sharedMemory.CreateViewAccessor();

            // Open semaphores
            _semReady = Semaphore.OpenExisting(@"Global\" + SemReadyName);
            _semPromptsWritten = Semaphore.OpenExisting(@"Global\" + SemPromptsWrittenName);
            _semResponseWritten = Semaphore.OpenExisting(@"Global\" + SemResponseWrittenName);
        }

        private void InitializeLinux()
        {
            // On Linux, you need to:
            // 1. Use P/Invoke to access POSIX shared memory (shm_open, mmap)
            // 2. Use P/Invoke for POSIX semaphores (sem_open, sem_wait, sem_post)
            // 
            // Example P/Invoke declarations needed:
            // [DllImport("librt.so.1")]
            // private static extern IntPtr shm_open(string name, int oflag, uint mode);
            // 
            // [DllImport("libc.so.6")]
            // private static extern IntPtr mmap(IntPtr addr, IntPtr length, int prot, int flags, int fd, IntPtr offset);
            // 
            // [DllImport("libpthread.so.0")]
            // private static extern IntPtr sem_open(string name, int oflag);
            // 
            // [DllImport("libpthread.so.0")]
            // private static extern int sem_wait(IntPtr sem);
            // 
            // [DllImport("libpthread.so.0")]
            // private static extern int sem_post(IntPtr sem);

            throw new NotImplementedException(
                "Linux implementation requires P/Invoke to POSIX APIs. " +
                "See comments in code for required declarations.");
        }

        /// <summary>
        /// Sends prompts to the C++ chatbot and gets the response
        /// </summary>
        /// <param name="userPrompt">The user's prompt (required)</param>
        /// <param name="systemPrompt">Optional system prompt</param>
        /// <returns>The LLM's response</returns>
        public string SendRequest(string userPrompt, string systemPrompt = "")
        {
            if (_accessor == null)
            {
                throw new InvalidOperationException("Client not initialized");
            }

            // Wait for C++ to signal it's ready
            Console.WriteLine("Waiting for C++ to be ready...");
            _semReady?.WaitOne();

            // Write prompts to shared memory
            Console.WriteLine("Writing prompts to shared memory...");
            var data = new SharedMemoryData
            {
                SystemPrompt = systemPrompt ?? "",
                UserPrompt = userPrompt ?? "",
                Response = "",
                ShutdownRequested = false
            };

            _accessor.Write(0, ref data);

            // Signal that prompts are written
            Console.WriteLine("Signaling C++ that prompts are ready...");
            _semPromptsWritten?.Release();

            // Wait for response
            Console.WriteLine("Waiting for response...");
            _semResponseWritten?.WaitOne();

            // Read response from shared memory
            Console.WriteLine("Reading response from shared memory...");
            _accessor.Read(0, out data);

            return data.Response;
        }

        /// <summary>
        /// Requests shutdown of the C++ process
        /// </summary>
        public void Shutdown()
        {
            if (_accessor == null) return;

            // Wait for C++ to be ready
            _semReady?.WaitOne();

            // Set shutdown flag
            var data = new SharedMemoryData
            {
                SystemPrompt = "",
                UserPrompt = "",
                Response = "",
                ShutdownRequested = true
            };

            _accessor.Write(0, ref data);

            // Signal C++
            _semPromptsWritten?.Release();

            // Wait for process to exit
            _cppProcess?.WaitForExit(5000);
        }

        public void Dispose()
        {
            if (_isDisposed) return;

            Shutdown();

            _accessor?.Dispose();
            _sharedMemory?.Dispose();
            _semReady?.Dispose();
            _semPromptsWritten?.Dispose();
            _semResponseWritten?.Dispose();

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
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("=== LLama.cpp C# Integration Example ===\n");

            using var client = new LlamaCppClient();

            try
            {
                // Initialize and start the C++ chatbot
                Console.WriteLine("Starting C++ chatbot...");
                client.Initialize("./build/chatbot");

                // Example 1: Simple question
                Console.WriteLine("\n--- Example 1: Simple Question ---");
                string response1 = client.SendRequest("What is C++?");
                Console.WriteLine($"Response: {response1}\n");

                // Example 2: With system prompt
                Console.WriteLine("\n--- Example 2: With System Prompt ---");
                string response2 = client.SendRequest(
                    "Explain pointers",
                    "You are a helpful programming tutor. Keep answers concise."
                );
                Console.WriteLine($"Response: {response2}\n");

                // Example 3: Multiple requests
                Console.WriteLine("\n--- Example 3: Multiple Requests ---");
                string[] questions = {
                    "What is memory management?",
                    "What are smart pointers?",
                    "What is RAII?"
                };

                foreach (var question in questions)
                {
                    Console.WriteLine($"\nQuestion: {question}");
                    string response = client.SendRequest(question);
                    Console.WriteLine($"Response: {response}");
                }

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

