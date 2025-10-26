# CMake Toolchain Files

This directory contains CMake toolchain files for cross-compilation and optimized builds.

## Available Toolchains

### `windows-clang-ninja-minsize.cmake`
**Purpose**: Build the smallest possible executable on Windows

**Target**: Windows x64 (MinGW)  
**Compiler**: LLVM-MinGW (Clang)  
**Build System**: Ninja  
**Optimization**: Size (`-Os`)  

**Features**:
- Link-Time Optimization (LTO)
- Dead code elimination
- Symbol stripping
- Static linking (no DLL dependencies)
- Disabled exceptions and RTTI
- Custom MinGW optimizations

**Usage**:
```powershell
# Using CMake preset (recommended)
cmake --preset windows-clang-ninja-minsize

# Or manual
cmake -B build -G Ninja -DCMAKE_TOOLCHAIN_FILE=cmake/windows-clang-ninja-minsize.cmake
```

**Requirements**:
- LLVM-MinGW installed and in PATH
- Ninja build system
- CMake 3.21+

**Expected Results**:
- Executable size: 5-8 MB (vs 15-20 MB with MSVC)
- Standalone binary with no external dependencies
- 50-60% size reduction

## Adding New Toolchains

To add a new toolchain file:

1. Create `<target>-<compiler>.cmake` in this directory
2. Set appropriate compiler and linker flags
3. Add corresponding preset in `CMakePresets.json`
4. Document usage in this README

## See Also

- `../CMakePresets.json` - Build presets using these toolchains
- `../WINDOWS-BUILD-QUICKSTART.md` - Quick start guide for Windows builds
- `../LLVM-MINGW-SETUP.md` - Detailed LLVM-MinGW setup

