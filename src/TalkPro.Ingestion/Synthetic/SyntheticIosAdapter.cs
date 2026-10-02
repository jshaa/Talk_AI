using System.Text.RegularExpressions;
using TalkPro.Core.Parsing;

namespace TalkPro.Ingestion.Synthetic;

/// <summary>Synthetic I1 ("iOS-like"): <c>yyyy.MM.dd HH:mm:ss&lt;TAB&gt;name&lt;TAB&gt;text</c>, speaker <c>*</c> = system.</summary>
public sealed partial class SyntheticIosAdapter() : SyntheticAdapterBase(Id, 'I')
{
    public static readonly ChatFormatId Id = new("synthetic.ios.v1");

    protected override bool IsReserved(string line) =>
        StartsWithDigits(line, 4) && line.Length >= 11
        && line[4] == '.' && char.IsAsciiDigit(line[5]) && char.IsAsciiDigit(line[6])
        && line[7] == '.' && char.IsAsciiDigit(line[8]) && char.IsAsciiDigit(line[9])
        && line[10] == ' ';

    protected override bool IsCompleteRecord(string line) => Record().IsMatch(line);

    protected override LineStep ParseRecord(ParserState state, string line)
    {
        var match = Record().Match(line);
        if (!match.Success)
        {
            return Malformed(state, ParseIssue.IncompleteRecord);
        }

        var issue = TryCreateLocalTime(
            state.Context,
            match.Groups["y"].ValueSpan,
            match.Groups["mo"].ValueSpan,
            match.Groups["d"].ValueSpan,
            Number(match.Groups["h"].ValueSpan),
            Number(match.Groups["mi"].ValueSpan),
            Number(match.Groups["s"].ValueSpan),
            out var time);
        if (issue != ParseIssue.None)
        {
            return Malformed(state, issue);
        }

        var speaker = match.Groups["name"].Value;
        var text = match.Groups["text"].Value;
        if (speaker == SystemSpeaker)
        {
            return SystemEntry(state, time, text);
        }

        issue = ValidateSpeaker(state.Context, speaker);
        return issue == ParseIssue.None ? StartMessage(state, time, speaker, text) : Malformed(state, issue);
    }

    [GeneratedRegex(
        @"^(?<y>[0-9]{4})\.(?<mo>[0-9]{2})\.(?<d>[0-9]{2}) (?<h>[0-9]{2}):(?<mi>[0-9]{2}):(?<s>[0-9]{2})\t(?<name>[^\t]*)\t(?<text>.*)$",
        RegexOptions.CultureInvariant,
        RegexTimeoutMilliseconds)]
    private static partial Regex Record();
}
