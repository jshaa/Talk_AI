using System.Security.Cryptography;
using TalkPro.Core.Model;
using TalkPro.Core.Parsing;
using TalkPro.Infrastructure.Vault;
using TalkPro.Ingestion.Parsing;
using TalkPro.Privacy;

namespace TalkPro.Presentation.Services;

public enum ImportFileError
{
    None,
    FileNotFound,
    ReadFailed,
    Parse,
}

/// <summary>Result of reading and parsing an export. Contains no file path.</summary>
public sealed record ImportParseResult(ImportFileError FileError, ParseFailure ParseFailure, ParsedChat? Chat)
{
    public bool Succeeded => Chat is not null;

    public override string ToString() => $"ImportParseResult {{ FileError = {FileError}, ParseFailure = {ParseFailure}, Chat = {Chat} }}";
}

public interface IImportWorkflow
{
    Task<ImportParseResult> ParseAsync(string path, CancellationToken cancellationToken);

    /// <summary>Runs the L1–L3 privacy detectors on the reviewed chat (local, worker thread).</summary>
    Task<IReadOnlyList<PiiFinding>> ScanAsync(ReviewedChat chat, CancellationToken cancellationToken);

    Task<DatasetId> SaveAsync(PrivacyReviewedImport import, string? label, CancellationToken cancellationToken);
}

/// <summary>
/// Reads the chosen export (size-checked before reading), parses it on a worker thread and stores the
/// reviewed result as a new encrypted dataset. If storing fails, the half-created dataset is deleted.
/// The file path is never logged or kept.
/// </summary>
public sealed class ImportWorkflow(ChatExportParser parser, IPrivacyScanner scanner, DatasetRepository repository, DatasetDeletionService deletion) : IImportWorkflow
{
    public Task<ImportParseResult> ParseAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Task.Run(() => Parse(path), cancellationToken);
    }

    public Task<IReadOnlyList<PiiFinding>> ScanAsync(ReviewedChat chat, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(chat);
        return Task.Run(() => scanner.Scan(chat.Messages, chat.Participants), cancellationToken);
    }

    public Task<DatasetId> SaveAsync(PrivacyReviewedImport import, string? label, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(import);
        return Task.Run(() => Save(import, label), cancellationToken);
    }

    private ImportParseResult Parse(string path)
    {
        byte[] bytes;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return new ImportParseResult(ImportFileError.FileNotFound, ParseFailure.None, null);
            }

            if (info.Length > ChatExportParser.MaxFileBytes)
            {
                return new ImportParseResult(ImportFileError.Parse, ParseFailure.TooLarge, null);
            }

            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ImportParseResult(ImportFileError.ReadFailed, ParseFailure.None, null);
        }

        try
        {
            var outcome = parser.Parse(bytes, ParseContext.ForLocalTimeZone());
            return outcome.IsSuccess
                ? new ImportParseResult(ImportFileError.None, ParseFailure.None, outcome.Chat)
                : new ImportParseResult(ImportFileError.Parse, outcome.Failure, null);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private DatasetId Save(PrivacyReviewedImport import, string? label)
    {
        var id = repository.Create(label);
        try
        {
            using var session = repository.Open(id);
            session.ImportReviewed(import);
            return id;
        }
        catch
        {
            deletion.Delete(id);
            throw;
        }
    }
}
