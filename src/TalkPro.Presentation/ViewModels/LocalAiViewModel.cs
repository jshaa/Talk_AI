using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TalkPro.Core.Llm;
using TalkPro.Presentation.Abstractions;
using TalkPro.Presentation.Text;

namespace TalkPro.Presentation.ViewModels;

public sealed record ModelRow(string Name, string Id, string Role, string License, string Approval, string State, string InstallCommand, bool IsSelected)
{
    public override string ToString() => $"ModelRow {{ Id = {Id} }}";
}

/// <summary>
/// "로컬 AI" page (Phase 5): runtime health, allow-listed models and their state, and a connection test with
/// a fixed app-authored prompt (no conversation data). Model installation is done by the user with Ollama.
/// </summary>
public sealed class LocalAiViewModel : ObservableObject
{
    // Fixed connection-test prompt: app text only, never conversation data.
    internal const string TestInstruction = "당신은 연결 확인용 도우미입니다. 한 문장으로만 답하세요.";
    internal const string TestTask = "'로컬 AI가 준비되었습니다'라고만 답하세요.";

    private readonly ILocalAiRuntime _runtime;
    private readonly ILlmClient _llm;
    private readonly IModelInstaller? _installer;
    private readonly INetworkInfo? _network;
    private readonly IDialogService? _dialogs;
    private string _statusText = "확인 전";
    private string? _installText;
    private string? _testOutput;
    private string? _testLabel;
    private bool _isBusy;
    private bool _canTest;

    /// <param name="installer">On-device model installer (Android); null where the user installs models with Ollama.</param>
    public LocalAiViewModel(ILocalAiRuntime runtime, ILlmClient llm, IModelInstaller? installer = null, INetworkInfo? network = null, IDialogService? dialogs = null)
    {
        _runtime = runtime;
        _llm = llm;
        _installer = installer;
        _network = network;
        _dialogs = dialogs;
        CheckCommand = new AsyncRelayCommand(CheckAsync, () => !IsBusy);
        TestCommand = new AsyncRelayCommand(TestAsync, () => !IsBusy && _canTest);
        InstallCommand = new AsyncRelayCommand(InstallAsync, () => !IsBusy && _installer is { IsInstalled: false });
        RemoveModelCommand = new AsyncRelayCommand(RemoveAsync, () => !IsBusy && _installer is { IsInstalled: true });
        Notice = installer is null
            ? "로컬 AI는 이 PC에서 실행 중인 Ollama(127.0.0.1)에만 연결합니다. 대화 내용은 인터넷으로 나가지 않으며, " +
              "AI에는 개인정보 검토에서 마스킹·제외가 적용된 내용만 전달됩니다. 모델은 아래 명령으로 사용자가 직접 설치합니다."
            : "로컬 AI는 이 기기 안에서만 실행됩니다. 인터넷은 모델 파일을 내려받을 때(huggingface.co)만 쓰며, 그 요청에는 대화 내용이 전혀 들어가지 않습니다. " +
              "내려받은 파일은 고정된 SHA-256으로 검증합니다. AI에는 개인정보 검토에서 마스킹·제외가 적용된 내용만 전달됩니다.";
        _installText = InstallSummary();
    }

    public string Notice { get; }

    /// <summary>True on devices where the app downloads its own model (Android).</summary>
    public bool CanInstall => _installer is not null;

    /// <summary>"Phi-4-mini · 4.9GB · 설치 안 됨", progress while downloading, or the outcome.</summary>
    public string? InstallText
    {
        get => _installText;
        private set => SetProperty(ref _installText, value);
    }

    public AsyncRelayCommand InstallCommand { get; }

    public AsyncRelayCommand RemoveModelCommand { get; }

    public ObservableCollection<ModelRow> Models { get; } = [];

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string? TestOutput
    {
        get => _testOutput;
        private set => SetProperty(ref _testOutput, value);
    }

    /// <summary>"AI 생성" label shown with every model output.</summary>
    public string? TestLabel
    {
        get => _testLabel;
        private set => SetProperty(ref _testLabel, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                CheckCommand.NotifyCanExecuteChanged();
                TestCommand.NotifyCanExecuteChanged();
                InstallCommand.NotifyCanExecuteChanged();
                RemoveModelCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public AsyncRelayCommand CheckCommand { get; }

    public AsyncRelayCommand TestCommand { get; }

    private async Task CheckAsync()
    {
        IsBusy = true;
        try
        {
            var status = await _runtime.GetStatusAsync(CancellationToken.None).ConfigureAwait(true);
            Models.Clear();
            var chat = status.Selected(ModelRole.Chat);
            var embedding = status.Selected(ModelRole.Embedding);
            foreach (var m in status.Models)
            {
                Models.Add(new ModelRow(
                    m.Spec.DisplayName,
                    m.Spec.Id,
                    UiText.For(m.Spec.Role),
                    m.Spec.License,
                    UiText.For(m.Spec.Approval),
                    UiText.For(m.State),
                    "ollama pull " + m.Spec.Id,
                    ReferenceEquals(m, chat) || ReferenceEquals(m, embedding)));
            }

            StatusText = status.State switch
            {
                RuntimeState.Running => string.Create(UiText.Korean,
                    $"{status.RuntimeName} {status.Version} 실행 중 · 대화 모델: {chat?.Spec.DisplayName ?? "없음"} · 임베딩 모델: {embedding?.Spec.DisplayName ?? "없음"}"),
                RuntimeState.NotRunning => "Ollama가 실행 중이 아닙니다. Ollama를 설치·실행한 뒤 다시 확인해 주세요. (https://ollama.com)",
                _ => "Ollama 응답을 해석할 수 없습니다. Ollama를 최신 버전으로 업데이트해 주세요.",
            };
            _canTest = chat is not null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private string? InstallSummary() => _installer is null
        ? null
        : string.Create(UiText.Korean, $"{_installer.Model.DisplayName} · {_installer.TotalBytes / 1e9:F1}GB · {(_installer.IsInstalled ? "설치됨" : "설치 안 됨")}");

    private async Task InstallAsync()
    {
        var installer = _installer!;
        if (_network is { IsMetered: true } && _dialogs is not null
            && !await _dialogs.ConfirmAsync("모바일 데이터", string.Create(UiText.Korean, $"지금 모바일 데이터(요금제 네트워크)에 연결되어 있습니다. 약 {installer.TotalBytes / 1e9:F1}GB를 내려받을까요? Wi-Fi에서 받기를 권장합니다.")).ConfigureAwait(true))
        {
            return;
        }

        IsBusy = true;
        try
        {
            // Progress<T> posts updates asynchronously: once the outcome is shown, late updates must not overwrite it.
            var gate = new Lock();
            var finished = false;
            var progress = new Progress<ModelInstallProgress>(p =>
            {
                lock (gate)
                {
                    if (!finished)
                    {
                        InstallText = string.Create(UiText.Korean, $"내려받는 중 {p.Fraction:P0} ({p.BytesDone / 1e9:F2}/{p.BytesTotal / 1e9:F1}GB)");
                    }
                }
            });
            var error = await installer.InstallAsync(progress, CancellationToken.None).ConfigureAwait(true);
            lock (gate)
            {
                finished = true;
                InstallText = error == ModelInstallError.None ? InstallSummary() + " · 검증 완료" : UiText.For(error);
            }
        }
        finally
        {
            IsBusy = false;
        }

        await CheckAsync().ConfigureAwait(true);
    }

    private async Task RemoveAsync()
    {
        if (_dialogs is not null && !await _dialogs.ConfirmAsync("모델 삭제", "내려받은 AI 모델 파일을 삭제합니다. 다시 쓰려면 다시 내려받아야 합니다.").ConfigureAwait(true))
        {
            return;
        }

        _installer!.Remove();
        InstallText = InstallSummary();
        InstallCommand.NotifyCanExecuteChanged();
        RemoveModelCommand.NotifyCanExecuteChanged();
        await CheckAsync().ConfigureAwait(true);
    }

    private async Task TestAsync()
    {
        IsBusy = true;
        TestOutput = null;
        TestLabel = null;
        try
        {
            var result = await _llm.GenerateAsync(new LlmRequest(TestInstruction, [], TestTask) { Temperature = 0, MaxOutputTokens = 64 }, CancellationToken.None).ConfigureAwait(true);
            if (result.Succeeded)
            {
                TestOutput = result.Text!.Trim();
                TestLabel = string.Create(CultureInfo.InvariantCulture, $"AI 생성 · {result.Model!.ModelId} · {result.Elapsed.TotalSeconds:F1}초");
            }
            else
            {
                TestOutput = UiText.For(result.Error);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }
}
