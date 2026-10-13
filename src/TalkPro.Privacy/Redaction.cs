using System.Text;
using TalkPro.Core.Llm;
using TalkPro.Core.Model;

namespace TalkPro.Privacy;

/// <summary>
/// Stable pseudonyms for one dataset: the same name text always becomes the same token
/// (<c>[인물1]</c>, <c>[기관1]</c>), so masked conversations stay coherent.
/// </summary>
public sealed class PseudonymMap
{
    private readonly Dictionary<string, string> _people = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _organizations = new(StringComparer.Ordinal);

    public string Person(string text) => Token(_people, text, "인물");

    public string Organization(string text) => Token(_organizations, text, "기관");

    /// <summary>
    /// Puts the original names back into model output for local display only (the model never saw them).
    /// Unknown tokens and identifier tokens such as [전화번호] stay as they are.
    /// </summary>
    public string Restore(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        foreach (var (original, token) in _people.Concat(_organizations))
        {
            text = text.Replace(token, original, StringComparison.Ordinal);
        }

        return text;
    }

    public override string ToString() => $"PseudonymMap {{ People = {_people.Count}, Organizations = {_organizations.Count} }}";

    private static string Token(Dictionary<string, string> map, string text, string prefix)
    {
        var key = text.Trim();
        if (!map.TryGetValue(key, out var token))
        {
            token = "[" + prefix + (map.Count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + "]";
            map.Add(key, token);
        }

        return token;
    }
}

/// <summary>
/// Purpose-specific views of a message; the raw message is never changed (master prompt §5).
/// <list type="table">
/// <item><term>Display</term><description>original; high-risk identifiers masked; excluded messages still shown.</description></item>
/// <item><term>LocalSearch</term><description>original names (needed for memory search); high-risk identifiers masked; excluded messages omitted.</description></item>
/// <item><term>Analysis / LlmPrompt</term><description>high-risk identifiers always masked; other spans masked when decided "Mask"; excluded messages omitted.</description></item>
/// <item><term>CloudTransfer</term><description>refused (DD-014).</description></item>
/// </list>
/// A message with any "Delete" decision has no view at all.
/// </summary>
public static class PurposeRedactor
{
    /// <summary>Returns the view of <paramref name="message"/> for <paramref name="purpose"/>, or null if the message must be omitted.</summary>
    public static string? Render(
        ChatMessage message,
        IReadOnlyList<PiiFinding> findings,
        Func<FindingId, PrivacyAction> actionOf,
        RedactionPurpose purpose,
        PseudonymMap pseudonyms)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(findings);
        ArgumentNullException.ThrowIfNull(actionOf);
        ArgumentNullException.ThrowIfNull(pseudonyms);

        if (purpose == RedactionPurpose.CloudTransfer)
        {
            throw new InvalidOperationException("Cloud transfer of conversation content is not available (DD-014).");
        }

        var own = findings.Where(f => f.MessageId == message.Id).ToList();
        if (own.Any(f => actionOf(f.Id) == PrivacyAction.Delete))
        {
            return null;
        }

        if (purpose != RedactionPurpose.Display && own.Any(f => actionOf(f.Id) == PrivacyAction.Exclude))
        {
            return null;
        }

        var masked = own
            .Where(f => f.Category != PiiCategory.Sensitive && ShouldMask(f, actionOf(f.Id), purpose))
            .OrderByDescending(f => f.Start)
            .ToList();
        if (masked.Count == 0)
        {
            return message.Text;
        }

        var text = new StringBuilder(message.Text);
        foreach (var f in masked)
        {
            if (f.Start < 0 || f.End > message.Text.Length)
            {
                throw new InvalidOperationException("Finding is outside the message text.");
            }

            text.Remove(f.Start, f.Length).Insert(f.Start, TokenFor(f, message.Text.Substring(f.Start, f.Length), pseudonyms));
        }

        return text.ToString();
    }

    /// <summary>
    /// Renders a stored dataset for <paramref name="purpose"/>: messages flagged excluded and messages the
    /// purpose omits are dropped; the rest carry the purpose view as their text. Order is kept.
    /// </summary>
    public static IReadOnlyList<ChatMessage> RenderAll(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<(PiiFinding Finding, PrivacyAction Action)> findings,
        IReadOnlySet<MessageId> excluded,
        RedactionPurpose purpose,
        PseudonymMap? pseudonyms = null)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(findings);
        ArgumentNullException.ThrowIfNull(excluded);
        if (purpose == RedactionPurpose.CloudTransfer)
        {
            throw new InvalidOperationException("Cloud transfer of conversation content is not available (DD-014).");
        }

        var actions = findings.ToDictionary(f => f.Finding.Id, f => f.Action);
        var byMessage = findings.Select(f => f.Finding).ToLookup(f => f.MessageId);
        pseudonyms ??= new PseudonymMap();
        var result = new List<ChatMessage>(messages.Count);
        foreach (var message in messages)
        {
            if (purpose != RedactionPurpose.Display && excluded.Contains(message.Id))
            {
                continue;
            }

            if (Render(message, [.. byMessage[message.Id]], id => actions[id], purpose, pseudonyms) is { } text)
            {
                result.Add(message with { Text = text });
            }
        }

        return result;
    }

    /// <summary>
    /// The LlmPrompt view of a stored dataset as <see cref="RedactedText"/> — the only way conversation data
    /// can be put into an <see cref="LlmRequest"/>. Same omission rules as <see cref="RenderAll"/>.
    /// Pass one <paramref name="pseudonyms"/> map for everything in a request so tokens stay consistent.
    /// </summary>
    public static IReadOnlyList<(ChatMessage Source, RedactedText Text)> RenderForLlm(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<(PiiFinding Finding, PrivacyAction Action)> findings,
        IReadOnlySet<MessageId> excluded,
        PseudonymMap? pseudonyms = null) =>
        [.. RenderAll(messages, findings, excluded, RedactionPurpose.LlmPrompt, pseudonyms).Select(m => (m, new RedactedText(m.Text)))];

    private static bool ShouldMask(PiiFinding finding, PrivacyAction action, RedactionPurpose purpose) =>
        finding.Category == PiiCategory.HighRiskIdentifier
        || (purpose is RedactionPurpose.Analysis or RedactionPurpose.LlmPrompt && action == PrivacyAction.Mask);

    private static string TokenFor(PiiFinding finding, string original, PseudonymMap pseudonyms) => finding.Type switch
    {
        PiiType.Phone => "[전화번호]",
        PiiType.Email => "[이메일]",
        PiiType.ResidentId => "[주민등록번호]",
        PiiType.Card => "[카드번호]",
        PiiType.Account => "[계좌번호]",
        PiiType.IpAddress => "[IP]",
        PiiType.Url => "[URL]",
        PiiType.Address => "[주소]",
        PiiType.Person or PiiType.Title => pseudonyms.Person(original),
        PiiType.Organization => pseudonyms.Organization(original),
        _ => "[가림]",
    };
}

/// <summary>Turns a speaker-reviewed chat plus the privacy review into what the vault stores.</summary>
public static class PrivacyImportBuilder
{
    public static PrivacyReviewedImport Build(ReviewedChat chat, PrivacyReview review)
    {
        ArgumentNullException.ThrowIfNull(chat);
        ArgumentNullException.ThrowIfNull(review);

        var deleted = review.DeletedMessages;
        var excluded = review.ExcludedMessages;
        var kept = chat.Messages.Where(m => !deleted.Contains(m.Id)).ToList();
        var keptIds = kept.Select(m => m.Id).ToHashSet();
        var findings = review.Findings.Where(f => keptIds.Contains(f.MessageId)).ToList();
        var byMessage = findings.ToLookup(f => f.MessageId);
        var pseudonyms = new PseudonymMap();

        var searchText = new Dictionary<MessageId, string>();
        foreach (var message in kept.Where(m => !excluded.Contains(m.Id)))
        {
            searchText[message.Id] = PurposeRedactor.Render(message, [.. byMessage[message.Id]], review.ActionFor, RedactionPurpose.LocalSearch, pseudonyms)
                ?? throw new InvalidOperationException("Included message produced no search view.");
        }

        return new PrivacyReviewedImport(
            chat with { Messages = kept },
            findings,
            findings.ToDictionary(f => f.Id, f => review.ActionFor(f.Id)),
            excluded,
            searchText,
            chat.Messages.Count - kept.Count);
    }
}
