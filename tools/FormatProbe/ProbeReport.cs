using System.Globalization;
using System.Text;
using TalkPro.Core.Parsing;
using TalkPro.Ingestion.Parsing;

namespace TalkPro.Tools.FormatProbe;

/// <summary>
/// Builds content-free reports. Files are identified by position (#1, #2…), never by name or path,
/// because export file names often contain a chat partner's name.
/// </summary>
public static class ProbeReport
{
    public const int HeaderLines = 6;
    public const int TopSkeletons = 30;
    public const int MaxIssueSkeletons = 20;

    public const string Banner =
        "TalkPro FormatProbe — content-free report. Names and message text are replaced by length tokens such as ⟦H3⟧. " +
        "Review it before sharing; never share the export file itself.";

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static string Profile(IReadOnlyList<ReadOnlyMemory<byte>> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var report = new StringBuilder().AppendLine(Banner);
        for (var index = 0; index < files.Count; index++)
        {
            var bytes = files[index].Span;
            report.AppendLine().AppendLine(Invariant, $"== file #{index + 1}: {SizeBucket(bytes.Length)}");
            if (!TextDecoding.TryDecode(bytes, out var decoded))
            {
                report.AppendLine("encoding: unsupported (not UTF-8 or CP949)");
                continue;
            }

            var text = decoded.Text;
            var lines = LineSplitter.Split(text);
            report.AppendLine(Invariant, $"encoding: {decoded.Encoding}");
            report.AppendLine(Invariant, $"line endings: CRLF {Count(text, "\r\n")}, LF-only {CountLfOnly(text)}, CR-only {CountCrOnly(text)}; final newline: {(text.EndsWith('\n') || text.EndsWith('\r') ? "yes" : "no")}");
            report.AppendLine(Invariant, $"lines: {lines.Count:N0} (blank {lines.Count(string.IsNullOrWhiteSpace):N0})");

            report.AppendLine("header skeletons:");
            for (var i = 0; i < Math.Min(HeaderLines, lines.Count); i++)
            {
                report.AppendLine(Invariant, $"  {i + 1}: {LineSkeleton.Of(lines[i])}");
            }

            var histogram = lines.Where(l => !string.IsNullOrWhiteSpace(l))
                .GroupBy(LineSkeleton.Of, StringComparer.Ordinal)
                .Select(g => (Skeleton: g.Key, Count: g.Count()))
                .OrderByDescending(x => x.Count)
                .ThenBy(x => x.Skeleton, StringComparer.Ordinal)
                .ToList();
            var nonBlank = Math.Max(1, histogram.Sum(x => x.Count));
            report.AppendLine(Invariant, $"distinct skeletons: {histogram.Count:N0}; top {Math.Min(TopSkeletons, histogram.Count)}:");
            foreach (var (skeleton, count) in histogram.Take(TopSkeletons))
            {
                report.AppendLine(Invariant, $"  {100.0 * count / nonBlank,6:F2}% {count,8:N0}  {skeleton}");
            }
        }

        return report.ToString();
    }

    public static string Validate(IReadOnlyList<ReadOnlyMemory<byte>> files, IReadOnlyList<IChatFormatAdapter> adapters, ParseContext context)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(adapters);
        ArgumentNullException.ThrowIfNull(context);
        var parser = new ChatExportParser(adapters);
        var report = new StringBuilder().AppendLine(Banner);
        for (var index = 0; index < files.Count; index++)
        {
            var bytes = files[index].Span;
            report.AppendLine().AppendLine(Invariant, $"== file #{index + 1}: {SizeBucket(bytes.Length)}");
            var outcome = parser.Parse(bytes, context);
            if (!outcome.IsSuccess)
            {
                report.AppendLine(Invariant, $"result: failed ({outcome.Failure})");
                continue;
            }

            var chat = outcome.Chat!;
            var stats = chat.Statistics;
            var rate = (double)(stats.TotalLines - stats.Malformed - stats.Unsupported) / Math.Max(1, stats.TotalLines);
            report.AppendLine(Invariant, $"result: {chat.FormatId} · {chat.Encoding} · parse rate {rate:P2}");
            report.AppendLine(Invariant, $"lines {stats.TotalLines:N0} · messages {stats.MessagesStarted:N0} · system {stats.SystemMessages:N0} · continuation {stats.Continuations:N0} · metadata {stats.Metadata:N0} · ignored {stats.Ignored:N0} · malformed {stats.Malformed:N0} · unsupported {stats.Unsupported:N0}");
            report.AppendLine(Invariant, $"participants: {chat.Participants.Count:N0}");

            foreach (var group in chat.Issues.GroupBy(i => i.Issue).OrderByDescending(g => g.Count()))
            {
                report.AppendLine(Invariant, $"issue {group.Key}: {group.Count():N0}");
            }

            if (chat.Issues.Count > 0 && TextDecoding.TryDecode(bytes, out var decoded))
            {
                var lines = LineSplitter.Split(decoded.Text);
                report.AppendLine("issue line skeletons:");
                foreach (var skeleton in chat.Issues
                             .Select(i => LineSkeleton.Of(lines[i.LineNumber - 1]))
                             .GroupBy(s => s, StringComparer.Ordinal)
                             .OrderByDescending(g => g.Count())
                             .Take(MaxIssueSkeletons))
                {
                    report.AppendLine(Invariant, $"  {skeleton.Count(),6:N0}  {skeleton.Key}");
                }
            }
        }

        return report.ToString();
    }

    private static string SizeBucket(int bytes) => bytes switch
    {
        < 1024 => "< 1 KB",
        < 1024 * 1024 => "< 1 MB",
        < 10 * 1024 * 1024 => "1–10 MB",
        < 100 * 1024 * 1024 => "10–100 MB",
        _ => "≥ 100 MB",
    };

    private static int Count(string text, string value)
    {
        var count = 0;
        for (var i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static int CountLfOnly(string text) => text.Select((c, i) => c == '\n' && (i == 0 || text[i - 1] != '\r')).Count(x => x);

    private static int CountCrOnly(string text) => text.Select((c, i) => c == '\r' && (i + 1 >= text.Length || text[i + 1] != '\n')).Count(x => x);
}
