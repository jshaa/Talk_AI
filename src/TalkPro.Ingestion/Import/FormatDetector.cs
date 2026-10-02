using TalkPro.Core.Parsing;

namespace TalkPro.Ingestion.Import;

/// <summary>
/// Chooses an adapter from the content of the first lines (never from the file name). The best score
/// wins if it reaches <see cref="MinimumConfidence"/> and no other adapter has the same score.
/// </summary>
public static class FormatDetector
{
    public const int SampleLineCount = 50;

    public const double MinimumConfidence = 0.5;

    public static IChatFormatAdapter? Select(IReadOnlyList<IChatFormatAdapter> adapters, IReadOnlyList<string> lines, ParseContext context)
    {
        ArgumentNullException.ThrowIfNull(adapters);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(context);

        // Lines the driver would reject anyway are blanked (not removed) so line positions stay intact.
        var sample = lines
            .Take(SampleLineCount)
            .Select(line => line.Length > context.MaxLineLength || line.Contains('\0', StringComparison.Ordinal) ? string.Empty : line)
            .ToList();

        IChatFormatAdapter? best = null;
        var bestScore = 0.0;
        var tied = false;
        foreach (var adapter in adapters)
        {
            var score = adapter.Detect(sample);
            if (double.IsNaN(score) || score < 0 || score > 1)
            {
                throw new InvalidOperationException("Format adapter returned a confidence outside [0, 1].");
            }

            if (score > bestScore)
            {
                (best, bestScore, tied) = (adapter, score, false);
            }
            else if (score > 0 && score == bestScore)
            {
                tied = true;
            }
        }

        return best is not null && !tied && bestScore >= MinimumConfidence ? best : null;
    }
}
