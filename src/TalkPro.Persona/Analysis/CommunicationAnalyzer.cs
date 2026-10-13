using System.Globalization;
using TalkPro.Core.Model;

namespace TalkPro.Persona.Analysis;

public sealed record DistributionStats(int Count, double Mean, int P50, int P90)
{
    public override string ToString() => $"DistributionStats {{ Count = {Count}, P50 = {P50}, P90 = {P90} }}";
}

public sealed record RegisterMix(double Hapsyo, double Haeyo, double Banmal, double Unknown)
{
    public static readonly RegisterMix Empty = new(0, 0, 0, 0);

    public override string ToString() => $"RegisterMix {{ Hapsyo = {Hapsyo:F2}, Haeyo = {Haeyo:F2}, Banmal = {Banmal:F2} }}";
}

/// <summary>How the owner writes to one counterpart (the speaker right before the owner's reply). Display name is personal data.</summary>
public sealed record RelationshipStyle(ParticipantId Counterpart, string DisplayName, int Replies, double MeanLength, double LaughRate, RegisterMix Register)
{
    public override string ToString() => $"RelationshipStyle {{ Counterpart = {Counterpart}, Replies = {Replies} }}";
}

/// <summary>Owner style in one quarter, for "how my style changed".</summary>
public sealed record PeriodStyle(string Period, int Messages, double MeanLength, double LaughRate, double EmojiRate, RegisterMix Register)
{
    public override string ToString() => $"PeriodStyle {{ Period = {Period}, Messages = {Messages} }}";
}

public sealed record MonthlyActivity(string Month, int OwnerMessages, int AllMessages, int Sessions)
{
    public override string ToString() => $"MonthlyActivity {{ Month = {Month}, All = {AllMessages} }}";
}

public sealed record BusyDay(DateOnly Day, int Messages)
{
    public override string ToString() => $"BusyDay {{ Messages = {Messages} }}";
}

/// <summary>Keyword or emoji with its count. Local display only; never logged.</summary>
public sealed record TermCount(string Term, int Count)
{
    public override string ToString() => $"TermCount {{ Count = {Count} }}";
}

/// <summary>
/// Owner-centred communication report (master prompt §8). Counterparts are only used to attribute the
/// owner's replies; their emotions, psychology or affection are never analysed.
/// </summary>
public sealed record CommunicationReport(
    int TotalMessages,
    int OwnerMessages,
    int SessionCount,
    DateTimeOffset? FirstMessage,
    DateTimeOffset? LastMessage,
    int ActiveDays,
    DistributionStats Length,
    double MeanBurst,
    IReadOnlyList<int> ByHour,
    IReadOnlyList<int> ByWeekday,
    TimeSpan? MedianResponse,
    TimeSpan? P90Response,
    int ResponseSamples,
    double QuestionRate,
    double EmojiRate,
    IReadOnlyList<TermCount> TopEmojis,
    double LaughKRate,
    double LaughHRate,
    double MeanLaughRun,
    double PeriodEndRate,
    double TildeRate,
    double EllipsisRate,
    double ExclamationRate,
    RegisterMix Register,
    IReadOnlyList<RelationshipStyle> Relationships,
    IReadOnlyList<TermCount> Keywords,
    IReadOnlyList<MonthlyActivity> Timeline,
    IReadOnlyList<BusyDay> BusyDays,
    IReadOnlyList<PeriodStyle> Periods)
{
    public override string ToString() => $"CommunicationReport {{ Messages = {TotalMessages}, Owner = {OwnerMessages}, Sessions = {SessionCount} }}";
}

/// <summary>
/// Computes the owner-centred report. Input messages must already be the Analysis view (DD-020):
/// excluded messages removed, masking applied.
/// </summary>
public static class CommunicationAnalyzer
{
    public const int KeywordCount = 20;
    public const int EmojiCount = 5;
    public const int BusyDayCount = 5;

    public static CommunicationReport Analyze(ParticipantId owner, IReadOnlyList<Participant> participants, IReadOnlyList<ChatMessage> messages, IReadOnlyList<Session> sessions)
    {
        ArgumentNullException.ThrowIfNull(participants);
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(sessions);

        var text = messages.Where(m => m.Kind == MessageKind.Text).ToList();
        var mine = text.Where(m => m.SpeakerId == owner).ToList();
        var byId = messages.ToDictionary(m => m.Id);

        var lengths = mine.Select(m => TextFeatures.Length(m.Text)).Order().ToList();
        var (bursts, responses, replies) = Turns(owner, sessions, byId);

        return new CommunicationReport(
            TotalMessages: text.Count,
            OwnerMessages: mine.Count,
            SessionCount: sessions.Count,
            FirstMessage: messages.Count == 0 ? null : messages.Min(m => m.Timestamp),
            LastMessage: messages.Count == 0 ? null : messages.Max(m => m.Timestamp),
            ActiveDays: mine.Select(m => DateOnly.FromDateTime(m.Timestamp.DateTime)).Distinct().Count(),
            Length: new DistributionStats(lengths.Count, lengths.Count == 0 ? 0 : lengths.Average(), Percentile(lengths, 0.5), Percentile(lengths, 0.9)),
            MeanBurst: bursts.Count == 0 ? 0 : bursts.Average(),
            ByHour: [.. Enumerable.Range(0, 24).Select(h => mine.Count(m => m.Timestamp.Hour == h))],
            ByWeekday: [.. Enumerable.Range(0, 7).Select(d => mine.Count(m => (int)m.Timestamp.DayOfWeek == d))],
            MedianResponse: responses.Count == 0 ? null : PercentileTime(responses, 0.5),
            P90Response: responses.Count == 0 ? null : PercentileTime(responses, 0.9),
            ResponseSamples: responses.Count,
            QuestionRate: Rate(mine, m => TextFeatures.IsQuestion(m.Text)),
            EmojiRate: Rate(mine, m => TextFeatures.Emojis(m.Text).Any()),
            TopEmojis: [.. mine.SelectMany(m => TextFeatures.Emojis(m.Text)).GroupBy(e => e, StringComparer.Ordinal)
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).Take(EmojiCount).Select(g => new TermCount(g.Key, g.Count()))],
            LaughKRate: Rate(mine, m => TextFeatures.LaughRun(m.Text, 'ㅋ') > 0),
            LaughHRate: Rate(mine, m => TextFeatures.LaughRun(m.Text, 'ㅎ') > 0),
            MeanLaughRun: MeanOrZero(mine.Select(m => TextFeatures.LaughRun(m.Text, 'ㅋ')).Where(r => r > 0)),
            PeriodEndRate: Rate(mine, m => TextFeatures.EndsWithPeriod(m.Text)),
            TildeRate: Rate(mine, m => TextFeatures.HasTilde(m.Text)),
            EllipsisRate: Rate(mine, m => TextFeatures.HasEllipsis(m.Text)),
            ExclamationRate: Rate(mine, m => TextFeatures.HasExclamation(m.Text)),
            Register: Mix(mine),
            Relationships: Relationships(participants, replies),
            Keywords: [.. KeywordExtractor.Top(mine.Select(m => m.Text), KeywordCount).Select(k => new TermCount(k.Word, k.Count))],
            Timeline: Timeline(owner, text, sessions),
            BusyDays: [.. text.GroupBy(m => DateOnly.FromDateTime(m.Timestamp.DateTime)).OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
                .Take(BusyDayCount).Select(g => new BusyDay(g.Key, g.Count()))],
            Periods: [.. mine.GroupBy(m => Quarter(m.Timestamp)).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => new PeriodStyle(
                g.Key,
                g.Count(),
                g.Average(m => TextFeatures.Length(m.Text)),
                Rate(g, m => TextFeatures.LaughRun(m.Text, 'ㅋ') > 0),
                Rate(g, m => TextFeatures.Emojis(m.Text).Any()),
                Mix(g)))]);
    }

    private static (List<int> Bursts, List<TimeSpan> Responses, List<(ParticipantId Counterpart, ChatMessage Reply)> Replies) Turns(
        ParticipantId owner, IReadOnlyList<Session> sessions, Dictionary<MessageId, ChatMessage> byId)
    {
        var bursts = new List<int>();
        var responses = new List<TimeSpan>();
        var replies = new List<(ParticipantId, ChatMessage)>();
        foreach (var session in sessions)
        {
            var ordered = session.MessageIds.Where(byId.ContainsKey).Select(id => byId[id]).Where(m => m.Kind == MessageKind.Text).ToList();
            var run = 0;
            for (var i = 0; i < ordered.Count; i++)
            {
                var message = ordered[i];
                if (message.SpeakerId != owner)
                {
                    if (run > 0)
                    {
                        bursts.Add(run);
                    }

                    run = 0;
                    continue;
                }

                if (run == 0 && i > 0 && ordered[i - 1].SpeakerId is { } previous && previous != owner)
                {
                    var gap = message.Timestamp - ordered[i - 1].Timestamp;
                    responses.Add(gap < TimeSpan.Zero ? TimeSpan.Zero : gap);
                    replies.Add((previous, message));
                }

                run++;
            }

            if (run > 0)
            {
                bursts.Add(run);
            }
        }

        return (bursts, responses, replies);
    }

    private static List<RelationshipStyle> Relationships(IReadOnlyList<Participant> participants, List<(ParticipantId Counterpart, ChatMessage Reply)> replies)
    {
        var names = participants.Where(p => p.Consent != ParticipantConsent.Excluded).ToDictionary(p => p.Id, p => p.DisplayName);
        return [.. replies.Where(r => names.ContainsKey(r.Counterpart))
            .GroupBy(r => r.Counterpart)
            .OrderByDescending(g => g.Count())
            .Select(g => new RelationshipStyle(
                g.Key,
                names[g.Key],
                g.Count(),
                g.Average(r => TextFeatures.Length(r.Reply.Text)),
                Rate(g.Select(r => r.Reply), m => TextFeatures.LaughRun(m.Text, 'ㅋ') > 0),
                Mix(g.Select(r => r.Reply))))];
    }

    private static List<MonthlyActivity> Timeline(ParticipantId owner, List<ChatMessage> text, IReadOnlyList<Session> sessions) =>
        [.. text.GroupBy(m => Month(m.Timestamp)).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => new MonthlyActivity(
            g.Key,
            g.Count(m => m.SpeakerId == owner),
            g.Count(),
            sessions.Count(s => Month(s.Start) == g.Key)))];

    private static RegisterMix Mix(IEnumerable<ChatMessage> messages)
    {
        var registers = messages.Select(m => TextFeatures.ClassifyRegister(m.Text)).ToList();
        if (registers.Count == 0)
        {
            return RegisterMix.Empty;
        }

        double Share(Register r) => (double)registers.Count(x => x == r) / registers.Count;
        return new RegisterMix(Share(Register.Hapsyo), Share(Register.Haeyo), Share(Register.Banmal), Share(Register.Unknown));
    }

    private static double Rate(IEnumerable<ChatMessage> messages, Func<ChatMessage, bool> predicate)
    {
        var list = messages as IReadOnlyCollection<ChatMessage> ?? [.. messages];
        return list.Count == 0 ? 0 : (double)list.Count(predicate) / list.Count;
    }

    private static double MeanOrZero(IEnumerable<int> values)
    {
        var list = values.ToList();
        return list.Count == 0 ? 0 : list.Average();
    }

    private static int Percentile(List<int> sorted, double p) =>
        sorted.Count == 0 ? 0 : sorted[(int)Math.Min(sorted.Count - 1, Math.Floor((sorted.Count - 1) * p + 0.5))];

    private static TimeSpan PercentileTime(List<TimeSpan> values, double p)
    {
        var sorted = values.Order().ToList();
        return sorted[(int)Math.Min(sorted.Count - 1, Math.Floor((sorted.Count - 1) * p + 0.5))];
    }

    private static string Month(DateTimeOffset t) => t.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    private static string Quarter(DateTimeOffset t) =>
        string.Create(CultureInfo.InvariantCulture, $"{t.Year} Q{((t.Month - 1) / 3) + 1}");
}
