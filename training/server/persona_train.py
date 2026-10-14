#!/usr/bin/env python3
"""LoRA fine-tune of one person's speaking style + GGUF export, run by server.py on the user's PC.

Input is the chat JSONL from the 말투 AI app (imitated person = assistant). The model is a ChatML
(Qwen2.5 / Qwen3-Instruct) model; only the assistant turns are trained. Output: <out>/persona-<quant>.gguf and,
when larger than 512 MB, split parts for the web app (Q6_K for models up to 1.5B). Intermediate files (uploaded data, merged
weights, f16 GGUF) are deleted when the job ends.

GPU (NVIDIA, CUDA): Unsloth 4-bit QLoRA, or PEFT LoRA in bf16/fp16 when Unsloth is not installed (native
Windows). CPU: transformers + PEFT LoRA in fp32 (slow; 0.5B recommended).
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


def _transformers_major() -> int:
    try:
        from importlib.metadata import version

        return int(version("transformers").split(".")[0])
    except Exception:
        return 4


_TF5 = _transformers_major() >= 5  # "torch_dtype" was renamed to "dtype"
LLAMA_CPP = Path(os.environ.get("LLAMA_CPP_DIR", "/opt/llama.cpp"))
SPLIT_BYTES = 512 * 1024 * 1024

# App choice -> Hugging Face repo (GPU repos are Unsloth's re-uploads of the same weights).
MODELS = {
    "qwen2.5-0.5b": ("unsloth/Qwen2.5-0.5B-Instruct", "Qwen/Qwen2.5-0.5B-Instruct"),
    "qwen2.5-1.5b": ("unsloth/Qwen2.5-1.5B-Instruct", "Qwen/Qwen2.5-1.5B-Instruct"),
    "qwen2.5-3b": ("unsloth/Qwen2.5-3B-Instruct", "Qwen/Qwen2.5-3B-Instruct"),
    # Non-thinking Qwen3 (Apache-2.0): far better Korean and context than Qwen2.5 at a similar size.
    "qwen3-4b": ("unsloth/Qwen3-4B-Instruct-2507", "Qwen/Qwen3-4B-Instruct-2507"),
}
# Small models lose noticeably more quality at 4-bit, so they get 6-bit (still ~0.5/1.3 GB).
QUANT = {"qwen2.5-0.5b": "Q6_K", "qwen2.5-1.5b": "Q6_K"}
TARGET_MODULES = ["q_proj", "k_proj", "v_proj", "o_proj", "gate_proj", "up_proj", "down_proj"]


class Status:
    def __init__(self, path: Path, console: bool = False):
        self.path = path
        self.console = console
        self.last = ""
        self.data: dict = {"stage": "starting", "step": 0, "total": 0, "loss": None, "message": "", "started": time.time()}

    def update(self, **kw) -> None:
        self.data.update(kw, updated=time.time())
        tmp = self.path.with_suffix(".tmp")
        tmp.write_text(json.dumps(self.data, ensure_ascii=False), encoding="utf-8")
        os.replace(tmp, self.path)
        if self.console:
            d = self.data
            line = f"[{d['stage']}] {d.get('message', '')}"
            if d["stage"] == "training" and d.get("total"):
                line += f" {d.get('step', 0)}/{d['total']}" + (f" loss={d['loss']}" if d.get("loss") is not None else "")
            if line != self.last:
                print(line, flush=True)
                self.last = line


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
    # More epochs made small models memorise whole lines instead of answering in context.
    return 2 if replies < 3000 else 1


def tool(name: str) -> str:
    """llama.cpp binary from a source build (build/bin), a release zip (flat or bin/) or PATH; .exe on Windows."""
    for d in (LLAMA_CPP / "build" / "bin", LLAMA_CPP / "bin", LLAMA_CPP):
        for n in (name, f"{name}.exe"):
            if (d / n).is_file():
                return str(d / n)
    found = shutil.which(name)
    if not found:
        raise RuntimeError(f"{name}을(를) 찾지 못했습니다. llama.cpp 릴리스를 받아 LLAMA_CPP_DIR 환경변수로 폴더를 지정하세요.")
    return found


def run(cmd: list[str]) -> None:
    proc = subprocess.run(cmd, capture_output=True, text=True)
    if proc.returncode != 0:
        raise RuntimeError(f"{Path(cmd[0]).name} failed:\n{proc.stderr[-2000:]}")


def export_gguf(merged: Path, out: Path, status: Status, quant: str = "Q4_K_M") -> list[str]:
    status.update(stage="converting", message="GGUF 변환 중")
    f16 = out / "model-f16.gguf"
    stem = f"persona-{quant.lower()}"
    final = out / f"{stem}.gguf"
    convert = LLAMA_CPP / "convert_hf_to_gguf.py"
    if not convert.is_file():
        raise RuntimeError(f"{convert} 가 없습니다. llama.cpp 소스를 받아 LLAMA_CPP_DIR로 지정하세요.")
    run([sys.executable, str(convert), str(merged), "--outfile", str(f16), "--outtype", "f16"])
    status.update(message=f"{quant} 양자화 중")
    run([tool("llama-quantize"), str(f16), str(final), quant])
    f16.unlink(missing_ok=True)
    files = [final.name]
    if final.stat().st_size > SPLIT_BYTES:
        status.update(message="웹용으로 나누는 중")
        run([tool("llama-gguf-split"), "--split", "--split-max-size", "512M", str(final), str(out / stem)])
        files += sorted(Path(p).name for p in glob.glob(str(out / f"{stem}-*-of-*.gguf")))
    return files


def train(args: argparse.Namespace, status: Status) -> list[str]:
    import torch
    from transformers import DataCollatorForSeq2Seq, Trainer, TrainerCallback, TrainingArguments

    gpu = torch.cuda.is_available() and not args.cpu
    use_unsloth = False
    if gpu:
        try:
            import unsloth  # noqa: F401  (Linux/WSL2; on native Windows usually not installed)

            use_unsloth = True
        except ImportError:
            print("unsloth 없음: GPU에서 PEFT LoRA로 학습합니다(메모리를 더 씁니다).", flush=True)
    gpu_repo, cpu_repo = MODELS[args.base]
    out = Path(args.out)
    merged = out / "merged"
    max_len = args.max_len or (1536 if gpu else 1024)
    repo = gpu_repo if use_unsloth else (args.model_path or cpu_repo)

    status.update(stage="downloading", message=f"기본 모델 준비 중 ({repo})", device="cuda" if gpu else "cpu")
    if use_unsloth:
        from unsloth import FastLanguageModel

        model, tokenizer = FastLanguageModel.from_pretrained(model_name=gpu_repo, max_seq_length=max_len, load_in_4bit=True)
        model = FastLanguageModel.get_peft_model(
            model, r=16, lora_alpha=32, lora_dropout=0, target_modules=TARGET_MODULES,
            use_gradient_checkpointing="unsloth", random_state=3407,
        )
    else:
        from peft import LoraConfig, get_peft_model
        from transformers import AutoModelForCausalLM, AutoTokenizer

        tokenizer = AutoTokenizer.from_pretrained(repo)
        dtype = (torch.bfloat16 if torch.cuda.is_bf16_supported() else torch.float16) if gpu else torch.float32
        model = AutoModelForCausalLM.from_pretrained(repo, dtype=dtype) if _TF5 else AutoModelForCausalLM.from_pretrained(repo, torch_dtype=dtype)
        if gpu:
            model = model.to("cuda")
            model.gradient_checkpointing_enable()
            model.enable_input_require_grads()
        model = get_peft_model(model, LoraConfig(r=16, lora_alpha=32, lora_dropout=0.05, target_modules=TARGET_MODULES, task_type="CAUSAL_LM"))
        for p in model.parameters():  # LoRA weights in fp32 so fp16 mixed precision can unscale them
            if p.requires_grad:
                p.data = p.data.float()

    template = getattr(tokenizer, "chat_template", None) or ""
    if IM_START not in template:
        raise RuntimeError("ChatML(<|im_start|>) 채팅 템플릿 모델만 지원합니다 (Qwen2.5 계열).")

    data_path = Path(args.data)
    samples = read_samples(data_path)
    if args.delete_data:
        data_path.unlink(missing_ok=True)  # server mode: the conversation text is no longer needed on disk
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
            learning_rate=1e-4,
            warmup_steps=max(1, round(total * 0.03)),  # int works in transformers 4 and 5
            lr_scheduler_type="cosine",
            weight_decay=0.01,
            logging_steps=1,
            save_strategy="no",
            report_to="none",
            fp16=gpu and not bf16,
            bf16=bf16,
            optim="adamw_8bit" if use_unsloth else "adamw_torch",
            use_cpu=not gpu,
            seed=3407,
            remove_unused_columns=False,
        ),
    )
    trainer.train()

    status.update(stage="merging", message="LoRA 병합 중")
    if use_unsloth:
        model.save_pretrained_merged(str(merged), tokenizer, save_method="merged_16bit")
    else:
        model.merge_and_unload().save_pretrained(str(merged), safe_serialization=True)
        tokenizer.save_pretrained(str(merged))
    del trainer, model
    try:
        return export_gguf(merged, out, status, QUANT.get(args.base, "Q4_K_M"))
    finally:
        shutil.rmtree(merged, ignore_errors=True)
        shutil.rmtree(out / "checkpoints", ignore_errors=True)


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--data", required=True, help="persona_train.jsonl from the app or prepare_kakao.py")
    ap.add_argument("--out", default="persona_out", help="output folder (default: persona_out)")
    ap.add_argument("--status", help="progress JSON (default: <out>/status.json)")
    ap.add_argument("--delete-data", action="store_true", help="delete --data once read (used by server.py)")
    ap.add_argument("--base", choices=sorted(MODELS), default="qwen2.5-1.5b")
    ap.add_argument("--epochs", type=int, default=0, help="0 = automatic by number of replies")
    ap.add_argument("--max-len", type=int, default=0)
    ap.add_argument("--max-steps", type=int, default=0, help="testing only")
    ap.add_argument("--cpu", action="store_true", help="force CPU")
    ap.add_argument("--model-path", help="local model directory instead of the Hub (CPU path, testing)")
    args = ap.parse_args(argv)
    Path(args.out).mkdir(parents=True, exist_ok=True)
    status = Status(Path(args.status or Path(args.out) / "status.json"), console=sys.stdout.isatty() or not args.status)
    try:
        files = train(args, status)
        status.update(stage="done", files=files, message="완료")
        if not args.status:
            print("\n결과:", *(str(Path(args.out) / f) for f in files), sep="\n  ")
        return 0
    except Exception as e:  # reported to the app; the traceback stays in the server log only
        traceback.print_exc()
        status.update(stage="error", message=str(e)[:500])
        return 1
    finally:
        if args.delete_data:
            Path(args.data).unlink(missing_ok=True)


if __name__ == "__main__":
    sys.exit(main())
