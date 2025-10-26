# Streaming IPC Guide - C++ to C# Real-Time Responses

## ✅ Yes! Streaming via IPC is now implemented!

Your C++ LLM process can now send **partial responses in real-time** to your C# UI application via shared memory IPC.

---

## 🎯 How It Works

### Architecture

```
C++ Process                    Shared Memory                C# UI Application
     |                              |                              |
     |  1. Generate token          |                              |
     |  2. Append to buffer        |                              |
     |  3. Update counter     ---> | update_counter++            |
     |  4. Post semaphore          | tokens_generated++           |
     |------------------------------> sem_chunk_ready             |
     |                              |                              |
     |                              | <---- Read latest text ------| 5. Wait on semaphore
     |                              |       Display in UI          | 6. Update UI
     |                              |                              |
     |  Repeat for each token...   |                              |
     |                              |                              |
     |  Final token generated      |                              |
     |  Set generation_complete--> | generation_complete = true  |
     |  Post semaphore        ---> | sem_chunk_ready             |
     |                              |                              | 7. Final update
     |  Post completion       ---> | sem_response_written        |
     |                              | <--------------------------- | 8. Done
```

---

## 📊 Shared Memory Structure (Updated)

```c
struct SharedMemoryData {
    char system_prompt[4096];          // Offset: 0
    char user_prompt[4096];            // Offset: 4096
    char response[32768];              // Offset: 8192 (continuously updated)
    bool shutdown_requested;           // Offset: 40960
    
    // NEW - Streaming support
    bool stream_mode;                  // Offset: 40961 (set by C# to enable streaming)
    int update_counter;                // Offset: 40962 (increments with each token)
    bool generation_complete;          // Offset: 40966 (true when done)
    int tokens_generated;              // Offset: 40967 (count of tokens so far)
};
```

---

## 🔧 Semaphores

### Existing:
- **sem_ready**: C++ signals when ready for requests
- **sem_prompts_written**: C# signals when prompts are written
- **sem_response_written**: C++ signals when generation is complete (final)

### NEW:
- **sem_chunk_ready**: C++ signals after each token (streaming mode only)

---

## 💻 C# Implementation

### 1. Normal Mode (Full Response)

```csharp
using var client = new LlamaCppStreamingClient();
client.Initialize("./build/chatbot");

// Get full response at once (like before)
string response = client.SendRequest("What is C++?");
Console.WriteLine(response);
```

### 2. Streaming Mode (Real-Time Updates)

```csharp
using var client = new LlamaCppStreamingClient();
client.Initialize("./build/chatbot");

// Subscribe to streaming updates
client.OnStreamUpdate += (sender, e) =>
{
    // Update your UI textbox/label here!
    myTextBox.Text = e.PartialResponse;
    statusLabel.Text = $"Tokens: {e.TokensGenerated}";
    
    if (e.IsComplete)
    {
        statusLabel.Text = "Complete!";
    }
};

// Send request with streaming
string finalResponse = client.SendStreamingRequest(
    "Explain C++ pointers in detail",
    "You are a helpful tutor"
);
```

---

## 🎨 WPF/WinForms UI Example

### WPF Example

```csharp
public partial class MainWindow : Window
{
    private LlamaCppStreamingClient _llmClient;
    
    public MainWindow()
    {
        InitializeComponent();
        
        _llmClient = new LlamaCppStreamingClient();
        _llmClient.Initialize("./build/chatbot");
        
        // Handle streaming updates on UI thread
        _llmClient.OnStreamUpdate += (sender, e) =>
        {
            Dispatcher.Invoke(() =>
            {
                // Update UI elements
                ResponseTextBox.Text = e.PartialResponse;
                ProgressLabel.Text = $"Generated {e.TokensGenerated} tokens...";
                
                if (e.IsComplete)
                {
                    ProgressLabel.Text = $"Complete! ({e.TokensGenerated} tokens)";
                    SendButton.IsEnabled = true;
                }
            });
        };
    }
    
    private async void SendButton_Click(object sender, RoutedEventArgs e)
    {
        SendButton.IsEnabled = false;
        ProgressLabel.Text = "Generating...";
        
        await Task.Run(() =>
        {
            _llmClient.SendStreamingRequest(
                UserInputTextBox.Text,
                "You are my best assistance."
            );
        });
    }
}
```

---

## 🚀 How to Use

### Step 1: Start C++ Process

```bash
./build/chatbot
```

The C++ process:
- Loads the model
- Initializes shared memory with new streaming fields
- Waits for requests from C#

### Step 2: Run C# Application

```csharp
// In your C# app
using var client = new LlamaCppStreamingClient();
client.Initialize("./build/chatbot");

// Enable streaming in your UI
client.OnStreamUpdate += UpdateUI;

// Send streaming request
client.SendStreamingRequest("Your question here");
```

### Step 3: Watch Real-Time Streaming!

The C# UI will receive updates **after every token** generated by the LLM!

---

## 📝 Protocol Flow

### Streaming Mode:

1. **C# → Shared Memory**: Write prompts, set `stream_mode = true`
2. **C# → C++**: Signal via `sem_prompts_written`
3. **C++**: Starts generating tokens
4. **For each token**:
   - C++: Append token to response buffer
   - C++: Increment `update_counter`
   - C++: Update `tokens_generated`
   - C++: Signal `sem_chunk_ready`
   - C#: Wait on `sem_chunk_ready`
   - C#: Read latest response from shared memory
   - C#: Update UI with partial response
5. **When complete**:
   - C++: Set `generation_complete = true`
   - C++: Signal `sem_chunk_ready` (last update)
   - C++: Signal `sem_response_written` (final)
   - C#: Display final response

### Normal Mode:

1. **C# → Shared Memory**: Write prompts, set `stream_mode = false`
2. **C# → C++**: Signal via `sem_prompts_written`
3. **C++**: Generates entire response (internal)
4. **C++**: Writes complete response to shared memory once
5. **C++**: Signals `sem_response_written`
6. **C#**: Reads complete response

---

## ⚡ Performance Characteristics

| Mode | Latency to First Token | UI Responsiveness | Total Time |
|------|------------------------|-------------------|------------|
| Normal | High (waits for all) | Poor (UI frozen) | Same |
| Streaming | Low (~100ms) | Excellent (real-time) | Same |

**Streaming doesn't make generation faster, but makes the UI feel much more responsive!**

---

## 🎯 Benefits of Streaming

✅ **Better UX** - Users see progress immediately  
✅ **Responsive UI** - No frozen interface  
✅ **Progress indication** - Know how many tokens generated  
✅ **Cancelable** - Could add cancellation support  
✅ **Like ChatGPT** - Professional streaming experience  

---

## 🔍 Debugging Tips

### Check if streaming is enabled:

```bash
# In C++ output, you should see:
Stream Mode: Enabled
Processing with streaming...
```

### Monitor semaphore posts:

```csharp
// Add logging in C#
client.OnStreamUpdate += (sender, e) =>
{
    Console.WriteLine($"[Update {e.TokensGenerated}] Latest: {e.PartialResponse.Length} chars");
};
```

### Check shared memory:

```bash
# View shared memory
ls -lh /dev/shm/llama_cpp_shared_mem

# View semaphores
ls -la /dev/shm/sem.llama_cpp_*
```

---

## ✅ Complete Example

See `CSharpStreamingExample.cs` for a full working example with:
- Streaming mode implementation
- Event-based updates
- Both normal and streaming modes
- Error handling
- Proper cleanup

---

## 🎉 Summary

You now have **real-time streaming** from C++ to C# via IPC!

- ✅ Token-by-token updates
- ✅ Shared memory communication
- ✅ Semaphore synchronization
- ✅ Event-driven UI updates
- ✅ Progress tracking
- ✅ Backward compatible (normal mode still works)

Your C# UI can now display LLM responses **in real-time**, just like ChatGPT! 🚀

