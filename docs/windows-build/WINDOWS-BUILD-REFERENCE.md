# Windows Build Quick Reference Card

## 🎯 Goal
Build the **smallest and lightest** Windows executable using LLVM-MinGW with static linking and aggressive optimization.

---

## 📦 Required Tools

| Tool | Version | Download |
|------|---------|----------|
| **LLVM-MinGW** | Latest | https://github.com/mstorsjo/llvm-mingw/releases |
| **CMake** | 3.21+ | https://cmake.org/download/ |
| **Ninja** | Latest | https://github.com/ninja-build/ninja/releases |

### Quick Install (PowerShell as Administrator)
```powershell
.\setup-llvm-mingw.ps1
```

---

## 🚀 Build Commands

### Automated Build (Recommended)

```batch
REM Native build (fastest, optimized for your CPU)
build-windows-minsize.bat

REM Portable build (runs on any x86-64 CPU)
build-windows-minsize.bat portable
```

### Manual Build

```batch
REM Configure
cmake --preset windows-clang-ninja-minsize

REM Build
cmake --build --preset windows-clang-ninja-minsize -j

REM Output
build-minsize\chatbot.exe
```

---

## 🎮 Run the Chatbot

```batch
REM Interactive mode
build-minsize\chatbot.exe --test

REM With streaming
build-minsize\chatbot.exe --test --stream

REM One-shot query
build-minsize\chatbot.exe --test --user "What is C++?"

REM Custom system prompt
build-minsize\chatbot.exe --test --system "You are a coding expert"

REM Unlimited tokens
build-minsize\chatbot.exe --test --max-tokens 0
```

---

## ⚙️ CMake Presets

| Preset | CPU Target | SIMD | Portable | Use Case |
|--------|-----------|------|----------|----------|
| **windows-clang-ninja-minsize** | Native | All | ❌ | Building for same machine |
| **windows-clang-ninja-minsize-portable** | x86-64 | None | ✅ | Distribution |

---

## 🔧 Compiler & Linker Flags

### Compiler Flags (Applied)
```
-O3                     # Maximum optimization
-march=native           # CPU-specific optimizations (or -march=x86-64)
-flto                   # Link-Time Optimization
-ffunction-sections     # Separate sections for DCE
-fdata-sections         # Separate data sections
-fno-exceptions         # No C++ exceptions
-fno-rtti               # No RTTI
-fomit-frame-pointer    # Omit frame pointers
-fno-stack-protector    # No stack protection
```

### Linker Flags (Applied)
```
-s                      # Strip symbols
-flto                   # Link-Time Optimization
-static                 # Static linking (no DLLs)
-Wl,--gc-sections       # Remove unused code
-Wl,--strip-all         # Strip everything
```

---

## 📊 Expected Results

| Metric | Value |
|--------|-------|
| **Size** | 2-5 MB |
| **Dependencies** | 0 (fully static) |
| **Target OS** | Windows 7+ (64-bit) |
| **Performance** | O3 + LTO optimized |

---

## 🛠️ Directory Structure

```
llama-cpp/
├── cmake/
│   └── windows-clang-ninja-minsize.cmake   # Toolchain file
├── .vscode/                                 # VS Code integration
├── CMakePresets.json                        # Build presets
├── build-windows-minsize.bat                # Build script
├── setup-llvm-mingw.ps1                     # LLVM-MinGW installer
├── build-minsize/                           # Native build output
│   └── chatbot.exe
└── build-minsize-portable/                  # Portable build output
    └── chatbot.exe
```

---

## 🐛 Common Issues & Fixes

### "clang++ not found"
```powershell
# Install LLVM-MinGW
.\setup-llvm-mingw.ps1

# OR add to PATH manually
$env:Path += ";C:\llvm-mingw\bin"
```

### "ninja not found"
```batch
REM Download ninja.exe and add to PATH
REM OR use MinGW Makefiles instead:
cmake -G "MinGW Makefiles" -DCMAKE_BUILD_TYPE=Release ...
```

### Executable too large
```batch
REM Verify Release build
type build-minsize\CMakeCache.txt | findstr CMAKE_BUILD_TYPE

REM Should show: CMAKE_BUILD_TYPE:STRING=Release
```

### Executable crashes
```batch
REM Try portable build
build-windows-minsize.bat portable
```

### "Access Denied" (PowerShell)
```powershell
# Run as Administrator
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
```

---

## 📏 Build Variants Comparison

| Feature | Native Build | Portable Build |
|---------|--------------|----------------|
| **Command** | `build-windows-minsize.bat` | `build-windows-minsize.bat portable` |
| **CPU Target** | Current CPU (`-march=native`) | Generic x86-64 |
| **SIMD** | AVX, AVX2, FMA, F16C | None |
| **Size** | Smallest | Slightly larger |
| **Speed** | Fastest | Slightly slower |
| **Portability** | Same CPU only | Any x86-64 CPU |
| **Use Case** | Personal use | Distribution |

---

## 🎨 VS Code Integration

### Install Extensions
- CMake Tools (ms-vscode.cmake-tools)
- CMake Language Support (twxs.cmake)
- C/C++ Extension Pack (ms-vscode.cpptools-extension-pack)

### Build Tasks
- **Ctrl+Shift+B** → Build Windows (Native)
- **Ctrl+Shift+P** → `CMake: Configure`
- **Ctrl+Shift+P** → `CMake: Build`

---

## 📚 Documentation Files

| File | Description |
|------|-------------|
| **WINDOWS-BUILD-QUICKSTART.md** | Complete Windows build guide |
| **BUILD_CONFIGURATION_SUMMARY.md** | Detailed build system overview |
| **cmake/README.md** | Technical details & customization |
| **WINDOWS-BUILD-REFERENCE.md** | This quick reference card |

---

## 🔍 Verification

```batch
REM Check file size
dir build-minsize\chatbot.exe

REM Check dependencies (should show minimal/none)
dumpbin /dependents build-minsize\chatbot.exe

REM Test execution
build-minsize\chatbot.exe --test --user "Hello!"
```

---

## 💡 Optimization Details

### Build Type: Release
- `-O3` optimization level
- NDEBUG defined (assertions disabled)
- Symbol stripping enabled
- Static linking enabled

### Size Reduction Techniques
1. **Link-Time Optimization (LTO)** - Cross-module optimization
2. **Dead Code Elimination** - Remove unused functions
3. **Symbol Stripping** - Remove debug symbols
4. **Static Linking** - No external DLLs
5. **Section GC** - Garbage collect unused sections
6. **No Exceptions** - Disable C++ exceptions
7. **No RTTI** - Disable runtime type information

---

## 🔗 Quick Links

- **LLVM-MinGW:** https://github.com/mstorsjo/llvm-mingw
- **CMake:** https://cmake.org/
- **Ninja:** https://ninja-build.org/
- **llama.cpp:** https://github.com/ggerganov/llama.cpp

---

## 📋 Checklist

Before building:
- [ ] LLVM-MinGW installed and in PATH
- [ ] CMake 3.21+ installed
- [ ] Ninja installed
- [ ] Model file present: `models/*.gguf`

After building:
- [ ] Executable exists in `build-minsize/`
- [ ] Size is reasonable (~2-5 MB)
- [ ] No external DLL dependencies
- [ ] Runs successfully: `chatbot.exe --test`

---

## 🎯 One-Line Summary

**Build the smallest Windows executable with LLVM-MinGW using static linking, LTO, and aggressive optimization.**

```batch
build-windows-minsize.bat
```

---

**Version:** 1.0  
**Last Updated:** October 2025  
**Platform:** Windows 7+ (64-bit)

