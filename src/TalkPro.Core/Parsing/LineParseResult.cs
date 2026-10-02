using System.Collections.Immutable;

namespace TalkPro.Core.Parsing;

/// <summary>How a single input line was interpreted.</summary>
public enum LineOutcome
{
    /// <summary>The line opened a new message (any previously open message completed).</summary>
    MessageStarted,

    /// <summary>The line was appended to the open message.</summary>
    Continuation,

    /// <summary>The line was a complete system record (join/leave/…).</summary>
    SystemMessage,

    /// <summary>Header or context line (signature, room, date marker); may change parser state.</summary>
    Metadata,

    /// <summary>Line without meaning, e.g. a blank line while no message is open.</summary>
    Ignored,

    /// <summary>Line that violates the format; see <see cref="ParseIssue"/>.</summary>
    Malformed,

    /// <summary>Recognised construct this parser does not support (e.g. unknown format version).</summary>
    Unsupported,
}

public enum ParseIssue
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
    PatternTimeout,
}

/// <summary>
/// Immutable result of processing one line. <see cref="Completed"/> holds the entries finalised by
/// this line: the previously open message and/or a system entry ("message completed" events).
/// </summary>
public sealed record LineParseResult
{
    private LineParseResult(int lineNumber, LineOutcome outcome, ParseIssue issue, ImmutableArray<ParsedEntry> completed)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(lineNumber, 1);
        var needsIssue = outcome is LineOutcome.Malformed or LineOutcome.Unsupported;
        if (needsIssue == (issue == ParseIssue.None))
        {
            throw new ArgumentException("Malformed/Unsupported results require an issue; other outcomes must not have one.", nameof(issue));
        }

        LineNumber = lineNumber;
        Outcome = outcome;
        Issue = issue;
        Completed = completed.IsDefault ? [] : completed;
    }

    public int LineNumber { get; }

    public LineOutcome Outcome { get; }

    public ParseIssue Issue { get; }

    public ImmutableArray<ParsedEntry> Completed { get; }

    public bool CompletesEntries => !Completed.IsEmpty;

    public static LineParseResult MessageStarted(int lineNumber, ImmutableArray<ParsedEntry> completed) =>
        new(lineNumber, LineOutcome.MessageStarted, ParseIssue.None, completed);

    public static LineParseResult Continuation(int lineNumber) =>
        new(lineNumber, LineOutcome.Continuation, ParseIssue.None, []);

    public static LineParseResult SystemMessage(int lineNumber, ImmutableArray<ParsedEntry> completed) =>
        new(lineNumber, LineOutcome.SystemMessage, ParseIssue.None, completed);

    public static LineParseResult Metadata(int lineNumber, ImmutableArray<ParsedEntry> completed) =>
        new(lineNumber, LineOutcome.Metadata, ParseIssue.None, completed);

    public static LineParseResult Ignored(int lineNumber) =>
        new(lineNumber, LineOutcome.Ignored, ParseIssue.None, []);

    public static LineParseResult Malformed(int lineNumber, ParseIssue issue, ImmutableArray<ParsedEntry> completed) =>
        new(lineNumber, LineOutcome.Malformed, issue, completed);

    public static LineParseResult Unsupported(int lineNumber, ParseIssue issue, ImmutableArray<ParsedEntry> completed) =>
        new(lineNumber, LineOutcome.Unsupported, issue, completed);

    public override string ToString() =>
        $"LineParseResult {{ Line = {LineNumber}, Outcome = {Outcome}, Issue = {Issue}, Completed = {Completed.Length} }}";
}
