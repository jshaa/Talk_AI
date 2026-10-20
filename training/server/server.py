#!/usr/bin/env python3
"""말투 AI training server for the user's own PC (Docker Desktop).

- :8000  API for the app (token required) + the web app itself for LAN use.
- :8001  pairing page with a QR code (compose publishes it on 127.0.0.1 only).

The phone uploads the chat JSONL, the PC fine-tunes (persona_train.py) and the phone downloads the
GGUF. One job at a time; only the newest job's files are kept; uploaded text is deleted as soon as
training has read it. Nothing is sent anywhere except the base-model download from Hugging Face.
"""
from __future__ import annotations

import asyncio
import hmac
import io
import json
import os
import secrets
import shutil
import signal
import subprocess
import sys
import threading
import time
import urllib.request
import uuid
from html import escape
from pathlib import Path
from urllib.parse import quote

from fastapi import FastAPI, HTTPException, Request
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import FileResponse, HTMLResponse, JSONResponse, PlainTextResponse
from fastapi.staticfiles import StaticFiles

DATA = Path(os.environ.get("TRAINER_DATA", "/data"))
JOBS = DATA / "jobs"
WEB_DIR = Path(os.environ.get("TRAINER_WEB", "/app/web"))
# Empty: the QR opens the app served by this PC through the tunnel (works from any network, always matches
# this server). Set e.g. https://jshaa.github.io/Talk_AI/ to use the GitHub Pages build instead.
APP_URL = os.environ.get("APP_URL", "")
PUBLIC_URL = os.environ.get("PUBLIC_URL", "").rstrip("/")
TUNNEL_METRICS = os.environ.get("TUNNEL_METRICS", "http://tunnel:20241")
MAX_UPLOAD = int(os.environ.get("MAX_UPLOAD_MB", "64")) * 1024 * 1024
TRAIN_SCRIPT = Path(os.environ.get("TRAIN_SCRIPT") or Path(__file__).with_name("persona_train.py"))
ORIGINS = [o for o in os.environ.get("ALLOW_ORIGINS", "https://jshaa.github.io,https://localhost,http://localhost:5173,http://localhost:4173").split(",") if o]
BASES = {"qwen2.5-0.5b", "qwen2.5-1.5b", "qwen2.5-3b", "qwen3-4b", "qwen3-4b-pc"}
CHAT_WORKER = Path(os.environ.get("CHAT_WORKER") or Path(__file__).with_name("chat_worker.py"))
ID_CHARS = set("0123456789abcdef")


def load_token() -> str:
    if os.environ.get("TRAINER_TOKEN"):
        return os.environ["TRAINER_TOKEN"]
    DATA.mkdir(parents=True, exist_ok=True)
    f = DATA / "token"
    if not f.exists():
        f.write_text(secrets.token_urlsafe(18), encoding="utf-8")
        f.chmod(0o600)
    return f.read_text(encoding="utf-8").strip()


TOKEN = load_token()
PROCS: dict[str, subprocess.Popen] = {}


def device() -> str:
    try:
        import torch

        if torch.cuda.is_available():
            return f"GPU: {torch.cuda.get_device_name(0)}"
    except Exception:
        pass
    return "CPU (느림: 0.5B 권장)"


DEVICE = device()


def authorize(request: Request) -> None:
    given = request.headers.get("authorization", "").removeprefix("Bearer ").strip()
    if not given or not hmac.compare_digest(given, TOKEN):
        raise HTTPException(401, "토큰이 맞지 않습니다.")


def job_dir(job_id: str) -> Path:
    if len(job_id) != 32 or not set(job_id) <= ID_CHARS:
        raise HTTPException(404, "작업이 없습니다.")
    d = JOBS / job_id
    if not d.is_dir():
        raise HTTPException(404, "작업이 없습니다.")
    return d


def busy() -> str | None:
    for job_id, p in list(PROCS.items()):
        if p.poll() is None:
            return job_id
        PROCS.pop(job_id)
    return None


class ChatWorker:
    """One chat_worker.py process holding the PC-only model on the GPU; stopped while a job trains."""

    def __init__(self) -> None:
        self.proc: subprocess.Popen | None = None
        self.adapter: Path | None = None
        self.ready = threading.Event()
        self.failed = ""
        self.lock = threading.Lock()
        self.log = None

    def stop(self) -> None:
        if self.proc and self.proc.poll() is None:
            self.proc.kill()
            self.proc.wait(timeout=30)
        if self.log:
            self.log.close()
        self.proc, self.adapter, self.failed, self.log = None, None, "", None
        self.ready.clear()

    def warm(self, adapter: Path) -> None:
        """Starts loading the model in the background (takes a minute) unless it is already loaded."""
        if self.adapter == adapter and self.proc and self.proc.poll() is None:
            return
        self.stop()
        self.log = open(adapter.parent.parent / "chat.log", "a")
        self.proc = subprocess.Popen([sys.executable, str(CHAT_WORKER), "--adapter", str(adapter)], stdin=subprocess.PIPE,
                                     stdout=subprocess.PIPE, stderr=self.log, text=True, encoding="utf-8", bufsize=1)
        self.adapter = adapter
        proc = self.proc

        def wait_ready() -> None:
            line = proc.stdout.readline()
            if proc is self.proc:
                if '"ready"' in line:
                    self.ready.set()
                else:
                    self.failed = "PC 답장 모델을 불러오지 못했습니다(GPU 메모리 부족일 수 있음). PC의 chat.log를 확인하세요."

        threading.Thread(target=wait_ready, daemon=True).start()

    def ask(self, payload: dict, wait: float) -> dict:
        if not self.ready.wait(wait):
            raise HTTPException(500 if self.failed else 503, self.failed or "PC에서 모델을 불러오는 중입니다. 1분 뒤 다시 보내 주세요.")
        with self.lock:
            proc = self.proc
            if proc is None or proc.poll() is not None:
                self.stop()
                raise HTTPException(503, "PC 답장 프로그램이 멈췄습니다. 다시 보내 주세요.")
            proc.stdin.write(json.dumps(payload, ensure_ascii=False) + "\n")
            proc.stdin.flush()
            line = proc.stdout.readline()
        if not line:
            self.stop()
            raise HTTPException(503, "PC 답장 프로그램이 멈췄습니다. 다시 보내 주세요.")
        return json.loads(line)


CHAT = ChatWorker()


def chat_adapter(d: Path, status: dict) -> Path | None:
    adapter = d / "out" / str(status.get("adapter", ""))
    return adapter if status.get("stage") == "done" and status.get("adapter") and adapter.is_dir() else None


def newest_job() -> Path | None:
    jobs = [d for d in JOBS.iterdir() if (d / "job.json").exists()] if JOBS.exists() else []
    return max(jobs, key=lambda d: (d / "job.json").stat().st_mtime) if jobs else None


def read_status(d: Path) -> dict:
    f = d / "status.json"
    return json.loads(f.read_text(encoding="utf-8")) if f.exists() else {"stage": "queued"}


def job_view(d: Path) -> dict:
    job = json.loads((d / "job.json").read_text(encoding="utf-8"))
    status = read_status(d)
    proc = PROCS.get(d.name)
    if status.get("stage") not in ("done", "error", "cancelled") and (proc is None or proc.poll() is not None):
        status.update(stage="error", message=status.get("message") or "학습 프로세스가 중단되었습니다.")
    files = [{"name": n, "size": (d / "out" / n).stat().st_size} for n in status.get("files", []) if (d / "out" / n).exists()]
    adapter = chat_adapter(d, status)
    if adapter and not busy():
        CHAT.warm(adapter)  # load it before the phone's first message
    return {**job, **status, "files": files, "chat": adapter is not None}


def validate_jsonl(text: str) -> tuple[int, int]:
    samples = replies = 0
    for i, line in enumerate(text.splitlines(), 1):
        if not line.strip():
            continue
        try:
            msgs = json.loads(line)["messages"]
            assert isinstance(msgs, list) and msgs and msgs[0]["role"] == "system"
            for m in msgs:
                assert m["role"] in ("system", "user", "assistant") and isinstance(m["content"], str)
        except Exception:
            raise HTTPException(400, f"{i}번째 줄이 학습 데이터 형식이 아닙니다.")
        samples += 1
        replies += sum(1 for m in msgs if m["role"] == "assistant")
    if samples == 0 or replies == 0:
        raise HTTPException(400, "학습 데이터가 비어 있습니다.")
    return samples, replies


api = FastAPI(title="말투 AI trainer", docs_url=None, redoc_url=None, openapi_url=None)
api.add_middleware(
    CORSMiddleware,
    allow_origins=ORIGINS,
    allow_origin_regex=r"https://[a-z0-9-]+\.trycloudflare\.com",
    allow_methods=["GET", "POST", "DELETE"],
    allow_headers=["Authorization", "Content-Type"],
    expose_headers=["Content-Length", "Content-Range", "Accept-Ranges"],
    max_age=600,
)


@api.middleware("http")
async def isolation_headers(request: Request, call_next):
    # The app needs CORS for cross-origin use and CORP so a cross-origin-isolated page may load files.
    response = await call_next(request)
    if not request.url.path.startswith("/api/"):
        # The app served from this PC (tunnel/LAN) is cross-origin isolated without the service-worker reload.
        response.headers["Cross-Origin-Opener-Policy"] = "same-origin"
        response.headers["Cross-Origin-Embedder-Policy"] = "require-corp"
    response.headers["Cross-Origin-Resource-Policy"] = "cross-origin"
    response.headers["Cache-Control"] = "no-store"
    response.headers["X-Content-Type-Options"] = "nosniff"
    return response


@api.get("/api/health")
def health() -> dict:
    return {"ok": True, "app": "maltu-ai-trainer", "device": DEVICE, "busy": busy() is not None}


@api.post("/api/jobs")
async def create_job(request: Request) -> dict:
    authorize(request)
    if int(request.headers.get("content-length") or 0) > MAX_UPLOAD:
        raise HTTPException(413, "학습 데이터가 너무 큽니다.")
    if busy():
        raise HTTPException(409, "이미 학습 중인 작업이 있습니다.")
    body = await request.body()
    if len(body) > MAX_UPLOAD:
        raise HTTPException(413, "학습 데이터가 너무 큽니다.")
    try:
        req = json.loads(body)
        jsonl, base = req["jsonl"], req.get("base", "qwen2.5-1.5b")
        epochs = int(req.get("epochs") or 0)
        persona = str(req.get("persona", ""))[:40]
    except Exception:
        raise HTTPException(400, "요청 형식이 잘못되었습니다.")
    if base not in BASES or not 0 <= epochs <= 5 or not isinstance(jsonl, str):
        raise HTTPException(400, "모델 또는 에폭 값이 잘못되었습니다.")
    samples, replies = validate_jsonl(jsonl)

    CHAT.stop()  # frees the GPU for training (and the old job's adapter is deleted next)
    # Keep only the newest job on disk.
    if JOBS.exists():
        for old in JOBS.iterdir():
            shutil.rmtree(old, ignore_errors=True)
    job_id = uuid.uuid4().hex
    d = JOBS / job_id
    (d / "out").mkdir(parents=True)
    (d / "data.jsonl").write_text(jsonl, encoding="utf-8")
    job = {"id": job_id, "base": base, "persona": persona, "samples": samples, "replies": replies, "created": time.time()}
    (d / "job.json").write_text(json.dumps(job, ensure_ascii=False), encoding="utf-8")
    cmd = [sys.executable, str(TRAIN_SCRIPT), "--data", str(d / "data.jsonl"), "--out", str(d / "out"),
           "--status", str(d / "status.json"), "--base", base, "--epochs", str(epochs), "--delete-data"]
    cmd += json.loads(os.environ.get("TRAINER_EXTRA_ARGS", "[]"))
    log = open(d / "train.log", "w")
    PROCS[job_id] = subprocess.Popen(cmd, stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
    return job


@api.post("/api/chat")
async def chat(request: Request) -> dict:
    """Reply generated on this PC by the newest PC-only model (the phone only sends the recent conversation)."""
    authorize(request)
    try:
        req = json.loads(await request.body())
        messages = [{"role": str(m["role"]), "content": str(m["content"])} for m in req["messages"]][-60:]
        temperature = min(max(float(req.get("temperature", 0.8)), 0.1), 1.5)
    except Exception:
        raise HTTPException(400, "요청 형식이 잘못되었습니다.")
    if not messages or any(m["role"] not in ("system", "user", "assistant") for m in messages) or sum(len(m["content"]) for m in messages) > 12000:
        raise HTTPException(400, "대화 형식이 잘못되었습니다.")
    if busy():
        raise HTTPException(409, "PC가 학습 중이라 지금은 PC 답장을 쓸 수 없습니다.")
    d = newest_job()
    adapter = chat_adapter(d, read_status(d)) if d else None
    if adapter is None:
        raise HTTPException(404, "PC에 PC 답장용 모델이 없습니다. 'Qwen3-4B · PC에서 답장'으로 먼저 학습하세요.")
    CHAT.warm(adapter)
    out = await asyncio.to_thread(CHAT.ask, {"messages": messages, "temperature": temperature, "max_tokens": 160}, 75)
    if "error" in out:
        raise HTTPException(500, f"답장을 만들지 못했습니다: {out['error']}")
    return {"reply": out["reply"]}


@api.get("/api/jobs/latest")
def latest_job(request: Request) -> dict:
    """The job on disk (only the newest is kept), so a phone can load a model trained from the PC itself."""
    authorize(request)
    d = newest_job()
    if d is None:
        raise HTTPException(404, "작업이 없습니다.")
    return job_view(d)


@api.get("/api/jobs/{job_id}")
def get_job(job_id: str, request: Request) -> dict:
    authorize(request)
    return job_view(job_dir(job_id))


@api.post("/api/jobs/{job_id}/cancel")
def cancel_job(job_id: str, request: Request) -> dict:
    authorize(request)
    d = job_dir(job_id)
    proc = PROCS.get(job_id)
    if proc and proc.poll() is None:
        os.killpg(proc.pid, signal.SIGTERM)
        proc.wait(timeout=30)
    (d / "data.jsonl").unlink(missing_ok=True)
    status = d / "status.json"
    data = json.loads(status.read_text(encoding="utf-8")) if status.exists() else {}
    if data.get("stage") != "done":
        data.update(stage="cancelled", message="취소됨")
        status.write_text(json.dumps(data, ensure_ascii=False), encoding="utf-8")
    return job_view(d)


@api.delete("/api/jobs/{job_id}")
def delete_job(job_id: str, request: Request) -> dict:
    cancel_job(job_id, request)
    CHAT.stop()
    shutil.rmtree(job_dir(job_id), ignore_errors=True)
    return {"deleted": job_id}


@api.get("/api/jobs/{job_id}/files/{name}")
def get_file(job_id: str, name: str, request: Request):
    authorize(request)
    d = job_dir(job_id)
    if name not in {f["name"] for f in job_view(d)["files"]}:
        raise HTTPException(404, "파일이 없습니다.")
    return FileResponse(d / "out" / name, media_type="application/octet-stream", filename=name)


@api.exception_handler(HTTPException)
async def http_error(_: Request, exc: HTTPException):
    return JSONResponse({"error": exc.detail}, status_code=exc.status_code)


if WEB_DIR.is_dir():
    # LAN mode: the phone opens http://<PC>:8000/ and talks to the API on the same origin.
    api.mount("/", StaticFiles(directory=WEB_DIR, html=True), name="web")


# ---------------------------------------------------------------- pairing page (host loopback only)

pair = FastAPI(docs_url=None, redoc_url=None, openapi_url=None)


def public_url() -> str:
    if PUBLIC_URL:
        return PUBLIC_URL
    try:
        with urllib.request.urlopen(f"{TUNNEL_METRICS}/quicktunnel", timeout=2) as r:
            host = json.loads(r.read()).get("hostname")
            return f"https://{host}" if host else ""
    except Exception:
        return ""


def qr_svg(text: str) -> str:
    import qrcode
    import qrcode.image.svg

    buf = io.BytesIO()
    qrcode.make(text, image_factory=qrcode.image.svg.SvgPathImage, box_size=8, border=2).save(buf)
    return buf.getvalue().decode()


def pairing_link(url: str) -> str:
    """App address that connects to this PC when opened (server URL + token in the #fragment, never sent)."""
    app = APP_URL or (f"{url}/" if WEB_DIR.is_dir() else "https://jshaa.github.io/Talk_AI/")
    return f"{app}#trainer={quote(url, safe='')}&token={quote(TOKEN, safe='')}" if url else ""


@pair.get("/link", response_class=PlainTextResponse)
def share_link() -> str:
    """Plain link for sharing without a QR scan, e.g. (iwr http://localhost:8001/link).Content | Set-Clipboard"""
    link = pairing_link(public_url())
    if not link:
        raise HTTPException(503, "외부 주소(터널)를 아직 받지 못했습니다.")
    return link


@pair.get("/", response_class=HTMLResponse)
def pairing_page() -> str:
    url = public_url()
    link = pairing_link(url)
    qr = qr_svg(link) if link else ""
    body = (
        "<p>휴대폰에서 아래 <b>연결 주소</b>를 열면(카카오톡 나에게 보내기·메일 등으로 전달) 말투 AI가 이 PC에 연결됩니다. "
        "Wi-Fi·LTE 등 어느 네트워크에서나 됩니다.</p>"
        f"<p><input id=link readonly value=\"{escape(link)}\" onclick=\"this.select()\"> <button id=copy>주소 복사</button> <span id=copied class=small></span></p>"
        f"<p class=small>또는 휴대폰 카메라로 QR 찍기:</p><div class=qr>{qr}</div>"
        f"<p class=small>서버: <code>{escape(url)}</code> · <a href=\"{escape(link)}\">이 PC에서 앱 열기</a></p>"
        "<script>document.getElementById('copy').onclick=async()=>{const i=document.getElementById('link');"
        "try{await navigator.clipboard.writeText(i.value)}catch{i.select();document.execCommand('copy')}"
        "document.getElementById('copied').textContent='복사됨'}</script>"
        if link
        else "<p>외부 주소(터널)를 아직 받지 못했습니다. 10초 뒤 새로고침하거나, 같은 Wi-Fi라면 아래 LAN 방식을 쓰세요.</p>"
    )
    return f"""<!doctype html><html lang=ko><meta charset=utf-8><meta name=viewport content="width=device-width,initial-scale=1">
<title>말투 AI 학습 서버</title><style>body{{font:16px/1.6 system-ui,'Malgun Gothic',sans-serif;max-width:640px;margin:32px auto;padding:0 16px}}
#link{{width:100%;box-sizing:border-box;font:13px monospace;padding:8px}}.qr svg{{width:220px;height:220px;background:#fff}}code{{background:#eee;padding:2px 6px;border-radius:4px;word-break:break-all}}.small{{font-size:13px;color:#555}}</style>
<h1>말투 AI 학습 서버</h1><p>장치: <b>{escape(DEVICE)}</b></p>{body}
<h2>같은 Wi-Fi(LAN)에서 쓰기</h2><p>휴대폰 브라우저로 <code>http://(이 PC의 IP):8000</code>을 열고, 앱 ② 탭 "내 PC로 학습"에 토큰을 입력하세요.<br>토큰: <code>{escape(TOKEN)}</code></p>
<p class=small>이 페이지는 이 PC에서만 열립니다. 연결 주소와 토큰은 학습 서버를 쓸 수 있는 비밀번호와 같으니 믿을 수 있는 사람에게만 보내세요. 터널을 다시 켜면 주소가 바뀝니다.</p></html>"""


async def serve() -> None:
    import uvicorn

    servers = [
        uvicorn.Server(uvicorn.Config(api, host="0.0.0.0", port=8000, log_level="warning", access_log=False)),
        uvicorn.Server(uvicorn.Config(pair, host="0.0.0.0", port=8001, log_level="warning", access_log=False)),
    ]
    print(f"말투 AI 학습 서버 · {DEVICE}\n  PC 브라우저로 http://localhost:8001 을 열어 휴대폰을 연결하세요.", flush=True)
    await asyncio.gather(*(s.serve() for s in servers))


if __name__ == "__main__":
    asyncio.run(serve())
