using System.Text.RegularExpressions;
using TalkPro.Core.Parsing;

namespace TalkPro.Ingestion.RealFormats;

/// <summary>
/// Windows PC export, Korean UI (<c>docs/REAL_FORMATS.md</c>):
/// <c>--------------- 2026년 10월 1일 목요일 ---------------</c> date separators and
/// <c>[sender] [오후 3:15] text</c> (12-hour) or <c>[sender] [15:15] text</c> (24-hour) messages.
/// Lines without a timestamp (including untimed invite/leave notices) cannot be told apart from
/// message text and are treated as continuation lines.
/// </summary>
public sealed partial class WindowsChatFormatAdapter() : RealExportAdapterBase(Id)
{
    public static readonly ChatFormatId Id = new("kakaotalk.windows.ko.v1");

    protected override bool IsRecordShape(string line) => DateSeparator().IsMatch(line) || MessageLine().IsMatch(line);

    protected override LineStep? TryParseRecord(ParserState state, string line)
    {
        var separator = DateSeparator().Match(line);
        if (separator.Success)
        {
            var issue = TryCreateLocalTime(state.Context, Number(separator.Groups["y"]), Number(separator.Groups["mo"]), Number(separator.Groups["d"]), 0, 0, out var day);
            return DateMarker(state, issue, DateOnly.FromDateTime(day));
        }

        var message = MessageLine().Match(line);
        if (!message.Success)
        {
            return null;
        }

        var hourGroup = Number(message.Groups["h"]);
        var period = message.Groups["ap"];
        var hour = period.Success ? To24Hour(period.ValueSpan, hourGroup) : hourGroup;
        var minute = Number(message.Groups["mi"]);

        // Timestamp errors take precedence over a missing date context, then the sender is checked.
        if (hour is < 0 or > 23 || minute > 59)
        {
            return Malformed(state, ParseIssue.InvalidTimestamp);
        }

        if (state.CurrentDate is not { } date)
        {
            return Malformed(state, ParseIssue.MissingDateContext);
        }

        var localTime = date.ToDateTime(new TimeOnly(hour, minute));
        var speaker = message.Groups["name"].Value;
        var text = message.Groups["text"].Value;

        // An empty sender bracket is a messenger notice, not a person.
        return speaker.Length == 0 ? SystemEntry(state, localTime, text) : Message(state, localTime, speaker, text);
    }

    [GeneratedRegex(
        @"^-{15} (?<y>[0-9]{4})년 (?<mo>[0-9]{1,2})월 (?<d>[0-9]{1,2})일 [월화수목금토일]요일 -{15}$",
        RegexOptions.CultureInvariant,
        RegexTimeoutMilliseconds)]
    private static partial Regex DateSeparator();

    /// <summary>The sender ends at the first <c>] [</c> that is followed by a complete time and <c>]</c>.</summary>
    [GeneratedRegex(
        @"^\[(?<name>.*?)\] \[(?:(?<ap>오전|오후) )?(?<h>[0-9]{1,2}):(?<mi>[0-9]{2})\](?: (?<text>.*))?$",
        RegexOptions.CultureInvariant,
        RegexTimeoutMilliseconds)]
    private static partial Regex MessageLine();
}
