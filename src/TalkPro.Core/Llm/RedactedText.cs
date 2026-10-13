using TalkPro.Core.Diagnostics;

namespace TalkPro.Core.Llm;

/// <summary>
/// Conversation-derived text that has passed the <c>LlmPrompt</c> redaction view (DD-020): excluded and
/// deleted messages are omitted, high-risk identifiers and "Mask" decisions are replaced by tokens.
/// Only TalkPro.Privacy can create it (internal constructor), so conversation data cannot reach a model
/// without redaction. App-authored instructions are plain strings in <see cref="LlmRequest"/>.
/// </summary>
public sealed class RedactedText : ILogSafe
{
    internal RedactedText(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => $"RedactedText {{ Length = {Value.Length} }}";
}
