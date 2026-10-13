using System.Text.RegularExpressions;
using TalkPro.Core.Model;

namespace TalkPro.Memory;

/// <summary>Hybrid score weights (report §6.3 starting values; entity overlap is not used in MVP1).</summary>
public sealed record RetrievalWeights
{
    public double Dense { get; init; } = 0.55;

    public double Sparse { get; init; } = 0.25;

    public double Recency { get; init; } = 0.05;

    public double Participant { get; init; } = 0.10;

    /// <summary>Recency half-life-like constant: exp(-Δdays / τ).</summary>
    public double RecencyDays { get; init; } = 365;

    /// <summary>
    /// A dense-only candidate (no keyword hit) needs at least this cosine. Absolute cosines alone do not separate
    /// relevant from unrelated well (bge-m3, synthetic Korean chats, 2026-10-13: paraphrase 0.50–0.68, unrelated up
    /// to 0.48), so the two relative rules below do most of the work.
    /// </summary>
    public double DenseFloor { get; init; } = 0.45;

    /// <summary>…must be within this distance of the best dense score (drops off-topic items next to a strong match)…</summary>
    public double DenseMargin { get; init; } = 0.1;

    /// <summary>…and must stand out from the median dense score by this much (an unrelated query scores everything alike).</summary>
    public double DenseSeparation { get; init; } = 0.08;

    public override string ToString() => $"RetrievalWeights {{ Dense = {Dense}, Sparse = {Sparse}, Recency = {Recency}, Participant = {Participant} }}";
}

/// <summary>One ranked item with its score parts (for "왜 이 기록인가요?").</summary>
public sealed record ScoredItem(MemoryItem Item, double Score, double Dense, double Sparse, double Recency, double Participant) : Core.Diagnostics.ILogSafe
{
    public override string ToString() => $"ScoredItem {{ {Item}, Score = {Score:F3} }}";
}

/// <summary>
/// Fuses dense similarity, keyword (FTS5) hits, recency and participant match. A candidate needs a keyword hit
/// or a strong dense match; anything else is dropped so that "기록에서 찾지 못했습니다" can be said honestly.
/// </summary>
public static class HybridRetriever
{
    public const int DefaultCount = 8;

    public static IReadOnlyList<ScoredItem> Rank(
        IReadOnlyList<MemoryItem> items,
        IReadOnlyDictionary<MemoryItemId, double> dense,
        IReadOnlyDictionary<MessageId, double> sparse,
        DateTimeOffset now,
        Func<MemoryItem, bool> participantMatch,
        RetrievalWeights? weights = null,
        int count = DefaultCount)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(dense);
        ArgumentNullException.ThrowIfNull(sparse);
        ArgumentNullException.ThrowIfNull(participantMatch);
        weights ??= new RetrievalWeights();
        var maxSparse = sparse.Count == 0 ? 0 : sparse.Values.Max();
        var bestDense = items.Select(i => dense.GetValueOrDefault(i.Id)).DefaultIfEmpty(0).Max();
        var denseCut = Math.Max(weights.DenseFloor, bestDense - weights.DenseMargin);
        var denseMedian = Median(dense.Values);

        var ranked = new List<ScoredItem>();
        foreach (var item in items)
        {
            var d = dense.TryGetValue(item.Id, out var cos) ? Math.Max(0, cos) : 0;
            var s = maxSparse <= 0 ? 0 : item.SourceMessageIds.Select(id => sparse.GetValueOrDefault(id)).Max() / maxSparse;
            if (s <= 0 && (d < denseCut || d - denseMedian < weights.DenseSeparation))
            {
                continue;
            }

            var r = Math.Exp(-Math.Max(0, (now - item.End).TotalDays) / weights.RecencyDays);
            var p = participantMatch(item) ? 1.0 : 0.0;
            ranked.Add(new ScoredItem(item, (weights.Dense * d) + (weights.Sparse * s) + (weights.Recency * r) + (weights.Participant * p), d, s, r, p));
        }

        return [.. ranked.OrderByDescending(x => x.Score).ThenByDescending(x => x.Item.End).Take(count)];
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToList();
        return sorted.Count == 0 ? 0 : sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[(sorted.Count / 2) - 1] + sorted[sorted.Count / 2]) / 2;
    }
}

/// <summary>A segment of model output after grounding (Phase 6.5 preparation).</summary>
public sealed record GroundedSegment(GroundingLabel Label, string? Text, MessageId? Quote) : Core.Diagnostics.ILogSafe
{
    public override string ToString() => $"GroundedSegment {{ Label = {Label}, Quote = {Quote} }}";
}

/// <summary>
/// QUOTE verification (report §7.5): a model may quote only by reference, <c>{{quote:m0000123}}</c>. A reference to a
/// message in the evidence set becomes a <see cref="GroundingLabel.Quote"/> segment whose text is rendered from the
/// vault, never from the model; any other reference is <see cref="GroundingLabel.Unsupported"/> and must not be shown.
/// Plain text is <see cref="GroundingLabel.Generated"/> until a claim checker (Phase 6.5) says otherwise.
/// </summary>
public static partial class QuoteVerifier
{
    public static IReadOnlyList<GroundedSegment> Verify(string modelText, IReadOnlySet<MessageId> evidence)
    {
        ArgumentNullException.ThrowIfNull(modelText);
        ArgumentNullException.ThrowIfNull(evidence);
        var segments = new List<GroundedSegment>();
        var last = 0;
        foreach (Match match in Reference().Matches(modelText))
        {
            AddText(segments, modelText[last..match.Index]);
            var id = new MessageId(match.Groups[1].Value);
            segments.Add(new GroundedSegment(evidence.Contains(id) ? GroundingLabel.Quote : GroundingLabel.Unsupported, null, id));
            last = match.Index + match.Length;
        }

        AddText(segments, modelText[last..]);
        return segments;
    }

    private static void AddText(List<GroundedSegment> segments, string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            segments.Add(new GroundedSegment(GroundingLabel.Generated, text.Trim(), null));
        }
    }

    [GeneratedRegex(@"\{\{quote:([A-Za-z0-9_\-]{1,40})\}\}", RegexOptions.CultureInvariant, 100)]
    private static partial Regex Reference();
}
