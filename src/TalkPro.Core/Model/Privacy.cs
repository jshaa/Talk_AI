using TalkPro.Core.Diagnostics;

namespace TalkPro.Core.Model;

/// <summary>What a privacy finding is. Detection never alters the raw message.</summary>
public enum PiiType
{
    // L1 patterns
    Phone,
    Email,
    ResidentId,
    Card,
    Account,
    IpAddress,
    Url,
    Address,

    // L2 dictionary
    Person,
    Title,
    Organization,

    // L3 context (sensitive categories, PIPA Art. 23 and other high-risk topics)
    Health,
    MentalHealth,
    Religion,
    Political,
    Sexual,
    Criminal,
    Finance,
}

public enum PiiCategory
{
    /// <summary>Unique identifiers (resident id, card, account): never kept in clear outside the screen.</summary>
    HighRiskIdentifier,
    Identifier,
    Name,
    Organization,

    /// <summary>Topic of the message (health, religion, …): message-level, cannot be "masked" meaningfully.</summary>
    Sensitive,
}

public enum DetectionLayer
{
    L1Pattern,
    L2Dictionary,
    L3Context,
}

/// <summary>User decision for a finding (L4 review).</summary>
public enum PrivacyAction
{
    /// <summary>Use as is (high-risk identifiers are still masked outside the local screen).</summary>
    Keep,

    /// <summary>Replace with a token in analysis and LLM views.</summary>
    Mask,

    /// <summary>Keep the encrypted original but leave the whole message out of search, analysis and LLM.</summary>
    Exclude,

    /// <summary>Do not store the message at all.</summary>
    Delete,
}

/// <summary>Where a text view is used. Each purpose has its own redaction rules.</summary>
public enum RedactionPurpose
{
    /// <summary>The user's own local screen.</summary>
    Display,

    /// <summary>Local memory search index.</summary>
    LocalSearch,

    /// <summary>Statistics and style analysis.</summary>
    Analysis,

    /// <summary>Prompts for the local LLM.</summary>
    LlmPrompt,

    /// <summary>Not available in MVP1 (DD-014): always refused.</summary>
    CloudTransfer,
}

public readonly record struct FindingId(int Value) : ILogSafe
{
    public override string ToString() => "f" + Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>A detected span. Holds offsets only, never the matched text.</summary>
public sealed record PiiFinding(FindingId Id, MessageId MessageId, int Start, int Length, PiiType Type, double Confidence, DetectionLayer Layer) : ILogSafe
{
    public PiiCategory Category => PiiTypes.CategoryOf(Type);

    public int End => Start + Length;

    public override string ToString() => $"PiiFinding {{ Id = {Id}, Message = {MessageId}, Type = {Type}, Layer = {Layer} }}";
}

public static class PiiTypes
{
    public static PiiCategory CategoryOf(PiiType type) => type switch
    {
        PiiType.ResidentId or PiiType.Card or PiiType.Account => PiiCategory.HighRiskIdentifier,
        PiiType.Phone or PiiType.Email or PiiType.IpAddress or PiiType.Url or PiiType.Address => PiiCategory.Identifier,
        PiiType.Person or PiiType.Title => PiiCategory.Name,
        PiiType.Organization => PiiCategory.Organization,
        _ => PiiCategory.Sensitive,
    };

    /// <summary>Conservative defaults: identifiers and names masked, sensitive topics excluded.</summary>
    public static PrivacyAction DefaultAction(PiiType type) =>
        CategoryOf(type) == PiiCategory.Sensitive ? PrivacyAction.Exclude : PrivacyAction.Mask;

    /// <summary>High-risk identifiers cannot be kept; sensitive topics cannot be masked.</summary>
    public static IReadOnlyList<PrivacyAction> AllowedActions(PiiType type) => CategoryOf(type) switch
    {
        PiiCategory.HighRiskIdentifier => [PrivacyAction.Mask, PrivacyAction.Exclude, PrivacyAction.Delete],
        PiiCategory.Sensitive => [PrivacyAction.Keep, PrivacyAction.Exclude, PrivacyAction.Delete],
        _ => [PrivacyAction.Keep, PrivacyAction.Mask, PrivacyAction.Exclude, PrivacyAction.Delete],
    };
}

/// <summary>
/// A reviewed import ready for storage: speaker review + privacy review applied. Deleted messages are
/// already removed; <see cref="SearchText"/> holds the LocalSearch view of every included message.
/// </summary>
public sealed record PrivacyReviewedImport(
    ReviewedChat Chat,
    IReadOnlyList<PiiFinding> Findings,
    IReadOnlyDictionary<FindingId, PrivacyAction> Decisions,
    IReadOnlySet<MessageId> ExcludedMessages,
    IReadOnlyDictionary<MessageId, string> SearchText,
    int DeletedMessageCount)
{
    public override string ToString() =>
        $"PrivacyReviewedImport {{ Messages = {Chat.Messages.Count}, Findings = {Findings.Count}, Excluded = {ExcludedMessages.Count}, Deleted = {DeletedMessageCount} }}";
}
