# Synthetic Export Formats v1 (test-only)

These three formats are **invented for TalkPro tests**. They are not, and do not try to be, the private export format of any messenger. They model the *structural* differences that a real adapter must handle: date blocks vs per-line dates, 24h vs Korean 12h clock, different field separators, multiline messages and system lines. Real-format adapters are added only after validation against real exports stored outside the repository (DD-012).

Content rules for all golden data: only synthetic names (`UserA`, `User B`, `합성사용자 C`, `SyntheticPerson01`, …) and synthetic text (`합성 테스트 메시지 001`, …). No real names, phone numbers, e-mail addresses or conversations.

## Common rules
| Rule | Definition |
|---|---|
| Header | Lines from line 1 until the first blank line: signature, `Room: …`, `Saved: …`. Header lines are metadata and are not stored. |
| Signature | Line 1: `TalkPro Synthetic Export (W1)` / `(A1)` / `(I1)`. Another known family or version on line 1 makes that line `Unsupported`. |
| Record line | A message or system line that matches the format's full record pattern. |
| Reserved prefix | Lines starting with the record prefix (see each format) are records. If they do not match fully, they are `Malformed` (`IncompleteRecord` / `InvalidTimestamp` / …). A continuation line therefore never starts with a reserved prefix. |
| Continuation | Any other line while a record is open is appended to the message with `\n`. Trailing empty continuation lines are dropped when the message completes. |
| Blank line, nothing open | `Ignored`. |
| Other line, nothing open | `Malformed` (`OrphanLine`). |
| Malformed record | Completes (closes) the open message. Following non-record lines are orphans. |
| Message text | Kept verbatim (leading/trailing spaces, tabs and control characters other than NUL preserved). Empty text is allowed. |
| Speaker name | 1–128 characters, not whitespace-only. Longer → `SpeakerNameTooLong`. `*` is reserved for system lines. |
| Timestamps | Local time, assumed time zone from `ParseContext`. Valid range 1990-01-01 … 2100-12-31, otherwise `TimestampOutOfRange`. Order in the file is preserved (no sorting, no de-duplication). |
| Driver guards | Line longer than 100,000 chars → `LineTooLong`. Line containing U+0000 → `NullCharacter`. Both close the open message. |
| System text | `{name} 님이 들어왔습니다.` → Join, `{name} 님이 나갔습니다.` → Leave, anything else → Other. |

## W1 — "Windows-like" (date blocks, 24h)
```
TalkPro Synthetic Export (W1)
Room: 합성 테스트방 01
Saved: 2026-10-02 14:23:11

=== 2026-10-01 (Thu) ===
[UserA] [15:15] 합성 테스트 메시지 001
[User B] [15:16] 첫 줄
둘째 줄
* [15:17] 합성사용자 C 님이 들어왔습니다.
```
- Date marker: `=== yyyy-MM-dd (Ddd) ===` sets the date context (`Metadata`).
- Message: `[name] [HH:mm] text`. The name must not contain `]`.
- System: `* [HH:mm] text`.
- Reserved prefixes: `[`, `* [`, `=== `.
- A message before any date marker → `Malformed` (`MissingDateContext`).

## A1 — "Android-like" (per-line date, Korean 12h, pipe fields)
```
TalkPro Synthetic Export (A1)
Room: 합성 테스트방 01
Saved: 2026-10-02 오후 2:23

2026-10-01 오후 3:15 | UserA | 합성 테스트 메시지 001
2026-10-01 오후 3:17 * 합성사용자 C 님이 들어왔습니다.
```
- Message: `yyyy-MM-dd (오전|오후) h:mm | name | text`. The name ends at the first ` | `; everything after the second separator is text, including extra ` | ` fields.
- `오전 12:xx` = 00:xx, `오후 12:xx` = 12:xx. An hour outside 1–12 → `InvalidTimestamp`.
- System: `yyyy-MM-dd (오전|오후) h:mm * text`.
- Reserved prefix: `^\d{4}-\d{2}-\d{2} `.

## I1 — "iOS-like" (dotted date, seconds, tab fields)
```
TalkPro Synthetic Export (I1)
Room: 합성 테스트방 01
Saved: 2026.10.02 14:23:11

2026.10.01 15:15:07<TAB>UserA<TAB>합성 테스트 메시지 001
2026.10.01 15:17:00<TAB>*<TAB>합성사용자 C 님이 들어왔습니다.
```
- Message: `yyyy.MM.dd HH:mm:ss\tname\ttext`. Extra tabs belong to the text.
- Speaker `*` = system line.
- Reserved prefix: `^\d{4}\.\d{2}\.\d{2} `.

## Encodings and the CP949 policy
- Every case is generated in UTF-8 (no BOM). Cases marked CP949-safe are also generated in UTF-8 with BOM and CP949.
- **CP949 policy**: a case is generated in CP949 only if every character is representable. Generation uses an exception fallback, so a non-representable character (emoji, supplementary characters, most non-Korean scripts) **fails generation loudly**. It is never replaced with `?`.
- Cases with such characters (`unicode`) exist only in UTF-8 / UTF-8 BOM.
