namespace TalkPro.Core.Model;

public enum MessageKind
{
    Text,
    Emoticon,
    Photo,
    Video,
    File,
    Voice,
    Call,
    Transfer,
    Deleted,
    System,
}

public enum SystemEventType
{
    None,
    Join,
    Leave,
    Invite,
    Kick,
    AdminChange,
    Hidden,
    Other,
}

/// <summary>
/// One parsed message. <see cref="Text"/> is raw conversation content and must never be logged,
/// put into exception messages or sent off the device.
/// </summary>
public sealed record ChatMessage(
    MessageId Id,
    DateTimeOffset Timestamp,
    ParticipantId? SpeakerId,
    MessageKind Kind,
    string Text,
    int SourceLine)
{
    public SystemEventType SystemEvent { get; init; } = SystemEventType.None;

    /// <summary>Content-free representation; the compiler-generated one would print <see cref="Text"/>.</summary>
    public override string ToString() => $"ChatMessage {{ Id = {Id}, Kind = {Kind} }}";
}
