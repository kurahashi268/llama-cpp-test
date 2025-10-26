# LocalLLMService - Quick Reference Card

## 🚀 Simplest Usage (3 Lines!)

```csharp
using var llm = new LocalLLMService();
await llm.InitializeAsync();
string answer = await llm.GetResponseAsync("What is C++?");
```

---

## 📋 Common Scenarios

### Scenario 1: Get Full Response

```csharp
string response = await llm.GetResponseAsync("Your question");
```

### Scenario 2: Stream to TextBox (WinForms)

```csharp
await llm.GetResponseToTextBox(myTextBox, "Your question");
```

### Scenario 3: Stream to TextBox (WPF)

```csharp
await llm.GetResponseToTextBoxWPF(myTextBox, "Your question");
```

### Scenario 4: Custom Streaming Events

```csharp
llm.OnStreamUpdate += (s, e) => {
    myLabel.Text = $"{e.TokensGenerated} tokens";
    myTextBox.Text = e.Text;
};
await llm.GetResponseStreamingAsync("Your question");
```

---

## 🎨 Full WinForms Example

```csharp
public class ChatForm : Form
{
    private LocalLLMService _llm;
    private TextBox _input, _output;
    private Button _send;

    public ChatForm()
    {
        // Setup UI
        _input = new TextBox();
        _output = new TextBox { ReadOnly = true };
        _send = new Button { Text = "Send" };
        _send.Click += async (s, e) => {
            _send.Enabled = false;
            await _llm.GetResponseToTextBox(_output, _input.Text);
            _send.Enabled = true;
        };

        // Initialize LLM
        _llm = new LocalLLMService();
        Task.Run(async () => await _llm.InitializeAsync());

        // Add controls
        this.Controls.AddRange(new[] { _input, _output, _send });
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _llm?.Dispose();
    }
}
```

---

## 🎯 Method Cheat Sheet

| What You Want | Method to Use |
|---------------|---------------|
| Full response, no streaming | `GetResponseAsync()` |
| Stream with custom handling | `GetResponseStreamingAsync()` + events |
| Stream directly to TextBox (WinForms) | `GetResponseToTextBox()` |
| Stream directly to TextBox (WPF) | `GetResponseToTextBoxWPF()` |

---

## ⚡ Mode Comparison

### Normal Mode:
```csharp
string response = await llm.GetResponseAsync("Question");
// Waits for complete response, then returns all at once
```

### Streaming Mode:
```csharp
llm.OnStreamUpdate += (s, e) => Console.Write(e.Text);
string response = await llm.GetResponseStreamingAsync("Question");
// Updates in real-time as tokens generate
```

---

## 🔧 Configuration

```csharp
var llm = new LocalLLMService(new LLMServiceConfig
{
    ChatbotPath = "./build/chatbot",
    DefaultSystemPrompt = "You are helpful",
    InitializationDelayMs = 3000
});
```

---

## 💡 Pro Tips

1. **Initialize once, use many times** - Don't create new service for each request
2. **Use `using` statement** - Ensures proper cleanup
3. **Disable buttons during generation** - Prevent double-clicks
4. **Show loading message** - Initialization takes 2-3 seconds

---

## 🐛 Common Mistakes

### ❌ Wrong:
```csharp
// Creating new service each time (SLOW!)
using var llm = new LocalLLMService();
await llm.InitializeAsync();  // Takes 3 seconds!
```

### ✅ Right:
```csharp
// Initialize once in constructor/startup
private LocalLLMService _llm;
_llm = new LocalLLMService();
await _llm.InitializeAsync();  // Only once!
```

---

## 📦 What You Need

1. ✅ `LocalLLMService.cs` - The main class
2. ✅ C++ chatbot running: `./build/chatbot`
3. ✅ .NET Framework 4.7.2+ or .NET 6.0+
4. ✅ Linux OS (for POSIX APIs)

---

## 🎉 That's It!

**Three lines to get started:**
```csharp
using var llm = new LocalLLMService();
await llm.InitializeAsync();
await llm.GetResponseToTextBox(myTextBox, "Hello!");
```

**Your TextBox now shows streaming AI responses!** ✨

