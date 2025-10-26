# Windows Clang + Ninja Toolchain with Minimum Size Optimization
set(CMAKE_SYSTEM_NAME Windows)
set(CMAKE_SYSTEM_PROCESSOR x86_64)

# Specify Clang as the compiler
set(CMAKE_C_COMPILER clang)
set(CMAKE_CXX_COMPILER clang++)

# Use lld linker (LLVM's linker, faster and can optimize better)
set(CMAKE_LINKER lld-link)
set(CMAKE_C_COMPILER_LINKER lld-link)
set(CMAKE_CXX_COMPILER_LINKER lld-link)

# Compiler flags for minimum size
set(CMAKE_C_FLAGS_RELEASE "-Os -DNDEBUG -flto=thin -ffunction-sections -fdata-sections")
set(CMAKE_CXX_FLAGS_RELEASE "-Os -DNDEBUG -flto=thin -ffunction-sections -fdata-sections")

# Linker flags for minimum size
set(CMAKE_EXE_LINKER_FLAGS_RELEASE "-flto=thin -Wl,--gc-sections -Wl,--strip-all -Wl,--as-needed")
set(CMAKE_SHARED_LINKER_FLAGS_RELEASE "-flto=thin -Wl,--gc-sections -Wl,--strip-all -Wl,--as-needed")

# Additional optimization options
add_compile_options(
    -fomit-frame-pointer      # Remove frame pointer for smaller code
    -fno-exceptions           # Disable exceptions if not needed
    -fno-rtti                 # Disable RTTI if not needed
    -fmerge-all-constants     # Merge identical constants
    -fno-ident                # Remove compiler identification
)

# Link-time optimization
set(CMAKE_INTERPROCEDURAL_OPTIMIZATION TRUE)

