@echo off
setlocal

echo =============================================================================
echo   llama.cpp CPU (LLVM-MinGW) x64 Build Script for Windows
echo =============================================================================

:: Configuration
set "BUILD_TYPE=Release"
set "BUILD_DIR=build-cpu-x64-llvm"
set "GENERATOR=Ninja"

echo [INFO] Build type   : %BUILD_TYPE%
echo [INFO] Build dir    : %BUILD_DIR%
echo [INFO] Generator    : %GENERATOR%
echo [INFO] CPU only     : ON (No CUDA/Vulkan/SYCL)
echo =============================================================================

:: Clean previous build
if exist "%BUILD_DIR%" (
    echo [INFO] Cleaning previous build...
    rmdir /s /q "%BUILD_DIR%"
)
mkdir "%BUILD_DIR%"
cd "%BUILD_DIR%"

:: Configure CMake
echo [INFO] Configuring CMake project...
cmake -G "%GENERATOR%" ^
  -DCMAKE_BUILD_TYPE=%BUILD_TYPE% ^
  -DCMAKE_C_COMPILER="C:/llvm-mingw/bin/clang.exe" ^
  -DCMAKE_CXX_COMPILER="C:/llvm-mingw/bin/clang++.exe" ^
  -DGGML_CUDA=OFF ^
  -DGGML_VULKAN=OFF ^
  -DGGML_HIPBLAS=OFF ^
  -DGGML_SYCL=OFF ^
  -DLLAMA_CUBLAS=OFF ^
  -DLLAMA_METAL=OFF ^
  -DLLAMA_CLBLAST=OFF ^
  ..

if errorlevel 1 (
    echo [ERROR] ❌ CMake configuration failed.
    exit /b 1
)

:: Build with Ninja
echo [INFO] Building project with Ninja...
ninja -j1

if errorlevel 1 (
    echo [ERROR] ❌ Build failed.
    exit /b 1
)

echo =============================================================================
echo   ✅ Build completed successfully (x64 CPU-only)
echo   Output folder: %CD%
echo =============================================================================

endlocal
pause
