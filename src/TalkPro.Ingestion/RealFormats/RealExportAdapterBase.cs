using System.Globalization;
using System.Text.RegularExpressions;
using TalkPro.Core.Model;
using TalkPro.Core.Parsing;

namespace TalkPro.Ingestion.RealFormats;

/// <summary>
/// Rules shared by the Korean-UI messenger export layouts (<c>docs/REAL_FORMATS.md</c>):
/// a two-line header (title ending in <c> 카카오톡 대화</c>, <c>저장한 날짜 : …</c>), record lines
/// recognised only by their complete shape, and every other line treated as message text.
/// <para>
/// Unlike the synthetic formats there are no reserved prefixes: real message text may start with
/// <c>[</c>, digits or a date, so a line becomes a record only if it matches a full record pattern.
/// A full-shape record with impossible values is <see cref="LineOutcome.Malformed"/>.
/// </para>
/// Adapters are pure: no I/O, no logging, no shared mutable state.
/// </summary>
public abstract class RealExportAdapterBase : IChatFormatAdapter
{
    public const string TitleSuffix = " 카카오톡 대화";

    public const string SavedDatePrefix = "저장한 날짜 : ";

    /// <summary>Score for a recognised header without any record of this layout (below the detection threshold).</summary>
    public const double HeaderOnlyConfidence = 0.3;

    protected const int RegexTimeoutMilliseconds = 250;

    protected RealExportAdapterBase(ChatFormatId formatId) => FormatId = formatId;

    public ChatFormatId FormatId { get; }

    /// <summary>
    /// Requires both header lines and at least one complete record of this layout. Score is
    /// 0.6 + 0.4 × (records / non-blank body lines); header without records → <see cref="HeaderOnlyConfidence"/>; no header → 0.
    /// </summary>
    public double Detect(IReadOnlyList<string> sampleLines)
    {
        ArgumentNullException.ThrowIfNull(sampleLines);
        if (sampleLines.Count < 2 || !IsTitle(sampleLines[0]) || !sampleLines[1].StartsWith(SavedDatePrefix, StringComparison.Ordinal))
        {
            return 0;
        }

        int nonBlank = 0, records = 0;
        for (var i = 2; i < sampleLines.Count; i++)
        {
            var line = sampleLines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            nonBlank++;
            try
            {
                if (IsRecordShape(line))
                {
                    records++;
                }
            }
            catch (RegexMatchTimeoutException)
            {
                // A line that cannot be matched in time is simply not evidence for this layout.
            }
        }

        return records == 0 ? HeaderOnlyConfidence : 0.6 + (0.4 * records / nonBlank);
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

    /// <summary>Whether the line has the complete shape of a record of this layout (used by detection).</summary>
    protected abstract bool IsRecordShape(string line);

    /// <summary>
    /// Parses a record line, or returns <see langword="null"/> if the line is not a record of this
    /// layout (then it is message text). Every returned step must complete the open message.
    /// </summary>
    protected abstract LineStep? TryParseRecord(ParserState state, string line);

    protected static LineStep Malformed(ParserState state, ParseIssue issue)
    {
        var (flushed, completed) = state.FlushPending();
        return new LineStep(flushed, LineParseResult.Malformed(state.LineNumber, issue, completed));
    }

    /// <summary>A date marker line: completes the open message and sets (or, if invalid, clears) the date context.</summary>
    protected static LineStep DateMarker(ParserState state, ParseIssue issue, DateOnly date)
    {
        if (issue != ParseIssue.None)
        {
            return Malformed(state.ClearDate(), issue);
        }

        var (flushed, completed) = state.FlushPending();
        return new LineStep(flushed.WithDate(date), LineParseResult.Metadata(state.LineNumber, completed));
    }

    /// <summary>A user message. Whitespace-only or too long sender labels are malformed; labels are kept verbatim.</summary>
    protected static LineStep Message(ParserState state, DateTime localTime, string speaker, string text)
    {
        if (string.IsNullOrWhiteSpace(speaker))
        {
            return Malformed(state, ParseIssue.InvalidSpeakerName);
        }

        if (speaker.Length > state.Context.MaxSpeakerNameLength)
        {
            return Malformed(state, ParseIssue.SpeakerNameTooLong);
        }

        var (next, completed) = state.StartMessage(state.Context.ToTimestamp(localTime), speaker, text);
        return new LineStep(next, LineParseResult.MessageStarted(state.LineNumber, completed));
    }

    protected static LineStep SystemEntry(ParserState state, DateTime localTime, string text)
    {
        var (next, completed) = state.CompleteSystemEntry(state.Context.ToTimestamp(localTime), text, ClassifySystemText(text));
        return new LineStep(next, LineParseResult.SystemMessage(state.LineNumber, completed));
    }

    /// <summary>
    /// Mobile layouts: <c>sender : text</c> after the timestamp. The sender ends at the first <c> : </c>
    /// (a sender label containing <c> : </c> is split wrongly; Phase 1.6 re-splits with known names).
    /// No separator → system line. Empty sender → system line.
    /// </summary>
    protected static LineStep SenderRecord(ParserState state, DateTime localTime, string rest)
    {
        string speaker, text;
        var separator = rest.IndexOf(" : ", StringComparison.Ordinal);
        if (separator >= 0)
        {
            (speaker, text) = (rest[..separator], rest[(separator + 3)..]);
        }
        else if (rest.EndsWith(" :", StringComparison.Ordinal))
        {
            (speaker, text) = (rest[..^2], string.Empty);
        }
        else
        {
            return SystemEntry(state, localTime, rest);
        }

        return speaker.Length == 0 ? SystemEntry(state, localTime, text) : Message(state, localTime, speaker, text);
    }

    /// <summary>Korean 12-hour clock: 오전 12 = 0 h, 오후 12 = 12 h, 오후 h = h + 12. Returns -1 for hours outside 1–12.</summary>
    protected static int To24Hour(ReadOnlySpan<char> period, int hour12)
    {
        if (hour12 is < 1 or > 12)
        {
            return -1;
        }

        return (hour12 % 12) + (period is "오후" ? 12 : 0);
    }

    /// <summary>Validates numeric date/time parts without throwing.</summary>
    protected static ParseIssue TryCreateLocalTime(ParseContext context, int year, int month, int day, int hour, int minute, out DateTime localTime)
    {
        localTime = default;
        if (year < 1 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month)
            || hour is < 0 or > 23 || minute is < 0 or > 59)
        {
            return ParseIssue.InvalidTimestamp;
        }

        if (!context.IsInRange(new DateOnly(year, month, day)))
        {
            return ParseIssue.TimestampOutOfRange;
        }

        localTime = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
        return ParseIssue.None;
    }

    /// <summary>Parses a group of at most four ASCII digits (guaranteed by the patterns).</summary>
    protected static int Number(Group group) => int.Parse(group.ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture);

    private static bool IsTitle(string line) => line.Length > TitleSuffix.Length && line.EndsWith(TitleSuffix, StringComparison.Ordinal);

    /// <summary>
    /// Only exact messenger-generated endings observed in public fixtures are classified;
    /// everything else stays <see cref="SystemEventType.Other"/> (rules as data are Phase 1.6).
    /// </summary>
    private static SystemEventType ClassifySystemText(string text)
    {
        if (text.EndsWith("님이 나갔습니다.", StringComparison.Ordinal))
        {
            return SystemEventType.Leave;
        }

        return text.EndsWith("님을 초대하였습니다.", StringComparison.Ordinal) ? SystemEventType.Invite : SystemEventType.Other;
    }

    /// <summary>Line 1 must be the title; the header then runs until the first blank line or the first record.</summary>
    private LineStep ParseHeader(ParserState state, string line)
    {
        if (state.LineNumber == 1)
        {
            return IsTitle(line)
                ? new LineStep(state, LineParseResult.Metadata(1, []))
                : ParseBody(state.EnterBody(), line);
        }

        if (string.IsNullOrWhiteSpace(line))
        {
            return new LineStep(state.EnterBody(), LineParseResult.Ignored(state.LineNumber));
        }

        return TryParseRecord(state.EnterBody(), line) ?? new LineStep(state, LineParseResult.Metadata(state.LineNumber, []));
    }

    private LineStep ParseBody(ParserState state, string line)
    {
        if (TryParseRecord(state, line) is { } record)
        {
            return record;
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
