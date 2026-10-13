using TalkPro.Core.Llm;
using TalkPro.Core.Model;
using TalkPro.Persona.Analysis;
using TalkPro.Persona.Style;

namespace TalkPro.Persona.Tests;

public sealed class ReplyDirectionTests
{
    private static readonly StyleProfile Profile = StyleProfile.From(StyleProfileTests.Report(new RegisterMix(0, 0, 1, 0), laugh: 0.5));

    [Fact]
    public void DirectionsMapToWireNamesAndTheDefaultSpreadIsDistinct()
    {
        Assert.Equal(["accept", "hold", "ask_back"], Enum.GetValues<ReplyDirection>().Select(ReplyDirections.WireName));
        Assert.Equal([ReplyDirection.Accept, ReplyDirection.Hold, ReplyDirection.AskBack], ReplyDirections.Default);
        Assert.Equal(ReplyDirections.Default.Count, ReplyDirections.Default.Distinct().Count());
        Assert.All(Enum.GetValues<ReplyDirection>(), d => Assert.StartsWith(ReplyDirections.WireName(d) + "(", ReplyDirections.Rule(d), StringComparison.Ordinal));
    }

    [Fact]
    public void PromptListsDirectionRulesAndOrderButNoConversationText()
    {
        var request = ReplyPromptBuilder.Build(Profile, [(Text("합성비밀질문"), Text("합성비밀답장"))], Text("합성비밀받은말"), [ReplyDirection.Hold, ReplyDirection.AskBack]);
        var instruction = request.Instruction.ReplaceLineEndings("\n");

        Assert.Contains(ReplyDirections.Rule(ReplyDirection.Hold), instruction, StringComparison.Ordinal);
        Assert.Contains(ReplyDirections.Rule(ReplyDirection.AskBack), instruction, StringComparison.Ordinal);
        Assert.DoesNotContain(ReplyDirections.Rule(ReplyDirection.Accept), instruction, StringComparison.Ordinal);
        Assert.Contains("1. hold\n2. ask_back\n", instruction, StringComparison.Ordinal);
        Assert.DoesNotContain("합성비밀", request.Instruction + request.Task, StringComparison.Ordinal);
        Assert.Contains("2개", request.Task, StringComparison.Ordinal);
        Assert.Contains("\"enum\":[\"accept\",\"hold\",\"ask_back\"]", request.JsonSchema, StringComparison.Ordinal);
        Assert.Throws<ArgumentOutOfRangeException>(() => ReplyPromptBuilder.Build(Profile, [], Text("x"), []));
        Assert.Throws<ArgumentOutOfRangeException>(() => ReplyPromptBuilder.Build(Profile, [], Text("x"), [.. ReplyDirections.Default, ReplyDirection.Hold]));
    }

    [Fact]
    public void ModelOutputIsParsedStrictlyAndAssignedBySlotDirection()
    {
        Assert.True(StructuredOutput.TryParse<ReplyDraftOutput>(
            "{\"drafts\":[{\"direction\":\"ask_back\",\"text\":\"언제?\"},{\"direction\":\"accept\",\"text\":\"좋아\"},{\"direction\":\"accept\",\"text\":\"콜\"}]}",
            out var output));

        var slots = output.Assign([ReplyDirection.Accept, ReplyDirection.Hold, ReplyDirection.AskBack, ReplyDirection.Accept]);

        Assert.Equal(["좋아", null, "언제?", "콜"], slots.Select(s => s?.Text));
    }

    [Theory]
    [InlineData("{\"drafts\":[{\"direction\":\"Accept\",\"text\":\"a\"}]}")]
    [InlineData("{\"drafts\":[{\"direction\":\"ACCEPT\",\"text\":\"a\"}]}")]
    [InlineData("{\"drafts\":[{\"direction\":\"askBack\",\"text\":\"a\"}]}")]
    [InlineData("{\"drafts\":[{\"direction\":1,\"text\":\"a\"}]}")]
    [InlineData("{\"drafts\":[{\"direction\":null,\"text\":\"a\"}]}")]
    [InlineData("{\"drafts\":[{\"text\":\"a\"}]}")]
    [InlineData("{\"drafts\":[{\"direction\":\"accept\"}]}")]
    public void AnythingButExactDirectionNamesIsRejected(string json) =>
        Assert.False(StructuredOutput.TryParse<ReplyDraftOutput>(json, out _));

    [Theory]
    [InlineData(ReplyDirection.Accept, "좋아 ㅋㅋ 가자", true)]
    [InlineData(ReplyDirection.Accept, "음 글쎄", false)]
    [InlineData(ReplyDirection.Hold, "일단 생각해볼게", true)]
    [InlineData(ReplyDirection.Hold, "좋아", false)]
    [InlineData(ReplyDirection.AskBack, "몇 시에 볼까?", true)]
    [InlineData(ReplyDirection.AskBack, "알겠어", false)]
    public void DirectionCheckIsARoughHeuristic(ReplyDirection direction, string draft, bool expected) =>
        Assert.Equal(expected, DirectionCheck.Matches(direction, draft));

    [Theory]
    [InlineData("나: 좋아", "좋아")]
    [InlineData("  \"언제?\"  ", "언제?")]
    [InlineData("   ", null)]
    public void SingleDraftsAreCleaned(string raw, string? expected) => Assert.Equal(expected, DraftPostProcessor.CleanOne(raw));

    private static RedactedText Text(string value) =>
        TalkPro.Privacy.PurposeRedactor.RenderForLlm(
            [new ChatMessage(new MessageId("m0000001"), DateTimeOffset.UnixEpoch, new ParticipantId("p_01"), MessageKind.Text, value, 1)], [], new HashSet<MessageId>())[0].Text;
}
