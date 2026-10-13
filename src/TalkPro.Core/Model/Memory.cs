using System.Globalization;
using TalkPro.Core.Diagnostics;

namespace TalkPro.Core.Model;

public readonly record struct MemoryItemId(int Value) : ILogSafe
{
    public override string ToString() => "mi" + Value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// A retrieval unit (Phase 7): consecutive messages of one session that include at least one owner message.
/// Holds ids and times only; text is rendered from the vault for the purpose at hand, so evidence is always the
/// stored record and never generated text.
/// </summary>
public sealed record MemoryItem(MemoryItemId Id, SessionId Session, IReadOnlyList<MessageId> SourceMessageIds, DateTimeOffset Start, DateTimeOffset End) : ILogSafe
{
    public override string ToString() => $"MemoryItem {{ Id = {Id}, Session = {Session}, Messages = {SourceMessageIds.Count} }}";
}

/// <summary>
/// Grounding labels (report §7.5), assigned by a verifier separate from the generator, never by the model.
/// Phase 7 uses <see cref="Quote"/> for evidence cards; the others are prepared for Phase 6.5.
/// </summary>
public enum GroundingLabel
{
    /// <summary>A stored message rendered verbatim from the vault ("실제 기록").</summary>
    Quote,

    /// <summary>A past fact restated and supported by evidence messages.</summary>
    Fact,

    /// <summary>A tendency inferred from many messages ("기록에서 추정").</summary>
    Inferred,

    /// <summary>New text with no claim about the past (greeting, empathy, question).</summary>
    Generated,

    /// <summary>A past claim or quote without evidence: blocked, never shown.</summary>
    Unsupported,
}
