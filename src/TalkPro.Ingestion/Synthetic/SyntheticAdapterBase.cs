using System.Globalization;
using System.Text.RegularExpressions;
using TalkPro.Core.Model;
using TalkPro.Core.Parsing;

namespace TalkPro.Ingestion.Synthetic;

/// <summary>
/// Common rules of the synthetic test formats (<c>docs/SYNTHETIC_FORMATS.md</c>): header section,
/// reserved record prefixes, continuation/orphan/blank lines, speaker and timestamp validation.
/// These adapters are test fixtures for the parser contract; they are not registered in DI (DD-012).
/// </summary>
public abstract class SyntheticAdapterBase : IChatFormatAdapter
{
    public const string SignaturePrefix = "TalkPro Synthetic Export (";

    protected const string SystemSpeaker = "*";

    protected const int RegexTimeoutMilliseconds = 250;

    private readonly string _signature;

    protected SyntheticAdapterBase(ChatFormatId formatId, char family)
    {
        FormatId = formatId;
        _signature = SignaturePrefix + family + "1)";
    }

    public ChatFormatId FormatId { get; }

    /// <summary>1.0 for this format's signature; otherwise 0.5–0.9 by the share of body lines that are full records.</summary>
    public double Detect(IReadOnlyList<string> sampleLines)
    {
        ArgumentNullException.ThrowIfNull(sampleLines);
        if (sampleLines.Count == 0)
        {
            return 0;
        }

        if (string.Equals(sampleLines[0], _signature, StringComparison.Ordinal))
        {
            return 1;
        }

        var start = 0;
        if (sampleLines[0] is { } first && first.StartsWith(SignaturePrefix, StringComparison.Ordinal))
        {
            while (start < sampleLines.Count && !string.IsNullOrWhiteSpace(sampleLines[start]))
            {
                start++;
            }
        }

        int nonBlank = 0, records = 0;
        for (var i = start; i < sampleLines.Count; i++)
        {
            var line = sampleLines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            nonBlank++;
            if (IsReserved(line) && IsCompleteRecord(line))
            {
                records++;
            }
        }

        return records == 0 ? 0 : 0.5 + (0.4 * records / nonBlank);
    }

    public ParserState CreateInitialState(ParseContext context) => ParserState.Initial(context);

    public LineStep ParseLine(ParserState state, string line)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(line);
        if (state.LineNumber < 1)
        {
            throw new ArgumentException("The driver must advance the line before parsing it.", nameof(state));
        }

        try
        {
            return state.Section == ParserSection.Header ? ParseHeader(state, line) : ParseBody(state, line);
        }
        catch (RegexMatchTimeoutException)
        {
            return Malformed(state.EnterBody(), ParseIssue.PatternTimeout);
        }
    }

    /// <summary>Whether the line starts with a reserved record prefix (records are never continuation lines).</summary>
    protected abstract bool IsReserved(string line);

    /// <summary>Whether a reserved line is syntactically a complete record (used by detection).</summary>
    protected abstract bool IsCompleteRecord(string line);

    /// <summary>Parses a reserved line. Must complete the open message in every outcome.</summary>
    protected abstract LineStep ParseRecord(ParserState state, string line);

    protected static LineStep Malformed(ParserState state, ParseIssue issue)
    {
        var (flushed, completed) = state.FlushPending();
        return new LineStep(flushed, LineParseResult.Malformed(state.LineNumber, issue, completed));
    }

    protected static LineStep StartMessage(ParserState state, DateTime localTime, string speaker, string text)
    {
        var (next, completed) = state.StartMessage(state.Context.ToTimestamp(localTime), speaker, text);
        return new LineStep(next, LineParseResult.MessageStarted(state.LineNumber, completed));
    }

    protected static LineStep SystemEntry(ParserState state, DateTime localTime, string text)
    {
        var (next, completed) = state.CompleteSystemEntry(state.Context.ToTimestamp(localTime), text, ClassifySystemText(text));
        return new LineStep(next, LineParseResult.SystemMessage(state.LineNumber, completed));
    }

    /// <summary>Speakers are 1–<see cref="ParseContext.MaxSpeakerNameLength"/> UTF-16 units, not blank, not <c>*</c>.</summary>
    protected static ParseIssue ValidateSpeaker(ParseContext context, string speaker)
    {
        if (string.IsNullOrWhiteSpace(speaker) || speaker == SystemSpeaker)
        {
            return ParseIssue.InvalidSpeakerName;
        }

        return speaker.Length > context.MaxSpeakerNameLength ? ParseIssue.SpeakerNameTooLong : ParseIssue.None;
    }

    /// <summary>Builds a local time from ASCII digit groups; reports invalid or out-of-range values without throwing.</summary>
    protected static ParseIssue TryCreateLocalTime(
        ParseContext context,
        ReadOnlySpan<char> year,
        ReadOnlySpan<char> month,
        ReadOnlySpan<char> day,
        int hour,
        int minute,
        int second,
        out DateTime localTime)
    {
        localTime = default;
        int y = Number(year), mo = Number(month), d = Number(day);
        if (y < 1 || mo is < 1 or > 12 || d < 1 || d > DateTime.DaysInMonth(y, mo)
            || hour is < 0 or > 23 || minute is < 0 or > 59 || second is < 0 or > 59)
        {
            return ParseIssue.InvalidTimestamp;
        }

        if (!context.IsInRange(new DateOnly(y, mo, d)))
        {
            return ParseIssue.TimestampOutOfRange;
        }

        localTime = new DateTime(y, mo, d, hour, minute, second, DateTimeKind.Unspecified);
        return ParseIssue.None;
    }

    /// <summary>Parses a group of at most four ASCII digits (guaranteed by the patterns).</summary>
    protected static int Number(ReadOnlySpan<char> digits) => int.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);

    protected static bool StartsWithDigits(string line, int count)
    {
        if (line.Length < count)
        {
            return false;
        }

        for (var i = 0; i < count; i++)
        {
            if (!char.IsAsciiDigit(line[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static SystemEventType ClassifySystemText(string text)
    {
        if (text.EndsWith(" 님이 들어왔습니다.", StringComparison.Ordinal))
        {
            return SystemEventType.Join;
        }

        return text.EndsWith(" 님이 나갔습니다.", StringComparison.Ordinal) ? SystemEventType.Leave : SystemEventType.Other;
    }

    /// <summary>
    /// The header exists only if line 1 is a signature and ends at the first blank line or the first
    /// record. Another family or version on line 1 is <see cref="LineOutcome.Unsupported"/>.
    /// </summary>
    private LineStep ParseHeader(ParserState state, string line)
    {
        var lineNumber = state.LineNumber;
        if (lineNumber == 1)
        {
            if (string.Equals(line, _signature, StringComparison.Ordinal))
            {
                return new LineStep(state, LineParseResult.Metadata(lineNumber, []));
            }

            if (line.StartsWith(SignaturePrefix, StringComparison.Ordinal))
            {
                return new LineStep(state, LineParseResult.Unsupported(lineNumber, ParseIssue.UnsupportedVersion, []));
            }

            return ParseBody(state.EnterBody(), line);
        }

        if (string.IsNullOrWhiteSpace(line))
        {
            return new LineStep(state.EnterBody(), LineParseResult.Ignored(lineNumber));
        }

        return IsReserved(line)
            ? ParseBody(state.EnterBody(), line)
            : new LineStep(state, LineParseResult.Metadata(lineNumber, []));
    }

    private LineStep ParseBody(ParserState state, string line)
    {
        if (IsReserved(line))
        {
            return ParseRecord(state, line);
        }

        if (state.IsEntryOpen)
        {
            return new LineStep(state.AppendContinuation(line), LineParseResult.Continuation(state.LineNumber));
        }

        return string.IsNullOrWhiteSpace(line)
            ? new LineStep(state, LineParseResult.Ignored(state.LineNumber))
            : new LineStep(state, LineParseResult.Malformed(state.LineNumber, ParseIssue.OrphanLine, []));
    }
}
