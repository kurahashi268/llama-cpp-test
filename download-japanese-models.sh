#!/bin/bash

# Japanese Model Downloader for llama.cpp
# This script downloads popular Japanese language models in GGUF format

set -e

MODELS_DIR="models"
mkdir -p "$MODELS_DIR"

echo "╔════════════════════════════════════════════════════════════╗"
echo "║     Japanese Model Downloader for llama.cpp              ║"
echo "╚════════════════════════════════════════════════════════════╝"
echo ""

# Color codes
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

show_menu() {
    echo -e "${BLUE}Available Japanese Models:${NC}"
    echo ""
    echo "1) Llama-3-ELYZA-JP-8B (Q4_K_M) - ~5GB [RECOMMENDED]"
    echo "   - Latest Llama-3 based, excellent Japanese performance"
    echo "   - Comparable to GPT-3.5 for Japanese tasks"
    echo ""
    echo "2) ELYZA-japanese-Llama-2-7b (Q4_K_M) - ~4GB"
    echo "   - Llama-2 based, proven Japanese model"
    echo "   - Good for general Japanese conversation"
    echo ""
    echo "3) Swallow-7b-instruct (Q4_K_M) - ~4GB"
    echo "   - Tokyo Tech's Japanese model"
    echo "   - Strong instruction-following in Japanese"
    echo ""
    echo "4) Llama-3-ELYZA-JP-8B (Q8_0) - ~8.5GB"
    echo "   - Higher quality version (more VRAM needed)"
    echo "   - Better accuracy, slower inference"
    echo ""
    echo "5) Qwen2.5-7B-Instruct (Q4_K_M) - ~4.7GB"
    echo "   - Multilingual (Chinese/Japanese/English)"
    echo "   - Very strong at Japanese despite being Chinese-focused"
    echo ""
    echo "6) ALL - Download all recommended models"
    echo ""
    echo "0) Exit"
    echo ""
}

download_model() {
    local url=$1
    local filename=$2
    local description=$3
    
    echo -e "${GREEN}Downloading: $description${NC}"
    echo "URL: $url"
    echo "Destination: $MODELS_DIR/$filename"
    echo ""
    
    if [ -f "$MODELS_DIR/$filename" ]; then
        echo -e "${YELLOW}File already exists. Skipping...${NC}"
        echo ""
        return
    fi
    
    # Try wget first, fall back to curl
    if command -v wget &> /dev/null; then
        wget -c "$url" -O "$MODELS_DIR/$filename"
    elif command -v curl &> /dev/null; then
        curl -L -C - "$url" -o "$MODELS_DIR/$filename"
    else
        echo "Error: Neither wget nor curl is available. Please install one of them."
        exit 1
    fi
    
    echo -e "${GREEN}✓ Download complete: $filename${NC}"
    echo ""
}

download_llama3_elyza_q4() {
    download_model \
        "https://huggingface.co/mmnga/Llama-3-ELYZA-JP-8B-gguf/resolve/main/Llama-3-ELYZA-JP-8B-q4_k_m.gguf" \
        "Llama-3-ELYZA-JP-8B-q4_k_m.gguf" \
        "Llama-3 ELYZA JP 8B (Q4_K_M)"
}

download_elyza_llama2() {
    download_model \
        "https://huggingface.co/mmnga/ELYZA-japanese-Llama-2-7b-instruct-gguf/resolve/main/ELYZA-japanese-Llama-2-7b-instruct-q4_K_M.gguf" \
        "ELYZA-japanese-Llama-2-7b-instruct-q4_K_M.gguf" \
        "ELYZA Japanese Llama-2 7B (Q4_K_M)"
}

download_swallow() {
    download_model \
        "https://huggingface.co/mmnga/Swallow-7b-instruct-v0.1-gguf/resolve/main/Swallow-7b-instruct-v0.1-q4_k_m.gguf" \
        "Swallow-7b-instruct-v0.1-q4_k_m.gguf" \
        "Swallow 7B Instruct (Q4_K_M)"
}

download_llama3_elyza_q8() {
    download_model \
        "https://huggingface.co/mmnga/Llama-3-ELYZA-JP-8B-gguf/resolve/main/Llama-3-ELYZA-JP-8B-q8_0.gguf" \
        "Llama-3-ELYZA-JP-8B-q8_0.gguf" \
        "Llama-3 ELYZA JP 8B (Q8_0 - Higher Quality)"
}

download_qwen25() {
    download_model \
        "https://huggingface.co/bartowski/Qwen2.5-7B-Instruct-GGUF/resolve/main/Qwen2.5-7B-Instruct-Q4_K_M.gguf" \
        "Qwen2.5-7B-Instruct-Q4_K_M.gguf" \
        "Qwen 2.5 7B Instruct (Q4_K_M)"
}

# Main loop
while true; do
    show_menu
    read -p "Select a model to download (0-6): " choice
    echo ""
    
    case $choice in
        1)
            download_llama3_elyza_q4
            ;;
        2)
            download_elyza_llama2
            ;;
        3)
            download_swallow
            ;;
        4)
            download_llama3_elyza_q8
            ;;
        5)
            download_qwen25
            ;;
        6)
            echo -e "${YELLOW}Downloading all recommended models...${NC}"
            echo ""
            download_llama3_elyza_q4
            download_elyza_llama2
            download_swallow
            download_qwen25
            echo -e "${GREEN}✓ All downloads complete!${NC}"
            ;;
        0)
            echo "Exiting..."
            exit 0
            ;;
        *)
            echo -e "${YELLOW}Invalid choice. Please try again.${NC}"
            echo ""
            ;;
    esac
    
    read -p "Press Enter to continue..."
    clear
done

