# MVP1 Backlog

Source of Truth: `docs/Persona_AI_상용화_검토보고서.md` (ch. 4·5·6·7·9·10·11·12·13·18·19·23).
Status: ✅ done · 🔄 in progress · ⬜ todo · ⏳ waiting for input · ⛔ blocked (see `DESIGN_DECISIONS.md`)

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

## Phase 1 — Synthetic Data & Import 🔄
Gate: line parse success ≥ 99 % on supported synthetic samples — **met (100 % on all non-robustness golden files)**.
| # | Item | Status |
|---|---|---|
| 1.1 | `tools/SampleGenerator`: deterministic (SplitMix64 seed) synthetic corpus for 3 synthetic formats (W1/A1/I1, docs/SYNTHETIC_FORMATS.md) × UTF-8 / UTF-8 BOM / CP949. Expected-result JSON per case, `manifest.json` with SHA-256. Writes only into a folder named `Golden`, never reads inputs, never deletes, `--check` mode. 77 files | ✅ |
| 1.2 | Core contracts: `IChatFormatAdapter`, `LineParseResult` (+`LineOutcome`, `ParseIssue`), immutable `ParserState` with typed `AdapterState`, `ParsedEntry`, `ParseContext`, `ChatFormatId`, `ParsedChat` | ✅ |
| 1.3 | `TextDecoding` (BOM → strict UTF-8 → strict CP949, no lossy decode, no global provider registration) + `LineSplitter` | ✅ |
| 1.4 | `ChatExportParser.SelectAdapter` (content scores, minimum score, ambiguity margin) | ✅ |
| 1.5 | Synthetic adapters `SyntheticWindowsAdapter` / `SyntheticAndroidAdapter` / `SyntheticIosAdapter` (test-only, not in production DI) | ✅ |
| 1.6 | Driver: line numbering, guards (line length, NUL, regex timeout), contract enforcement, participant mapping (all `ContextOnly`), system-event rule table | ✅ |
| 1.7 | Golden, equivalence (encoding / line ending / format), contract, driver and architecture tests | ✅ |
| 1.8a | `tools/FormatProbe` (DD-021): offline content-free `profile` / `validate` reports for real exports; user procedure in `docs/REAL_FORMAT_PROTOCOL.md` | ✅ |
| 1.8b | **KakaoTalk PC export** (`KakaoPcAdapter`, `kakao.pc.ko.v1`, DD-027): validated 100 % on a real 1:1 export, registered in every build; hand-written synthetic fixtures in `RealFormats/Golden/kakao-pc` | ✅ |
| 1.8c | Android and iPhone export formats (need a probe report or sample of each), group-chat system lines (join/leave/invite) on PC, media placeholders (이모티콘/사진/동영상/파일) as message kinds, size/line-count benchmarks | ⬜ |
| 1.9 | `SpeakerReview` (Ingestion/Speakers, DD-015): immutable, UI-independent. Import attestation, user-only owner selection (never automatic), alias merge, same-name split (by messages or from a timestamp), exclusion. Suspicion hints (normalized-name match, containment, ≥180-day gap, leave-then-return) must be resolved or acknowledged. `Complete()` → `ReviewedChat` with exactly one owner. Wiring into the import UI: Phase 3 | ✅ |

## Phase 2 — Secure Vault 🔄 (DD-007, DD-016)
| # | Item | Status |
|---|---|---|
| 2.1 | Key hierarchy: `IKeyProtector` (DPAPI CurrentUser), per-dataset DEK, HKDF per-purpose subkeys, `DatasetKeyStore` (`TPK1`, bound to dataset, atomic writes) | ✅ |
| 2.2 | `VaultFileCrypto` AES-256-GCM container (`TPV1`, header as AAD, random nonce, atomic write) | ✅ |
| 2.3 | Crypto-shredding `DatasetDeletionService` (key first, then vault/cache incl. -wal/-shm/-journal, residual scan, user notice) | ✅ |
| 2.4 | `VaultDatabase` (DD-017): in-memory SQLite + FTS5, encrypted snapshot via serialize/deserialize, pooling off, temp_store/journal in memory, secure_delete, attach limit 0 (ATTACH / VACUUM INTO blocked), `Compact()`. Tests prove no temp files, no sidecars, no deleted-row remnants | ✅ |
| 2.5 | `DatasetRepository` / `DatasetSession` (DD-018): create (key + empty snapshot + encrypted metadata), list (Damaged state, no names in `ToString`), open with schema migration, schema v1 (participants, messages, FTS5 trigram), validated transactional import of `ReviewedChat`, round-trip load, minimal search | ✅ |
| 2.6 | Dataset screen: list (labels, counts, damaged state), delete one, delete all (confirm with user notice, residual reporting) — DD-019 | ✅ |
| 2.7 | Manual release check: another Windows user cannot unwrap (docs/SECURITY_TESTS.md) | ⬜ |

## Phase 3 — Privacy 🔄
| # | Item | Status |
|---|---|---|
| 3.0 | Import wizard (DD-019): attestation → file → speaker review (owner, merge, split by date, exclude, acknowledge) → encrypted save; `TalkPro.Presentation` layer | ✅ |
| 3.1 | PII detection L1 (patterns + resident-id checksum, Luhn, bank context), L2 (participant names, given names, titles, organisations), L3 keyword hints; overlap resolution; synthetic evaluation set (recall ≥ 0.99, precision ≥ 0.95) — DD-020 | ✅ |
| 3.2 | Wizard step 4 (L4): keep / mask / exclude / delete per type and per finding, local preview of the LLM view, conservative defaults; purpose-specific redaction; schema v2 with masked search index and v1 migration — DD-020 | ✅ |
| 3.3 | Optional ONNX Korean NER for third-party names (license review first) and a re-review flow for v1 / unreviewed datasets | ⬜ |

Remaining Phase 3 scope: `PiiFinding` · L1 regex (checksums: RRN, Luhn) · L2 detector abstraction (participant dictionary + honorifics) · L3 interface · `PrivacyDecision` · `RedactionPolicy` (purpose-specific views) · Privacy Review UI · owner selection + participant attestation · `ConsentManifest` (MVP1 states).

## Phase 4 — Session / Analytics ✅ (DD-022)
| # | Item | Status |
|---|---|---|
| 4.1 | `ISessionizer` / `ConversationSessionizer` (Memory): hard merge ≤ 10 min, hard split ≥ 6 h, adaptive threshold = P90 of the chat's own gaps clamped to 20 min–2 h, discourse cues (continuation, greeting, open question), optional `ISemanticSimilarity` at candidate boundaries only (no implementation until Phase 5/7 embeddings), 400-message cut; every message in exactly one session | ✅ |
| 4.2 | `CommunicationAnalyzer` (Persona): owner-only length P50/P90, burst, hour/weekday, reply latency, question / emoji / ㅋ·ㅎ / punctuation rates, register (합쇼체·해요체·반말), per-counterpart owner style, keywords, monthly timeline, busiest days, quarterly style change | ✅ |
| 4.3 | `AnalyticsService` (Analysis view only, privacy-reviewed datasets only, computed on demand, not stored) + `AnalyticsViewModel` + `AnalyticsView`; "선택한 데이터셋 분석" button on the dataset list | ✅ |
| 4.4 | Morphological analyser for keywords (license review first) and an `ISemanticSimilarity` implementation from local embeddings | ⬜ (Phase 5/7) |

## Phase 5 — Local AI ✅ (DD-023)
| # | Item | Status |
|---|---|---|
| 5.1 | Core contracts (platform-neutral): `ILlmClient`, `IEmbeddingClient`, `ILocalAiRuntime`, `LlmRequest`/`LlmResult` (always AI-generated), `RedactedText` (only Privacy can create it), `StructuredOutput` (schema from C# type, strict parse) | ✅ |
| 5.2 | `OllamaClient` (Llm): loopback IP literal only + per-socket loopback check, no proxy/redirect/cookies, bounded responses, timeouts, content-free logs; chat, embeddings, status | ✅ |
| 5.3 | Model registry and policy: allow-list with license, approval level (Approved / Candidate / LicenseReview), pinned runtime digests; Release = Approved only, Debug = all listed | ✅ |
| 5.4 | "로컬 AI" page: runtime status, model states with `ollama pull` commands, connection test with a fixed prompt and "AI 생성" label | ✅ |
| 5.5 | Default models: chat = **qwen3:8b** (Approved, digest pinned, reasoning turned off with `think:false`); embedding model decided in Phase 7 | ✅ |
| 5.6 | In-process runtime (LLamaSharp / llama.cpp, GGUF SHA-256 verified) behind the same contracts, for Windows without Ollama and for the Android PoC | ⬜ |

## Phase 6 — Self Style ✅ (DD-024)
| # | Item | Status |
|---|---|---|
| 6.1 | `StyleProfile` (Persona): measured style from the Phase 4 report, global or per counterpart (≥ 10 replies); rules and UI summary contain numbers only | ✅ |
| 6.2 | `StyleExamples`: exemplar search over the owner's first replies to a counterpart within a session; ranking = same counterpart, bigram overlap with the typed message, recency; duplicates / empty / over-long skipped | ✅ |
| 6.3 | `ReplyPromptBuilder` (labelled context lines, data-is-not-instructions rule, no counterpart imitation, no invented identifiers) · `DraftPostProcessor` · `StyleMatch` score | ✅ |
| 6.4 | `ReplyDraftService`: LlmPrompt view + typed message redacted with one pseudonym map; strict JSON with one retry; safety check drops drafts with invented phone/account/address etc.; names restored for local display only | ✅ |
| 6.5 | "답장 초안" page: dataset + counterpart, 3 drafts with "AI 생성 초안" label and style-match hint; copy only (cleared after 60 s, excluded from Windows clipboard history / cloud sync); no send function exists | ✅ |
| 6.6 | Draft directions (DD-024): one draft each for 수락 / 보류 / 되묻기 by default, `direction` in the JSON (`accept`/`hold`/`ask_back`, exact names only), per-card direction change and single-card regeneration, same-direction warning, direction shape check; synthetic Korean evaluation set (12 scenarios, 9 categories) with direction / style / PII / no-auto-send metrics; auto-clearing clipboard moved to a testable service | ✅ |
| 6.7 | Embeddings-based exemplar search (with Phase 7) and a larger evaluation set; consider a judge model for direction instead of the cue heuristic | ⬜ |

## Phase 6.5 — Self Persona Chat ⬜ (DD-014, separate from MVP1 core)
Chat with an AI that speaks in the user's own style, reusing the Phase 6 engine plus memory retrieval and grounding labels (QUOTE/FACT/INFERRED/GENERATED/UNSUPPORTED). Owner persona only, persistent "AI Persona" labelling, no memory write-back.

## Phase 7 — Memory ✅ (DD-025)
| # | Item | Status |
|---|---|---|
| 7.1 | `MemoryItem` (Core): turn windows of a session (6 messages, overlap 2, ≤ 400 chars) with `SourceMessageIds`; only windows containing an owner message; laughter-only windows skipped | ✅ |
| 7.2 | Embeddings with bge-m3 (Approved, digest pinned) over the LlmPrompt view ("나:/상대:" lines, names as [인물N]); `VectorIndex` brute-force cosine (SIMD), stored encrypted with the dataset key (`memory.index`, purpose `memory-index`), fingerprinted and rebuilt on demand | ✅ |
| 7.3 | Hybrid retriever: dense + FTS5 BM25 (OR of particle-stripped terms) + recency + participant match; dense-only matches need floor 0.45, margin 0.1 from the best and 0.08 above the median; last filter drops excluded messages; "기록에서 찾지 못했습니다." | ✅ |
| 7.4 | Evidence cards (QUOTE, "실제 기록") rendered from the vault's LocalSearch view; "기억 검색" page with index build/progress, keyword-only fallback | ✅ |
| 7.5 | Grounding prepared: `GroundingLabel` (Quote/Fact/Inferred/Generated/Unsupported) and `QuoteVerifier` for `{{quote:id}}` references (used by Phase 6.5) | ✅ |
| 7.6 | Retrieval evaluation set (Recall@8 on synthetic questions), optional local reranker (license check first), session summaries as an extra index unit | ⬜ |

## Phase 8 — QA / Packaging ⬜
Integration, deletion, PII, parser and grounding regression evaluations · installer · code-signing and update-signing structure · privacy documentation.

## Android PoC + iPhone web viewer 🔄 (DD-026)
| # | Item | Status |
|---|---|---|
| M.1 | `src/TalkPro.Mobile` (.NET MAUI, Android arm64, API 31+): datasets, import wizard, analysis, reply drafts, keyword memory search, local AI pages on the shared Presentation ViewModels; Debug APK with bundled synthetic sample | ✅ |
| M.2 | Android Keystore KEK (AES-256-GCM, non-exportable) for dataset keys; backup and device transfer excluded (manifest + data extraction rules); clipboard marked sensitive and auto-cleared | ✅ |
| M.3 | On-device AI: ONNX Runtime GenAI + Phi-4-mini int4 (MIT, 4.9 GB) with schema-guided JSON; verified on Windows with the same engine and model (`tools/OnDeviceSmoke`) | ✅ |
| M.4 | Model downloader: HTTPS + Hugging Face host allow-list on every redirect, resume, pinned SHA-256 per file, metered-network confirmation | ✅ |
| M.5 | `src/TalkPro.Web` (Blazor WebAssembly) iPhone viewer: in-browser parse → owner → privacy defaults → analysis → keyword search; nothing uploaded or stored; `tools/WebViewerHost` with strict CSP, GET only, no logs | ✅ |
| M.6 | On-phone measurements (first draft latency, RAM, 10-minute thermal) on two reference devices; Share Intent import (needs Phase 1.8b real formats) | ⬜ |
| M.7 | 16 KB page alignment: wait for an aligned `libmat.so` (ONNX Runtime GenAI) before any store build; on-device embeddings (smaller multilingual model) for meaning search on the phone | ⬜ |

## Cross-cutting — Network policy (DD-014)
Only model download, app update and model-version checks, implemented solely in `TalkPro.Llm`, with signed allow-list + SHA-256 and tests proving payloads carry no content. No conversation data ever leaves the device.
