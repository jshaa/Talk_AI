using System.Collections.Immutable;
using TalkPro.Core.Model;

namespace TalkPro.Privacy;

public enum PrivacyReviewError
{
    None,
    UnknownFinding,
    ActionNotAllowed,
}

public readonly record struct PrivacyStep(PrivacyReview Review, PrivacyReviewError Error)
{
    public bool Succeeded => Error == PrivacyReviewError.None;

    public override string ToString() => $"PrivacyStep {{ Error = {Error} }}";
}

/// <summary>Counts for the review screen. Log-safe.</summary>
public sealed record PrivacySummary(int Findings, int MessagesToStore, int ExcludedMessages, int DeletedMessages, IReadOnlyDictionary<PiiType, int> ByType)
{
    public override string ToString() =>
        $"PrivacySummary {{ Findings = {Findings}, Store = {MessagesToStore}, Excluded = {ExcludedMessages}, Deleted = {DeletedMessages} }}";
}

/// <summary>
/// Immutable L4 review: one decision per finding, starting from conservative defaults
/// (<see cref="PiiTypes.DefaultAction"/>). High-risk identifiers can never be kept and sensitive
/// topics cannot be masked (<see cref="PiiTypes.AllowedActions"/>).
/// </summary>
public sealed class PrivacyReview
{
    private readonly ImmutableDictionary<FindingId, PrivacyAction> _decisions;
    private readonly int _messageCount;

    private PrivacyReview(ImmutableArray<PiiFinding> findings, ImmutableDictionary<FindingId, PrivacyAction> decisions, int messageCount)
    {
        Findings = findings;
        _decisions = decisions;
        _messageCount = messageCount;
    }

    public ImmutableArray<PiiFinding> Findings { get; }

    public IReadOnlyDictionary<FindingId, PrivacyAction> Decisions => _decisions;

    public static PrivacyReview Start(IReadOnlyList<PiiFinding> findings, int messageCount)
    {
        ArgumentNullException.ThrowIfNull(findings);
        ArgumentOutOfRangeException.ThrowIfNegative(messageCount);
        return new PrivacyReview(
            [.. findings],
            findings.ToImmutableDictionary(f => f.Id, f => PiiTypes.DefaultAction(f.Type)),
            messageCount);
    }

    public PrivacyAction ActionFor(FindingId id) => _decisions[id];

    public PrivacyStep SetAction(FindingId id, PrivacyAction action)
    {
        var finding = Findings.FirstOrDefault(f => f.Id == id);
        if (finding is null)
        {
            return new(this, PrivacyReviewError.UnknownFinding);
        }

        if (!PiiTypes.AllowedActions(finding.Type).Contains(action))
        {
            return new(this, PrivacyReviewError.ActionNotAllowed);
        }

        return new(new PrivacyReview(Findings, _decisions.SetItem(id, action), _messageCount), PrivacyReviewError.None);
    }

    /// <summary>Applies one action to every finding of a type (bulk decision on the review screen).</summary>
    public PrivacyStep SetActionForType(PiiType type, PrivacyAction action)
    {
        if (!PiiTypes.AllowedActions(type).Contains(action))
        {
            return new(this, PrivacyReviewError.ActionNotAllowed);
        }

        var updated = _decisions.SetItems(Findings.Where(f => f.Type == type).Select(f => KeyValuePair.Create(f.Id, action)));
        return new(new PrivacyReview(Findings, updated, _messageCount), PrivacyReviewError.None);
    }

    /// <summary>Messages with any finding decided as Delete: not stored at all.</summary>
    public IReadOnlySet<MessageId> DeletedMessages =>
        Findings.Where(f => _decisions[f.Id] == PrivacyAction.Delete).Select(f => f.MessageId).ToHashSet();

    /// <summary>Messages kept encrypted but left out of search, analysis and LLM (Delete wins over Exclude).</summary>
    public IReadOnlySet<MessageId> ExcludedMessages
    {
        get
        {
            var deleted = DeletedMessages;
            return Findings.Where(f => _decisions[f.Id] == PrivacyAction.Exclude && !deleted.Contains(f.MessageId)).Select(f => f.MessageId).ToHashSet();
        }
    }

    public PrivacySummary Summary()
    {
        var deleted = DeletedMessages.Count;
        return new PrivacySummary(
            Findings.Length,
            _messageCount - deleted,
            ExcludedMessages.Count,
            deleted,
            Findings.GroupBy(f => f.Type).ToDictionary(g => g.Key, g => g.Count()));
    }

    public override string ToString() => $"PrivacyReview {{ Findings = {Findings.Length} }}";
}
