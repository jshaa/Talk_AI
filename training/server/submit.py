#!/usr/bin/env python3
"""Start a training job from this PC (no phone needed) on the running Docker training server.

    python training/server/submit.py chat.txt --target "홍길동" --exclude 플레이봇 --base qwen2.5-1.5b

The KakaoTalk export becomes chat JSONL (prepare_kakao.py), is sent to http://localhost:8000 and trained.
When it is done, the phone app (QR on http://localhost:8001) shows "학습된 모델 불러오기" for this model.
The token is read from the trainer container; nothing leaves this PC.
"""
from __future__ import annotations

import argparse
import json
import subprocess
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import prepare_kakao as pk  # noqa: E402


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("inputs", nargs="+", type=Path, help="KakaoTalk export files (.txt/.csv)")
    ap.add_argument("--target", required=True, help="speaker to imitate (exact name in the export)")
    ap.add_argument("--name", help="name shown in the app and used in the system prompt (default: --target)")
    ap.add_argument("--exclude", nargs="*", default=[], help="speakers to drop, e.g. bots")
    ap.add_argument("--base", default="qwen2.5-1.5b", choices=["qwen2.5-0.5b", "qwen2.5-1.5b", "qwen2.5-3b", "qwen3-4b", "qwen3-4b-pc"])
    ap.add_argument("--server", default="http://localhost:8000")
    ap.add_argument("--container", default="maltu-ai-trainer-trainer-1")
    args = ap.parse_args(argv)

    samples: list[dict] = []
    for path in args.inputs:
        fmt, messages, _ = pk.parse_export(pk.read_text(path))
        if fmt is None:
            print(f"{path.name}: KakaoTalk export format not recognised", file=sys.stderr)
            return 2
        messages = [m for m in messages if m.speaker not in set(args.exclude)]
        # Replies made on the PC see ~3,000 characters of conversation, so they train on windows that long.
        longer = {"max_turns": 24, "max_sample_chars": 3000, "overlap_turns": 6} if args.base.endswith("-pc") else {}
        samples += pk.build_samples(messages, args.target, args.name, **longer)
    if not samples:
        print(f"'{args.target}'의 답장이 없습니다. --target 이름을 확인하세요(prepare_kakao.py --list).", file=sys.stderr)
        return 2

    token = subprocess.run(["docker", "exec", args.container, "cat", "/data/token"], capture_output=True, text=True, check=True).stdout.strip()

    def call(path: str, body: bytes | None = None) -> dict:
        req = urllib.request.Request(args.server + path, data=body, method="POST" if body else "GET",
                                     headers={"Authorization": f"Bearer {token}", "Content-Type": "application/json"})
        try:
            with urllib.request.urlopen(req, timeout=120) as r:
                return json.loads(r.read())
        except urllib.error.HTTPError as e:
            raise SystemExit(f"서버 오류 {e.code}: {json.loads(e.read()).get('error')}")

    body = {"jsonl": pk.to_jsonl(samples), "base": args.base, "persona": args.name or args.target}
    job = call("/api/jobs", json.dumps(body, ensure_ascii=False).encode("utf-8"))
    print(f"학습 시작: {len(samples)}개 샘플 · {args.base}", flush=True)
    last = None
    while job.get("stage", "queued") not in ("done", "error", "cancelled"):
        time.sleep(10)
        job = call(f"/api/jobs/{job['id']}")
        line = f"{job['stage']} {job.get('step') or ''}/{job.get('total') or ''} {job.get('message') or ''}"
        if line != last:
            print(time.strftime("%H:%M:%S"), line, flush=True)
            last = line
    if job["stage"] != "done":
        return 1
    if job.get("chat"):
        print("완료. 휴대폰에서 연결 주소로 앱을 열면 'PC에서 답장' 방식으로 바로 대화할 수 있습니다.")
    else:
        print("완료. 휴대폰에서 앱을 열면 ② 탭에 '학습된 모델 불러오기'가 나옵니다.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
