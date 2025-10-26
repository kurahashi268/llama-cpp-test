# Architecture Documentation

## System Overview

```
┌─────────────────────────────────────────────────────────────┐
│                    C# .NET Desktop Application               │
│  ┌──────────────────────────────────────────────────────┐   │
│  │         LlamaCppLinuxClient                          │   │
│  │  - Process Management                                │   │
│  │  - Shared Memory Access                              │   │
│  │  - Semaphore Synchronization                         │   │
│  └──────────────────────────────────────────────────────┘   │
└──────────────────────┬──────────────────────────────────────┘
                       │ P/Invoke to POSIX APIs
                       │ (shm_open, mmap, sem_wait, sem_post)
                       │
        ┌──────────────▼──────────────┐
        │   POSIX Shared Memory       │
        │  /dev/shm/llama_cpp_*       │
        │                             │
        │  ┌───────────────────────┐  │
        │  │  system_prompt[4096]  │  │
        │  ├───────────────────────┤  │
        │  │  user_prompt[4096]    │  │
        │  ├───────────────────────┤  │
        │  │  response[32768]      │  │
        │  ├───────────────────────┤  │
        │  │  shutdown_requested   │  │
        │  └───────────────────────┘  │
        └──────────────┬──────────────┘
                       │
        ┌──────────────▼──────────────┐
        │   POSIX Semaphores          │
        │                             │
        │  • sem_ready                │
        │  • sem_prompts_written      │
        │  • sem_response_written     │
        └──────────────┬──────────────┘
                       │
┌──────────────────────▼──────────────────────────────────────┐
│              C++ LLama.cpp Chatbot Process                   │
│  ┌──────────────────────────────────────────────────────┐   │
│  │  Main Loop (Shared Memory Mode)                      │   │
│  │  1. Signal ready (sem_ready)                         │   │
│  │  2. Wait for prompts (sem_prompts_written)           │   │
│  │  3. Read from shared memory                          │   │
│  │  4. Process with LLM                                 │   │
│  │  5. Write response to shared memory                  │   │
│  │  6. Signal done (sem_response_written)               │   │
│  │  7. Repeat or shutdown                               │   │
│  └──────────────────────────────────────────────────────┘   │
│                                                              │
│  ┌──────────────────────────────────────────────────────┐   │
│  │  LLM Inference Engine (llama.cpp)                    │   │
│  │  - Model: Phi-3-mini-4k-instruct-q4.gguf            │   │
│  │  - Context: 2048 tokens                              │   │
│  │  - Sampler: temperature=0.7, min_p=0.05             │   │
│  └──────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
```

## Sequence Diagram

### Initialization Phase

```
C# Application          Shared Memory       Semaphores      C++ Process
     |                       |                   |               |
     |--[Start Process]------------------------------------->    |
     |                       |                   |               |
     |                       |                   |<--[Create]----| 
     |                       |<--[Initialize]------------------- |
     |                       |                   |               |
     |                       |                   |<--[Create]----| 
     |                       |                   |               |
     |                       |                   |               |--[Load Model]
     |                       |                   |               |
     |                       |                   |<--[Post sem_ready]
     |                       |                   |               |
```

### Request-Response Cycle

```
C# Application          Shared Memory       Semaphores      C++ Process
     |                       |                   |               |
     |--[Wait]-------------------------------------------------------->|
     |                       |                   |   sem_ready   |    |
     |<-----------------------------------------------------------------|
     |                       |                   |               |
     |--[Write Prompts]----->|                   |               |
     |                       |                   |               |
     |--[Post]------------------------------------>              |
     |                       |    sem_prompts_written            |
     |                       |                   |               |
     |                       |                   |<--[Wait]------|
     |                       |                   |               |
     |                       |<--[Read Prompts]------------------|
     |                       |                   |               |
     |                       |                   |     [Process LLM]
     |                       |                   |               |
     |                       |<--[Write Response]----------------|
     |                       |                   |               |
     |                       |                   |<--[Post sem_response_written]
     |                       |                   |               |
     |--[Wait]---------------------------------------->          |
     |         sem_response_written              |               |
     |<----------------------------------------------------------|
     |                       |                   |               |
     |--[Read Response]----->|                   |               |
     |                       |                   |               |
```

## Component Details

### 1. C# Client (LlamaCppLinuxClient)

**Responsibilities:**
- Start and manage C++ process lifecycle
- Open and map POSIX shared memory
- Open POSIX semaphores
- Synchronize with C++ process using semaphores
- Marshal data to/from shared memory
- Handle errors and cleanup

**Key Methods:**
```csharp
Initialize(string chatbotPath)
SendRequest(string userPrompt, string systemPrompt)
Shutdown()
Dispose()
```

**Dependencies:**
- System.Diagnostics (Process management)
- System.Runtime.InteropServices (P/Invoke)
- POSIX libraries (librt, libpthread, libc)

### 2. Shared Memory Layout

```
┌─────────────────────────────────────┐
│ Offset: 0                           │
│ Size: 4096 bytes                    │
│ Field: system_prompt                │
│ Type: char[4096]                    │
├─────────────────────────────────────┤
│ Offset: 4096                        │
│ Size: 4096 bytes                    │
│ Field: user_prompt                  │
│ Type: char[4096]                    │
├─────────────────────────────────────┤
│ Offset: 8192                        │
│ Size: 32768 bytes                   │
│ Field: response                     │
│ Type: char[32768]                   │
├─────────────────────────────────────┤
│ Offset: 40960                       │
│ Size: 1 byte                        │
│ Field: shutdown_requested           │
│ Type: bool                          │
└─────────────────────────────────────┘
Total: 40961 bytes (~41 KB)
```

### 3. C++ Chatbot

**Modes:**
1. **Test Mode** (`--test` flag)
   - Single request/response
   - Command-line interface
   - Exits after completion

2. **Shared Memory Mode** (default)
   - Continuous operation
   - Background process
   - Multiple requests
   - Graceful shutdown

**Components:**
- Signal handlers (SIGINT, SIGTERM)
- Shared memory initialization
- Semaphore management
- LLM inference engine
- Request processing loop

**Key Functions:**
```cpp
init_shared_memory()
cleanup_shared_resources()
process_llm_request()
signal_handler()
```

### 4. Synchronization Mechanism

**Three Semaphores:**

1. **sem_ready**
   - Posted by: C++
   - Waited by: C#
   - Purpose: C++ signals it's ready for new request

2. **sem_prompts_written**
   - Posted by: C#
   - Waited by: C++
   - Purpose: C# signals prompts are written

3. **sem_response_written**
   - Posted by: C++
   - Waited by: C#
   - Purpose: C++ signals response is ready

**State Machine:**

```
┌─────────────┐
│   READY     │<─────────┐
│  (C++ idle) │          │
└──────┬──────┘          │
       │                 │
       │ sem_ready       │
       ▼                 │
┌─────────────┐          │
│  WAITING    │          │
│ (C# writes) │          │
└──────┬──────┘          │
       │                 │
       │ sem_prompts_    │
       │    written      │
       ▼                 │
┌─────────────┐          │
│ PROCESSING  │          │
│ (C++ works) │          │
└──────┬──────┘          │
       │                 │
       │ sem_response_   │
       │    written      │
       ▼                 │
┌─────────────┐          │
│  READING    │          │
│ (C# reads)  │──────────┘
└─────────────┘
```

## Data Flow

### Write Path (C# → C++)

```
User Input (C#)
    │
    ▼
Convert to UTF-8 byte[]
    │
    ▼
Copy to byte buffer[4096]
    │
    ▼
Marshal.Copy to shared memory
    │
    ▼
Post semaphore
    │
    ▼
C++ reads from shared memory
    │
    ▼
Tokenize with llama.cpp
    │
    ▼
LLM Processing
```

### Read Path (C++ → C#)

```
LLM generates tokens
    │
    ▼
Convert tokens to text
    │
    ▼
Write to shared memory (char*)
    │
    ▼
Post semaphore
    │
    ▼
C# copies from shared memory
    │
    ▼
Marshal.Copy to byte[]
    │
    ▼
Convert to UTF-8 string
    │
    ▼
Return to user
```

## Error Handling

### C++ Side

```cpp
try {
    init_shared_memory()
} catch {
    log error
    cleanup
    exit
}

signal(SIGINT, signal_handler)   // Ctrl+C
signal(SIGTERM, signal_handler)  // Kill signal

while (true) {
    if (shutdown_requested) break;
    // process request
}

cleanup_shared_resources()
```

### C# Side

```csharp
try {
    Initialize()
    SendRequest()
} catch (Exception ex) {
    log error
} finally {
    Dispose()  // Always cleanup
}

// Automatic cleanup with 'using' statement
using var client = new LlamaCppLinuxClient();
```

## Performance Characteristics

| Operation                | Time           | Notes                    |
|-------------------------|----------------|--------------------------|
| Initial startup         | 2-5 seconds    | Model loading            |
| First request           | 1-3 seconds    | Context setup            |
| Subsequent requests     | 0.1-2 seconds  | Depends on length        |
| Semaphore operations    | <1 ms          | Kernel call              |
| Shared memory access    | <1 μs          | Direct memory access     |
| Context switch          | ~10 μs         | Process scheduling       |

## Scalability

**Current Design:**
- Single client → Single C++ process
- Sequential request processing
- No request queuing

**Possible Extensions:**

1. **Multiple Clients:**
   ```
   C# App 1 ──┐
              ├──> Queue ──> C++ Process
   C# App 2 ──┘
   ```

2. **Multiple Workers:**
   ```
   C# App ──> Load Balancer ──┬──> C++ Process 1
                               ├──> C++ Process 2
                               └──> C++ Process 3
   ```

3. **Request Queue:**
   ```
   Requests ──> Circular Buffer ──> C++ Processor
   ```

## Security Considerations

1. **Shared Memory Permissions:** 0666 (rw-rw-rw-)
   - Consider restricting to 0600 for production

2. **No Authentication:** Any process can access
   - Add auth token in shared memory

3. **Buffer Overflow Protection:** Fixed-size buffers
   - Always null-terminate strings

4. **Process Isolation:** Separate processes
   - Crash in one doesn't affect other

## Resource Management

**Cleanup Order:**

1. Signal shutdown via shared memory flag
2. Wait for C++ process exit (timeout: 5s)
3. Unmap shared memory
4. Close shared memory file descriptor
5. Close semaphores
6. Kill process if still running
7. Unlink shared memory and semaphores

**POSIX Resources:**
- `/dev/shm/llama_cpp_shared_mem` - Shared memory
- `/dev/shm/sem.llama_cpp_sem_*` - Semaphores (3 total)

These persist until explicitly unlinked!

## Testing Strategy

1. **Unit Tests:**
   - Shared memory creation/deletion
   - Semaphore operations
   - Data marshalling

2. **Integration Tests:**
   - Full request-response cycle
   - Multiple consecutive requests
   - Graceful shutdown
   - Error recovery

3. **Stress Tests:**
   - Rapid consecutive requests
   - Large prompts (near buffer limits)
   - Long-running operation
   - Resource leaks

4. **Failure Tests:**
   - C++ process crash
   - Semaphore timeout
   - Shared memory corruption
   - Out of memory

---

This architecture provides a robust, performant foundation for C#/.NET applications to leverage local LLM capabilities through a C++ backend.

