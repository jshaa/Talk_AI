"""Label masking for persona_train.py (no torch needed)."""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
import persona_train as pt  # noqa: E402


def fake_tokenize(text: str) -> list[int]:
    # special tokens -> one id, everything else -> one id per character
    out, i = [], 0
    while i < len(text):
        for tok, tid in ((pt.IM_START, 1), (pt.IM_END, 2)):
            if text.startswith(tok, i):
                out.append(tid)
                i += len(tok)
                break
        else:
            out.append(100 + ord(text[i]) % 1000)
            i += 1
    return out


def test_only_assistant_content_and_end_token_are_trained():
    msgs = [{"role": "system", "content": "S"}, {"role": "user", "content": "hi"}, {"role": "assistant", "content": "yo"}]
    enc = pt.encode_sample(fake_tokenize, msgs, 100)
    trained = [t for t, l in zip(enc["input_ids"], enc["labels"]) if l != -100]
    assert trained == fake_tokenize("yo") + [2]
    assert len(enc["input_ids"]) == len(enc["labels"]) == len(enc["attention_mask"])


def test_too_long_or_reply_less_samples_are_dropped():
    msgs = [{"role": "system", "content": "S"}, {"role": "user", "content": "x" * 50}, {"role": "assistant", "content": "y"}]
    assert pt.encode_sample(fake_tokenize, msgs, 20) is None
    assert pt.encode_sample(fake_tokenize, msgs[:2], 200) is None


def test_long_samples_keep_latest_turns_from_a_user_turn():
    s = {"role": "system", "content": "S"}
    msgs = [s, {"role": "user", "content": "x" * 50}, {"role": "assistant", "content": "a" * 50},
            {"role": "user", "content": "q"}, {"role": "assistant", "content": "r"}]
    enc = pt.encode_sample(fake_tokenize, msgs, 30)
    full = pt.encode_sample(fake_tokenize, [s, *msgs[3:]], 30)
    assert enc == full  # oldest pair dropped, no partial turn, still starts with the user


def test_overlapping_window_replies_are_context_only():
    s = {"role": "system", "content": "S"}
    u = [{"role": "user", "content": f"u{i}"} for i in range(4)]
    a = [{"role": "assistant", "content": f"a{i}"} for i in range(4)]
    prev = [s, u[0], a[0], u[1], a[1], u[2], a[2]]
    cur = [s, u[1], a[1], u[2], a[2], u[3], a[3]]
    assert pt.overlap_turns(prev, cur) == 4
    assert pt.overlap_turns(prev, [s, u[3], a[3]]) == 0
    enc = pt.encode_sample(fake_tokenize, cur, 1000, 4)
    trained = [t for t, l in zip(enc["input_ids"], enc["labels"]) if l != -100]
    assert trained == fake_tokenize("a3") + [2]
    assert pt.encode_sample(fake_tokenize, cur[:5], 1000, 4) is None  # nothing new to learn


def test_fixture_windows_train_every_reply_once():
    path = Path(__file__).parents[1] / "fixtures" / "expected-long.jsonl"
    samples = pt.read_samples(path)
    contexts = [0] + [pt.overlap_turns(a, b) for a, b in zip(samples, samples[1:])]
    trained = [m["content"] for s, c in zip(samples, contexts) for m in [m for m in s if m["role"] != "system"][c:] if m["role"] == "assistant"]
    every = {m["content"] for s in samples for m in s if m["role"] == "assistant"}
    assert len(trained) == len(set(trained)) == len(every)


def test_interjection_only_replies_are_mostly_context():
    assert all(pt.is_backchannel(t) for t in ["어어엉", "우어어엉", "ㅋㅋㅋㅋ", "웅!", "ㅠㅠ", "우하하하하", "응"])
    assert not any(pt.is_backchannel(t) for t in ["안먹엇어", "피자먹으러가자~", "운동가까아", "ㅋㅋㅋ\n개쩐당", ""])
    s = {"role": "system", "content": "S"}
    samples = [[s, {"role": "user", "content": f"q{i}"}, {"role": "assistant", "content": "어어엉"},
                {"role": "user", "content": "뭐해"}, {"role": "assistant", "content": "운동가까아"}] for i in range(10)]
    skips = pt.backchannel_context(samples, [0] * 10, keep=3)
    assert sum(1 for k in skips if k == frozenset()) == 3 and all(k <= {1} for k in skips)  # 3 of 10 trained
    enc = pt.encode_sample(fake_tokenize, samples[5], 1000, 0, skips[5])
    trained = [t for t, l in zip(enc["input_ids"], enc["labels"]) if l != -100]
    assert trained == fake_tokenize("운동가까아") + [2]


def test_pc_models_see_an_untrained_reply_tag():
    assert pt.reply_tag("응\n뭐해?") == "[2줄 질문]" and pt.reply_tag("ㅋㅋ") == "[1줄]" and pt.reply_tag("\n".join("a" * 9)) == "[6줄]"
    msgs = [{"role": "system", "content": "S"}, {"role": "user", "content": "hi"}, {"role": "assistant", "content": "응\n뭐해?"}]
    plain = pt.encode_sample(fake_tokenize, msgs, 1000)
    tagged = pt.encode_sample(fake_tokenize, msgs, 1000, tags=True)
    trained = lambda e: [t for t, l in zip(e["input_ids"], e["labels"]) if l != -100]  # noqa: E731
    assert trained(tagged) == trained(plain)  # the tag is context only
    assert len(tagged["input_ids"]) == len(plain["input_ids"]) + len(fake_tokenize("[2줄 질문]\n"))


def test_auto_epochs():
    assert [pt.auto_epochs(n) for n in (100, 2000, 9000)] == [2, 2, 1]


if __name__ == "__main__":
    for name, fn in list(globals().items()):
        if name.startswith("test_"):
            fn()
    print("ok")
