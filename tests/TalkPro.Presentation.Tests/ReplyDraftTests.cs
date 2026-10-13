using System.Text.Json;
using TalkPro.Core.Llm;
using TalkPro.Core.Model;
using TalkPro.Infrastructure.Storage;
using TalkPro.Infrastructure.Vault;
using TalkPro.Ingestion.Formats.Synthetic;
using TalkPro.Ingestion.Parsing;
using TalkPro.Ingestion.Speakers;
using TalkPro.Memory;
using TalkPro.Persona.Style;
using TalkPro.Presentation.Abstractions;
using TalkPro.Presentation.Services;
using TalkPro.Presentation.ViewModels;
using TalkPro.Privacy;
using TalkPro.Security.Keys;

namespace TalkPro.Presentation.Tests;

/// <summary>Builds a privacy-reviewed synthetic dataset in a real encrypted vault.</summary>
internal sealed class SyntheticVault : IDisposable
{
    private readonly TempDirectory _root = new();
    private readonly ImportWorkflow _workflow;

    public SyntheticVault()
    {
        var paths = new AppPaths(Path.Combine(_root.Path, "app"), []);
        paths.EnsureCreated();
        var keys = new DatasetKeyStore(paths.Keys, new TestKeyProtector());
        var deletion = new DatasetDeletionService(paths, keys);
        Repository = new DatasetRepository(paths, keys, deletion, TimeProvider.System);
        _workflow = new ImportWorkflow(new ChatExportParser(SyntheticFormats.All), PrivacyScanner.CreateDefault(), Repository, deletion);
    }

    public DatasetRepository Repository { get; }

    /// <summary>Root of the temporary app folder (for checks on stored files).</summary>
    public string Root => _root.Path;

    /// <summary>Lines are (speaker, text), one minute apart; "UserA" is the owner.</summary>
    public async Task<DatasetId> ImportAsync(params (string Speaker, string Text)[] lines)
    {
        var parsed = SyntheticChat.Create([.. lines.Select(l => (l.Speaker, 0))]);
        var review = SpeakerReview.Start(parsed).ConfirmAttestation();
        var owner = review.Participants.Single(p => p.DisplayName == "UserA").Id;
        var chat = review.SelectOwner(owner).Review.Complete().Chat!;
        chat = chat with { Messages = [.. chat.Messages.Select((m, i) => m with { Text = lines[i].Text, Timestamp = m.Timestamp.AddMinutes(i) })] };
        var findings = await _workflow.ScanAsync(chat, CancellationToken.None);
        return await _workflow.SaveAsync(PrivacyImportBuilder.Build(chat, PrivacyReview.Start(findings, chat.Messages.Count)), "합성", CancellationToken.None);
    }

    public void Dispose() => _root.Dispose();
}

/// <summary>Real vault, privacy, sessionizer and style engine; the model is a fake that records every request.</summary>
public sealed class ReplyDraftServiceTests : IDisposable
{
    private readonly SyntheticVault _vault = new();
    private readonly FakeLlm _llm = new();
    private readonly ReplyDraftService _service;

    public ReplyDraftServiceTests() =>
        _service = new ReplyDraftService(_vault.Repository, new ConversationSessionizer(), PrivacyScanner.CreateDefault(), _llm);

    [Fact]
    public async Task OnlyRedactedTextReachesTheModelAndDraftsAreRestoredAndChecked()
    {
        var id = await _vault.ImportAsync(
            ("라마바", "합성 인사"),
            ("가나다", "주말에 영화 볼래?"),
            ("UserA", "좋아 ㅋㅋ 가나다 너도 와"),
            ("가나다", "합성 가족 항암 치료 소식"),
            ("UserA", "헐 괜찮아?"));
        _llm.Reply = _ => Json(("accept", "[인물1] 나도 갈래 ㅋㅋ"), ("hold", "010-9999-8888로 전화해"), ("ask_back", "몇 시?"));

        var result = await _service.DraftAsync(id, null, "라마바가 내일 영화 보재 010-1234-5678", ReplyDirections.Default, CancellationToken.None);

        var request = _llm.Requests[0];
        var sent = request.Instruction + request.Task + string.Join("\n", request.Context.Select(c => c.Label + c.Text.Value));
        Assert.DoesNotContain("가나다", sent, StringComparison.Ordinal);
        Assert.DoesNotContain("라마바", sent, StringComparison.Ordinal);
        Assert.DoesNotContain("항암", sent, StringComparison.Ordinal);
        Assert.DoesNotContain("1234", sent, StringComparison.Ordinal);

        // One pseudonym map per request: [인물1] is already 가나다 (from the example), so 라마바 must be [인물2].
        Assert.Equal("[인물2]가 내일 영화 보재 [전화번호]", request.Context[^1].Text.Value);
        Assert.Equal(["예시-상대", "예시-나", "받은 메시지"], request.Context.Select(c => c.Label));
        Assert.Equal("좋아 ㅋㅋ [인물1] 너도 와", request.Context[1].Text.Value);

        // The hold draft invents a phone number: dropped, asked for once more (only that slot), dropped again.
        Assert.Equal(DraftError.None, result.Error);
        Assert.Equal([(0, ReplyDirection.Accept, "가나다 나도 갈래 ㅋㅋ"), (2, ReplyDirection.AskBack, "몇 시?")], result.Drafts.Select(d => (d.Slot, d.Direction, d.Text)));
        Assert.Equal([1], result.MissingSlots);
        Assert.Contains("1. hold", _llm.Requests[1].Instruction, StringComparison.Ordinal);
        Assert.DoesNotContain("2. ", _llm.Requests[1].Instruction, StringComparison.Ordinal);
        Assert.All(result.Drafts, d => Assert.InRange(d.StyleMatch, 0, 100));
        Assert.Equal(1, result.ExampleCount);
        Assert.Equal("fake:1b", result.Model!.ModelId);
        Assert.Contains("전체 대화 기준", result.StyleSummary);
    }

    [Theory]
    [InlineData("국민은행 123456-78-901234로 보내 줘")]
    [InlineData("서울특별시 강남구 테헤란로 123으로 와")]
    [InlineData("내 번호 010-9876-5432야")]
    public async Task DraftsWithInventedIdentifiersAreDropped(string invented)
    {
        var id = await SimpleDatasetAsync();
        _llm.Reply = _ => Json(("accept", invented));

        var result = await _service.DraftAsync(id, null, "합성 메시지", [ReplyDirection.Accept], CancellationToken.None);

        Assert.Equal(DraftError.InvalidOutput, result.Error);
        Assert.Equal(2, _llm.Requests.Count);
    }

    [Fact]
    public async Task DefaultGenerationAsksForEachDirectionInOrderWithRulesButNoConversationText()
    {
        var id = await _vault.ImportAsync(("가나다", "합성비밀질문"), ("UserA", "합성비밀답장"));
        _llm.Reply = _ => Json(("accept", "좋아 ㅋㅋ"), ("hold", "일단 생각해볼게"), ("ask_back", "몇 시에?"));

        var result = await _service.DraftAsync(id, null, "합성비밀받은말", ReplyDirections.Default, CancellationToken.None);

        var request = Assert.Single(_llm.Requests);
        Assert.Equal([ReplyDirection.Accept, ReplyDirection.Hold, ReplyDirection.AskBack], result.Drafts.Select(d => d.Direction));
        Assert.Equal(3, result.Drafts.Select(d => d.Direction).Distinct().Count());
        Assert.Contains("1. accept\n2. hold\n3. ask_back", request.Instruction.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        foreach (var direction in ReplyDirections.Default)
        {
            Assert.Contains(ReplyDirections.Rule(direction), request.Instruction, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("합성비밀", request.Instruction + request.Task, StringComparison.Ordinal);
        Assert.Contains("\"enum\":[\"accept\",\"hold\",\"ask_back\"]", request.JsonSchema, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\"drafts\":[{\"text\":\"합성\"}]}")]
    [InlineData("{\"drafts\":[{\"direction\":\"maybe\",\"text\":\"합성\"}]}")]
    [InlineData("{\"drafts\":[{\"direction\":0,\"text\":\"합성\"}]}")]
    [InlineData("{\"drafts\":[{\"direction\":\"Accept\",\"text\":\"합성\"}]}")]
    [InlineData("{\"drafts\":[\"합성\"]}")]
    [InlineData("{\"drafts\": 3}")]
    public async Task MissingOrInvalidDirectionIsRetriedOnceThenReported(string reply)
    {
        var id = await SimpleDatasetAsync();
        _llm.Reply = _ => new LlmResult(LlmError.None, reply, new ModelProvenance("fake:1b", "d"), TimeSpan.Zero);

        var result = await _service.DraftAsync(id, null, "합성 메시지", ReplyDirections.Default, CancellationToken.None);

        Assert.Equal(DraftError.InvalidOutput, result.Error);
        Assert.Equal([0.8, 0.4], _llm.Requests.Select(r => r.Temperature));
    }

    [Fact]
    public async Task DraftsOfAnotherDirectionNeverFillASlot()
    {
        var id = await SimpleDatasetAsync();
        _llm.Reply = _ => Json(("accept", "좋아"), ("accept", "콜"), ("accept", "ㅇㅋ"));

        var result = await _service.DraftAsync(id, null, "합성 메시지", ReplyDirections.Default, CancellationToken.None);

        Assert.Equal([ReplyDirection.Accept], result.Drafts.Select(d => d.Direction));
        Assert.Equal([1, 2], result.MissingSlots);
        Assert.Contains("1. hold\n2. ask_back", _llm.Requests[1].Instruction.ReplaceLineEndings("\n"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OneDirectionRegeneratesOneSlotAndRepeatedDirectionsAreAllowed()
    {
        var id = await SimpleDatasetAsync();
        _llm.Reply = _ => Json(("ask_back", "언제?"));
        var single = await _service.DraftAsync(id, null, "합성 메시지", [ReplyDirection.AskBack], CancellationToken.None);
        _llm.Reply = _ => Json(("hold", "생각해볼게"), ("hold", "이따 말해줄게"), ("hold", "일단 확인하고"));
        var same = await _service.DraftAsync(id, null, "합성 메시지", [ReplyDirection.Hold, ReplyDirection.Hold, ReplyDirection.Hold], CancellationToken.None);

        Assert.Equal((0, ReplyDirection.AskBack, "언제?"), (single.Drafts[0].Slot, single.Drafts[0].Direction, single.Drafts[0].Text));
        Assert.Single(single.Drafts);
        Assert.DoesNotContain("2. ", _llm.Requests[0].Instruction, StringComparison.Ordinal);
        Assert.Equal([0, 1, 2], same.Drafts.Select(d => d.Slot));
        Assert.Contains("같은 방향이 여러 번이면", _llm.Requests[1].Instruction, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ModelFailureIsPassedThrough()
    {
        var id = await SimpleDatasetAsync();
        _llm.Reply = _ => LlmResult.Failed(LlmError.RuntimeNotRunning);

        var result = await _service.DraftAsync(id, null, "합성 메시지", ReplyDirections.Default, CancellationToken.None);

        Assert.Equal(DraftError.Model, result.Error);
        Assert.Equal(LlmError.RuntimeNotRunning, result.LlmError);
    }

    [Fact]
    public async Task InputAndDatasetAreValidatedBeforeTheModelIsCalled()
    {
        var unreviewed = _vault.Repository.Create("합성");
        var all = ReplyDirections.Default;

        Assert.Equal(DraftError.EmptyInput, (await _service.DraftAsync(unreviewed, null, "  ", all, CancellationToken.None)).Error);
        Assert.Equal(DraftError.InputTooLong, (await _service.DraftAsync(unreviewed, null, new string('가', ReplyDraftService.MaxInputLength + 1), all, CancellationToken.None)).Error);
        Assert.Equal(DraftError.NotPrivacyReviewed, (await _service.DraftAsync(unreviewed, null, "합성", all, CancellationToken.None)).Error);
        Assert.Equal(DraftError.Unreadable, (await _service.DraftAsync(DatasetId.New(), null, "합성", all, CancellationToken.None)).Error);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _service.DraftAsync(unreviewed, null, "합성", [], CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _service.DraftAsync(unreviewed, null, "합성", [.. all, ReplyDirection.Hold], CancellationToken.None));
        Assert.Empty(_llm.Requests);
    }

    [Fact]
    public async Task TargetsExcludeTheOwner()
    {
        var id = await SimpleDatasetAsync();

        var targets = await _service.LoadTargetsAsync(id, CancellationToken.None);

        Assert.Equal(["가나다"], targets!.Select(t => t.DisplayName));
        Assert.Null(await _service.LoadTargetsAsync(DatasetId.New(), CancellationToken.None));
    }

    public void Dispose() => _vault.Dispose();

    internal static LlmResult Json(params (string Direction, string Text)[] drafts) =>
        new(LlmError.None, JsonSerializer.Serialize(new { drafts = drafts.Select(d => new { direction = d.Direction, text = d.Text }) }), new ModelProvenance("fake:1b", "d"), TimeSpan.FromSeconds(1));

    private Task<DatasetId> SimpleDatasetAsync() => _vault.ImportAsync(("가나다", "합성 질문"), ("UserA", "합성 답"));

    private sealed class FakeLlm : ILlmClient
    {
        public Func<LlmRequest, LlmResult> Reply { get; set; } = _ => Json(("accept", "합성"));

        public List<LlmRequest> Requests { get; } = [];

        public Task<LlmResult> GenerateAsync(LlmRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(Reply(request));
        }
    }
}

public sealed class ReplyDraftViewModelTests
{
    private readonly FakeCatalog _catalog = new();
    private readonly FakeDrafts _drafts = new();
    private readonly FakeClipboard _clipboard = new();

    public ReplyDraftViewModelTests()
    {
        _catalog.Datasets.Add(new DatasetSummary(DatasetId.New(), DatasetState.Ready, "합성 라벨", DateTimeOffset.UnixEpoch, null, 2, 10, null));
        _catalog.Datasets.Add(new DatasetSummary(DatasetId.New(), DatasetState.Damaged, null, null, null, 0, 0, null));
    }

    [Fact]
    public void DirectionsHaveKoreanLabelsAndWireNames()
    {
        Assert.Equal(["수락", "보류", "되묻기"], DirectionOption.All.Select(o => o.Label));
        Assert.Equal(["accept", "hold", "ask_back"], Enum.GetValues<ReplyDirection>().Select(ReplyDirections.WireName));
        Assert.Equal([ReplyDirection.Accept, ReplyDirection.Hold, ReplyDirection.AskBack], ReplyDirections.Default);
    }

    [Fact]
    public async Task DefaultGenerationShowsOneLabelledCardPerDirection()
    {
        var vm = await ReadyAsync();
        Assert.Equal("초안 만들기", vm.DraftButtonText);

        await vm.DraftCommand.ExecuteAsync(null);

        Assert.Equal([ReplyDirection.Accept, ReplyDirection.Hold, ReplyDirection.AskBack], Assert.Single(_drafts.Calls).Directions);
        Assert.Equal(["수락", "보류", "되묻기"], vm.Drafts.Select(d => d.DirectionLabel));
        Assert.Equal(["수락 초안 1", "보류 초안 1", "되묻기 초안 1"], vm.Drafts.Select(d => d.Text));
        Assert.Equal("말투 일치도 90", vm.Drafts[0].MatchText);
        Assert.Equal("AI 생성 초안 · fake:1b · 내 답장 예시 4개 참고", vm.AiLabel);
        Assert.Equal("반영한 내 말투: 반말 · 전체 대화 기준", vm.StyleText);
        Assert.Equal("전체 다시 생성", vm.DraftButtonText);
        Assert.Null(vm.SameDirectionWarning);
    }

    [Fact]
    public async Task ChangingADirectionRegeneratesOnlyThatCardForTheSameMessage()
    {
        var vm = await ReadyAsync();
        vm.SelectedTarget = vm.Targets[1];
        await vm.DraftCommand.ExecuteAsync(null);
        var (first, third) = (vm.Drafts[0].Text, vm.Drafts[2].Text);
        vm.IncomingText = "나중에 바꾼 입력";

        vm.Drafts[1].SelectedDirection = DirectionOption.Of(ReplyDirection.AskBack);
        Assert.Contains("이 초안만 다시 생성", vm.Drafts[1].Note, StringComparison.Ordinal);
        await vm.Drafts[1].RegenerateCommand.ExecuteAsync(null);

        var call = _drafts.Calls[^1];
        Assert.Equal([ReplyDirection.AskBack], call.Directions);
        Assert.Equal(("합성 받은 메시지", new ParticipantId("p_02")), (call.Incoming, call.Counterpart!.Value));
        Assert.Equal("되묻기 초안 2", vm.Drafts[1].Text);
        Assert.Equal("되묻기", vm.Drafts[1].DirectionLabel);
        Assert.Null(vm.Drafts[1].Note);
        Assert.Equal((first, third), (vm.Drafts[0].Text, vm.Drafts[2].Text));
        Assert.Equal(2, _drafts.Calls.Count);
    }

    [Fact]
    public async Task SameDirectionEverywhereIsAllowedButWarned()
    {
        var vm = await ReadyAsync();
        await vm.DraftCommand.ExecuteAsync(null);

        foreach (var row in vm.Drafts)
        {
            row.SelectedDirection = DirectionOption.Of(ReplyDirection.Hold);
        }

        Assert.Equal("방향이 모두 같습니다. 서로 다른 답장을 원하면 초안마다 방향을 바꿔 보세요.", vm.SameDirectionWarning);
        Assert.True(vm.Drafts[0].RegenerateCommand.CanExecute(null));
        vm.Drafts[2].SelectedDirection = DirectionOption.Of(ReplyDirection.AskBack);
        Assert.Null(vm.SameDirectionWarning);
    }

    [Fact]
    public async Task MissingSlotIsExplainedAndCannotBeCopied()
    {
        _drafts.Missing = [1];
        var vm = await ReadyAsync();

        await vm.DraftCommand.ExecuteAsync(null);

        Assert.Null(vm.Drafts[1].Text);
        Assert.Contains("만들지 못했습니다", vm.Drafts[1].Note, StringComparison.Ordinal);
        Assert.False(vm.CopyCommand.CanExecute(vm.Drafts[1]));
        Assert.True(vm.CopyCommand.CanExecute(vm.Drafts[0]));
    }

    [Fact]
    public async Task FailedRegenerationKeepsTheOtherCards()
    {
        var vm = await ReadyAsync();
        await vm.DraftCommand.ExecuteAsync(null);
        _drafts.Next = DraftSet.Failed(DraftError.Model, LlmError.Timeout);

        await vm.Drafts[0].RegenerateCommand.ExecuteAsync(null);

        Assert.Contains("응답 시간이 초과", vm.Drafts[0].Note, StringComparison.Ordinal);
        Assert.Equal("수락 초안 1", vm.Drafts[0].Text);
        Assert.Equal("보류 초안 1", vm.Drafts[1].Text);
    }

    [Fact]
    public async Task DraftsCanOnlyBeCopiedWithAutoClear()
    {
        var vm = await ReadyAsync();
        await vm.DraftCommand.ExecuteAsync(null);

        vm.CopyCommand.Execute(vm.Drafts[2]);

        Assert.Equal(("되묻기 초안 1", TimeSpan.FromSeconds(60)), _clipboard.Last);
        Assert.Contains("60초 후 클립보드에서 지웁니다", vm.Message, StringComparison.Ordinal);
        Assert.Contains("자동 전송 기능은 없습니다", vm.Notice, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ErrorsAreExplained()
    {
        _drafts.Next = DraftSet.Failed(DraftError.Model, LlmError.ModelNotInstalled);
        var vm = await ReadyAsync();

        await vm.DraftCommand.ExecuteAsync(null);

        Assert.Equal("사용할 수 있는 모델이 설치되어 있지 않습니다.", vm.Message);
        Assert.Empty(vm.Drafts);
        Assert.Null(vm.AiLabel);
        Assert.Equal("초안 만들기", vm.DraftButtonText);
    }

    [Fact]
    public void ShellOpensTheDraftPage()
    {
        var shell = new MainViewModel(
            new DatasetsViewModel(_catalog, new FakeDialogs()),
            () => throw new InvalidOperationException(),
            () => throw new InvalidOperationException(),
            () => throw new InvalidOperationException(),
            () => new ReplyDraftViewModel(_catalog, _drafts, _clipboard), () => throw new InvalidOperationException());

        shell.ShowDraftsCommand.Execute(null);

        Assert.IsType<ReplyDraftViewModel>(shell.CurrentPage);
        Assert.DoesNotContain(shell.UpcomingSections, s => s.Title.Contains("답장", StringComparison.Ordinal));
    }

    private async Task<ReplyDraftViewModel> ReadyAsync()
    {
        var vm = new ReplyDraftViewModel(_catalog, _drafts, _clipboard);
        Assert.Single(vm.Datasets);
        Assert.False(vm.DraftCommand.CanExecute(null));
        vm.SelectedDataset = vm.Datasets[0];
        await vm.PendingTargets!;
        Assert.Equal(["(지정 안 함 — 전체 말투)", "UserB"], vm.Targets.Select(t => t.Name));
        vm.IncomingText = "합성 받은 메시지";
        Assert.True(vm.DraftCommand.CanExecute(null));
        return vm;
    }

    private sealed class FakeDrafts : IReplyDraftService
    {
        public DraftSet? Next { get; set; }

        public IReadOnlyList<int> Missing { get; set; } = [];

        public List<(ParticipantId? Counterpart, string Incoming, IReadOnlyList<ReplyDirection> Directions)> Calls { get; } = [];

        public Task<IReadOnlyList<DraftTarget>?> LoadTargetsAsync(DatasetId id, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DraftTarget>?>([new DraftTarget(new ParticipantId("p_02"), "UserB")]);

        public Task<DraftSet> DraftAsync(DatasetId id, ParticipantId? counterpart, string incoming, IReadOnlyList<ReplyDirection> directions, CancellationToken cancellationToken)
        {
            Calls.Add((counterpart, incoming, directions));
            if (Next is { } next)
            {
                return Task.FromResult(next);
            }

            var drafts = directions
                .Select((d, slot) => new DraftItem(slot, d, $"{Presentation.Text.UiText.For(d)} 초안 {Calls.Count}", 90))
                .Where(d => !Missing.Contains(d.Slot))
                .ToList();
            return Task.FromResult(new DraftSet(DraftError.None, LlmError.None, drafts, ["반말", "전체 대화 기준"], 4, new ModelProvenance("fake:1b", "d"))
            {
                MissingSlots = Missing,
            });
        }
    }

    private sealed class FakeClipboard : IClipboardService
    {
        public (string Text, TimeSpan ClearAfter)? Last { get; private set; }

        public void CopyWithAutoClear(string text, TimeSpan clearAfter) => Last = (text, clearAfter);
    }
}
