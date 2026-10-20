"""Reply selection in chat_worker.py (no torch needed)."""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
import chat_worker as cw  # noqa: E402

S = {"role": "system", "content": "s"}


def test_avoids_repeating_and_echoing():
    msgs = [S, {"role": "user", "content": "우캬캬캬캬"}, {"role": "assistant", "content": "운동가까아"}, {"role": "user", "content": "뭐해?"}]
    assert cw.pick(["운동가까아", "우캬캬캬캬", "집에서 누워있어"], msgs) == "집에서 누워있어"


def test_question_is_not_answered_with_a_bare_interjection():
    msgs = [S, {"role": "user", "content": "너 집 어디야?"}]
    assert cw.pick(["어어엉", "ㅋㅋㅋㅋ", "나 지금 합정이지"], msgs) == "나 지금 합정이지"
    # without a question an interjection is a fine reply, and the first candidate wins ties
    assert cw.pick(["어어엉", "응응"], [S, {"role": "user", "content": "ㅋㅋㅋ"}]) == "어어엉"


def test_empty_candidates_lose_and_fit_drops_oldest_turns():
    assert cw.pick(["", "웅"], [S, {"role": "user", "content": "ㅎㅎ"}]) == "웅"
    msgs = [S] + [{"role": r, "content": "x" * 10} for r in ("user", "assistant") * 5]
    assert cw.fit(msgs, len, len(cw.chatml([S] + msgs[-4:]))) == [S] + msgs[-4:]
    assert cw.fit(msgs, len, len(cw.chatml([S] + msgs[-3:]))) == [S] + msgs[-2:]  # never starts on a reply


if __name__ == "__main__":
    for name, fn in list(globals().items()):
        if name.startswith("test_"):
            fn()
    print("ok")
