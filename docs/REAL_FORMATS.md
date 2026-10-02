# Real Messenger Export Formats (Korean UI) — Phase 1.5

Adapters: `TalkPro.Ingestion.RealFormats` — `WindowsChatFormatAdapter` (`kakaotalk.windows.ko.v1`), `AndroidChatFormatAdapter` (`kakaotalk.android.ko.v1`), `IosChatFormatAdapter` (`kakaotalk.ios.ko.v1`). The format ids are internal identifiers, not product branding.

> **Status: implemented from public structural evidence, not yet validated against a real export.**
> No real export was available while this was built. Every grammar below comes from public, anonymised third-party sources (listed at the end). Each variant shows its evidence level. Until the local validation test (below) has passed on real files, the adapters are **not registered in the production container** (DD-012, DD-014).

These layouts are separate from the synthetic W1/A1/I1 formats (`SYNTHETIC_FORMATS.md`), which exist only to test parser infrastructure. No rule here was taken from the synthetic formats.

## Common rules (all three layouts)
| Rule | Definition |
|---|---|
| Header | Line 1 ends with ` 카카오톡 대화` (room title or `{name} 님과 카카오톡 대화`). Line 2 starts with `저장한 날짜 : ` (the date format after it varies and is not parsed). The header runs until the first blank line or the first record. Header lines are `Metadata` and are not stored. A file whose line 1 is not a title has no header. |
| Records | A line is a record **only if it matches a complete record pattern**. There are no reserved prefixes: real message text may start with `[`, digits or a date. |
| Continuation | Every other line is appended to the open message with `\n`, including blank lines. Trailing blank lines are dropped when the message completes. With no message open, a blank line is `Ignored` and any other line is `Malformed` (`OrphanLine`). |
| Malformed | A complete-shape record with impossible values: hour outside 1–12 (12-hour) or 0–23 (24-hour), minute > 59, impossible date → `InvalidTimestamp`. Outside 1990–2100 → `TimestampOutOfRange`. Time-only record without a date header → `MissingDateContext`. A malformed record completes the open message. |
| Sender | Kept verbatim (spaces, punctuation, emoji). Empty sender → system line. Whitespace-only sender → `InvalidSpeakerName`. More than 128 UTF-16 units → `SpeakerNameTooLong`. Identical labels map to one participant. There is no identity resolution (Phase 1.6). |
| Clock | `오전 12:xx` = 00:xx, `오후 12:xx` = 12:xx, `오후 h:mm` = h+12. Minutes always have two digits. Time zone: the one in `ParseContext` (exports carry none). |
| Text | Kept verbatim (no trimming, control characters other than NUL kept). The driver guards against NUL and lines over 100,000 characters. Encoding (BOM → strict UTF-8 → strict CP949) and LF/CRLF splitting are done by the driver, not the adapters. |
| System events | Only system lines are classified. Text ending in `님이 나갔습니다.` → `Leave`, `님을 초대하였습니다.` → `Invite`, anything else → `Other`. These are the only messenger-generated endings seen in the sources. |
| Media / deleted | **Not classified.** Placeholders such as `사진` or `삭제된 메시지입니다.` appear as ordinary message text and cannot be told apart from typed text. They stay `MessageKind.Text`. Classification belongs to Phase 1.6. |

## Windows PC (`kakaotalk.windows.ko.v1`)
```
{title} 님과 카카오톡 대화
저장한 날짜 : 2026-10-02 14:23:11            (also: 2026년 10월 2일 오후 2:23)

--------------- 2026년 10월 1일 목요일 ---------------
[UserA] [오후 3:15] 합성 테스트 메시지 001
[User B] [15:16] 24-hour variant
이어지는 줄
[] [오후 3:17] 합성 시스템 알림
```
| Construct | Grammar | Evidence |
|---|---|---|
| Date separator | `^-{15} yyyy년 M월 d일 X요일 -{15}$` (weekday not validated). Sets the date context. An invalid date is `Malformed` and clears the context. | 2 sources + report |
| Message, 12-hour | `^[sender] [오전\|오후 h:mm] text` | 2 sources + report |
| Message, 24-hour | `^[sender] [H:mm] text` | 1 source |
| Sender delimiter | The sender ends at the **first** `] [` that is followed by a complete time and `]`, so text containing `[x] [오후 1:00]` stays text. | — |
| Empty text | `[sender] [오후 3:15]` with nothing after it | Not observed; accepted defensively |
| System line | `[] [time] text` (empty sender) | 1 source |
| Untimed notices | Lines like `…님을 초대하였습니다.` / `…님이 나갔습니다.` have no timestamp and cannot be distinguished from message text. They are continuation lines (or `OrphanLine` when no message is open). **Known limitation.** | 1 source |

## Android (`kakaotalk.android.ko.v1`)
```
{title} 카카오톡 대화
저장한 날짜 : 2026년 10월 2일 오후 2:23


2026년 10월 1일 오후 3:15
2026년 10월 1일 오후 3:15, UserA : 합성 테스트 메시지 001
2026년 10월 1일 오후 3:16, User B님이 나갔습니다.
2026년 10월 1일 오후 3:17,  : 합성 시스템 알림
```
| Construct | Grammar | Evidence |
|---|---|---|
| Record prefix | `^yyyy년 M월 d일 오전\|오후 h:mm` (12-hour only) | 2 sources + report |
| Date line | Prefix alone on the line → `Metadata` (sets the date context) | 1 source |
| Message | Prefix + `, sender : text`. The sender ends at the **first** ` : `, so a colon in the text is kept. | 2 sources + report |
| Empty text | `, sender : ` or `, sender :` | Not observed; accepted defensively |
| System line | Prefix + `, text` without ` : `, or an empty sender (`,  : text`) | 1 source |
| Timestamp-like text | The prefix followed by anything other than `, ` or end of line (e.g. `…오후 3:17 이후 일정`) is text | — |

## iOS (`kakaotalk.ios.ko.v1`)
```
{title} 님과 카카오톡 대화
저장한 날짜 : 2026. 10. 2. 오후 2:23        (also: 2026년 10월 2일 오후 2:23)


2026년 10월 1일 목요일
2026. 10. 1. 오후 3:15, UserA : 합성 테스트 메시지 001      (dotted-date layout)
오후 3:16, User B : 합성 테스트 메시지 002                  (time-only layout)
오후 3:17, User B님이 나갔습니다.
```
| Construct | Grammar | Evidence |
|---|---|---|
| Date header | `^yyyy년 M월 d일 X요일$` → `Metadata`, sets the date context. An invalid date is `Malformed` and clears it. | 1 source |
| Message, dotted date | `^yyyy. M. d. 오전\|오후 h:mm, sender : text` (one space after each dot) | 1 source + report |
| Message, time only | `^오전\|오후 h:mm, sender : text`. The date comes from the last date header (otherwise `MissingDateContext`). | 1 source |
| System line | A record without ` : ` or with an empty sender | 1 source (time-only). **Inferred** for the dotted layout. |

## Detection
- Content only: never the file name, extension or path.
- A real adapter scores **0** unless line 1 is a title (` 카카오톡 대화`) **and** line 2 starts with `저장한 날짜 : `.
- With that header, the score is **0.6 + 0.4 × (complete records of this layout / non-blank body lines)**. With no record of the layout the score is **0.3**, below the 0.5 threshold, so a header-only or truncated file is `UnknownFormat`.
- Record shapes are mutually exclusive across the three layouts (`[…] [time]` / `yyyy년 … 오후 h:mm` / `yyyy년 … X요일`, `yyyy. M. d.` and `오전|오후 h:mm,`). The layout with the most matching lines wins; a tie is rejected.
- The synthetic adapters need either their own signature or a first line that is one of their records, and W1 accepts only `HH:mm` times. So no synthetic adapter claims a real export, and no real adapter claims a synthetic file (tested on all 163 golden files).

## Known unsupported variants
| Variant | Behaviour |
|---|---|
| English (or other non-Korean) UI: `Date Saved :`, `Thursday, October 1, 2026`, `Oct 1, 2026 at 15:15, …`, `[Name] [3:15 PM]` | `UnknownFormat` (no Korean header) |
| macOS export, including the CSV `Date,User,Message` layout | `UnknownFormat` |
| Android 24-hour clock | Not observed. Lines would be text, so the file is `UnknownFormat`. |
| iOS dotted layout without the space after the dots (`2026.10.1.`) | Not supported (1 source suggests it may exist). Lines would be text. |
| Sender labels containing ` : ` (mobile) | Split at the first ` : ` (wrong sender). Phase 1.6 two-pass re-split. |
| Records cut inside the timestamp (truncated last line) | No record shape, so kept as text of the previous message |
| Windows untimed invite/leave notices | Continuation text (see above) |
| Media, emoticon and deleted-message placeholders | Plain text |

Encodings: the importer accepts UTF-8, UTF-8 with BOM and CP949 for every layout (tested). This is about what TalkPro can read. It is **not** a claim about which encodings each messenger version writes; that has not been verified.

## Fixture and privacy rules
- Real exports are **never** committed, copied into tests, or put into `Golden/` (the folder's exact-contents test would fail).
- Fixtures are hand-written string literals in `tests/TalkPro.Ingestion.Tests/RealFormats/RealFormatFixtures.cs`. They follow the grammar above and use only synthetic names (`UserA`, `User B`, `합성사용자 C`, `Synth#Person (테스트)`, `Synthetic🙂User`) and synthetic text. Their expected outcomes are written by hand per line, never produced by parser code. They are not `.txt` files, so nothing export-like sits outside `Golden/`.
- When a real export reveals a new construct, write a new synthetic line with the same **structure**. Never copy its names, text, numbers, URLs or room title.

## Local real-export validation (opt-in, private)
1. Keep real exports in a folder **outside** the repository (ideally an encrypted folder). Do not add a `.gitignore` entry for it.
2. Run:
   ```
   set TALKPRO_REAL_EXPORTS_DIR=D:\private\exports
   dotnet test --project tests/TalkPro.Ingestion.Tests -- --filter-trait "Category=LocalPrivateValidation"
   ```
3. Every top-level `*.txt` file is read in place (never copied) and imported with the real adapters only. The test fails unless every file is `Success` with **zero** malformed/unsupported lines.
4. The output contains only the file ordinal, a SHA-256 prefix, status, format id, encoding, counts, and line number + outcome + issue for each problem. It never contains the path, file name, room title, sender labels or text. Unreadable files are reported by exception type only. A folder inside the repository is refused.
5. Without the variable the test is **skipped**, so normal builds and CI never need private data.
6. Once all target exports pass, the adapters may be registered in `AddTalkProIngestion` (follow-up task). The `CompositionTests` assertion that no adapter is registered is then changed deliberately in the same change.

## Sources (public, anonymised; structure only, no content copied)
- zeikar/kakaotalk-viewer (MIT): `src/parser/{windows,android,ios}.ts` and their tests — https://github.com/zeikar/kakaotalk-viewer
- uoneway/kakaotalk_msg_preprocessor: Windows/Android Korean patterns — https://github.com/uoneway/kakaotalk_msg_preprocessor
- `docs/Persona_AI_상용화_검토보고서.md` §6.1 (observed-example table; itself marked "to be validated with real samples")
