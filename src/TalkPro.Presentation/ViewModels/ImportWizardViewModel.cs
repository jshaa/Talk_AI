using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TalkPro.Core.Model;
using TalkPro.Ingestion.Speakers;
using TalkPro.Presentation.Abstractions;
using TalkPro.Presentation.Services;
using TalkPro.Presentation.Text;

namespace TalkPro.Presentation.ViewModels;

public enum ImportStep
{
    /// <summary>The user confirms the attestation before any file is read (report 9.5).</summary>
    Attestation,
    SelectFile,
    ReviewSpeakers,

    /// <summary>L4 privacy review: keep / mask / exclude / delete per finding (Phase 3.2).</summary>
    PrivacyReview,
    Completed,
}

/// <summary>A participant as shown in the review list. Contains personal data; ToString prints none.</summary>
public sealed record ParticipantRow(
    ParticipantId Id,
    string DisplayName,
    string AliasesText,
    int MessageCount,
    string ConsentText,
    bool IsOwner,
    bool IsExcluded,
    string FirstSeen,
    string LastSeen)
{
    public string ExcludeActionText => IsExcluded ? "포함" : "제외";

    public override string ToString() => $"ParticipantRow {{ Id = {Id} }}";
}

public sealed record SuspicionRow(int Id, string Description, bool IsOpen, string StatusText)
{
    public override string ToString() => $"SuspicionRow {{ Id = {Id}, Open = {IsOpen} }}";
}

/// <summary>
/// Import wizard: attestation → file → speaker review (owner, merge, split, exclude, acknowledge) →
/// privacy review (keep / mask / exclude / delete) → encrypted save.
/// Wraps the immutable <see cref="SpeakerReview"/>; all I/O goes through <see cref="IImportWorkflow"/>.
/// </summary>
public sealed partial class ImportWizardViewModel : ObservableObject
{
    private const int SampleCount = 3;
    private const int SampleLength = 80;

    private readonly IImportWorkflow _workflow;
    private readonly IDialogService _dialogs;
    private Dictionary<MessageId, ChatMessage> _messages = [];
    private SpeakerReview? _review;
    private ImportStep _step;
    private bool _attestationChecked;
    private bool _isBusy;
    private string? _errorMessage;
    private string? _fileSummary;
    private string? _label;
    private ParticipantRow? _selectedParticipant;
    private IReadOnlyList<string> _selectedSamples = [];
    private ParticipantRow? _mergeKeep;
    private ParticipantRow? _mergeAbsorb;
    private DateTime? _splitDate;
    private DatasetId? _savedDatasetId;

    public ImportWizardViewModel(IImportWorkflow workflow, IDialogService dialogs)
    {
        _workflow = workflow;
        _dialogs = dialogs;
        ContinueCommand = new RelayCommand(Continue, () => AttestationChecked);
        PickFileCommand = new AsyncRelayCommand(PickFileAsync, () => !IsBusy);
        SetOwnerCommand = new RelayCommand<ParticipantRow>(row => Apply(r => r.SelectOwner(row!.Id)), row => row is not null);
        ToggleExcludedCommand = new RelayCommand<ParticipantRow>(row => Apply(r => r.SetExcluded(row!.Id, !row.IsExcluded)), row => row is not null);
        AcknowledgeCommand = new RelayCommand<SuspicionRow>(row => Apply(r => r.Acknowledge(row!.Id)), row => row is { IsOpen: true });
        MergeCommand = new RelayCommand(Merge, () => MergeKeep is not null && MergeAbsorb is not null);
        SplitCommand = new RelayCommand(Split, () => SelectedParticipant is not null && SplitDate is not null);
        ContinueToPrivacyCommand = new AsyncRelayCommand(ContinueToPrivacyAsync, () => !IsBusy && _review is { CanComplete: true });
        BackToSpeakersCommand = new RelayCommand(BackToSpeakers);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy && Step == ImportStep.PrivacyReview && _privacy is not null);
        RestartCommand = new RelayCommand(Restart);
    }

    public event EventHandler<DatasetId>? ImportCompleted;

    public string AttestationText { get; } = ImportAttestation.Text;

    public ImportStep Step
    {
        get => _step;
        private set
        {
            if (SetProperty(ref _step, value))
            {
                OnPropertyChanged(nameof(IsAttestationStep));
                OnPropertyChanged(nameof(IsSelectFileStep));
                OnPropertyChanged(nameof(IsReviewStep));
                OnPropertyChanged(nameof(IsPrivacyStep));
                OnPropertyChanged(nameof(IsCompletedStep));
                SaveCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsAttestationStep => Step == ImportStep.Attestation;

    public bool IsSelectFileStep => Step == ImportStep.SelectFile;

    public bool IsReviewStep => Step == ImportStep.ReviewSpeakers;

    public bool IsPrivacyStep => Step == ImportStep.PrivacyReview;

    public bool IsCompletedStep => Step == ImportStep.Completed;

    public bool AttestationChecked
    {
        get => _attestationChecked;
        set
        {
            if (SetProperty(ref _attestationChecked, value))
            {
                ContinueCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                PickFileCommand.NotifyCanExecuteChanged();
                ContinueToPrivacyCommand.NotifyCanExecuteChanged();
                SaveCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public string? FileSummary
    {
        get => _fileSummary;
        private set => SetProperty(ref _fileSummary, value);
    }

    /// <summary>Optional dataset label (stored encrypted).</summary>
    public string? Label
    {
        get => _label;
        set => SetProperty(ref _label, value);
    }

    public ObservableCollection<ParticipantRow> Participants { get; } = [];

    public ObservableCollection<SuspicionRow> Suspicions { get; } = [];

    public ObservableCollection<string> ValidationMessages { get; } = [];

    public ParticipantRow? SelectedParticipant
    {
        get => _selectedParticipant;
        set
        {
            if (SetProperty(ref _selectedParticipant, value))
            {
                SelectedSamples = value is null ? [] : Samples(value.Id);
                SplitCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>A few of the selected speaker's messages, shown locally to help the user recognise themselves.</summary>
    public IReadOnlyList<string> SelectedSamples
    {
        get => _selectedSamples;
        private set => SetProperty(ref _selectedSamples, value);
    }

    public ParticipantRow? MergeKeep
    {
        get => _mergeKeep;
        set
        {
            if (SetProperty(ref _mergeKeep, value))
            {
                MergeCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public ParticipantRow? MergeAbsorb
    {
        get => _mergeAbsorb;
        set
        {
            if (SetProperty(ref _mergeAbsorb, value))
            {
                MergeCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public DateTime? SplitDate
    {
        get => _splitDate;
        set
        {
            if (SetProperty(ref _splitDate, value))
            {
                SplitCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public DatasetId? SavedDatasetId
    {
        get => _savedDatasetId;
        private set => SetProperty(ref _savedDatasetId, value);
    }

    /// <summary>The speaker review is complete, so the privacy step can start.</summary>
    public bool CanContinueToPrivacy => _review is { CanComplete: true };

    public RelayCommand ContinueCommand { get; }

    public AsyncRelayCommand PickFileCommand { get; }

    public RelayCommand<ParticipantRow> SetOwnerCommand { get; }

    public RelayCommand<ParticipantRow> ToggleExcludedCommand { get; }

    public RelayCommand<SuspicionRow> AcknowledgeCommand { get; }

    public RelayCommand MergeCommand { get; }

    public RelayCommand SplitCommand { get; }

    public AsyncRelayCommand ContinueToPrivacyCommand { get; }

    public RelayCommand BackToSpeakersCommand { get; }

    public AsyncRelayCommand SaveCommand { get; }

    public RelayCommand RestartCommand { get; }

    private void Continue()
    {
        ErrorMessage = null;
        Step = ImportStep.SelectFile;
    }

    private async Task PickFileAsync()
    {
        var path = await _dialogs.PickExportFileAsync().ConfigureAwait(true);
        if (path is null)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            ImportParseResult result;
            try
            {
                result = await _workflow.ParseAsync(path, CancellationToken.None).ConfigureAwait(true);
            }
            finally
            {
                _dialogs.ReleasePickedFile(path);
            }

            if (!result.Succeeded)
            {
                ErrorMessage = UiText.ForImport(result);
                return;
            }

            var chat = result.Chat!;
            _messages = chat.Messages.ToDictionary(m => m.Id);
            _review = SpeakerReview.Start(chat).ConfirmAttestation();
            FileSummary = string.Create(UiText.Korean,
                $"형식 {chat.FormatId} · 인코딩 {UiText.Encoding(chat.Encoding)} · 메시지 {chat.Messages.Count:N0}개 · 참여자 {chat.Participants.Count}명 · 문제 줄 {chat.Issues.Count}개");
            Step = ImportStep.ReviewSpeakers;
            Refresh();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ErrorMessage = "파일을 처리하지 못했습니다.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Merge()
    {
        var keep = MergeKeep!.Id;
        var absorb = MergeAbsorb!.Id;
        if (Apply(r => r.Merge(keep, absorb)))
        {
            MergeKeep = null;
            MergeAbsorb = null;
        }
    }

    private void Split()
    {
        var day = SplitDate!.Value.Date;
        var from = new DateTimeOffset(day, TimeZoneInfo.Local.GetUtcOffset(day));
        Apply(r => r.SplitFrom(SelectedParticipant!.Id, from));
    }

    private async Task SaveAsync()
    {
        if (_reviewedChat is null || _privacy is null)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var import = TalkPro.Privacy.PrivacyImportBuilder.Build(_reviewedChat, _privacy);
            var id = await _workflow.SaveAsync(import, Label, CancellationToken.None).ConfigureAwait(true);
            SavedDatasetId = id;
            Step = ImportStep.Completed;
            ImportCompleted?.Invoke(this, id);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ErrorMessage = "저장하지 못했습니다. 저장 공간을 확인한 뒤 다시 시도해 주세요.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Restart()
    {
        _review = null;
        _messages = [];
        AttestationChecked = false;
        Label = null;
        FileSummary = null;
        ErrorMessage = null;
        SavedDatasetId = null;
        Participants.Clear();
        Suspicions.Clear();
        ValidationMessages.Clear();
        SelectedParticipant = null;
        ResetPrivacy();
        Step = ImportStep.Attestation;
        SaveCommand.NotifyCanExecuteChanged();
    }

    private bool Apply(Func<SpeakerReview, ReviewStep> operation)
    {
        if (_review is null)
        {
            return false;
        }

        var step = operation(_review);
        ErrorMessage = step.Succeeded ? null : UiText.For(step.Error);
        if (step.Succeeded)
        {
            _review = step.Review;
            Refresh();
        }

        return step.Succeeded;
    }

    private void Refresh()
    {
        var review = _review!;
        var selectedId = SelectedParticipant?.Id;

        Participants.Clear();
        foreach (var p in review.Participants)
        {
            var times = review.MessagesOf(p.Id).Select(id => _messages[id].Timestamp).ToList();
            Participants.Add(new ParticipantRow(
                p.Id,
                p.DisplayName,
                string.Join(", ", p.Aliases),
                p.MessageCount,
                UiText.For(p.Consent),
                p.Consent == ParticipantConsent.Owner,
                p.Consent == ParticipantConsent.Excluded,
                UiText.FormatDate(times.Count > 0 ? times.Min() : null),
                UiText.FormatDate(times.Count > 0 ? times.Max() : null)));
        }

        var names = review.Participants.ToDictionary(p => p.Id, p => p.DisplayName);
        Suspicions.Clear();
        foreach (var s in review.Suspicions)
        {
            var status = s.Status switch
            {
                SuspicionStatus.Open => "확인 필요",
                SuspicionStatus.Acknowledged => "확인함",
                _ => "해결됨",
            };
            Suspicions.Add(new SuspicionRow(s.Id, UiText.Describe(s, id => names.GetValueOrDefault(id, id.Value)), s.Status == SuspicionStatus.Open, status));
        }

        ValidationMessages.Clear();
        foreach (var error in review.Validate())
        {
            ValidationMessages.Add(UiText.For(error));
        }

        SelectedParticipant = Participants.FirstOrDefault(r => r.Id == selectedId);
        OnPropertyChanged(nameof(CanContinueToPrivacy));
        ContinueToPrivacyCommand.NotifyCanExecuteChanged();
    }

    private string[] Samples(ParticipantId id) =>
        [.. _review is null ? [] : _review.MessagesOf(id).Take(SampleCount).Select(m => Truncate(_messages[m].Text))];

    private static string Truncate(string text)
    {
        var info = new StringInfo(text.ReplaceLineEndings(" "));
        return info.LengthInTextElements <= SampleLength ? info.String : info.SubstringByTextElements(0, SampleLength) + "…";
    }
}
