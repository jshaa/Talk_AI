# Manual UI check (Debug build)

Automated coverage: ViewModel unit tests, real-component integration tests (parse → review → encrypted save → delete) and a UI-Automation smoke run up to the file dialog. The Windows 11 file dialog itself is checked by hand:

1. Open the solution in Visual Studio, set **TalkPro.App** as startup project, select **Debug**, press F5.
2. **가져오기** → read the attestation → tick **위 내용을 확인했으며 사실입니다.** → **다음**.
3. **파일 선택...** → pick a synthetic sample, e.g. `tests/TalkPro.Ingestion.Tests/Golden/w1/conversation.utf8.txt`. The CP949 and Android/iOS variants (`a1/`, `i1/`) work too.
4. Expect: summary line (format, encoding, 23 messages for `conversation`), participants without an owner, and "본인(나)을 선택해 주세요."
5. Click a row to see local message samples. Try **병합**, **분리** (pick a date) and **제외**. "본인은 제외할 수 없습니다." appears if you exclude the owner.
6. **본인으로 지정** on `UserA` → **다음: 개인정보 검토**.
7. Step 4 shows the detected types with bulk decisions, the per-finding list (excerpt marked with 「」), and the original vs "AI가 받게 될 내용" preview. Check that a resident/card/account number never offers **유지** and that sensitive topics default to **분석 제외**.
8. **암호화하여 저장** → you return to **데이터셋** with the success message.
8a. Select the new row → **선택한 데이터셋 분석** (disabled for damaged rows). Expect the notice "상대방의 감정·심리·애정도는 분석하지 않습니다", then overview, 내 말투, 상대별 내 말투, hour/weekday bars, keywords, monthly bars, busiest days and quarterly rows. Excluded (sensitive) messages are not counted and masked names never appear as keywords. Leaving the page discards the result.
8b. **로컬 AI**: the page checks Ollama immediately. With Ollama running, expect "Ollama 0.x 실행 중", the registry models with their license, approval and state, and `ollama pull …` commands. **연결 테스트** shows a short answer with the "AI 생성 · model · seconds" label. With Ollama stopped, expect "Ollama가 실행 중이 아닙니다" and a disabled test button. (Developers: `dotnet test --project tests/TalkPro.Llm.Tests -- --explicit only` runs the same check headless.)
8c. **답장 초안** (needs `qwen3:8b` in Ollama): pick the dataset and optionally a counterpart, paste a message, **초안 만들기**. Expect three cards labelled **수락**, **보류**, **되묻기** (in that order), the "AI 생성 초안 · qwen3:8b · 내 답장 예시 N개 참고" label, the style summary and a "말투 일치도" per card. The button now reads **전체 다시 생성** (it always uses the 수락·보류·되묻기 spread).
    - On one card change **방향** (e.g. 보류 → 되묻기): a hint asks to regenerate; press **이 초안만 다시 생성** — only that card changes, the other two keep their text.
    - Set all three cards to the same direction: "방향이 모두 같습니다…" appears (still allowed).
    - A card that could not be written shows "이 방향의 초안을 만들지 못했습니다…" and has no usable **복사**.
    - **복사** puts the draft on the clipboard; after 60 s it is cleared, and it does not appear in Windows clipboard history (Win+V). There is no send button anywhere.
    - Developers: `dotnet test --project tests/TalkPro.Presentation.Tests -- --explicit only` runs the Korean evaluation set; the text-free report is written to `tests/TalkPro.Presentation.Tests/bin/Debug/net10.0/eval-report.json`.
8d. **기억 검색** (needs `bge-m3` in Ollama for meaning search): pick the dataset. The status says no index yet and keyword-only search works immediately — search a word from the chat and expect cards with the date, "실제 기록" badge, times, speakers and the stored text. Press **기억 색인 만들기** (progress bar), then search with different words of the same meaning (e.g. "바다 여행" for a chat about 강릉): "검색 방식: 뜻 + 단어". An unrelated query shows "기록에서 찾지 못했습니다.". Excluded messages never appear; resident/card/account numbers show as [주민등록번호] etc.
9. Check `%LOCALAPPDATA%\TalkPro\vault\{id}` contains only `data.snapshot`, `dataset.meta` and, after 8d, `memory.index`, and `keys\{id}.key` exists.
10. **모든 데이터 삭제** → **예** → the list is empty, and the key and vault folders are gone.

Release builds show "지원하는 내보내기 형식이 아닙니다" for every file until real-format adapters exist (DD-019).
