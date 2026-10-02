using TalkPro.Core.Diagnostics;
using TalkPro.Core.Parsing;

namespace TalkPro.Core.Model;

public enum SourceEncoding
{
    Utf8,
    Utf8WithBom,
    Cp949,
}

/// <summary>Location and kind of a rejected line. Contains no line content.</summary>
public sealed record LineIssue(int LineNumber, LineOutcome Outcome, ParseIssue Issue) : ILogSafe
{
    public override string ToString() => $"LineIssue {{ Line = {LineNumber}, Outcome = {Outcome}, Issue = {Issue} }}";
}

/// <summary>Per-outcome line counts. Log-safe.</summary>
public sealed record ParseStatistics(
    int TotalLines,
    int MessagesStarted,
    int SystemMessages,
    int Continuations,
    int Metadata,
    int Ignored,
    int Malformed,
    int Unsupported) : ILogSafe
{
    public override string ToString() =>
        $"ParseStatistics {{ Lines = {TotalLines}, Messages = {MessagesStarted}, System = {SystemMessages}, Malformed = {Malformed}, Unsupported = {Unsupported} }}";
}

/// <summary>Result of importing one export file.</summary>
public sealed record ParsedChat(
    ChatFormatId FormatId,
    SourceEncoding Encoding,
    IReadOnlyList<Participant> Participants,
    IReadOnlyList<ChatMessage> Messages,
    IReadOnlyList<LineIssue> Issues,
    ParseStatistics Statistics)
{
    public override string ToString() =>
        $"ParsedChat {{ Format = {FormatId}, Encoding = {Encoding}, Participants = {Participants.Count}, Messages = {Messages.Count}, Issues = {Issues.Count} }}";
}
