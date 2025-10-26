# LLama.cpp C++ Chatbot - C# Integration Guide

This guide explains how to use the C++ LLama chatbot in two modes:
1. **Test Mode**: Standalone command-line chatbot for testing
2. **Shared Memory Mode**: Background process for C# .NET Desktop integration

## Table of Contents
- [Building the Project](#building-the-project)
- [Test Mode](#test-mode)
- [Shared Memory Mode](#shared-memory-mode)
- [C# Integration](#c-integration)
- [Architecture Overview](#architecture-overview)
- [Troubleshooting](#troubleshooting)

---

## Building the Project

```bash
# Create build directory and compile
mkdir -p build
cd build
cmake ..
make -j$(nproc)
```

The executable will be available at `build/chatbot`.

---

## Test Mode

Test mode allows you to run the chatbot as a standalone application for testing purposes.

### Usage

```bash
# Basic usage with user prompt only
./build/chatbot --test --user "What is C++?"

# With system prompt
./build/chatbot --test --system "You are a helpful assistant" --user "Explain pointers"
```

### Options

- `--test`: Run in test mode (required for standalone operation)
- `--user <text>`: User prompt (required)
- `--system <text>`: System prompt (optional)

### Example

```bash
./build/chatbot --test \
  --system "You are a concise programming expert" \
  --user "What is RAII in C++?"
```

---

## Shared Memory Mode

Shared memory mode runs the chatbot as a background process that communicates with C# applications via POSIX shared memory and semaphores.

### Starting the Background Process

```bash
# Simply run without --test flag
./build/chatbot
```

The process will:
1. Initialize shared memory at `/dev/shm/llama_cpp_shared_mem`
2. Create three semaphores for synchronization:
   - `llama_cpp_sem_ready`: C++ signals when ready for requests
   - `llama_cpp_sem_prompts_written`: C# signals when prompts are written
   - `llama_cpp_sem_response_written`: C++ signals when response is ready
3. Load the LLM model
4. Wait for requests from C# application

### Stopping the Background Process

The C# application can request shutdown by setting the `shutdown_requested` flag in shared memory, or you can send SIGINT/SIGTERM:

```bash
# Find the process
ps aux | grep chatbot

# Send termination signal
kill <PID>
```

---

## C# Integration

### Linux Implementation

For Linux, use the P/Invoke based implementation that interfaces with POSIX APIs:

```csharp
using LlamaCppIntegration.Linux;

// Create client
using var client = new LlamaCppLinuxClient();

// Initialize and connect to C++ process
client.Initialize("./build/chatbot");

// Send request
string response = client.SendRequest(
    userPrompt: "What is C++?",
    systemPrompt: "You are a helpful assistant"  // optional
);

Console.WriteLine(response);
```

### Complete Example

See `CSharpLinuxHelper.cs` for the full Linux implementation with P/Invoke.

```csharp
using System;
using LlamaCppIntegration.Linux;

class Program
{
    static void Main()
    {
        using var client = new LlamaCppLinuxClient();
        
        try
        {
            // Start C++ chatbot
            client.Initialize("./build/chatbot");
            
            // Send multiple requests
            var questions = new[] {
                "What is memory management?",
                "Explain smart pointers",
                "What is RAII?"
            };
            
            foreach (var question in questions)
            {
                Console.WriteLine($"\nQ: {question}");
                string answer = client.SendRequest(question);
                Console.WriteLine($"A: {answer}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }
}
```

### Compiling the C# Example

```bash
# Compile the C# client
dotnet new console -n LlamaCppClient
cd LlamaCppClient

# Copy the helper file
cp ../CSharpLinuxHelper.cs ./

# Replace Program.cs with your code
# Then build and run
dotnet build
dotnet run
```

Or use `csc` directly:

```bash
csc /out:LlamaCppClient.exe CSharpLinuxHelper.cs
mono LlamaCppClient.exe  # or ./LlamaCppClient.exe if on Linux with .NET runtime
```

---

## Architecture Overview

### Shared Memory Structure

The shared memory layout matches between C++ and C#:

```cpp
struct SharedMemoryData {
    char system_prompt[4096];   // Offset: 0
    char user_prompt[4096];     // Offset: 4096
    char response[32768];       // Offset: 8192
    bool shutdown_requested;    // Offset: 40960
};
```

Total size: ~41KB

### Communication Flow

```
C# Application                          C++ Chatbot
     |                                       |
     |  1. Start C++ process                |
     |-------------------------------------->|
     |                                       |
     |                                       | 2. Initialize shared memory
     |                                       | 3. Load LLM model
     |                                       |
     |  4. Wait for ready signal             |
     |<--------------------------------------|
     |       (sem_ready)                     |
     |                                       |
     |  5. Write prompts to shared memory    |
     |                                       |
     |  6. Signal prompts written            |
     |-------------------------------------->|
     |       (sem_prompts_written)           |
     |                                       |
     |                                       | 7. Process prompts
     |                                       | 8. Generate response
     |                                       | 9. Write response
     |                                       |
     | 10. Wait for response                 |
     |<--------------------------------------|
     |       (sem_response_written)          |
     |                                       |
     | 11. Read response                     |
     |                                       |
     | 12. Repeat from step 4 or shutdown    |
```

### Synchronization Primitives

**Semaphores**:
- `sem_ready`: Posted by C++ when ready to receive new prompts
- `sem_prompts_written`: Posted by C# when prompts are written to shared memory
- `sem_response_written`: Posted by C++ when response is ready

**Mutex**: The C++ implementation uses atomic operations on the shared memory. No explicit mutex is needed as the semaphores provide the necessary synchronization.

---

## Troubleshooting

### Issue: "Failed to open shared memory"

**Cause**: C++ process hasn't created shared memory yet or has terminated.

**Solution**:
1. Check if C++ process is running: `ps aux | grep chatbot`
2. Increase initialization delay in C#: `Thread.Sleep(3000);`
3. Check for errors in C++ output

### Issue: "Failed to open semaphore"

**Cause**: Semaphore names mismatch or not created yet.

**Solution**:
1. Verify semaphore names match exactly in both C++ and C#
2. Check `/dev/shm/` for semaphores: `ls -la /dev/shm/sem.*`
3. Clean up old semaphores: `rm /dev/shm/sem.llama_cpp_*`

### Issue: Application hangs on sem_wait

**Cause**: Deadlock or C++ process crashed.

**Solution**:
1. Check if C++ process is still running
2. Use timeout variants: `sem_timedwait()` instead of `sem_wait()`
3. Add timeout handling in C# implementation

### Issue: Garbage or truncated response

**Cause**: Encoding issues or response too large.

**Solution**:
1. Ensure UTF-8 encoding on both sides
2. Check response buffer size (32768 bytes)
3. Verify null termination

### Cleaning Up Resources

If you encounter persistent issues, clean up POSIX resources:

```bash
# Remove shared memory
rm /dev/shm/llama_cpp_shared_mem

# Remove semaphores
rm /dev/shm/sem.llama_cpp_sem_*
```

### Debugging Tips

**C++ Side**:
- The process outputs status messages to stdout
- Check for model loading errors
- Verify model file exists at `models/Phi-3-mini-4k-instruct-q4.gguf`

**C# Side**:
- Enable output redirection to see C++ messages
- Add logging for each semaphore operation
- Verify P/Invoke signatures match system libraries

### Performance Optimization

1. **First Request Latency**: The first request includes model loading time (~several seconds)
2. **Subsequent Requests**: Much faster as model stays loaded in memory
3. **Memory Usage**: The model requires ~2-4GB RAM depending on quantization
4. **GPU Support**: Set `n_gpu_layers = 99` in C++ code for GPU acceleration

---

## Model Configuration

To use a different model, modify the model path in `main.cpp`:

```cpp
const std::string model_path = "models/your-model.gguf";
```

Supported model formats: GGUF (llama.cpp format)

---

## API Reference

### C# Linux Client API

#### `LlamaCppLinuxClient.Initialize(string chatbotPath)`
Starts the C++ chatbot process and connects to shared memory.

**Parameters**:
- `chatbotPath`: Path to the chatbot executable

**Throws**: `Exception` if initialization fails

#### `LlamaCppLinuxClient.SendRequest(string userPrompt, string systemPrompt = "")`
Sends a request to the chatbot and waits for the response.

**Parameters**:
- `userPrompt`: The user's question or prompt (required)
- `systemPrompt`: Optional system prompt to set behavior

**Returns**: String containing the LLM's response

**Throws**: `Exception` if communication fails

#### `LlamaCppLinuxClient.Shutdown()`
Requests graceful shutdown of the C++ process.

#### `LlamaCppLinuxClient.Dispose()`
Cleans up all resources (called automatically with `using` statement).

---

## License

This integration follows the license of llama.cpp. See `llama.cpp/LICENSE` for details.

---

## Additional Resources

- [llama.cpp Documentation](https://github.com/ggerganov/llama.cpp)
- [POSIX Shared Memory](https://man7.org/linux/man-pages/man7/shm_overview.7.html)
- [POSIX Semaphores](https://man7.org/linux/man-pages/man7/sem_overview.7.html)

