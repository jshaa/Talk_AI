namespace TalkPro.Tools.SampleGenerator.Formats;

/// <summary>Expected line outcome. Names mirror <c>TalkPro.Core.Parsing.LineOutcome</c> (checked by tests, not by reference).</summary>
public enum ExpectedOutcome
{
    MessageStarted,
    Continuation,
    SystemMessage,
    Metadata,
    Ignored,
    Malformed,
    Unsupported,
}

/// <summary>Expected issue. Names mirror <c>TalkPro.Core.Parsing.ParseIssue</c> (checked by tests, not by reference).</summary>
public enum ExpectedIssue
{
    None,
    IncompleteRecord,
    InvalidTimestamp,
    TimestampOutOfRange,
    MissingDateContext,
    InvalidSpeakerName,
    SpeakerNameTooLong,
    OrphanLine,
    LineTooLong,
    NullCharacter,
    UnsupportedVersion,
}

public sealed record ExpectedLine(ExpectedOutcome Outcome, ExpectedIssue Issue = ExpectedIssue.None);

/// <summary>A completed entry as the parser must report it. <see cref="Speaker"/> is <see langword="null"/> for system entries.</summary>
public sealed record ExpectedEntry(int StartLine, int EndLine, DateTime LocalTime, string? Speaker, string SystemEvent, string Text)
{
    public bool IsSystem => Speaker is null;
}

/// <summary>One case rendered in one format: the lines (without terminators) and what a parser must make of them.</summary>
public sealed record RenderedSample(
    IReadOnlyList<string> Lines,
    IReadOnlyList<ExpectedLine> Outcomes,
    IReadOnlyList<ExpectedEntry> Entries);
