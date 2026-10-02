using System.Text.RegularExpressions;
using TalkPro.Core.Parsing;

namespace TalkPro.Ingestion.Synthetic;

/// <summary>Synthetic W1 ("Windows-like"): <c>=== yyyy-MM-dd (Ddd) ===</c> date blocks, <c>[name] [HH:mm] text</c>.</summary>
public sealed partial class SyntheticWindowsAdapter() : SyntheticAdapterBase(Id, 'W')
{
    public static readonly ChatFormatId Id = new("synthetic.windows.v1");

    protected override bool IsReserved(string line) =>
        line.StartsWith('[') || line.StartsWith("* [", StringComparison.Ordinal) || line.StartsWith("=== ", StringComparison.Ordinal);

    /// <summary>For detection a record also needs a well-formed <c>HH:mm</c> time (real layouts use other clocks).</summary>
    protected override bool IsCompleteRecord(string line) =>
        DateMarker().IsMatch(line) || HasClockTime(MessageLine().Match(line)) || HasClockTime(SystemLine().Match(line));

    protected override LineStep ParseRecord(ParserState state, string line)
    {
        if (line.StartsWith("=== ", StringComparison.Ordinal))
        {
            return ParseDateMarker(state, line);
        }

        if (line.StartsWith("* [", StringComparison.Ordinal))
        {
            var system = SystemLine().Match(line);
            if (!system.Success)
            {
                return Malformed(state, ParseIssue.IncompleteRecord);
            }

            var systemIssue = Resolve(state, system.Groups["time"].ValueSpan, out var systemTime);
            return systemIssue == ParseIssue.None
                ? SystemEntry(state, systemTime, system.Groups["text"].Value)
                : Malformed(state, systemIssue);
        }

        var message = MessageLine().Match(line);
        if (!message.Success)
        {
            return Malformed(state, ParseIssue.IncompleteRecord);
        }

        var issue = Resolve(state, message.Groups["time"].ValueSpan, out var time);
        var speaker = message.Groups["name"].Value;
        if (issue == ParseIssue.None)
        {
            issue = ValidateSpeaker(state.Context, speaker);
        }

        return issue == ParseIssue.None
            ? StartMessage(state, time, speaker, message.Groups["text"].Value)
            : Malformed(state, issue);
    }

    /// <summary>A valid marker sets the date context; an invalid one clears it so later lines cannot inherit a wrong date.</summary>
    private static LineStep ParseDateMarker(ParserState state, string line)
    {
        var marker = DateMarker().Match(line);
        if (!marker.Success)
        {
            return Malformed(state.ClearDate(), ParseIssue.IncompleteRecord);
        }

        var issue = TryCreateLocalTime(
            state.Context,
            marker.Groups["y"].ValueSpan,
            marker.Groups["mo"].ValueSpan,
            marker.Groups["d"].ValueSpan,
            hour: 0,
            minute: 0,
            second: 0,
            out var day);
        if (issue != ParseIssue.None)
        {
            return Malformed(state.ClearDate(), issue);
        }

        var (flushed, completed) = state.FlushPending();
        return new LineStep(flushed.WithDate(DateOnly.FromDateTime(day)), LineParseResult.Metadata(state.LineNumber, completed));
    }

    /// <summary><c>HH:mm</c> on the current date context. Timestamp errors take precedence over a missing context.</summary>
    private static ParseIssue Resolve(ParserState state, ReadOnlySpan<char> time, out DateTime localTime)
    {
        localTime = default;
        if (time.Length != 5 || time[2] != ':' || !IsDigits(time[..2]) || !IsDigits(time[3..]))
        {
            return ParseIssue.InvalidTimestamp;
        }

        int hour = Number(time[..2]), minute = Number(time[3..]);
        if (hour > 23 || minute > 59)
        {
            return ParseIssue.InvalidTimestamp;
        }

        if (state.CurrentDate is not { } date)
        {
            return ParseIssue.MissingDateContext;
        }

        localTime = date.ToDateTime(new TimeOnly(hour, minute));
        return ParseIssue.None;
    }

    private static bool HasClockTime(Match match)
    {
        var time = match.Groups["time"].ValueSpan;
        return match.Success && time.Length == 5 && time[2] == ':' && IsDigits(time[..2]) && IsDigits(time[3..]);
    }

    private static bool IsDigits(ReadOnlySpan<char> value)
    {
        foreach (var c in value)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    [GeneratedRegex(@"^=== (?<y>[0-9]{4})-(?<mo>[0-9]{2})-(?<d>[0-9]{2}) \([^)]*\) ===$", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
    private static partial Regex DateMarker();

    [GeneratedRegex(@"^\[(?<name>[^\]]*)\] \[(?<time>[^\]]*)\](?: (?<text>.*))?$", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
    private static partial Regex MessageLine();

    [GeneratedRegex(@"^\* \[(?<time>[^\]]*)\](?: (?<text>.*))?$", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
    private static partial Regex SystemLine();
}
