# Building with Clang + Ninja on Windows for Minimum Size

This guide shows how to build the llama-cpp chatbot on Windows using Clang and Ninja to achieve the smallest possible executable size.

## Prerequisites

1. **Visual Studio Build Tools** (or Visual Studio 2019/2022)
   - Install from: https://visualstudio.microsoft.com/downloads/
   - Select "Desktop development with C++"

2. **LLVM/Clang for Windows**
   - Download from: https://github.com/llvm/llvm-project/releases
   - Get the latest Windows installer (e.g., LLVM-XX.X.X-win64.exe)
   - During installation, select "Add LLVM to system PATH"

3. **Ninja Build System**
   - Download from: https://github.com/ninja-build/ninja/releases
   - Extract ninja.exe and add its directory to PATH
   - Or install via: `winget install Ninja-build.Ninja`

4. **CMake** (3.21 or newer)
   - Download from: https://cmake.org/download/
   - Or install via: `winget install Kitware.CMake`

## Quick Build Method

### Option 1: Using the Build Script (Easiest)

Open **Developer Command Prompt for Visual Studio** or **x64 Native Tools Command Prompt**, then:

```batch
cd /d C:\path\to\llama-cpp
build-windows-minsize.bat
```

The executable will be in: `build-minsize\bin\chatbot.exe`

### Option 2: Using CMake Presets

```batch
# Configure
cmake --preset windows-clang-ninja-minsize

# Build
cmake --build build-minsize --config Release -j
```

### Option 3: Manual Configuration

```batch
# Create and enter build directory
mkdir build-minsize
cd build-minsize

# Configure with Clang toolchain
cmake .. -G Ninja ^
  -DCMAKE_TOOLCHAIN_FILE=../cmake/windows-clang-ninja-minsize.cmake ^
  -DCMAKE_BUILD_TYPE=Release ^
  -DBUILD_SHARED_LIBS=OFF ^
  -DGGML_STATIC=ON ^
  -DGGML_NATIVE=OFF ^
  -DGGML_AVX=ON ^
  -DGGML_AVX2=ON ^
  -DGGML_FMA=ON ^
  -DGGML_F16C=ON ^
  -DLLAMA_BUILD_TESTS=OFF ^
  -DLLAMA_BUILD_EXAMPLES=OFF ^
  -DLLAMA_BUILD_SERVER=OFF

# Build
ninja
```

## Build Configurations

### 1. Minimum Size (Recommended)
- Uses AVX/AVX2 instructions for performance
- Optimized for size with -Os
- Link-Time Optimization (LTO) enabled
- Dead code elimination
- Symbol stripping

```batch
cmake --preset windows-clang-ninja-minsize
cmake --build build-minsize --config Release
```

### 2. Minimum Size + Maximum Portability
- No AVX/AVX2 (runs on older CPUs)
- Maximum compatibility
- Slightly larger but works everywhere

```batch
cmake --preset windows-clang-ninja-minsize-portable
cmake --build build-minsize-portable --config Release
```

## Size Optimization Techniques Applied

The toolchain file includes these optimizations:

1. **-Os**: Optimize for size
2. **-flto=thin**: Thin Link-Time Optimization for smaller binaries
3. **-ffunction-sections -fdata-sections**: Separate sections for better dead code elimination
4. **-Wl,--gc-sections**: Remove unused sections during linking
5. **-Wl,--strip-all**: Strip all symbols from the binary
6. **-fomit-frame-pointer**: Remove frame pointers
7. **-fno-exceptions**: Disable C++ exceptions (if not needed)
8. **-fno-rtti**: Disable runtime type information
9. **-fmerge-all-constants**: Merge duplicate constants
10. **BUILD_SHARED_LIBS=OFF**: Static linking for standalone executable

## Further Size Reduction (Optional)

### Using UPX Compression

After building, you can compress the executable with UPX:

```batch
# Download UPX from: https://upx.github.io/
upx --best --lzma build-minsize\bin\chatbot.exe
```

This can reduce size by 50-70% but may trigger antivirus false positives.

### Strip Additional Symbols (If not already stripped)

```batch
llvm-strip build-minsize\bin\chatbot.exe
```

## Troubleshooting

### Issue: "clang not found"
- Ensure LLVM is installed and added to PATH
- Restart terminal after installation
- Check: `where clang` should show the path

### Issue: "ninja not found"
- Download Ninja and add to PATH
- Or use `-G "NMake Makefiles"` instead of Ninja

### Issue: LTO errors
- Try removing `-flto=thin` from the toolchain file
- Or use `-DCMAKE_INTERPROCEDURAL_OPTIMIZATION=OFF`

### Issue: "undefined reference to ___chkstk_ms"
- Use Visual Studio Developer Command Prompt
- Or set: `set LIB=%LIB%;C:\Program Files\LLVM\lib\clang\XX\lib\windows`

## Comparing Sizes

After building, compare with other build methods:

```batch
dir build\bin\chatbot.exe           REM Default build
dir build-minsize\bin\chatbot.exe   REM Size-optimized build
```

You should see a 30-50% reduction in size with the optimized build.

## Performance Note

Size optimization (-Os) may result in slightly slower execution compared to -O3, but the difference is usually minimal for I/O-bound applications like LLM inference. The smaller size improves:
- Download/distribution speed
- Memory footprint
- Cache efficiency
- Startup time

## Running the Chatbot

```batch
cd build-minsize\bin
chatbot.exe
```

Or provide a model path:
```batch
chatbot.exe --model path\to\model.gguf
```

