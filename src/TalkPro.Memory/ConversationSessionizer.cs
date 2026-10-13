using TalkPro.Core.Model;

namespace TalkPro.Memory;

public sealed record SessionizerOptions
{
    /// <summary>Gaps up to this always stay in the same session.</summary>
    public TimeSpan HardMerge { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>Gaps of at least this always start a new session.</summary>
    public TimeSpan HardSplit { get; init; } = TimeSpan.FromHours(6);

    public TimeSpan MinAdaptive { get; init; } = TimeSpan.FromMinutes(20);

    public TimeSpan MaxAdaptive { get; init; } = TimeSpan.FromHours(2);

    /// <summary>Used when a chat has too few short gaps to learn its own rhythm.</summary>
    public TimeSpan DefaultAdaptive { get; init; } = TimeSpan.FromMinutes(30);

    public int MinGapsToLearn { get; init; } = 20;

    public double SplitThreshold { get; init; } = 1.0;

    /// <summary>Very long sessions are cut so later retrieval units stay small.</summary>
    public int MaxMessagesPerSession { get; init; } = 400;

    public override string ToString() => $"SessionizerOptions {{ HardMerge = {HardMerge}, HardSplit = {HardSplit} }}";
}

/// <summary>
/// Hybrid sessionizer (master prompt §7): hard merge → hard split → adaptive threshold learned per chat
/// (P90 of in-session gaps, clamped) → discourse cues (continuation lowers, greeting raises, an open
/// question lowers the split score) → optional semantic similarity at candidate boundaries only.
/// Time gaps are the primary signal; the day boundary is not used (late-night talks stay together).
/// </summary>
public sealed class ConversationSessionizer(SessionizerOptions? options = null, ISemanticSimilarity? similarity = null) : ISessionizer
{
    private const int Window = 3;
    private readonly SessionizerOptions _options = Validate(options ?? new SessionizerOptions());

    public IReadOnlyList<Session> Split(IReadOnlyList<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (messages.Count == 0)
        {
            return [];
        }

        var threshold = LearnThreshold(messages);
        var boundaries = new List<int> { 0 };
        for (var i = 1; i < messages.Count; i++)
        {
            var gap = Gap(messages[i - 1], messages[i]);
            if (gap <= _options.HardMerge)
            {
                continue;
            }

            if (gap >= _options.HardSplit || (gap > threshold && Score(messages, i, gap, threshold) >= _options.SplitThreshold))
            {
                boundaries.Add(i);
            }
        }

        boundaries.Add(messages.Count);
        var sessions = new List<Session>();
        for (var b = 0; b < boundaries.Count - 1; b++)
        {
            for (var start = boundaries[b]; start < boundaries[b + 1]; start += _options.MaxMessagesPerSession)
            {
                var end = Math.Min(boundaries[b + 1], start + _options.MaxMessagesPerSession);
                var slice = messages.Skip(start).Take(end - start).ToList();
                sessions.Add(new Session(
                    new SessionId(sessions.Count + 1),
                    [.. slice.Select(m => m.Id)],
                    slice.Min(m => m.Timestamp),
                    slice.Max(m => m.Timestamp)));
            }
        }

        return sessions;
    }

    /// <summary>Per-chat rhythm: P90 of gaps shorter than the hard split, clamped to [MinAdaptive, MaxAdaptive].</summary>
    public TimeSpan LearnThreshold(IReadOnlyList<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var gaps = messages.Zip(messages.Skip(1), Gap).Where(g => g > TimeSpan.Zero && g < _options.HardSplit).Order().ToList();
        if (gaps.Count < _options.MinGapsToLearn)
        {
            return _options.DefaultAdaptive;
        }

        var p90 = gaps[(int)Math.Min(gaps.Count - 1, Math.Floor(gaps.Count * 0.9))];
        return p90 < _options.MinAdaptive ? _options.MinAdaptive : p90 > _options.MaxAdaptive ? _options.MaxAdaptive : p90;
    }

    private double Score(IReadOnlyList<ChatMessage> messages, int index, TimeSpan gap, TimeSpan threshold)
    {
        var score = 2.0 * Math.Min(gap / threshold, 3.0) / 3.0;
        var next = messages[index].Text;
        score -= 0.8 * DiscourseCues.ContinuationScore(next);
        score += 0.5 * DiscourseCues.GreetingScore(next);
        if (DiscourseCues.IsQuestion(messages[index - 1].Text))
        {
            score -= 0.6;
        }

        if (similarity is not null)
        {
            var before = messages.Skip(Math.Max(0, index - Window)).Take(Math.Min(Window, index)).ToList();
            var after = messages.Skip(index).Take(Window).ToList();
            score += 1.2 * (1.0 - Math.Clamp(similarity.Similarity(before, after), 0, 1)) - 0.6;
        }

        return score;
    }

    private static SessionizerOptions Validate(SessionizerOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxMessagesPerSession, 1, nameof(options));
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MinGapsToLearn, 1, nameof(options));
        if (options.MinAdaptive > options.MaxAdaptive || options.HardMerge >= options.HardSplit || options.DefaultAdaptive <= TimeSpan.Zero)
        {
            throw new ArgumentException("Sessionizer thresholds are inconsistent.", nameof(options));
        }

        return options;
    }

    /// <summary>Out-of-order timestamps count as no gap (the file order is kept).</summary>
    private static TimeSpan Gap(ChatMessage previous, ChatMessage next)
    {
        var gap = next.Timestamp - previous.Timestamp;
        return gap < TimeSpan.Zero ? TimeSpan.Zero : gap;
    }
}

/// <summary>Korean chat discourse cues (rule list, no content is stored).</summary>
public static class DiscourseCues
{
    private static readonly string[] Continuations = ["아까", "그래서", "근데", "그리고", "그러니까", "ㅇㅇ", "응", "웅", "어 ", "ㅋㅋ", "ㅎㅎ", "맞아", "그치", "그러게", "네", "넵", "ok", "ㅇㅋ"];
    private static readonly string[] Greetings = ["안녕", "굿모닝", "좋은 아침", "자니", "뭐해", "오랜만", "잘 지내", "hello", "hi ", "하이"];
    private static readonly string[] QuestionEndings = ["?", "냐", "까", "나요", "까요", "어때", "할래", "니?"];

    public static double ContinuationScore(string text) => StartsWithAny(text, Continuations) ? 1 : 0;

    public static double GreetingScore(string text) => StartsWithAny(text, Greetings) ? 1 : 0;

    public static bool IsQuestion(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var trimmed = text.TrimEnd(' ', '~', '.', '!', 'ㅋ', 'ㅎ');
        return text.Contains('?', StringComparison.Ordinal) || QuestionEndings.Any(e => trimmed.EndsWith(e, StringComparison.Ordinal));
    }

    private static bool StartsWithAny(string text, string[] cues)
    {
        ArgumentNullException.ThrowIfNull(text);
        var trimmed = text.TrimStart();
        return cues.Any(c => string.Equals(trimmed, c.Trim(), StringComparison.OrdinalIgnoreCase)
            || (trimmed.StartsWith(c, StringComparison.OrdinalIgnoreCase) && WordEnds(trimmed, c)));
    }

    /// <summary>A one-syllable cue must stand alone or repeat ("네", "응응"), so "네가 …" is not "네".</summary>
    private static bool WordEnds(string text, string cue)
    {
        if (cue.Length > 1 || text.Length == cue.Length)
        {
            return true;
        }

        var next = text[cue.Length];
        return next == cue[0] || next is not (>= '가' and <= '힣');
    }
}
