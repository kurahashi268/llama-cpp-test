# LocalLLMService - Complete Usage Guide

## 📋 Overview

`LocalLLMService` is a simple, easy-to-use C# class for integrating the local LLM into your .NET applications. It handles all IPC communication automatically and provides clean async/await APIs.

---

## 🚀 Quick Start

### 1. Add the Class to Your Project

Copy `LocalLLMService.cs` to your project.

### 2. Basic Usage (3 Lines!)

```csharp
using var llm = new LocalLLMService();
await llm.InitializeAsync();
string response = await llm.GetResponseAsync("What is C++?");
```

That's it! ✅

---

## 📚 Complete API Reference

### Initialization

```csharp
// Default configuration
var llm = new LocalLLMService();

// Custom configuration
var llm = new LocalLLMService(new LLMServiceConfig
{
    ChatbotPath = "./build/chatbot",
    DefaultSystemPrompt = "You are my best assistance.",
    InitializationDelayMs = 3000
});

// Initialize (required before use)
await llm.InitializeAsync();  // Async
// or
llm.Initialize();             // Sync
```

### Normal Mode (Full Response at Once)

```csharp
// Simple
string response = await llm.GetResponseAsync("Your question");

// With custom system prompt
string response = await llm.GetResponseAsync(
    "Your question",
    "You are a helpful coding assistant"
);

// Synchronous version
string response = llm.GetResponse("Your question");
```

### Streaming Mode (Real-Time Updates)

```csharp
// Subscribe to updates
llm.OnStreamUpdate += (sender, e) =>
{
    Console.WriteLine(e.Text);          // Current text
    Console.WriteLine(e.TokensGenerated); // Progress
    Console.WriteLine(e.IsComplete);    // Done?
};

// Get response with streaming
string response = await llm.GetResponseStreamingAsync("Your question");
```

### Easy TextBox Integration

#### WinForms:
```csharp
// Automatically updates TextBox in real-time!
await llm.GetResponseToTextBox(myTextBox, "Your question");
```

#### WPF:
```csharp
// Automatically updates TextBox in real-time!
await llm.GetResponseToTextBoxWPF(myTextBox, "Your question");
```

---

## 💡 Usage Examples

### Example 1: Simple Console App

```csharp
using LlamaCpp.Service;

class Program
{
    static async Task Main()
    {
        using var llm = new LocalLLMService();
        await llm.InitializeAsync();

        while (true)
        {
            Console.Write("\nYou: ");
            string question = Console.ReadLine();

            if (question == "exit") break;

            Console.Write("Assistant: ");
            string answer = await llm.GetResponseAsync(question);
            Console.WriteLine(answer);
        }
    }
}
```

### Example 2: WinForms with Streaming

```csharp
public class ChatForm : Form
{
    private LocalLLMService _llm;
    private TextBox _inputBox;
    private TextBox _outputBox;
    private Button _sendButton;

    public ChatForm()
    {
        // Create UI controls
        _inputBox = new TextBox { /* ... */ };
        _outputBox = new TextBox { ReadOnly = true, /* ... */ };
        _sendButton = new Button { Text = "Send" };
        _sendButton.Click += SendButton_Click;

        // Initialize LLM
        _llm = new LocalLLMService();
        _llm.InitializeAsync();
    }

    private async void SendButton_Click(object sender, EventArgs e)
    {
        _sendButton.Enabled = false;

        // This handles everything automatically!
        await _llm.GetResponseToTextBox(_outputBox, _inputBox.Text);

        _sendButton.Enabled = true;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _llm?.Dispose();
        base.OnFormClosing(e);
    }
}
```

### Example 3: WPF with Progress

```csharp
public partial class MainWindow : Window
{
    private LocalLLMService _llm;

    public MainWindow()
    {
        InitializeComponent();

        _llm = new LocalLLMService();
        _llm.InitializeAsync();

        // Show progress
        _llm.OnStreamUpdate += (s, e) =>
        {
            Dispatcher.Invoke(() =>
            {
                ResponseTextBox.Text = e.Text;
                ProgressLabel.Text = $"Tokens: {e.TokensGenerated}";

                if (e.IsComplete)
                {
                    StatusLabel.Text = "Complete!";
                }
            });
        };
    }

    private async void SendButton_Click(object sender, RoutedEventArgs e)
    {
        StatusLabel.Text = "Generating...";
        await _llm.GetResponseStreamingAsync(PromptTextBox.Text);
    }
}
```

### Example 4: Custom Event Handling

```csharp
using var llm = new LocalLLMService();
await llm.InitializeAsync();

// Custom progress tracking
int wordCount = 0;
llm.OnStreamUpdate += (sender, e) =>
{
    wordCount = e.Text.Split(' ').Length;
    myProgressBar.Value = e.TokensGenerated;
    myWordCountLabel.Text = $"Words: {wordCount}";

    if (e.IsComplete)
    {
        myStatusLabel.Text = $"Done! ({e.TokensGenerated} tokens, {wordCount} words)";
    }
};

await llm.GetResponseStreamingAsync("Write a story");
```

### Example 5: Multiple Sequential Requests

```csharp
using var llm = new LocalLLMService();
await llm.InitializeAsync();

// Process multiple questions
var questions = new[] {
    "What is C++?",
    "Explain pointers",
    "What is RAII?"
};

foreach (var q in questions)
{
    Console.WriteLine($"\nQ: {q}");
    string answer = await llm.GetResponseAsync(q);
    Console.WriteLine($"A: {answer}");
}
```

---

## 🎯 API Methods Summary

| Method | Mode | Returns | Use Case |
|--------|------|---------|----------|
| `GetResponseAsync()` | Normal | Task<string> | Get full response |
| `GetResponseStreamingAsync()` | Streaming | Task<string> | Get with events |
| `GetResponseToTextBox()` | Streaming | Task<string> | WinForms auto-update |
| `GetResponseToTextBoxWPF()` | Streaming | Task<string> | WPF auto-update |

---

## ⚙️ Configuration Options

```csharp
var config = new LLMServiceConfig
{
    // Path to C++ chatbot executable
    ChatbotPath = "./build/chatbot",

    // Default system prompt (used if not specified)
    DefaultSystemPrompt = "You are my best assistance.",

    // Delay to wait for C++ initialization (ms)
    InitializationDelayMs = 3000
};

var llm = new LocalLLMService(config);
```

---

## 🎨 TextBox Streaming Features

### WinForms:
```csharp
// Single line - handles everything!
await llm.GetResponseToTextBox(myTextBox, userInput);

// What it does automatically:
// - Updates TextBox in real-time
// - Handles UI thread marshalling
// - Shows streaming text token by token
// - Returns complete text when done
```

### WPF:
```csharp
// Single line - handles everything!
await llm.GetResponseToTextBoxWPF(myTextBox, userInput);

// Uses Dispatcher.Invoke automatically
```

---

## 🔥 Best Practices

### 1. Initialize Once, Use Many Times

```csharp
// ✅ Good - Reuse service
private LocalLLMService _llm;

public async void Initialize()
{
    _llm = new LocalLLMService();
    await _llm.InitializeAsync();
}

public async Task<string> Ask(string question)
{
    return await _llm.GetResponseAsync(question);
}
```

```csharp
// ❌ Bad - Creating new service each time is slow
public async Task<string> Ask(string question)
{
    using var llm = new LocalLLMService();
    await llm.InitializeAsync();  // Slow!
    return await llm.GetResponseAsync(question);
}
```

### 2. Always Dispose

```csharp
// ✅ Good - Using statement
using var llm = new LocalLLMService();

// ✅ Good - Manual disposal
var llm = new LocalLLMService();
try
{
    // use llm
}
finally
{
    llm.Dispose();
}
```

### 3. Handle Errors

```csharp
try
{
    string response = await llm.GetResponseAsync(question);
}
catch (InvalidOperationException ex)
{
    // Service not initialized
    MessageBox.Show("Please initialize first");
}
catch (Exception ex)
{
    // Other errors
    MessageBox.Show($"Error: {ex.Message}");
}
```

### 4. Disable UI During Generation

```csharp
private async void SendButton_Click(object sender, EventArgs e)
{
    SendButton.Enabled = false;  // Prevent double-click

    try
    {
        await llm.GetResponseAsync(inputBox.Text);
    }
    finally
    {
        SendButton.Enabled = true;  // Re-enable
    }
}
```

---

## 📊 Streaming vs Normal Mode

### Normal Mode:
- User clicks send
- UI freezes (or shows "loading")
- **Wait 10-30 seconds**
- Entire response appears
- Good for: background processing, simple apps

### Streaming Mode:
- User clicks send
- First tokens appear in ~1 second
- **Text streams in real-time**
- Feels responsive and professional
- Good for: chat UIs, user-facing apps

---

## 🐛 Troubleshooting

### "Service not initialized"
```csharp
// Call Initialize() or InitializeAsync() first!
await llm.InitializeAsync();
```

### "Failed to start C++ chatbot process"
```csharp
// Check chatbot path
var llm = new LocalLLMService(new LLMServiceConfig
{
    ChatbotPath = "./build/chatbot"  // Verify this path
});
```

### TextBox not updating
```csharp
// Make sure you're using the right method:
// WinForms: GetResponseToTextBox()
// WPF: GetResponseToTextBoxWPF()
```

### Slow initialization
```csharp
// Normal - model loading takes 2-3 seconds
// Show loading message to user during InitializeAsync()
StatusLabel.Text = "Loading AI model...";
await llm.InitializeAsync();
StatusLabel.Text = "Ready!";
```

---

## 🎉 Summary

**LocalLLMService makes LLM integration incredibly easy:**

✅ Simple async/await API  
✅ Automatic TextBox streaming  
✅ Event-driven updates  
✅ WinForms & WPF support  
✅ Thread-safe  
✅ Error handling built-in  
✅ Production-ready  

### Minimal Example:
```csharp
// 3 lines to get started!
using var llm = new LocalLLMService();
await llm.InitializeAsync();
await llm.GetResponseToTextBox(myTextBox, "Hello!");
```

**That's it! Your TextBox now shows streaming LLM responses!** 🚀

