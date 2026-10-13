using System.Globalization;
using System.Text.RegularExpressions;
using TalkPro.Core.Parsing;

namespace TalkPro.Ingestion.Formats.Synthetic;

/// <summary>
/// Shared behaviour of the test-only synthetic formats (docs/SYNTHETIC_FORMATS.md): header handling,
/// continuation/orphan rules and content-based detection. Subclasses parse their own record lines.
/// </summary>
public abstract class SyntheticFormatAdapter : IChatFormatAdapter
{
    protected const int RegexTimeoutMilliseconds = 250;
    private const string SignaturePrefix = "TalkPro Synthetic Export (";

    public abstract ChatFormatId FormatId { get; }

    /// <summary>Signature code on line 1, e.g. <c>W1</c>.</summary>
    protected abstract string SignatureCode { get; }

    private string Signature => SignaturePrefix + SignatureCode + ")";

    public double Detect(IReadOnlyList<string> sampleLines)
    {
        ArgumentNullException.ThrowIfNull(sampleLines);
        var content = sampleLines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        if (content.Count == 0)
        {
            return 0;
        }

        if (content[0].StartsWith(SignaturePrefix, StringComparison.Ordinal))
        {
            return string.Equals(content[0], Signature, StringComparison.Ordinal) ? 1.0 : 0.0;
        }

        var records = content.Count(IsWellFormedRecord);
        return (double)records / content.Count;
    }

    public ParserState CreateInitialState(ParseContext context) => ParserState.Initial(context);

    public LineStep ParseLine(ParserState state, string line)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(line);
        if (state.LineNumber < 1)
        {
            throw new InvalidOperationException("AdvanceLine must be called before ParseLine.");
        }

        var number = state.LineNumber;
        if (state.Section == ParserSection.Header)
        {
            if (number == 1 && line.StartsWith(SignaturePrefix, StringComparison.Ordinal))
            {
                return string.Equals(line, Signature, StringComparison.Ordinal)
                    ? new(state, LineParseResult.Metadata(number, []))
                    : new(state, LineParseResult.Unsupported(number, ParseIssue.UnsupportedVersion, []));
            }

            if (line.StartsWith("Room: ", StringComparison.Ordinal) || line.StartsWith("Saved: ", StringComparison.Ordinal))
            {
                return new(state, LineParseResult.Metadata(number, []));
            }

            state = state.EnterBody();
            if (string.IsNullOrWhiteSpace(line))
            {
                return new(state, LineParseResult.Ignored(number));
            }
        }

        if (HasReservedPrefix(line))
        {
            return ParseRecord(state, line);
        }

        if (state.IsEntryOpen)
        {
            return new(state.AppendContinuation(line), LineParseResult.Continuation(number));
        }

        return string.IsNullOrWhiteSpace(line)
            ? new(state, LineParseResult.Ignored(number))
            : Malformed(state, ParseIssue.OrphanLine);
    }

    /// <summary>True if the line starts like a record of this format (cannot be a continuation).</summary>
    protected abstract bool HasReservedPrefix(string line);

    /// <summary>True if the line is a complete, well-formed record (used for detection only).</summary>
    protected abstract bool IsWellFormedRecord(string line);

    /// <summary>Parses a line that has the reserved prefix.</summary>
    protected abstract LineStep ParseRecord(ParserState state, string line);

    protected static LineStep Malformed(ParserState state, ParseIssue issue)
    {
        var (flushed, completed) = state.FlushPending();
        return new(flushed, LineParseResult.Malformed(state.LineNumber, issue, completed));
    }

    protected static LineStep StartMessage(ParserState state, DateTimeOffset timestamp, string speaker, string text)
    {
        var nameIssue = ValidateSpeaker(speaker, state.Context);
        if (nameIssue != ParseIssue.None)
        {
            return Malformed(state, nameIssue);
        }

        var (next, completed) = state.StartMessage(timestamp, speaker, text);
        return new(next, LineParseResult.MessageStarted(state.LineNumber, completed));
    }

    protected static LineStep SystemLine(ParserState state, DateTimeOffset timestamp, string text)
    {
        var (next, completed) = state.CompleteSystemEntry(timestamp, text);
        return new(next, LineParseResult.SystemMessage(state.LineNumber, completed));
    }

    protected static ParseIssue ValidateSpeaker(string speaker, ParseContext context)
    {
        if (string.IsNullOrWhiteSpace(speaker) || speaker == "*")
        {
            return ParseIssue.InvalidSpeakerName;
        }

        return speaker.Length > context.MaxSpeakerNameLength ? ParseIssue.SpeakerNameTooLong : ParseIssue.None;
    }

    protected static ParseIssue TryDate(Match match, ParseContext context, out DateOnly date)
    {
        date = default;
        var year = Number(match, "y");
        var month = Number(match, "mo");
        var day = Number(match, "d");
        if (year < 1 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month))
        {
            return ParseIssue.InvalidTimestamp;
        }

        date = new DateOnly(year, month, day);
        return context.IsInRange(date) ? ParseIssue.None : ParseIssue.TimestampOutOfRange;
    }

    protected static ParseIssue TryTimestamp(DateOnly date, int hour, int minute, int second, ParseContext context, out DateTimeOffset timestamp)
    {
        timestamp = default;
        if (hour is < 0 or > 23 || minute is < 0 or > 59 || second is < 0 or > 59)
        {
            return ParseIssue.InvalidTimestamp;
        }

        timestamp = context.ToTimestamp(date.ToDateTime(new TimeOnly(hour, minute, second)));
        return ParseIssue.None;
    }

    protected static int Number(Match match, string group)
    {
        ArgumentNullException.ThrowIfNull(match);
        return int.Parse(match.Groups[group].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture);
    }
}
