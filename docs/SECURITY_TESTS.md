# Security Tests — automated vs. manual

## Automated (`tests/TalkPro.Security.Tests`)
| Requirement (master prompt §24) | Test |
|---|---|
| DEK generation (256-bit, unique per dataset) | `DatasetKeyStoreTests.EveryDatasetGetsADifferentRandomKey`, `SubkeysDifferPerPurposeAndAreDeterministic` |
| KEK wrap / unwrap | `DpapiKeyProtectorTests.WrapsAndUnwraps`, `KeyStoreRoundTripsWithDpapi` (real DPAPI, Windows) |
| Key bound to its dataset / purpose | `KeyFileCopiedToAnotherDatasetIsRejected`, `DpapiKeyProtectorTests.WrongPurposeIsRejected` |
| Raw key never on disk | `KeyFileNeverContainsTheRawKey` |
| AES-GCM tamper detection | `VaultFileCryptoTests.AnyModifiedByteIsDetected` (magic, version, dataset, purpose, nonce, tag, ciphertext), `TruncatedFileIsRejected`, tampered key files |
| Wrong dataset / purpose rejected | `FileOfAnotherDatasetIsRejected`, `FileOfAnotherPurposeIsRejected` |
| Dataset delete, WAL/SHM/journal residue | `DatasetDeletionServiceTests.DeletesKeyAndEveryDatasetArtifact`, `ResidualsAreReportedAsAppRelativePaths` |
| Key unrecoverable after delete | `DestroyMakesTheKeyUnrecoverable`, `DeletesKeyAndEveryDatasetArtifact` (an outside copy of a vault file remains but has no key) |
| No plaintext temp files | `AtomicWriteLeavesNoTemporaryFiles`, `WriteFileIsAtomicAndEncrypted` |
| Working DB never on disk in plaintext (DD-017) | `VaultDatabaseTests.SnapshotIsEncryptedAndTheOnlyFileOnDisk`, `HeavyWorkloadCreatesNoTemporaryFiles` (+ control `TempFileWatcherDetectsSqliteSpillFiles`), `PlaintextSafetyPragmasAreEnforced` |
| No file export from SQL | `FileExportStatementsAreBlocked` (ATTACH, VACUUM INTO), `ExtensionLoadingIsBlocked`, `PlainVacuumIsOnlyAvailableThroughCompact` |
| Deleted rows not recoverable from snapshot | `DeletedRowsDoNotSurviveInsideTheSnapshot` |
| Dataset metadata encrypted, labels hidden | `DatasetRepositoryTests.CreateWritesKeySnapshotAndEncryptedMetadataOnly`, `ListShowsDatasetsWithDecryptedLabelButToStringHidesIt` |
| Import is all-or-nothing | `FailureInsideTheTransactionRollsBackEverything` (mutation-verified), `InvalidReviewedChatsAreRejected` |
| Raw text never modified; purpose views differ | `PrivacyReviewAndRedactionTests.ViewsDifferByPurpose`, `VaultPrivacyStorageTests.DecisionsExclusionAndMaskedSearchAreStored` |
| High-risk identifiers always masked (mutation-verified) | `KeptHighRiskIdentifierIsStillMaskedForTheLlm`, `HighRiskIdentifiersCannotBeKeptAndTopicsCannotBeMasked` |
| Excluded/deleted messages absent from search, analysis, storage | `ExcludedAndDeletedMessagesHaveNoAnalysisView`, `SensitiveMessagesAreExcludedAndDeletedOnesAreNotStored` |
| Cloud transfer refused | `CloudTransferIsAlwaysRefused` |
| Inconsistent privacy imports rejected | `InconsistentPrivacyImportsAreRejected` |
| Search input cannot inject SQL/FTS syntax | `SearchUsesFtsOrLikeAndTreatsQueriesAsData` |
| No cross-dataset state | `SeparateDatabasesNeverShareState`, `SnapshotCannotBeOpenedWithAnotherDatasetsKey`, `TamperedSnapshotIsRejected` |

## Manual check — another Windows user cannot unwrap (run before each release)
DPAPI CurrentUser scope (`DpapiKeyProtector.Scope`) cannot be exercised by a single-user test run.
1. As user A, run the app (or `KeyStoreRoundTripsWithDpapi` with a fixed temp folder) to create a dataset key under `%LOCALAPPDATA%\TalkPro\keys`.
2. Copy the `.key` file and one vault file into a folder readable by user B.
3. As user B, open them with `DatasetKeyStore` pointing at that folder.
4. **Expected**: `CryptographicException` on `Open`. Record the result in the release checklist.

## Known limits
- SSD wear-levelling, VSS snapshots and backups may keep old physical blocks. Deletion therefore relies on destroying the key (crypto-shredding), and the key itself is DPAPI-wrapped.
- Managed memory: `DatasetKey.Dispose` zeroes its buffer, but the GC may have copied it earlier. Keys are held only while needed.
- An administrator or malware running as the same Windows user can unwrap keys (DPAPI threat model). Out of scope, documented to users.
