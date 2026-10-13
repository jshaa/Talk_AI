using TalkPro.Core.Model;

namespace TalkPro.Ingestion.Tests.GoldenTests;

/// <summary>Logically identical samples must produce identical Core models regardless of encoding, line endings or format.</summary>
public sealed class EquivalenceTests
{
    [Theory]
    [InlineData("w1", "conversation")]
    [InlineData("a1", "conversation")]
    [InlineData("i1", "conversation")]
    [InlineData("w1", "single-message")]
    [InlineData("a1", "header-only")]
    [InlineData("i1", "single-message")]
    public void EncodingDoesNotChangeTheLogicalResult(string folder, string sample)
    {
        var utf8 = Chat($"{folder}/{sample}.utf8.txt");
        var bom = Chat($"{folder}/{sample}.utf8bom.txt");
        var cp949 = Chat($"{folder}/{sample}.cp949.txt");

        Assert.Equal(SourceEncoding.Utf8, utf8.Encoding);
        Assert.Equal(SourceEncoding.Utf8WithBom, bom.Encoding);
        Assert.Equal(SourceEncoding.Cp949, cp949.Encoding);
        AssertSameLogicalChat(utf8, bom);
        AssertSameLogicalChat(utf8, cp949);
    }

    [Theory]
    [InlineData("w1")]
    [InlineData("a1")]
    [InlineData("i1")]
    public void LineEndingsAndFinalNewlineDoNotChangeTheLogicalResult(string folder)
    {
        AssertSameLogicalChat(Chat($"{folder}/conversation.utf8.txt"), Chat($"{folder}/conversation-lf-nofinal.utf8.txt"));
    }

    [Fact]
    public void AllThreeFormatsProduceTheSameConversation()
    {
        var windows = Chat("w1/conversation.utf8.txt");
        var android = Chat("a1/conversation.utf8.txt");
        var ios = Chat("i1/conversation.utf8.txt");

        // Line numbers differ (W1 has date-marker lines); everything else is identical.
        Assert.Equal(Logical(windows), Logical(android));
        Assert.Equal(Logical(windows), Logical(ios));
        Assert.Equal(windows.Participants, android.Participants);
        Assert.Equal(windows.Participants, ios.Participants);
    }

    [Fact]
    public void ParsingIsDeterministic()
    {
        var first = Chat("a1/robustness.utf8.txt");
        var second = Chat("a1/robustness.utf8.txt");

        Assert.Equal(first.Messages, second.Messages);
        Assert.Equal(first.Issues, second.Issues);
        Assert.Equal(first.Statistics, second.Statistics);
    }

    private static void AssertSameLogicalChat(ParsedChat expected, ParsedChat actual)
    {
        Assert.Equal(expected.FormatId, actual.FormatId);
        Assert.Equal(expected.Participants, actual.Participants);
        Assert.Equal(expected.Messages, actual.Messages);
        Assert.Equal(expected.Issues, actual.Issues);
        Assert.Equal(expected.Statistics, actual.Statistics);
    }

    private static List<(DateTimeOffset, ParticipantId?, MessageKind, SystemEventType, string)> Logical(ParsedChat chat) =>
        [.. chat.Messages.Select(m => (m.Timestamp, m.SpeakerId, m.Kind, m.SystemEvent, m.Text))];

    private static ParsedChat Chat(string path) => GoldenData.Parse(path).Chat ?? throw new InvalidOperationException(path);
}
