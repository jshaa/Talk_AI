using Microsoft.Extensions.Logging.Abstractions;
using TalkPro.Core.Llm;
using TalkPro.Core.Model;
using TalkPro.Llm;
using TalkPro.Llm.Ollama;
using TalkPro.Memory;
using TalkPro.Presentation.Services;
using TalkPro.Presentation.ViewModels;
using TalkPro.Privacy;

namespace TalkPro.Presentation.Tests;

/// <summary>Real vault, privacy, sessionizer and chunker; a deterministic "topic" embedder stands in for bge-m3.</summary>
public sealed class MemorySearchServiceTests : IDisposable
{
    private static readonly (string Speaker, string Text)[] Lines =
    [
        ("가나다", "이번 주말에 바다 보러 갈래?"),
        ("UserA", "좋아 ㅋㅋ 강릉 가자"),
        ("가나다", "기차표 내가 끊을게"),
        ("UserA", "고마워 ㅋㅋ"),
        ("가나다", "합성 잡담 하나"),
        ("UserA", "합성 잡담 둘"),
        ("가나다", "합성 잡담 셋"),
        ("UserA", "합성 가족 항암 치료 소식"),
        ("가나다", "과제 다 했어?"),
        ("UserA", "아직 반밖에 못함 ㅠ"),
        ("가나다", "라마바도 과제 같이 하재"),
        ("라마바", "합성 인사"),
        ("UserA", "그래 같이 하자"),
        ("가나다", "주민번호 900101-1234568 적어야 돼?"),
        ("UserA", "아니 안 적어도 돼"),
    ];

    private readonly SyntheticVault _vault = new();
    private readonly TopicEmbedder _embedder = new();
    private readonly MemorySearchService _service;

    public MemorySearchServiceTests() =>
        _service = new MemorySearchService(_vault.Repository, new ConversationSessionizer(), PrivacyScanner.CreateDefault(), _embedder);

    [Fact]
    public async Task WithoutAnIndexKeywordSearchShowsStoredRecordsOnly()
    {
        var id = await _vault.ImportAsync(Lines);

        var status = await _service.GetStatusAsync(id, CancellationToken.None);
        var found = await _service.SearchAsync(id, "강릉", CancellationToken.None);
        var none = await _service.SearchAsync(id, "제주도 비행기", CancellationToken.None);

        Assert.Equal((MemoryIndexState.Missing, 3), (status.State, status.Items));
        Assert.Equal(SearchMode.KeywordOnly, found.Mode);
        var card = Assert.Single(found.Cards);
        Assert.Equal(GroundingLabel.Quote, EvidenceCard.Label);
        Assert.Contains(card.Lines, l => l is { Speaker: "UserA", Text: "좋아 ㅋㅋ 강릉 가자", IsOwner: true });
        Assert.Contains(card.Lines, l => l is { Speaker: "가나다", IsOwner: false });
        Assert.True(none.NotFound);
        Assert.Empty(_embedder.Inputs);
    }

    [Fact]
    public async Task ExcludedMessagesNeverAppearAndHighRiskNumbersAreMasked()
    {
        var id = await _vault.ImportAsync(Lines);

        var all = await _service.SearchAsync(id, "합성 잡담 하자 주민번호", CancellationToken.None);

        var lines = all.Cards.SelectMany(c => c.Lines).ToList();
        Assert.NotEmpty(lines);
        Assert.DoesNotContain(lines, l => l.Text.Contains("항암", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Text.Contains("900101", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Text.Contains("[주민등록번호]", StringComparison.Ordinal));
    }

    [Fact]
    public async Task IndexEmbedsOnlyRedactedTextAndEnablesMeaningSearch()
    {
        var id = await _vault.ImportAsync(Lines);

        var built = await _service.BuildIndexAsync(id, null, CancellationToken.None);
        var status = await _service.GetStatusAsync(id, CancellationToken.None);
        var result = await _service.SearchAsync(id, "동해 여행 갔던 거", CancellationToken.None);

        Assert.Equal((MemoryError.None, MemoryIndexState.Ready, 3, "fake-embed"), (built.Error, built.State, built.Items, built.Model));
        Assert.Equal(MemoryIndexState.Ready, status.State);

        var indexed = _embedder.Inputs.Take(3).ToList();
        Assert.All(indexed, t => Assert.All(t.Split('\n'), line => Assert.True(line.StartsWith("나: ", StringComparison.Ordinal) || line.StartsWith("상대: ", StringComparison.Ordinal))));
        var all = string.Join("\n", _embedder.Inputs);
        Assert.DoesNotContain("가나다", all, StringComparison.Ordinal);
        Assert.DoesNotContain("라마바", all, StringComparison.Ordinal);
        Assert.DoesNotContain("항암", all, StringComparison.Ordinal);
        Assert.DoesNotContain("900101", all, StringComparison.Ordinal);
        Assert.Contains("상대: [인물1]도 과제 같이 하재", all, StringComparison.Ordinal);

        // No keyword of the query occurs in the chat: only the meaning (topic) match finds the 강릉 trip.
        Assert.Equal(SearchMode.Hybrid, result.Mode);
        var card = Assert.Single(result.Cards);
        Assert.Contains(card.Lines, l => l.Text == "좋아 ㅋㅋ 강릉 가자");
    }

    [Fact]
    public async Task QueriesAreRedactedWithTheSamePseudonymsAsTheIndex()
    {
        var id = await _vault.ImportAsync(Lines);
        await _service.BuildIndexAsync(id, null, CancellationToken.None);

        var result = await _service.SearchAsync(id, "라마바 가나다 과제", CancellationToken.None);

        // 라마바 is [인물1] in the indexed chat; a fresh map would number the query from its end and swap them.
        Assert.Equal("[인물1] [인물2] 과제", _embedder.Inputs[^1]);
        Assert.Contains(result.Cards[0].Lines, l => l.Speaker == "라마바");
    }

    [Fact]
    public async Task IndexFileIsEncryptedAndAStaleOrForeignIndexFallsBackToKeywords()
    {
        var id = await _vault.ImportAsync(Lines);
        await _service.BuildIndexAsync(id, null, CancellationToken.None);
        var file = Directory.EnumerateFiles(Path.GetDirectoryName(Directory.EnumerateFiles(_vault.Root, "dataset.meta", SearchOption.AllDirectories).Single())!, "memory.index").Single();
        var bytes = File.ReadAllBytes(file);
        Assert.Equal(-1, bytes.AsSpan().IndexOf("m0000002"u8));

        _embedder.Digest = "other-model-version";
        var foreign = await _service.SearchAsync(id, "동해 여행", CancellationToken.None);
        Assert.Equal(SearchMode.KeywordOnly, foreign.Mode);
        Assert.True(foreign.NotFound);

        using (var session = _vault.Repository.Open(id))
        {
            session.SaveMemoryIndex(new VectorIndex("fake-embed", "d1", "old-fingerprint", 3).Serialize());
        }

        Assert.Equal(MemoryIndexState.Stale, (await _service.GetStatusAsync(id, CancellationToken.None)).State);
        Assert.Equal(SearchMode.KeywordOnly, (await _service.SearchAsync(id, "강릉", CancellationToken.None)).Mode);

        bytes[^1] ^= 1;
        File.WriteAllBytes(file, bytes);
        Assert.Equal(MemoryIndexState.Missing, (await _service.GetStatusAsync(id, CancellationToken.None)).State);
    }

    [Fact]
    public async Task EmbeddingFailureIsReportedAndNothingIsSaved()
    {
        var id = await _vault.ImportAsync(Lines);
        _embedder.Fail = LlmError.ModelNotInstalled;
        var progress = new List<double>();

        var built = await _service.BuildIndexAsync(id, new Progress<double>(progress.Add), CancellationToken.None);

        Assert.Equal((MemoryError.Model, LlmError.ModelNotInstalled), (built.Error, built.LlmError));
        Assert.Equal(MemoryIndexState.Missing, (await _service.GetStatusAsync(id, CancellationToken.None)).State);
    }

    [Fact]
    public async Task InputsAndDatasetsAreValidated()
    {
        var unreviewed = _vault.Repository.Create("합성");

        Assert.Equal(MemoryError.EmptyQuery, (await _service.SearchAsync(unreviewed, " ", CancellationToken.None)).Error);
        Assert.Equal(MemoryError.QueryTooLong, (await _service.SearchAsync(unreviewed, new string('가', MemorySearchService.MaxQueryLength + 1), CancellationToken.None)).Error);
        Assert.Equal(MemoryError.NotPrivacyReviewed, (await _service.SearchAsync(unreviewed, "강릉", CancellationToken.None)).Error);
        Assert.Equal(MemoryError.Unreadable, (await _service.GetStatusAsync(DatasetId.New(), CancellationToken.None)).Error);
        Assert.Equal(MemoryError.NotPrivacyReviewed, (await _service.BuildIndexAsync(unreviewed, null, CancellationToken.None)).Error);
        Assert.Empty(_embedder.Inputs);
    }

    public void Dispose() => _vault.Dispose();

    /// <summary>
    /// Vectors from topic words, so tests control similarity: travel words → axis 0, homework words → axis 1,
    /// plus a small constant. Records every input (redacted text) it receives.
    /// </summary>
    private sealed class TopicEmbedder : IEmbeddingClient
    {
        private static readonly string[] Travel = ["강릉", "바다", "동해", "여행", "기차"];
        private static readonly string[] Homework = ["과제", "숙제"];

        public List<string> Inputs { get; } = [];

        public string Digest { get; set; } = "d1";

        public LlmError Fail { get; set; } = LlmError.None;

        public Task<EmbeddingResult> EmbedAsync(IReadOnlyList<RedactedText> texts, CancellationToken cancellationToken)
        {
            if (Fail != LlmError.None)
            {
                return Task.FromResult(EmbeddingResult.Failed(Fail));
            }

            Inputs.AddRange(texts.Select(t => t.Value));
            IReadOnlyList<float[]> vectors = [.. texts.Select(t => new[] { Count(t.Value, Travel), Count(t.Value, Homework), 0.1f })];
            return Task.FromResult(new EmbeddingResult(LlmError.None, vectors, new ModelProvenance("fake-embed", Digest)));
        }

        private static float Count(string text, string[] words) => words.Count(w => text.Contains(w, StringComparison.Ordinal));
    }
}

public sealed class MemorySearchViewModelTests
{
    private readonly FakeCatalog _catalog = new();
    private readonly FakeMemory _memory = new();

    public MemorySearchViewModelTests() =>
        _catalog.Datasets.Add(new DatasetSummary(DatasetId.New(), DatasetState.Ready, "합성 라벨", DateTimeOffset.UnixEpoch, null, 2, 10, null));

    [Fact]
    public async Task StatusBuildAndSearchAreShown()
    {
        var vm = new MemorySearchViewModel(_catalog, _memory);
        Assert.False(vm.SearchCommand.CanExecute(null));

        vm.SelectedDataset = vm.Datasets[0];
        await vm.PendingStatus!;
        Assert.Contains("기억 색인이 없습니다", vm.StatusText, StringComparison.Ordinal);
        Assert.Equal("기억 색인 만들기", vm.BuildButtonText);

        await vm.BuildIndexCommand.ExecuteAsync(null);
        Assert.Contains("항목 12개 · fake-embed", vm.StatusText, StringComparison.Ordinal);
        Assert.Equal("기억 색인 다시 만들기", vm.BuildButtonText);

        vm.Query = "합성 검색어";
        await vm.SearchCommand.ExecuteAsync(null);
        Assert.Equal("합성 검색어", _memory.LastQuery);
        Assert.Single(vm.Cards);
        Assert.Equal("검색 방식: 뜻 + 단어", vm.ModeText);
        Assert.Contains("모두 실제 기록 원문입니다", vm.ResultText, StringComparison.Ordinal);
        Assert.Contains("AI가 만든 문장이 아니라", vm.Notice, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NothingFoundIsSaidPlainly()
    {
        _memory.Cards = [];
        var vm = new MemorySearchViewModel(_catalog, _memory) { Query = "없는 기억" };
        vm.SelectedDataset = vm.Datasets[0];
        await vm.PendingStatus!;

        await vm.SearchCommand.ExecuteAsync(null);

        Assert.Equal("기록에서 찾지 못했습니다.", vm.ResultText);
        Assert.Empty(vm.Cards);
    }

    [Fact]
    public void ShellOpensMemorySearch()
    {
        var shell = new MainViewModel(
            new DatasetsViewModel(_catalog, new FakeDialogs()),
            () => throw new InvalidOperationException(),
            () => throw new InvalidOperationException(),
            () => throw new InvalidOperationException(),
            () => throw new InvalidOperationException(),
            () => new MemorySearchViewModel(_catalog, _memory));

        shell.ShowMemoryCommand.Execute(null);

        Assert.IsType<MemorySearchViewModel>(shell.CurrentPage);
        Assert.DoesNotContain(shell.UpcomingSections, s => s.Title == "기억 검색");
    }

    private sealed class FakeMemory : IMemorySearchService
    {
        private MemoryIndexState _state = MemoryIndexState.Missing;

        public IReadOnlyList<EvidenceCard> Cards { get; set; } =
            [new EvidenceCard(new MemoryItemId(1), "2026-01-01 (목)", 0.8, [new EvidenceLine(new MessageId("m0000001"), "UserA", "09:00", "합성 기록", true)])];

        public string? LastQuery { get; private set; }

        public Task<MemoryIndexStatus> GetStatusAsync(DatasetId id, CancellationToken cancellationToken) =>
            Task.FromResult(new MemoryIndexStatus(MemoryError.None, _state, 12, _state == MemoryIndexState.Ready ? "fake-embed" : null));

        public Task<MemoryIndexStatus> BuildIndexAsync(DatasetId id, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            progress?.Report(1);
            _state = MemoryIndexState.Ready;
            return GetStatusAsync(id, cancellationToken);
        }

        public Task<MemorySearchResult> SearchAsync(DatasetId id, string query, CancellationToken cancellationToken)
        {
            LastQuery = query;
            return Task.FromResult(new MemorySearchResult(MemoryError.None, SearchMode.Hybrid, Cards));
        }
    }
}

/// <summary>
/// Memory search against the real bge-m3 on this PC with a synthetic chat. Explicit only:
/// <c>dotnet test --project tests/TalkPro.Presentation.Tests -- --explicit only</c>.
/// </summary>
public sealed class LiveMemorySearchTests : IDisposable
{
    private readonly SyntheticVault _vault = new();

    [Fact(Explicit = true)]
    public async Task BgeM3FindsAParaphraseAndNothingForAnUnrelatedQuery()
    {
        using var client = new OllamaClient(new OllamaOptions(), ModelRegistry.Default, ModelPolicy.ReleaseDefault, NullLogger<OllamaClient>.Instance);
        var service = new MemorySearchService(_vault.Repository, new ConversationSessionizer(), PrivacyScanner.CreateDefault(), client);
        string[][] exchanges =
        [
            ["이번 주말에 바다 보러 갈래?", "좋아 ㅋㅋ 강릉 가자"], ["기차표 내가 끊을게", "고마워 ㅋㅋ 몇 시 기차?"],
            ["과제 다 했어?", "아직 ㅠ 반밖에 못함"], ["나도 ㅋㅋ 내일 도서관 갈래?", "콜 10시에 보자"],
            ["오늘 저녁 치킨 먹을래?", "양념 반 후라이드 반 ㄱㄱ"], ["배달 시켰어", "오 고마워 ㅋㅋ"],
            ["다음 주 시험 범위 알아?", "3장부터 5장까지래"], ["감기 걸렸어 ㅠ", "헐 약 먹고 푹 쉬어"],
        ];
        var id = await _vault.ImportAsync([.. exchanges.SelectMany(e => new[] { ("가나다", e[0]), ("UserA", e[1]) })]);

        var built = await service.BuildIndexAsync(id, null, TestContext.Current.CancellationToken);
        var paraphrase = await service.SearchAsync(id, "동해 여행 계획 세웠던 거", TestContext.Current.CancellationToken);
        var unrelated = await service.SearchAsync(id, "회사 연봉 협상", TestContext.Current.CancellationToken);

        Assert.Equal((MemoryError.None, MemoryIndexState.Ready, "bge-m3"), (built.Error, built.State, built.Model));
        Assert.Equal(SearchMode.Hybrid, paraphrase.Mode);
        Assert.Contains(paraphrase.Cards.Take(2).SelectMany(c => c.Lines), l => l.Text.Contains("강릉", StringComparison.Ordinal));
        Assert.True(unrelated.NotFound, unrelated.ToString());
    }

    public void Dispose() => _vault.Dispose();
}
