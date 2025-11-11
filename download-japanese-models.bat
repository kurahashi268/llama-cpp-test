@echo off
REM Japanese Model Downloader for llama.cpp (Windows)
REM This script downloads popular Japanese language models in GGUF format

setlocal enabledelayedexpansion

set "MODELS_DIR=models"
if not exist "%MODELS_DIR%" mkdir "%MODELS_DIR%"

:menu
cls
echo ================================================================
echo      Japanese Model Downloader for llama.cpp
echo ================================================================
echo.
echo Available Japanese Models:
echo.
echo 1) Llama-3-ELYZA-JP-8B (Q4_K_M) - ~5GB [RECOMMENDED]
echo    - Latest Llama-3 based, excellent Japanese performance
echo    - Comparable to GPT-3.5 for Japanese tasks
echo.
echo 2) ELYZA-japanese-Llama-2-7b (Q4_K_M) - ~4GB
echo    - Llama-2 based, proven Japanese model
echo    - Good for general Japanese conversation
echo.
echo 3) Swallow-7b-instruct (Q4_K_M) - ~4GB
echo    - Tokyo Tech's Japanese model
echo    - Strong instruction-following in Japanese
echo.
echo 4) Llama-3-ELYZA-JP-8B (Q8_0) - ~8.5GB
echo    - Higher quality version (more VRAM needed)
echo    - Better accuracy, slower inference
echo.
echo 5) Qwen2.5-7B-Instruct (Q4_K_M) - ~4.7GB
echo    - Multilingual (Chinese/Japanese/English)
echo    - Very strong at Japanese despite being Chinese-focused
echo.
echo 6) ALL - Download all recommended models
echo.
echo 0) Exit
echo.

set /p choice="Select a model to download (0-6): "

if "%choice%"=="1" goto download_llama3_elyza_q4
if "%choice%"=="2" goto download_elyza_llama2
if "%choice%"=="3" goto download_swallow
if "%choice%"=="4" goto download_llama3_elyza_q8
if "%choice%"=="5" goto download_qwen25
if "%choice%"=="6" goto download_all
if "%choice%"=="0" goto end
echo Invalid choice. Please try again.
timeout /t 2 >nul
goto menu

:download_llama3_elyza_q4
echo.
echo Downloading: Llama-3 ELYZA JP 8B (Q4_K_M)
echo.
set "URL=https://huggingface.co/mmnga/Llama-3-ELYZA-JP-8B-gguf/resolve/main/Llama-3-ELYZA-JP-8B-q4_k_m.gguf"
set "FILENAME=Llama-3-ELYZA-JP-8B-q4_k_m.gguf"
call :download_file
goto menu_wait

:download_elyza_llama2
echo.
echo Downloading: ELYZA Japanese Llama-2 7B (Q4_K_M)
echo.
set "URL=https://huggingface.co/mmnga/ELYZA-japanese-Llama-2-7b-instruct-gguf/resolve/main/ELYZA-japanese-Llama-2-7b-instruct-q4_K_M.gguf"
set "FILENAME=ELYZA-japanese-Llama-2-7b-instruct-q4_K_M.gguf"
call :download_file
goto menu_wait

:download_swallow
echo.
echo Downloading: Swallow 7B Instruct (Q4_K_M)
echo.
set "URL=https://huggingface.co/mmnga/Swallow-7b-instruct-v0.1-gguf/resolve/main/Swallow-7b-instruct-v0.1-q4_k_m.gguf"
set "FILENAME=Swallow-7b-instruct-v0.1-q4_k_m.gguf"
call :download_file
goto menu_wait

:download_llama3_elyza_q8
echo.
echo Downloading: Llama-3 ELYZA JP 8B (Q8_0 - Higher Quality)
echo.
set "URL=https://huggingface.co/mmnga/Llama-3-ELYZA-JP-8B-gguf/resolve/main/Llama-3-ELYZA-JP-8B-q8_0.gguf"
set "FILENAME=Llama-3-ELYZA-JP-8B-q8_0.gguf"
call :download_file
goto menu_wait

:download_qwen25
echo.
echo Downloading: Qwen 2.5 7B Instruct (Q4_K_M)
echo.
set "URL=https://huggingface.co/bartowski/Qwen2.5-7B-Instruct-GGUF/resolve/main/Qwen2.5-7B-Instruct-Q4_K_M.gguf"
set "FILENAME=Qwen2.5-7B-Instruct-Q4_K_M.gguf"
call :download_file
goto menu_wait

:download_all
echo.
echo Downloading all recommended models...
echo.

echo [1/4] Llama-3 ELYZA JP 8B (Q4_K_M)
set "URL=https://huggingface.co/mmnga/Llama-3-ELYZA-JP-8B-gguf/resolve/main/Llama-3-ELYZA-JP-8B-q4_k_m.gguf"
set "FILENAME=Llama-3-ELYZA-JP-8B-q4_k_m.gguf"
call :download_file

echo [2/4] ELYZA Japanese Llama-2 7B (Q4_K_M)
set "URL=https://huggingface.co/mmnga/ELYZA-japanese-Llama-2-7b-instruct-gguf/resolve/main/ELYZA-japanese-Llama-2-7b-instruct-q4_K_M.gguf"
set "FILENAME=ELYZA-japanese-Llama-2-7b-instruct-q4_K_M.gguf"
call :download_file

echo [3/4] Swallow 7B Instruct (Q4_K_M)
set "URL=https://huggingface.co/mmnga/Swallow-7b-instruct-v0.1-gguf/resolve/main/Swallow-7b-instruct-v0.1-q4_k_m.gguf"
set "FILENAME=Swallow-7b-instruct-v0.1-q4_k_m.gguf"
call :download_file

echo [4/4] Qwen 2.5 7B Instruct (Q4_K_M)
set "URL=https://huggingface.co/bartowski/Qwen2.5-7B-Instruct-GGUF/resolve/main/Qwen2.5-7B-Instruct-Q4_K_M.gguf"
set "FILENAME=Qwen2.5-7B-Instruct-Q4_K_M.gguf"
call :download_file

echo.
echo All downloads complete!
goto menu_wait

:download_file
if exist "%MODELS_DIR%\%FILENAME%" (
    echo File already exists: %FILENAME%
    echo Skipping...
    echo.
    exit /b
)

echo Downloading from: %URL%
echo Destination: %MODELS_DIR%\%FILENAME%
echo.

REM Check if curl is available (Windows 10+ has it built-in)
where curl >nul 2>&1
if %errorlevel% equ 0 (
    curl -L --progress-bar -C - "%URL%" -o "%MODELS_DIR%\%FILENAME%"
) else (
    REM Fall back to PowerShell if curl is not available
    echo Using PowerShell to download...
    powershell -Command "& {[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; $ProgressPreference = 'SilentlyContinue'; Invoke-WebRequest -Uri '%URL%' -OutFile '%MODELS_DIR%\%FILENAME%' -UseBasicParsing}"
)

if %errorlevel% equ 0 (
    echo Download complete: %FILENAME%
    echo.
) else (
    echo Download failed for: %FILENAME%
    echo Please check your internet connection and try again.
    echo.
)
exit /b

:menu_wait
echo.
pause
goto menu

:end
echo Exiting...
exit /b 0

