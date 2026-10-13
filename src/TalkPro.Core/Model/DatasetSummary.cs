using TalkPro.Core.Parsing;

namespace TalkPro.Core.Model;

public enum DatasetState
{
    /// <summary>Key and metadata are readable.</summary>
    Ready,

    /// <summary>Key missing or metadata unreadable; the dataset can only be deleted.</summary>
    Damaged,
}

/// <summary>
/// One entry of the dataset list. <see cref="Label"/> is a user-chosen, possibly personal name; it is
/// decrypted only in memory and never printed by <see cref="ToString"/>.
/// </summary>
public sealed record DatasetSummary(
    DatasetId Id,
    DatasetState State,
    string? Label,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt,
    int ParticipantCount,
    int MessageCount,
    ChatFormatId? FormatId)
{
    public override string ToString() =>
        $"DatasetSummary {{ Id = {Id}, State = {State}, Participants = {ParticipantCount}, Messages = {MessageCount} }}";
}
