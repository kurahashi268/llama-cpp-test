# Windows Build Quick Start Guide

This guide will help you build the **smallest possible Windows executable** for the llama-cpp chatbot using LLVM-MinGW.

## 🎯 Goal

Build a fully static, optimized Windows executable with:
- **Smallest file size possible**
- **No external DLL dependencies**
- **Maximum performance** (O3 optimization + LTO)
- **Single standalone .exe file**

## 📋 Prerequisites

You need to install three tools:

### 1. LLVM-MinGW (Compiler)

**Option A: Automatic Setup (Recommended)**

Run the provided PowerShell script (as Administrator):

```powershell
# Run PowerShell as Administrator, then:
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
.\setup-llvm-mingw.ps1
```

**Option B: Manual Download**

1. Download from: https://github.com/mstorsjo/llvm-mingw/releases
2. Get the latest `llvm-mingw-*-ucrt-x86_64.zip`
3. Extract to `C:\llvm-mingw`
4. Add `C:\llvm-mingw\bin` to your PATH

### 2. CMake (Build System)

Download and install from: https://cmake.org/download/
- Version 3.21 or newer required
- During installation, select "Add CMake to system PATH"

### 3. Ninja (Build Tool)

Download from: https://github.com/ninja-build/ninja/releases
- Download `ninja-win.zip`
- Extract `ninja.exe` to a folder in your PATH (e.g., `C:\Windows\System32` or `C:\llvm-mingw\bin`)

## 🚀 Quick Build (3 Steps)

### Step 1: Open Command Prompt

Open a new Command Prompt or PowerShell window in the project directory.

### Step 2: Run the Build Script

```batch
REM For native build (fastest, optimized for your CPU)
build-windows-minsize.bat

REM OR for portable build (works on any x86-64 CPU)
build-windows-minsize.bat portable
```

### Step 3: Run Your Chatbot

```batch
REM Test in interactive mode
build-minsize\chatbot.exe --test

REM One-shot query
build-minsize\chatbot.exe --test --user "What is C++?"

REM Streaming mode
build-minsize\chatbot.exe --test --stream
```

That's it! 🎉

## 📊 Build Options

### Native Build (Default)

**Command:**
```batch
build-windows-minsize.bat
```

**Features:**
- Optimized for **your specific CPU**
- Uses `-march=native` flag
- **Fastest performance**
- **Smallest size**
- ⚠️ May not run on older or different CPUs

**Use when:**
- Building for the same machine you'll run on
- Want absolute best performance
- Don't need to distribute to other machines

### Portable Build

**Command:**
```batch
build-windows-minsize.bat portable
```

**Features:**
- Generic x86-64 optimizations
- Runs on **any modern 64-bit CPU**
- Slightly larger and slower than native
- ✅ Safe for distribution

**Use when:**
- Building for distribution
- Running on different machines
- Want maximum compatibility

## 🔧 Manual Build (Advanced)

If you prefer to use CMake directly:

```batch
REM Configure
cmake --preset windows-clang-ninja-minsize

REM Build
cmake --build --preset windows-clang-ninja-minsize -j

REM Output
build-minsize\chatbot.exe
```

For portable build:
```batch
cmake --preset windows-clang-ninja-minsize-portable
cmake --build --preset windows-clang-ninja-minsize-portable -j
```

## 📏 Expected Results

With these optimizations, you should get:

| Metric | Value |
|--------|-------|
| **Executable Size** | ~2-5 MB (depending on features) |
| **Dependencies** | None (fully static) |
| **Runtime Libraries** | None required |
| **Performance** | Near-optimal |
| **Portability** | Windows 7+ (64-bit) |

## 🔍 Verification

Check your build:

```batch
REM Check executable size
dir build-minsize\chatbot.exe

REM Check dependencies (should show none or only Windows system DLLs)
dumpbin /dependents build-minsize\chatbot.exe

REM Test it works
build-minsize\chatbot.exe --test --user "Hello, world!"
```

## 🛠️ Optimization Details

The build uses these aggressive optimizations:

### Compiler Flags
- `-O3` - Maximum optimization
- `-march=native` - CPU-specific instructions (or `-march=x86-64` for portable)
- `-flto` - Link-Time Optimization
- `-ffunction-sections -fdata-sections` - Separate sections for better dead code elimination
- `-fno-exceptions -fno-rtti` - Disable C++ overhead
- `-fomit-frame-pointer` - Omit frame pointers

### Linker Flags
- `-static` - Static linking (no DLLs)
- `-s` - Strip all symbols
- `-Wl,--gc-sections` - Remove unused code
- `-Wl,--strip-all` - Strip everything possible

### CMake Options
- `BUILD_SHARED_LIBS=OFF` - Static libraries only
- `GGML_STATIC=ON` - Static GGML library
- `LLAMA_BUILD_TESTS=OFF` - Skip tests
- `LLAMA_BUILD_EXAMPLES=OFF` - Skip examples
- `LLAMA_BUILD_SERVER=OFF` - Skip server

## ❓ Troubleshooting

### Error: "clang++ not found"

**Solution:**
- Install LLVM-MinGW using `setup-llvm-mingw.ps1`
- Or manually add LLVM-MinGW to PATH:
  ```batch
  set PATH=C:\llvm-mingw\bin;%PATH%
  ```

### Error: "cmake not found"

**Solution:**
- Install CMake and make sure "Add to PATH" was selected
- Or manually add CMake to PATH

### Error: "ninja not found"

**Solution:**
- Download ninja.exe and put it in a folder that's in your PATH
- Or use MinGW Makefiles instead:
  ```batch
  cmake -G "MinGW Makefiles" -DCMAKE_BUILD_TYPE=Release ...
  ```

### Error: "Failed to load model"

**Solution:**
- Make sure the model file exists: `models/Phi-3-mini-4k-instruct-q4.gguf`
- Check that the path in `main.cpp` matches your model location

### Executable is too large

**Solution:**
- Verify you're building in Release mode (not Debug)
- Check that LTO is enabled (should see `-flto` in output)
- Make sure stripping is working (should see `-s` in output)
- Try the portable preset which may be slightly smaller

### Executable crashes or doesn't run

**Solution:**
- If using native build, try portable build instead:
  ```batch
  build-windows-minsize.bat portable
  ```
- Some CPU-specific optimizations may not be compatible
- Check you're on Windows 7 or later (64-bit)

### "Access Denied" when running PowerShell script

**Solution:**
- Run PowerShell as Administrator
- Enable script execution:
  ```powershell
  Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
  ```

## 🎓 Understanding the Build System

### Files Created

| File | Purpose |
|------|---------|
| `cmake/windows-clang-ninja-minsize.cmake` | Toolchain file with all optimization flags |
| `CMakePresets.json` | CMake preset configurations |
| `build-windows-minsize.bat` | Automated build script |
| `setup-llvm-mingw.ps1` | Automatic LLVM-MinGW installer |
| `cmake/README.md` | Detailed technical documentation |

### Build Directories

| Directory | Created By |
|-----------|------------|
| `build-minsize/` | Native build output |
| `build-minsize-portable/` | Portable build output |

### CMake Presets

The project includes two presets:

1. **windows-clang-ninja-minsize** (native)
   - Optimized for current CPU
   - Maximum performance
   - Smallest size

2. **windows-clang-ninja-minsize-portable** (portable)
   - Generic x86-64 optimizations
   - Runs on any modern CPU
   - Good for distribution

## 📚 Further Reading

- **Detailed technical docs:** See `cmake/README.md`
- **LLVM-MinGW:** https://github.com/mstorsjo/llvm-mingw
- **CMake documentation:** https://cmake.org/documentation/
- **Ninja build:** https://ninja-build.org/

## 🤝 Getting Help

If you encounter issues:

1. Check the **Troubleshooting** section above
2. Read `cmake/README.md` for advanced options
3. Verify all prerequisites are installed correctly
4. Make sure you're using a fresh Command Prompt (after installing tools)

## 📄 License

Same as the parent project.

---

**Happy Building! 🚀**

