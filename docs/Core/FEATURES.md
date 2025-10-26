# Features Guide

## 🎯 Overview

This chatbot supports two main modes:
1. **Test Mode** - Interactive chatbot for testing and standalone use
2. **Shared Memory Mode** - Background process for C# application integration

---

## 🎮 Test Mode Features

### Interactive Mode

Run continuous conversations with the AI:

```bash
./build/chatbot --test
```

**Features:**
- Default system prompt: "You are my best assistance."
- Continuous conversation loop
- Exit with: `exit`, `quit`, or `bye`
- Colored output (Cyan for user, Green for assistant)

### Streaming Mode

Watch tokens appear in real-time as they're generated:

```bash
./build/chatbot --test --stream
```

**Streaming vs Normal:**
- **Normal**: Complete response appears after generation finishes
- **Streaming**: Tokens appear one by one as generated (like ChatGPT)

### Configurable Response Length

Control how long responses can be:

```bash
# Default: 4096 tokens (~3000 words)
./build/chatbot --test

# Custom length
./build/chatbot --test --max-tokens 8192

# Unlimited (generates until model stops naturally)
./build/chatbot --test --max-tokens 0
```

**Token Guide:**
- **512 tokens** ≈ 380 words ≈ 1-2 paragraphs
- **2048 tokens** ≈ 1500 words ≈ 1 page
- **4096 tokens** ≈ 3000 words ≈ 2 pages (default)
- **8192 tokens** ≈ 6000 words ≈ 4 pages
- **0 (unlimited)** = Until model generates EOS token

### Custom System Prompts

Customize the AI's behavior:

```bash
./build/chatbot --test --system "You are a helpful coding expert"
```

### One-Shot Mode

Single question and answer (for scripting):

```bash
./build/chatbot --test --user "What is C++?"
```

---

## 📋 Command Reference

### Basic Commands

```bash
# Interactive with defaults
./build/chatbot --test

# Interactive with streaming
./build/chatbot --test --stream

# Custom system prompt
./build/chatbot --test --system "You are helpful"

# Custom max tokens
./build/chatbot --test --max-tokens 8192

# Unlimited tokens
./build/chatbot --test --max-tokens 0

# One-shot mode
./build/chatbot --test --user "Your question"

# Combine options
./build/chatbot --test --stream --max-tokens 0 --system "You are an expert"
```

### Shared Memory Mode

```bash
# Start background process for C# integration
./build/chatbot
```

---

## 🎨 Example Sessions

### Example 1: Basic Interactive

```
$ ./build/chatbot --test

╔════════════════════════════════════════════════════════════╗
║          Interactive Chatbot Mode                         ║
╚════════════════════════════════════════════════════════════╝

System: You are my best assistance.
Mode: Normal (full response at once)
Max Tokens: 4096

You: Hello!