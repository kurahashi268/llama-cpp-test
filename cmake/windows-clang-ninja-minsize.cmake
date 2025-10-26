# Toolchain file for LLVM-MinGW (clang++) on Windows
# Optimized for smallest executable size with maximum performance

# Set the target system
set(CMAKE_SYSTEM_NAME Windows)
set(CMAKE_SYSTEM_PROCESSOR AMD64)

# Specify the cross compiler
# Note: Adjust these paths based on your LLVM-MinGW installation
# Default assumes LLVM-MinGW is in PATH or C:/llvm-mingw/bin
find_program(CMAKE_C_COMPILER NAMES clang clang.exe REQUIRED)
find_program(CMAKE_CXX_COMPILER NAMES clang++ clang++.exe REQUIRED)
find_program(CMAKE_RC_COMPILER NAMES llvm-rc llvm-rc.exe windres windres.exe REQUIRED)
find_program(CMAKE_AR NAMES llvm-ar llvm-ar.exe REQUIRED)
find_program(CMAKE_RANLIB NAMES llvm-ranlib llvm-ranlib.exe REQUIRED)

# Set the linker
find_program(CMAKE_LINKER NAMES ld.lld lld lld.exe ld ld.exe REQUIRED)

# Force static linking
set(CMAKE_EXE_LINKER_FLAGS_INIT "-static -static-libgcc -static-libstdc++")
set(CMAKE_SHARED_LINKER_FLAGS_INIT "-static -static-libgcc -static-libstdc++")

# Compiler flags for minimum size with maximum optimization
# -O3: Maximum optimization for performance
# -march=native: Optimize for the current CPU architecture (use -march=x86-64 for portable builds)
# -flto: Link-time optimization for better optimization and smaller size
# -DNDEBUG: Disable debug assertions
# -ffunction-sections -fdata-sections: Place each function/data in separate sections for better dead code elimination
# -fno-exceptions: Disable C++ exceptions (reduces size significantly)
# -fno-rtti: Disable runtime type information (reduces size)
# -fno-asynchronous-unwind-tables: Don't generate unwind tables (reduces size)
# -fno-unwind-tables: Don't generate unwind tables (reduces size)

set(CMAKE_C_FLAGS_RELEASE_INIT "-O3 -march=native -flto -DNDEBUG -ffunction-sections -fdata-sections -fno-asynchronous-unwind-tables -fno-unwind-tables")
set(CMAKE_CXX_FLAGS_RELEASE_INIT "-O3 -march=native -flto -DNDEBUG -ffunction-sections -fdata-sections -fno-exceptions -fno-rtti -fno-asynchronous-unwind-tables -fno-unwind-tables")

# Linker flags for minimum size
# -s: Strip all symbols
# -flto: Link-time optimization
# -Wl,--gc-sections: Enable garbage collection of unused sections
# -Wl,--strip-all: Strip all symbols
# -Wl,--strip-debug: Strip debug information
# -Wl,--build-id=none: Don't generate build ID (saves a few bytes)
# -static: Static linking
set(CMAKE_EXE_LINKER_FLAGS_RELEASE_INIT "-s -flto -Wl,--gc-sections -Wl,--strip-all -Wl,--strip-debug -Wl,--build-id=none -static")
set(CMAKE_SHARED_LINKER_FLAGS_RELEASE_INIT "-s -flto -Wl,--gc-sections -Wl,--strip-all -Wl,--strip-debug -Wl,--build-id=none")

# Additional settings for Windows
set(CMAKE_FIND_ROOT_PATH_MODE_PROGRAM NEVER)
set(CMAKE_FIND_ROOT_PATH_MODE_LIBRARY ONLY)
set(CMAKE_FIND_ROOT_PATH_MODE_INCLUDE ONLY)

# Use static libraries by default
set(BUILD_SHARED_LIBS OFF CACHE BOOL "Build shared libraries" FORCE)

# Compiler-specific settings
if(CMAKE_C_COMPILER_ID MATCHES "Clang")
    # Additional clang-specific optimizations
    add_compile_options(
        -fmerge-all-constants      # Merge duplicate constants
        -fno-stack-protector       # Disable stack protector (reduces size, less secure)
        -fomit-frame-pointer       # Omit frame pointer (reduces size, harder to debug)
    )
endif()

# Set target architecture explicitly
add_compile_definitions(_WIN32_WINNT=0x0601)  # Windows 7+

message(STATUS "=================================================")
message(STATUS "LLVM-MinGW Toolchain Configuration (Minimum Size)")
message(STATUS "=================================================")
message(STATUS "C Compiler: ${CMAKE_C_COMPILER}")
message(STATUS "C++ Compiler: ${CMAKE_CXX_COMPILER}")
message(STATUS "Linker: ${CMAKE_LINKER}")
message(STATUS "AR: ${CMAKE_AR}")
message(STATUS "Build Type: Release (optimized for size)")
message(STATUS "=================================================")

