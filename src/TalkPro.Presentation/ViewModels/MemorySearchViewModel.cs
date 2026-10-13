using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TalkPro.Core.Model;
using TalkPro.Presentation.Services;
using TalkPro.Presentation.Text;

namespace TalkPro.Presentation.ViewModels;

/// <summary>
/// "기억 검색" (Phase 7). Finds past conversations by meaning and keywords and shows them as QUOTE cards —
/// the stored record, not AI text. Building the index (local embeddings) is an explicit user action.
/// Queries and results live in memory only.
/// </summary>
public sealed class MemorySearchViewModel : ObservableObject
{
    private readonly IMemorySearchService _memory;
    private DatasetChoice? _dataset;
    private string _query = string.Empty;
    private string? _statusText;
    private string? _resultText;
    private string? _modeText;
    private double _progress;
    private bool _isBusy;
    private MemoryIndexState _state = MemoryIndexState.Missing;

    public MemorySearchViewModel(IDatasetCatalog catalog, IMemorySearchService memory)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _memory = memory;
        SearchCommand = new AsyncRelayCommand(SearchAsync, () => !IsBusy && SelectedDataset is not null && !string.IsNullOrWhiteSpace(Query));
        BuildIndexCommand = new AsyncRelayCommand(BuildAsync, () => !IsBusy && SelectedDataset is not null);
        foreach (var d in catalog.List().Where(d => d.State == DatasetState.Ready))
        {
            Datasets.Add(new DatasetChoice(d.Id, string.IsNullOrWhiteSpace(d.Label) ? "(이름 없음)" : d.Label));
        }
    }

    public string Notice { get; } =
        "검색은 이 PC 안에서만 합니다. 결과는 AI가 만든 문장이 아니라 저장된 대화 원문(실제 기록)입니다. " +
        "분석 제외한 메시지는 찾지 않고, 주민등록번호·카드·계좌번호는 가려서 보여 줍니다. 상대방의 메시지는 내 메시지의 앞뒤 맥락으로만 나옵니다. 검색어와 결과는 저장되지 않습니다.";

    public ObservableCollection<DatasetChoice> Datasets { get; } = [];

    public ObservableCollection<EvidenceCard> Cards { get; } = [];

    public DatasetChoice? SelectedDataset
    {
        get => _dataset;
        set
        {
            if (SetProperty(ref _dataset, value))
            {
                Cards.Clear();
                ResultText = null;
                ModeText = null;
                PendingStatus = RefreshStatusAsync();
                NotifyCommands();
            }
        }
    }

    /// <summary>The last status refresh, so tests can await it.</summary>
    public Task? PendingStatus { get; private set; }

    public string Query
    {
        get => _query;
        set
        {
            if (SetProperty(ref _query, value ?? string.Empty))
            {
                SearchCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string? StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string? ResultText
    {
        get => _resultText;
        private set => SetProperty(ref _resultText, value);
    }

    public string? ModeText
    {
        get => _modeText;
        private set => SetProperty(ref _modeText, value);
    }

    /// <summary>Index build progress, 0..100.</summary>
    public double Progress
    {
        get => _progress;
        private set => SetProperty(ref _progress, value);
    }

    public string BuildButtonText => _state == MemoryIndexState.Missing ? "기억 색인 만들기" : "기억 색인 다시 만들기";

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                NotifyCommands();
            }
        }
    }

    public AsyncRelayCommand SearchCommand { get; }

    public AsyncRelayCommand BuildIndexCommand { get; }

    private void NotifyCommands()
    {
        SearchCommand.NotifyCanExecuteChanged();
        BuildIndexCommand.NotifyCanExecuteChanged();
    }

    private async Task RefreshStatusAsync()
    {
        if (SelectedDataset is not { } dataset)
        {
            return;
        }

        Show(await _memory.GetStatusAsync(dataset.Id, CancellationToken.None).ConfigureAwait(true));
    }

    private void Show(MemoryIndexStatus status)
    {
        _state = status.State;
        StatusText = status.Error switch
        {
            MemoryError.None => UiText.For(status.State, status.Items, status.Model),
            MemoryError.Model => UiText.For(status.LlmError),
            var e => UiText.For(e),
        };
        OnPropertyChanged(nameof(BuildButtonText));
    }

    private async Task BuildAsync()
    {
        IsBusy = true;
        Progress = 0;
        StatusText = "기억 색인을 만드는 중입니다… (이 PC의 로컬 AI로 처리)";
        try
        {
            var progress = new Progress<double>(p => Progress = Math.Round(p * 100));
            Show(await _memory.BuildIndexAsync(SelectedDataset!.Id, progress, CancellationToken.None).ConfigureAwait(true));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SearchAsync()
    {
        IsBusy = true;
        Cards.Clear();
        ResultText = null;
        ModeText = null;
        try
        {
            var result = await _memory.SearchAsync(SelectedDataset!.Id, Query, CancellationToken.None).ConfigureAwait(true);
            if (result.Error != MemoryError.None)
            {
                ResultText = UiText.For(result.Error);
                return;
            }

            foreach (var card in result.Cards)
            {
                Cards.Add(card);
            }

            ModeText = UiText.For(result.Mode);
            ResultText = result.NotFound
                ? UiText.NotFoundInRecords
                : string.Create(CultureInfo.InvariantCulture, $"관련 기록 {result.Cards.Count}건 · 모두 실제 기록 원문입니다.");
        }
        finally
        {
            IsBusy = false;
        }
    }
}
