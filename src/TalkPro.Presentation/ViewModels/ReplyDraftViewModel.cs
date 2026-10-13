using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TalkPro.Core.Model;
using TalkPro.Persona.Style;
using TalkPro.Presentation.Abstractions;
using TalkPro.Presentation.Services;
using TalkPro.Presentation.Text;

namespace TalkPro.Presentation.ViewModels;

public sealed record DatasetChoice(DatasetId Id, string Title)
{
    public override string ToString() => Title;
}

/// <summary>"(지정 안 함)" or a counterpart; the display name is personal data shown locally only.</summary>
public sealed record TargetChoice(ParticipantId? Id, string Name)
{
    public override string ToString() => Name;
}

public sealed record DirectionOption(ReplyDirection Value, string Label)
{
    public static IReadOnlyList<DirectionOption> All { get; } =
        [.. Enum.GetValues<ReplyDirection>().Select(d => new DirectionOption(d, UiText.For(d)))];

    public static DirectionOption Of(ReplyDirection direction) => All.Single(o => o.Value == direction);

    public override string ToString() => Label;
}

/// <summary>
/// One draft card: the direction the user wants, the direction the current text was generated for, and a
/// command that regenerates only this card. Text is kept in memory only.
/// </summary>
public sealed class DraftRow : ObservableObject
{
    private readonly ReplyDraftViewModel _owner;
    private DirectionOption _selected;
    private ReplyDirection? _generated;
    private string? _text;
    private string? _matchText;
    private string? _note;
    private bool _isBusy;

    internal DraftRow(ReplyDraftViewModel owner, int slot, ReplyDirection direction)
    {
        _owner = owner;
        Slot = slot;
        _selected = DirectionOption.Of(direction);
        RegenerateCommand = new AsyncRelayCommand(() => _owner.RegenerateAsync(this), () => !IsBusy && _owner.CanRegenerate);
    }

    public int Slot { get; }

    /// <summary>The direction the user chose for this card (ComboBox).</summary>
    public DirectionOption SelectedDirection
    {
        get => _selected;
        set
        {
            if (value is not null && SetProperty(ref _selected, value))
            {
                Note = Text is not null && _generated != value.Value ? UiText.DirectionChanged : null;
                _owner.OnDirectionsChanged();
            }
        }
    }

    public ReplyDirection Direction => _selected.Value;

    /// <summary>Label of the direction the shown text was written for.</summary>
    public string DirectionLabel => UiText.For(_generated ?? Direction);

    public string? Text
    {
        get => _text;
        private set => SetProperty(ref _text, value);
    }

    public string? MatchText
    {
        get => _matchText;
        private set => SetProperty(ref _matchText, value);
    }

    /// <summary>Per-card hint or error ("방향을 바꿨습니다…", "이 방향의 초안을 만들지 못했습니다…").</summary>
    public string? Note
    {
        get => _note;
        internal set => SetProperty(ref _note, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        internal set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RegenerateCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public AsyncRelayCommand RegenerateCommand { get; }

    public override string ToString() => $"DraftRow {{ Slot = {Slot}, Direction = {Direction}, Length = {Text?.Length ?? 0} }}";

    internal void Show(DraftItem item)
    {
        _generated = item.Direction;
        Text = item.Text;
        MatchText = string.Create(CultureInfo.InvariantCulture, $"말투 일치도 {item.StyleMatch}");
        Note = null;
        OnPropertyChanged(nameof(DirectionLabel));
    }

    internal void ShowMissing(string note)
    {
        _generated = Direction;
        Text = null;
        MatchText = null;
        Note = note;
        OnPropertyChanged(nameof(DirectionLabel));
    }
}

/// <summary>
/// "내 말투 답장 초안" (Phase 6/6.6). The owner pastes a message they received; the local model writes one
/// draft per direction — 수락, 보류, 되묻기 by default. Each card's direction can be changed and that card
/// regenerated alone. Drafts are labelled "AI 생성 초안", can only be copied (the clipboard is cleared again
/// after <see cref="ClipboardLifetime"/>), and are never sent or stored. There is no send function.
/// </summary>
public sealed class ReplyDraftViewModel : ObservableObject
{
    public static readonly TimeSpan ClipboardLifetime = TimeSpan.FromSeconds(60);

    private readonly IReplyDraftService _drafts;
    private readonly IClipboardService _clipboard;
    private DatasetChoice? _dataset;
    private TargetChoice? _target;
    private string _incoming = string.Empty;
    private string? _message;
    private string? _aiLabel;
    private string? _styleText;
    private string? _sameDirectionWarning;
    private bool _isBusy;

    // Inputs of the last full generation, so a single regenerated card answers the same message. Memory only.
    private (DatasetId Dataset, ParticipantId? Target, string Incoming)? _context;

    public ReplyDraftViewModel(IDatasetCatalog catalog, IReplyDraftService drafts, IClipboardService clipboard)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _drafts = drafts;
        _clipboard = clipboard;
        DraftCommand = new AsyncRelayCommand(DraftAsync, CanDraft);
        CopyCommand = new RelayCommand<DraftRow>(Copy, row => row?.Text is not null);
        foreach (var d in catalog.List().Where(d => d.State == DatasetState.Ready))
        {
            Datasets.Add(new DatasetChoice(d.Id, string.IsNullOrWhiteSpace(d.Label) ? "(이름 없음)" : d.Label));
        }
    }

    public string Notice { get; } =
        "받은 메시지를 붙여 넣으면 이 PC의 로컬 AI가 내 말투로 수락·보류·되묻기 초안을 하나씩 씁니다. 초안마다 방향을 바꿔 그 초안만 다시 만들 수 있습니다. " +
        "자동 전송 기능은 없습니다 — 복사해서 직접 확인하고 보내세요. 입력한 메시지와 초안은 저장되지 않으며, AI에는 이름·연락처가 가려진 내용만 전달됩니다. 상대방의 말투는 흉내 내지 않습니다.";

    public ObservableCollection<DatasetChoice> Datasets { get; } = [];

    public ObservableCollection<TargetChoice> Targets { get; } = [];

    public ObservableCollection<DraftRow> Drafts { get; } = [];

    public DatasetChoice? SelectedDataset
    {
        get => _dataset;
        set
        {
            if (SetProperty(ref _dataset, value))
            {
                PendingTargets = LoadTargetsAsync();
                DraftCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>The last target-loading task, so tests can await it.</summary>
    public Task? PendingTargets { get; private set; }

    public TargetChoice? SelectedTarget
    {
        get => _target;
        set => SetProperty(ref _target, value);
    }

    public string IncomingText
    {
        get => _incoming;
        set
        {
            if (SetProperty(ref _incoming, value ?? string.Empty))
            {
                DraftCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>"초안 만들기" before the first generation, "전체 다시 생성" afterwards.</summary>
    public string DraftButtonText => Drafts.Count == 0 ? UiText.DraftAll : UiText.RedraftAll;

    public string? Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public string? AiLabel
    {
        get => _aiLabel;
        private set => SetProperty(ref _aiLabel, value);
    }

    public string? StyleText
    {
        get => _styleText;
        private set => SetProperty(ref _styleText, value);
    }

    /// <summary>Shown when every card is set to the same direction (allowed, but less useful).</summary>
    public string? SameDirectionWarning
    {
        get => _sameDirectionWarning;
        private set => SetProperty(ref _sameDirectionWarning, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                DraftCommand.NotifyCanExecuteChanged();
                foreach (var row in Drafts)
                {
                    row.RegenerateCommand.NotifyCanExecuteChanged();
                }
            }
        }
    }

    /// <summary>Full generation with the default spread (수락, 보류, 되묻기) — also "전체 다시 생성".</summary>
    public AsyncRelayCommand DraftCommand { get; }

    public RelayCommand<DraftRow> CopyCommand { get; }

    internal bool CanRegenerate => !IsBusy && _context is not null;

    internal void OnDirectionsChanged() =>
        SameDirectionWarning = Drafts.Count > 1 && Drafts.All(d => d.Direction == Drafts[0].Direction) ? UiText.SameDirectionWarning : null;

    /// <summary>Regenerates only <paramref name="row"/> for its selected direction; other cards are untouched.</summary>
    internal async Task RegenerateAsync(DraftRow row)
    {
        if (_context is not { } context)
        {
            return;
        }

        IsBusy = true;
        row.IsBusy = true;
        row.Note = UiText.Regenerating;
        try
        {
            var result = await _drafts.DraftAsync(context.Dataset, context.Target, context.Incoming, [row.Direction], CancellationToken.None).ConfigureAwait(true);
            if (result.Error != DraftError.None)
            {
                row.Note = Describe(result);
                return;
            }

            if (result.Drafts.Count == 0)
            {
                row.ShowMissing(UiText.DraftMissing);
                return;
            }

            row.Show(result.Drafts[0]);
            CopyCommand.NotifyCanExecuteChanged();
        }
        finally
        {
            row.IsBusy = false;
            IsBusy = false;
        }
    }

    private static string Describe(DraftSet result) =>
        result.Error == DraftError.Model ? UiText.For(result.LlmError) : UiText.For(result.Error);

    private bool CanDraft() => !IsBusy && SelectedDataset is not null && !string.IsNullOrWhiteSpace(IncomingText);

    private async Task LoadTargetsAsync()
    {
        Targets.Clear();
        Targets.Add(new TargetChoice(null, "(지정 안 함 — 전체 말투)"));
        SelectedTarget = Targets[0];
        if (SelectedDataset is not { } dataset)
        {
            return;
        }

        var targets = await _drafts.LoadTargetsAsync(dataset.Id, CancellationToken.None).ConfigureAwait(true);
        if (targets is null)
        {
            Message = UiText.For(DraftError.Unreadable);
            return;
        }

        foreach (var t in targets)
        {
            Targets.Add(new TargetChoice(t.Id, t.DisplayName));
        }
    }

    private async Task DraftAsync()
    {
        IsBusy = true;
        Drafts.Clear();
        AiLabel = null;
        StyleText = null;
        SameDirectionWarning = null;
        Message = "내 말투를 분석하고 초안을 쓰는 중입니다… (이 PC 안에서만 처리)";
        var context = (SelectedDataset!.Id, SelectedTarget?.Id, IncomingText);
        try
        {
            var directions = ReplyDirections.Default;
            var result = await _drafts.DraftAsync(context.Item1, context.Item2, context.IncomingText, directions, CancellationToken.None).ConfigureAwait(true);
            if (result.Error != DraftError.None)
            {
                _context = null;
                Message = Describe(result);
                return;
            }

            _context = context;
            for (var slot = 0; slot < directions.Count; slot++)
            {
                var row = new DraftRow(this, slot, directions[slot]);
                if (result.Drafts.FirstOrDefault(d => d.Slot == slot) is { } item)
                {
                    row.Show(item);
                }
                else
                {
                    row.ShowMissing(UiText.DraftMissing);
                }

                Drafts.Add(row);
            }

            AiLabel = string.Create(CultureInfo.InvariantCulture, $"AI 생성 초안 · {result.Model?.ModelId} · 내 답장 예시 {result.ExampleCount}개 참고");
            StyleText = "반영한 내 말투: " + string.Join(" · ", result.StyleSummary);
            Message = "보내기 전에 내용을 꼭 확인하세요. 초안은 사실과 다를 수 있습니다.";
        }
        finally
        {
            OnPropertyChanged(nameof(DraftButtonText));
            CopyCommand.NotifyCanExecuteChanged();
            IsBusy = false;
        }
    }

    private void Copy(DraftRow? row)
    {
        _clipboard.CopyWithAutoClear(row!.Text!, ClipboardLifetime);
        Message = string.Create(CultureInfo.InvariantCulture, $"복사했습니다. {ClipboardLifetime.TotalSeconds:F0}초 후 클립보드에서 지웁니다. AI가 만든 초안이니 확인 후 보내세요.");
    }
}
