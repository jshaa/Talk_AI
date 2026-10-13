using System.Globalization;
using System.Text.RegularExpressions;
using TalkPro.Core.Parsing;

namespace TalkPro.Ingestion.Formats.Kakao;

/// <summary>
/// KakaoTalk PC (Windows/Mac) text export, Korean UI (Phase 1.8b, DD-027). Structure learned from one owner-supplied
/// 1:1 export; golden tests use synthetic files of the same shape only.
/// <code>
/// {상대} 님과 카카오톡 대화
/// 저장한 날짜 : 2026-10-13 11:32:06
///
/// --------------- 2025년 7월 1일 화요일 ---------------
/// [이름] [오후 1:46] 본문
/// 이어지는 줄(앞 메시지에 붙음)
/// </code>
/// Only a line that fully matches the message or date pattern starts a record; anything else continues the open
/// message (real multi-line messages can start with "[" too). Header values (the counterpart's name, save time)
/// are not kept.
/// </summary>
public sealed partial class KakaoPcAdapter : IChatFormatAdapter
{
    public static readonly ChatFormatId Id = new("kakao.pc.ko.v1");

    private const int RegexTimeoutMilliseconds = 250;
    private const string TitleSuffix = " 님과 카카오톡 대화";
    private const string SavedPrefix = "저장한 날짜 : ";

    public ChatFormatId FormatId => Id;

    [GeneratedRegex(@"^-{3,} (?<y>\d{4})년 (?<mo>\d{1,2})월 (?<d>\d{1,2})일 [월화수목금토일]요일 -{3,}$", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
    private static partial Regex DateSeparator();

    [GeneratedRegex(@"^\[(?<name>[^\[\]]+(?:\[[^\[\]]*\][^\[\]]*)*)\] \[(?<ap>오전|오후) (?<h>\d{1,2}):(?<mi>\d{2})\](?: (?<text>.*))?$", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
    private static partial Regex MessageLine();

    public double Detect(IReadOnlyList<string> sampleLines)
    {
        ArgumentNullException.ThrowIfNull(sampleLines);
        var content = sampleLines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        if (content.Count == 0)
        {
            return 0;
        }

        if (content[0].EndsWith(TitleSuffix, StringComparison.Ordinal) && content.Skip(1).Take(1).Any(l => l.StartsWith(SavedPrefix, StringComparison.Ordinal)))
        {
            return 1.0;
        }

        // Without the header (e.g. a trimmed file): share of lines that are records of this format.
        return (double)content.Count(l => DateSeparator().IsMatch(l) || MessageLine().IsMatch(l)) / content.Count;
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
            if (line.EndsWith(TitleSuffix, StringComparison.Ordinal) || line.StartsWith(SavedPrefix, StringComparison.Ordinal))
            {
                return new(state, LineParseResult.Metadata(number, []));
            }

            state = state.EnterBody();
            if (string.IsNullOrWhiteSpace(line))
            {
                return new(state, LineParseResult.Ignored(number));
            }
        }

        if (DateSeparator().Match(line) is { Success: true } separator)
        {
            var issue = TryDate(separator, state.Context, out var date);
            if (issue != ParseIssue.None)
            {
                return Malformed(state, issue);
            }

            var (flushed, completed) = state.FlushPending();
            return new(flushed.WithDate(date), LineParseResult.Metadata(number, completed));
        }

        if (MessageLine().Match(line) is { Success: true } message)
        {
            return ParseMessage(state, message);
        }

        if (state.IsEntryOpen)
        {
            return new(state.AppendContinuation(line), LineParseResult.Continuation(number));
        }

        return string.IsNullOrWhiteSpace(line)
            ? new(state, LineParseResult.Ignored(number))
            : Malformed(state, ParseIssue.OrphanLine);
    }

    public override string ToString() => $"KakaoPcAdapter {{ {Id} }}";

    private static LineStep ParseMessage(ParserState state, Match message)
    {
        if (state.CurrentDate is not { } date)
        {
            return Malformed(state, ParseIssue.MissingDateContext);
        }

        var hour12 = Number(message, "h");
        var minute = Number(message, "mi");
        if (hour12 is < 1 or > 12 || minute > 59)
        {
            return Malformed(state, ParseIssue.InvalidTimestamp);
        }

        // 오전 12:xx = 00:xx, 오후 12:xx = 12:xx.
        var hour = (hour12 % 12) + (message.Groups["ap"].ValueSpan.SequenceEqual("오후") ? 12 : 0);
        var speaker = message.Groups["name"].Value;
        if (string.IsNullOrWhiteSpace(speaker))
        {
            return Malformed(state, ParseIssue.InvalidSpeakerName);
        }

        if (speaker.Length > state.Context.MaxSpeakerNameLength)
        {
            return Malformed(state, ParseIssue.SpeakerNameTooLong);
        }

        var timestamp = state.Context.ToTimestamp(date.ToDateTime(new TimeOnly(hour, minute)));
        var (next, completed) = state.StartMessage(timestamp, speaker, message.Groups["text"].Value);
        return new(next, LineParseResult.MessageStarted(state.LineNumber, completed));
    }

    private static ParseIssue TryDate(Match match, ParseContext context, out DateOnly date)
    {
        date = default;
        var (year, month, day) = (Number(match, "y"), Number(match, "mo"), Number(match, "d"));
        if (year < 1 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month))
        {
            return ParseIssue.InvalidTimestamp;
        }

        date = new DateOnly(year, month, day);
        return context.IsInRange(date) ? ParseIssue.None : ParseIssue.TimestampOutOfRange;
    }

    private static LineStep Malformed(ParserState state, ParseIssue issue)
    {
        var (flushed, completed) = state.FlushPending();
        return new(flushed, LineParseResult.Malformed(state.LineNumber, issue, completed));
    }

    private static int Number(Match match, string group) =>
        int.Parse(match.Groups[group].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture);
}

/// <summary>Real export formats (Phase 1.8b). Registered in every build once validated (DD-027).</summary>
public static class KakaoFormats
{
    public static IReadOnlyList<IChatFormatAdapter> All { get; } = [new KakaoPcAdapter()];
}
