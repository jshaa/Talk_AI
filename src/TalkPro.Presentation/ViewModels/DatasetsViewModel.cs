using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TalkPro.Core.Model;
using TalkPro.Infrastructure.Vault;
using TalkPro.Presentation.Abstractions;
using TalkPro.Presentation.Services;
using TalkPro.Presentation.Text;

namespace TalkPro.Presentation.ViewModels;

public sealed record DatasetRow(DatasetId Id, string Title, string CreatedText, string CountsText, string StateText, bool IsDamaged)
{
    public override string ToString() => $"DatasetRow {{ Id = {Id}, Damaged = {IsDamaged} }}";
}

/// <summary>Dataset list with single and full deletion (Phase 2.6). Deletion always asks first.</summary>
public sealed class DatasetsViewModel : ObservableObject
{
    private readonly IDatasetCatalog _catalog;
    private readonly IDialogService _dialogs;
    private DatasetRow? _selected;
    private string? _statusMessage;

    public DatasetsViewModel(IDatasetCatalog catalog, IDialogService dialogs)
    {
        _catalog = catalog;
        _dialogs = dialogs;
        RefreshCommand = new RelayCommand(Refresh);
        DeleteSelectedCommand = new AsyncRelayCommand(DeleteSelectedAsync, () => Selected is not null);
        AnalyzeSelectedCommand = new RelayCommand(AnalyzeSelected, () => Selected is { IsDamaged: false });
        DeleteAllCommand = new AsyncRelayCommand(DeleteAllAsync, () => Items.Count > 0);
    }

    public ObservableCollection<DatasetRow> Items { get; } = [];

    public DatasetRow? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
            {
                DeleteSelectedCommand.NotifyCanExecuteChanged();
                AnalyzeSelectedCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public bool IsEmpty => Items.Count == 0;

    public RelayCommand RefreshCommand { get; }

    public AsyncRelayCommand DeleteSelectedCommand { get; }

    /// <summary>Asks the shell to open the analysis page for the selected dataset.</summary>
    public RelayCommand AnalyzeSelectedCommand { get; }

    public event EventHandler<DatasetRow>? AnalyzeRequested;

    public AsyncRelayCommand DeleteAllCommand { get; }

    public void Refresh()
    {
        Items.Clear();
        foreach (var s in _catalog.List())
        {
            Items.Add(new DatasetRow(
                s.Id,
                s.State == DatasetState.Damaged ? "(손상된 데이터셋)" : string.IsNullOrWhiteSpace(s.Label) ? "(이름 없음)" : s.Label,
                UiText.FormatDate(s.CreatedAt),
                s.State == DatasetState.Damaged ? "-" : string.Create(UiText.Korean, $"참여자 {s.ParticipantCount}명 · 메시지 {s.MessageCount:N0}개"),
                s.State == DatasetState.Damaged ? "손상됨 — 삭제만 가능" : "정상",
                s.State == DatasetState.Damaged));
        }

        Selected = null;
        OnPropertyChanged(nameof(IsEmpty));
        DeleteAllCommand.NotifyCanExecuteChanged();
    }

    private void AnalyzeSelected() => AnalyzeRequested?.Invoke(this, Selected!);

    private async Task DeleteSelectedAsync()
    {
        var target = Selected!;
        if (!await _dialogs.ConfirmAsync("데이터셋 삭제", $"'{target.Title}'을(를) 삭제합니다. 되돌릴 수 없습니다.\n\n{DatasetDeletionService.UserNotice}").ConfigureAwait(true))
        {
            return;
        }

        StatusMessage = Describe([_catalog.Delete(target.Id)]);
        Refresh();
    }

    private async Task DeleteAllAsync()
    {
        var count = Items.Count;
        if (!await _dialogs.ConfirmAsync("모든 데이터 삭제", string.Create(CultureInfo.InvariantCulture, $"데이터셋 {count}개를 모두 삭제합니다. 암호화 키를 먼저 파기하므로 되돌릴 수 없습니다.\n\n{DatasetDeletionService.UserNotice}")).ConfigureAwait(true))
        {
            return;
        }

        StatusMessage = Describe(_catalog.DeleteAll());
        Refresh();
    }

    private static string Describe(IReadOnlyList<DatasetDeletionReport> reports)
    {
        var incomplete = reports.Count(r => !r.Complete);
        var residuals = reports.Sum(r => r.Residuals.Count);
        return incomplete == 0
            ? string.Create(UiText.Korean, $"삭제를 완료했습니다 ({reports.Count}개). {DatasetDeletionService.UserNotice}")
            : string.Create(UiText.Korean, $"암호화 키는 파기되었지만 파일 {residuals}개를 지우지 못했습니다(다른 프로그램이 사용 중일 수 있음). 키가 없어 내용은 읽을 수 없습니다. 앱을 다시 시작한 뒤 다시 삭제해 주세요.");
    }
}
