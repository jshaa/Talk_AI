using TalkPro.Core.Diagnostics;

namespace TalkPro.Core.Model;

public readonly record struct SessionId(int Value) : ILogSafe
{
    public override string ToString() => "s" + Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>A conversation episode: consecutive messages that belong together. Always linked to source message ids.</summary>
public sealed record Session(SessionId Id, IReadOnlyList<MessageId> MessageIds, DateTimeOffset Start, DateTimeOffset End) : ILogSafe
{
    public TimeSpan Duration => End - Start;

    public override string ToString() => $"Session {{ Id = {Id}, Messages = {MessageIds.Count} }}";
}

/// <summary>Splits a chat into sessions (master prompt §7). Implementations must keep message order and cover every input message exactly once.</summary>
public interface ISessionizer
{
    IReadOnlyList<Session> Split(IReadOnlyList<ChatMessage> messages);
}

/// <summary>
/// Optional semantic signal for boundary candidates (0 = unrelated, 1 = same topic). Supplied once local
/// embeddings exist (Phase 5/7); time gaps stay the primary signal.
/// </summary>
public interface ISemanticSimilarity
{
    double Similarity(IReadOnlyList<ChatMessage> before, IReadOnlyList<ChatMessage> after);
}
