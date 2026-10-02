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

**DD-007 Vault database = in-memory SQLite/FTS5 + AES-GCM encrypted snapshot — CONFIRMED (product owner, 2026-10-02).**
- **Decision**:
  1. Working databases (search, analysis) live **in memory** only. SQLite with **FTS5** is used where full-text search is needed.
  2. **No plaintext database is ever persisted.** Persistence is an **AES-256-GCM encrypted snapshot** of the in-memory database (`sqlite3_serialize`/`deserialize`), written atomically (temp file of ciphertext + rename).
  3. Each dataset has its **own 256-bit data key (DEK)**. The DEK is protected with **Windows DPAPI** (CurrentUser scope) and stored next to the snapshot.
  4. Deleting a dataset destroys its DEK (**cryptographic deletion**). Deleting the snapshot file is secondary.
  5. No temp file, WAL, SHM or rollback journal may hold plaintext on disk: in-memory databases only, `journal_mode=MEMORY` or `OFF`, `temp_store=MEMORY`. Phase 2 tests must verify that no sidecar files appear.
  6. **Commercial SQLCipher, and any other DB-encryption product that needs a paid or commercial license, is not used.** Only the free, supported SQLite build (e_sqlite3, public domain / Apache-2.0 packaging) may be considered, after it is recorded in `DEPENDENCIES.md`.
- **Reason**: keep TalkPro **completely free and local-first** (no paid licenses, no usage-based APIs, no external servers). The free .NET SQLCipher builds are "unofficial and unsupported" (SQLitePCLRaw.lib.e_sqlcipher 2.1.11), and the supported build needs a paid license.
- **Trade-offs accepted**: a dataset must fit in memory (tens of MB per 100k messages), and durability is limited to the last committed snapshot.
- **Status**: decision confirmed; **implementation is Phase 2 and has not started**.

**DD-008 Domain records override `ToString`.** Compiler-generated record `ToString` prints all properties. `ChatMessage` and `Participant` print IDs only, so content cannot leak through exceptions, debugger displays or accidental logging.

**DD-009 Parser contracts are line-based and I/O-free.**
- `IChatFormatAdapter` (Core) has four members: `FormatId`, `Detect(sampleLines)`, `CreateInitialState(context)`, `ParseLine(state, line) → LineStep(State, Result)`. It works on one `string` line at a time with an immutable `ParserState`. It has no `Stream`/`TextReader`, logging or DI types (tested).
- Encoding detection, line splitting, size/length guards and format detection live in Ingestion (`ChatExportParser`, `EncodingDetector`, `LineSplitter`, `FormatDetector`).
- The driver, not the adapter, advances `LineNumber`. An adapter that changes the line number or the context, or returns a null result, makes the driver throw (with a content-free message).
- `LineParseResult` is an immutable type with an enum outcome (`MessageStarted`, `Continuation`, `SystemMessage`, `Metadata`, `Ignored`, `Malformed`, `Unsupported`) and an enum `ParseIssue`. Malformed/Unsupported require an issue; the other outcomes must not have one.
- **"Message completed" is not a line outcome.** A message is completed by whatever comes next (a record, a malformed record, a driver guard) or by end of input. The completed entries are attached to that line's result (`Completed`), and the driver flushes the open message at the end. This avoids a fake outcome for a line that did not cause the completion.
- `ParserState` holds only common state: line number, header/body section, date context, the open message (`PendingEntry`) and seen speaker names. Adapter-specific state is a typed `AdapterState` record (no `Dictionary<string, object>`). Its `ToString` is sealed so adapter assemblies cannot print content.
- A blank line is empty or whitespace-only (`string.IsNullOrWhiteSpace`). Trailing blank continuation lines are dropped when a message completes.
- Import results are explicit: `Success`, `EmptyInput`, `InputTooLarge` (> 256 MiB), `UnsupportedEncoding`, `UnknownFormat` (no adapter ≥ 0.5 confidence, or a tie).
- Participants are created per distinct speaker name in order of first appearance (`p_01`, …), always `ContextOnly` (DD-006). Merging nicknames and same-name speakers is Phase 1.6.

**DD-010 `MessageId` is position-based (`m{startLine:D7}`), not a content hash.** IDs are log-safe. A hash of short messages ("ㅇㅇ", "밥 먹었어?") can be reversed with a dictionary, so content-derived IDs would leak content through logs.

**DD-011 Encoding policy.**
- Detection order: UTF-8 BOM → strict UTF-8 → strict CP949. Otherwise the file is rejected as unsupported encoding. **No lossy decoding** (no `?`/U+FFFD substitution).
- CP949 is obtained from `CodePagesEncodingProvider.Instance.GetEncoding(949, ExceptionFallback, ExceptionFallback)` (in-box since .NET 5). `Encoding.RegisterProvider` is never called, so there is no process-wide side effect. This holds in both Ingestion and the SampleGenerator. Tests check it from assembly metadata (no `RegisterProvider` reference in any TalkPro assembly) and at runtime (`Encoding.GetEncoding(949)` still fails after decoding CP949).
- Known limitation: .NET's CP949 table maps a few non-text bytes (e.g. `0xFF`) without error, so some binary input decodes "successfully". Format detection then rejects it as `UnknownFormat`.

**DD-012 Synthetic formats first.** Golden data uses three explicitly synthetic formats (`docs/SYNTHETIC_FORMATS.md`), not guessed private messenger formats. Adapters for real exports are added only after validation against real files kept in an encrypted local folder outside the repository. Synthetic adapters (`TalkPro.Ingestion.Synthetic`) are not registered in the production DI container (tested in `CompositionTests`).

**DD-013 SampleGenerator is an independent oracle.**
- `tools/SampleGenerator` references no TalkPro project. Expected results (line outcomes, issues, entries, statistics) are derived from the logical corpus and the format specification, not from parser code.
- It never reads files and uses no network. Tests check this from assembly metadata (no `File.Read*`/`Open*`, `StreamReader`, `FileStream`, `System.Net*`).
- It writes only to a directory named `Golden` (`tests/TalkPro.Ingestion.Tests/Golden`), together with a `manifest.json`. Stale files are removed only if they match the generator's own naming scheme (`{w1|a1|i1}.{case}.{utf8|utf8bom|cp949}.txt`).
- Output is deterministic: a fixed SplitMix64 PRNG (default seed `20261002`), fixed timestamps, invariant formatting and `\n` in the manifest. Building the set is pure (in memory), so tests regenerate it and compare it byte for byte with the checked-in files.
- Tests require the Golden folder to contain exactly the manifest's files. This stops a real export from being dropped into the git-allowed Golden path.
- Corpus errors fail generation, for example an orphan line inside an open message, a continuation line with a reserved record prefix, or CP949 text that is not representable. A sample never claims something it does not test.

## Open issues

- **[DESIGN ISSUE] Visual Studio 2017 upgrade artifacts (found 2026-10-02).** An old-format project upgrade was run on the solution.
  - Every csproj except App gained `ToolsVersion="15.0"`, `TargetFrameworkVersion v2.0`, `OldToolsVersion` and `FileUpgradeFlags`.
  - `TalkPro.sln` now has a VS 15 header.
  - `Backup/` and `UpgradeLog.htm` were created.
  - Build and tests are unaffected (the SDK derives the framework from `TargetFramework`), but the properties are misleading.
  - Recommended cleanup (by the owner, not done automatically): remove the inserted properties, delete `Backup/` and `UpgradeLog.htm`, and open the solution with Visual Studio 2026 / 17.14+.
