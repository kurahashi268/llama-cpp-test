# Japanese Language Models Guide

This guide helps you download and use Japanese language models with llama.cpp.

## 📥 Quick Start

### Linux/Mac:
```bash
./download-japanese-models.sh
```

### Windows:
```cmd
download-japanese-models.bat
```

Both scripts provide an interactive menu to download models.

## 🎯 Model Recommendations

### **Best Overall: Llama-3-ELYZA-JP-8B (Q4_K_M)**
- **Size**: ~5GB
- **Performance**: Comparable to GPT-3.5 for Japanese
- **Use case**: General Japanese conversation, instruction following
- **Chat format**: Llama-3 format (use with updated code)

### **Budget-Friendly: ELYZA-japanese-Llama-2-7b (Q4_K_M)**
- **Size**: ~4GB  
- **Performance**: Good Japanese conversation
- **Use case**: Lower memory systems
- **Chat format**: Llama-2 format (already configured in your code)

### **High Quality: Llama-3-ELYZA-JP-8B (Q8_0)**
- **Size**: ~8.5GB
- **Performance**: Best accuracy, slower inference
- **Use case**: When you need highest quality and have enough RAM

### **Multilingual: Qwen2.5-7B-Instruct (Q4_K_M)**
- **Size**: ~4.7GB
- **Performance**: Excellent at Chinese, Japanese, and English
- **Use case**: Need multiple languages in one model

### **Research: Swallow-7b-instruct (Q4_K_M)**
- **Size**: ~4GB
- **Performance**: Strong instruction-following
- **Use case**: Academic/research applications

## 🚀 Using the Models

After downloading a model, update your `main.cpp` to point to it:

```cpp
const std::string MODEL_PATH = "models/Llama-3-ELYZA-JP-8B-q4_k_m.gguf";
```

Then rebuild:
```bash
./build.sh
```

## 💬 Chat Format Support

### For ELYZA Llama-2 models (already in your code):
```
[INST] <<SYS>>
{system_prompt}
<</SYS>>

{user_prompt} [/INST]
```

### For Llama-3 based models (ELYZA JP 8B):
Update `build_prompt()` function to use:
```
<|begin_of_text|><|start_header_id|>system<|end_header_id|>

{system_prompt}<|eot_id|><|start_header_id|>user<|end_header_id|>

{user_prompt}<|eot_id|><|start_header_id|>assistant<|end_header_id|>
```

### For Qwen2.5:
```
<|im_start|>system
{system_prompt}<|im_end|>
<|im_start|>user
{user_prompt}<|im_end|>
<|im_start|>assistant
```

## 🧪 Testing

Test your Japanese model:
```bash
./build/chatbot --test
```

Example prompts:
- "日本の文化について教えてください" (Tell me about Japanese culture)
- "東京の観光スポットを教えて" (Tell me about Tokyo tourist spots)
- "Pythonでソートアルゴリズムを説明して" (Explain sort algorithms in Python)

## 📊 Model Comparison

| Model | Size | Speed | Quality | Memory | Best For |
|-------|------|-------|---------|--------|----------|
| ELYZA Llama-2 7B Q4 | 4GB | Fast | Good | 6GB RAM | General use |
| ELYZA Llama-3 8B Q4 | 5GB | Fast | Excellent | 8GB RAM | **Recommended** |
| ELYZA Llama-3 8B Q8 | 8.5GB | Medium | Best | 12GB RAM | High quality |
| Qwen2.5 7B Q4 | 4.7GB | Fast | Excellent | 8GB RAM | Multilingual |
| Swallow 7B Q4 | 4GB | Fast | Good | 6GB RAM | Research |

## 🔧 Troubleshooting

### Model doesn't respond in Japanese
- Make sure you're using a Japanese-specific model
- Try a more explicit prompt: "Please respond in Japanese: [your question]"
- Check that the chat format matches the model

### Out of memory errors
- Use a smaller quantization (Q4_K_M instead of Q8_0)
- Reduce context size in `main.cpp`: `DEFAULT_CONTEXT_SIZE = 1024`
- Use a smaller model

### Strange tokens in output (like `<|end|>`)
- Your code's chat format doesn't match the model
- Use the correct format for your model (see Chat Format Support above)

## 📚 Resources

- [ELYZA Official](https://www.elyza.ai/)
- [Swallow (TokyoTech)](https://tokyotech-llm.github.io/swallow-llama)
- [HuggingFace mmnga](https://huggingface.co/mmnga) - Japanese GGUF models
- [llama.cpp](https://github.com/ggerganov/llama.cpp)

## ⚡ Quick Commands

Download recommended model only:
```bash
# Linux
./download-japanese-models.sh
# Select option 1

# Windows
download-japanese-models.bat
REM Select option 1
```

Test immediately after download:
```bash
# Update model path in main.cpp first!
./build.sh && ./build/chatbot --test
```

