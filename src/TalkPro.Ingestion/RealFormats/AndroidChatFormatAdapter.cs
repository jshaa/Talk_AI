using System.Text.RegularExpressions;
using TalkPro.Core.Parsing;

namespace TalkPro.Ingestion.RealFormats;

/// <summary>
/// Android export, Korean UI (<c>docs/REAL_FORMATS.md</c>): every record starts with
/// <c>2026년 10월 1일 오후 3:15</c>. Followed by nothing it is a date line; followed by
/// <c>, sender : text</c> a message; followed by <c>, text</c> without <c> : </c> a system line.
/// </summary>
public sealed partial class AndroidChatFormatAdapter() : RealExportAdapterBase(Id)
{
    public static readonly ChatFormatId Id = new("kakaotalk.android.ko.v1");

    protected override bool IsRecordShape(string line) => Record().IsMatch(line);

    protected override LineStep? TryParseRecord(ParserState state, string line)
    {
        var match = Record().Match(line);
        if (!match.Success)
        {
            return null;
        }

        var hour = To24Hour(match.Groups["ap"].ValueSpan, Number(match.Groups["h"]));
        var issue = TryCreateLocalTime(
            state.Context,
            Number(match.Groups["y"]),
            Number(match.Groups["mo"]),
            Number(match.Groups["d"]),
            hour,
            Number(match.Groups["mi"]),
            out var localTime);

        var rest = match.Groups["rest"];
        if (!rest.Success)
        {
            return DateMarker(state, issue, DateOnly.FromDateTime(localTime));
        }

        return issue == ParseIssue.None ? SenderRecord(state, localTime, rest.Value) : Malformed(state, issue);
    }

    [GeneratedRegex(
        @"^(?<y>[0-9]{4})년 (?<mo>[0-9]{1,2})월 (?<d>[0-9]{1,2})일 (?<ap>오전|오후) (?<h>[0-9]{1,2}):(?<mi>[0-9]{2})(?:, (?<rest>.*))?$",
        RegexOptions.CultureInvariant,
        RegexTimeoutMilliseconds)]
    private static partial Regex Record();
}
