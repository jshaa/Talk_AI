using System.Text;
using TalkPro.Core.Llm;
using TalkPro.Presentation.Abstractions;
using TalkPro.Presentation.ViewModels;
using TalkPro.Web.Viewer;

namespace TalkPro.Presentation.Tests;

/// <summary>The web viewer's in-memory pipeline (runs in the iPhone browser), tested outside the browser.</summary>
public sealed class WebViewerSessionTests
{
    private static readonly byte[] Sample = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "wwwroot", "samples", "synthetic-sample.txt"));

    [Fact]
    public void SampleLoadsAndOwnerConfirmationProducesAnOwnerOnlyReport()
    {
        var session = new ViewerSession();

        Assert.Equal(ViewerError.None, session.Load(Sample));
        var owner = session.Participants.Single(p => p.Name == "UserA");
        Assert.Equal(ViewerError.NotAttested, session.Confirm(owner.Id, attested: false, suspicionsConfirmed: true));
        Assert.Equal(ViewerError.None, session.Confirm(owner.Id, attested: true, suspicionsConfirmed: true));

        Assert.True(session.IsReady);
        Assert.Equal(owner.Messages, session.Report!.OwnerMessages);
        Assert.NotNull(session.Privacy);
    }

    [Fact]
    public void SearchShowsStoredRecordsOrNothing()
    {
        var session = Ready();

        var found = session.Search("중복처럼");
        var none = session.Search("제주도 비행기");

        Assert.Contains(found.SelectMany(c => c.Lines), l => l.Text.Contains("중복처럼 보이는", StringComparison.Ordinal));
        Assert.Contains(found.SelectMany(c => c.Lines), l => l is { Speaker: "UserA", IsOwner: true });
        Assert.Empty(none);
    }

    [Fact]
    public void SensitiveMessagesAreExcludedAndHighRiskNumbersMasked()
    {
        var text = Encoding.UTF8.GetString(Sample).TrimEnd()
            + "\n[UserA] [23:58] 합성 가족 항암 치료 소식"
            + "\n[UserA] [23:59] 합성 번호 900101-1234568 확인\n";
        var session = new ViewerSession();
        Assert.Equal(ViewerError.None, session.Load(Encoding.UTF8.GetBytes(text)));
        Assert.Equal(ViewerError.None, session.Confirm(session.Participants.Single(p => p.Name == "UserA").Id, true, true));

        var lines = session.Search("합성 항암 번호 확인").SelectMany(c => c.Lines).ToList();

        Assert.DoesNotContain(lines, l => l.Text.Contains("항암", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Text.Contains("900101", StringComparison.Ordinal));
        Assert.True(session.Privacy!.ExcludedMessages >= 1);
    }

    [Theory]
    [InlineData(new byte[0], ViewerError.Empty)]
    [InlineData(new byte[] { 0x41, 0x42, 0x43 }, ViewerError.UnknownFormat)]
    public void BadFilesAreExplained(byte[] content, ViewerError expected) =>
        Assert.Equal(expected, new ViewerSession().Load(content));

    [Fact]
    public void ResetForgetsEverything()
    {
        var session = Ready();

        session.Reset();

        Assert.False(session.IsReady);
        Assert.Empty(session.Participants);
        Assert.Null(session.Report);
        Assert.Empty(session.Search("합성"));
    }

    [Fact]
    public void ToStringHasNoContent()
    {
        var session = Ready();
        var card = session.Search("중복처럼")[0];

        var text = string.Join("|", session, card, card.Lines[0], session.Participants[0]);

        Assert.DoesNotContain("중복", text, StringComparison.Ordinal);
        Assert.DoesNotContain("UserA", text, StringComparison.Ordinal);
    }

    private static ViewerSession Ready()
    {
        var session = new ViewerSession();
        Assert.Equal(ViewerError.None, session.Load(Sample));
        Assert.Equal(ViewerError.None, session.Confirm(session.Participants.Single(p => p.Name == "UserA").Id, true, true));
        return session;
    }
}

public sealed class LocalAiInstallTests
{
    [Fact]
    public async Task OnMeteredNetworksTheDownloadAsksFirstAndProgressIsShown()
    {
        var installer = new FakeInstaller();
        var dialogs = new FakeDialogs { ConfirmResult = false };
        var vm = new LocalAiViewModel(new FakeRuntime(), new FakeLlm(), installer, new Metered(), dialogs);

        Assert.True(vm.CanInstall);
        Assert.Contains("기기 안에서만", vm.Notice, StringComparison.Ordinal);
        Assert.Equal("합성 모델 · 4.9GB · 설치 안 됨", vm.InstallText);
        await vm.InstallCommand.ExecuteAsync(null);
        Assert.Equal(0, installer.Calls);
        Assert.Contains("모바일 데이터", dialogs.ConfirmMessages.Single(), StringComparison.Ordinal);

        dialogs.ConfirmResult = true;
        await vm.InstallCommand.ExecuteAsync(null);

        Assert.Equal(1, installer.Calls);
        Assert.Equal("합성 모델 · 4.9GB · 설치됨 · 검증 완료", vm.InstallText);
        Assert.False(vm.InstallCommand.CanExecute(null));
        Assert.True(vm.RemoveModelCommand.CanExecute(null));
    }

    [Fact]
    public async Task FailedVerificationIsExplained()
    {
        var vm = new LocalAiViewModel(new FakeRuntime(), new FakeLlm(), new FakeInstaller { Result = ModelInstallError.HashMismatch });

        await vm.InstallCommand.ExecuteAsync(null);

        Assert.Contains("SHA-256", vm.InstallText, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutAnInstallerThePcTextStays()
    {
        var vm = new LocalAiViewModel(new FakeRuntime(), new FakeLlm());

        Assert.False(vm.CanInstall);
        Assert.Null(vm.InstallText);
        Assert.Contains("Ollama", vm.Notice, StringComparison.Ordinal);
    }

    private sealed class FakeInstaller : IModelInstaller
    {
        public ModelSpec Model { get; } = new("synthetic", "합성 모델", ModelRole.Chat, "MIT", new Uri("https://example.invalid"), ModelApproval.Approved);

        public long TotalBytes => 4_930_552_755;

        public bool IsInstalled { get; private set; }

        public int Calls { get; private set; }

        public ModelInstallError Result { get; set; } = ModelInstallError.None;

        public Task<ModelInstallError> InstallAsync(IProgress<ModelInstallProgress>? progress, CancellationToken cancellationToken)
        {
            Calls++;
            progress?.Report(new ModelInstallProgress(TotalBytes / 2, TotalBytes));
            IsInstalled = Result == ModelInstallError.None;
            return Task.FromResult(Result);
        }

        public void Remove() => IsInstalled = false;
    }

    private sealed class Metered : INetworkInfo
    {
        public bool IsMetered => true;
    }

    private sealed class FakeRuntime : ILocalAiRuntime
    {
        public Task<RuntimeStatus> GetStatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new RuntimeStatus(RuntimeState.Running, "ONNX Runtime GenAI", "0.17", []));
    }

    private sealed class FakeLlm : ILlmClient
    {
        public Task<LlmResult> GenerateAsync(LlmRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(LlmResult.Failed(LlmError.ModelNotInstalled));
    }
}
