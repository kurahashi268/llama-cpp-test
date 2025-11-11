#!/usr/bin/env bash
set -euo pipefail

# GPU build helper for llama.cpp-based chatbot
# Autodetects backend (CUDA/ROCm/SYCL/Vulkan) on Linux, or use GGML_BACKEND env to force.
# Usage:
#   GGML_BACKEND=cuda ./build-gpu.sh         # force CUDA
#   EXTRA_CMAKE_FLAGS="-DLLAMA_CUDA_F16=ON" ./build-gpu.sh

project_root="$(cd "$(dirname "$0")" && pwd)"
build_dir="${project_root}/build"

backend_flag=""
backend_name=""

detect_backend() {
    # Respect explicit override
    if [[ ${GGML_BACKEND:-} != "" ]]; then
        case "${GGML_BACKEND,,}" in
            cuda)
                backend_flag="-DGGML_CUDA=ON"; backend_name="CUDA" ;;
            hip|rocm|hipblas)
                backend_flag="-DGGML_HIPBLAS=ON"; backend_name="ROCm/HIPBLAS" ;;
            sycl|oneapi)
                backend_flag="-DGGML_SYCL=ON"; backend_name="SYCL/oneAPI" ;;
            metal)
                echo "Metal is macOS-only; not supported on Linux."; exit 2 ;;
            vulkan)
                backend_flag="-DGGML_VULKAN=ON"; backend_name="VULKAN" ;;
            *)
                echo "Unknown GGML_BACKEND='${GGML_BACKEND}'. Use: cuda | rocm | sycl | vulkan"; exit 2 ;;
        esac
        return
    fi

    # NVIDIA CUDA (check for CUDA Toolkit, not GPU hardware - GPU only needed at runtime)
    if command -v nvcc >/dev/null 2>&1; then
        backend_flag="-DGGML_CUDA=ON"; backend_name="CUDA"; return
    fi
    # Also check if nvidia-smi is available (indicates drivers, but toolkit might still be missing)
    if command -v nvidia-smi >/dev/null 2>&1; then
        echo "Note: GPU drivers detected but nvcc not found."
        echo "CUDA Toolkit is required for building. You can install it on CPU-only systems."
        echo "Install: sudo apt install nvidia-cuda-toolkit"
    fi

    # AMD ROCm
    if [[ -d /opt/rocm ]] || command -v rocminfo >/dev/null 2>&1; then
        backend_flag="-DGGML_HIPBLAS=ON"; backend_name="ROCm/HIPBLAS"; return
    fi

    # Intel oneAPI SYCL
    if command -v sycl-ls >/dev/null 2>&1; then
        backend_flag="-DGGML_SYCL=ON"; backend_name="SYCL/oneAPI"; return
    fi

    # Vulkan (cross-vendor fallback)
    if command -v vulkaninfo >/dev/null 2>&1; then
        backend_flag="-DGGML_VULKAN=ON"; backend_name="VULKAN"; return
    fi

    echo "No GPU backend detected. Set GGML_BACKEND=cuda|rocm|sycl|vulkan to force."
    exit 1
}

detect_backend

echo "[build-gpu] Backend: ${backend_name}"
echo "[build-gpu] Project : ${project_root}"
echo "[build-gpu] Build dir: ${build_dir}"

mkdir -p "${build_dir}"
cd "${build_dir}"
rm -rf ./*

# Architecture configuration (x64 on Linux is usually default)
arch_flag=""
if [[ -n "${CMAKE_GENERATOR_PLATFORM:-}" ]]; then
    arch_flag="-A ${CMAKE_GENERATOR_PLATFORM}"
fi

cmake "${project_root}" \
  -DCMAKE_BUILD_TYPE=Release \
  ${arch_flag} \
  ${backend_flag} \
  ${EXTRA_CMAKE_FLAGS:-}

cmake --build "${build_dir}" -j"$(nproc)"

echo ""
echo "Build complete. Example run:"
echo "  ${build_dir}/chatbot \\
    --model ${project_root}/models/llama.gguf \\
    --n-gpu-layers 999 \\
    --batch-size 512"


