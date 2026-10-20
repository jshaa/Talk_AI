#!/usr/bin/env python3
"""Generates replies on this PC (base qwen3-4b-pc), run by server.py; the phone only sends the conversation.

Loads the 4-bit base model + the LoRA adapter saved by persona_train.py and answers one request per line:
    stdin:  {"messages": [{"role": "system"|"user"|"assistant", "content": "..."}], "temperature": 0.8}
    stdout: {"ready": true} once loaded, then {"reply": "..."} or {"error": "..."} per request.
The prompt is ChatML exactly as in training. Several candidates are sampled and the one that does not repeat
the conversation (or answer a question with a bare "어어엉") is returned. Nothing is logged or stored.
"""
from __future__ import annotations

import argparse
import json
import os
import re
import sys

from persona_train import IM_END, IM_START, is_backchannel

CANDIDATES = 3


def chatml(messages: list[dict]) -> str:
    return "".join(f"{IM_START}{m['role']}\n{m['content']}{IM_END}\n" for m in messages) + f"{IM_START}assistant\n"


def fit(messages: list[dict], count, limit: int) -> list[dict]:
    """Drops the oldest turns (keeping the system prompt and a user turn first) until the prompt fits."""
    system = [m for m in messages if m["role"] == "system"]
    turns = [m for m in messages if m["role"] != "system"]
    while len(turns) > 1 and (count(chatml(system + turns)) > limit or turns[0]["role"] != "user"):
        turns.pop(0)
    return system + turns


def pick(candidates: list[str], messages: list[dict]) -> str:
    """The first candidate with the fewest problems: empty, repeating a recent line, echoing the user,
    or only an interjection when the user asked something."""
    recent = {line.strip() for m in messages[-10:] if m["role"] != "system" for line in m["content"].split("\n") if line.strip()}
    last_user = next((m["content"] for m in reversed(messages) if m["role"] == "user"), "")
    asked = "?" in last_user or len(last_user) >= 12

    def problems(c: str) -> int:
        lines = [line.strip() for line in c.split("\n") if line.strip()]
        if not lines:
            return 10
        n = 3 * sum(line in recent for line in lines) // len(lines)
        return n + (2 if asked and all(is_backchannel(line) for line in lines) else 0)

    return min(candidates, key=problems)


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--adapter", required=True, help="LoRA adapter folder from persona_train.py")
    ap.add_argument("--context", type=int, default=3072)
    args = ap.parse_args()

    # Protocol on the real stdout; library banners and warnings go to stderr (the server's log).
    proto = os.fdopen(os.dup(1), "w", encoding="utf-8", buffering=1)
    os.dup2(2, 1)

    import unsloth  # noqa: F401  (before transformers)
    import torch
    from unsloth import FastLanguageModel

    model, tokenizer = FastLanguageModel.from_pretrained(model_name=args.adapter, max_seq_length=args.context, load_in_4bit=True)
    FastLanguageModel.for_inference(model)
    end = tokenizer.convert_tokens_to_ids(IM_END)
    count = lambda s: len(tokenizer(s, add_special_tokens=False).input_ids)  # noqa: E731
    proto.write(json.dumps({"ready": True}) + "\n")

    for line in sys.stdin:
        try:
            req = json.loads(line)
            max_new = int(req.get("max_tokens", 160))
            messages = fit(req["messages"], count, args.context - max_new)
            ids = tokenizer(chatml(messages), add_special_tokens=False, return_tensors="pt").input_ids.to(model.device)
            with torch.inference_mode():
                out = model.generate(
                    input_ids=ids, attention_mask=torch.ones_like(ids), max_new_tokens=max_new, do_sample=True,
                    temperature=float(req.get("temperature", 0.8)), top_p=0.92, top_k=50, min_p=0.05,
                    repetition_penalty=1.12, num_return_sequences=CANDIDATES, eos_token_id=end, pad_token_id=end,
                )
            texts = [tokenizer.decode(o[ids.shape[1]:], skip_special_tokens=True) for o in out]
            replies = [re.sub(r"<think>.*?</think>", "", t, flags=re.S).strip() for t in texts]
            proto.write(json.dumps({"reply": pick(replies, messages)}, ensure_ascii=False) + "\n")
        except Exception as e:  # one bad request must not stop the worker
            proto.write(json.dumps({"error": str(e)[:300]}, ensure_ascii=False) + "\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
