using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TalkPro.Core.Model;

namespace TalkPro.Presentation.ViewModels;

public sealed record NavigationSection(string Title, string AvailableIn)
{
    public override string ToString() => $"NavigationSection {{ AvailableIn = {AvailableIn} }}";
}

/// <summary>Shell: persistent notices and navigation between the dataset list, the import wizard and analysis.</summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly Func<ImportWizardViewModel> _createWizard;
    private readonly Func<AnalyticsViewModel> _createAnalytics;
    private readonly Func<LocalAiViewModel> _createLocalAi;
    private readonly Func<ReplyDraftViewModel> _createDrafts;
    private readonly Func<MemorySearchViewModel> _createMemory;
    private object _currentPage;

    public MainViewModel(
        DatasetsViewModel datasets,
        Func<ImportWizardViewModel> createWizard,
        Func<AnalyticsViewModel> createAnalytics,
        Func<LocalAiViewModel> createLocalAi,
        Func<ReplyDraftViewModel> createDrafts,
        Func<MemorySearchViewModel> createMemory)
    {
        ArgumentNullException.ThrowIfNull(datasets);
        Datasets = datasets;
        _createWizard = createWizard;
        _createAnalytics = createAnalytics;
        _createLocalAi = createLocalAi;
        _createDrafts = createDrafts;
        _createMemory = createMemory;
        _currentPage = datasets;
        datasets.AnalyzeRequested += OnAnalyzeRequested;
        ShowDatasetsCommand = new RelayCommand(ShowDatasets);
        ShowImportCommand = new RelayCommand(ShowImport);
        ShowLocalAiCommand = new RelayCommand(ShowLocalAi);
        ShowDraftsCommand = new RelayCommand(() => CurrentPage = _createDrafts());
        ShowMemoryCommand = new RelayCommand(() => CurrentPage = _createMemory());
        datasets.Refresh();
    }

    public string LocalOnlyNotice { get; } =
        "모든 대화 데이터는 이 PC 안에서만 처리됩니다 · 외부 전송 없음 · 사용 통계 수집 없음";

    // Prior notice required by the AI Basic Act (Art. 31(1)) for generative-AI-based products.
    public string GenerativeAiNotice { get; } =
        "이 앱은 생성형 AI(로컬 모델)를 사용합니다. AI가 만든 결과물에는 'AI 생성' 표시가 붙습니다.";

    public IReadOnlyList<NavigationSection> UpcomingSections { get; } =
    [
        new("나 자신과 대화", "Phase 6.5"),
    ];

    public DatasetsViewModel Datasets { get; }

    public object CurrentPage
    {
        get => _currentPage;
        private set => SetProperty(ref _currentPage, value);
    }

    public RelayCommand ShowDatasetsCommand { get; }

    public RelayCommand ShowImportCommand { get; }

    public RelayCommand ShowLocalAiCommand { get; }

    public RelayCommand ShowDraftsCommand { get; }

    public RelayCommand ShowMemoryCommand { get; }

    private void ShowDatasets()
    {
        Datasets.Refresh();
        CurrentPage = Datasets;
    }

    private void ShowLocalAi()
    {
        var page = _createLocalAi();
        CurrentPage = page;
        page.CheckCommand.Execute(null);
    }

    private void ShowImport()
    {
        var wizard = _createWizard();
        wizard.ImportCompleted += OnImportCompleted;
        CurrentPage = wizard;
    }

    /// <summary>The last analysis task, so callers (and tests) can await it.</summary>
    public Task? PendingAnalysis { get; private set; }

    private void OnAnalyzeRequested(object? sender, DatasetRow row)
    {
        var page = _createAnalytics();
        CurrentPage = page;
        PendingAnalysis = page.LoadAsync(row.Id, row.Title);
    }

    private void OnImportCompleted(object? sender, DatasetId id)
    {
        if (sender is ImportWizardViewModel wizard)
        {
            wizard.ImportCompleted -= OnImportCompleted;
        }

        Datasets.Refresh();
        Datasets.StatusMessage = "가져오기를 완료했습니다. 데이터는 이 PC에 암호화되어 저장되었습니다.";
        CurrentPage = Datasets;
    }
}
