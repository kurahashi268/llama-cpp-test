@echo off
REM Build script for Windows with LLVM-MinGW optimized for minimum size
REM Requirements:
REM   - LLVM-MinGW installed and in PATH (https://github.com/mstorsjo/llvm-mingw/releases)
REM   - CMake 3.21+ installed
REM   - Ninja build system installed

echo ============================================================
echo Building llama-cpp chatbot with LLVM-MinGW (Minimum Size)
echo ============================================================
echo.

REM Check if LLVM-MinGW is in PATH
where clang++ >nul 2>&1
if %errorlevel% neq 0 (
    echo ERROR: clang++ not found in PATH
    echo Please install LLVM-MinGW and add it to PATH
    echo Download from: https://github.com/mstorsjo/llvm-mingw/releases
    echo.
    echo You can also run setup-llvm-mingw.ps1 to download and setup automatically
    exit /b 1
)

REM Check if CMake is installed
where cmake >nul 2>&1
if %errorlevel% neq 0 (
    echo ERROR: cmake not found in PATH
    echo Please install CMake from: https://cmake.org/download/
    exit /b 1
)

REM Check if Ninja is installed
where ninja >nul 2>&1
if %errorlevel% neq 0 (
    echo ERROR: ninja not found in PATH
    echo Please install Ninja from: https://github.com/ninja-build/ninja/releases
    exit /b 1
)

echo Environment check passed!
echo.

REM Display compiler information
echo Compiler Information:
clang++ --version | findstr /C:"clang version"
echo.

REM Choose preset based on command line argument
set PRESET=windows-clang-ninja-minsize
if "%1"=="portable" (
    set PRESET=windows-clang-ninja-minsize-portable
    echo Using PORTABLE preset (no CPU-specific optimizations)
) else (
    echo Using NATIVE preset (optimized for current CPU)
    echo Use "build-windows-minsize.bat portable" for portable build
)
echo.

REM Configure with CMake preset
echo Configuring with preset: %PRESET%
echo --------------------------------------------------------
cmake --preset %PRESET%
if %errorlevel% neq 0 (
    echo.
    echo ERROR: CMake configuration failed!
    exit /b 1
)

echo.
echo Configuration successful!
echo.

REM Build
echo Building...
echo --------------------------------------------------------
cmake --build --preset %PRESET% -j %NUMBER_OF_PROCESSORS%
if %errorlevel% neq 0 (
    echo.
    echo ERROR: Build failed!
    exit /b 1
)

echo.
echo ============================================================
echo Build completed successfully!
echo ============================================================
echo.

REM Determine build directory based on preset
if "%PRESET%"=="windows-clang-ninja-minsize-portable" (
    set BUILD_DIR=build-minsize-portable
) else (
    set BUILD_DIR=build-minsize
)

REM Display build output information
if exist "%BUILD_DIR%\chatbot.exe" (
    echo Output executable: %BUILD_DIR%\chatbot.exe
    echo.
    echo File size:
    dir "%BUILD_DIR%\chatbot.exe" | findstr chatbot.exe
    echo.
    echo You can now run: %BUILD_DIR%\chatbot.exe --test
) else (
    echo WARNING: chatbot.exe not found in expected location
)

echo.
echo To run in test mode: %BUILD_DIR%\chatbot.exe --test
echo To see all options: %BUILD_DIR%\chatbot.exe --help
echo.
pause

