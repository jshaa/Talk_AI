#!/usr/bin/env python3
"""Imports a persona adapter trained on Colab (training/train_pc_8b_colab.ipynb), run by server.py.

The zip holds the PEFT adapter (adapter_config.json, adapter_model.safetensors) and meta.json. The adapter is
converted to a llama.cpp LoRA GGUF, the quantized base model is downloaded once, and both are placed where the
`llm` service (llm.sh) serves them: <llm-dir>/base.gguf and <llm-dir>/lora.gguf.
"""
from __future__ import annotations

import argparse
import json
import os
import shutil
import sys
import traceback
import zipfile
from pathlib import Path

from persona_train import LLAMA_CPP, TARGET_MODULES, Status, run

# Importable bases -> (HF model id for the converter, GGUF repo, GGUF file served by llama.cpp).
IMPORTS = {"qwen3-8b-pc": ("Qwen/Qwen3-8B", "Qwen/Qwen3-8B-GGUF", "Qwen3-8B-Q4_K_M.gguf")}
FILES = {"adapter_config.json", "adapter_model.safetensors", "meta.json"}
MAX_UNZIPPED = 2 * 1024**3


def read_meta(path: Path) -> dict:
    """Checks the zip and returns meta.json; raises ValueError with a message for the user."""
    try:
        with zipfile.ZipFile(path) as z:
            entries = {Path(i.filename).name: i for i in z.infolist() if not i.is_dir()}
            if set(entries) != FILES or sum(i.file_size for i in entries.values()) > MAX_UNZIPPED:
                raise ValueError("Colab 노트북에서 받은 어댑터 zip이 아닙니다 (adapter_config.json, adapter_model.safetensors, meta.json 필요).")
            meta = json.loads(z.read(entries["meta.json"]))
            config = json.loads(z.read(entries["adapter_config.json"]))
    except zipfile.BadZipFile as e:
        raise ValueError("zip 파일이 아닙니다.") from e
    except (json.JSONDecodeError, UnicodeDecodeError) as e:
        raise ValueError("meta.json 또는 adapter_config.json을 읽지 못했습니다.") from e
    if meta.get("base") not in IMPORTS:
        raise ValueError(f"지원하지 않는 기본 모델입니다: {meta.get('base')}")
    if not isinstance(meta.get("persona"), str) or not 0 < len(meta["persona"]) <= 40:
        raise ValueError("meta.json에 따라할 이름(persona)이 없습니다.")
    if config.get("peft_type") != "LORA" or not set(config.get("target_modules") or []) <= set(TARGET_MODULES) or int(config.get("r", 0)) > 128:
        raise ValueError("지원하지 않는 어댑터 형식입니다.")
    return meta


def extract(path: Path, dest: Path) -> None:
    dest.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(path) as z:
        for info in z.infolist():
            if not info.is_dir() and Path(info.filename).name in FILES:
                with z.open(info) as src, open(dest / Path(info.filename).name, "wb") as out:
                    shutil.copyfileobj(src, out)


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--zip", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--status", required=True)
    ap.add_argument("--llm-dir", required=True)
    ap.add_argument("--delete-zip", action="store_true")
    args = ap.parse_args(argv)

    out, llm = Path(args.out), Path(args.llm_dir)
    status = Status(Path(args.status))
    try:
        meta = read_meta(Path(args.zip))
        base_id, gguf_repo, gguf_file = IMPORTS[meta["base"]]
        status.update(stage="converting", message="어댑터 변환 중", replies=meta.get("replies"), loss=meta.get("loss"))
        extract(Path(args.zip), out / "adapter")
        if args.delete_zip:
            Path(args.zip).unlink(missing_ok=True)
        run([sys.executable, str(LLAMA_CPP / "convert_lora_to_gguf.py"), str(out / "adapter"),
             "--base-model-id", base_id, "--outtype", "f16", "--outfile", str(out / "lora.gguf")])
        shutil.rmtree(out / "adapter", ignore_errors=True)

        status.update(stage="downloading", message="기본 모델 받는 중 (처음 한 번, 약 5GB)")
        from huggingface_hub import hf_hub_download

        base = hf_hub_download(gguf_repo, gguf_file)
        llm.mkdir(parents=True, exist_ok=True)
        (llm / "base.tmp").unlink(missing_ok=True)
        os.symlink(base, llm / "base.tmp")
        os.replace(llm / "base.tmp", llm / "base.gguf")
        os.replace(out / "lora.gguf", llm / "lora.gguf")  # the llm service (re)starts when this appears
        status.update(stage="done", llm=True, files=[], message="완료")
        return 0
    except Exception as e:
        traceback.print_exc()
        status.update(stage="error", message=str(e)[:500])
        return 1
    finally:
        shutil.rmtree(out / "adapter", ignore_errors=True)
        if args.delete_zip:
            Path(args.zip).unlink(missing_ok=True)


if __name__ == "__main__":
    sys.exit(main())
