"""API tests with a fake trainer: python -m pytest training/server (needs fastapi, httpx, qrcode)."""
import json
import os
import sys
import tempfile
import time
from pathlib import Path

HERE = Path(__file__).parent
TMP = Path(tempfile.mkdtemp())
FAKE = TMP / "fake_train.py"
FAKE.write_text(
    """
import argparse, json, pathlib, time
ap = argparse.ArgumentParser(); [ap.add_argument(a) for a in ("--data","--out","--status","--base","--epochs")]
a = ap.parse_args()
st = pathlib.Path(a.status)
st.write_text(json.dumps({"stage": "training", "step": 1, "total": 2}))
time.sleep(float(__import__("os").environ.get("FAKE_SLEEP", "0.2")))
pathlib.Path(a.data).unlink()
(pathlib.Path(a.out) / "persona-q4_k_m.gguf").write_bytes(b"GGUF" + b"\\0" * 60)
st.write_text(json.dumps({"stage": "done", "files": ["persona-q4_k_m.gguf"]}))
""",
    encoding="utf-8",
)
os.environ.update(TRAINER_DATA=str(TMP / "data"), TRAINER_TOKEN="secret", TRAIN_SCRIPT=str(FAKE), TRAINER_WEB=str(TMP / "noweb"), PUBLIC_URL="https://abc.trycloudflare.com")
sys.path.insert(0, str(HERE))

from fastapi.testclient import TestClient  # noqa: E402

import server  # noqa: E402

client = TestClient(server.api)
AUTH = {"Authorization": "Bearer secret"}
SAMPLE = json.dumps({"messages": [{"role": "system", "content": "s"}, {"role": "user", "content": "뭐해"}, {"role": "assistant", "content": "누워있어 ㅋㅋ"}]}, ensure_ascii=False)


def wait_done(job_id: str) -> dict:
    for _ in range(100):
        job = client.get(f"/api/jobs/{job_id}", headers=AUTH).json()
        if job["stage"] in ("done", "error", "cancelled"):
            return job
        time.sleep(0.05)
    raise AssertionError("job did not finish")


def test_health_is_public_and_content_free():
    body = client.get("/api/health").json()
    assert body["ok"] and body["app"] == "maltu-ai-trainer" and set(body) == {"ok", "app", "device", "busy"}


def test_token_required():
    assert client.post("/api/jobs", json={"jsonl": SAMPLE}).status_code == 401
    assert client.post("/api/jobs", json={"jsonl": SAMPLE}, headers={"Authorization": "Bearer wrong"}).status_code == 401


def test_rejects_bad_data():
    assert client.post("/api/jobs", json={"jsonl": "not json"}, headers=AUTH).status_code == 400
    assert client.post("/api/jobs", json={"jsonl": SAMPLE, "base": "llama-70b"}, headers=AUTH).status_code == 400
    no_reply = json.dumps({"messages": [{"role": "system", "content": "s"}, {"role": "user", "content": "x"}]})
    assert client.post("/api/jobs", json={"jsonl": no_reply}, headers=AUTH).status_code == 400


def test_job_lifecycle_deletes_upload_and_serves_model():
    job = client.post("/api/jobs", json={"jsonl": SAMPLE, "base": "qwen2.5-0.5b", "persona": "친구"}, headers=AUTH).json()
    assert job["replies"] == 1 and job["persona"] == "친구"
    done = wait_done(job["id"])
    assert done["stage"] == "done" and done["files"] == [{"name": "persona-q4_k_m.gguf", "size": 64}]
    d = server.JOBS / job["id"]
    assert not (d / "data.jsonl").exists()
    r = client.get(f"/api/jobs/{job['id']}/files/persona-q4_k_m.gguf", headers=AUTH)
    assert r.status_code == 200 and r.content.startswith(b"GGUF")
    assert client.get(f"/api/jobs/{job['id']}/files/persona-q4_k_m.gguf").status_code == 401
    assert client.get(f"/api/jobs/{job['id']}/files/..%2Fjob.json", headers=AUTH).status_code == 404
    assert client.get("/api/jobs/../../token", headers=AUTH).status_code == 404
    # a new job replaces the old one on disk
    second = client.post("/api/jobs", json={"jsonl": SAMPLE}, headers=AUTH).json()
    assert not d.exists()
    wait_done(second["id"])
    assert client.delete(f"/api/jobs/{second['id']}", headers=AUTH).status_code == 200
    assert not (server.JOBS / second["id"]).exists()


def test_one_job_at_a_time_and_cancel():
    os.environ["FAKE_SLEEP"] = "5"
    try:
        job = client.post("/api/jobs", json={"jsonl": SAMPLE}, headers=AUTH).json()
        assert client.post("/api/jobs", json={"jsonl": SAMPLE}, headers=AUTH).status_code == 409
        cancelled = client.post(f"/api/jobs/{job['id']}/cancel", headers=AUTH).json()
        assert cancelled["stage"] == "cancelled"
        assert not (server.JOBS / job["id"] / "data.jsonl").exists()
    finally:
        os.environ["FAKE_SLEEP"] = "0.2"


def test_cors_allows_app_origins_only():
    pre = {"Access-Control-Request-Method": "POST", "Access-Control-Request-Headers": "authorization,content-type"}
    ok = client.options("/api/jobs", headers={"Origin": "https://jshaa.github.io", **pre})
    assert ok.headers.get("access-control-allow-origin") == "https://jshaa.github.io"
    tunnel = client.options("/api/jobs", headers={"Origin": "https://abc-xyz.trycloudflare.com", **pre})
    assert tunnel.headers.get("access-control-allow-origin") == "https://abc-xyz.trycloudflare.com"
    evil = client.options("/api/jobs", headers={"Origin": "https://evil.example", **pre})
    assert "access-control-allow-origin" not in evil.headers


def test_pairing_page_has_qr_link():
    html = TestClient(server.pair).get("/").text
    assert "<svg" in html and "secret" in html and "https://abc.trycloudflare.com" in html


if __name__ == "__main__":
    for name, fn in list(globals().items()):
        if name.startswith("test_"):
            fn()
    print("ok")
