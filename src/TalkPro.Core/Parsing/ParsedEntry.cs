using TalkPro.Core.Model;

namespace TalkPro.Core.Parsing;

/// <summary>
/// A completed record as read from the export, before speaker resolution.
/// <see cref="SpeakerName"/> and <see cref="Text"/> are personal data.
/// </summary>
public sealed record ParsedEntry(
    int StartLine,
    int EndLine,
    DateTimeOffset Timestamp,
    string? SpeakerName,
    MessageKind Kind,
    string Text)
{
    public override string ToString() => $"ParsedEntry {{ Lines = {StartLine}-{EndLine}, Kind = {Kind} }}";
}
