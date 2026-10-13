using TalkPro.Core.Model;
using TalkPro.Infrastructure.Storage;
using TalkPro.Infrastructure.Vault;
using TalkPro.Ingestion.Formats.Synthetic;
using TalkPro.Ingestion.Parsing;
using TalkPro.Ingestion.Speakers;
using TalkPro.Memory;
using TalkPro.Persona.Analysis;
using TalkPro.Privacy;
using TalkPro.Presentation.Services;
using TalkPro.Presentation.ViewModels;
using TalkPro.Security.Keys;

namespace TalkPro.Presentation.Tests;

public sealed class AnalyticsViewModelTests
{
    private readonly FakeAnalytics _service = new();

    [Theory]
    [InlineData(AnalyticsError.NotPrivacyReviewed, "개인정보 검토 전에")]
    [InlineData(AnalyticsError.NoOwner, "본인(나)")]
    [InlineData(AnalyticsError.Unreadable, "열 수 없습니다")]
    public async Task ErrorsAreShownWithoutAReport(AnalyticsError error, string expected)
    {
        _service.Next = new AnalyticsResult(error, null);
        var vm = new AnalyticsViewModel(_service);

        await vm.LoadAsync(DatasetId.New(), "합성 라벨");

        Assert.Contains(expected, vm.ErrorMessage, StringComparison.Ordinal);
        Assert.False(vm.HasReport);
        Assert.False(vm.IsBusy);
        Assert.Empty(vm.Overview);
    }

    [Fact]
    public async Task ReportIsShownAsRowsAndBars()
    {
        _service.Next = new AnalyticsResult(AnalyticsError.None, SampleReport());
        var vm = new AnalyticsViewModel(_service);

        await vm.LoadAsync(DatasetId.New(), "합성 라벨");

        Assert.Null(vm.ErrorMessage);
        Assert.True(vm.HasReport);
        Assert.Equal("합성 라벨", vm.Title);
        Assert.Equal(24, vm.ByHour.Count);
        Assert.Equal(7, vm.ByWeekday.Count);
        Assert.Equal(1.0, vm.ByHour[21].Ratio);
        Assert.Equal(AnalyticsViewModel.BarMaxWidth, vm.ByHour[21].Width);
        Assert.Equal(0.5, vm.ByHour[9].Ratio);
        Assert.Contains(vm.Overview, r => r.Label == "내 답장 시간" && r.Value.Contains("3분", StringComparison.Ordinal));
        Assert.Contains(vm.Style, r => r.Label == "말투" && r.Value.Contains("반말 75%", StringComparison.Ordinal));
        Assert.Equal("영화", vm.Keywords[0].Label);
        Assert.Equal("UserB", Assert.Single(vm.Relationships).Name);
        Assert.Equal("2026 Q1", Assert.Single(vm.Periods).Period);
    }

    [Fact]
    public void ScopeNoticeExcludesCounterpartEmotionAnalysis()
    {
        var vm = new AnalyticsViewModel(_service);

        Assert.Contains("상대방의 감정·심리·애정도는 분석하지 않습니다", vm.ScopeNotice, StringComparison.Ordinal);
        Assert.Contains("저장되지 않습니다", vm.ScopeNotice, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShellOpensAnalysisForTheSelectedDataset()
    {
        var catalog = new FakeCatalog();
        var target = new DatasetSummary(DatasetId.New(), DatasetState.Ready, "합성 라벨", DateTimeOffset.UnixEpoch, null, 2, 10, null);
        catalog.Datasets.Add(target);
        catalog.Datasets.Add(new DatasetSummary(DatasetId.New(), DatasetState.Damaged, null, null, null, 0, 0, null));
        var datasets = new DatasetsViewModel(catalog, new FakeDialogs());
        var shell = new MainViewModel(datasets, () => throw new InvalidOperationException(), () => new AnalyticsViewModel(_service), () => throw new InvalidOperationException(), () => throw new InvalidOperationException(), () => throw new InvalidOperationException());

        Assert.False(datasets.AnalyzeSelectedCommand.CanExecute(null));
        datasets.Selected = datasets.Items[1];
        Assert.False(datasets.AnalyzeSelectedCommand.CanExecute(null));
        datasets.Selected = datasets.Items[0];
        datasets.AnalyzeSelectedCommand.Execute(null);
        await shell.PendingAnalysis!;

        var page = Assert.IsType<AnalyticsViewModel>(shell.CurrentPage);
        Assert.Equal("합성 라벨", page.Title);
        Assert.Equal([target.Id], _service.Requests);
        Assert.DoesNotContain(shell.UpcomingSections, s => s.Title == "대화 분석");
    }

    private static CommunicationReport SampleReport()
    {
        var hours = new int[24];
        hours[9] = 2;
        hours[21] = 4;
        var mix = new RegisterMix(0, 0.25, 0.75, 0);
        return new CommunicationReport(
            10, 6, 2, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(3), 2,
            new DistributionStats(6, 8.5, 7, 15), 1.5, hours, [0, 6, 0, 0, 0, 0, 0],
            TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(40), 4,
            0.2, 0.1, [new TermCount("😀", 1)], 0.5, 0, 3, 0.1, 0, 0, 0.2, mix,
            [new RelationshipStyle(new ParticipantId("p_02"), "UserB", 4, 8, 0.5, mix)],
            [new TermCount("영화", 3), new TermCount("주말", 1)],
            [new MonthlyActivity("1970-01", 6, 10, 2)],
            [new BusyDay(new DateOnly(1970, 1, 1), 7)],
            [new PeriodStyle("2026 Q1", 6, 8.5, 0.5, 0.1, mix)]);
    }
}

/// <summary>Real vault + privacy + sessionizer: analysis sees only the Analysis view.</summary>
public sealed class AnalyticsIntegrationTests : IDisposable
{
    private readonly TempDirectory _root = new();
    private readonly DatasetRepository _repository;
    private readonly ImportWorkflow _workflow;
    private readonly AnalyticsService _analytics;

    public AnalyticsIntegrationTests()
    {
        var paths = new AppPaths(Path.Combine(_root.Path, "app"), []);
        paths.EnsureCreated();
        var keys = new DatasetKeyStore(paths.Keys, new TestKeyProtector());
        var deletion = new DatasetDeletionService(paths, keys);
        _repository = new DatasetRepository(paths, keys, deletion, TimeProvider.System);
        _workflow = new ImportWorkflow(new ChatExportParser(SyntheticFormats.All), PrivacyScanner.CreateDefault(), _repository, deletion);
        _analytics = new AnalyticsService(_repository, new ConversationSessionizer());
    }

    [Theory]
    [InlineData(PrivacyAction.Mask, false)]
    [InlineData(PrivacyAction.Keep, true)]
    public async Task ExcludedMessagesAreNotCountedAndMaskedNamesAreNotKeywords(PrivacyAction nameAction, bool nameIsKeyword)
    {
        var id = await ImportAsync(nameAction);

        var result = await _analytics.AnalyzeAsync(id, CancellationToken.None);

        var report = Assert.IsType<CommunicationReport>(result.Report);
        Assert.Equal(AnalyticsError.None, result.Error);
        Assert.Equal(3, report.TotalMessages);
        Assert.Equal(2, report.OwnerMessages);
        Assert.DoesNotContain(report.Keywords, k => k.Term.Contains("항암", StringComparison.Ordinal));
        Assert.DoesNotContain(report.Keywords, k => k.Term.Contains("인물", StringComparison.Ordinal));
        Assert.Equal(nameIsKeyword, report.Keywords.Any(k => k.Term == "가나다"));
        Assert.Contains(report.Keywords, k => k.Term == "영화" && k.Count == 2);
    }

    [Fact]
    public async Task DatasetWithoutPrivacyReviewIsRefused()
    {
        var id = _repository.Create("합성");

        var result = await _analytics.AnalyzeAsync(id, CancellationToken.None);

        Assert.Equal(AnalyticsError.NotPrivacyReviewed, result.Error);
        Assert.Null(result.Report);
    }

    [Fact]
    public async Task MissingDatasetIsUnreadable()
    {
        var result = await _analytics.AnalyzeAsync(DatasetId.New(), CancellationToken.None);

        Assert.Equal(AnalyticsError.Unreadable, result.Error);
    }

    public void Dispose() => _root.Dispose();

    private async Task<DatasetId> ImportAsync(PrivacyAction nameAction)
    {
        var parsed = SyntheticChat.Create(("UserA", 0), ("가나다", 0), ("UserA", 0), ("UserA", 0));
        var review = SpeakerReview.Start(parsed).ConfirmAttestation();
        var owner = review.Participants.Single(p => p.DisplayName == "UserA").Id;
        var chat = review.SelectOwner(owner).Review.Complete().Chat!;
        string[] texts = ["가나다랑 영화 봤어", "재밌었지", "가나다 영화 또 보자", "합성 가족 항암 치료 소식"];
        chat = chat with { Messages = [.. chat.Messages.Select((m, i) => m with { Text = texts[i] })] };

        var findings = await _workflow.ScanAsync(chat, CancellationToken.None);
        var privacy = PrivacyReview.Start(findings, chat.Messages.Count).SetActionForType(PiiType.Person, nameAction).Review;
        return await _workflow.SaveAsync(PrivacyImportBuilder.Build(chat, privacy), null, CancellationToken.None);
    }
}
