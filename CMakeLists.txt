cmake_minimum_required(VERSION 3.10)

project(LlamaCppChatbot)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Add llama.cpp subdirectory
add_subdirectory(llama.cpp)

# Create the chatbot executable
add_executable(chatbot main.cpp)

# Link against llama library from llama.cpp
target_link_libraries(chatbot PRIVATE llama)

# Include llama.cpp headers
target_include_directories(chatbot PRIVATE 
    ${CMAKE_CURRENT_SOURCE_DIR}/llama.cpp
    ${CMAKE_CURRENT_SOURCE_DIR}/llama.cpp/common
    ${CMAKE_CURRENT_SOURCE_DIR}/llama.cpp/include
)

