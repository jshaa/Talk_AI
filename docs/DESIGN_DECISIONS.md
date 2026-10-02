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

**DD-012 Synthetic formats first.** Golden data uses three explicitly synthetic formats (`docs/SYNTHETIC_FORMATS.md`), not guessed private messenger formats. Adapters for real exports are added only after validation against real files kept in an encrypted local folder outside the repository. Synthetic adapters are not registered in the production DI container.

**DD-013 SampleGenerator is an independent oracle.**
- `tools/SampleGenerator` references no TalkPro project. Expected results are derived from the logical corpus, not from parser code.
- It never reads input files.
- It writes only to a directory named `Golden`, together with a `manifest.json`.
- Tests require the Golden folder to contain exactly the manifest's files. This stops a real export from being dropped into the git-allowed Golden path.

## Open issues

- **[DESIGN ISSUE] Visual Studio 2017 upgrade artifacts (found 2026-10-02).** An old-format project upgrade was run on the solution.
  - Every csproj except App gained `ToolsVersion="15.0"`, `TargetFrameworkVersion v2.0`, `OldToolsVersion` and `FileUpgradeFlags`.
  - `TalkPro.sln` now has a VS 15 header.
  - `Backup/` and `UpgradeLog.htm` were created.
  - Build and tests are unaffected (the SDK derives the framework from `TargetFramework`), but the properties are misleading.
  - Recommended cleanup (by the owner, not done automatically): remove the inserted properties, delete `Backup/` and `UpgradeLog.htm`, and open the solution with Visual Studio 2026 / 17.14+.
