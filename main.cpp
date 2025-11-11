/*
 * Cross-Platform LLM Chatbot with Shared Memory IPC
 * 
 * This application provides a chatbot interface powered by llama.cpp with support
 * for inter-process communication via shared memory.
 * 
 * Platform Support:
 * - Windows: Uses Win32 API (CreateFileMapping, CreateSemaphore)
 * - Linux:   Uses POSIX API (shm_open, sem_open)
 * 
 * Operating Modes:
 * 1. Test Mode (--test): Interactive chatbot for direct testing
 * 2. IPC Mode: Background service for C# integration via shared memory
 * 
 * Author: Cross-platform IPC implementation
 * License: MIT
 */

#include <iostream>
#include <string>
#include <vector>
#include <cstring>
#include <cstdio>
#include <cstdlib>
#include <climits>
#include <locale>
#include <atomic>

// Platform-specific includes
#ifdef _WIN32
    #include <windows.h>
    #include <io.h>
#else
#include <sys/mman.h>
#include <sys/stat.h>
#include <fcntl.h>
#include <unistd.h>
#include <semaphore.h>
#include <signal.h>
#endif

#include "llama.h"

// ============================================================================
// CONSTANTS
// ============================================================================

namespace Config {
    constexpr size_t SYSTEM_PROMPT_SIZE = 4096;
    constexpr size_t USER_PROMPT_SIZE = 4096;
    constexpr size_t RESPONSE_SIZE = 32768;
    constexpr size_t TOKEN_BUFFER_SIZE = 256;
    
    constexpr int DEFAULT_CONTEXT_SIZE = 2048;
    constexpr int DEFAULT_BATCH_SIZE = 2048;
    constexpr int DEFAULT_MAX_TOKENS = 0;
    constexpr int DEFAULT_GPU_LAYERS = 0;  // Reference value; actual behavior uses auto-detection (see LLMEngine::initialize)
    constexpr int DEFAULT_CHUNK_TOKENS = 8; // stream chunks every N tokens
    
    constexpr float SAMPLER_MIN_P = 0.05f;
    constexpr float SAMPLER_TEMPERATURE = 0.7f;
    
    const std::string MODEL_PATH = "models/llama.gguf";
    const std::string DEFAULT_SYSTEM_PROMPT = "You are my best assistance.";
    
#ifdef _WIN32
    const char* SHM_NAME = "Local\\llama_cpp_shared_mem";
    const char* SEM_READY = "Local\\llama_cpp_sem_ready";
    const char* SEM_PROMPTS_WRITTEN = "Local\\llama_cpp_sem_prompts_written";
    const char* SEM_RESPONSE_WRITTEN = "Local\\llama_cpp_sem_response_written";
    const char* SEM_CHUNK_READY = "Local\\llama_cpp_sem_chunk_ready";
#else
    const char* SHM_NAME = "/llama_cpp_shared_mem";
    const char* SEM_READY = "/llama_cpp_sem_ready";
    const char* SEM_PROMPTS_WRITTEN = "/llama_cpp_sem_prompts_written";
    const char* SEM_RESPONSE_WRITTEN = "/llama_cpp_sem_response_written";
    const char* SEM_CHUNK_READY = "/llama_cpp_sem_chunk_ready";
#endif
}

// ============================================================================
// DATA STRUCTURES
// ============================================================================

/**
 * Shared memory structure for IPC communication
 * Used to exchange prompts and responses between processes
 * 
 * CRITICAL: This struct must use tight packing to match C# expectations.
 * C# uses hardcoded offsets without padding.
 * 
 * Memory Layout (must match C# exactly):
 * - system_prompt:         offset 0      (4096 bytes)
 * - user_prompt:           offset 4096   (4096 bytes)
 * - response:              offset 8192   (32768 bytes)
 * - shutdown_requested:    offset 40960  (1 byte)
 * - stream_mode:           offset 40961  (1 byte)
 * - update_counter:        offset 40962  (4 bytes)
 * - generation_complete:   offset 40966  (1 byte)
 * - tokens_generated:      offset 40967  (4 bytes)
 * Total size: 40971 bytes minimum
 */
#pragma pack(push, 1)  // Force tight packing without padding
struct SharedMemoryData {
    char system_prompt[Config::SYSTEM_PROMPT_SIZE];    // offset 0
    char user_prompt[Config::USER_PROMPT_SIZE];        // offset 4096
    char response[Config::RESPONSE_SIZE];              // offset 8192
    bool shutdown_requested;                            // offset 40960
    bool stream_mode;                                   // offset 40961
    int update_counter;                                 // offset 40962
    bool generation_complete;                           // offset 40966
    int tokens_generated;                               // offset 40967
};
#pragma pack(pop)  // Restore default packing

// ============================================================================
// PLATFORM ABSTRACTION LAYER
// ============================================================================

/**
 * Platform-agnostic semaphore and shared memory handles
 */
class IPCResources {
public:
#ifdef _WIN32
    HANDLE hMapFile = nullptr;
    HANDLE sem_ready = nullptr;
    HANDLE sem_prompts_written = nullptr;
    HANDLE sem_response_written = nullptr;
    HANDLE sem_chunk_ready = nullptr;
#else
    int shm_fd = -1;
    sem_t* sem_ready = nullptr;
    sem_t* sem_prompts_written = nullptr;
    sem_t* sem_response_written = nullptr;
    sem_t* sem_chunk_ready = nullptr;
#endif
    
    SharedMemoryData* shared_mem = nullptr;
    
    /**
     * Post (signal) a semaphore - platform independent
     */
    void semaphore_post(
#ifdef _WIN32
        HANDLE sem
#else
        sem_t* sem
#endif
    ) {
#ifdef _WIN32
        if (sem) ReleaseSemaphore(sem, 1, NULL);
#else
        if (sem) sem_post(sem);
#endif
    }
    
    /**
     * Wait on a semaphore - platform independent
     */
    void semaphore_wait(
#ifdef _WIN32
        HANDLE sem
#else
        sem_t* sem
#endif
    ) {
#ifdef _WIN32
        if (sem) WaitForSingleObject(sem, INFINITE);
#else
        if (sem) sem_wait(sem);
#endif
    }
};

// Global IPC resources
static IPCResources g_ipc;

// ============================================================================
// CLEANUP & SIGNAL HANDLING
// ============================================================================

/**
 * Clean up all shared memory and synchronization resources
 */
void cleanup_ipc_resources() {
#ifdef _WIN32
    if (g_ipc.shared_mem) {
        UnmapViewOfFile(g_ipc.shared_mem);
        g_ipc.shared_mem = nullptr;
    }
    
    if (g_ipc.hMapFile) {
        CloseHandle(g_ipc.hMapFile);
        g_ipc.hMapFile = nullptr;
    }
    
    if (g_ipc.sem_ready) {
        CloseHandle(g_ipc.sem_ready);
        g_ipc.sem_ready = nullptr;
    }
    
    if (g_ipc.sem_prompts_written) {
        CloseHandle(g_ipc.sem_prompts_written);
        g_ipc.sem_prompts_written = nullptr;
    }
    
    if (g_ipc.sem_response_written) {
        CloseHandle(g_ipc.sem_response_written);
        g_ipc.sem_response_written = nullptr;
    }
    
    if (g_ipc.sem_chunk_ready) {
        CloseHandle(g_ipc.sem_chunk_ready);
        g_ipc.sem_chunk_ready = nullptr;
    }
#else
    if (g_ipc.shared_mem) {
        munmap(g_ipc.shared_mem, sizeof(SharedMemoryData));
        g_ipc.shared_mem = nullptr;
    }
    
    if (g_ipc.shm_fd != -1) {
        close(g_ipc.shm_fd);
        shm_unlink(Config::SHM_NAME);
        g_ipc.shm_fd = -1;
    }
    
    if (g_ipc.sem_ready) {
        sem_close(g_ipc.sem_ready);
        sem_unlink(Config::SEM_READY);
        g_ipc.sem_ready = nullptr;
    }
    
    if (g_ipc.sem_prompts_written) {
        sem_close(g_ipc.sem_prompts_written);
        sem_unlink(Config::SEM_PROMPTS_WRITTEN);
        g_ipc.sem_prompts_written = nullptr;
    }
    
    if (g_ipc.sem_response_written) {
        sem_close(g_ipc.sem_response_written);
        sem_unlink(Config::SEM_RESPONSE_WRITTEN);
        g_ipc.sem_response_written = nullptr;
    }
    
    if (g_ipc.sem_chunk_ready) {
        sem_close(g_ipc.sem_chunk_ready);
        sem_unlink(Config::SEM_CHUNK_READY);
        g_ipc.sem_chunk_ready = nullptr;
    }
#endif
}

/**
 * Platform-specific signal handlers for graceful shutdown
 */
#ifdef _WIN32
BOOL WINAPI console_ctrl_handler(DWORD ctrl_type) {
    switch (ctrl_type) {
        case CTRL_C_EVENT:
        case CTRL_BREAK_EVENT:
        case CTRL_CLOSE_EVENT:
            std::cout << "\nReceived shutdown signal, cleaning up..." << std::endl;
            cleanup_ipc_resources();
            return TRUE;
        default:
            return FALSE;
    }
}
#else
[[noreturn]] void signal_handler(int signum) {
    std::cout << "\nReceived signal " << signum << ", shutting down..." << std::endl;
    cleanup_ipc_resources();
    exit(signum);
}
#endif

// ============================================================================
// IPC INITIALIZATION
// ============================================================================

/**
 * Initialize shared memory and synchronization primitives
 * @return true on success, false on failure
 */
bool initialize_ipc() {
#ifdef _WIN32
    // Create shared memory
    g_ipc.hMapFile = CreateFileMappingA(
        INVALID_HANDLE_VALUE,
        NULL,
        PAGE_READWRITE,
        0,
        sizeof(SharedMemoryData),
        Config::SHM_NAME
    );
    
    if (!g_ipc.hMapFile) {
        std::cerr << "Error: Failed to create shared memory (Error: " 
                  << GetLastError() << ")" << std::endl;
        return false;
    }
    
    // Map shared memory into process address space
    g_ipc.shared_mem = static_cast<SharedMemoryData*>(MapViewOfFile(
        g_ipc.hMapFile,
        FILE_MAP_ALL_ACCESS,
        0,
        0,
        sizeof(SharedMemoryData)
    ));
    
    if (!g_ipc.shared_mem) {
        std::cerr << "Error: Failed to map shared memory (Error: " 
                  << GetLastError() << ")" << std::endl;
        CloseHandle(g_ipc.hMapFile);
        g_ipc.hMapFile = nullptr;
        return false;
    }
    
    // Initialize shared memory contents
    memset(g_ipc.shared_mem, 0, sizeof(SharedMemoryData));
    g_ipc.shared_mem->shutdown_requested = false;
    
    // Create semaphores
    g_ipc.sem_ready = CreateSemaphoreA(NULL, 0, 1, Config::SEM_READY);
    g_ipc.sem_prompts_written = CreateSemaphoreA(NULL, 0, 1, Config::SEM_PROMPTS_WRITTEN);
    g_ipc.sem_response_written = CreateSemaphoreA(NULL, 0, 1, Config::SEM_RESPONSE_WRITTEN);
    g_ipc.sem_chunk_ready = CreateSemaphoreA(NULL, 0, LONG_MAX, Config::SEM_CHUNK_READY);
    
    if (!g_ipc.sem_ready || !g_ipc.sem_prompts_written || 
        !g_ipc.sem_response_written || !g_ipc.sem_chunk_ready) {
        std::cerr << "Error: Failed to create semaphores (Error: " 
                  << GetLastError() << ")" << std::endl;
        // Cleanup shared memory on failure
        if (g_ipc.shared_mem) {
            UnmapViewOfFile(g_ipc.shared_mem);
            g_ipc.shared_mem = nullptr;
        }
        if (g_ipc.hMapFile) {
            CloseHandle(g_ipc.hMapFile);
            g_ipc.hMapFile = nullptr;
        }
        return false;
    }
    
#else
    // Create shared memory
    g_ipc.shm_fd = shm_open(Config::SHM_NAME, O_CREAT | O_RDWR, 0666);
    if (g_ipc.shm_fd == -1) {
        std::cerr << "Error: Failed to create shared memory" << std::endl;
        return false;
    }
    
    // Set the size of shared memory
    if (ftruncate(g_ipc.shm_fd, sizeof(SharedMemoryData)) == -1) {
        std::cerr << "Error: Failed to set shared memory size" << std::endl;
        close(g_ipc.shm_fd);
        shm_unlink(Config::SHM_NAME);
        return false;
    }
    
    // Map shared memory into process address space
    g_ipc.shared_mem = static_cast<SharedMemoryData*>(mmap(
        NULL, 
        sizeof(SharedMemoryData), 
        PROT_READ | PROT_WRITE, 
        MAP_SHARED, 
        g_ipc.shm_fd, 
        0
    ));
    
    if (g_ipc.shared_mem == MAP_FAILED) {
        std::cerr << "Error: Failed to map shared memory" << std::endl;
        close(g_ipc.shm_fd);
        shm_unlink(Config::SHM_NAME);
        return false;
    }
    
    // Initialize shared memory contents
    memset(g_ipc.shared_mem, 0, sizeof(SharedMemoryData));
    g_ipc.shared_mem->shutdown_requested = false;
    
    // Create semaphores (clean up any existing ones first)
    sem_unlink(Config::SEM_READY);
    sem_unlink(Config::SEM_PROMPTS_WRITTEN);
    sem_unlink(Config::SEM_RESPONSE_WRITTEN);
    sem_unlink(Config::SEM_CHUNK_READY);
    
    g_ipc.sem_ready = sem_open(Config::SEM_READY, O_CREAT, 0666, 0);
    g_ipc.sem_prompts_written = sem_open(Config::SEM_PROMPTS_WRITTEN, O_CREAT, 0666, 0);
    g_ipc.sem_response_written = sem_open(Config::SEM_RESPONSE_WRITTEN, O_CREAT, 0666, 0);
    g_ipc.sem_chunk_ready = sem_open(Config::SEM_CHUNK_READY, O_CREAT, 0666, 0);
    
    if (g_ipc.sem_ready == SEM_FAILED || g_ipc.sem_prompts_written == SEM_FAILED ||
        g_ipc.sem_response_written == SEM_FAILED || g_ipc.sem_chunk_ready == SEM_FAILED) {
        std::cerr << "Error: Failed to create semaphores" << std::endl;
        // Cleanup shared memory on failure
        if (g_ipc.shared_mem && g_ipc.shared_mem != MAP_FAILED) {
            munmap(g_ipc.shared_mem, sizeof(SharedMemoryData));
            g_ipc.shared_mem = nullptr;
        }
        if (g_ipc.shm_fd != -1) {
            close(g_ipc.shm_fd);
            shm_unlink(Config::SHM_NAME);
            g_ipc.shm_fd = -1;
        }
        return false;
    }
#endif
    
    return true;
}

// ============================================================================
// COMMAND LINE PARSING
// ============================================================================

/**
 * Get the value of a command-line argument
 */
std::string get_arg_value(int argc, char** argv, const std::string& flag) {
    for (int i = 1; i < argc - 1; ++i) {
        if (std::string(argv[i]) == flag) {
            return std::string(argv[i + 1]);
        }
    }
    return "";
}

/**
 * Check if a command-line flag is present
 */
bool has_flag(int argc, char** argv, const std::string& flag) {
    for (int i = 1; i < argc; ++i) {
        if (std::string(argv[i]) == flag) {
            return true;
        }
    }
    return false;
}

/**
 * Display usage information
 */
void print_usage(const char* program_name) {
    std::cout << "╔════════════════════════════════════════════════════════════╗\n";
    std::cout << "║  Cross-Platform LLM Chatbot - llama.cpp Integration      ║\n";
    std::cout << "╚════════════════════════════════════════════════════════════╝\n\n";
    
    std::cout << "Usage: " << program_name << " [OPTIONS]\n\n";
    
    std::cout << "Operating Modes:\n";
    std::cout << "  --test             Interactive test mode (chatbot)\n";
    std::cout << "  (default)          Shared memory mode (C# integration)\n\n";
    
    std::cout << "Test Mode Options:\n";
    std::cout << "  --system <text>    Custom system prompt\n";
    std::cout << "  --user <text>      Single user prompt (one-shot mode)\n";
    std::cout << "  --stream           Enable streaming output\n";
    std::cout << "  --chunk-tokens <n> Chunk size in tokens for streaming (default: " << Config::DEFAULT_CHUNK_TOKENS << ")\n";
    std::cout << "  --max-tokens <n>   Maximum tokens (default: 0 = unlimited)\n\n";
    
    std::cout << "Examples:\n";
    std::cout << "  " << program_name << " --test\n";
    std::cout << "      Interactive mode with default settings\n\n";
    
    std::cout << "  " << program_name << " --test --stream --max-tokens 0\n";
    std::cout << "      Streaming mode with unlimited tokens\n\n";
    
    std::cout << "  " << program_name << " --test --system \"You are a coding expert\"\n";
    std::cout << "      Custom system prompt\n\n";
    
    std::cout << "  " << program_name << " --test --user \"What is C++?\"\n";
    std::cout << "      One-shot query mode\n\n";
}

// ----------------------------------------------------------------------------
// ENV HELPERS
// ----------------------------------------------------------------------------

static int get_env_int(const char* name, int default_value) {
#ifdef _WIN32
    const char* val = nullptr;
    size_t len = 0;
    if (_dupenv_s((char**)&val, &len, name) != 0 || val == nullptr) {
        return default_value;
    }
    try {
        int parsed = std::stoi(val);
        free((void*)val);
        return parsed;
    } catch (...) {
        free((void*)val);
        return default_value;
    }
#else
    const char* val = std::getenv(name);
    if (!val) return default_value;
    try {
        return std::stoi(val);
    } catch (...) {
        return default_value;
    }
#endif
}

// ============================================================================
// LLM INFERENCE
// ============================================================================

/**
 * Build a formatted prompt with system and user messages
 * Automatically uses the model's chat template when available.
 * Falls back to Llama-2 style if no template is provided by the model.
 */
std::string build_prompt(llama_context* ctx, const std::string& system_prompt, const std::string& user_prompt) {
    const llama_model* model = llama_get_model(ctx);

    // Try to use the model's default chat template (from GGUF metadata)
    const char* tmpl = nullptr;
    if (model) {
        tmpl = llama_model_chat_template(model, /* name */ nullptr);
    }

    llama_chat_message msgs[2];
    size_t n = 0;
    if (!system_prompt.empty()) {
        msgs[n++] = { "system", system_prompt.c_str() };
    }
    msgs[n++] = { "user", user_prompt.c_str() };

    // Apply template if available
    if (tmpl && *tmpl) {
        char buf[16384];
        int32_t len = llama_chat_apply_template(tmpl, msgs, n, /* add_ass */ true, buf, sizeof(buf));
        if (len > 0) {
            return std::string(buf, len);
        }
    }

    // Fallback: Llama-2 style
    if (!system_prompt.empty()) {
        return "[INST] <<SYS>>\n" + system_prompt + "\n<</SYS>>\n\n" + user_prompt + " [/INST] ";
    }
    return "[INST] " + user_prompt + " [/INST] ";
}

/**
 * Tokenize a prompt
 * @return Vector of tokens, or empty vector on failure
 */
std::vector<llama_token> tokenize_prompt(const llama_vocab* vocab, const std::string& prompt) {
    // Pre-allocate with reasonable estimate to avoid double tokenization
    std::vector<llama_token> tokens;
    tokens.reserve(prompt.length() / 3);  // Rough estimate: ~3 chars per token
    
    const int n_tokens = -llama_tokenize(vocab, prompt.c_str(), prompt.length(), NULL, 0, true, true);
    if (n_tokens <= 0) {
        std::cerr << "Error: Failed to tokenize prompt" << std::endl;
        return {};
    }
    
    tokens.resize(n_tokens);
    
    if (llama_tokenize(vocab, prompt.c_str(), prompt.length(), tokens.data(), tokens.size(), true, true) < 0) {
        std::cerr << "Error: Failed to tokenize prompt" << std::endl;
        return {};
    }
    
    return tokens;
}

/**
 * Process a single inference request (non-streaming)
 */
std::string process_inference_request(
    llama_context* ctx, 
    const llama_vocab* vocab,
    llama_sampler* smpl,
    const std::string& system_prompt, 
    const std::string& user_prompt,
    bool print_output = true,
    int max_tokens = Config::DEFAULT_MAX_TOKENS,
    bool verbose = false,
    int chunk_tokens = 1
) {
    // Build and tokenize prompt
    std::string full_prompt = build_prompt(ctx, system_prompt, user_prompt);
    std::vector<llama_token> tokens = tokenize_prompt(vocab, full_prompt);
    
    if (tokens.empty()) {
        return "";
    }
    
    if (verbose) {
        std::cout << "\n─────────── Prompt ───────────\n" << full_prompt << std::endl;
        std::cout << "\n─────────── Response ───────────\n";
    }

    // Evaluate the prompt
    llama_batch batch = llama_batch_get_one(tokens.data(), tokens.size());
    if (llama_decode(ctx, batch) != 0) {
        std::cerr << "Error: Failed to decode prompt" << std::endl;
        return "";
    }

    // Generate response token by token
    std::string response;
    response.reserve(2048);  // Pre-allocate response buffer to reduce reallocations
    int tokens_generated = 0;
    int tokens_since_flush = 0;
    std::string pending_output;
    
    while (max_tokens == 0 || tokens_generated < max_tokens) {
        // Sample next token
        llama_token new_token = llama_sampler_sample(smpl, ctx, -1);

        // Check for end of generation
        if (llama_vocab_is_eog(vocab, new_token)) {
            break;
        }

        // Convert token to text
        char buffer[Config::TOKEN_BUFFER_SIZE];
        int n = llama_token_to_piece(vocab, new_token, buffer, sizeof(buffer), 0, true);
        if (n < 0) {
            std::cerr << "\nError: Failed to convert token to text" << std::endl;
            break;
        }
        
        std::string piece(buffer, n);
        response += piece;
        
        if (print_output) {
            pending_output += piece;
            tokens_since_flush++;
            if (tokens_since_flush >= chunk_tokens) {
                std::cout << pending_output << std::flush;
                pending_output.clear();
                tokens_since_flush = 0;
            }
        }

        // Decode next token
        batch = llama_batch_get_one(&new_token, 1);
        if (llama_decode(ctx, batch) != 0) {
            std::cerr << "\nError: Failed to decode token" << std::endl;
            break;
        }
        
        tokens_generated++;
    }

    if (print_output && !pending_output.empty()) {
        std::cout << pending_output << std::flush;
    }

    if (verbose) {
        std::cout << "\n\n─────────── Complete ───────────" << std::endl;
        std::cout << "Tokens generated: " << tokens_generated << std::endl;
    }
    
    // Clean up for next request
    llama_sampler_reset(smpl);
    llama_memory_t mem = llama_get_memory(ctx);
    llama_memory_seq_rm(mem, 0, -1, -1);
    
    return response;
}

/**
 * Process inference request with streaming support for IPC
 */
std::string process_inference_streaming(
    llama_context* ctx, 
    const llama_vocab* vocab,
    llama_sampler* smpl,
                                          const std::string& system_prompt, 
                                          const std::string& user_prompt,
    int max_tokens = Config::DEFAULT_MAX_TOKENS,
    int chunk_tokens = 1
) {
    // Build and tokenize prompt
    std::string full_prompt = build_prompt(ctx, system_prompt, user_prompt);
    std::vector<llama_token> tokens = tokenize_prompt(vocab, full_prompt);
    
    if (tokens.empty()) {
        // Mark as complete with empty response to unblock C#
        g_ipc.shared_mem->generation_complete = true;
        g_ipc.shared_mem->tokens_generated = 0;
        memset(g_ipc.shared_mem->response, 0, sizeof(g_ipc.shared_mem->response));
        std::atomic_thread_fence(std::memory_order_release);
        g_ipc.shared_mem->update_counter++;
        g_ipc.semaphore_post(g_ipc.sem_chunk_ready);
        return "";
    }

    // Evaluate the prompt
    llama_batch batch = llama_batch_get_one(tokens.data(), tokens.size());
    if (llama_decode(ctx, batch) != 0) {
        std::cerr << "Error: Failed to decode prompt" << std::endl;
        // Mark as complete with empty response to unblock C#
        g_ipc.shared_mem->generation_complete = true;
        g_ipc.shared_mem->tokens_generated = 0;
        memset(g_ipc.shared_mem->response, 0, sizeof(g_ipc.shared_mem->response));
        std::atomic_thread_fence(std::memory_order_release);
        g_ipc.shared_mem->update_counter++;
        g_ipc.semaphore_post(g_ipc.sem_chunk_ready);
        return "";
    }

    // Initialize streaming state
    g_ipc.shared_mem->generation_complete = false;
    g_ipc.shared_mem->update_counter = 0;
    g_ipc.shared_mem->tokens_generated = 0;
    memset(g_ipc.shared_mem->response, 0, sizeof(g_ipc.shared_mem->response));
    
    // Generate response with streaming updates
    std::string response;
    response.reserve(2048);  // Pre-allocate response buffer to reduce reallocations
    int tokens_generated = 0;
    int tokens_since_flush = 0;
    
    while (max_tokens == 0 || tokens_generated < max_tokens) {
        // Sample next token
        llama_token new_token = llama_sampler_sample(smpl, ctx, -1);

        // Check for end of generation
        if (llama_vocab_is_eog(vocab, new_token)) {
            break;
        }

        // Convert token to text
        char buffer[Config::TOKEN_BUFFER_SIZE];
        int n = llama_token_to_piece(vocab, new_token, buffer, sizeof(buffer), 0, true);
        if (n < 0) {
            std::cerr << "\nError: Failed to convert token to text" << std::endl;
            break;
        }
        
        std::string piece(buffer, n);
        response += piece;
        tokens_since_flush++;

        // Only publish after chunk boundary to reduce IPC overhead
        if (tokens_since_flush >= chunk_tokens) {
            size_t response_len = response.length();
            size_t max_copy = sizeof(g_ipc.shared_mem->response) - 1;
            if (response_len > max_copy) response_len = max_copy;

            // Write all shared memory fields before incrementing counter
            memcpy(g_ipc.shared_mem->response, response.c_str(), response_len);
            g_ipc.shared_mem->response[response_len] = '\0';
            g_ipc.shared_mem->tokens_generated = tokens_generated + 1;  // +1 because we haven't incremented yet
            
            // Memory barrier: ensure all writes are visible before signaling
            std::atomic_thread_fence(std::memory_order_release);
            g_ipc.shared_mem->update_counter++;
            
            // Signal after counter is updated
            g_ipc.semaphore_post(g_ipc.sem_chunk_ready);
            tokens_since_flush = 0;
        }
        
        // Decode next token
        batch = llama_batch_get_one(&new_token, 1);
        if (llama_decode(ctx, batch) != 0) {
            std::cerr << "\nError: Failed to decode token" << std::endl;
            break;
        }
        
        tokens_generated++;
    }

    // Flush any remaining partial chunk before completion
    if (tokens_since_flush > 0) {
        size_t response_len = response.length();
        size_t max_copy = sizeof(g_ipc.shared_mem->response) - 1;
        if (response_len > max_copy) response_len = max_copy;
        memcpy(g_ipc.shared_mem->response, response.c_str(), response_len);
        g_ipc.shared_mem->response[response_len] = '\0';
        g_ipc.shared_mem->tokens_generated = tokens_generated;
        
        // Memory barrier: ensure all writes are visible before signaling
        std::atomic_thread_fence(std::memory_order_release);
        g_ipc.shared_mem->update_counter++;
        g_ipc.semaphore_post(g_ipc.sem_chunk_ready);
    }

    // Mark generation as complete
    g_ipc.shared_mem->generation_complete = true;
    g_ipc.shared_mem->tokens_generated = tokens_generated;  // Final count
    // Memory barrier: ensure all writes are visible before signaling
    std::atomic_thread_fence(std::memory_order_release);
    g_ipc.shared_mem->update_counter++;
    g_ipc.semaphore_post(g_ipc.sem_chunk_ready);
    
    // Clean up for next request
    llama_sampler_reset(smpl);
    llama_memory_t mem = llama_get_memory(ctx);
    llama_memory_seq_rm(mem, 0, -1, -1);
    
    return response;
}

// ============================================================================
// MODEL INITIALIZATION
// ============================================================================

/**
 * RAII wrapper for LLM model resources
 */
class LLMEngine {
public:
    llama_model* model = nullptr;
    llama_context* ctx = nullptr;
    const llama_vocab* vocab = nullptr;
    llama_sampler* sampler = nullptr;
    
    bool initialize(const std::string& model_path) {
        std::cout << "Loading model: " << model_path << std::endl;

        // Set logging to errors only
        llama_log_set([](enum ggml_log_level level, const char* text, void*) {
            if (level >= GGML_LOG_LEVEL_ERROR) {
                fprintf(stderr, "%s", text);
            }
        }, nullptr);

        // Initialize llama backend (required for GPU detection)
        llama_backend_init();
        
        // Load dynamic backends
        ggml_backend_load_all();

        // Check available backends for diagnostics
        size_t backend_count = ggml_backend_dev_count();
        std::cout << "Available backends: " << backend_count << std::endl;
        for (size_t i = 0; i < backend_count; ++i) {
            ggml_backend_dev_t dev = ggml_backend_dev_get(i);
            const char* dev_name = ggml_backend_dev_name(dev);
            const char* dev_desc = ggml_backend_dev_description(dev);
            enum ggml_backend_dev_type dev_type = ggml_backend_dev_type(dev);
            const char* type_str = (dev_type == GGML_BACKEND_DEVICE_TYPE_GPU) ? "GPU" :
                                   (dev_type == GGML_BACKEND_DEVICE_TYPE_IGPU) ? "iGPU" :
                                   (dev_type == GGML_BACKEND_DEVICE_TYPE_ACCEL) ? "ACCEL" : "CPU";
            std::cout << "  [" << i << "] " << dev_name << " (" << dev_desc << ") - " << type_str << std::endl;
        }

        // Auto-detect GPU and configure layers
        // Priority: 1) LLAMA_GPU_LAYERS env var, 2) Auto-detect GPU, 3) CPU-only default
        int gpu_layers = get_env_int("LLAMA_GPU_LAYERS", -1);  // -1 means not set
        
        if (gpu_layers == -1) {
            // Auto-detect: use GPU if available, otherwise CPU
            if (llama_supports_gpu_offload()) {
                gpu_layers = 999;  // Use all GPU layers when GPU is detected
                std::cout << "GPU detected - enabling GPU acceleration (all layers)" << std::endl;
            } else {
                gpu_layers = 0;  // CPU-only mode
                std::cout << "No GPU detected at runtime - falling back to CPU mode" << std::endl;
                std::cout << "  The program will work normally using CPU, but may be slower." << std::endl;
                std::cout << "  Possible reasons:" << std::endl;
                std::cout << "  - No GPU hardware available on this system" << std::endl;
                std::cout << "  - GPU drivers are not installed or not accessible" << std::endl;
                std::cout << "  - GPU backend not loaded (check if CUDA/Vulkan libraries are available)" << std::endl;
                std::cout << "  Note: This is normal - GPU-enabled builds work on CPU-only devices." << std::endl;
            }
        } else {
            // User explicitly set via environment variable
            if (gpu_layers > 0) {
                if (!llama_supports_gpu_offload()) {
                    std::cout << "WARNING: LLAMA_GPU_LAYERS=" << gpu_layers 
                              << " was set, but no GPU detected at runtime!" << std::endl;
                    std::cout << "  Falling back to CPU mode." << std::endl;
                    std::cout << "  The program will work normally using CPU." << std::endl;
                    gpu_layers = 0;
                } else {
                    std::cout << "Using GPU with " << gpu_layers << " layers (from LLAMA_GPU_LAYERS)" << std::endl;
                }
            } else {
                std::cout << "Using CPU mode (LLAMA_GPU_LAYERS=0)" << std::endl;
            }
        }

        // Load model
        llama_model_params model_params = llama_model_default_params();
        model_params.n_gpu_layers = gpu_layers;
        
        model = llama_model_load_from_file(model_path.c_str(), model_params);
        if (!model) {
            std::cerr << "Error: Failed to load model from " << model_path << std::endl;
            return false;
        }

        vocab = llama_model_get_vocab(model);

        // Create context
        llama_context_params ctx_params = llama_context_default_params();
        ctx_params.n_ctx = Config::DEFAULT_CONTEXT_SIZE;
        ctx_params.n_batch = Config::DEFAULT_BATCH_SIZE;

        ctx = llama_init_from_model(model, ctx_params);
        if (!ctx) {
            std::cerr << "Error: Failed to create context" << std::endl;
            return false;
        }
        
        // Initialize sampler
        sampler = llama_sampler_chain_init(llama_sampler_chain_default_params());
        llama_sampler_chain_add(sampler, llama_sampler_init_min_p(Config::SAMPLER_MIN_P, 1));
        llama_sampler_chain_add(sampler, llama_sampler_init_temp(Config::SAMPLER_TEMPERATURE));
        llama_sampler_chain_add(sampler, llama_sampler_init_dist(LLAMA_DEFAULT_SEED));
        
        std::cout << "Model loaded successfully." << std::endl;
        return true;
    }
    
    ~LLMEngine() {
        if (sampler) llama_sampler_free(sampler);
        if (ctx) llama_free(ctx);
        if (model) llama_model_free(model);
    }
};

// ============================================================================
// TEST MODE - INTERACTIVE CHATBOT
// ============================================================================

/**
 * Run interactive chatbot mode for testing
 */
int run_test_mode(int argc, char** argv) {
    // Parse command-line arguments
    std::string system_prompt = get_arg_value(argc, argv, "--system");
    std::string user_prompt = get_arg_value(argc, argv, "--user");
    bool stream_mode = has_flag(argc, argv, "--stream");
    // Parse chunk size for streaming
    int chunk_tokens = Config::DEFAULT_CHUNK_TOKENS;
    std::string chunk_tokens_str = get_arg_value(argc, argv, "--chunk-tokens");
    if (!chunk_tokens_str.empty()) {
        try {
            int v = std::stoi(chunk_tokens_str);
            if (v > 0) chunk_tokens = v;
        } catch (...) {
            // ignore invalid, keep default
        }
    }
    
    if (system_prompt.empty()) {
        system_prompt = Config::DEFAULT_SYSTEM_PROMPT;
    }
    
    // Parse max_tokens (0 = unlimited)
    int max_tokens = Config::DEFAULT_MAX_TOKENS;
    std::string max_tokens_str = get_arg_value(argc, argv, "--max-tokens");
    if (!max_tokens_str.empty()) {
        try {
            max_tokens = std::stoi(max_tokens_str);
            if (max_tokens < 0) {
                std::cerr << "Error: --max-tokens must be non-negative (use 0 for unlimited)\n";
                return 1;
            }
        } catch (...) {
            std::cerr << "Error: Invalid --max-tokens value\n";
            return 1;
        }
    }
    
    // Initialize LLM
    LLMEngine engine;
    if (!engine.initialize(Config::MODEL_PATH)) {
            return 1;
        }

    bool interactive_mode = user_prompt.empty();

        if (interactive_mode) {
        // Interactive conversation mode
            std::cout << "\n╔════════════════════════════════════════════════════════════╗" << std::endl;
            std::cout << "║          Interactive Chatbot Mode                         ║" << std::endl;
            std::cout << "╚════════════════════════════════════════════════════════════╝" << std::endl;
            std::cout << "\nSystem: " << system_prompt << std::endl;
        std::cout << "Mode: " << (stream_mode ? "Streaming" : "Normal") << std::endl;
        std::cout << "Max Tokens: " << (max_tokens == 0 ? "Unlimited" : std::to_string(max_tokens)) << std::endl;
        std::cout << "\nType 'exit', 'quit', or 'bye' to end the conversation.\n" << std::endl;
        
        std::string line;
        while (true) {
            std::cout << "\n\033[1;36mYou:\033[0m ";
            
            if (!std::getline(std::cin, line)) {
                break;  // EOF or error
            }
            
            // Trim whitespace
            size_t start = line.find_first_not_of(" \t\r\n");
            size_t end = line.find_last_not_of(" \t\r\n");
            
            if (start == std::string::npos) {
                continue;  // Empty input
            }
            
            std::string input = line.substr(start, end - start + 1);
            
            // Check for exit commands
                if (input == "exit" || input == "quit" || input == "bye") {
                    std::cout << "\n\033[1;33mGoodbye!\033[0m\n" << std::endl;
                    break;
                }
                
            // Process request
            std::cout << "\n\033[1;32mAssistant:\033[0m ";
            std::string response = process_inference_request(
                engine.ctx, engine.vocab, engine.sampler,
                system_prompt, input, stream_mode, max_tokens, false, chunk_tokens
            );
            
            if (!stream_mode) {
                std::cout << response << std::endl;
            } else {
                std::cout << std::endl;  // Newline after streaming output
            }
        }
    } else {
        // One-shot mode (verbose = true to show prompt and stats)
        process_inference_request(
            engine.ctx, engine.vocab, engine.sampler,
            system_prompt, user_prompt, true, max_tokens, true
        );
    }
    
    return 0;
}

// ============================================================================
// IPC MODE - SHARED MEMORY SERVICE
// ============================================================================

/**
 * Run as a background service for C# integration
 */
int run_ipc_mode() {
    std::cout << "Starting shared memory IPC mode for C# integration..." << std::endl;
        
        // Setup signal handlers
#ifdef _WIN32
    SetConsoleCtrlHandler(console_ctrl_handler, TRUE);
#else
        signal(SIGINT, signal_handler);
        signal(SIGTERM, signal_handler);
#endif
    
    // Initialize IPC
    if (!initialize_ipc()) {
        std::cerr << "Error: Failed to initialize IPC" << std::endl;
            return 1;
        }
        
    std::cout << "IPC initialized successfully." << std::endl;
    
    // Initialize LLM
    LLMEngine engine;
    if (!engine.initialize(Config::MODEL_PATH)) {
        cleanup_ipc_resources();
        return 1;
    }

    std::cout << "Ready to process requests from C#." << std::endl;
    
    // Chunk size from env for streaming chunks
    int chunk_tokens = get_env_int("LLAMA_CHUNK_TOKENS", Config::DEFAULT_CHUNK_TOKENS);

    // Main request processing loop
    while (true) {
        // Signal ready and wait for prompts
        g_ipc.semaphore_post(g_ipc.sem_ready);
        std::cout << "\nWaiting for prompts from C#..." << std::endl;
        g_ipc.semaphore_wait(g_ipc.sem_prompts_written);
            
            // Check for shutdown request
        if (g_ipc.shared_mem->shutdown_requested) {
            std::cout << "Shutdown requested by C#." << std::endl;
                break;
            }
            
        // Read prompts
        std::string system_prompt(g_ipc.shared_mem->system_prompt);
        std::string user_prompt(g_ipc.shared_mem->user_prompt);
        bool stream_mode = g_ipc.shared_mem->stream_mode;
        
        // Validate prompts
        if (user_prompt.empty()) {
            std::cerr << "Warning: Empty user prompt received, skipping..." << std::endl;
            // Write empty response and signal to unblock C#
            memset(g_ipc.shared_mem->response, 0, sizeof(g_ipc.shared_mem->response));
            std::atomic_thread_fence(std::memory_order_release);
            g_ipc.semaphore_post(g_ipc.sem_response_written);
            continue;
        }
        
        std::cout << "Processing request..." << std::endl;
        std::cout << "  System: " << (system_prompt.empty() ? "(none)" : system_prompt) << std::endl;
        std::cout << "  User: " << user_prompt << std::endl;
        std::cout << "  Streaming: " << (stream_mode ? "Yes" : "No") << std::endl;
        
        // Process request (max_tokens = 0 means unlimited)
        std::string response;
        bool processing_success = true;
        
        if (stream_mode) {
            // Streaming mode: Updates sent via chunk_ready signals during generation
            // The final response_written signal confirms completion and readiness for next request
            response = process_inference_streaming(
                engine.ctx, engine.vocab, engine.sampler,
                system_prompt, user_prompt, 0, chunk_tokens
            );
            if (response.empty()) {
                std::cerr << "Error: Streaming inference failed, returning empty response" << std::endl;
                processing_success = false;
            }
        } else {
            response = process_inference_request(
                engine.ctx, engine.vocab, engine.sampler,
                system_prompt, user_prompt, false, 0
            );
            
            if (response.empty()) {
                std::cerr << "Error: Inference failed, returning empty response" << std::endl;
                processing_success = false;
            }
            
            // Write response to shared memory (PERFORMANCE: Use memcpy instead of strncpy)
            size_t response_len = response.length();
            size_t max_copy = sizeof(g_ipc.shared_mem->response) - 1;
            if (response_len > max_copy) response_len = max_copy;
            
            memcpy(g_ipc.shared_mem->response, response.c_str(), response_len);
            g_ipc.shared_mem->response[response_len] = '\0';
            
            // Memory barrier: ensure response is visible before signaling
            std::atomic_thread_fence(std::memory_order_release);
        }
        
        if (processing_success) {
            std::cout << "Request complete. Signaling C#..." << std::endl;
        } else {
            std::cout << "Request failed but signaling C# with empty response..." << std::endl;
        }
        g_ipc.semaphore_post(g_ipc.sem_response_written);
        }

        // Cleanup
        std::cout << "Cleaning up..." << std::endl;
    cleanup_ipc_resources();
    std::cout << "Shutdown complete." << std::endl;
    
    return 0;
}

// ============================================================================
// MAIN ENTRY POINT
// ============================================================================

int main(int argc, char** argv) {
    // Set UTF-8 locale for proper Unicode support
#ifdef _WIN32
    // Set console to UTF-8 on Windows
    SetConsoleOutputCP(CP_UTF8);
    SetConsoleCP(CP_UTF8);
#else
    // Set UTF-8 locale on Linux/Unix
    std::setlocale(LC_ALL, "");
    std::locale::global(std::locale(""));
    std::cout.imbue(std::locale());
#endif
    
    // Display help if requested
    if (has_flag(argc, argv, "--help") || has_flag(argc, argv, "-h")) {
        print_usage(argv[0]);
        return 0;
    }
    
    // Determine operating mode
    bool test_mode = has_flag(argc, argv, "--test");
    
    if (test_mode) {
        return run_test_mode(argc, argv);
    } else {
        return run_ipc_mode();
    }
}
