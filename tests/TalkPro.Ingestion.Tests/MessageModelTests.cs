using TalkPro.Core.Model;

namespace TalkPro.Ingestion.Tests;

public sealed class MessageModelTests
{
    [Fact]
    public void ToStringNeverContainsMessageText()
    {
        var message = new ChatMessage(
            new MessageId("m_0001"),
            new DateTimeOffset(2026, 10, 1, 15, 15, 0, TimeSpan.FromHours(9)),
            new ParticipantId("p_01"),
            MessageKind.Text,
            "우리 엄마 서울대병원에서 암 치료 중",
            SourceLine: 12);

        var text = message.ToString();

        Assert.DoesNotContain("엄마", text, StringComparison.Ordinal);
        Assert.Contains("m_0001", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void IdentifiersRejectBlankValues(string value)
    {
        Assert.Throws<ArgumentException>(() => new MessageId(value));
        Assert.Throws<ArgumentException>(() => new ParticipantId(value));
    }

    [Fact]
    public void MessageIdsCompareByValue()
    {
        Assert.Equal(new MessageId("m_1"), new MessageId("m_1"));
    }
}
