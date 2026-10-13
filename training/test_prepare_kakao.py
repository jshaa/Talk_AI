"""Parity with the app: python -m pytest training (or python training/test_prepare_kakao.py)."""
from pathlib import Path

import prepare_kakao as pk

FIXTURES = Path(__file__).with_name("fixtures")


def test_fixtures_match_expected_jsonl():
    expected = (FIXTURES / "expected.jsonl").read_text(encoding="utf-8")
    for name, fmt in [("pc.txt", "pc"), ("android.txt", "mobile"), ("ios.txt", "mobile"), ("mac.csv", "csv")]:
        got_fmt, messages, skipped = pk.parse_export((FIXTURES / name).read_text(encoding="utf-8"))
        assert (got_fmt, len(messages), skipped) == (fmt, 13, 0), name
        assert pk.to_jsonl(pk.build_samples(messages, "합성B", "가상친구")) == expected, name


def test_masking_matches_app_rules():
    assert pk.mask_pii("번호는01012345678야") == "번호는[전화번호]야"
    assert pk.mask_pii("2026-10-13 3-2 승") == "2026-10-13 3-2 승"
    assert pk.mask_pii("900101-1234567") == "[주민번호]"


def test_impossible_date_keeps_message_without_time():
    _, messages, _ = pk.parse_export("--------------- 2026년 2월 30일 월요일 ---------------\n[A] [오후 3:00] x")
    assert [(m.time, m.text) for m in messages] == [(None, "x")]


if __name__ == "__main__":
    for fn in [v for k, v in dict(globals()).items() if k.startswith("test_")]:
        fn()
    print("ok")
