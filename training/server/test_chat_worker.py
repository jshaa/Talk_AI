"""Reply selection in chat_worker.py (no torch needed)."""
import random
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


def test_tags_steer_length_and_questions():
    msgs = [S, {"role": "user", "content": "ㅋㅋ"}, {"role": "assistant", "content": "응\n뭐해?"}, {"role": "user", "content": "밥먹어"}]
    prompt = cw.chatml(msgs, "[2줄 질문]")
    assert "<|im_start|>assistant\n[2줄 질문]\n응\n뭐해?<|im_end|>" in prompt  # earlier reply tagged as in training
    assert prompt.endswith("<|im_start|>assistant\n[2줄 질문]\n")
    # asks back more often after replies without a question, rarely right after being asked
    quiet = [S] + [{"role": r, "content": "응"} for r in ("user", "assistant") * 3] + [{"role": "user", "content": "피곤해"}]
    asked = [S, {"role": "user", "content": "뭐해?"}]
    rate = lambda m: sum("질문" in cw.choose_tag(m, random.Random(i)) for i in range(400)) / 400  # noqa: E731
    assert rate(quiet) > 0.5 > 0.25 > rate(asked)
    # the candidate that follows the tag wins; a stack of laughs loses
    assert cw.pick(["집이야", "집이야\n너는 뭐해?"], asked[:1] + [{"role": "user", "content": "ㅎㅎ"}], "[2줄 질문]") == "집이야\n너는 뭐해?"
    assert cw.pick(["ㅋㅋㅋ\n캬캬캬컄", "ㅋㅋㅋ\n개웃기넼"], [S, {"role": "user", "content": "ㅎㅎ"}], "[2줄]") == "ㅋㅋㅋ\n개웃기넼"


def test_respond_builds_the_tagged_prompt_and_picks_a_candidate():
    seen = {}

    def generate(prompt, n, temperature, max_new):
        seen.update(prompt=prompt, n=n, temperature=temperature)
        return ["<think></think>ㅋㅋㅋ\n캬캬캬", "응 집이야<|im_end|>", "응 집이야\n너는?"]

    msgs = [S, {"role": "user", "content": "ㅎㅎ"}]
    reply = cw.respond(msgs, 0.7, generate, len)
    assert seen["n"] == cw.CANDIDATES and seen["temperature"] == 0.7
    assert seen["prompt"].startswith("<|im_start|>system\ns<|im_end|>") and "<|im_start|>assistant\n[" in seen["prompt"]
    assert reply in ("응 집이야", "응 집이야\n너는?")


def test_clean_keeps_only_the_reply():
    assert cw.clean("안녕~\n</think>\n[1줄]\n오늘은 좀") == "안녕~"
    assert cw.clean("<think>음</think>집이야\n[2줄 질문]\n뭐해") == "집이야"
    assert cw.clean("<think>생각만 하다 끝남") == ""
    assert cw.clean("응응\n밥 먹었어?<|im_end|>") == "응응\n밥 먹었어?"
    assert cw.clean("[1] 이건 그대로") == "[1] 이건 그대로"


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
