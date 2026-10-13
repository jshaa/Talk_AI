using TalkPro.Core.Diagnostics;
using TalkPro.Core.Parsing;

namespace TalkPro.Core.Model;

/// <summary>Counts of user decisions. Log-safe.</summary>
public sealed record ReviewSummary(int Participants, int Merges, int Splits, int Excluded, int AcknowledgedSuspicions, int DroppedMessages) : ILogSafe
{
    public override string ToString() =>
        $"ReviewSummary {{ Participants = {Participants}, Merges = {Merges}, Splits = {Splits}, Excluded = {Excluded}, Acknowledged = {AcknowledgedSuspicions}, Dropped = {DroppedMessages} }}";
}

/// <summary>
/// The import after the user's speaker review: exactly one owner and the final speaker mapping.
/// Produced by Ingestion (speaker review), stored by Infrastructure (vault), read by later phases.
/// </summary>
public sealed record ReviewedChat(
    ChatFormatId FormatId,
    SourceEncoding Encoding,
    ParticipantId OwnerId,
    IReadOnlyList<Participant> Participants,
    IReadOnlyList<ChatMessage> Messages,
    ReviewSummary Summary)
{
    public override string ToString() =>
        $"ReviewedChat {{ Format = {FormatId}, Owner = {OwnerId}, Participants = {Participants.Count}, Messages = {Messages.Count} }}";
}
