# Synthetic Export Formats v1 (test-only)

These three formats are **invented for TalkPro tests**. They are not, and do not try to be, the private export format of any messenger. They model the *structural* differences that a real adapter must handle: date blocks vs per-line dates, 24h vs Korean 12h clock, different field separators, multiline messages and system lines. Real-format adapters are added only after validation against real exports stored outside the repository (DD-012).

Content rules for all golden data: only synthetic names (`UserA`, `User B`, `합성사용자 C`, `SyntheticPerson01`, …) and synthetic text (`합성 테스트 메시지 001`, …). No real names, phone numbers, e-mail addresses or conversations.

## Common rules
| Rule | Definition |
|---|---|
| Lines | LF and CRLF end a line. A lone CR is content. A final terminator does not start another line, so a 0-byte (or BOM-only) file has no lines and is `EmptyInput`. |
| Header | Exists only if line 1 is a signature. It runs until the first blank line (`Ignored`) or the first line with a reserved prefix. Header lines (`Room: …`, `Saved: …`, unknown extra fields) are `Metadata` and are not stored. |
| Signature | Line 1: `TalkPro Synthetic Export (W1)` / `(A1)` / `(I1)`. Any other `TalkPro Synthetic Export (…)` line 1 (another family or version) is `Unsupported` (`UnsupportedVersion`). The body is still parsed. |
| Blank line | Empty or whitespace-only (`string.IsNullOrWhiteSpace`). |
| Record line | A message or system line that matches the format's full record pattern. |
| Reserved prefix | Lines starting with the record prefix (see each format) are records. If they do not match fully, they are `Malformed` (`IncompleteRecord` / `InvalidTimestamp` / …). A continuation line therefore never starts with a reserved prefix. |
| Continuation | Any other line while a record is open is appended to the message with `\n`, including blank lines. Trailing blank continuation lines are dropped when the message completes. |
| Blank line, nothing open | `Ignored`. |
| Other line, nothing open | `Malformed` (`OrphanLine`). |
| Malformed record | Completes (closes) the open message. Following non-record lines are orphans. |
| Message text | Kept verbatim (leading/trailing spaces, tabs and control characters other than NUL preserved). Empty text is allowed. |
| Speaker name | 1–128 UTF-16 code units, not whitespace-only, not `*` → otherwise `InvalidSpeakerName`. Longer → `SpeakerNameTooLong`. `*` is reserved for system lines. |
| Timestamps | Local time, assumed time zone from `ParseContext`. Digits are ASCII `[0-9]` only. Impossible values (month 13, Feb 30, year 0, 25:61) → `InvalidTimestamp`. Valid range 1990-01-01 … 2100-12-31, otherwise `TimestampOutOfRange`. Order in the file is preserved (no sorting, no de-duplication). |
| Precedence | Within one record: syntax (`IncompleteRecord`) → timestamp → date context (W1) → speaker. |
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
- An invalid date marker is `Malformed` and **clears** the date context, so later messages are never attributed to a wrong date.
- The weekday in the marker is not validated.

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
- Empty text: `… | name | ` (the separator space may be missing).
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

## Format detection
Detection uses content only, never the file name. The first 50 lines are scored; lines the driver would reject are blanked.
- Exact signature on line 1 → 1.0.
- Otherwise, if line 1 is another synthetic signature, the header is skipped. Score = 0.5 + 0.4 × (complete records / non-blank body lines), or 0 if there is no record.
- Without a synthetic signature, the first non-blank line must itself be a complete record, otherwise the score is 0. A W1 record needs an `HH:mm` time. This keeps synthetic adapters from claiming real exports (`REAL_FORMATS.md`).
- Reserved prefixes are mutually exclusive across W1/A1/I1, so a body line scores for at most one format.
- The best score wins if it is ≥ 0.5 and not tied.

## Golden data
`tools/SampleGenerator` writes `tests/TalkPro.Ingestion.Tests/Golden/{w1|a1|i1}.{case}.{utf8|utf8bom|cp949}.txt` and `manifest.json` (expected statistics, issues and entries per sample). Regenerate with:
```
dotnet run --project tools/SampleGenerator -- --out tests/TalkPro.Ingestion.Tests/Golden
```
Cases: `empty`, `header-only`, `single-message`, `single-participant`, `multi-participant` (seeded, crosses midnight), `multiline`, `line-ending-lf`, `no-final-newline`, `text-variety`, `unicode`, `timestamps`, `system-messages`, `names`, `malformed`, `extra-fields`, `truncated-last-record`, `blank-lines`, `no-header`, `unsupported-version`, `very-long-line`, `missing-date-context` (W1 only).

## Encodings and the CP949 policy
- Every case is generated in UTF-8 (no BOM). Most cases are also generated in UTF-8 with BOM. Cases whose text CP949 can represent are also generated in CP949. All three formats have all three encodings.
- **CP949 policy**: a case is generated in CP949 only if every character is representable. Generation uses an exception fallback, so a non-representable character (emoji, supplementary characters, most non-Korean scripts) **fails generation loudly**. It is never replaced with `?`.
- Cases with such characters (`unicode`) exist only in UTF-8 / UTF-8 BOM.
- A CP949 file must not also be valid UTF-8, because detection tries UTF-8 first. Generation fails otherwise, so every CP949 sample contains Korean text.
- `very-long-line` and `unsupported-version` (and W1-only `missing-date-context`) are UTF-8 only, to keep the repository small.
