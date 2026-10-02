# Dependency Register

Rule: a package is added to `Directory.Packages.props` only after it is recorded here.
Unclear license ⇒ not a production dependency. Checked: 2026-10-02 (nuget.org nuspec).

| Package | Version | Scope | License | Maintenance | Commercial redistribution | Native code | Used by |
|---|---|---|---|---|---|---|---|
| CommunityToolkit.Mvvm | 8.4.2 | Production | MIT | Active (.NET Foundation) | Yes (include notice) | No | App |
| Microsoft.Extensions.Hosting | 10.0.12 | Production | MIT | Active (Microsoft, .NET 10 LTS) | Yes | No | App |
| Microsoft.Extensions.DependencyInjection.Abstractions | 10.0.12 | Production | MIT | Active | Yes | No | Feature modules |
| Microsoft.Extensions.Logging | 10.0.12 | Production | MIT | Active | Yes | No | Infrastructure |
| Microsoft.Extensions.Logging.Abstractions | 10.0.12 | Production (transitive) | MIT | Active | Yes | No | — |
| xunit.v3 | 4.0.1 | Test only | Apache-2.0 | Active | Not shipped | No | tests/* |

Runtime/tooling (not packages): .NET SDK 10.0.401 (MIT), WPF (MIT, part of Windows Desktop runtime).

## Candidates evaluated, not adopted (yet)
| Package | Finding | Status |
|---|---|---|
| SQLitePCLRaw.bundle_e_sqlcipher 2.1.11 | Apache-2.0, but the package states its SQLCipher builds are "unofficial and unsupported"; stuck on 2.x while SQLitePCLRaw moved to 3.x | See DD-007 |
| Microsoft.Data.Sqlite.Core 10.0.12 | MIT | Pending DD-007 |
| System.Security.Cryptography.ProtectedData 10.0.12 | MIT, DPAPI wrapper | Adopt in Phase 2 |
