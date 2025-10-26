# LLM Chatbot with llama.cpp

A C++ chatbot implementation using llama.cpp with dual-mode operation: interactive CLI for testing and shared memory IPC for C# .NET Desktop application integration.

## ✨ Features

### Dual Operating Modes:
- **Test Mode**: Interactive command-line chatbot with streaming support
- **Shared Memory Mode**: Background process for C# application integration with real-time streaming

### Test Mode Features:
- ✅ Interactive continuous conversations
- ✅ Real-time streaming output (like ChatGPT)
- ✅ Configurable response length (default 4096 tokens, unlimited with 0)
- ✅ Custom system prompts
- ✅ One-shot mode for scripting
- ✅ Colored terminal output

### C# Integration Features:
- ✅ POSIX shared memory IPC
- ✅ Real-time token streaming to C# UI
- ✅ Easy-to-use `LocalLLMService` class
- ✅ One-line TextBox streaming integration
- ✅ Event-driven architecture
- ✅ WinForms & WPF support

---

## 🚀 Quick Start

### Build

```bash
mkdir -p build
cd build
cmake ..
make -j$(nproc)
```

### Test Mode (Interactive Chatbot)

```bash
# Basic interactive mode
./build/chatbot --test

# With streaming (tokens appear in real-time)
./build/chatbot --test --stream

# With custom settings
./build/chatbot --test --stream --max-tokens 8192

# Unlimited tokens (generates until model stops)
./build/chatbot --test --max-tokens 0
```

### C# Integration Mode

```bash
# Start background process
./build/chatbot
```

Then in your C# application:

```csharp
using LlamaCpp.Service;

// Initialize once
using var llm = new LocalLLMService();
await llm.InitializeAsync();

// Stream to TextBox with ONE line!
await llm.GetResponseToTextBox(myTextBox, "What is C++?");
```

---

## 📋 Prerequisites

- CMake 3.10 or higher
- C++17 compatible compiler (GCC, Clang)
- Git (for submodules)
- Linux OS (for POSIX shared memory)

---

## 📚 Documentation

### Getting Started:
- **[QUICKSTART.md](QUICKSTART.md)** - 5-minute getting started guide
- **[FEATURES.md](FEATURES.md)** - Complete feature list and usage examples

### C# Integration:
- **[LocalLLMService_GUIDE.md](LocalLLMService_GUIDE.md)** - Complete C# API guide
- **[LocalLLMService_QUICKREF.md](LocalLLMService_QUICKREF.md)** - Quick reference card
- **[LocalLLMService_SUMMARY.md](LocalLLMService_SUMMARY.md)** - Implementation overview

### Advanced:
- **[STREAMING_IPC_GUIDE.md](STREAMING_IPC_GUIDE.md)** - IPC streaming protocol details
- **[ARCHITECTURE.md](ARCHITECTURE.md)** - Technical architecture and design
- **[INTEGRATION_GUIDE.md](INTEGRATION_GUIDE.md)** - Detailed C# integration guide

### Code Examples:
- **[LocalLLMService.cs](LocalLLMService.cs)** - Main C# service class (copy to your project)
- **[LocalLLMService_Examples.cs](LocalLLMService_Examples.cs)** - Complete usage examples
- **[CSharpStreamingExample.cs](CSharpStreamingExample.cs)** - Advanced streaming example

---

## 💡 Usage Examples

### Test Mode

```bash
# Quick chat
./build/chatbot --test

# Streaming mode (recommended)
./build/chatbot --test --stream

# Custom system prompt
./build/chatbot --test --system "You are a Python expert"

# One-shot for scripts
./build/chatbot --test --user "Explain pointers" > output.txt
```

### C# Desktop Application

```csharp
public class ChatWindow : Form
{
    private LocalLLMService _llm;
    private TextBox _input, _output;
    private Button _send;

    public ChatWindow()
    {
        // Initialize UI controls
        _input = new TextBox();
        _output = new TextBox { ReadOnly = true };
        _send = new Button { Text = "Send" };

        // Wire up button
        _send.Click += async (s, e) => {
            _send.Enabled = false;
            await _llm.GetResponseToTextBox(_output, _input.Text);
            _send.Enabled = true;
        };

        // Initialize LLM
        _llm = new LocalLLMService();
        Task.Run(async () => await _llm.InitializeAsync());
    }
}
```

---

## ⚙️ Configuration

### Model Path

Edit in `main.cpp`:
```cpp
const std::string model_path = "models/Phi-3-mini-4k-instruct-q4.gguf";
```

### Parameters

```cpp
// Context and generation
ctx_params.n_ctx = 2048;        // Context window
ctx_params.n_batch = 2048;      // Batch size
model_params.n_gpu_layers = 0;  // 0=CPU, 99=full GPU

// Sampling
llama_sampler_init_temp(0.7f);  // Temperature
llama_sampler_init_min_p(0.05f, 1);  // Min-P sampling
```

### Shared Memory (C# Integration)

Configuration in `main.cpp`:
- **Shared Memory**: `/llama_cpp_shared_mem` (41KB)
- **Buffer Sizes**:
  - System prompt: 4096 bytes
  - User prompt: 4096 bytes
  - Response: 32768 bytes

**Semaphores**:
- `/llama_cpp_sem_ready` - C++ ready for request
- `/llama_cpp_sem_prompts_written` - C# prompts ready
- `/llama_cpp_sem_response_written` - C++ response complete
- `/llama_cpp_sem_chunk_ready` - C++ streaming chunk ready

---

## 🎯 Command-Line Options

```bash
Usage: ./build/chatbot [OPTIONS]

Modes:
  --test                 Run in interactive test mode
  (default)              Run in shared memory mode for C# integration

Test Mode Options:
  --system <text>        Custom system prompt (default: "You are my best assistance.")
  --user <text>          Single user prompt for one-shot mode
  --stream               Enable streaming mode (real-time token display)
  --max-tokens <n>       Maximum tokens to generate (default: 4096, 0=unlimited)

Examples:
  ./build/chatbot --test
  ./build/chatbot --test --stream
  ./build/chatbot --test --max-tokens 0
  ./build/chatbot --test --system "You are helpful" --stream
  ./build/chatbot --test --user "What is C++?"
```

---

## 📁 Project Structure

```
.
├── main.cpp                          # Main C++ implementation
├── CMakeLists.txt                    # Build configuration
├── llama.cpp/                        # llama.cpp library (submodule)
├── models/                           # GGUF model files
│   └── Phi-3-mini-4k-instruct-q4.gguf
├── build/                            # Build artifacts
│   └── chatbot                       # Compiled executable
│
├── LocalLLMService.cs                # Main C# service class
├── LocalLLMService_Examples.cs       # C# usage examples
├── CSharpStreamingExample.cs         # Advanced C# example
│
├── README.md                         # This file
├── QUICKSTART.md                     # Quick start guide
├── FEATURES.md                       # Complete features guide
├── LocalLLMService_GUIDE.md          # C# API documentation
├── LocalLLMService_QUICKREF.md       # Quick reference
├── STREAMING_IPC_GUIDE.md            # IPC streaming details
└── ARCHITECTURE.md                   # Technical architecture
```

---

## 🎓 Model Support

This chatbot uses the **Phi-3-mini** prompt format:

```
<|system|>
{system_prompt}<|end|>
<|user|>
{user_prompt}<|end|>
<|assistant|>
```

For other models, adjust the prompt template in `main.cpp`.

Supported formats: **GGUF** (llama.cpp format)

---

## 🐛 Troubleshooting

### Model not found
```bash
# Check model exists
ls -lh models/Phi-3-mini-4k-instruct-q4.gguf

# Download from Hugging Face if needed
```

### Slow generation
```bash
# Enable GPU acceleration (edit main.cpp)
model_params.n_gpu_layers = 99;  // Use all GPU layers

# Then rebuild
cd build && make -j$(nproc)
```

### C# can't connect to shared memory
```bash
# Ensure C++ process is running
ps aux | grep chatbot

# Clean up old shared memory
rm /dev/shm/llama_cpp_*
rm /dev/shm/sem.llama_cpp_*

# Restart C++ process
./build/chatbot
```

### Response cut off
```bash
# Increase max tokens
./build/chatbot --test --max-tokens 8192

# Or use unlimited
./build/chatbot --test --max-tokens 0
```

---

## 📊 Performance

| Operation | Time | Notes |
|-----------|------|-------|
| Model loading | 2-3 seconds | One-time at startup |
| First token | ~500ms | After sending prompt |
| Token generation | ~50-100ms | Per token (CPU) |
| Full response (4096 tokens) | 40-80 seconds | CPU only |
| Context window | 2048 tokens | Prompt + response |

---

## 🔗 Resources

- **llama.cpp**: https://github.com/ggerganov/llama.cpp
- **GGUF Models**: https://huggingface.co/models?library=gguf
- **Phi-3 Model**: https://huggingface.co/microsoft/Phi-3-mini-4k-instruct-gguf

---

## 📝 License

This project uses llama.cpp, which is licensed under the MIT License.

---

## 🎉 Summary

**Two powerful modes in one:**

1. **Test Mode** - Interactive CLI chatbot with streaming
2. **C# Integration** - One-line TextBox streaming for desktop apps

```bash
# Test it
./build/chatbot --test --stream

# Use in C#
await llm.GetResponseToTextBox(myTextBox, "Hello!");
```

**Production-ready LLM integration made easy!** 🚀
