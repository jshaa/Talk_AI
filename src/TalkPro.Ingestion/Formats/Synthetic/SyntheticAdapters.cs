using System.Text.RegularExpressions;
using TalkPro.Core.Parsing;

namespace TalkPro.Ingestion.Formats.Synthetic;

/// <summary>W1: date blocks, 24h time, <c>[name] [HH:mm] text</c>.</summary>
public sealed partial class SyntheticWindowsAdapter : SyntheticFormatAdapter
{
    public static readonly ChatFormatId Id = new("synthetic.windows.v1");

    public override ChatFormatId FormatId => Id;

    protected override string SignatureCode => "W1";

    [GeneratedRegex(@"^=== (?<y>\d{4})-(?<mo>\d{2})-(?<d>\d{2}) \([A-Za-z]{3}\) ===$", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
    private static partial Regex DateMarker();

    [GeneratedRegex(@"^\[(?<name>[^\]]*)\] \[(?<h>\d{2}):(?<mi>\d{2})\] (?<text>.*)$", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
    private static partial Regex MessageLine();

    [GeneratedRegex(@"^\* \[(?<h>\d{2}):(?<mi>\d{2})\] (?<text>.*)$", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
    private static partial Regex SystemLine();

    protected override bool HasReservedPrefix(string line) =>
        line.StartsWith('[') || line.StartsWith("* [", StringComparison.Ordinal) || line.StartsWith("=== ", StringComparison.Ordinal);

    protected override bool IsWellFormedRecord(string line) =>
        DateMarker().IsMatch(line) || MessageLine().IsMatch(line) || SystemLine().IsMatch(line);

    protected override LineStep ParseRecord(ParserState state, string line)
    {
        if (DateMarker().Match(line) is { Success: true } marker)
        {
            var issue = TryDate(marker, state.Context, out var date);
            if (issue != ParseIssue.None)
            {
                return Malformed(state, issue);
            }

            var (flushed, completed) = state.FlushPending();
            return new(flushed.WithDate(date), LineParseResult.Metadata(state.LineNumber, completed));
        }

        var message = MessageLine().Match(line);
        var system = message.Success ? Match.Empty : SystemLine().Match(line);
        var record = message.Success ? message : system;
        if (!record.Success)
        {
            return Malformed(state, ParseIssue.IncompleteRecord);
        }

        if (state.CurrentDate is not { } currentDate)
        {
            return Malformed(state, ParseIssue.MissingDateContext);
        }

        var timeIssue = TryTimestamp(currentDate, Number(record, "h"), Number(record, "mi"), 0, state.Context, out var timestamp);
        if (timeIssue != ParseIssue.None)
        {
            return Malformed(state, timeIssue);
        }

        var text = record.Groups["text"].Value;
        return message.Success
            ? StartMessage(state, timestamp, message.Groups["name"].Value, text)
            : SystemLine(state, timestamp, text);
    }
}

/// <summary>A1: per-line date, Korean 12h clock, <c>date 오전|오후 h:mm | name | text</c>.</summary>
public sealed partial class SyntheticAndroidAdapter : SyntheticFormatAdapter
{
    public static readonly ChatFormatId Id = new("synthetic.android.v1");

    private const string FieldSeparator = " | ";
    private const string SystemMarker = " * ";

    public override ChatFormatId FormatId => Id;

    protected override string SignatureCode => "A1";

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2} ", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
    private static partial Regex ReservedPrefix();

    [GeneratedRegex(@"^(?<y>\d{4})-(?<mo>\d{2})-(?<d>\d{2}) (?<ap>오전|오후) (?<h>\d{1,2}):(?<mi>\d{2})(?<rest>( \| | \* ).*)$", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
    private static partial Regex Record();

    protected override bool HasReservedPrefix(string line) => ReservedPrefix().IsMatch(line);

    protected override bool IsWellFormedRecord(string line) => Record().IsMatch(line);

    protected override LineStep ParseRecord(ParserState state, string line)
    {
        var record = Record().Match(line);
        if (!record.Success)
        {
            return Malformed(state, ParseIssue.IncompleteRecord);
        }

        var dateIssue = TryDate(record, state.Context, out var date);
        if (dateIssue != ParseIssue.None)
        {
            return Malformed(state, dateIssue);
        }

        var hour12 = Number(record, "h");
        if (hour12 is < 1 or > 12)
        {
            return Malformed(state, ParseIssue.InvalidTimestamp);
        }

        var hour = (hour12 % 12) + (record.Groups["ap"].ValueSpan.SequenceEqual("오후") ? 12 : 0);
        var timeIssue = TryTimestamp(date, hour, Number(record, "mi"), 0, state.Context, out var timestamp);
        if (timeIssue != ParseIssue.None)
        {
            return Malformed(state, timeIssue);
        }

        state = state.WithDate(date);
        var rest = record.Groups["rest"].Value;
        if (rest.StartsWith(SystemMarker, StringComparison.Ordinal))
        {
            return SystemLine(state, timestamp, rest[SystemMarker.Length..]);
        }

        var body = rest[FieldSeparator.Length..];
        var split = body.IndexOf(FieldSeparator, StringComparison.Ordinal);
        return split < 0
            ? Malformed(state, ParseIssue.IncompleteRecord)
            : StartMessage(state, timestamp, body[..split], body[(split + FieldSeparator.Length)..]);
    }
}

/// <summary>I1: dotted date with seconds, tab-separated <c>name</c> and <c>text</c>; name <c>*</c> = system.</summary>
public sealed partial class SyntheticIosAdapter : SyntheticFormatAdapter
{
    public static readonly ChatFormatId Id = new("synthetic.ios.v1");

    public override ChatFormatId FormatId => Id;

    protected override string SignatureCode => "I1";

    [GeneratedRegex(@"^\d{4}\.\d{2}\.\d{2} ", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
    private static partial Regex ReservedPrefix();

    [GeneratedRegex(@"^(?<y>\d{4})\.(?<mo>\d{2})\.(?<d>\d{2}) (?<h>\d{2}):(?<mi>\d{2}):(?<s>\d{2})\t(?<name>[^\t]*)\t(?<text>.*)$", RegexOptions.CultureInvariant, RegexTimeoutMilliseconds)]
    private static partial Regex Record();

    protected override bool HasReservedPrefix(string line) => ReservedPrefix().IsMatch(line);

    protected override bool IsWellFormedRecord(string line) => Record().IsMatch(line);

    protected override LineStep ParseRecord(ParserState state, string line)
    {
        var record = Record().Match(line);
        if (!record.Success)
        {
            return Malformed(state, ParseIssue.IncompleteRecord);
        }

        var dateIssue = TryDate(record, state.Context, out var date);
        if (dateIssue != ParseIssue.None)
        {
            return Malformed(state, dateIssue);
        }

        var timeIssue = TryTimestamp(date, Number(record, "h"), Number(record, "mi"), Number(record, "s"), state.Context, out var timestamp);
        if (timeIssue != ParseIssue.None)
        {
            return Malformed(state, timeIssue);
        }

        state = state.WithDate(date);
        var name = record.Groups["name"].Value;
        var text = record.Groups["text"].Value;
        return name == "*" ? SystemLine(state, timestamp, text) : StartMessage(state, timestamp, name, text);
    }
}

/// <summary>All synthetic adapters. Test-only: not registered in the production container (DD-012).</summary>
public static class SyntheticFormats
{
    public static IReadOnlyList<IChatFormatAdapter> All { get; } =
        [new SyntheticWindowsAdapter(), new SyntheticAndroidAdapter(), new SyntheticIosAdapter()];
}
