using TalkPro.Core.Model;
using TalkPro.Privacy.Detection;

namespace TalkPro.Privacy;

public interface IPrivacyScanner
{
    /// <summary>Detects findings in every message. Raw messages are never modified.</summary>
    IReadOnlyList<PiiFinding> Scan(IReadOnlyList<ChatMessage> messages, IReadOnlyList<Participant> participants);
}

/// <summary>
/// Runs the L1/L2/L3 detectors and resolves overlaps: span findings never overlap (higher-risk, then
/// longer spans win), sensitive-topic findings are message-level hints (one per topic per message).
/// </summary>
public sealed class PrivacyScanner(IEnumerable<IPiiDetector> detectors) : IPrivacyScanner
{
    private readonly IReadOnlyList<IPiiDetector> _detectors = [.. detectors];

    public static PrivacyScanner CreateDefault() => new([new PatternDetector(), new DictionaryDetector(), new SensitiveKeywordDetector()]);

    public IReadOnlyList<PiiFinding> Scan(IReadOnlyList<ChatMessage> messages, IReadOnlyList<Participant> participants)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(participants);
        var context = DetectionContext.From(participants);
        var result = new List<PiiFinding>();

        foreach (var message in messages)
        {
            if (message.Text.Length == 0)
            {
                continue;
            }

            var raw = _detectors.SelectMany(d => d.Detect(message.Text, context).Select(f => (Finding: f, Layer: d.Layer))).ToList();
            foreach (var (finding, layer) in Resolve(raw).OrderBy(x => x.Finding.Start).ThenBy(x => x.Finding.Type))
            {
                result.Add(new PiiFinding(new FindingId(result.Count + 1), message.Id, finding.Start, finding.Length, finding.Type, finding.Confidence, layer));
            }
        }

        return result;
    }

    private static List<(RawFinding Finding, DetectionLayer Layer)> Resolve(List<(RawFinding Finding, DetectionLayer Layer)> raw)
    {
        var accepted = new List<(RawFinding Finding, DetectionLayer Layer)>();

        var spans = raw
            .Where(x => PiiTypes.CategoryOf(x.Finding.Type) != PiiCategory.Sensitive)
            .OrderByDescending(x => Priority(x.Finding.Type))
            .ThenByDescending(x => x.Finding.Length)
            .ThenBy(x => x.Finding.Start);
        foreach (var candidate in spans)
        {
            var f = candidate.Finding;
            if (!accepted.Any(a => f.Start < a.Finding.Start + a.Finding.Length && a.Finding.Start < f.Start + f.Length))
            {
                accepted.Add(candidate);
            }
        }

        accepted.AddRange(raw
            .Where(x => PiiTypes.CategoryOf(x.Finding.Type) == PiiCategory.Sensitive)
            .GroupBy(x => x.Finding.Type)
            .Select(g => g.OrderBy(x => x.Finding.Start).First()));

        return accepted;
    }

    private static int Priority(PiiType type) => PiiTypes.CategoryOf(type) switch
    {
        PiiCategory.HighRiskIdentifier => 3,
        PiiCategory.Identifier => 2,
        _ => 1,
    };
}
