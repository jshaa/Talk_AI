# Design Decisions & Issues

## Decisions taken (within agent authority)

**DD-001 Solution root = repository root (`D:\Talk_pro`).** The report's `TalkPro/` folder maps to the repo root so that `docs/` keeps its location.

**DD-002 Extra test project `TalkPro.Architecture.Tests`.** Dependency direction (Core ← modules, no cross-module references, network APIs only in `TalkPro.Llm`) is enforced by tests instead of convention. Verified by mutation: adding `HttpClient` to Privacy makes the test fail.

**DD-003 xunit.v3 on Microsoft.Testing.Platform.** xunit.v3 4.x no longer supports VSTest under the .NET 10 SDK; MTP is enabled in `global.json`. Run tests with `dotnet test --solution TalkPro.sln`.

**DD-004 Repository `nuget.config` clears machine sources.** The developer machine has a broken global source (local Cognex folder) and an implicit multi-source restore is a supply-chain risk. Only nuget.org is used, with package source mapping.

**DD-005 Logging = content-free provider only.**
- The host is created with `DisableDefaults` (no environment-variable/appsettings configuration, no console/debug/event-log/event-source loggers).
- The logger never calls the caller's formatter.
- It renders only primitives, enums, times and `ILogSafe` values; strings are redacted.
- Exceptions are written as type, HResult and stack trace, without `Message` or `Data`.
- Scopes are dropped.
- CA2254, CA1848 and CA1873 build errors force static `LoggerMessage` templates (verified by mutation).

**DD-006 MVP1 consent states.** Report ch. 9 defines a richer manifest (None/Pending/Granted/Revoked/Expired/Legacy + basis). MVP1 uses the master-prompt subset `Owner / Consented / ContextOnly / Excluded`. `Consented` is reserved and cannot be assigned in MVP1. Only `Owner` may become a style subject. The full manifest is introduced with the Consent Registry (MVP2).

**DD-008 Domain records override `ToString`.** Compiler-generated record `ToString` prints all properties. `ChatMessage` and `Participant` print IDs only, so content cannot leak through exceptions, debugger displays or accidental logging.

**DD-007 Vault database = in-memory SQLite/FTS5 + AES-GCM encrypted snapshot (decided 2026-10-02 by the product owner).**
- **Decision**:
  1. Working databases (search, analysis) live **in memory** only, using SQLite with FTS5 where full-text search is needed.
  2. **No plaintext database is ever persisted.** Persistence is an AES-256-GCM encrypted snapshot (`sqlite3_serialize`/`deserialize`), written atomically.
  3. Each dataset has its own 256-bit key (DEK). The DEK is protected by Windows **DPAPI** (CurrentUser).
  4. Deleting a dataset destroys its DEK (**cryptographic deletion**); file deletion is secondary.
  5. No temp file, WAL, SHM or rollback journal may hold plaintext on disk. In-memory databases only, `journal_mode=MEMORY`/`OFF`, `temp_store=MEMORY`. Phase 2 tests must verify no sidecar files appear.
  6. **Commercial SQLCipher (or any paid DB-encryption product) is not used.** Only the free, supported SQLite build (e_sqlite3, Apache-2.0/public domain) may be considered, after it is recorded in `DEPENDENCIES.md`.
- **Reason**: keep the product completely free and local-first. The free .NET SQLCipher builds are "unofficial and unsupported" (SQLitePCLRaw.lib.e_sqlcipher 2.1.11), and the supported one is a paid license.
- **Trade-offs accepted**: the dataset must fit in memory (tens of MB per 100k messages), and the durability window is limited to the last committed snapshot.
- **Status**: decision only. Implementation is Phase 2 (not started).

**DD-009 Parser contracts are line-based and I/O-free.**
- `IChatFormatAdapter` works on one `string` line at a time with an immutable `ParserState`.
- Encoding detection, line splitting, size/length guards and format detection live in Ingestion (`ChatExportParser`), so Core has no `Stream`/`TextReader` coupling.
- The driver, not the adapter, advances `LineNumber`. Adapters cannot change it (enforced).

**DD-010 `MessageId` is position-based (`m{startLine:D7}`), not a content hash.** IDs are log-safe. A hash of short messages ("ㅇㅇ", "밥 먹었어?") can be reversed with a dictionary, so content-derived IDs would leak content through logs.

**DD-011 Encoding policy.**
- Detection order: UTF-8 BOM → strict UTF-8 → strict CP949. Otherwise the file is rejected as unsupported encoding. **No lossy decoding** (no `?`/U+FFFD substitution).
- CP949 is obtained from `CodePagesEncodingProvider.Instance` (in-box since .NET 5). `Encoding.RegisterProvider` is never called, so there is no process-wide side effect.
- Verified on 2026-10-06: .NET's strict CP949 decoder does **not** reject every non-KS byte. `0x80` decodes to U+0080, `0xFF` to U+F8F7, and user-defined areas (e.g. `C9 A1`, `FE FE`) to Private Use Area characters. It does reject a lead byte with an invalid trail byte (`81 20`) and a truncated lead byte (`B0` at end of input). Golden `invalid-encoding.txt` uses `81 20`. A test asserts `Encoding.GetEncoding(949)` still fails, proving no global registration.

**DD-012 Synthetic formats first.** Golden data uses three explicitly synthetic formats (`docs/SYNTHETIC_FORMATS.md`), not guessed private messenger formats. Adapters for real exports are added only after validation against real files kept in an encrypted local folder outside the repository. Synthetic adapters are not registered in the production DI container.

**DD-013 SampleGenerator is an independent oracle.**
- `tools/SampleGenerator` references no TalkPro project. Expected results are derived from the logical corpus, not from parser code.
- It never reads input files.
- It writes only to a directory named `Golden`, together with a `manifest.json`.
- Tests require the Golden folder to contain exactly the manifest's files. This stops a real export from being dropped into the git-allowed Golden path.

**DD-014 Platform & network policy (decided 2026-10-06 by the product owner).** Details: `docs/MOBILE_PLATFORM_OPTIONS.md`.
1. **Platform**: Windows first, plus an Android real-device PoC. The PoC moves no UI. It checks four things: Share Intent → parse, Korean reply drafts with 1–4B models, device performance/thermals, and exclusion from cloud backup. The UI framework (MAUI preferred) is decided after the PoC. AI runtimes stay behind `ILlmClient`/`IEmbeddingClient`, with platform runtimes in platform projects.
2. **iOS**: no paid Apple Developer Program and no Mac build host for now. Revisit at the distribution stage, after the Android PoC.
3. **Network**: allowed **only** for model download, app update and model-version checks (signed allow-list + SHA-256).
   - **Forbidden**: sending conversation text, messages, style profiles, embeddings or memory DB content.
   - Network code lives only in `TalkPro.Llm` (architecture test).
   - Tests must verify that request payloads carry no content.
   - Not implemented yet. Phase 5 (model registry/download).
4. **Self Persona Chat** = Phase 6.5. It reuses the Phase 6 engine (own messages → style features + memory retrieval + grounding labels). It is separate from the MVP1 core and is the user's own persona only.

**DD-015 Speaker review is mandatory and user-driven (Phase 1.9).**
- Exports carry no user IDs, so `SpeakerReview` requires the user to:
  - confirm the import attestation;
  - select the owner (never automatic, even with one participant);
  - resolve or acknowledge every same-name/alias suspicion before analysis.
- Suspicions are heuristics: normalized-name match, name containment, long inactivity gap, leave-then-return. They are hints only, never applied automatically.
- Excluded speakers' messages are dropped from the reviewed result. System lines are kept.

**DD-016 Key hierarchy and vault file format (Phase 2, part 1).**
- **Levels**:
  1. Platform protector `IKeyProtector` (Windows DPAPI CurrentUser; Android Keystore later, DD-014).
  2. Per-dataset 256-bit DEK.
  3. Per-purpose AES-256 subkey via HKDF-SHA256 (`TalkPro.Subkey.v1:{purpose}`).
- **Key files** live in `%LOCALAPPDATA%\TalkPro\keys\{datasetId}.key`, apart from `vault/`, so the key is destroyed first.
  - Format `TPK1`: version, protector kind, dataset id, wrapped length, wrapped DEK.
  - The DPAPI entropy binds the key to its dataset id.
- **Vault files** use format `TPV1`: header (magic, version, dataset id, purpose, 12-byte random nonce) + 16-byte tag + ciphertext.
  - The whole header is AES-GCM associated data.
  - Single-shot up to 1 GiB, consistent with the in-memory DB design (DD-007).
- **Writes** are atomic (temp + rename in the same folder; the temp file holds ciphertext only).
- **Deletion** (`DatasetDeletionService`): destroy the key (random overwrite with write-through, then delete), delete the vault and cache directories, scan for residuals, and report app-relative paths only.
- **Package**: `System.Security.Cryptography.ProtectedData` 10.0.12 (MIT, Microsoft), the only DPAPI wrapper; DPAPI is not in the .NET 10 shared framework.
- **Not yet**: the in-memory SQLite/FTS5 snapshot store (Phase 2, part 2), and Android Keystore.

**DD-017 VaultDatabase — in-memory SQLite with encrypted snapshots (Phase 2.4).**
- **Engine**: Microsoft.Data.Sqlite 10.0.12 + SQLitePCLRaw 2.1.12 (SQLite 3.53.3, FTS5).
- **Working DB**: `Data Source=:memory:` with **pooling off**. A pooled in-memory connection could otherwise be reused across datasets.
- **Persistence**: `sqlite3_serialize` → AES-GCM `TPV1` snapshot `vault/{id}/data.snapshot` (purpose `snapshot`, atomic write). Loading is the reverse via `sqlite3_deserialize` (FREEONCLOSE | RESIZEABLE). Managed plaintext buffers are zeroed after use.
- **Hardening, verified at open (fail closed)**:
  - `temp_store=MEMORY` (the bundled build defaults to file temp storage)
  - `journal_mode=MEMORY`
  - `secure_delete=ON` (deleted rows do not survive as free-page remnants inside snapshots)
  - `foreign_keys=ON`, `trusted_schema=OFF`
  - extension loading off
- **Attach limit = 0**, which blocks `ATTACH` and `VACUUM INTO` file exports. Plain `VACUUM` also attaches internally, so it is only reachable through `Compact()`, which raises the limit to 1 for that fixed statement.
- **Durability contract**: only `Save()` persists; unsaved changes are lost on crash. Callers save after each committed batch.
- **Verified by tests**: no `etilqs_*` temp files during a spilling workload. A control experiment proves the watcher detects them when `temp_store=FILE`, and mutation runs confirmed both guards.

**DD-018 Dataset repository and schema v1 (Phase 2.5).**
- **Structure**: `ReviewedChat`/`ReviewSummary` moved from Ingestion to `Core.Model`. Ingestion produces them, Infrastructure stores them and later phases read them; dependency rules are unchanged.
- **A dataset is**:
  - a key in `keys/`
  - `vault/{id}/data.snapshot` (encrypted SQLite image)
  - `vault/{id}/dataset.meta` (encrypted JSON: user label, created/updated, schema, counts, format)
  
  No dataset information is stored in plaintext. Listing decrypts metadata in memory. Unreadable datasets are listed as `Damaged` (deletable) instead of failing.
- **Schema v1** (`PRAGMA user_version`, STRICT tables):
  - `dataset_info`, `participant` (consent CHECK: Owner/ContextOnly/Excluded)
  - `message` (UTC ticks + offset minutes, so `DateTimeOffset` round-trips exactly)
  - external-content FTS5 `message_fts` (trigram)
  
  A newer schema is refused (`UnsupportedSchemaException`).
- **Import**: one per dataset (MVP1). Validated first: exactly one owner, no `Consented`, messages only from included participants, unique ids. Then one SQLite transaction → FTS rebuild → snapshot save → metadata. A failure inside the transaction rolls everything back (verified by mutation).
- **Search**: `SearchMessageIds` is a minimal lookup (FTS phrase for 3+ characters, LIKE otherwise, always parameterised). Hybrid retrieval comes in Phase 7.
- **Crash window**: if the app stops between the snapshot save and the metadata write, the metadata counts are stale. They are derived data, to be recomputed on open in a later phase.

**DD-019 Presentation layer and Debug-only synthetic formats (Phase 2.6 + import wizard).**
- **`TalkPro.Presentation`** (net10.0) holds ViewModels (CommunityToolkit.Mvvm) and application services (`ImportWorkflow`, `DatasetCatalog`, `IDialogService` abstraction).
  - The WPF app keeps only XAML views and `WpfDialogService`.
  - Architecture test: Presentation references no WPF or network assemblies, so a MAUI client can reuse it (DD-014).
  - ViewModels never touch files, DB or crypto directly (§29).
- **Wizard order follows the report UX (9.5)**: attestation **before** any file is read → file (size-checked before reading, bytes zeroed after parsing, path never logged) → speaker review → save (on failure the half-created dataset is deleted).
- **Synthetic adapters are registered only in Debug builds** (`#if DEBUG` in `AppHost`), so the wizard can be tried with Golden samples. Release builds ship no format until real-format adapters are validated (Phase 1.8).
- **Dataset screen**: list, delete one, delete all. Confirmation dialogs default to "No" and include the user notice about the original TXT and external backups.
- The privacy review step (PII L1/L2/L4) is not in the wizard yet. It is the rest of Phase 3.

**DD-020 Privacy engine and L4 review (Phase 3.1–3.2).**
- **Raw messages are never modified.** The vault stores findings (offsets, type, layer, confidence; never the matched text) and the user's decision. Views are derived per purpose.
- **Detectors** (Privacy module, Core-only dependency):
  - **L1 patterns**: phone, e-mail, resident id (legacy checksum lowers or raises confidence only, since post-2020-10 numbers have no checksum), card (Luhn), account (shape + bank context, never overlapping a phone), IPv4, URL, address candidates.
  - **L2 dictionary**: participant names and aliases, Korean given names followed by a particle ("민수야"), titles, organisation suffixes.
  - **L3 minimal**: conservative keyword hints for health, mental health, religion, politics, sexual life, criminal matters and finance, one hint per topic per message.
  - Span findings never overlap: higher risk wins, then the longer span.
- **Decisions**:
  - **Keep / Mask / Exclude / Delete.**
  - Defaults: identifiers and names → Mask; sensitive topics → **Exclude** (master prompt "민감정보 기본 제외").
  - High-risk identifiers (resident id, card, account) cannot be kept. Sensitive topics cannot be masked.
  - Delete beats Exclude.
- **Purpose views**:

  | Purpose | Rule |
  |---|---|
  | Display | Original; high-risk identifiers masked |
  | LocalSearch | Original names (needed for memory search); high-risk identifiers masked; excluded messages omitted |
  | Analysis / LlmPrompt | High-risk identifiers always masked; other spans masked when decided Mask; stable pseudonyms `[인물N]`/`[기관N]`; excluded messages omitted |
  | CloudTransfer | Refused (DD-014) |

  A Delete decision means the message is not stored at all.
- **Schema v2**:
  - `message.excluded`, `pii_finding` (action CHECK excludes Delete)
  - `message_search` FTS5 built from the LocalSearch view, replacing the raw-text index
  - `dataset_info.privacy_reviewed`

  v1 datasets migrate automatically. Their raw text is indexed and they are marked as not reviewed.
- **Layering**: Infrastructure cannot reference Privacy, so `PrivacyImportBuilder` (Privacy) precomputes the search view, and `DatasetSession.ImportReviewed` validates consistency before one transaction.
- **Limits (stated in the UI)**: names of people who are not participants are only caught via titles. The L3 keyword list is a candidate list, not a classifier. ONNX NER is deferred until its license is reviewed. The product never claims complete removal.

**DD-021 Real formats are learned from content-free probe reports (Phase 1.8, step 1).**
- **Why**: reading a real export with the AI assistant would send conversation text off the device, which violates the top project principle. Guessing private formats is also excluded (DD-012).
- **Tool**: `tools/FormatProbe` runs on the user's PC only.
  - It is read-only, prints to stdout only, never prints file names or paths, and never uses the network. Architecture test: no network, no Infrastructure, no logging.
  - `profile` reports encoding, line endings, header skeletons and the top line skeletons.
  - `validate` reports the parse rate and the skeletons of issue lines for the available adapters.
- **Skeletons**:
  - Digits become `⟦D4⟧`; Hangul, Latin and other words become `⟦H3⟧`, `⟦A5⟧`, `⟦X2⟧` (length only); emoji and symbols become `⟦E⟧`; control characters become `⟦C⟧`.
  - Only an allow-list of format words and ASCII punctuation stays verbatim.
  - Output is cut after 3 content tokens or 64 input characters.
  - A test verifies on every line of every golden file that nothing but tokens, allow-listed words and punctuation remains. A mutation that emits Hangul verbatim fails 52 tests.
- **Step 2** (after the user shares a report): real adapters plus synthetic golden samples in the same shape, then the user runs `validate` locally. Release registration requires a line parse rate of at least 99 %. Procedure: `docs/REAL_FORMAT_PROTOCOL.md`.

**DD-022 Sessions and owner-centred analytics (Phase 4).**
- **Sessionizer** (`TalkPro.Memory`): time gaps are the primary signal; the calendar day is not used, so late-night talks stay together.
  - Gap ≤ 10 min: same session. Gap ≥ 6 h: new session.
  - Otherwise a gap above the chat's adaptive threshold (P90 of gaps under 6 h, clamped to 20 min–2 h; 30 min if fewer than 20 gaps) is a candidate. Split score = 2·min(gap/T, 3)/3 − 0.8·continuation + 0.5·greeting − 0.6·(previous message is a question) [+ 1.2·(1 − similarity) − 0.6 when an `ISemanticSimilarity` is supplied]. Split when the score ≥ 1.0.
  - Out-of-order timestamps count as a zero gap; the file order is kept. Sessions longer than 400 messages are cut.
  - Sessions hold only message ids and times (no text), and are not stored yet (Phase 7 will persist them with the memory index).
- **Analytics** (`TalkPro.Persona`, rule-based, no LLM): only the owner's messages are measured. A counterpart is used only to attribute the owner's reply that follows them in the same session. Counterpart emotion, psychology or affection is never computed (§25), and the UI says so.
- **Input is the Analysis view** (`PurposeRedactor.RenderAll`): messages flagged excluded are dropped, Mask decisions and high-risk masking are applied, pseudonym tokens (and particles attached to them) are removed from keywords. Datasets without `privacy_reviewed` are refused.
- **Not persisted**: the report is computed on demand in memory and discarded with the page; nothing is logged except counts.
- **Register rule**: 합쇼체 = -십시오 or -니다/-니까 after a syllable with final ㅂ (so 그러니까 / 아니다 stay 반말); 해요체 = -요/-죠; otherwise 반말; no Hangul ending = unknown.
- **Known limits**: keywords use particle stripping, not morphological analysis; "-ㄹ래" is not treated as a question (ambiguous); register is by last word only.

**DD-023 Local AI: loopback runtime, typed redaction, model allow-list (Phase 5).**
- **Platform-neutral contracts in Core** (`ILlmClient`, `IEmbeddingClient`, `ILocalAiRuntime`). Feature modules (Persona, Memory) depend only on these, so a runtime can be swapped per platform without touching them: Ollama now (Windows MVP), an in-process llama.cpp/LLamaSharp runtime later (Windows without Ollama, Android PoC).
- **Conversation data only as `RedactedText`**: its constructor is internal to Core and visible only to TalkPro.Privacy (architecture test), which creates it from the LlmPrompt view. Instructions and tasks are app-authored strings. Context items are wrapped in delimiters, and look-alike delimiters inside data are neutralised.
- **Loopback only**:
  - Endpoint = `http://127.0.0.1:port/` or `http://[::1]:port/`. Host names (including `localhost`, which a hosts file can redirect), credentials, paths and queries are rejected.
  - `ConnectCallback` re-checks every socket.
  - No proxy, no automatic redirects, no cookies or credentials, bounded response size, per-call timeouts.
  - The app itself makes no internet request; models are installed by the user with `ollama pull`.
- **Model registry**:
  - Allow-list with license, approval level and pinned runtime digests.
  - Release uses `Approved` models only (license confirmed for commercial use and digest pinned); Debug also allows `Candidate`/`LicenseReview`.
  - A pinned model whose installed digest differs is refused.
  - **[DESIGN DECISION REQUIRED]** which models become Approved for Release (see DEPENDENCIES.md); none are approved yet.
- **Structured output**: the JSON schema is derived from the C# type and sent as Ollama `format`. Replies are parsed strictly: unknown members, missing members, nulls and numbers-as-strings are rejected, and parsing never throws.
- **Labelling and logs**: every `LlmResult` is AI-generated and carries model provenance for the "AI 생성" label. Logs record only error codes, counts and durations.
- **Other networks and mobile environments**:
  - The product works on any network, including offline, because inference never needs the network.
  - The only network uses DD-014 allows are model download, app update and version check. They must work behind proxies and on metered or mobile data: resumable download, Wi-Fi-only option on mobile, SHA-256 verification, and no content in requests.
  - These belong to the in-process runtime track (5.6) and are not implemented yet.
  - Using one device's model from another device (phone → PC over LAN or the internet) would send conversation text over the network. It conflicts with DD-014 and the "no external server/SaaS" principles (relays/VPN services), and is not planned unless the owner decides otherwise.
- **Known limits**:
  - Ollama itself can still reach the internet for `pull`. That is the user's own tool, outside the app.
  - Digest pinning uses Ollama's manifest digest. For the in-process runtime, the GGUF file SHA-256 will be pinned instead.
  - Response quality in Korean is not evaluated yet (Phase 6).

**DD-024 Self Style reply drafts (Phase 6).**
- **Model**: Qwen3 8B (`qwen3:8b`, Apache-2.0, Q4_K_M) is the default chat model by owner decision. It is Approved with its digest pinned, so Release builds can use it. Its reasoning phase is turned off (`think:false`), and a leftover `<think>` block is removed.
- **Style without content in the instruction**: the profile is numbers from the Phase 4 report (register mix, P50/P90 length, ㅋ/ㅎ, emoji, punctuation). It is rendered into app-authored rules. A relationship-scoped profile is used only with at least 10 replies to that person.
- **Exemplars**:
  - Only the owner's first reply to a counterpart message within a session, from the LlmPrompt view.
  - Up to 8 exemplars, ranked by same counterpart, character-bigram overlap with the typed message, then recency.
  - They go to the model as labelled `ContextLine`s ("예시-상대" / "예시-나" / "받은 메시지"). Line breaks inside data are flattened, so an item cannot fake a label.
  - Counterparts are never imitated, and no counterpart persona is built (§25).
- **Typed message**: redacted by `PromptInput` with the same detectors. Identifiers and names are masked. Sensitive topics are kept, because the owner deliberately asks about this text and the model is local.
  - One `PseudonymMap` per request keeps `[인물N]` consistent between exemplars and the typed message. `Restore` puts names back only for local display.
  - The typed message and the drafts are never stored or logged.
- **Output**: strict JSON (`{"drafts": [...]}`) with one retry at a lower temperature. The post-processor strips prefixes and quotes and drops empty, duplicate and over-long drafts.
  - Safety check: a draft that contains a phone, account, card, e-mail, URL or address not present in the typed message is dropped.
  - `StyleMatch` (0–100) is shown as a hint only.
- **No auto-send**: there is no send API. Copy puts the text on the clipboard with formats that exclude it from Windows clipboard history and cloud clipboard, and clears it after 60 s if unchanged.
  - Every draft is labelled "AI 생성 초안" with the model name.
  - **[법률 검토 필요]** (report §AI 기본법): whether in-app labelling plus the copy notice is enough when the user pastes a draft into another messenger.
- **Measured on this PC (synthetic data)**: RTX 3070 8 GB; first call about 70 s including model load, warm calls 2–4 s. Drafts followed the synthetic owner's style (반말, short, ㅋ, no period). The three drafts often share one direction (backlog 6.6).
- **Directions (Phase 6.6)**:
  - Model: `ReplyDirection` = Accept (수락) / Hold (보류) / AskBack (되묻기). The JSON is `{"drafts": [{"direction": "accept|hold|ask_back", "text": "..."}]}`.
  - Strict parsing: the schema lists the three values (`enum`), and parsing accepts only those exact names. Numbers, other casing and a missing `direction` are rejected.
  - Slots: a full generation asks for 수락, 보류, 되묻기 in that order. A card's direction can be changed and that card regenerated alone; repeated directions are allowed, and the UI warns when all are the same.
  - Each requested direction is a slot. It is filled only by a draft with exactly that direction that survives clean-up, the safety check and a minimal shape check (되묻기 is a question; 수락/보류 do not end with "?"). Drafts mixing in Han characters or kana are dropped.
  - Retry: empty slots are requested once more at a lower temperature. A slot still empty is shown as "만들지 못했습니다" with its own regenerate button; if no slot fills, the result is `InvalidOutput`.
  - Prompt: direction rules are fixed app sentences with sample phrasings, plus "write in Hangul only". Conversation text still enters only as `ContextLine`s.
- **Privacy in 6.6**:
  - Single-card regeneration reuses the dataset, counterpart and typed message of the last full generation. They are held in the ViewModel's memory only (never stored or logged) and redacted again on every request with a fresh shared pseudonym map.
  - The clipboard logic moved to `AutoClearClipboard` (Presentation, testable with a manual `TimeProvider`). The WPF backend only writes the text plus the three DWORD flags (`ExcludeClipboardContentFromMonitorProcessing`, `CanIncludeInClipboardHistory = 0`, `CanUploadToCloudClipboard = 0`), and clears after 60 s only if the clipboard still holds the same text.
  - A source-level architecture test forbids send/injection APIs and send buttons anywhere in `src/` except the Llm loopback client.
- **Evaluation set (synthetic only)**: `tests/TalkPro.Presentation.Tests/Evaluation/KoreanReplyEvalSet.v1.json`.
  - Contents: 2 synthetic owner personas (반말·ㅋ·no period; 해요체·period) and 12 scenarios covering 약속 잡기, 일정 조율, 거절, 안부, 부탁, 사과, 축하, 농담, 답장 지연, a phone number and a name.
  - Metrics: direction match (cue heuristic `DirectionCheck`), style match (register, ㅋ, period, length), PII leaks (must be 0), forbidden expressions, coverage, no-auto-send (reflection).
  - The report and failure messages carry scenario IDs, SHA-256 prefixes and flags only.
  - The live run is explicit-only and replaces the Phase 6 live draft test.
  - Baselines with qwen3:8b (2026-10-12, two runs): coverage 97–100 %, direction 75–83 % (53–58 % before the 6.6 prompt and shape check), style 91–92 %, PII leaks 0, forbidden 0. Floors in the test: coverage ≥ 0.8, direction ≥ 0.6, style ≥ 0.6.
  - Known limit: the direction metric is a keyword heuristic and under-counts natural 보류/수락 wording.

**DD-025 Memory search (Phase 7).**
- **Retrieval unit**: `MemoryItem` = a window of up to 6 text messages (overlap 2, ≤ 400 characters) inside one session, holding ids and times only.
  - A window must contain an owner message. Counterpart (ContextOnly) messages are retrievable only as context around the owner's own (report §9.2: "마스킹된 맥락으로만").
  - Laughter- or jamo-only windows are skipped (report §6.3: single messages are too noisy).
- **Embeddings**: model bge-m3 (MIT), approved by the owner and pinned.
  - Input is the LlmPrompt view as "나: …/상대: …" lines, built by `PromptInput.Join`, so it stays a `RedactedText`. Names become [인물N], excluded messages and high-risk numbers are absent.
  - The query is redacted with the same pseudonym map, which is deterministic for a dataset.
  - Embeddings can be inverted into text (report §2), so the index is stored only as `memory.index`, encrypted with the dataset DEK under purpose `memory-index`. Dataset deletion and crypto-shredding remove it; nothing is cached elsewhere.
  - A fingerprint (chunker version + item composition + LlmPrompt text) marks the index stale. A different embedding-model digest at query time falls back to keyword search.
- **Keywords**: FTS5 trigram BM25 over the LocalSearch view.
  - Query terms are split on spaces, common particles and question words are dropped, and the terms are OR'ed. Terms under 3 characters use a LIKE scan.
  - Terms are bound as parameters and quoted, so FTS operators cannot be injected.
- **Hybrid score**: 0.55·cosine + 0.25·BM25 (normalised) + 0.05·recency (τ = 365 days) + 0.10·participant mentioned. Entity overlap is not used in MVP1.
  - A candidate needs a keyword hit, or a dense match that is ≥ 0.45, within 0.10 of the best and ≥ 0.08 above the median dense score.
  - Reason: bge-m3 cosines on synthetic Korean chats overlap in absolute terms (paraphrase 0.50–0.68, unrelated up to 0.48), but an unrelated query scores everything alike.
  - Top 8 results; a last filter drops any excluded message. With no candidate: "기록에서 찾지 못했습니다."
- **Evidence**: cards are QUOTE only — the LocalSearch view rendered from the vault (original names for local display, high-risk numbers masked). No model text is involved in Phase 7.
  - `QuoteVerifier` prepares Phase 6.5: models quote only by `{{quote:id}}`, ids outside the evidence set become Unsupported, and quote text always comes from the vault.
- **Not stored**: queries and results stay in memory, and logs carry no query or result text.
- **Measured (2026-10-13, RTX 3070)**: the live test indexes a 16-message synthetic chat, finds the 강릉 trip for "동해 여행 계획 세웠던 거" (no shared keyword), and returns nothing for "회사 연봉 협상".
- **Known limits**:
  - Thresholds are tuned on small synthetic sets; a retrieval evaluation (Recall@8) is backlog 7.6.
  - No morphological analyser: a term like "제주도에서" is shortened to "제주도", but verb forms are not.
  - Re-indexing is manual (button), not automatic.

**DD-026 Android app with on-device AI, iPhone web viewer (owner decision 2026-10-13).**
- **Android** (`src/TalkPro.Mobile`, .NET MAUI): a second composition root that reuses every module and the UI-agnostic Presentation ViewModels.
  - Pages are created through DI.
  - `IDialogService` gained async members (default implementations keep WPF unchanged) and `ReleasePickedFile`. Android copies a picked document into the app cache, and that copy is deleted right after parsing.
  - Debug builds bundle the synthetic sample; real formats wait for Phase 1.8b.
  - A composition test mirrors MauiProgram with fake platform services.
- **Keys and storage on Android**:
  - Dataset keys are wrapped by an Android Keystore AES-256-GCM key (non-exportable; purpose bound as AAD).
  - Data lives in app-private storage. `allowBackup=false` plus data-extraction rules exclude every domain from cloud backup and device transfer.
  - Drafts copied to the clipboard carry `IS_SENSITIVE` and are cleared after 60 s by the shared `AutoClearClipboard`.
- **On-device AI**: ONNX Runtime GenAI 0.17.1 (MIT) with Phi-4-mini-instruct int4 (MIT, 3.8B, 4.9 GB), owner's choice over a self-converted Qwen3-1.7B.
  - The same `ILlmClient` and Phi chat template are used.
  - Chat-template control tokens inside data are neutralised by the shared `PromptComposer`.
  - JSON output uses the runtime's schema guidance; the first complete JSON object is kept because stray tokens can follow it.
  - The KV cache is sized to prompt + output tokens to keep phone memory low.
  - Verified on this PC with the same engine and model: load 11 s, about 9.6 tokens/s on CPU, schema-valid Korean output. Phone figures are still to be measured (M.6).
  - No embeddings on the phone yet: memory search there is keyword-only.
- **Model download** (DD-014 allowed use; code only in TalkPro.Llm):
  - HTTPS only; Hugging Face hosts on every hop, with manual redirects checked against the allow-list.
  - Plain GETs with at most a Range header, so no user data is sent.
  - Resume is supported. Every file is pinned to its SHA-256 (equal to the Hugging Face LFS id), and a verified-marker file avoids re-hashing 4.9 GB at start.
  - The app asks before downloading on a metered network.
- **iPhone**: no app (no Mac, DD-014). Instead, a Blazor WebAssembly viewer (`src/TalkPro.Web`) runs the same Core/Ingestion/Privacy/Persona/Memory code in the browser.
  - The chat is read, reviewed with default privacy decisions, analysed and keyword-searched in page memory only. Nothing is uploaded or stored, and reload discards it.
  - No AI in the browser.
  - `tools/WebViewerHost` serves only the published files on the LAN: GET/HEAD only, no request logs, `no-store`, and CSP `default-src 'self'; connect-src 'self'; script-src 'self' 'wasm-unsafe-eval'`. The page cannot send data anywhere; this was verified in the browser network log, which showed same-origin GETs only.
  - The browser runtime lacks NFKC, so the speaker-name normaliser folds full-width forms by hand there. A parity test covers both paths.
  - A viewer that streams data from the PC to the phone was rejected: it would send chat text over the network (DD-014).
- **[DESIGN ISSUE] 16 KB pages**: `libmat.so` in ONNX Runtime GenAI 0.17.1 (required by `libonnxruntime-genai.so`) is 4 KB aligned.
  - On 16 KB-page devices on-device AI cannot load.
  - XA0141 is suppressed in the Mobile project only, with this reason. Not acceptable for a Play Store build (M.7).
- **[DESIGN ISSUE] Test runner hang**: running ONNX Runtime GenAI inside the xUnit/MTP test host left an unkillable process (the same code exits normally as a console app).
  - The live on-device check is therefore `tools/OnDeviceSmoke`, not an explicit test.

**DD-027 First real format: KakaoTalk PC export (Phase 1.8b).**
- **Source**: the owner attached one real 1:1 export directly, instead of the FormatProbe report (DD-021).
  - Only its structure was used.
  - No real text, name or timestamp went into code, tests or docs. The fixtures in `tests/TalkPro.Ingestion.Tests/RealFormats/Golden/kakao-pc` are hand-written synthetic text in the same shape.
  - For future formats the content-free probe remains the preferred channel.
- **Shape**: header `{상대} 님과 카카오톡 대화` + `저장한 날짜 : yyyy-MM-dd HH:mm:ss` (kept as metadata only, values dropped), then `--------------- yyyy년 M월 d일 X요일 ---------------` separators and `[이름] [오전|오후 h:mm] 본문`.
  - 12-hour clock: 오전 12:xx = 00:xx, 오후 12:xx = 12:xx.
  - Only a line that fully matches a message or separator starts a record. Every other line continues the open message, because real continuation lines may start with "[".
- **Validation**: `FormatProbe validate` on the real file reported 100 % (221 lines → 214 messages, 1 continuation, 0 malformed). This meets the ≥ 99 % rule, so `KakaoFormats.All` is registered by `AddTalkProIngestion` in every build (PC, Android, web viewer). Synthetic formats remain Debug-only.
- **Known gaps (1.8c)**:
  - Group-chat system lines on PC (join/leave/invite) were not in the sample and are treated as continuations.
  - Media placeholders ("이모티콘", "사진") stay text messages.
  - Android and iPhone export formats are not supported yet.

**DD-028 "말투 AI": imitate a consenting counterpart's style, web/PWA + Android, GGUF on device (owner decision 2026-10-13).**
- **Scope change**: the owner asked for a model that learns **the other person's** speaking style. This reverses the product-policy exclusion in DD-014 for a separate test app, `apps/mimic`, and does not touch the TalkPro modules (Self Style drafts keep "no counterpart imitation").
- **Safeguards** (the phishing risk in the report, ch. 3):
  - Two attestations before analysis: the user took part in the chat and has the imitated person's consent (or is that person), and will not send generated text as if it came from them.
  - Every reply bubble carries an "AI" tag and the chat shows a permanent "AI가 흉내 낸 … · 실제 … 아님" banner. There is no send or share function.
  - `persona/safety.ts`: replies about money, transfers, accounts, verification codes, passwords, gift cards, "broken phone / other number", app installs or remote control are never shown. The same patterns cut those replies out of the training data, so a fine-tuned model does not learn them.
  - Identifiers (phone, account, card, resident id, e-mail, links) are masked; media placeholders dropped; other group members become `참여자N`. Names inside message text are not detected (stated in the UI).
- **One codebase for both phones**: a Vite/TypeScript web app (PWA). The iPhone uses it from Safari (no Mac, DD-014); Android uses the same build in a Capacitor 8 WebView (`apps/mimic/android`, debug APK from CI).
- **Runtime**: wllama 3.8.1 (llama.cpp → WebAssembly, MIT) loads GGUF files picked on the device or downloaded once from Hugging Face (https only, CSP-limited to huggingface.co / hf.co, cached in the browser). Safari lacks JSPI, so the bundled `@wllama/wllama-compat` build is used there (no CDN).
  - Threads need cross-origin isolation: Vite sets COOP/COEP in dev/preview, `public/sw.js` adds them on static hosting (GitHub Pages), and `MainActivity` adds them to the Capacitor local server.
  - Context 2048 tokens, f16 KV cache (q8_0 needs head size divisible by 32). Over-long prompts are retried once with a smaller history/example budget.
- **Two modes**: (1) a fine-tuned GGUF with the exact training system prompt (`training/system_prompt.txt` is the single source for the app and the notebook); (2) a base Qwen2.5 Instruct model with numeric style rules plus real reply examples ranked by bigram overlap (no training, needs the analysed chat in memory).
- **Training** (outside the app, free GPU): the app or `training/prepare_kakao.py` writes chat JSONL with the imitated person as `assistant`; `training/train_persona_unsloth.ipynb` runs Unsloth QLoRA on Kaggle/Colab T4, trains on assistant turns only, exports GGUF Q4_K_M and 512 MB splits. Uploading to Kaggle/Colab sends the chat to that service; the app and notebook say so and the notebook ends with a cleanup step.
- **Parity**: the TypeScript and Python converters must produce `training/fixtures/expected.jsonl` byte for byte from four synthetic exports (PC, Android, iOS, Mac CSV). Both test suites check it; Python counts lengths in UTF-16 units and uses ASCII `\b`/`\d` to match JavaScript.
- **Storage**: conversations and analysis live in page memory only; settings (name, mode, model URL) in localStorage; downloaded models in the browser cache. Android excludes all app data from backup and device transfer.
- **Model licences**: Qwen2.5 0.5B/1.5B Instruct are Apache-2.0. **Qwen2.5-3B is under the Qwen Research licence (non-commercial)**, so it is offered for personal tests only.
- **Verified here**: unit/parity tests (TS 25, Python 3), production build, and an end-to-end run in headless Chromium with a synthetic random-weight GGUF (load from file, multithread, chat in both modes). Not verified here: a real Qwen model (Hugging Face is not reachable from the build container), the APK build (no Android SDK; built in CI), Safari.

## Open issues

- **[DESIGN ISSUE] CP949 Private Use Area mapping.**
  - Because of the decoder behaviour above (DD-011), a non-UTF-8 file of random bytes can "decode" as CP949 into PUA/C1 characters instead of failing.
  - Impact: low for real exports (they are UTF-8 or valid CP949), but a corrupted file could import as noise.
  - Recommended (Phase 1.8, with real samples): if decoded CP949 text contains U+0080–U+009F or PUA characters, reject it as `UnsupportedEncoding` or warn the user. Not implemented yet, to avoid guessing without real data.

- **[DESIGN ISSUE] Visual Studio 2017 upgrade artifacts (found 2026-10-02).** An old-format project upgrade was run on the solution.
  - Every csproj except App gained `ToolsVersion="15.0"`, `TargetFrameworkVersion v2.0`, `OldToolsVersion` and `FileUpgradeFlags`.
  - `TalkPro.sln` now has a VS 15 header.
  - `Backup/` and `UpgradeLog.htm` were created.
  - Build and tests are unaffected (the SDK derives the framework from `TargetFramework`), but the properties are misleading.
  - Recommended cleanup (by the owner, not done automatically): remove the inserted properties, delete `Backup/` and `UpgradeLog.htm`, and open the solution with Visual Studio 2026 / 17.14+.
