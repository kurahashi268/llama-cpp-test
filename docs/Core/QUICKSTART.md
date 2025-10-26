# Quick Start Guide

Get up and running in 5 minutes!

## 🚀 1. Build the Project

```bash
cd /home/klaus/A-Work/llama-cpp/build
make -j$(nproc)
```

✅ If successful, you'll see: `[100%] Built target chatbot`

---

## 🎮 2. Test Mode - Interactive Chatbot

### Basic Usage

```bash
./chatbot --test
```

Type your questions and press Enter. Type `exit` to quit.

### Streaming Mode (Recommended!)

```bash
./chatbot --test --stream
```

Watch tokens appear in real-time, just like ChatGPT!

### Custom Settings

```bash
# Longer responses
./chatbot --test --stream --max-tokens 8192

# Unlimited length
./chatbot --test --stream --max-tokens 0

# Custom system prompt
./chatbot --test --stream --system "You are a Python expert"
```

---

## 💻 3. C# Integration - Desktop Apps

### Step 1: Start C++ Process

In Terminal 1:
```bash
./chatbot
```

Leave this running!

### Step 2: Create C# Project

In Terminal 2:
```bash
cd /home/klaus/A-Work/llama-cpp
dotnet new winforms -n MyAIChat
cd MyAIChat
```

### Step 3: Copy the Service Class

```bash
cp ../LocalLLMService.cs ./
```

### Step 4: Use It!

Edit `Form1.cs`:

```csharp
using LlamaCpp.Service;

public partial class Form1 : Form
{
    private LocalLLMService _llm;
    private TextBox _input, _output;
    private Button _send;

    public Form1()
    {
        InitializeComponent();

        // Create UI
        _input = new TextBox { Location = new Point(10, 10), Width = 400 };
        _output = new TextBox { Location = new Point(10, 40), Width = 400, Height = 300, Multiline = true, ReadOnly = true };
        _send = new Button { Text = "Send", Location = new Point(10, 350) };

        Controls.AddRange(new Control[] { _input, _output, _send });

        // Wire button
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

### Step 5: Run

```bash
dotnet run
```

**That's it! You have a working AI chat app!** ✨

---

## 📋 Common Commands

```bash
# Interactive with streaming
./chatbot --test --stream

# Unlimited tokens
./chatbot --test --stream --max-tokens 0

# One-shot (for scripts)
./chatbot --test --user "What is C++?" > output.txt

# C# integration mode
./chatbot
```

---

## 🐛 Troubleshooting

### Problem: Model not found

```bash
# Check if model exists
ls -lh models/Phi-3-mini-4k-instruct-q4.gguf

# If missing, download from Hugging Face
```

### Problem: C# can't connect

```bash
# 1. Check C++ is running
ps aux | grep chatbot

# 2. Clean up old resources
rm /dev/shm/llama_cpp_*
rm /dev/shm/sem.llama_cpp_*

# 3. Restart C++ process
./chatbot
```

### Problem: Slow responses

```bash
# Use shorter responses
./chatbot --test --max-tokens 512

# Or enable GPU (edit main.cpp, set n_gpu_layers = 99, then rebuild)
```

---

## 📚 Next Steps

1. Read [FEATURES.md](FEATURES.md) for all features
2. Check [LocalLLMService_GUIDE.md](LocalLLMService_GUIDE.md) for C# API
3. See [LocalLLMService_Examples.cs](LocalLLMService_Examples.cs) for more examples

---

## 🎉 You're Ready!

**Test Mode:**
```bash
./chatbot --test --stream
```

**C# Integration:**
```csharp
await llm.GetResponseToTextBox(myTextBox, "Hello!");
```

**Enjoy your local AI chatbot!** 🚀
