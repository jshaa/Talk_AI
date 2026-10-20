#!/usr/bin/env python3
"""KakaoTalk export (.txt / Mac .csv) -> chat JSONL for fine-tuning a model on ONE person's speaking style.

The imitated person becomes the "assistant"; everyone else becomes the "user".
Identifiers (phone, account, card, resident id, e-mail, links) are masked, media placeholders dropped,
and replies about money / verification codes are cut out so the model never learns them.

This is a line-for-line port of apps/mimic/src/kakao/*.ts and persona/dataset.ts; both must produce
training/fixtures/expected.jsonl byte for byte (see test_prepare_kakao.py and apps/mimic/tests).

Usage:
    python prepare_kakao.py chat.txt --list                      # show speakers
    python prepare_kakao.py chat.txt --target "홍길동" --name "길동" -o persona_train.jsonl
"""
from __future__ import annotations

import argparse
import calendar
import json
import re
import sys
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path

SYSTEM_PROMPT_FILE = Path(__file__).with_name("system_prompt.txt")

# ---------------------------------------------------------------- parsing (kakao/parse.ts)

PC_DATE = re.compile(r"^-{3,} (\d{4})년 (\d{1,2})월 (\d{1,2})일 [월화수목금토일]요일 -{3,}$")
PC_MSG = re.compile(r"^\[([^\[\]]+(?:\[[^\[\]]*\][^\[\]]*)*)\] \[(오전|오후) (\d{1,2}):(\d{2})\](?: (.*))?$")
MOBILE_MSG = re.compile(
    r"^(\d{4})(?:년 |\. )(\d{1,2})(?:월 |\. )(\d{1,2})(?:일|\.) (?:(오전|오후) )?(\d{1,2}):(\d{2})(?::\d{2})?, (.+?) : (.*)$"
)
MOBILE_STAMP = re.compile(
    r"^(\d{4})(?:년 |\. )(\d{1,2})(?:월 |\. )(\d{1,2})(?:일|\.)(?: [월화수목금토일]요일)?(?: (?:오전|오후) ?)?(?: ?\d{1,2}:\d{2}(?::\d{2})?)?(?:[,:] .*)?$"
)
CSV_HEADER = re.compile(r"^﻿?Date,User,Message\s*$")
LINE_BREAK = re.compile(r"\r\n|\n|\r")


@dataclass
class Message:
    time: str | None
    speaker: str
    text: str


def _to_time(y: int, mo: int, d: int, ampm: str | None, h: int, mi: int) -> str | None:
    if mo < 1 or mo > 12 or mi > 59:
        return None
    if ampm:
        if h < 1 or h > 12:
            return None
        if ampm == "오전":
            h = 0 if h == 12 else h
        else:
            h = 12 if h == 12 else h + 12
    elif h > 23:
        return None
    if d < 1 or d > calendar.monthrange(y, mo)[1]:
        return None
    return f"{y}-{mo:02d}-{d:02d}T{h:02d}:{mi:02d}"


def detect_format(lines: list[str]) -> str | None:
    if lines and CSV_HEADER.match(lines[0]):
        return "csv"
    pc = mobile = 0
    for line in lines[:400]:
        if PC_DATE.match(line) or PC_MSG.match(line):
            pc += 1
        elif MOBILE_MSG.match(line):
            mobile += 1
    if pc == 0 and mobile == 0:
        return None
    return "pc" if pc >= mobile else "mobile"


def _is_header(line: str, index: int) -> bool:
    return index < 3 and (line.endswith("카카오톡 대화") or re.match(r"^저장한 날짜 ?: ", line) is not None)


def parse_export(raw: str) -> tuple[str | None, list[Message], int]:
    text = raw[1:] if raw.startswith("﻿") else raw
    lines = LINE_BREAK.split(text)
    fmt = detect_format(lines)
    if fmt == "csv":
        return (fmt, *_parse_csv(text))
    if fmt is None:
        return fmt, [], len(lines)
    return (fmt, *(_parse_pc(lines) if fmt == "pc" else _parse_mobile(lines)))


def _parse_pc(lines: list[str]) -> tuple[list[Message], int]:
    messages: list[Message] = []
    day: tuple[int, int, int] | None = None
    last: Message | None = None
    skipped = 0
    for i, line in enumerate(lines):
        if _is_header(line, i):
            continue
        sep = PC_DATE.match(line)
        if sep:
            y, mo, d = int(sep[1]), int(sep[2]), int(sep[3])
            day = (y, mo, d) if _to_time(y, mo, d, None, 0, 0) else None
            last = None
            continue
        m = PC_MSG.match(line)
        if m:
            time = _to_time(*day, m[2], int(m[3]), int(m[4])) if day else None
            last = Message(time, m[1], m[5] or "")
            messages.append(last)
            continue
        if last:
            last.text += "\n" + line
        elif line.strip():
            skipped += 1
    return [_trim(m) for m in messages], skipped


def _parse_mobile(lines: list[str]) -> tuple[list[Message], int]:
    messages: list[Message] = []
    last: Message | None = None
    skipped = 0
    for i, line in enumerate(lines):
        if _is_header(line, i):
            continue
        m = MOBILE_MSG.match(line)
        if m:
            time = _to_time(int(m[1]), int(m[2]), int(m[3]), m[4], int(m[5]), int(m[6]))
            last = Message(time, m[7], m[8])
            messages.append(last)
            continue
        if MOBILE_STAMP.match(line):
            last = None
            continue
        if last:
            last.text += "\n" + line
        elif line.strip():
            skipped += 1
    return [_trim(m) for m in messages], skipped


def _parse_csv(text: str) -> tuple[list[Message], int]:
    messages: list[Message] = []
    skipped = 0
    for row in _csv_rows(text)[1:]:
        if len(row) < 3:
            if "".join(row).strip():
                skipped += 1
            continue
        t = re.match(r"^(\d{4})-(\d{2})-(\d{2})[ T](\d{2}):(\d{2})", row[0])
        time = _to_time(int(t[1]), int(t[2]), int(t[3]), None, int(t[4]), int(t[5])) if t else None
        messages.append(Message(time, row[1], ",".join(row[2:])))
    return [_trim(m) for m in messages], skipped


def _csv_rows(text: str) -> list[list[str]]:
    rows: list[list[str]] = []
    row: list[str] = []
    field = ""
    quoted = False
    i = 0
    while i < len(text):
        c = text[i]
        if quoted:
            if c == '"':
                if i + 1 < len(text) and text[i + 1] == '"':
                    field += '"'
                    i += 1
                else:
                    quoted = False
            else:
                field += c
        elif c == '"':
            quoted = True
        elif c == ",":
            row.append(field)
            field = ""
        elif c in "\r\n":
            if c == "\r" and i + 1 < len(text) and text[i + 1] == "\n":
                i += 1
            row.append(field)
            rows.append(row)
            row, field = [], ""
        else:
            field += c
        i += 1
    if field or row:
        row.append(field)
        rows.append(row)
    return rows


def _trim(m: Message) -> Message:
    return Message(m.time, m.speaker, m.text.rstrip())


def speaker_counts(messages: list[Message]) -> list[tuple[str, int]]:
    counts: dict[str, int] = {}
    for m in messages:
        counts[m.speaker] = counts.get(m.speaker, 0) + 1
    return sorted(counts.items(), key=lambda kv: (-kv[1], kv[0]))


# ---------------------------------------------------------------- cleaning (kakao/clean.ts)

A = re.ASCII  # \b and \d must behave like JavaScript (ASCII only)


def _account(m: re.Match[str]) -> str:
    return "[계좌번호]" if len(re.sub(r"\D", "", m[0], flags=A)) >= 10 else m[0]


MASKS = [
    (re.compile(r"https?://\S+|www\.\S+", re.IGNORECASE | A), "[링크]"),
    (re.compile(r"[\w.+-]+@[\w-]+\.[\w.-]+", A), "[이메일]"),
    (re.compile(r"\b\d{6}\s?-\s?[1-8]\d{6}\b", A), "[주민번호]"),
    (re.compile(r"\b\d{4}[\s-]\d{4}[\s-]\d{4}[\s-]\d{4}\b", A), "[카드번호]"),
    (re.compile(r"\b01[016789][\s-]?\d{3,4}[\s-]?\d{4}\b", A), "[전화번호]"),
    (re.compile(r"\b0\d{1,2}[\s-]\d{3,4}[\s-]\d{4}\b", A), "[전화번호]"),
    (re.compile(r"\b\d{2,6}-\d{2,6}-\d{2,8}(?:-\d{1,6})?\b", A), _account),
    (re.compile(r"\b\d{10,16}\b", A), "[번호]"),
]

PLACEHOLDER = re.compile(
    "^(?:"
    + "|".join(
        [
            "사진", r"사진 \d+장", "동영상", "이모티콘", r"\(이모티콘\)", "음성메시지", "파일: .*", r"삭제된 메시지입니다\.",
            "보이스톡 .*", "페이스톡 .*", "지도: .*", "연락처: .*", "송금: .*", "선물: .*", "샵검색: .*", "라이브톡 .*",
            "이 메시지는 .*", "<사진 읽지 않음>", "<동영상 읽지 않음>", "Photo", "Video", "Emoticons",
        ]
    )
    + ")$",
    A,
)


def mask_pii(text: str) -> str:
    for pattern, label in MASKS:
        text = pattern.sub(label, text)
    return text


def _js_trim(s: str) -> str:
    return s.strip(" \t\n\r\x0b\x0c ﻿                　")


def clean_text(text: str) -> str | None:
    lines = [_js_trim(l) for l in text.split("\n")]
    lines = [l for l in lines if l and not PLACEHOLDER.match(l)]
    return mask_pii("\n".join(lines)) if lines else None


# ---------------------------------------------------------------- safety (persona/safety.ts)

RISK = [
    re.compile(r"계좌|송금|이체|입금|환불|대출|결제\s*(해|좀)|돈\s*(좀|을|이|보내|빌려|부쳐|필요)|급전|현금"),
    re.compile(r"인증\s*번호|인증\s*코드|비밀\s*번호|비번|OTP|보안\s*카드|카드\s*번호|주민\s*번호|신분증|공인\s*인증|본인\s*인증", re.IGNORECASE),
    re.compile(r"상품권|기프트\s*카드|구글\s*기프트|문화\s*상품권|기프티콘\s*(사|보내)"),
    re.compile(r"폰\s*(이\s*)?(고장|깨|액정)|휴대폰\s*(이\s*)?(고장|깨|액정)|임시\s*(폰|번호)|다른\s*번호로"),
    re.compile(r"(앱|어플)\s*(설치|깔아)|원격|링크\s*(눌러|클릭)"),
    re.compile(r"\d{2,6}-\d{2,6}-\d{2,8}", A),
]


def is_risky(text: str) -> bool:
    return any(p.search(text) for p in RISK)


# ---------------------------------------------------------------- dataset (persona/dataset.ts)


def system_prompt(name: str) -> str:
    return SYSTEM_PROMPT_FILE.read_text(encoding="utf-8").strip().replace("{name}", name.strip())


def _js_len(s: str) -> int:
    """String length in UTF-16 code units, like JavaScript's String.length."""
    return len(s.encode("utf-16-le")) // 2


def _minutes(time: str) -> int:
    dt = datetime.strptime(time, "%Y-%m-%dT%H:%M").replace(tzinfo=timezone.utc)
    return int(dt.timestamp() // 60)


def to_segments(messages: list[Message], target: str, session_gap_minutes: int = 180, max_turn_chars: int = 400) -> list[list[dict]]:
    others = list(dict.fromkeys(m.speaker for m in messages if m.speaker != target))
    group = len(others) > 1
    alias = {s: f"참여자{i + 1}" for i, s in enumerate(others)}

    segments: list[list[dict]] = []
    current: list[dict] = []
    last_time: int | None = None

    def flush() -> None:
        nonlocal current
        while current and current[0]["role"] == "assistant":
            current.pop(0)
        if any(t["role"] == "assistant" for t in current):
            segments.append(current)
        current = []

    for m in messages:
        text = clean_text(m.text)
        if text is None:
            continue
        t = _minutes(m.time) if m.time else None
        if t is not None and last_time is not None and t - last_time > session_gap_minutes:
            flush()
        if t is not None:
            last_time = t
        role = "assistant" if m.speaker == target else "user"
        content = "\n".join(f"{alias[m.speaker]}: {l}" for l in text.split("\n")) if role == "user" and group else text
        if current and current[-1]["role"] == role:
            current[-1]["content"] += "\n" + content
        else:
            current.append({"role": role, "content": content})
        last = current[-1]
        if _js_len(last["content"]) > max_turn_chars or (last["role"] == "assistant" and is_risky(last["content"])):
            current.pop()
            flush()
    flush()
    return segments


def build_samples(
    messages: list[Message],
    target: str,
    display_name: str | None = None,
    session_gap_minutes: int = 180,
    max_turns: int = 12,
    max_sample_chars: int = 1500,
    max_turn_chars: int = 400,
    overlap_turns: int = 4,
) -> list[dict]:
    system = {"role": "system", "content": system_prompt(display_name or target)}
    samples: list[dict] = []
    for turns in to_segments(messages, target, session_gap_minutes, max_turn_chars):
        start = 0
        while start < len(turns):
            end, chars = start, 0
            while end < len(turns) and end - start < max_turns and chars + _js_len(turns[end]["content"]) <= max_sample_chars:
                chars += _js_len(turns[end]["content"])
                end += 1
            if end == start:
                end = start + 1
            window = turns[start:end]
            while window and window[-1]["role"] != "assistant":
                window = window[:-1]
            if len(window) >= 2:
                samples.append({"messages": [system, *window]})
            if end >= len(turns):
                break
            # The next window starts a few turns back (on a user turn) so its first reply keeps its context.
            nxt = max(start + 1, end - overlap_turns)
            while nxt < len(turns) and turns[nxt]["role"] != "user":
                nxt += 1
            start = nxt
    return samples


def to_jsonl(samples: list[dict]) -> str:
    body = "\n".join(json.dumps(s, ensure_ascii=False, separators=(",", ":")) for s in samples)
    return body + "\n" if samples else ""


# ---------------------------------------------------------------- CLI


def read_text(path: Path) -> str:
    data = path.read_bytes()
    try:
        return data.decode("utf-8")
    except UnicodeDecodeError:
        return data.decode("cp949")  # older Windows exports


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("inputs", nargs="+", type=Path, help="KakaoTalk export files (.txt/.csv)")
    ap.add_argument("--target", help="speaker to imitate (exact name in the export)")
    ap.add_argument("--name", help="name used in the system prompt (default: --target); use the same one in the app")
    ap.add_argument("--list", action="store_true", help="list speakers and exit")
    ap.add_argument("-o", "--output", type=Path, default=Path("persona_train.jsonl"))
    ap.add_argument("--session-gap", type=int, default=180, help="minutes of silence that start a new conversation")
    ap.add_argument("--exclude", nargs="*", default=[], help="speakers to drop, e.g. bots, so a 1:1 chat is not treated as a group")
    args = ap.parse_args(argv)

    all_samples: list[dict] = []
    for path in args.inputs:
        fmt, messages, skipped = parse_export(read_text(path))
        if fmt is None:
            print(f"{path.name}: KakaoTalk export format not recognised", file=sys.stderr)
            return 2
        print(f"{path.name}: {fmt}, {len(messages)} messages, {skipped} unparsed lines", file=sys.stderr)
        if args.list or not args.target:
            for name, count in speaker_counts(messages):
                print(f"  {count:7d}  {name}")
            continue
        messages = [m for m in messages if m.speaker not in set(args.exclude)]
        all_samples += build_samples(messages, args.target, args.name, session_gap_minutes=args.session_gap)

    if args.list or not args.target:
        return 0
    args.output.write_text(to_jsonl(all_samples), encoding="utf-8", newline="\n")
    replies = sum(1 for s in all_samples for t in s["messages"] if t["role"] == "assistant")
    print(f"wrote {len(all_samples)} samples ({replies} replies) -> {args.output}", file=sys.stderr)
    if replies < 300:
        print("warning: fewer than 300 replies; fine-tuning will be weak (1,000+ recommended)", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
