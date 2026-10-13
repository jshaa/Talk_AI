using TalkPro.Core.Model;
using TalkPro.Persona.Analysis;

namespace TalkPro.Persona.Style;

/// <summary>A counterpart message and the owner's first reply to it in the same session. Ids only.</summary>
public sealed record StyleExample(MessageId Prompt, MessageId Reply, ParticipantId Counterpart)
{
    public override string ToString() => $"StyleExample {{ Prompt = {Prompt}, Reply = {Reply} }}";
}

/// <summary>
/// Exemplar search over the owner's own replies (master prompt §9). Until local embeddings exist (Phase 7)
/// the ranking is: same counterpart, character-bigram overlap with the incoming message, then recency.
/// Empty, over-long and duplicate replies are skipped. Input must be the LlmPrompt view.
/// </summary>
public static class StyleExamples
{
    public const int DefaultCount = 8;
    public const int MaxReplyLength = 200;
    public const int MaxPromptLength = 300;

    public static IReadOnlyList<StyleExample> Select(
        ParticipantId owner,
        IReadOnlyList<ChatMessage> view,
        IReadOnlyList<Session> sessions,
        ParticipantId? counterpart,
        string incoming,
        int count = DefaultCount)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(incoming);
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);

        var byId = view.ToDictionary(m => m.Id);
        var candidates = new List<Candidate>();
        foreach (var session in sessions)
        {
            var ordered = session.MessageIds.Where(byId.ContainsKey).Select(id => byId[id]).Where(m => m.Kind == MessageKind.Text).ToList();
            for (var i = 1; i < ordered.Count; i++)
            {
                var (prompt, reply) = (ordered[i - 1], ordered[i]);
                if (reply.SpeakerId == owner
                    && prompt.SpeakerId is { } other && other != owner
                    && Usable(prompt.Text, MaxPromptLength) && Usable(reply.Text, MaxReplyLength))
                {
                    candidates.Add(new Candidate(new StyleExample(prompt.Id, reply.Id, other), prompt.Text, reply.Text.Trim(), candidates.Count));
                }
            }
        }

        var target = Bigrams(incoming);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return [.. candidates
            .OrderByDescending(c => Score(c, counterpart, target, candidates.Count))
            .Where(c => seen.Add(c.ReplyText))
            .Take(count)
            .OrderBy(c => c.Order)
            .Select(c => c.Example)];
    }

    private static double Score(Candidate c, ParticipantId? counterpart, HashSet<string> target, int total) =>
        (c.Example.Counterpart == counterpart ? 2.0 : 0.0)
        + (1.5 * Jaccard(target, Bigrams(c.PromptText)))
        + (0.5 * c.Order / Math.Max(1, total));

    private static bool Usable(string text, int maxLength)
    {
        var length = TextFeatures.Length(text.Trim());
        return length > 0 && length <= maxLength;
    }

    private static HashSet<string> Bigrams(string text)
    {
        var letters = new string([.. text.Where(c => !char.IsWhiteSpace(c) && !char.IsPunctuation(c))]);
        var set = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i + 1 < letters.Length; i++)
        {
            set.Add(letters.Substring(i, 2));
        }

        return set;
    }

    private static double Jaccard(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 || b.Count == 0)
        {
            return 0;
        }

        var intersection = a.Count(b.Contains);
        return (double)intersection / (a.Count + b.Count - intersection);
    }

    private sealed record Candidate(StyleExample Example, string PromptText, string ReplyText, int Order)
    {
        public override string ToString() => $"Candidate {{ {Example} }}";
    }
}
