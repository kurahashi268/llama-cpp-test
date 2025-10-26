# CMake Configuration Files

This directory contains CMake toolchain files and configuration files for building the project.

## Windows Build Configuration

### `windows-clang-ninja-minsize.cmake`

Toolchain file for building on Windows with LLVM-MinGW (clang++) optimized for **minimum executable size**.

**Key Features:**
- **Compiler:** LLVM-MinGW (clang++)
- **Build System:** Ninja
- **Optimization:** `-O3` (maximum optimization)
- **Architecture:** `-march=native` (optimizes for current CPU) or `-march=x86-64` (portable)
- **LTO:** Link-Time Optimization enabled (`-flto`)
- **Static Linking:** All libraries statically linked (no DLL dependencies)
- **Stripping:** All symbols and debug info stripped (`-s`, `-Wl,--strip-all`)
- **Size Optimizations:**
  - Function and data sections (`-ffunction-sections`, `-fdata-sections`)
  - Garbage collection of unused sections (`-Wl,--gc-sections`)
  - No exceptions (`-fno-exceptions`)
  - No RTTI (`-fno-rtti`)
  - No unwind tables
  - Frame pointer omission

**Target Platforms:**
- Windows 7 and later (64-bit)

**Build Types:**
1. **Native Build** (default) - Optimized for current CPU
   - Uses `-march=native`
   - Fastest performance, but not portable
   - Preset: `windows-clang-ninja-minsize`

2. **Portable Build** - Compatible with most x86-64 CPUs
   - Uses generic x86-64 optimizations
   - Slightly larger and slower, but runs on any modern CPU
   - Preset: `windows-clang-ninja-minsize-portable`

## Usage

### Prerequisites

1. **LLVM-MinGW** - Download from [GitHub Releases](https://github.com/mstorsjo/llvm-mingw/releases)
   - Or run `setup-llvm-mingw.ps1` to download automatically
2. **CMake 3.21+** - [Download](https://cmake.org/download/)
3. **Ninja** - [Download](https://github.com/ninja-build/ninja/releases)

### Quick Start

**Option 1: Using the build script (Recommended)**

```batch
REM For native build (optimized for your CPU)
build-windows-minsize.bat

REM For portable build (works on any x86-64 CPU)
build-windows-minsize.bat portable
```

**Option 2: Using CMake presets directly**

```batch
REM Configure
cmake --preset windows-clang-ninja-minsize

REM Build
cmake --build --preset windows-clang-ninja-minsize

REM Output: build-minsize\chatbot.exe
```

**Option 3: Manual configuration**

```batch
mkdir build-manual
cd build-manual

cmake .. ^
  -G Ninja ^
  -DCMAKE_BUILD_TYPE=Release ^
  -DCMAKE_TOOLCHAIN_FILE=../cmake/windows-clang-ninja-minsize.cmake ^
  -DBUILD_SHARED_LIBS=OFF ^
  -DGGML_STATIC=ON

cmake --build . -j
```

## Optimization Details

### Compiler Flags

```bash
# C/C++ flags
-O3                              # Maximum optimization level
-march=native                    # CPU-specific optimizations (or -march=x86-64 for portable)
-flto                           # Link-Time Optimization
-DNDEBUG                        # Disable assertions
-ffunction-sections             # Each function in separate section
-fdata-sections                 # Each data item in separate section
-fno-exceptions                 # Disable C++ exceptions (C++ only)
-fno-rtti                       # Disable runtime type information (C++ only)
-fno-asynchronous-unwind-tables # No unwind tables
-fno-unwind-tables              # No unwind tables
-fmerge-all-constants           # Merge duplicate constants
-fno-stack-protector            # Disable stack protector (less secure, smaller)
-fomit-frame-pointer            # Omit frame pointer register
```

### Linker Flags

```bash
-s                              # Strip symbols
-flto                           # Link-Time Optimization
-static                         # Static linking (no DLLs)
-static-libgcc                  # Static link libgcc
-static-libstdc++               # Static link libstdc++
-Wl,--gc-sections               # Remove unused sections
-Wl,--strip-all                 # Strip all symbols
-Wl,--strip-debug               # Strip debug info
-Wl,--build-id=none             # No build ID
```

### Expected Results

With these optimizations, you should achieve:
- **Executable size:** ~2-5 MB (depending on model and features)
- **No external dependencies:** Single standalone .exe file
- **Performance:** Near-optimal (O3 optimization)
- **Portability:** Runs on Windows 7+ without any runtime libraries

## Troubleshooting

### "clang++ not found"
- Install LLVM-MinGW and add to PATH
- Or run `setup-llvm-mingw.ps1`

### "ninja not found"
- Download Ninja and add to PATH
- Or use `-G "MinGW Makefiles"` instead

### Large executable size
- Make sure you're building in Release mode
- Verify LTO is enabled (`-flto` in compile and link flags)
- Check that stripping is working (`-s` and `-Wl,--strip-all`)
- Use `windows-clang-ninja-minsize-portable` preset for slightly smaller size

### Crashes or errors
- If using native build, try portable build instead
- Some CPU optimizations might not be compatible with runtime environment
- Check Windows version (requires Windows 7+)

## Advanced Configuration

### Custom LLVM-MinGW Location

If LLVM-MinGW is not in PATH, edit `windows-clang-ninja-minsize.cmake` and set:

```cmake
set(CMAKE_C_COMPILER "C:/path/to/llvm-mingw/bin/clang.exe")
set(CMAKE_CXX_COMPILER "C:/path/to/llvm-mingw/bin/clang++.exe")
```

### Additional Size Optimizations

To squeeze out even more size, you can try:

```cmake
# In CMakeLists.txt or as cache variables
set(GGML_AVX OFF)      # Disable AVX (smaller, slower)
set(GGML_AVX2 OFF)     # Disable AVX2
set(GGML_FMA OFF)      # Disable FMA
set(GGML_F16C OFF)     # Disable F16C
```

### Security Considerations

This configuration prioritizes size over security:
- Stack protector disabled (`-fno-stack-protector`)
- Exceptions disabled (errors may terminate program)
- RTTI disabled (dynamic_cast won't work)

For production use, consider:
- Enabling stack protector (`-fstack-protector-strong`)
- Enabling exceptions (remove `-fno-exceptions`)
- Keeping RTTI if needed (remove `-fno-rtti`)

## License

Same as parent project.

