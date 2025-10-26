# LLVM-MinGW Environment Setup Script
# Run this before building to ensure LLVM-MinGW is in PATH

param(
    [string]$LlvmMingwPath = "C:\llvm-mingw"
)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "   LLVM-MinGW Environment Setup" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$binPath = Join-Path $LlvmMingwPath "bin"

# Check if LLVM-MinGW exists
if (-not (Test-Path $binPath)) {
    Write-Host "✗ LLVM-MinGW not found at: $binPath" -ForegroundColor Red
    Write-Host ""
    Write-Host "Please install LLVM-MinGW:" -ForegroundColor Yellow
    Write-Host "  1. Download from: https://github.com/mstorsjo/llvm-mingw/releases" -ForegroundColor Yellow
    Write-Host "  2. Extract to: $LlvmMingwPath" -ForegroundColor Yellow
    Write-Host "  3. Run this script again" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Or specify custom path:" -ForegroundColor Yellow
    Write-Host "  .\setup-llvm-mingw.ps1 -LlvmMingwPath 'D:\my-llvm-mingw'" -ForegroundColor Yellow
    exit 1
}

# Add to PATH for current session
$env:PATH = "$binPath;$env:PATH"

Write-Host "✓ LLVM-MinGW found at: $LlvmMingwPath" -ForegroundColor Green
Write-Host ""

# Verify tools
$tools = @(
    @{Name="clang"; Required=$true},
    @{Name="clang++"; Required=$true},
    @{Name="ld.lld"; Required=$true},
    @{Name="llvm-ar"; Required=$false},
    @{Name="llvm-strip"; Required=$false},
    @{Name="cmake"; Required=$true},
    @{Name="ninja"; Required=$true}
)

Write-Host "Checking required tools:" -ForegroundColor Cyan
Write-Host ""

$allFound = $true
foreach ($tool in $tools) {
    $found = Get-Command $tool.Name -ErrorAction SilentlyContinue
    if ($found) {
        Write-Host "  ✓ $($tool.Name)" -ForegroundColor Green -NoNewline
        if ($tool.Name -eq "clang") {
            $version = & clang --version 2>&1 | Select-Object -First 1
            Write-Host " - $version" -ForegroundColor Gray
        } else {
            Write-Host ""
        }
    } else {
        if ($tool.Required) {
            Write-Host "  ✗ $($tool.Name) - REQUIRED" -ForegroundColor Red
            $allFound = $false
        } else {
            Write-Host "  ⚠ $($tool.Name) - optional" -ForegroundColor Yellow
        }
    }
}

Write-Host ""

if (-not $allFound) {
    Write-Host "✗ Some required tools are missing!" -ForegroundColor Red
    Write-Host ""
    Write-Host "Missing CMake or Ninja?" -ForegroundColor Yellow
    Write-Host "  Install via: winget install Kitware.CMake" -ForegroundColor Yellow
    Write-Host "  Install via: winget install Ninja-build.Ninja" -ForegroundColor Yellow
    exit 1
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "✓ Environment ready for building!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Cyan
Write-Host "  1. Run: .\build-windows-minsize.bat" -ForegroundColor White
Write-Host "  2. Or:  cmake --preset windows-clang-ninja-minsize" -ForegroundColor White
Write-Host "         cmake --build build-minsize --config Release" -ForegroundColor White
Write-Host ""

# Save environment setup for easy reuse
Write-Host "Tip: Add LLVM-MinGW to your system PATH permanently:" -ForegroundColor Yellow
Write-Host "  [Environment]::SetEnvironmentVariable('Path', `$env:Path + ';$binPath', 'User')" -ForegroundColor Gray
Write-Host ""

