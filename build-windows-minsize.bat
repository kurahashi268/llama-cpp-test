@echo off
echo =====================================
echo Building llama_chatbot (CPU-only)
echo =====================================

set BUILD_DIR=build-cpu-x64-minsize
rmdir /s /q %BUILD_DIR%
mkdir %BUILD_DIR%
cd %BUILD_DIR%

cmake -G "Ninja" -DGGML_CUDA=OFF -DGGML_VULKAN=OFF -DGGML_HIPBLAS=OFF -DGGML_SYCL=OFF -DCMAKE_BUILD_TYPE=Release ..
ninja

echo =====================================
echo ✅ Build finished (CPU-only)
echo =====================================

