@echo off
REM Build script for Windows with LLVM-MinGW + Ninja for minimum size executable

echo ================================================
echo Building with LLVM-MinGW + Ninja for Minimum Size
echo ================================================
echo.

REM Check if required tools are available
where clang >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: clang not found in PATH
    echo Please install LLVM-MinGW and add it to PATH
    echo Download from: https://github.com/mstorsjo/llvm-mingw/releases
    exit /b 1
)

where ninja >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: ninja not found in PATH
    echo Please install Ninja and add it to PATH
    exit /b 1
)

where cmake >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: cmake not found in PATH
    echo Please install CMake and add it to PATH
    exit /b 1
)

echo All required tools found!
echo.

REM Configure
echo ================================================
echo Configuring project...
echo ================================================
cmake --preset windows-clang-ninja-minsize
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: Configuration failed
    exit /b 1
)

echo.
echo ================================================
echo Building project...
echo ================================================
cmake --build build-minsize --config Release -j
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: Build failed
    exit /b 1
)

echo.
echo ================================================
echo Build successful!
echo ================================================
echo.
echo Executable location: build-minsize\bin\chatbot.exe
echo.

REM Show file size
if exist build-minsize\bin\chatbot.exe (
    echo Executable size:
    dir build-minsize\bin\chatbot.exe | find "chatbot.exe"
)

echo.
echo To run: build-minsize\bin\chatbot.exe
echo.

