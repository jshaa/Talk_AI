using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using TalkPro.Core.Model;
using TalkPro.Privacy;
using TalkPro.Presentation.Text;

namespace TalkPro.Presentation.ViewModels;

/// <summary>An action choice for a combo box.</summary>
public sealed record ActionOption(PrivacyAction Action, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Bulk decision for all findings of one type.</summary>
public sealed class PrivacyTypeRow : ObservableObject
{
    private readonly Func<PiiType, PrivacyAction, bool> _apply;
    private ActionOption? _selected;

    internal PrivacyTypeRow(PiiType type, int count, IReadOnlyList<ActionOption> options, Func<PiiType, PrivacyAction, bool> apply)
    {
        Type = type;
        Count = count;
        Options = options;
        _apply = apply;
    }

    public PiiType Type { get; }

    public string Label => UiText.For(Type);

    public string CategoryLabel => UiText.For(PiiTypes.CategoryOf(Type));

    public int Count { get; }

    public IReadOnlyList<ActionOption> Options { get; }

    /// <summary>Null when findings of this type have mixed decisions.</summary>
    public ActionOption? Selected
    {
        get => _selected;
        set
        {
            if (value is not null && !Equals(value, _selected) && _apply(Type, value.Action))
            {
                SetProperty(ref _selected, value);
            }
        }
    }

    internal void Show(PrivacyAction? action) => SetProperty(ref _selected, action is { } a ? Options.FirstOrDefault(o => o.Action == a) : null, nameof(Selected));

    public override string ToString() => $"PrivacyTypeRow {{ Type = {Type}, Count = {Count} }}";
}

/// <summary>One finding with its decision. <see cref="Excerpt"/> shows local message text; ToString prints none.</summary>
public sealed class PrivacyFindingRow : ObservableObject
{
    private readonly Func<FindingId, PrivacyAction, bool> _apply;
    private ActionOption? _selected;

    internal PrivacyFindingRow(PiiFinding finding, string excerpt, IReadOnlyList<ActionOption> options, Func<FindingId, PrivacyAction, bool> apply)
    {
        Finding = finding;
        Excerpt = excerpt;
        Options = options;
        _apply = apply;
    }

    public PiiFinding Finding { get; }

    public FindingId Id => Finding.Id;

    public string TypeLabel => UiText.For(Finding.Type);

    public string Excerpt { get; }

    public IReadOnlyList<ActionOption> Options { get; }

    public ActionOption? Selected
    {
        get => _selected;
        set
        {
            if (value is not null && !Equals(value, _selected) && _apply(Finding.Id, value.Action))
            {
                SetProperty(ref _selected, value);
            }
        }
    }

    internal void Show(PrivacyAction action) => SetProperty(ref _selected, Options.FirstOrDefault(o => o.Action == action), nameof(Selected));

    public override string ToString() => $"PrivacyFindingRow {{ Id = {Id}, Type = {Finding.Type} }}";
}

public sealed partial class ImportWizardViewModel
{
    private const int ExcerptMargin = 20;

    private ReviewedChat? _reviewedChat;
    private PrivacyReview? _privacy;
    private Dictionary<MessageId, ChatMessage> _reviewedMessages = [];
    private PrivacyFindingRow? _selectedFinding;
    private string? _privacySummary;
    private string? _originalPreview;
    private string? _llmPreview;

    public ObservableCollection<PrivacyTypeRow> PrivacyTypes { get; } = [];

    public ObservableCollection<PrivacyFindingRow> PrivacyFindings { get; } = [];

    public string PrivacyNotice { get; } =
        "자동 탐지는 완전하지 않습니다. 특히 대화 참여자가 아닌 사람의 이름은 놓칠 수 있으니 목록 밖의 민감한 내용도 확인해 주세요. 민감한 주제(건강·종교·정치 등)는 기본적으로 분석에서 제외됩니다.";

    public string? PrivacySummary
    {
        get => _privacySummary;
        private set => SetProperty(ref _privacySummary, value);
    }

    public PrivacyFindingRow? SelectedFinding
    {
        get => _selectedFinding;
        set
        {
            if (SetProperty(ref _selectedFinding, value))
            {
                UpdatePreview();
            }
        }
    }

    /// <summary>The selected message as shown on this PC (high-risk identifiers masked).</summary>
    public string? OriginalPreview
    {
        get => _originalPreview;
        private set => SetProperty(ref _originalPreview, value);
    }

    /// <summary>What a local LLM prompt would receive for the selected message.</summary>
    public string? LlmPreview
    {
        get => _llmPreview;
        private set => SetProperty(ref _llmPreview, value);
    }

    public bool HasNoFindings => _privacy is { Findings.Length: 0 };

    private async Task ContinueToPrivacyAsync()
    {
        var completion = _review!.Complete();
        if (!completion.Succeeded)
        {
            Refresh();
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var chat = completion.Chat!;
            var findings = await _workflow.ScanAsync(chat, CancellationToken.None).ConfigureAwait(true);
            _reviewedChat = chat;
            _reviewedMessages = chat.Messages.ToDictionary(m => m.Id);
            _privacy = PrivacyReview.Start(findings, chat.Messages.Count);
            BuildPrivacyRows();
            Step = ImportStep.PrivacyReview;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ErrorMessage = "개인정보 검사를 완료하지 못했습니다.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void BackToSpeakers()
    {
        ResetPrivacy();
        ErrorMessage = null;
        Step = ImportStep.ReviewSpeakers;
    }

    private void ResetPrivacy()
    {
        _reviewedChat = null;
        _privacy = null;
        _reviewedMessages = [];
        PrivacyTypes.Clear();
        PrivacyFindings.Clear();
        SelectedFinding = null;
        PrivacySummary = null;
        OnPropertyChanged(nameof(HasNoFindings));
    }

    private void BuildPrivacyRows()
    {
        var privacy = _privacy!;
        PrivacyTypes.Clear();
        foreach (var group in privacy.Findings.GroupBy(f => f.Type).OrderBy(g => PiiTypes.CategoryOf(g.Key)).ThenBy(g => g.Key))
        {
            PrivacyTypes.Add(new PrivacyTypeRow(group.Key, group.Count(), OptionsFor(group.Key), ApplyToType));
        }

        PrivacyFindings.Clear();
        foreach (var finding in privacy.Findings)
        {
            PrivacyFindings.Add(new PrivacyFindingRow(finding, Excerpt(finding), OptionsFor(finding.Type), ApplyToFinding));
        }

        SelectedFinding = PrivacyFindings.FirstOrDefault();
        ShowDecisions();
        OnPropertyChanged(nameof(HasNoFindings));
    }

    private bool ApplyToFinding(FindingId id, PrivacyAction action) => ApplyPrivacy(p => p.SetAction(id, action));

    private bool ApplyToType(PiiType type, PrivacyAction action) => ApplyPrivacy(p => p.SetActionForType(type, action));

    private bool ApplyPrivacy(Func<PrivacyReview, PrivacyStep> operation)
    {
        if (_privacy is null)
        {
            return false;
        }

        var step = operation(_privacy);
        ErrorMessage = step.Succeeded ? null : UiText.For(step.Error);
        if (step.Succeeded)
        {
            _privacy = step.Review;
            ShowDecisions();
        }

        return step.Succeeded;
    }

    /// <summary>Updates combo selections in place (rows are not recreated while the user edits them).</summary>
    private void ShowDecisions()
    {
        var privacy = _privacy!;
        foreach (var row in PrivacyFindings)
        {
            row.Show(privacy.ActionFor(row.Id));
        }

        foreach (var row in PrivacyTypes)
        {
            var actions = privacy.Findings.Where(f => f.Type == row.Type).Select(f => privacy.ActionFor(f.Id)).Distinct().ToList();
            row.Show(actions.Count == 1 ? actions[0] : null);
        }

        var summary = privacy.Summary();
        PrivacySummary = string.Create(UiText.Korean,
            $"탐지 {summary.Findings:N0}건 · 저장할 메시지 {summary.MessagesToStore:N0}개 · 분석 제외 {summary.ExcludedMessages:N0}개 · 삭제 {summary.DeletedMessages:N0}개");
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (_privacy is null || SelectedFinding is null || !_reviewedMessages.TryGetValue(SelectedFinding.Finding.MessageId, out var message))
        {
            OriginalPreview = null;
            LlmPreview = null;
            return;
        }

        var findings = _privacy.Findings.Where(f => f.MessageId == message.Id).ToList();
        var pseudonyms = new PseudonymMap();
        OriginalPreview = PurposeRedactor.Render(message, findings, _privacy.ActionFor, RedactionPurpose.Display, pseudonyms) ?? "(저장하지 않음)";
        LlmPreview = PurposeRedactor.Render(message, findings, _privacy.ActionFor, RedactionPurpose.LlmPrompt, pseudonyms) ?? "(분석·AI에서 제외됨)";
    }

    private string Excerpt(PiiFinding finding)
    {
        var text = _reviewedMessages[finding.MessageId].Text;
        var from = Math.Max(0, finding.Start - ExcerptMargin);
        var to = Math.Min(text.Length, finding.End + ExcerptMargin);
        if (from > 0 && char.IsLowSurrogate(text[from]))
        {
            from--;
        }

        if (to < text.Length && char.IsLowSurrogate(text[to]))
        {
            to++;
        }

        var excerpt = string.Concat(
            from > 0 ? "…" : string.Empty,
            text[from..finding.Start],
            "「",
            text[finding.Start..finding.End],
            "」",
            text[finding.End..to],
            to < text.Length ? "…" : string.Empty);
        return excerpt.ReplaceLineEndings(" ");
    }

    private static List<ActionOption> OptionsFor(PiiType type) =>
        [.. PiiTypes.AllowedActions(type).Select(a => new ActionOption(a, UiText.For(a)))];
}
