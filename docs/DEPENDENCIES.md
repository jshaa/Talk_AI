# Dependency Register

Rule: a package is added to `Directory.Packages.props` only after it is recorded here.
Unclear license ⇒ not a production dependency. Checked: 2026-10-02 (nuget.org nuspec).

| Package | Version | Scope | License | Maintenance | Commercial redistribution | Native code | Used by |
|---|---|---|---|---|---|---|---|
| CommunityToolkit.Mvvm | 8.4.2 | Production | MIT | Active (.NET Foundation) | Yes (include notice) | No | App |
| Microsoft.Extensions.Hosting | 10.0.12 | Production | MIT | Active (Microsoft, .NET 10 LTS) | Yes | No | App |
| Microsoft.Data.Sqlite | 10.0.12 | Production | MIT | Active (Microsoft, EF Core repo) | Yes, free | Via bundle below | Infrastructure (`VaultDatabase`) |
| SQLitePCLRaw.bundle_e_sqlite3 / core / provider / lib.e_sqlite3 (+android/ios) | 2.1.12 (transitive, pinned by Microsoft.Data.Sqlite) | Production | Apache-2.0 (bindings and native builds); SQLite engine 3.53.3 is public domain | Active (Eric Sink / SourceGear) | Yes, free | **Yes**: e_sqlite3 native library per RID, verified to export `sqlite3_serialize`/`sqlite3_deserialize` and to be compiled with FTS5 | Infrastructure |
| Microsoft.Extensions.DependencyInjection.Abstractions | 10.0.12 | Production | MIT | Active | Yes | No | Feature modules |
| Microsoft.Extensions.Logging | 10.0.12 | Production | MIT | Active | Yes | No | Infrastructure |
| Microsoft.Extensions.Logging.Abstractions | 10.0.12 | Production | MIT | Active | Yes | No | Llm (`[LoggerMessage]` in `OllamaClient`); transitive elsewhere |
| System.Security.Cryptography.ProtectedData | 10.0.12 | Production | MIT | Active (Microsoft, dotnet/runtime; published 2026-09-08) | Yes, free | No (calls Windows crypt32 DPAPI; Windows-only at runtime) | Security (`DpapiKeyProtector`) |
| Microsoft.ML.OnnxRuntimeGenAI | 0.17.1 | Production (Llm, Mobile) | MIT (verified in the repo LICENSE, 2026-10-13) | Active (Microsoft) | Yes, free | **Yes**: onnxruntime-genai + libmat per RID (win-x64, android-arm64 …). libmat.so for Android is 4 KB page aligned (XA0141, DD-026) | Llm (`OnnxGenAiClient`), Mobile |
| Microsoft.ML.OnnxRuntime | 1.30.0 (pinned to GenAI's dependency) | Production (transitive) | MIT | Active (Microsoft) | Yes, free | **Yes**: onnxruntime per RID (16 KB aligned on Android) | via OnnxRuntimeGenAI |
| Microsoft.Maui.Controls | 10.0.20 (matches the installed MAUI workload) | Production (Mobile) | MIT | Active (Microsoft, .NET MAUI) | Yes, free | Via the Android workload | Mobile (Android PoC shell) |
| Microsoft.AspNetCore.Components.WebAssembly | 10.0.12 | Production (Web viewer) | MIT | Active (Microsoft) | Yes, free | .NET WebAssembly runtime (no native packages) | Web (iPhone test viewer) |
| Microsoft.AspNetCore.Components.WebAssembly.DevServer | 10.0.12 | Development only (PrivateAssets) | MIT | Active (Microsoft) | Not shipped | No | Web (`dotnet run` during development) |
| xunit.v3 | 4.0.1 | Test only | Apache-2.0 | Active | Not shipped | No | tests/* |

Runtime/tooling (not packages): .NET SDK 10.0.401 (MIT), WPF (MIT, part of Windows Desktop runtime).

## Local AI runtime and models (not NuGet packages, installed by the user) — DD-023
No package was added for Phase 5: the Ollama client uses only `System.Net.Http` and `System.Text.Json` from the .NET BCL. The on-device runtime (DD-026) adds ONNX Runtime GenAI (table above).

| Item | License (checked 2026-10-07) | Cost | How it is used | Registry status |
|---|---|---|---|---|
| Ollama 0.35.x (runtime) | MIT | Free | Installed separately by the user; the app talks to it on 127.0.0.1 only and never bundles it | — |
| qwen3:8b (Qwen3 8B, Q4_K_M, 5.2 GB) | Apache-2.0 | Free, commercial use allowed | **Default chat model** (owner decision 2026-10-07): reply drafts (Phase 6); fits an 8 GB GPU | **Approved**: digest `500a1f06…4b2b8b41` pinned 2026-10-08 |
| gemma3:4b (Gemma 3 4B) | Gemma Terms of Use (custom; Prohibited Use Policy passes through to users) | Free | Fallback chat model in Debug only | LicenseReview: needs legal review before Release; digest pinned |
| Phi-4-mini-instruct ONNX, cpu-int4-rtn-block-32-acc-level-4 (Microsoft, 3.8B, 4.9 GB) | MIT | Free, commercial use allowed | **On-device chat model** on Android (DD-026), downloaded by the app from huggingface.co at a pinned commit; every file SHA-256 pinned | **Approved** (owner choice 2026-10-13) |
| bge-m3 (BAAI BGE-M3, 567M, 1024-dim, 1.2 GB) | MIT | Free, commercial use allowed | **Default embedding model** for memory search (Phase 7, owner approval 2026-10-13) | **Approved**: digest `79076464…2146bab` pinned 2026-10-13 |
| Kanana 1.5 2.1B/8B instruct (Kakao) | Apache-2.0 | Free, commercial use allowed | Strong Korean candidate | Not listed yet: no verified official Ollama/GGUF source |

On Windows, models are downloaded by Ollama itself (`ollama pull`), so the desktop app makes no internet request. The Android app downloads only its own model files (DD-014 allowed use) from allow-listed Hugging Face hosts, with no user data in the requests.

## Candidates evaluated, not adopted (yet)
| Package | Finding | Status |
|---|---|---|
| SQLitePCLRaw.bundle_e_sqlcipher 2.1.11 | Apache-2.0, but the package states its SQLCipher builds are "unofficial and unsupported" | Rejected (DD-007: no SQLCipher) |

Why ProtectedData is a package: DPAPI (`ProtectedData`) is not part of the .NET 10 shared framework. Hand-written P/Invoke to `CryptProtectData` was rejected as riskier than Microsoft's maintained wrapper.

SQLite notes (Phase 2.4):
- The 2.1.12 line is used because Microsoft.Data.Sqlite 10.0.12 depends on SQLitePCLRaw.core 2.1.12. The 3.x line is not mixed in.
- The native build has `TEMP_STORE=1` (temp files by default), so `PRAGMA temp_store=MEMORY` is mandatory and enforced by tests.
