using System.Text.RegularExpressions;
using TalkPro.Core.Parsing;

namespace TalkPro.Ingestion.Synthetic;

/// <summary>Synthetic A1 ("Android-like"): <c>yyyy-MM-dd 오전|오후 h:mm | name | text</c>, system <c>… * text</c>.</summary>
public sealed partial class SyntheticAndroidAdapter() : SyntheticAdapterBase(Id, 'A')
{
    public static readonly ChatFormatId Id = new("synthetic.android.v1");

    protected override bool IsReserved(string line) =>
        StartsWithDigits(line, 4) && line.Length >= 11
        && line[4] == '-' && char.IsAsciiDigit(line[5]) && char.IsAsciiDigit(line[6])
        && line[7] == '-' && char.IsAsciiDigit(line[8]) && char.IsAsciiDigit(line[9])
        && line[10] == ' ';

    protected override bool IsCompleteRecord(string line) => Record().IsMatch(line);

    protected override LineStep ParseRecord(ParserState state, string line)
    {
        var match = Record().Match(line);
        if (!match.Success)
        {
            return Malformed(state, ParseIssue.IncompleteRecord);
        }

        // 오전 12:xx = 00:xx, 오후 12:xx = 12:xx; hours outside 1–12 are invalid.
        var hour12 = Number(match.Groups["h"].ValueSpan);
        var hour = hour12 is >= 1 and <= 12
            ? (hour12 % 12) + (match.Groups["ap"].ValueSpan.SequenceEqual("오후") ? 12 : 0)
            : -1;

        var issue = TryCreateLocalTime(
            state.Context,
            match.Groups["y"].ValueSpan,
            match.Groups["mo"].ValueSpan,
            match.Groups["d"].ValueSpan,
            hour,
            Number(match.Groups["mi"].ValueSpan),
            second: 0,
            out var time);
        if (issue != ParseIssue.None)
        {
            return Malformed(state, issue);
        }

        if (match.Groups["sys"].Success)
        {
            return SystemEntry(state, time, match.Groups["sys"].Value);
        }

        var speaker = match.Groups["name"].Value;
        issue = ValidateSpeaker(state.Context, speaker);
        return issue == ParseIssue.None
            ? StartMessage(state, time, speaker, match.Groups["text"].Value)
            : Malformed(state, issue);
    }

    [GeneratedRegex(
        @"^(?<y>[0-9]{4})-(?<mo>[0-9]{2})-(?<d>[0-9]{2}) (?<ap>오전|오후) (?<h>[0-9]{1,2}):(?<mi>[0-9]{2}) (?:\| (?<name>.*?) \|(?: (?<text>.*))?|\* (?<sys>.*))$",
        RegexOptions.CultureInvariant,
        RegexTimeoutMilliseconds)]
    private static partial Regex Record();
}
