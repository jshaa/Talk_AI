#!/bin/sh
# Entry point of the `llm` service (official llama.cpp CUDA server image): serves an imported persona
# (base GGUF + LoRA, see import_adapter.py) for the trainer's /api/chat while /data/llm/lora.gguf exists.
# The trainer removes lora.gguf before training (freeing the GPU) and replaces it on a new import (restart).
D=/data/llm
while :; do
  if [ -f "$D/base.gguf" ] && [ -f "$D/lora.gguf" ]; then
    stamp=$(stat -c %Y-%i "$D/lora.gguf")
    /app/llama-server -m "$D/base.gguf" --lora "$D/lora.gguf" -ngl 99 -c 3072 --parallel 1 \
      --host 0.0.0.0 --port 8080 --no-webui &
    pid=$!
    while kill -0 "$pid" 2>/dev/null && [ "$(stat -c %Y-%i "$D/lora.gguf" 2>/dev/null)" = "$stamp" ]; do sleep 2; done
    kill "$pid" 2>/dev/null
    wait "$pid"
  fi
  sleep 2
done
