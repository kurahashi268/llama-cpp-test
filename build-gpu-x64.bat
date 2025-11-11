@echo off
setlocal

echo =============================================================================
echo   llama.cpp GPU (CUDA) x64 Build Script for Windows
echo =============================================================================

:: Configuration
set "BUILD_TYPE=Release"
set "BUILD_DIR=build-cuda-x64"
set "GENERATOR=Visual Studio 17 2022"
set "ARCH_FLAG=-A x64"
set "CUDA_FLAG=-DGGML_CUDA=ON"

echo [INFO] Build type   : %BUILD_TYPE%
echo [INFO] Build dir    : %BUILD_DIR%
echo [INFO] Generator    : %GENERATOR%
echo [INFO] Architecture : x64
echo [INFO] CUDA support : ON
echo =============================================================================

REM Add the path of cl.exe
SET "PATH=%PATH%;C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Tools\MSVC\14.44.35207\bin\Hostx64\x64"

set "CUDAHOSTCXX=C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Tools\MSVC\14.44.35207\bin\Hostx64\x64\cl.exe"

:: Check MSVC compiler (cl.exe)
where cl.exe >nul 2>nul
if errorlevel 1 (
    echo [ERROR] cl.exe not found in PATH.
    echo [HINT] Open "x64 Native Tools Command Prompt for VS 2022" and re-run this script.
    exit /b 1
)

:: Check CUDA compiler (nvcc)
where nvcc.exe >nul 2>nul
if errorlevel 1 (
    echo [ERROR] nvcc.exe not found in PATH.
    echo [HINT] Please install the NVIDIA CUDA Toolkit and ensure its 'bin' folder is in PATH.
    exit /b 1
)

:: Create and enter build directory
if not exist "%BUILD_DIR%" mkdir "%BUILD_DIR%"
cd "%BUILD_DIR%"

:: Configure CMake
echo [INFO] Configuring CMake project...
cmake -G "%GENERATOR%" %ARCH_FLAG% -DCMAKE_BUILD_TYPE=%BUILD_TYPE% %CUDA_FLAG% ..

if errorlevel 1 (
    echo [ERROR] CMake configuration failed.
    exit /b 1
)

:: Build
echo [INFO] Building project...
cmake --build . --config %BUILD_TYPE%
if errorlevel 1 (
    echo [ERROR] Build failed.
    exit /b 1
)

echo =============================================================================
echo   Build completed successfully (x64 CUDA)
echo   Output folder: %CD%\bin\%BUILD_TYPE%
echo =============================================================================

endlocal
pause

