# PowerShell script to download and setup LLVM-MinGW for Windows
# This script downloads LLVM-MinGW and sets it up for building

param(
    [string]$InstallPath = "C:\llvm-mingw",
    [string]$Version = "20241217",  # Latest stable version as of script creation
    [switch]$AddToPath = $true,
    [switch]$Force = $false
)

$ErrorActionPreference = "Stop"

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "LLVM-MinGW Setup Script" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host ""

# Determine architecture
$arch = if ([Environment]::Is64BitOperatingSystem) { "x86_64" } else { "i686" }
Write-Host "Detected architecture: $arch" -ForegroundColor Green

# Construct download URL
$fileName = "llvm-mingw-$Version-ucrt-$arch.zip"
$downloadUrl = "https://github.com/mstorsjo/llvm-mingw/releases/download/$Version/$fileName"
$tempZip = Join-Path $env:TEMP $fileName

Write-Host "Download URL: $downloadUrl" -ForegroundColor Gray
Write-Host "Install path: $InstallPath" -ForegroundColor Gray
Write-Host ""

# Check if already installed
if (Test-Path $InstallPath) {
    if ($Force) {
        Write-Host "Removing existing installation..." -ForegroundColor Yellow
        Remove-Item -Path $InstallPath -Recurse -Force
    } else {
        Write-Host "LLVM-MinGW appears to be already installed at: $InstallPath" -ForegroundColor Yellow
        Write-Host "Use -Force to reinstall" -ForegroundColor Yellow
        
        # Check if in PATH
        $envPath = [Environment]::GetEnvironmentVariable("Path", "User")
        $binPath = Join-Path $InstallPath "bin"
        if ($envPath -notlike "*$binPath*") {
            Write-Host ""
            Write-Host "Not in PATH. Adding to PATH..." -ForegroundColor Yellow
            [Environment]::SetEnvironmentVariable(
                "Path",
                "$envPath;$binPath",
                "User"
            )
            Write-Host "Added to PATH. Please restart your terminal." -ForegroundColor Green
        } else {
            Write-Host "Already in PATH." -ForegroundColor Green
        }
        exit 0
    }
}

# Download LLVM-MinGW
Write-Host "Downloading LLVM-MinGW..." -ForegroundColor Cyan
try {
    # Show progress
    $ProgressPreference = 'Continue'
    Invoke-WebRequest -Uri $downloadUrl -OutFile $tempZip -UseBasicParsing
    Write-Host "Download complete!" -ForegroundColor Green
} catch {
    Write-Host "ERROR: Failed to download LLVM-MinGW" -ForegroundColor Red
    Write-Host "URL: $downloadUrl" -ForegroundColor Red
    Write-Host "Error: $_" -ForegroundColor Red
    exit 1
}

# Extract
Write-Host ""
Write-Host "Extracting LLVM-MinGW..." -ForegroundColor Cyan
try {
    # Create installation directory
    $installDir = Split-Path $InstallPath -Parent
    if (-not (Test-Path $installDir)) {
        New-Item -ItemType Directory -Path $installDir -Force | Out-Null
    }
    
    # Extract
    Expand-Archive -Path $tempZip -DestinationPath $installDir -Force
    
    # Rename extracted folder to target name
    $extractedFolder = Join-Path $installDir "llvm-mingw-$Version-ucrt-$arch"
    if ($extractedFolder -ne $InstallPath) {
        if (Test-Path $InstallPath) {
            Remove-Item -Path $InstallPath -Recurse -Force
        }
        Rename-Item -Path $extractedFolder -NewName (Split-Path $InstallPath -Leaf)
    }
    
    Write-Host "Extraction complete!" -ForegroundColor Green
} catch {
    Write-Host "ERROR: Failed to extract LLVM-MinGW" -ForegroundColor Red
    Write-Host "Error: $_" -ForegroundColor Red
    exit 1
}

# Cleanup
Write-Host ""
Write-Host "Cleaning up..." -ForegroundColor Cyan
Remove-Item $tempZip -Force
Write-Host "Cleanup complete!" -ForegroundColor Green

# Add to PATH
if ($AddToPath) {
    Write-Host ""
    Write-Host "Adding LLVM-MinGW to PATH..." -ForegroundColor Cyan
    
    $binPath = Join-Path $InstallPath "bin"
    $envPath = [Environment]::GetEnvironmentVariable("Path", "User")
    
    if ($envPath -notlike "*$binPath*") {
        [Environment]::SetEnvironmentVariable(
            "Path",
            "$envPath;$binPath",
            "User"
        )
        Write-Host "Added to PATH (User environment variable)" -ForegroundColor Green
        Write-Host "Please restart your terminal for changes to take effect" -ForegroundColor Yellow
    } else {
        Write-Host "Already in PATH" -ForegroundColor Green
    }
}

# Verify installation
Write-Host ""
Write-Host "Verifying installation..." -ForegroundColor Cyan
$clangPath = Join-Path $InstallPath "bin\clang++.exe"
if (Test-Path $clangPath) {
    Write-Host "Installation successful!" -ForegroundColor Green
    Write-Host ""
    
    # Display version
    Write-Host "Compiler version:" -ForegroundColor Cyan
    & $clangPath --version | Select-Object -First 1
    
    Write-Host ""
    Write-Host "============================================================" -ForegroundColor Cyan
    Write-Host "Setup Complete!" -ForegroundColor Green
    Write-Host "============================================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "LLVM-MinGW is installed at: $InstallPath" -ForegroundColor White
    Write-Host ""
    Write-Host "Next steps:" -ForegroundColor Yellow
    Write-Host "  1. Restart your terminal (or open a new one)" -ForegroundColor White
    Write-Host "  2. Run: build-windows-minsize.bat" -ForegroundColor White
    Write-Host ""
    Write-Host "Or manually add to PATH for current session:" -ForegroundColor Yellow
    Write-Host "  `$env:Path += `";$binPath`"" -ForegroundColor Gray
    Write-Host ""
} else {
    Write-Host "ERROR: Installation verification failed" -ForegroundColor Red
    Write-Host "clang++.exe not found at: $clangPath" -ForegroundColor Red
    exit 1
}

