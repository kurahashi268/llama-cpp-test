# LLVM-MinGW Setup Guide for Minimum Size Build

This guide shows how to set up LLVM-MinGW to build the smallest possible executable on Windows.

## Why LLVM-MinGW?

**Advantages:**
- ✅ No Visual Studio required
- ✅ Standalone toolchain (just extract and use)
- ✅ Produces smaller executables than MSVC
- ✅ Better optimization with LLD linker
- ✅ Static linking for standalone EXE
- ✅ Open source and actively maintained

**vs Regular LLVM/Clang:**
- LLVM/Clang uses MSVC runtime (requires Visual Studio)
- LLVM-MinGW uses MinGW-w64 runtime (standalone)
- LLVM-MinGW is specifically tuned for Windows

## Installation

### Step 1: Download LLVM-MinGW

1. Go to: https://github.com/mstorsjo/llvm-mingw/releases
2. Download the latest release for your architecture:
   - **For x64 Windows**: `llvm-mingw-*-ucrt-x86_64.zip`
   - **For ARM64 Windows**: `llvm-mingw-*-ucrt-aarch64.zip`

Example: `llvm-mingw-20241119-ucrt-x86_64.zip` (replace with latest version)

### Step 2: Extract

Extract to a short path without spaces:
```
C:\llvm-mingw\
```

**❌ Avoid paths like:**
- `C:\Program Files\llvm-mingw\` (has spaces)
- `D:\My Tools\LLVM MinGW\` (has spaces)

### Step 3: Add to PATH

**Option A: PowerShell (Temporary - current session only)**
```powershell
$env:PATH = "C:\llvm-mingw\bin;$env:PATH"
```

**Option B: System Environment Variables (Permanent)**
1. Open **System Properties** → **Environment Variables**
2. Under **System variables**, find `Path`
3. Click **Edit** → **New**
4. Add: `C:\llvm-mingw\bin`
5. Click **OK** on all dialogs
6. **Restart PowerShell/CMD**

### Step 4: Verify Installation

Open a **new** PowerShell/CMD and run:
```powershell
clang --version
```

You should see output like:
```
clang version 18.1.8
Target: x86_64-w64-windows-gnu
Thread model: posix
```

Also verify:
```powershell
clang++ --version
ld.lld --version
llvm-ar --version
```

## Additional Tools

### Install Ninja

**Option 1: Standalone**
1. Download: https://github.com/ninja-build/ninja/releases
2. Extract `ninja.exe` to `C:\llvm-mingw\bin\` (reuse LLVM-MinGW path)

**Option 2: Via winget**
```powershell
winget install Ninja-build.Ninja
```

### Install CMake

**Option 1: Via winget**
```powershell
winget install Kitware.CMake
```

**Option 2: Standalone**
1. Download: https://cmake.org/download/
2. Install and check "Add to PATH"

Verify:
```powershell
cmake --version
ninja --version
```

## Building the Project

### Quick Build
```powershell
cd D:\path\to\llama-cpp
.\build-windows-minsize.bat
```

### Manual Build
```powershell
# Configure
cmake --preset windows-clang-ninja-minsize

# Build
cmake --build build-minsize --config Release -j
```

### Alternative: Shorter Path
If you have issues with long paths, move your project:
```powershell
# Copy to shorter path
mkdir C:\llama
Copy-Item -Recurse "D:\Work\OOO\New Medinote Engine\LLM Engine\llama-cpp-test" C:\llama\

# Build from there
cd C:\llama
cmake --preset windows-clang-ninja-minsize
cmake --build build-minsize --config Release
```

## Optimization Flags Explained

The toolchain uses these LLVM-MinGW specific optimizations:

| Flag | Purpose |
|------|---------|
| `-target x86_64-w64-mingw32` | Target MinGW runtime |
| `-Os` | Optimize for size |
| `-flto` | Link-Time Optimization |
| `-ffunction-sections -fdata-sections` | Separate sections for dead code elimination |
| `-Wl,--gc-sections` | Remove unused sections |
| `-Wl,--strip-all` | Strip all symbols |
| `-s` | Additional stripping |
| `-static` | Static link (no DLL dependencies) |
| `-fno-exceptions -fno-rtti` | Disable C++ overhead |
| `-fno-asynchronous-unwind-tables` | Remove unwind tables |
| `__USE_MINGW_ANSI_STDIO=0` | Use smaller printf implementation |

## Troubleshooting

### Issue: "clang not found"
**Solution:**
- Verify LLVM-MinGW is in PATH: `echo $env:PATH` (PowerShell)
- Restart terminal after adding to PATH
- Use full path: `C:\llvm-mingw\bin\clang.exe --version`

### Issue: Ninja file locking errors
**Solution:**
```powershell
# 1. Clean build
Remove-Item -Recurse -Force build-minsize

# 2. Exclude from Windows Defender
Add-MpPreference -ExclusionPath "C:\llama\build-minsize"

# 3. Use fewer parallel jobs
cmake --build build-minsize --config Release -j 2
```

### Issue: Path too long
**Solution:**
- Enable long paths in Windows:
```powershell
# Run as Administrator
New-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem" -Name "LongPathsEnabled" -Value 1 -PropertyType DWORD -Force
```
- Or move project to shorter path (e.g., `C:\llama\`)

### Issue: Missing DLL dependencies
**Solution:**
The `-static` flag should include everything, but if you still see missing DLLs:
```powershell
# Check dependencies
C:\llvm-mingw\bin\llvm-objdump.exe -p build-minsize\bin\chatbot.exe | Select-String "DLL Name"

# If it shows unwanted DLLs, add to CMakeLists.txt:
target_link_options(chatbot PRIVATE -static-libgcc -static-libstdc++)
```

## Size Comparison

Expected sizes with LLVM-MinGW (approximate):

| Build Type | Size | Notes |
|------------|------|-------|
| MSVC Default | ~15-20 MB | With MSVC runtime |
| LLVM-MinGW Default | ~10-15 MB | With MinGW runtime |
| LLVM-MinGW Optimized | **5-8 MB** | With all optimizations |
| + UPX Compressed | **2-3 MB** | With UPX --best --lzma |

## Further Optimization

### UPX Compression (Optional)
```powershell
# Download UPX: https://upx.github.io/
C:\path\to\upx.exe --best --lzma build-minsize\bin\chatbot.exe
```

**Warning:** UPX may trigger false positives in antivirus software.

### Strip with LLVM tools
```powershell
# Additional stripping (if needed)
C:\llvm-mingw\bin\llvm-strip.exe --strip-all build-minsize\bin\chatbot.exe
```

## Environment Variable Setup Script

Save as `setup-llvm-mingw.ps1`:
```powershell
# LLVM-MinGW Environment Setup
$LLVM_MINGW_PATH = "C:\llvm-mingw\bin"

if (Test-Path $LLVM_MINGW_PATH) {
    $env:PATH = "$LLVM_MINGW_PATH;$env:PATH"
    Write-Host "✓ LLVM-MinGW added to PATH" -ForegroundColor Green
    clang --version | Select-Object -First 1
} else {
    Write-Host "✗ LLVM-MinGW not found at $LLVM_MINGW_PATH" -ForegroundColor Red
    Write-Host "  Download from: https://github.com/mstorsjo/llvm-mingw/releases"
}
```

Run before building:
```powershell
.\setup-llvm-mingw.ps1
.\build-windows-minsize.bat
```

## Quick Reference

```powershell
# Setup (one-time)
# 1. Download LLVM-MinGW
# 2. Extract to C:\llvm-mingw
# 3. Add C:\llvm-mingw\bin to PATH

# Build
cd path\to\llama-cpp
.\build-windows-minsize.bat

# Clean rebuild
Remove-Item -Recurse -Force build-minsize
cmake --preset windows-clang-ninja-minsize
cmake --build build-minsize --config Release

# Run
.\build-minsize\bin\chatbot.exe
```

## Resources

- **LLVM-MinGW**: https://github.com/mstorsjo/llvm-mingw
- **Ninja**: https://github.com/ninja-build/ninja
- **CMake**: https://cmake.org/
- **UPX**: https://upx.github.io/

