# 말투 AI training server. Build context: repository root (see compose.yaml).
# NVIDIA GPU (Docker Desktop + WSL2 on Windows) -> Unsloth QLoRA; no GPU -> slow CPU LoRA.

# ---- 1. web app, served on :8000 for same-Wi-Fi (LAN) use
FROM node:22-bookworm-slim AS web
WORKDIR /src/apps/mimic
COPY apps/mimic/package.json apps/mimic/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY apps/mimic ./
COPY training/system_prompt.txt /src/training/system_prompt.txt
RUN npm run build

# ---- 2. llama.cpp GGUF tools (convert + quantize + split), statically linked
FROM debian:bookworm-slim AS llama
ARG LLAMA_CPP_REF=b11580
RUN apt-get update && apt-get install -y --no-install-recommends build-essential cmake git ca-certificates \
 && rm -rf /var/lib/apt/lists/*
RUN git clone --depth 1 --branch ${LLAMA_CPP_REF} https://github.com/ggml-org/llama.cpp /opt/llama.cpp \
 && cmake -S /opt/llama.cpp -B /opt/llama.cpp/build -DBUILD_SHARED_LIBS=OFF -DGGML_NATIVE=OFF -DLLAMA_CURL=OFF \
      -DLLAMA_BUILD_TESTS=OFF -DLLAMA_BUILD_EXAMPLES=OFF -DLLAMA_BUILD_SERVER=OFF \
 && cmake --build /opt/llama.cpp/build -j"$(nproc)" --target llama-quantize llama-gguf-split \
 && mkdir /opt/out && cp -r /opt/llama.cpp/convert_hf_to_gguf.py /opt/llama.cpp/conversion /opt/llama.cpp/gguf-py /opt/llama.cpp/LICENSE /opt/out/ \
 && mkdir /opt/out/build && cp -r /opt/llama.cpp/build/bin /opt/out/build/

# ---- 3. runtime
FROM python:3.11-slim-bookworm
# gcc: Triton (used by Unsloth) compiles small launchers at run time.
RUN apt-get update && apt-get install -y --no-install-recommends build-essential git ca-certificates \
 && rm -rf /var/lib/apt/lists/*
ENV PIP_NO_CACHE_DIR=1 PYTHONUNBUFFERED=1
RUN pip install "unsloth==2026.10.4" sentencepiece "protobuf<5"
COPY training/server/requirements.txt /app/requirements.txt
RUN pip install -r /app/requirements.txt
COPY --from=llama /opt/out /opt/llama.cpp
COPY --from=web /src/apps/mimic/dist /app/web
COPY training/server/server.py training/server/persona_train.py /app/
ENV LLAMA_CPP_DIR=/opt/llama.cpp TRAINER_DATA=/data TRAINER_WEB=/app/web HF_HOME=/data/hf \
    HF_HUB_DISABLE_TELEMETRY=1 TRANSFORMERS_NO_ADVISORY_WARNINGS=1 UNSLOTH_DISABLE_STATISTICS=1
VOLUME /data
EXPOSE 8000 8001
HEALTHCHECK --interval=30s --timeout=5s CMD python -c "import urllib.request;urllib.request.urlopen('http://127.0.0.1:8000/api/health',timeout=4)"
CMD ["python", "/app/server.py"]
