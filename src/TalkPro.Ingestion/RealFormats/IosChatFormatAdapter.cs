using System.Text.RegularExpressions;
using TalkPro.Core.Parsing;

namespace TalkPro.Ingestion.RealFormats;

/// <summary>
/// iOS export, Korean UI (<c>docs/REAL_FORMATS.md</c>): <c>2026년 10월 1일 목요일</c> date headers and
/// two message layouts, distinguished by their start:
/// <c>2026. 10. 1. 오후 3:15, sender : text</c> (dotted date) and <c>오후 3:15, sender : text</c>
/// (time only; the date comes from the preceding header). Without <c> : </c> the record is a system line.
/// </summary>
public sealed partial class IosChatFormatAdapter() : RealExportAdapterBase(Id)
{
    public static readonly ChatFormatId Id = new("kakaotalk.ios.ko.v1");

    protected override bool IsRecordShape(string line) =>
        DateHeader().IsMatch(line) || DottedRecord().IsMatch(line) || TimeOnlyRecord().IsMatch(line);

    protected override LineStep? TryParseRecord(ParserState state, string line)
    {
        var header = DateHeader().Match(line);
        if (header.Success)
        {
            var issue = TryCreateLocalTime(state.Context, Number(header.Groups["y"]), Number(header.Groups["mo"]), Number(header.Groups["d"]), 0, 0, out var day);
            return DateMarker(state, issue, DateOnly.FromDateTime(day));
        }

        var dotted = DottedRecord().Match(line);
        if (dotted.Success)
        {
            var issue = TryCreateLocalTime(
                state.Context,
                Number(dotted.Groups["y"]),
                Number(dotted.Groups["mo"]),
                Number(dotted.Groups["d"]),
                To24Hour(dotted.Groups["ap"].ValueSpan, Number(dotted.Groups["h"])),
                Number(dotted.Groups["mi"]),
                out var localTime);
            return issue == ParseIssue.None ? SenderRecord(state, localTime, dotted.Groups["rest"].Value) : Malformed(state, issue);
        }

        var timeOnly = TimeOnlyRecord().Match(line);
        if (!timeOnly.Success)
        {
            return null;
        }

        var hour = To24Hour(timeOnly.Groups["ap"].ValueSpan, Number(timeOnly.Groups["h"]));
        var minute = Number(timeOnly.Groups["mi"]);
        if (hour < 0 || minute > 59)
        {
            return Malformed(state, ParseIssue.InvalidTimestamp);
        }

        return state.CurrentDate is { } date
            ? SenderRecord(state, date.ToDateTime(new TimeOnly(hour, minute)), timeOnly.Groups["rest"].Value)
            : Malformed(state, ParseIssue.MissingDateContext);
    }

    [GeneratedRegex(
        @"^(?<y>[0-9]{4})년 (?<mo>[0-9]{1,2})월 (?<d>[0-9]{1,2})일 [월화수목금토일]요일$",
        RegexOptions.CultureInvariant,
        RegexTimeoutMilliseconds)]
    private static partial Regex DateHeader();

    [GeneratedRegex(
        @"^(?<y>[0-9]{4})\. (?<mo>[0-9]{1,2})\. (?<d>[0-9]{1,2})\. (?<ap>오전|오후) (?<h>[0-9]{1,2}):(?<mi>[0-9]{2}), (?<rest>.*)$",
        RegexOptions.CultureInvariant,
        RegexTimeoutMilliseconds)]
    private static partial Regex DottedRecord();

    [GeneratedRegex(
        @"^(?<ap>오전|오후) (?<h>[0-9]{1,2}):(?<mi>[0-9]{2}), (?<rest>.*)$",
        RegexOptions.CultureInvariant,
        RegexTimeoutMilliseconds)]
    private static partial Regex TimeOnlyRecord();
}
