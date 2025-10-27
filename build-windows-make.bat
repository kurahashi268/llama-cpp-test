@echo off
REM Alternative build script using MinGW Makefiles instead of Ninja
REM This avoids file locking issues common with Ninja on Windows
REM Requirements:
REM   - LLVM-MinGW installed and in PATH
REM   - CMake 3.21+ installed

echo ============================================================
echo Building llama-cpp chatbot with LLVM-MinGW (Using Make)
echo Alternative to Ninja - Avoids File Locking Issues
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

REM Check if mingw32-make is available
where mingw32-make >nul 2>&1
if %errorlevel% neq 0 (
    echo WARNING: mingw32-make not found in PATH
    echo This is OK if make.exe or gmake.exe is available
    echo CMake will find the appropriate make program
)

echo Environment check passed!
echo.

REM Display compiler information
echo Compiler Information:
clang++ --version | findstr /C:"clang version"
echo.

echo Using MinGW Makefiles (avoids Ninja file locking issues)
echo.

REM Configure with CMake preset
echo Configuring with preset: windows-clang-make-minsize
echo --------------------------------------------------------
cmake --preset windows-clang-make-minsize
if %errorlevel% neq 0 (
    echo.
    echo ERROR: CMake configuration failed!
    echo.
    echo Troubleshooting:
    echo 1. Make sure mingw32-make.exe is available (comes with LLVM-MinGW)
    echo 2. Try cleaning: rmdir /s /q build-minsize-make
    echo 3. Check that your path doesn't have spaces that need escaping
    exit /b 1
)

echo.
echo Configuration successful!
echo.

REM Build
echo Building...
echo --------------------------------------------------------
cmake --build --preset windows-clang-make-minsize -j %NUMBER_OF_PROCESSORS%
if %errorlevel% neq 0 (
    echo.
    echo ERROR: Build failed!
    echo.
    echo If you see "process cannot access the file" errors:
    echo 1. Add build directory to Windows Defender exclusions
    echo 2. Close all IDEs and file explorers
    echo 3. Try building again
    exit /b 1
)

echo.
echo ============================================================
echo Build completed successfully!
echo ============================================================
echo.

REM Display build output information
if exist "build-minsize-make\chatbot.exe" (
    echo Output executable: build-minsize-make\chatbot.exe
    echo.
    echo File size:
    dir "build-minsize-make\chatbot.exe" | findstr chatbot.exe
    echo.
    echo You can now run: build-minsize-make\chatbot.exe --test
) else (
    echo WARNING: chatbot.exe not found in expected location
)

echo.
echo To run in test mode: build-minsize-make\chatbot.exe --test
echo To see all options: build-minsize-make\chatbot.exe --help
echo.
pause

