using System.Globalization;
using TalkPro.Core.Model;
using TalkPro.Persona.Analysis;

namespace TalkPro.Persona.Tests;

public sealed class CommunicationAnalyzerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 3, 2, 9, 0, 0, TimeSpan.FromHours(9));
    private static readonly ParticipantId Me = new("p_01");
    private static readonly ParticipantId Friend = new("p_02");
    private static readonly ParticipantId Boss = new("p_03");

    private static readonly Participant[] People =
    [
        new(Me, "UserA", ParticipantConsent.Owner),
        new(Friend, "UserB", ParticipantConsent.ContextOnly),
        new(Boss, "UserC", ParticipantConsent.ContextOnly),
    ];

    [Fact]
    public void StyleIsMeasuredOnTheOwnersMessagesOnly()
    {
        var messages = Build(
            (0, Friend, "ㅋㅋㅋㅋㅋㅋ 😀😀 대박!!!"),
            (1, Me, "알겠어."),
            (2, Friend, "ㅋㅋㅋㅋ"),
            (3, Me, "좋아"));

        var report = Analyze(messages);

        Assert.Equal(4, report.TotalMessages);
        Assert.Equal(2, report.OwnerMessages);
        Assert.Equal(0, report.LaughKRate);
        Assert.Equal(0, report.EmojiRate);
        Assert.Equal(0, report.ExclamationRate);
        Assert.Equal(0.5, report.PeriodEndRate);
        Assert.Equal(1.0, report.Register.Banmal);
    }

    [Fact]
    public void ResponseTimeCountsOnlyTheFirstOwnerReplyAfterACounterpart()
    {
        var messages = Build(
            (0, Me, "먼저 보냄"),
            (1, Friend, "응"),
            (4, Me, "답장"),
            (5, Me, "이어서"),
            (6, Me, "또 이어서"),
            (7, Boss, "확인 부탁합니다"),
            (17, Me, "확인했습니다"),
            (18, Friend, "ㅇㅋ"),
            (19, Me, "ㅇㅇ"));

        var report = Analyze(messages);

        Assert.Equal(3, report.ResponseSamples);
        Assert.Equal(TimeSpan.FromMinutes(3), report.MedianResponse);
        Assert.Equal(TimeSpan.FromMinutes(10), report.P90Response);
        Assert.Equal((1 + 3 + 1 + 1) / 4.0, report.MeanBurst, 3);
    }

    [Fact]
    public void RepliesAreAttributedToThePrecedingCounterpart()
    {
        var messages = Build(
            (0, Friend, "뭐해"),
            (1, Me, "그냥 있어 ㅋㅋ"),
            (2, Boss, "회의 자료 부탁합니다"),
            (3, Me, "네 보내드리겠습니다"),
            (4, Friend, "밥 먹자"),
            (5, Me, "좋아"));

        var report = Analyze(messages);

        var friend = report.Relationships.Single(r => r.Counterpart == Friend);
        var boss = report.Relationships.Single(r => r.Counterpart == Boss);
        Assert.Equal(2, friend.Replies);
        Assert.Equal(1.0, friend.Register.Banmal);
        Assert.Equal(0.5, friend.LaughRate);
        Assert.Equal(1, boss.Replies);
        Assert.Equal(1.0, boss.Register.Hapsyo);
        Assert.Equal(Friend, report.Relationships[0].Counterpart);
    }

    [Fact]
    public void RepliesAcrossSessionsAreNotCounted()
    {
        var messages = Build((0, Friend, "뭐해"), (600, Me, "이제 봤어"));
        Session[] sessions =
        [
            new(new SessionId(1), [messages[0].Id], messages[0].Timestamp, messages[0].Timestamp),
            new(new SessionId(2), [messages[1].Id], messages[1].Timestamp, messages[1].Timestamp),
        ];

        var report = CommunicationAnalyzer.Analyze(Me, People, messages, sessions);

        Assert.Equal(0, report.ResponseSamples);
        Assert.Empty(report.Relationships);
    }

    [Fact]
    public void TimeDistributionsTimelineAndPeriods()
    {
        var messages = Build(
            (0, Me, "아침"),
            (60, Me, "점심"),
            (60 * 24 * 100, Friend, "오랜만"),
            ((60 * 24 * 100) + 1, Me, "반가워요"));

        var report = Analyze(messages);

        Assert.Equal(2, report.ByHour[9]);
        Assert.Equal(1, report.ByHour[10]);
        Assert.Equal(2, report.ByWeekday[(int)DayOfWeek.Monday]);
        Assert.Equal(["2026-03", "2026-06"], report.Timeline.Select(t => t.Month));
        Assert.Equal(2, report.Timeline[1].AllMessages);
        Assert.Equal(1, report.Timeline[1].OwnerMessages);
        Assert.Equal(["2026 Q1", "2026 Q2"], report.Periods.Select(p => p.Period));
        Assert.Equal(1.0, report.Periods[1].Register.Haeyo);
        Assert.Equal(2, report.ActiveDays);
        Assert.Equal(2, report.BusyDays[0].Messages);
    }

    [Fact]
    public void SystemMessagesAreNotCountedAsConversation()
    {
        var messages = Build((0, Me, "안녕"));
        messages.Add(new ChatMessage(new MessageId("m0000099"), T0.AddMinutes(1), null, MessageKind.System, "합성 시스템 알림", 99));

        var report = Analyze(messages);

        Assert.Equal(1, report.TotalMessages);
    }

    [Fact]
    public void LengthPercentilesAndEmptyChat()
    {
        var messages = Build([.. Enumerable.Range(1, 10).Select(i => ((double)i, Me, new string('가', i)))]);

        var report = Analyze(messages);
        var empty = CommunicationAnalyzer.Analyze(Me, People, [], []);

        Assert.Equal(6, report.Length.P50);
        Assert.Equal(9, report.Length.P90);
        Assert.Equal(5.5, report.Length.Mean);
        Assert.Equal(0, empty.TotalMessages);
        Assert.Null(empty.FirstMessage);
        Assert.Null(empty.MedianResponse);
    }

    [Fact]
    public void ReportToStringHasNoNamesOrText()
    {
        var report = Analyze(Build((0, Friend, "합성비밀문장"), (1, Me, "합성비밀답장")));

        var text = string.Join("|", new object[] { report, report.Relationships[0], report.Keywords[0], report.Periods[0] });

        Assert.DoesNotContain("합성비밀", text, StringComparison.Ordinal);
        Assert.DoesNotContain("UserB", text, StringComparison.Ordinal);
    }

    private static CommunicationReport Analyze(List<ChatMessage> messages)
    {
        Session[] sessions = [new(new SessionId(1), [.. messages.Select(m => m.Id)], messages.Min(m => m.Timestamp), messages.Max(m => m.Timestamp))];
        return CommunicationAnalyzer.Analyze(Me, People, messages, sessions);
    }

    private static List<ChatMessage> Build(params (double Minutes, ParticipantId Speaker, string Text)[] lines) =>
        [.. lines.Select((l, i) => new ChatMessage(
            new MessageId(string.Create(CultureInfo.InvariantCulture, $"m{i + 1:D7}")),
            T0.AddMinutes(l.Minutes),
            l.Speaker,
            MessageKind.Text,
            l.Text,
            i + 1))];
}
