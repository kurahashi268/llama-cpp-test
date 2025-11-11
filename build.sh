#!/usr/bin/env bash
set -euo pipefail

# Build script for llama-cpp chatbot with GPU support
# 
# Usage:
#   ./build.sh                           # Auto-detect GPU backend, x64 architecture (default)
#   ./build.sh --arch x64                # Explicit x64 architecture
#   ./build.sh --arch x86                # x86 (32-bit) architecture
#   ./build.sh cuda                      # Force CUDA backend
#   ./build.sh cuda --arch x86           # CUDA backend with x86 architecture
#   ./build.sh vulkan                    # Force Vulkan backend
#   ./build.sh rocm                      # Force ROCm/HIPBLAS backend
#   ./build.sh sycl                      # Force SYCL/oneAPI backend
#   ./build.sh cpu                       # CPU-only build (no GPU)
#   ./build.sh [backend] clean           # Clean before building
#
# Environment variables:
#   GGML_BACKEND=cuda|vulkan|rocm|sycl   Force specific backend
#   CMAKE_BUILD_TYPE=Release|Debug       Build type (default: Release)
#   ARCH=x64|x86                         Architecture (default: x64)
#   EXTRA_CMAKE_FLAGS="-D..."            Additional CMake flags

project_root="$(cd "$(dirname "$0")" && pwd)"
build_dir="${project_root}/build"

# Parse command-line arguments
backend_arg=""
arch_arg=""
clean_arg=""

# Parse all arguments
while [[ $# -gt 0 ]]; do
    case "${1}" in
        --arch)
            if [[ -z "${2:-}" ]]; then
                echo "Error: --arch requires an argument (x64 or x86)" >&2
                exit 1
            fi
            arch_arg="${2}"
            shift 2
            ;;
        clean)
            clean_arg="clean"
            shift
            ;;
        cuda|vulkan|rocm|hip|hipblas|sycl|oneapi|cpu)
            backend_arg="${1}"
            shift
            ;;
        *)
            echo "Error: Unknown argument '${1}'" >&2
            echo "Usage: $0 [cuda|vulkan|rocm|sycl|cpu] [--arch x64|x86] [clean]" >&2
            exit 1
            ;;
    esac
done

backend_flag=""
backend_name=""

# Determine backend
if [[ -n "${backend_arg}" ]]; then
    # Explicit backend from command line
    case "${backend_arg,,}" in
        cuda)
            backend_flag="-DGGML_CUDA=ON"
            backend_name="CUDA"
            # Verify CUDA Toolkit is available (not GPU hardware - that's only needed at runtime)
            if ! command -v nvcc >/dev/null 2>&1; then
                echo "NOTE: nvcc not found in PATH."
                echo "CUDA Toolkit is required for building with CUDA support."
                echo "You can build GPU-enabled binaries on CPU-only systems if CUDA Toolkit is installed."
                echo ""
                echo "Installing CUDA Toolkit:"
                echo "  - Ubuntu/Debian: sudo apt install nvidia-cuda-toolkit"
                echo "  - Or download from: https://developer.nvidia.com/cuda-downloads"
                echo ""
                echo "The build will continue but may fail if CMake cannot find CUDA Toolkit."
            else
                echo "CUDA Toolkit found - build can proceed on CPU-only device."
            fi
            ;;
        vulkan)
            backend_flag="-DGGML_VULKAN=ON"
            backend_name="Vulkan"
            ;;
        rocm|hip|hipblas)
            backend_flag="-DGGML_HIPBLAS=ON"
            backend_name="ROCm/HIPBLAS"
            ;;
        sycl|oneapi)
            backend_flag="-DGGML_SYCL=ON"
            backend_name="SYCL/oneAPI"
            ;;
        cpu)
            backend_flag="-DGGML_CUDA=OFF -DGGML_VULKAN=OFF -DGGML_HIPBLAS=OFF -DGGML_SYCL=OFF"
            backend_name="CPU-only"
            ;;
        *)
            echo "Error: Unknown backend '${backend_arg}'" >&2
            echo "Usage: $0 [cuda|vulkan|rocm|sycl|cpu]" >&2
            exit 1
            ;;
    esac
elif [[ -n "${GGML_BACKEND:-}" ]]; then
    # Backend from environment variable
    export GGML_BACKEND="${GGML_BACKEND}"
    backend_name="Auto-detect from GGML_BACKEND=${GGML_BACKEND}"
else
    # Auto-detect (CMakeLists.txt will handle this)
    backend_name="Auto-detect"
fi

# Build type
build_type="${CMAKE_BUILD_TYPE:-Release}"

echo "════════════════════════════════════════════════════════════"
echo "  llama-cpp Chatbot Build Script"
echo "════════════════════════════════════════════════════════════"
echo ""
echo "Project root: ${project_root}"
echo "Build directory: ${build_dir}"
echo "Build type: ${build_type}"
echo "Architecture: ${arch_name}"
echo "GPU backend: ${backend_name}"
echo ""

# Create build directory
mkdir -p "${build_dir}"
cd "${build_dir}"

# Architecture configuration (default: x64)
arch="${ARCH:-x64}"
if [[ -n "${arch_arg}" ]]; then
    arch="${arch_arg}"
fi

# Validate architecture
if [[ "${arch,,}" != "x64" && "${arch,,}" != "x86" ]]; then
    echo "Error: Invalid architecture '${arch}'. Use x64 or x86." >&2
    exit 1
fi

# Clean previous build (optional - comment out to do incremental builds)
if [[ "${clean_arg}" == "clean" ]]; then
    echo "Cleaning build directory..."
    rm -rf ./*
fi

# Configure CMake
echo "Configuring CMake..."

# Architecture configuration for CMake
arch_flag=""
arch_name="${arch,,}"
if [[ "${arch_name}" == "x64" ]]; then
    arch_name="x64 (x86_64)"
    # On Linux, x64 is usually the default, but we can explicitly set CMAKE_SYSTEM_PROCESSOR
    # On Windows, use -A flag for Visual Studio generators
    if [[ -n "${CMAKE_GENERATOR_PLATFORM:-}" ]]; then
        arch_flag="-A x64"
    else
        # For Unix Makefiles, ensure we're building for x86_64
        arch_flag="-DCMAKE_SYSTEM_PROCESSOR=x86_64"
    fi
elif [[ "${arch_name}" == "x86" ]]; then
    arch_name="x86 (i686)"
    if [[ -n "${CMAKE_GENERATOR_PLATFORM:-}" ]]; then
        arch_flag="-A Win32"
    else
        arch_flag="-DCMAKE_SYSTEM_PROCESSOR=i686"
    fi
fi

cmake "${project_root}" \
    -DCMAKE_BUILD_TYPE="${build_type}" \
    ${arch_flag} \
    ${backend_flag} \
    ${EXTRA_CMAKE_FLAGS:-}

# Build
echo ""
echo "Building..."
if command -v nproc >/dev/null 2>&1; then
    jobs=$(nproc)
else
    jobs=4  # Fallback for systems without nproc
fi

cmake --build "${build_dir}" -j"${jobs}"

# Success message
echo ""
echo "════════════════════════════════════════════════════════════"
echo "  Build complete!"
echo "════════════════════════════════════════════════════════════"
echo ""
echo "Executable: ${build_dir}/chatbot"
echo ""
echo "Run examples:"
echo "  ${build_dir}/chatbot --test"
echo "  ${build_dir}/chatbot --test --stream"
echo ""
if [[ "${backend_name}" != "CPU-only" ]]; then
    echo "GPU detection:"
    echo "  The program will auto-detect and use GPU if available."
    echo "  Set LLAMA_GPU_LAYERS=999 to force all layers to GPU."
fi
echo ""
