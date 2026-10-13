using System.Globalization;
using System.Security.Cryptography;
using TalkPro.Core.Model;
using TalkPro.Core.Parsing;
using TalkPro.Infrastructure.Vault;
using TalkPro.Ingestion.Parsing;
using TalkPro.Privacy;
using TalkPro.Presentation.Abstractions;
using TalkPro.Presentation.Services;
using TalkPro.Security.Keys;

namespace TalkPro.Presentation.Tests;

internal sealed class FakeDialogs : IDialogService
{
    public string? NextFile { get; set; } = "synthetic-path.txt";

    public bool ConfirmResult { get; set; } = true;

    public List<string> ConfirmMessages { get; } = [];

    public string? PickExportFile() => NextFile;

    public bool Confirm(string title, string message)
    {
        ConfirmMessages.Add(message);
        return ConfirmResult;
    }
}

internal sealed class FakeWorkflow : IImportWorkflow
{
    public ImportParseResult NextParse { get; set; } = new(ImportFileError.None, ParseFailure.None, SyntheticChat.Create(("UserA", 0), ("User C", 1)));

    public bool FailSave { get; set; }

    public int ParseCalls { get; private set; }

    public PrivacyReviewedImport? Saved { get; private set; }

    public string? SavedLabel { get; private set; }

    public bool FailScan { get; set; }

    public Task<ImportParseResult> ParseAsync(string path, CancellationToken cancellationToken)
    {
        ParseCalls++;
        return Task.FromResult(NextParse);
    }

    public Task<IReadOnlyList<PiiFinding>> ScanAsync(ReviewedChat chat, CancellationToken cancellationToken)
    {
        if (FailScan)
        {
            throw new InvalidOperationException("synthetic failure");
        }

        return Task.FromResult(PrivacyScanner.CreateDefault().Scan(chat.Messages, chat.Participants));
    }

    public Task<DatasetId> SaveAsync(PrivacyReviewedImport import, string? label, CancellationToken cancellationToken)
    {
        if (FailSave)
        {
            throw new IOException("synthetic failure");
        }

        Saved = import;
        SavedLabel = label;
        return Task.FromResult(DatasetId.New());
    }
}

internal sealed class FakeCatalog : IDatasetCatalog
{
    public List<DatasetSummary> Datasets { get; } = [];

    public List<DatasetId> Deleted { get; } = [];

    public IReadOnlyList<DatasetSummary> List() => [.. Datasets];

    public DatasetDeletionReport Delete(DatasetId id)
    {
        Deleted.Add(id);
        Datasets.RemoveAll(d => d.Id == id);
        return new DatasetDeletionReport(id, true, true, 3, []);
    }

    public IReadOnlyList<DatasetDeletionReport> DeleteAll() => [.. Datasets.ToList().Select(d => Delete(d.Id))];
}

/// <summary>AES-GCM stand-in for DPAPI so integration tests run without touching the user's key store.</summary>
internal sealed class TestKeyProtector : IKeyProtector
{
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

    public KeyProtectorKind Kind => KeyProtectorKind.WindowsDpapi;

    public byte[] Protect(ReadOnlySpan<byte> secret, ReadOnlySpan<byte> purpose)
    {
        var output = new byte[28 + secret.Length];
        RandomNumberGenerator.Fill(output.AsSpan(0, 12));
        using var aes = new AesGcm(_key, 16);
        aes.Encrypt(output.AsSpan(0, 12), secret, output.AsSpan(28), output.AsSpan(12, 16), purpose);
        return output;
    }

    public byte[] Unprotect(ReadOnlySpan<byte> protectedSecret, ReadOnlySpan<byte> purpose)
    {
        var plain = new byte[protectedSecret.Length - 28];
        using var aes = new AesGcm(_key, 16);
        aes.Decrypt(protectedSecret[..12], protectedSecret[28..], protectedSecret.Slice(12, 16), plain, purpose);
        return plain;
    }
}

internal static class SyntheticChat
{
    public static readonly DateTimeOffset T0 = new(2026, 1, 1, 9, 0, 0, TimeSpan.FromHours(9));

    /// <summary>Synthetic parsed chat: (speaker, day offset); speakers get p_NN in order of first appearance.</summary>
    public static ParsedChat Create(params (string Speaker, int Day)[] lines)
    {
        var ids = new Dictionary<string, ParticipantId>(StringComparer.Ordinal);
        var participants = new List<Participant>();
        var messages = new List<ChatMessage>();
        for (var i = 0; i < lines.Length; i++)
        {
            var (speaker, day) = lines[i];
            if (!ids.TryGetValue(speaker, out var id))
            {
                id = new ParticipantId(string.Create(CultureInfo.InvariantCulture, $"p_{ids.Count + 1:D2}"));
                ids.Add(speaker, id);
                participants.Add(new Participant(id, speaker, ParticipantConsent.ContextOnly));
            }

            messages.Add(new ChatMessage(
                new MessageId(string.Create(CultureInfo.InvariantCulture, $"m{i + 1:D7}")),
                T0.AddDays(day),
                id,
                MessageKind.Text,
                string.Create(CultureInfo.InvariantCulture, $"합성 테스트 메시지 {i + 1:D3} {new string('가', 100)}"),
                i + 1));
        }

        return new ParsedChat(new ChatFormatId("synthetic.windows.v1"), SourceEncoding.Utf8, participants, messages, [], new ParseStatistics(lines.Length, lines.Length, 0, 0, 0, 0, 0, 0));
    }
}

internal sealed class TempDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "talkpro-ui", Guid.NewGuid().ToString("N"));

    public TempDirectory() => Directory.CreateDirectory(Path);

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}

internal sealed class FakeAnalytics : IAnalyticsService
{
    public AnalyticsResult Next { get; set; } = new(AnalyticsError.NotPrivacyReviewed, null);

    public List<DatasetId> Requests { get; } = [];

    public Task<AnalyticsResult> AnalyzeAsync(DatasetId id, CancellationToken cancellationToken)
    {
        Requests.Add(id);
        return Task.FromResult(Next);
    }
}
