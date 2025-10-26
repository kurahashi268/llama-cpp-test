# 🚀 Windows Build Quick Start (LLVM-MinGW)

Build the smallest possible executable on Windows using LLVM-MinGW + Ninja.

## ⚡ Super Quick Start

### 1. Install LLVM-MinGW
```powershell
# Download from: https://github.com/mstorsjo/llvm-mingw/releases
# Get: llvm-mingw-*-ucrt-x86_64.zip
# Extract to: C:\llvm-mingw
# Add to PATH: C:\llvm-mingw\bin
```

### 2. Install Ninja & CMake
```powershell
winget install Ninja-build.Ninja
winget install Kitware.CMake
```

### 3. Build!
```powershell
cd path\to\llama-cpp

# Verify setup
.\setup-llvm-mingw.ps1

# Build
.\build-windows-minsize.bat
```

**Done!** Your executable is at: `build-minsize\bin\chatbot.exe`

---

## 📦 What You Get

- **5-8 MB** standalone executable (vs 15-20 MB with MSVC)
- **No DLL dependencies** - single file, runs anywhere
- **Fully optimized** - LTO, dead code elimination, stripped
- **Static linked** - includes all libraries

---

## 🛠️ Manual Build

```powershell
# Setup environment
.\setup-llvm-mingw.ps1

# Configure
cmake --preset windows-clang-ninja-minsize

# Build (use all CPU cores)
cmake --build build-minsize --config Release -j

# Run
.\build-minsize\bin\chatbot.exe
```

---

## 🔧 Build Options

### Option 1: Minimum Size (Recommended)
Optimized for size with AVX2 support for modern CPUs:
```powershell
cmake --preset windows-clang-ninja-minsize
cmake --build build-minsize --config Release
```

### Option 2: Portable Build
Maximum compatibility, runs on older CPUs (no AVX):
```powershell
cmake --preset windows-clang-ninja-minsize-portable
cmake --build build-minsize-portable --config Release
```

---

## 🩹 Troubleshooting

### "clang not found"
```powershell
# Check installation
C:\llvm-mingw\bin\clang.exe --version

# Add to PATH (current session)
$env:PATH = "C:\llvm-mingw\bin;$env:PATH"
```

### Ninja file locking errors
```powershell
# Clean and rebuild
Remove-Item -Recurse -Force build-minsize
cmake --preset windows-clang-ninja-minsize
cmake --build build-minsize --config Release -j 2

# Exclude from Windows Defender (Run as Admin)
Add-MpPreference -ExclusionPath "C:\path\to\llama-cpp\build-minsize"
```

### Path too long error
```powershell
# Move to shorter path
mkdir C:\llama
Copy-Item -Recurse . C:\llama\
cd C:\llama
```

---

## 📊 Size Comparison

| Build Method | Executable Size | Notes |
|--------------|----------------|-------|
| MSVC (default) | ~15-20 MB | Requires MSVC runtime |
| LLVM-MinGW (default) | ~10-15 MB | Standard build |
| **LLVM-MinGW (optimized)** | **5-8 MB** | ⭐ This config |
| + UPX compression | ~2-3 MB | Optional |

---

## 🎯 Optimization Flags Used

```
-Os                    # Optimize for size
-flto                  # Link-Time Optimization
-ffunction-sections    # Separate functions
-fdata-sections        # Separate data
-Wl,--gc-sections      # Remove unused code
-Wl,--strip-all        # Strip symbols
-static                # Static linking (no DLLs)
-fno-exceptions        # Disable exceptions
-fno-rtti              # Disable RTTI
```

See full details in: `cmake/windows-clang-ninja-minsize.cmake`

---

## 📚 More Information

- **Full Setup Guide**: `LLVM-MINGW-SETUP.md`
- **Toolchain Config**: `cmake/windows-clang-ninja-minsize.cmake`
- **Build Presets**: `CMakePresets.json`

---

## ❓ FAQ

**Q: Do I need Visual Studio?**  
A: No! LLVM-MinGW is completely standalone.

**Q: Will the EXE work on other computers?**  
A: Yes! It's statically linked with no external dependencies.

**Q: Can I make it even smaller?**  
A: Yes, use UPX compression:
```powershell
upx --best --lzma build-minsize\bin\chatbot.exe
```

**Q: Why LLVM-MinGW instead of regular LLVM?**  
A: LLVM-MinGW produces smaller standalone executables without requiring MSVC.

**Q: Is it slower than -O3 builds?**  
A: Negligible difference for I/O-bound tasks like LLM inference. Size optimization is worth it.

---

## 🎉 Success Check

After building, verify:
```powershell
# Check size
(Get-Item build-minsize\bin\chatbot.exe).Length / 1MB

# Check for DLL dependencies (should show none or only system DLLs)
C:\llvm-mingw\bin\llvm-objdump.exe -p build-minsize\bin\chatbot.exe | Select-String "DLL Name"

# Test run
.\build-minsize\bin\chatbot.exe --help
```

Happy building! 🚀

