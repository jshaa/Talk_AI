using TalkPro.Core.Llm;
using TalkPro.Presentation.ViewModels;

namespace TalkPro.Presentation.Tests;

public sealed class LocalAiViewModelTests
{
    private static readonly Uri License = new("https://example.invalid/license");
    private static readonly ModelSpec Chat = new("chat:1b", "Chat", ModelRole.Chat, "Apache-2.0", License, ModelApproval.Candidate);
    private static readonly ModelSpec Embed = new("embed", "Embed", ModelRole.Embedding, "MIT", License, ModelApproval.Candidate);

    private readonly FakeRuntime _runtime = new();
    private readonly FakeLlm _llm = new();

    [Fact]
    public async Task CheckShowsRuntimeAndModelStates()
    {
        _runtime.Next = new RuntimeStatus(RuntimeState.Running, "Ollama", "0.35.1",
        [
            new ModelStatus(Chat, ModelInstallState.Ready, "d"),
            new ModelStatus(Embed, ModelInstallState.NotInstalled, null),
        ]);
        var vm = new LocalAiViewModel(_runtime, _llm);

        await vm.CheckCommand.ExecuteAsync(null);

        Assert.Contains("Ollama 0.35.1 실행 중", vm.StatusText, StringComparison.Ordinal);
        Assert.Contains("대화 모델: Chat", vm.StatusText, StringComparison.Ordinal);
        Assert.Contains("임베딩 모델: 없음", vm.StatusText, StringComparison.Ordinal);
        Assert.Equal(["사용 가능", "설치 안 됨"], vm.Models.Select(m => m.State));
        Assert.Equal("ollama pull embed", vm.Models[1].InstallCommand);
        Assert.True(vm.Models[0].IsSelected);
        Assert.True(vm.TestCommand.CanExecute(null));
    }

    [Fact]
    public async Task NotRunningRuntimeExplainsWhatToDoAndDisablesTheTest()
    {
        _runtime.Next = new RuntimeStatus(RuntimeState.NotRunning, "Ollama", null, []);
        var vm = new LocalAiViewModel(_runtime, _llm);

        await vm.CheckCommand.ExecuteAsync(null);

        Assert.Contains("실행 중이 아닙니다", vm.StatusText, StringComparison.Ordinal);
        Assert.False(vm.TestCommand.CanExecute(null));
    }

    [Fact]
    public async Task ConnectionTestUsesAFixedPromptWithoutConversationDataAndLabelsTheOutput()
    {
        _runtime.Next = new RuntimeStatus(RuntimeState.Running, "Ollama", "1", [new ModelStatus(Chat, ModelInstallState.Ready, "d")]);
        _llm.Next = new LlmResult(LlmError.None, " 로컬 AI가 준비되었습니다 ", new ModelProvenance("chat:1b", "d"), TimeSpan.FromSeconds(1.5));
        var vm = new LocalAiViewModel(_runtime, _llm);
        await vm.CheckCommand.ExecuteAsync(null);

        await vm.TestCommand.ExecuteAsync(null);

        var request = Assert.Single(_llm.Requests);
        Assert.Empty(request.Context);
        Assert.Equal("로컬 AI가 준비되었습니다", vm.TestOutput);
        Assert.Equal("AI 생성 · chat:1b · 1.5초", vm.TestLabel);
    }

    [Fact]
    public async Task FailedTestShowsTheReason()
    {
        _runtime.Next = new RuntimeStatus(RuntimeState.Running, "Ollama", "1", [new ModelStatus(Chat, ModelInstallState.Ready, "d")]);
        _llm.Next = LlmResult.Failed(LlmError.Timeout);
        var vm = new LocalAiViewModel(_runtime, _llm);
        await vm.CheckCommand.ExecuteAsync(null);

        await vm.TestCommand.ExecuteAsync(null);

        Assert.Contains("응답 시간이 초과", vm.TestOutput, StringComparison.Ordinal);
        Assert.Null(vm.TestLabel);
    }

    [Fact]
    public async Task ShellOpensTheLocalAiPageAndChecksImmediately()
    {
        _runtime.Next = new RuntimeStatus(RuntimeState.NotRunning, "Ollama", null, []);
        var datasets = new DatasetsViewModel(new FakeCatalog(), new FakeDialogs());
        var shell = new MainViewModel(datasets, () => throw new InvalidOperationException(), () => throw new InvalidOperationException(), () => new LocalAiViewModel(_runtime, _llm), () => throw new InvalidOperationException(), () => throw new InvalidOperationException());

        shell.ShowLocalAiCommand.Execute(null);
        var page = Assert.IsType<LocalAiViewModel>(shell.CurrentPage);
        await page.CheckCommand.ExecutionTask!;

        Assert.Equal(1, _runtime.Calls);
        Assert.Contains("외부 전송", shell.LocalOnlyNotice, StringComparison.Ordinal);
        Assert.Contains("127.0.0.1", page.Notice, StringComparison.Ordinal);
    }

    private sealed class FakeRuntime : ILocalAiRuntime
    {
        public RuntimeStatus Next { get; set; } = new(RuntimeState.NotRunning, "Ollama", null, []);

        public int Calls { get; private set; }

        public Task<RuntimeStatus> GetStatusAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Next);
        }
    }

    private sealed class FakeLlm : ILlmClient
    {
        public LlmResult Next { get; set; } = LlmResult.Failed(LlmError.RuntimeNotRunning);

        public List<LlmRequest> Requests { get; } = [];

        public Task<LlmResult> GenerateAsync(LlmRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(Next);
        }
    }
}
