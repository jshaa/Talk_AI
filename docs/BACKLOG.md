# MVP1 Backlog

Source of Truth: `docs/Persona_AI_상용화_검토보고서.md` (ch. 4·5·6·7·9·10·11·12·13·18·19·23).
Status: ✅ done · 🔄 in progress · ⬜ todo · ⛔ blocked (see `DESIGN_DECISIONS.md`)

## Phase 0 — Foundation ✅
| # | Item | Status |
|---|---|---|
| 0.1 | Solution, .NET 10 SDK pin (`global.json`), nuget.org-only `nuget.config` | ✅ |
| 0.2 | `Directory.Build.props` (net10.0, Nullable, warnings-as-errors, latest-recommended analyzers, deterministic, lock files, NuGet audit) | ✅ |
| 0.3 | Central package management + license register (`DEPENDENCIES.md`) | ✅ |
| 0.4 | Core domain primitives (ids, `ChatMessage`, `Participant`, `ParticipantConsent`) with content-free `ToString` | ✅ |
| 0.5 | Module projects + DI entry points, composition root in App | ✅ |
| 0.6 | Logging policy: content-free logger as the only provider; CA2254/CA1848 as errors | ✅ |
| 0.7 | Storage location policy (`%LOCALAPPDATA%`, Documents/Desktop/OneDrive rejected) | ✅ |
| 0.8 | `.gitignore` / `.gitattributes` privacy rules | ✅ |
| 0.9 | WPF shell (local-only + generative-AI notices) | ✅ |
| 0.10 | Architecture tests (dependency direction, network APIs only in Llm) | ✅ |

## Phase 1 — Synthetic Data & Import ⬜
Gate: line parse success ≥ 99 % on the synthetic golden corpus.
| # | Item |
|---|---|
| 1.1 | `tools/SampleGenerator`: synthetic exports (Windows/Android/iOS, KO), UTF-8 / UTF-8 BOM / CP949, multiline, emoji, system/deleted messages, malformed lines, date rollover, special-character names, same-name speakers |
| 1.2 | `IChatFormatAdapter`, `LineParseResult`, `ParserState` in Core |
| 1.3 | `EncodingDetector` (BOM → strict UTF-8 → CP949) |
| 1.4 | `FormatDetector` (score head lines across adapters) |
| 1.5 | `WindowsKoAdapter`, `AndroidKoAdapter`, `IosKoAdapter` (regex timeouts, InvariantCulture) |
| 1.6 | Two-pass parser: line classification → speaker resolution → system/media classification (rules as data) |
| 1.7 | Golden tests + parse-rate report |

## Phase 2 — Secure Vault ⛔ (storage engine decision pending: DD-007)
Dataset model · KeyManager (DPAPI KEK, per-dataset DEK) · AES-256-GCM files · encrypted DB · vault repository · crypto-shredder · Delete All + residual scan tests.

## Phase 3 — Privacy ⬜
`PiiFinding` · L1 regex (checksums: RRN, Luhn) · L2 detector abstraction (participant dictionary + honorifics) · L3 interface · `PrivacyDecision` · `RedactionPolicy` (purpose-specific views) · Privacy Review UI · owner selection + participant attestation · `ConsentManifest` (MVP1 states).

## Phase 4 — Session / Analytics ⬜
`ISessionizer` (hard merge/split, adaptive threshold, continuation cues, semantic check at candidate boundaries) · self-centred statistics · register analysis · timeline · Analytics UI.

## Phase 5 — Local AI ⬜
`ILlmClient` / `IEmbeddingClient` · `OllamaClient` (loopback only) · model registry (allow-list, license, SHA-256) · structured JSON output.

## Phase 6 — Self Style ⬜
Style profiler (measured features, global + relationship scope) · exemplar index/search (owner replies only) · prompt builder · style post-processor · safety check · 3 drafts labelled "AI 생성 초안".

## Phase 7 — Memory ⬜
`MemoryItem` with `SourceMessageIds` · FTS5 · embeddings · vector store · hybrid retriever · evidence cards · "기록에서 찾지 못했습니다." · grounding labels prepared (QUOTE/FACT/INFERRED/GENERATED/UNSUPPORTED).

## Phase 8 — QA / Packaging ⬜
Integration, deletion, PII, parser and grounding regression evaluations · installer · code-signing and update-signing structure · privacy documentation.
