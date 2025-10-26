# Windows LLVM-MinGW + Ninja Toolchain with Minimum Size Optimization
set(CMAKE_SYSTEM_NAME Windows)
set(CMAKE_SYSTEM_PROCESSOR x86_64)

# Specify LLVM-MinGW compilers
# These will be found in PATH if LLVM-MinGW is installed
set(CMAKE_C_COMPILER clang)
set(CMAKE_CXX_COMPILER clang++)
set(CMAKE_RC_COMPILER llvm-rc)

# Use LLD with MinGW target
set(CMAKE_LINKER ld.lld)

# Compiler flags for minimum size
set(CMAKE_C_FLAGS_INIT "-target x86_64-w64-mingw32")
set(CMAKE_CXX_FLAGS_INIT "-target x86_64-w64-mingw32")
set(CMAKE_C_FLAGS_RELEASE "-Os -DNDEBUG -flto -ffunction-sections -fdata-sections")
set(CMAKE_CXX_FLAGS_RELEASE "-Os -DNDEBUG -flto -ffunction-sections -fdata-sections")

# Linker flags for minimum size (GNU ld style for MinGW)
set(CMAKE_EXE_LINKER_FLAGS_RELEASE "-flto -Wl,--gc-sections -Wl,--strip-all -Wl,--as-needed -s")
set(CMAKE_SHARED_LINKER_FLAGS_RELEASE "-flto -Wl,--gc-sections -Wl,--strip-all -Wl,--as-needed -s")

# Static linking to avoid DLL dependencies
set(CMAKE_EXE_LINKER_FLAGS "${CMAKE_EXE_LINKER_FLAGS} -static")

# Additional optimization options
add_compile_options(
    -fomit-frame-pointer      # Remove frame pointer for smaller code
    -fno-exceptions           # Disable exceptions if not needed
    -fno-rtti                 # Disable RTTI if not needed
    -fmerge-all-constants     # Merge identical constants
    -fno-ident                # Remove compiler identification
    -fno-asynchronous-unwind-tables  # Remove unwind tables
)

# Link-time optimization
set(CMAKE_INTERPROCEDURAL_OPTIMIZATION TRUE)

# MinGW-specific: Use printf/scanf from ucrtbase for smaller size
add_compile_definitions(__USE_MINGW_ANSI_STDIO=0)

