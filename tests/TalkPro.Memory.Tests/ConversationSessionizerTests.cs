using TalkPro.Core.Model;

namespace TalkPro.Memory.Tests;

/// <summary>
/// Default options and fewer than 20 gaps, so the adaptive threshold is the 30-minute default.
/// Split score at a candidate = 2·min(gap/T, 3)/3 − 0.8·continuation + 0.5·greeting − 0.6·(previous is a question).
/// </summary>
public sealed class ConversationSessionizerTests
{
    private readonly ConversationSessionizer _sessionizer = new();

    [Fact]
    public void EmptyChatHasNoSessions() => Assert.Empty(_sessionizer.Split([]));

    [Fact]
    public void ShortGapsStayTogether() => Assert.Equal([3], Chat.Sizes(_sessionizer.Split(Chat.At(0, 5, 10))));

    [Fact]
    public void HardSplitWinsOverContinuationCue()
    {
        var messages = Chat.Build((0, "p_01", "합성 문장"), (360, "p_02", "근데 아까 그거"));

        Assert.Equal([1, 1], Chat.Sizes(_sessionizer.Split(messages)));
    }

    [Fact]
    public void LateNightTalkAcrossMidnightIsOneSession()
    {
        // T0 is 22:00; messages every 5 minutes until 02:00.
        var messages = Chat.At([.. Enumerable.Range(0, 49).Select(i => i * 5.0)]);

        var session = Assert.Single(_sessionizer.Split(messages));
        Assert.NotEqual(session.Start.Date, session.End.Date);
    }

    [Fact]
    public void GapBelowAdaptiveThresholdIsNotACandidateEvenWithGreeting()
    {
        var messages = Chat.Build((0, "p_01", "합성 문장"), (20, "p_02", "안녕 잘 지냈어"));

        Assert.Single(_sessionizer.Split(messages));
    }

    [Fact]
    public void NeutralTextAfterALongGapStartsANewSession()
    {
        // 50 min: 2·(50/30)/3 = 1.11 ≥ 1.0.
        var messages = Chat.Build((0, "p_01", "합성 문장"), (50, "p_02", "점심 메뉴 정했어"));

        Assert.Equal([1, 1], Chat.Sizes(_sessionizer.Split(messages)));
    }

    [Fact]
    public void ContinuationCueKeepsTheSession()
    {
        // 1.11 − 0.8 = 0.31.
        var messages = Chat.Build((0, "p_01", "합성 문장"), (50, "p_02", "근데 아까 그거 말이야"));

        Assert.Single(_sessionizer.Split(messages));
    }

    [Fact]
    public void AnswerToAnOpenQuestionKeepsTheSession()
    {
        // 1.11 − 0.6 = 0.51.
        var messages = Chat.Build((0, "p_01", "내일 올래?"), (50, "p_02", "점심 메뉴 정했어"));

        Assert.Single(_sessionizer.Split(messages));
    }

    [Theory]
    [InlineData("점심 메뉴 정했어", 1)]
    [InlineData("안녕 잘 지냈어", 2)]
    public void GreetingTipsAMediumGapIntoASplit(string text, int expectedSessions)
    {
        // 35 min: 0.78 alone, 1.28 with a greeting.
        var messages = Chat.Build((0, "p_01", "합성 문장"), (35, "p_02", text));

        Assert.Equal(expectedSessions, _sessionizer.Split(messages).Count);
    }

    [Fact]
    public void ThresholdIsLearnedFromTheChatsOwnRhythm()
    {
        // A chat where everyone answers about once an hour: 25 gaps of 60 minutes.
        var messages = Chat.At([.. Enumerable.Range(0, 26).Select(i => i * 60.0)]);
        var fixedThreshold = new ConversationSessionizer(new SessionizerOptions { MinGapsToLearn = int.MaxValue });

        Assert.Equal(TimeSpan.FromMinutes(60), _sessionizer.LearnThreshold(messages));
        Assert.Single(_sessionizer.Split(messages));
        Assert.Equal(26, fixedThreshold.Split(messages).Count);
    }

    [Theory]
    [InlineData(1.0, 30, 20.0)]
    [InlineData(180.0, 30, 120.0)]
    [InlineData(60.0, 10, 30.0)]
    public void LearnedThresholdIsClampedAndNeedsEnoughGaps(double gapMinutes, int gaps, double expectedMinutes)
    {
        var messages = Chat.At([.. Enumerable.Range(0, gaps + 1).Select(i => i * gapMinutes)]);

        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), _sessionizer.LearnThreshold(messages));
    }

    [Fact]
    public void OutOfOrderTimestampsKeepFileOrderAndDoNotSplit()
    {
        var messages = Chat.At(0, 5, 3, 8);

        var session = Assert.Single(_sessionizer.Split(messages));
        Assert.Equal(messages.Select(m => m.Id), session.MessageIds);
        Assert.Equal(Chat.T0, session.Start);
        Assert.Equal(Chat.T0.AddMinutes(8), session.End);
    }

    [Fact]
    public void VeryLongSessionsAreCut()
    {
        var messages = Chat.At([.. Enumerable.Range(0, 1000).Select(i => (double)i)]);

        Assert.Equal([400, 400, 200], Chat.Sizes(_sessionizer.Split(messages)));
    }

    [Fact]
    public void EveryMessageBelongsToExactlyOneSessionInOrder()
    {
        // Deterministic mix of hard splits, candidate gaps and short gaps.
        var minutes = 0.0;
        var offsets = new List<double>();
        for (var i = 0; i < 500; i++)
        {
            minutes += (((i * 37) + 11) % 10) switch { 0 => 600, 1 => 90, 2 => 40, _ => (i * 13) % 8 };
            offsets.Add(minutes);
        }

        var messages = Chat.At([.. offsets]);
        var sessions = _sessionizer.Split(messages);

        Assert.Equal(messages.Select(m => m.Id), sessions.SelectMany(s => s.MessageIds));
        Assert.Equal(Enumerable.Range(1, sessions.Count), sessions.Select(s => s.Id.Value));
        Assert.All(sessions, s => Assert.True(s.Start <= s.End));
        Assert.True(sessions.Count > 10);
    }

    [Theory]
    [InlineData(0.0, 2)]
    [InlineData(1.0, 1)]
    public void SemanticSimilarityAdjustsOnlyCandidateBoundaries(double similarity, int expectedSessions)
    {
        var fake = new FakeSimilarity(similarity);
        var sessionizer = new ConversationSessionizer(similarity: fake);
        var messages = Chat.At(0, 5, 10, 45);

        Assert.Equal(expectedSessions, sessionizer.Split(messages).Count);
        var call = Assert.Single(fake.Calls);
        Assert.Equal(messages.Take(3).Select(m => m.Id), call.Before);
        Assert.Equal([messages[3].Id], call.After);
    }

    [Fact]
    public void InconsistentOptionsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConversationSessionizer(new SessionizerOptions { MaxMessagesPerSession = 0 }));
        Assert.Throws<ArgumentException>(() => new ConversationSessionizer(new SessionizerOptions { MinAdaptive = TimeSpan.FromHours(3) }));
    }

    [Fact]
    public void SessionToStringHasNoContent()
    {
        var session = Assert.Single(_sessionizer.Split(Chat.Build((0, "p_01", "합성 비밀 문장"))));

        Assert.Equal("Session { Id = s1, Messages = 1 }", session.ToString());
    }

    private sealed class FakeSimilarity(double value) : ISemanticSimilarity
    {
        public List<(IReadOnlyList<MessageId> Before, IReadOnlyList<MessageId> After)> Calls { get; } = [];

        public double Similarity(IReadOnlyList<ChatMessage> before, IReadOnlyList<ChatMessage> after)
        {
            Calls.Add(([.. before.Select(m => m.Id)], [.. after.Select(m => m.Id)]));
            return value;
        }
    }
}

public sealed class DiscourseCuesTests
{
    [Theory]
    [InlineData("내일 올래?", true)]
    [InlineData("그럴까", true)]
    [InlineData("괜찮나요", true)]
    [InlineData("이건 어때 ㅋㅋ", true)]
    [InlineData("밥 먹었냐", true)]
    [InlineData("고마워", false)]
    [InlineData("알았어.", false)]
    public void Questions(string text, bool expected) => Assert.Equal(expected, DiscourseCues.IsQuestion(text));

    [Theory]
    [InlineData("근데 아까 그거", 1)]
    [InlineData("응", 1)]
    [InlineData("응응 알겠어", 1)]
    [InlineData("네 알겠습니다", 1)]
    [InlineData("네가 말한 거", 0)]
    [InlineData("어제 본 영화", 0)]
    public void Continuations(string text, double expected) => Assert.Equal(expected, DiscourseCues.ContinuationScore(text));

    [Theory]
    [InlineData("안녕 잘 지냈어", 1)]
    [InlineData("오랜만이야", 1)]
    [InlineData("Hi there", 1)]
    [InlineData("history", 0)]
    [InlineData("점심 메뉴", 0)]
    public void Greetings(string text, double expected) => Assert.Equal(expected, DiscourseCues.GreetingScore(text));
}
