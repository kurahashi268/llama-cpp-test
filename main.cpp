#include <iostream>
#include <string>
#include <vector>
#include <cstring>
#include <cstdio>
#include <climits>
#include <sys/mman.h>
#include <sys/stat.h>
#include <fcntl.h>
#include <unistd.h>
#include <semaphore.h>
#include <signal.h>
#include "llama.h"

// Shared memory structure
struct SharedMemoryData {
    char system_prompt[4096];
    char user_prompt[4096];
    char response[32768];
    bool shutdown_requested;
    
    // Streaming support
    bool stream_mode;              // If true, send partial responses
    int update_counter;            // Increments with each partial update
    bool generation_complete;      // True when generation is finished
    int tokens_generated;          // Number of tokens generated so far
};

// Global variables for cleanup
static int shm_fd = -1;
static SharedMemoryData* shared_mem = nullptr;
static sem_t* sem_ready = nullptr;
static sem_t* sem_prompts_written = nullptr;
static sem_t* sem_response_written = nullptr;
static sem_t* sem_chunk_ready = nullptr;  // For streaming updates
static pthread_mutex_t* shared_mutex = nullptr;

void print_usage(const char* program_name) {
    std::cout << "Usage: " << program_name << " [OPTIONS]\n";
    std::cout << "\nModes:\n";
    std::cout << "  --test             Run in interactive test mode as a chatbot\n";
    std::cout << "  (default)          Run in shared memory mode for C# integration\n";
    std::cout << "\nTest Mode Options:\n";
    std::cout << "  --system <text>    Custom system prompt (optional, default: \"You are my best assistance.\")\n";
    std::cout << "  --user <text>      Single user prompt for one-shot mode (optional)\n";
    std::cout << "  --stream           Enable streaming mode (show tokens as they generate)\n";
    std::cout << "  --max-tokens <n>   Maximum tokens to generate (default: 4096, use 0 for unlimited)\n";
    std::cout << "\nExamples:\n";
    std::cout << "  " << program_name << " --test                    # Interactive mode with default system prompt\n";
    std::cout << "  " << program_name << " --test --stream           # Interactive mode with streaming output\n";
    std::cout << "  " << program_name << " --test --max-tokens 8192  # Allow longer responses\n";
    std::cout << "  " << program_name << " --test --max-tokens 0     # Unlimited (until model stops naturally)\n";
    std::cout << "  " << program_name << " --test --system \"You are a coding expert\"  # Interactive with custom system\n";
    std::cout << "  " << program_name << " --test --user \"What is C++?\"              # One-shot mode\n";
    std::cout << "\nShared Memory Mode:\n";
    std::cout << "  " << program_name << "                          # Background process for C# integration\n";
}

std::string get_arg_value(int argc, char** argv, const std::string& flag) {
    for (int i = 1; i < argc - 1; ++i) {
        if (std::string(argv[i]) == flag) {
            return std::string(argv[i + 1]);
        }
    }
    return "";
}

bool has_flag(int argc, char** argv, const std::string& flag) {
    for (int i = 1; i < argc; ++i) {
        if (std::string(argv[i]) == flag) {
            return true;
        }
    }
    return false;
}

// Cleanup function
void cleanup_shared_resources() {
    if (shared_mem != nullptr) {
        munmap(shared_mem, sizeof(SharedMemoryData));
        shared_mem = nullptr;
    }
    
    if (shm_fd != -1) {
        close(shm_fd);
        shm_unlink("/llama_cpp_shared_mem");
        shm_fd = -1;
    }
    
    if (sem_ready != nullptr) {
        sem_close(sem_ready);
        sem_unlink("/llama_cpp_sem_ready");
        sem_ready = nullptr;
    }
    
    if (sem_prompts_written != nullptr) {
        sem_close(sem_prompts_written);
        sem_unlink("/llama_cpp_sem_prompts_written");
        sem_prompts_written = nullptr;
    }
    
    if (sem_response_written != nullptr) {
        sem_close(sem_response_written);
        sem_unlink("/llama_cpp_sem_response_written");
        sem_response_written = nullptr;
    }
    
    if (sem_chunk_ready != nullptr) {
        sem_close(sem_chunk_ready);
        sem_unlink("/llama_cpp_sem_chunk_ready");
        sem_chunk_ready = nullptr;
    }
}

// Signal handler for graceful shutdown
void signal_handler(int signum) {
    std::cout << "\nReceived signal " << signum << ", shutting down..." << std::endl;
    cleanup_shared_resources();
    exit(signum);
}

// Initialize shared memory resources
bool init_shared_memory() {
    // Create shared memory
    shm_fd = shm_open("/llama_cpp_shared_mem", O_CREAT | O_RDWR, 0666);
    if (shm_fd == -1) {
        std::cerr << "Error: Failed to create shared memory" << std::endl;
        return false;
    }
    
    // Set the size of shared memory
    if (ftruncate(shm_fd, sizeof(SharedMemoryData)) == -1) {
        std::cerr << "Error: Failed to set shared memory size" << std::endl;
        close(shm_fd);
        shm_unlink("/llama_cpp_shared_mem");
        return false;
    }
    
    // Map shared memory
    shared_mem = (SharedMemoryData*)mmap(NULL, sizeof(SharedMemoryData), 
                                          PROT_READ | PROT_WRITE, MAP_SHARED, shm_fd, 0);
    if (shared_mem == MAP_FAILED) {
        std::cerr << "Error: Failed to map shared memory" << std::endl;
        close(shm_fd);
        shm_unlink("/llama_cpp_shared_mem");
        return false;
    }
    
    // Initialize shared memory
    memset(shared_mem, 0, sizeof(SharedMemoryData));
    shared_mem->shutdown_requested = false;
    
    // Create semaphores
    sem_unlink("/llama_cpp_sem_ready");
    sem_ready = sem_open("/llama_cpp_sem_ready", O_CREAT, 0666, 0);
    if (sem_ready == SEM_FAILED) {
        std::cerr << "Error: Failed to create ready semaphore" << std::endl;
        return false;
    }
    
    sem_unlink("/llama_cpp_sem_prompts_written");
    sem_prompts_written = sem_open("/llama_cpp_sem_prompts_written", O_CREAT, 0666, 0);
    if (sem_prompts_written == SEM_FAILED) {
        std::cerr << "Error: Failed to create prompts_written semaphore" << std::endl;
        return false;
    }
    
    sem_unlink("/llama_cpp_sem_response_written");
    sem_response_written = sem_open("/llama_cpp_sem_response_written", O_CREAT, 0666, 0);
    if (sem_response_written == SEM_FAILED) {
        std::cerr << "Error: Failed to create response_written semaphore" << std::endl;
        return false;
    }
    
    sem_unlink("/llama_cpp_sem_chunk_ready");
    sem_chunk_ready = sem_open("/llama_cpp_sem_chunk_ready", O_CREAT, 0666, 0);
    if (sem_chunk_ready == SEM_FAILED) {
        std::cerr << "Error: Failed to create chunk_ready semaphore" << std::endl;
        return false;
    }
    
    return true;
}

// Process inference request
std::string process_llm_request(llama_model* model, llama_context* ctx, 
                                const llama_vocab* vocab, llama_sampler* smpl,
                                const std::string& system_prompt, 
                                const std::string& user_prompt,
                                bool print_output = true,
                                int max_tokens = 4096) {
    // Build the prompt
    std::string full_prompt;
    if (!system_prompt.empty()) {
        full_prompt = "<|system|>\n" + system_prompt + "<|end|>\n<|user|>\n" + user_prompt + "<|end|>\n<|assistant|>\n";
    } else {
        full_prompt = "<|user|>\n" + user_prompt + "<|end|>\n<|assistant|>\n";
    }

    if (print_output) {
        std::cout << "\n--- Prompt ---\n" << full_prompt << std::endl;
        std::cout << "\n--- Response ---\n";
    }

    // Tokenize the prompt
    const int n_prompt_tokens = -llama_tokenize(vocab, full_prompt.c_str(), full_prompt.length(), NULL, 0, true, true);
    std::vector<llama_token> tokens_list(n_prompt_tokens);
    
    if (llama_tokenize(vocab, full_prompt.c_str(), full_prompt.length(), tokens_list.data(), tokens_list.size(), true, true) < 0) {
        std::cerr << "Error: Failed to tokenize prompt" << std::endl;
        return "";
    }

    // Prepare batch
    llama_batch batch = llama_batch_get_one(tokens_list.data(), tokens_list.size());

    // Evaluate the prompt
    if (llama_decode(ctx, batch) != 0) {
        std::cerr << "Error: Failed to decode prompt" << std::endl;
        return "";
    }

    // Generate response
    int n_decode = 0;
    std::string response;

    while (n_decode < max_tokens) {
        // Sample the next token
        llama_token new_token_id = llama_sampler_sample(smpl, ctx, -1);

        // Check for end of generation
        if (llama_vocab_is_eog(vocab, new_token_id)) {
            break;
        }

        // Convert token to piece
        char buf[256];
        int n = llama_token_to_piece(vocab, new_token_id, buf, sizeof(buf), 0, true);
        if (n < 0) {
            std::cerr << "\nError: Failed to convert token to piece" << std::endl;
            break;
        }
        std::string piece(buf, n);
        response += piece;
        
        if (print_output) {
            std::cout << piece << std::flush;
        }

        // Prepare the next batch with the new token
        batch = llama_batch_get_one(&new_token_id, 1);
        
        n_decode++;

        // Evaluate
        if (llama_decode(ctx, batch) != 0) {
            std::cerr << "\nError: Failed to decode" << std::endl;
            break;
        }
    }

    if (print_output) {
        std::cout << "\n\n--- Generation Complete ---" << std::endl;
        std::cout << "Tokens generated: " << n_decode << std::endl;
    }
    
    // Reset the sampler state
    llama_sampler_reset(smpl);
    
    // Clear KV cache for next request (remove all tokens from sequence 0)
    llama_memory_t mem = llama_get_memory(ctx);
    llama_memory_seq_rm(mem, 0, -1, -1);
    
    return response;
}

// Process inference request with streaming support for shared memory
std::string process_llm_request_streaming(llama_model* model, llama_context* ctx, 
                                          const llama_vocab* vocab, llama_sampler* smpl,
                                          const std::string& system_prompt, 
                                          const std::string& user_prompt,
                                          int max_tokens = 4096) {
    // Build the prompt
    std::string full_prompt;
    if (!system_prompt.empty()) {
        full_prompt = "<|system|>\n" + system_prompt + "<|end|>\n<|user|>\n" + user_prompt + "<|end|>\n<|assistant|>\n";
    } else {
        full_prompt = "<|user|>\n" + user_prompt + "<|end|>\n<|assistant|>\n";
    }

    // Tokenize the prompt
    const int n_prompt_tokens = -llama_tokenize(vocab, full_prompt.c_str(), full_prompt.length(), NULL, 0, true, true);
    std::vector<llama_token> tokens_list(n_prompt_tokens);
    
    if (llama_tokenize(vocab, full_prompt.c_str(), full_prompt.length(), tokens_list.data(), tokens_list.size(), true, true) < 0) {
        std::cerr << "Error: Failed to tokenize prompt" << std::endl;
        return "";
    }

    // Prepare batch
    llama_batch batch = llama_batch_get_one(tokens_list.data(), tokens_list.size());

    // Evaluate the prompt
    if (llama_decode(ctx, batch) != 0) {
        std::cerr << "Error: Failed to decode prompt" << std::endl;
        return "";
    }

    // Initialize streaming state in shared memory
    shared_mem->generation_complete = false;
    shared_mem->update_counter = 0;
    shared_mem->tokens_generated = 0;
    memset(shared_mem->response, 0, sizeof(shared_mem->response));

    // Generate response
    int n_decode = 0;
    std::string response;

    while (n_decode < max_tokens) {
        // Sample the next token
        llama_token new_token_id = llama_sampler_sample(smpl, ctx, -1);

        // Check for end of generation
        if (llama_vocab_is_eog(vocab, new_token_id)) {
            break;
        }

        // Convert token to piece
        char buf[256];
        int n = llama_token_to_piece(vocab, new_token_id, buf, sizeof(buf), 0, true);
        if (n < 0) {
            std::cerr << "\nError: Failed to convert token to piece" << std::endl;
            break;
        }
        std::string piece(buf, n);
        response += piece;
        
        // Write partial response to shared memory
        strncpy(shared_mem->response, response.c_str(), sizeof(shared_mem->response) - 1);
        shared_mem->response[sizeof(shared_mem->response) - 1] = '\0';
        shared_mem->tokens_generated = n_decode + 1;
        shared_mem->update_counter++;
        
        // Signal C# that a chunk is ready
        sem_post(sem_chunk_ready);

        // Prepare the next batch with the new token
        batch = llama_batch_get_one(&new_token_id, 1);
        
        n_decode++;

        // Evaluate
        if (llama_decode(ctx, batch) != 0) {
            std::cerr << "\nError: Failed to decode" << std::endl;
            break;
        }
    }

    // Mark generation as complete
    shared_mem->generation_complete = true;
    shared_mem->update_counter++;
    sem_post(sem_chunk_ready);
    
    // Reset the sampler state
    llama_sampler_reset(smpl);
    
    // Clear KV cache for next request (remove all tokens from sequence 0)
    llama_memory_t mem = llama_get_memory(ctx);
    llama_memory_seq_rm(mem, 0, -1, -1);
    
    return response;
}

int main(int argc, char** argv) {
    // Check for test mode
    bool test_mode = has_flag(argc, argv, "--test");
    
    if (test_mode) {
        // Test mode - interactive or single request/response
        std::string system_prompt = get_arg_value(argc, argv, "--system");
        std::string user_prompt = get_arg_value(argc, argv, "--user");
        bool stream_mode = has_flag(argc, argv, "--stream");
        
        // Parse max_tokens (default: 4096)
        int max_tokens = 4096;
        std::string max_tokens_str = get_arg_value(argc, argv, "--max-tokens");
        if (!max_tokens_str.empty()) {
            try {
                max_tokens = std::stoi(max_tokens_str);
                if (max_tokens < 0) {
                    std::cerr << "Error: --max-tokens must be non-negative (use 0 for unlimited)\n";
                    return 1;
                }
                // 0 means unlimited - use INT_MAX
                if (max_tokens == 0) {
                    max_tokens = INT_MAX;
                    std::cout << "Max tokens: Unlimited (will generate until model stops naturally)" << std::endl;
                } else {
                    std::cout << "Max tokens: " << max_tokens << std::endl;
                }
            } catch (...) {
                std::cerr << "Error: Invalid --max-tokens value\n";
                return 1;
            }
        }
        
        // Use default system prompt if not provided
        if (system_prompt.empty()) {
            system_prompt = "You are my best assistance.";
        }
        
        // Determine if interactive mode or one-shot mode
        bool interactive_mode = user_prompt.empty();
        
        // Model path
        const std::string model_path = "models/Phi-3-mini-4k-instruct-q4.gguf";
        std::cout << "Loading model: " << model_path << std::endl;

        // Set logging to errors only
        llama_log_set([](enum ggml_log_level level, const char * text, void *) {
            if (level >= GGML_LOG_LEVEL_ERROR) {
                fprintf(stderr, "%s", text);
            }
        }, nullptr);

        // Load dynamic backends
        ggml_backend_load_all();

        // Model parameters
        llama_model_params model_params = llama_model_default_params();
        model_params.n_gpu_layers = 0;  // CPU only, set to 99 for GPU
        
        // Load the model
        llama_model* model = llama_model_load_from_file(model_path.c_str(), model_params);
        if (model == nullptr) {
            std::cerr << "Error: Failed to load model from " << model_path << std::endl;
            return 1;
        }

        // Get vocab
        const llama_vocab* vocab = llama_model_get_vocab(model);

        // Context parameters
        llama_context_params ctx_params = llama_context_default_params();
        ctx_params.n_ctx = 2048;  // Context size
        ctx_params.n_batch = 2048; // Batch size for prompt processing

        // Create context
        llama_context* ctx = llama_init_from_model(model, ctx_params);
        if (ctx == nullptr) {
            std::cerr << "Error: Failed to create context" << std::endl;
            llama_model_free(model);
            return 1;
        }

        // Initialize the sampler
        llama_sampler* smpl = llama_sampler_chain_init(llama_sampler_chain_default_params());
        llama_sampler_chain_add(smpl, llama_sampler_init_min_p(0.05f, 1));
        llama_sampler_chain_add(smpl, llama_sampler_init_temp(0.7f));
        llama_sampler_chain_add(smpl, llama_sampler_init_dist(LLAMA_DEFAULT_SEED));

        if (interactive_mode) {
            // Interactive mode - continuous conversation
            std::cout << "\n╔════════════════════════════════════════════════════════════╗" << std::endl;
            std::cout << "║          Interactive Chatbot Mode                         ║" << std::endl;
            std::cout << "╚════════════════════════════════════════════════════════════╝" << std::endl;
            std::cout << "\nSystem: " << system_prompt << std::endl;
            if (stream_mode) {
                std::cout << "Mode: Streaming (tokens appear as they generate)" << std::endl;
            } else {
                std::cout << "Mode: Normal (full response at once)" << std::endl;
            }
            
            // Display max tokens info
            if (max_tokens == INT_MAX) {
                std::cout << "Max Tokens: Unlimited (generates until naturally stops)" << std::endl;
            } else {
                std::cout << "Max Tokens: " << max_tokens << std::endl;
            }
            
            std::cout << "\nType your message and press Enter. Type 'exit' or 'quit' to end.\n" << std::endl;
            
            std::string line;
            while (true) {
                // Prompt for user input
                std::cout << "\n\033[1;36mYou:\033[0m ";
                
                // Read multi-line input (until empty line or single line)
                std::string input;
                if (!std::getline(std::cin, line)) {
                    // EOF or error
                    break;
                }
                
                input = line;
                
                // Trim whitespace
                size_t start = input.find_first_not_of(" \t\r\n");
                size_t end = input.find_last_not_of(" \t\r\n");
                if (start == std::string::npos) {
                    continue; // Empty input, skip
                }
                input = input.substr(start, end - start + 1);
                
                // Check for exit command
                if (input == "exit" || input == "quit" || input == "bye") {
                    std::cout << "\n\033[1;33mGoodbye!\033[0m\n" << std::endl;
                    break;
                }
                
                if (input.empty()) {
                    continue;
                }
                
                // Process the request
                std::cout << "\n\033[1;32mAssistant:\033[0m ";
                if (stream_mode) {
                    // Streaming mode: print tokens as they generate
                    std::string response = process_llm_request(model, ctx, vocab, smpl, system_prompt, input, true, max_tokens);
                    std::cout << std::endl;
                } else {
                    // Normal mode: get full response then print
                    std::string response = process_llm_request(model, ctx, vocab, smpl, system_prompt, input, false, max_tokens);
                    std::cout << response << std::endl;
                }
            }
            
        } else {
            // One-shot mode - single request/response
            process_llm_request(model, ctx, vocab, smpl, system_prompt, user_prompt, true, max_tokens);
        }

        // Cleanup
        llama_sampler_free(smpl);
        llama_free(ctx);
        llama_model_free(model);

        return 0;
    } else {
        // Shared memory mode - continuous operation
        std::cout << "Starting in shared memory mode for C# integration..." << std::endl;
        
        // Setup signal handlers
        signal(SIGINT, signal_handler);
        signal(SIGTERM, signal_handler);
        
        // Initialize shared memory
        if (!init_shared_memory()) {
            std::cerr << "Error: Failed to initialize shared memory" << std::endl;
            return 1;
        }
        
        std::cout << "Shared memory initialized successfully." << std::endl;
        
        // Model path
        const std::string model_path = "models/Phi-3-mini-4k-instruct-q4.gguf";
        std::cout << "Loading model: " << model_path << std::endl;

        // Set logging to errors only
        llama_log_set([](enum ggml_log_level level, const char * text, void *) {
            if (level >= GGML_LOG_LEVEL_ERROR) {
                fprintf(stderr, "%s", text);
            }
        }, nullptr);

        // Load dynamic backends
        ggml_backend_load_all();

        // Model parameters
        llama_model_params model_params = llama_model_default_params();
        model_params.n_gpu_layers = 0;  // CPU only, set to 99 for GPU
        
        // Load the model
        llama_model* model = llama_model_load_from_file(model_path.c_str(), model_params);
        if (model == nullptr) {
            std::cerr << "Error: Failed to load model from " << model_path << std::endl;
            cleanup_shared_resources();
            return 1;
        }

        // Get vocab
        const llama_vocab* vocab = llama_model_get_vocab(model);

        // Context parameters
        llama_context_params ctx_params = llama_context_default_params();
        ctx_params.n_ctx = 2048;  // Context size
        ctx_params.n_batch = 2048; // Batch size for prompt processing

        // Create context
        llama_context* ctx = llama_init_from_model(model, ctx_params);
        if (ctx == nullptr) {
            std::cerr << "Error: Failed to create context" << std::endl;
            llama_model_free(model);
            cleanup_shared_resources();
            return 1;
        }

        // Initialize the sampler
        llama_sampler* smpl = llama_sampler_chain_init(llama_sampler_chain_default_params());
        llama_sampler_chain_add(smpl, llama_sampler_init_min_p(0.05f, 1));
        llama_sampler_chain_add(smpl, llama_sampler_init_temp(0.7f));
        llama_sampler_chain_add(smpl, llama_sampler_init_dist(LLAMA_DEFAULT_SEED));

        std::cout << "Model loaded. Ready to process requests from C#." << std::endl;
        std::cout << "Signal ready to C# application..." << std::endl;
        
        // Main loop
        while (true) {
            // Signal that we're ready
            sem_post(sem_ready);
            
            std::cout << "Waiting for prompts from C#..." << std::endl;
            
            // Wait for C# to write prompts
            sem_wait(sem_prompts_written);
            
            // Check for shutdown request
            if (shared_mem->shutdown_requested) {
                std::cout << "Shutdown requested by C# application." << std::endl;
                break;
            }
            
            std::cout << "Received prompts from C#. Processing..." << std::endl;
            
            // Read prompts from shared memory
            std::string system_prompt(shared_mem->system_prompt);
            std::string user_prompt(shared_mem->user_prompt);
            
            std::cout << "System Prompt: " << (system_prompt.empty() ? "(empty)" : system_prompt) << std::endl;
            std::cout << "User Prompt: " << user_prompt << std::endl;
            
            // Check if C# wants streaming
            bool stream_requested = shared_mem->stream_mode;
            std::cout << "Stream Mode: " << (stream_requested ? "Enabled" : "Disabled") << std::endl;
            
            std::string response;
            if (stream_requested) {
                // Streaming mode - send partial responses
                std::cout << "Processing with streaming..." << std::endl;
                response = process_llm_request_streaming(model, ctx, vocab, smpl, 
                                                        system_prompt, user_prompt, 0);
            } else {
                // Normal mode - send complete response
                std::cout << "Processing normally..." << std::endl;
                response = process_llm_request(model, ctx, vocab, smpl, 
                                               system_prompt, user_prompt, false, 0);
                
                // Write response to shared memory
                strncpy(shared_mem->response, response.c_str(), sizeof(shared_mem->response) - 1);
                shared_mem->response[sizeof(shared_mem->response) - 1] = '\0';
            }
            
            std::cout << "Response generation complete. Signaling C#..." << std::endl;
            
            // Signal that response is ready (final)
            sem_post(sem_response_written);
        }

        // Cleanup
        std::cout << "Cleaning up..." << std::endl;
        llama_sampler_free(smpl);
        llama_free(ctx);
        llama_model_free(model);
        cleanup_shared_resources();
        
        std::cout << "Shutdown complete." << std::endl;
        return 0;
    }
}

