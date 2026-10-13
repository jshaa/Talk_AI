#!/usr/bin/env python3
"""LoRA fine-tune of one person's speaking style + GGUF export, run by server.py on the user's PC.

Input is the chat JSONL from the 말투 AI app (imitated person = assistant). The model is a ChatML
(Qwen2.5) instruct model; only the assistant turns are trained. Output: <out>/persona-q4_k_m.gguf and,
when larger than 512 MB, split parts for the web app. Intermediate files (uploaded data, merged
weights, f16 GGUF) are deleted when the job ends.

GPU (NVIDIA, CUDA): Unsloth 4-bit QLoRA. CPU: transformers + PEFT LoRA (slow; 0.5B recommended).
Progress is written to --status as JSON for the server to relay.
"""
from __future__ import annotations

import argparse
import glob
import json
import math
import os
import shutil
import subprocess
import sys
import time
import traceback
from pathlib import Path

IM_START, IM_END = "<|im_start|>", "<|im_end|>"
LLAMA_CPP = Path(os.environ.get("LLAMA_CPP_DIR", "/opt/llama.cpp"))
SPLIT_BYTES = 512 * 1024 * 1024

# App choice -> Hugging Face repo (GPU repos are Unsloth's re-uploads of the same weights).
MODELS = {
    "qwen2.5-0.5b": ("unsloth/Qwen2.5-0.5B-Instruct", "Qwen/Qwen2.5-0.5B-Instruct"),
    "qwen2.5-1.5b": ("unsloth/Qwen2.5-1.5B-Instruct", "Qwen/Qwen2.5-1.5B-Instruct"),
    "qwen2.5-3b": ("unsloth/Qwen2.5-3B-Instruct", "Qwen/Qwen2.5-3B-Instruct"),
}
TARGET_MODULES = ["q_proj", "k_proj", "v_proj", "o_proj", "gate_proj", "up_proj", "down_proj"]


class Status:
    def __init__(self, path: Path):
        self.path = path
        self.data: dict = {"stage": "starting", "step": 0, "total": 0, "loss": None, "message": "", "started": time.time()}

    def update(self, **kw) -> None:
        self.data.update(kw, updated=time.time())
        tmp = self.path.with_suffix(".tmp")
        tmp.write_text(json.dumps(self.data, ensure_ascii=False), encoding="utf-8")
        os.replace(tmp, self.path)


def read_samples(path: Path) -> list[list[dict]]:
    samples = []
    for line in path.read_text(encoding="utf-8").splitlines():
        if line.strip():
            samples.append(json.loads(line)["messages"])
    return samples


def encode_sample(tokenize, messages: list[dict], max_len: int) -> dict | None:
    """ChatML token ids with labels only on assistant content + <|im_end|> (what the model must produce)."""
    ids: list[int] = []
    labels: list[int] = []
    for m in messages:
        head = tokenize(f"{IM_START}{m['role']}\n")
        body = tokenize(f"{m['content']}{IM_END}")
        tail = tokenize("\n")
        ids += head + body + tail
        labels += [-100] * len(head) + (body if m["role"] == "assistant" else [-100] * len(body)) + [-100] * len(tail)
    if len(ids) > max_len or all(x == -100 for x in labels):
        return None
    return {"input_ids": ids, "attention_mask": [1] * len(ids), "labels": labels}


def auto_epochs(replies: int) -> int:
    return 3 if replies < 1000 else 2 if replies < 5000 else 1


def run(cmd: list[str]) -> None:
    proc = subprocess.run(cmd, capture_output=True, text=True)
    if proc.returncode != 0:
        raise RuntimeError(f"{Path(cmd[0]).name} failed:\n{proc.stderr[-2000:]}")


def export_gguf(merged: Path, out: Path, status: Status) -> list[str]:
    status.update(stage="converting", message="GGUF 변환 중")
    f16 = out / "model-f16.gguf"
    final = out / "persona-q4_k_m.gguf"
    run([sys.executable, str(LLAMA_CPP / "convert_hf_to_gguf.py"), str(merged), "--outfile", str(f16), "--outtype", "f16"])
    status.update(message="Q4_K_M 양자화 중")
    run([str(LLAMA_CPP / "build/bin/llama-quantize"), str(f16), str(final), "Q4_K_M"])
    f16.unlink(missing_ok=True)
    files = [final.name]
    if final.stat().st_size > SPLIT_BYTES:
        status.update(message="웹용으로 나누는 중")
        run([str(LLAMA_CPP / "build/bin/llama-gguf-split"), "--split", "--split-max-size", "512M", str(final), str(out / "persona-q4_k_m")])
        files += sorted(Path(p).name for p in glob.glob(str(out / "persona-q4_k_m-*-of-*.gguf")))
    return files


def train(args: argparse.Namespace, status: Status) -> list[str]:
    import torch
    from transformers import DataCollatorForSeq2Seq, Trainer, TrainerCallback, TrainingArguments

    gpu = torch.cuda.is_available() and not args.cpu
    gpu_repo, cpu_repo = MODELS[args.base]
    out = Path(args.out)
    merged = out / "merged"
    max_len = args.max_len or (1536 if gpu else 1024)

    status.update(stage="downloading", message=f"기본 모델 준비 중 ({gpu_repo if gpu else cpu_repo})", device="cuda" if gpu else "cpu")
    if gpu:
        from unsloth import FastLanguageModel

        model, tokenizer = FastLanguageModel.from_pretrained(model_name=gpu_repo, max_seq_length=max_len, load_in_4bit=True)
        model = FastLanguageModel.get_peft_model(
            model, r=16, lora_alpha=32, lora_dropout=0, target_modules=TARGET_MODULES,
            use_gradient_checkpointing="unsloth", random_state=3407,
        )
    else:
        from peft import LoraConfig, get_peft_model
        from transformers import AutoModelForCausalLM, AutoTokenizer

        repo = args.model_path or cpu_repo
        tokenizer = AutoTokenizer.from_pretrained(repo)
        model = AutoModelForCausalLM.from_pretrained(repo, torch_dtype=torch.float32)
        model = get_peft_model(model, LoraConfig(r=16, lora_alpha=32, lora_dropout=0.05, target_modules=TARGET_MODULES, task_type="CAUSAL_LM"))

    template = getattr(tokenizer, "chat_template", None) or ""
    if IM_START not in template:
        raise RuntimeError("ChatML(<|im_start|>) 채팅 템플릿 모델만 지원합니다 (Qwen2.5 계열).")

    data_path = Path(args.data)
    samples = read_samples(data_path)
    data_path.unlink(missing_ok=True)  # the conversation text is no longer needed on disk
    tokenize = lambda s: tokenizer(s, add_special_tokens=False).input_ids  # noqa: E731
    encoded = [e for e in (encode_sample(tokenize, s, max_len) for s in samples) if e is not None]
    if not encoded:
        raise RuntimeError("학습할 샘플이 없습니다(모두 너무 길거나 답장이 없음).")
    replies = sum(1 for s in samples for m in s if m["role"] == "assistant")
    epochs = args.epochs or auto_epochs(replies)
    batch, accum = (4, 4) if gpu else (2, 8)
    total = math.ceil(math.ceil(len(encoded) / batch) / accum) * epochs
    status.update(stage="training", step=0, total=total, samples=len(encoded), dropped=len(samples) - len(encoded), replies=replies, epochs=epochs, message="학습 중")

    class Progress(TrainerCallback):
        def on_log(self, a, state, control, logs=None, **kw):
            loss = (logs or {}).get("loss")
            status.update(step=state.global_step, total=state.max_steps or total, **({"loss": round(loss, 4)} if loss is not None else {}))

        def on_step_end(self, a, state, control, **kw):
            status.update(step=state.global_step, total=state.max_steps or total)

    bf16 = gpu and torch.cuda.is_bf16_supported()
    trainer = Trainer(
        model=model,
        train_dataset=encoded,
        data_collator=DataCollatorForSeq2Seq(tokenizer, padding=True, label_pad_token_id=-100),
        callbacks=[Progress()],
        args=TrainingArguments(
            output_dir=str(out / "checkpoints"),
            per_device_train_batch_size=batch,
            gradient_accumulation_steps=accum,
            num_train_epochs=epochs,
            max_steps=args.max_steps or -1,
            learning_rate=2e-4,
            warmup_steps=max(1, round(total * 0.03)),  # int works in transformers 4 and 5
            lr_scheduler_type="cosine",
            weight_decay=0.01,
            logging_steps=1,
            save_strategy="no",
            report_to="none",
            fp16=gpu and not bf16,
            bf16=bf16,
            optim="adamw_8bit" if gpu else "adamw_torch",
            use_cpu=not gpu,
            seed=3407,
            remove_unused_columns=False,
        ),
    )
    trainer.train()

    status.update(stage="merging", message="LoRA 병합 중")
    if gpu:
        model.save_pretrained_merged(str(merged), tokenizer, save_method="merged_16bit")
    else:
        model.merge_and_unload().save_pretrained(str(merged), safe_serialization=True)
        tokenizer.save_pretrained(str(merged))
    del trainer, model
    try:
        return export_gguf(merged, out, status)
    finally:
        shutil.rmtree(merged, ignore_errors=True)
        shutil.rmtree(out / "checkpoints", ignore_errors=True)


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--data", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--status", required=True)
    ap.add_argument("--base", choices=sorted(MODELS), default="qwen2.5-1.5b")
    ap.add_argument("--epochs", type=int, default=0, help="0 = automatic by number of replies")
    ap.add_argument("--max-len", type=int, default=0)
    ap.add_argument("--max-steps", type=int, default=0, help="testing only")
    ap.add_argument("--cpu", action="store_true", help="force CPU")
    ap.add_argument("--model-path", help="local model directory instead of the Hub (CPU path, testing)")
    args = ap.parse_args(argv)

    status = Status(Path(args.status))
    try:
        files = train(args, status)
        status.update(stage="done", files=files, message="완료")
        return 0
    except Exception as e:  # reported to the app; the traceback stays in the server log only
        traceback.print_exc()
        status.update(stage="error", message=str(e)[:500])
        return 1
    finally:
        Path(args.data).unlink(missing_ok=True)


if __name__ == "__main__":
    sys.exit(main())
