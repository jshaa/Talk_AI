using TalkPro.Core.Llm;
using TalkPro.Core.Model;

namespace TalkPro.Privacy;

/// <summary>
/// Redacts text the owner types for a model request (e.g. a message they want to answer), with the same
/// detectors as imports: high-risk identifiers and other identifiers/names are masked with the request's
/// shared pseudonym map. Sensitive topics are kept: the owner deliberately asks about this text and the
/// model is local (DD-024). The typed text is never stored.
/// </summary>
public static class PromptInput
{
    private static readonly MessageId InputId = new("input");

    /// <summary>
    /// Joins already-redacted lines into one text ("나: …\n상대: …"), e.g. one memory item for embedding.
    /// Labels come from <see cref="ContextLine"/> (short app strings); line breaks inside data are flattened.
    /// </summary>
    public static RedactedText Join(IReadOnlyList<ContextLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        return new RedactedText(string.Join('\n', lines.Select(l => l.Label + ": " + l.Text.Value.ReplaceLineEndings(" "))));
    }

    public static RedactedText Redact(string text, IReadOnlyList<Participant> participants, IPrivacyScanner scanner, PseudonymMap pseudonyms)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentNullException.ThrowIfNull(participants);
        ArgumentNullException.ThrowIfNull(scanner);
        ArgumentNullException.ThrowIfNull(pseudonyms);

        var message = new ChatMessage(InputId, DateTimeOffset.UnixEpoch, null, MessageKind.Text, text, 0);
        var findings = scanner.Scan([message], participants);
        var types = findings.ToDictionary(f => f.Id, f => f.Type);
        var view = PurposeRedactor.Render(
            message,
            findings,
            id => PiiTypes.CategoryOf(types[id]) == PiiCategory.Sensitive ? PrivacyAction.Keep : PrivacyAction.Mask,
            RedactionPurpose.LlmPrompt,
            pseudonyms) ?? throw new InvalidOperationException("Typed input produced no prompt view.");
        return new RedactedText(view);
    }
}
