using TalkPro.Core.Model;
using TalkPro.Infrastructure.Storage;
using TalkPro.Infrastructure.Vault;
using TalkPro.Ingestion.Formats.Synthetic;
using TalkPro.Ingestion.Parsing;
using TalkPro.Ingestion.Speakers;
using TalkPro.Privacy;
using TalkPro.Presentation.Services;
using TalkPro.Security.Keys;
using TalkPro.Tools.SampleGenerator.Output;

namespace TalkPro.Presentation.Tests;

/// <summary>End to end with real components: synthetic export file → parse → review → encrypted dataset.</summary>
public sealed class ImportWorkflowIntegrationTests : IDisposable
{
    private readonly TempDirectory _root = new();
    private readonly AppPaths _paths;
    private readonly DatasetKeyStore _keys;
    private readonly DatasetDeletionService _deletion;
    private readonly DatasetRepository _repository;
    private readonly ImportWorkflow _workflow;

    public ImportWorkflowIntegrationTests()
    {
        _paths = new AppPaths(Path.Combine(_root.Path, "app"), []);
        _paths.EnsureCreated();
        _keys = new DatasetKeyStore(_paths.Keys, new TestKeyProtector());
        _deletion = new DatasetDeletionService(_paths, _keys);
        _repository = new DatasetRepository(_paths, _keys, _deletion, TimeProvider.System);
        _workflow = new ImportWorkflow(new ChatExportParser(SyntheticFormats.All), PrivacyScanner.CreateDefault(), _repository, _deletion);
    }

    [Theory]
    [InlineData("w1/conversation.cp949.txt")]
    [InlineData("a1/conversation.utf8bom.txt")]
    [InlineData("i1/conversation.utf8.txt")]
    public async Task SyntheticExportIsImportedIntoAnEncryptedDataset(string sample)
    {
        var file = WriteSample(sample);

        var parsed = await _workflow.ParseAsync(file, CancellationToken.None);
        Assert.True(parsed.Succeeded);
        var review = SpeakerReview.Start(parsed.Chat!).ConfirmAttestation();
        var owner = review.Participants.Single(p => p.DisplayName == "UserA").Id;
        var chat = review.SelectOwner(owner).Review.Complete().Chat!;
        var findings = await _workflow.ScanAsync(chat, CancellationToken.None);
        var import = PrivacyImportBuilder.Build(chat, PrivacyReview.Start(findings, chat.Messages.Count));

        var id = await _workflow.SaveAsync(import, "SyntheticLabel", CancellationToken.None);

        var summary = Assert.Single(_repository.List());
        Assert.Equal(id, summary.Id);
        Assert.Equal(parsed.Chat!.Messages.Count, summary.MessageCount);
        using var session = _repository.Open(id);
        Assert.Equal(owner, session.OwnerId);
        Assert.Equal(chat.Messages, session.LoadMessages());
        Assert.True(session.PrivacyReviewed);
        Assert.Equal(findings.Count, session.LoadFindings().Count);
        Assert.NotEmpty(session.SearchMessageIds("중복처럼 보이는"));
    }

    [Fact]
    public async Task MissingFileIsReportedWithoutThrowing()
    {
        var result = await _workflow.ParseAsync(Path.Combine(_root.Path, "missing.txt"), CancellationToken.None);

        Assert.Equal(ImportFileError.FileNotFound, result.FileError);
        Assert.DoesNotContain(_root.Path, result.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("common/empty.utf8.txt", ParseFailure.Empty)]
    [InlineData("common/invalid-encoding.txt", ParseFailure.UnsupportedEncoding)]
    public async Task InvalidFilesFailCleanly(string sample, ParseFailure expected)
    {
        var result = await _workflow.ParseAsync(WriteSample(sample), CancellationToken.None);

        Assert.Equal(expected, result.ParseFailure);
        Assert.Null(result.Chat);
    }

    [Fact]
    public async Task FailedSaveLeavesNoDatasetBehind()
    {
        var parsed = await _workflow.ParseAsync(WriteSample("w1/single-message.utf8.txt"), CancellationToken.None);
        var review = SpeakerReview.Start(parsed.Chat!).ConfirmAttestation();
        var chat = review.SelectOwner(review.Participants[0].Id).Review.Complete().Chat!;
        var invalid = PrivacyImportBuilder.Build(chat with { OwnerId = new ParticipantId("p_99") }, PrivacyReview.Start([], chat.Messages.Count));

        await Assert.ThrowsAsync<ArgumentException>(() => _workflow.SaveAsync(invalid, null, CancellationToken.None));

        Assert.Empty(_repository.List());
        Assert.Empty(Directory.EnumerateFiles(_paths.Keys));
    }

    [Fact]
    public async Task SensitiveMessagesAreExcludedAndDeletedOnesAreNotStored()
    {
        var parsed = await _workflow.ParseAsync(WriteSample("w1/single-message.utf8.txt"), CancellationToken.None);
        var review = SpeakerReview.Start(parsed.Chat!).ConfirmAttestation();
        var chat = review.SelectOwner(review.Participants[0].Id).Review.Complete().Chat!;
        var baseMessage = chat.Messages[0];
        chat = chat with
        {
            Messages =
            [
                baseMessage,
                baseMessage with { Id = new MessageId("m9000001"), SourceLine = 90, Text = "합성 가족 항암 치료 소식" },
                baseMessage with { Id = new MessageId("m9000002"), SourceLine = 91, Text = "합성 연락처 010-1234-5678 삭제할 것" },
            ],
        };

        var findings = await _workflow.ScanAsync(chat, CancellationToken.None);
        var privacy = PrivacyReview.Start(findings, chat.Messages.Count);
        privacy = privacy.SetActionForType(PiiType.Phone, PrivacyAction.Delete).Review;
        var id = await _workflow.SaveAsync(PrivacyImportBuilder.Build(chat, privacy), null, CancellationToken.None);

        using var session = _repository.Open(id);
        Assert.Equal([new MessageId("m9000001")], session.LoadExcludedMessageIds());
        Assert.DoesNotContain(session.LoadMessages(), m => m.Id == new MessageId("m9000002"));
        Assert.Empty(session.SearchMessageIds("항암 치료"));
        Assert.Empty(session.SearchMessageIds("010-1234"));
        Assert.NotEmpty(session.SearchMessageIds("합성 테스트 메시지 001"));
    }

    public void Dispose() => _root.Dispose();

    private string WriteSample(string relativePath)
    {
        var file = GoldenCorpusGenerator.Generate().Single(f => f.RelativePath == relativePath);
        var path = Path.Combine(_root.Path, "sample-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllBytes(path, file.Content.AsSpan());
        return path;
    }
}
