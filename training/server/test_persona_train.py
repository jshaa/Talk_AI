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


def test_auto_epochs():
    assert [pt.auto_epochs(n) for n in (100, 2000, 9000)] == [2, 2, 1]


if __name__ == "__main__":
    for name, fn in list(globals().items()):
        if name.startswith("test_"):
            fn()
    print("ok")
