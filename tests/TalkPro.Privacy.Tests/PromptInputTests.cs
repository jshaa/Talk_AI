using TalkPro.Core.Model;

namespace TalkPro.Privacy.Tests;

public sealed class PromptInputTests
{
    private static readonly Participant[] People =
    [
        new(new ParticipantId("p_01"), "UserA", ParticipantConsent.Owner),
        new(new ParticipantId("p_02"), "가나다", ParticipantConsent.ContextOnly),
    ];

    private readonly IPrivacyScanner _scanner = PrivacyScanner.CreateDefault();

    [Fact]
    public void TypedTextIsMaskedButSensitiveTopicsStay()
    {
        var map = new PseudonymMap();

        var redacted = PromptInput.Redact("가나다가 010-1234-5678로 연락 달래. 항암 치료 얘기도 했어", People, _scanner, map);

        Assert.Equal("[인물1]가 [전화번호]로 연락 달래. 항암 치료 얘기도 했어", redacted.Value);
    }

    [Fact]
    public void OneMapKeepsTokensConsistentAcrossExamplesAndInput()
    {
        var map = new PseudonymMap();
        var message = new ChatMessage(new MessageId("m0000001"), DateTimeOffset.UnixEpoch, new ParticipantId("p_01"), MessageKind.Text, "가나다 만났어", 1);
        (PiiFinding, PrivacyAction)[] findings =
            [(new PiiFinding(new FindingId(1), message.Id, 0, 3, PiiType.Person, 0.9, DetectionLayer.L2Dictionary), PrivacyAction.Mask)];

        var example = PurposeRedactor.RenderForLlm([message], findings, new HashSet<MessageId>(), map)[0].Text;
        var input = PromptInput.Redact("가나다 오늘 와?", People, _scanner, map);

        Assert.Equal("[인물1] 만났어", example.Value);
        Assert.Equal("[인물1] 오늘 와?", input.Value);
    }

    [Fact]
    public void RestorePutsNamesBackForLocalDisplayOnly()
    {
        var map = new PseudonymMap();
        PromptInput.Redact("가나다랑 서울대학교 갔어", People, _scanner, map);

        Assert.Equal("가나다한테 서울대학교 얘기 했어 [전화번호] [인물9]", map.Restore("[인물1]한테 [기관1] 얘기 했어 [전화번호] [인물9]"));
        Assert.DoesNotContain("가나다", map.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void JoinedLinesStayRedactedAndOneLineEach()
    {
        var map = new PseudonymMap();
        var first = PromptInput.Redact("가나다 왔어", People, _scanner, map);
        var second = PromptInput.Redact("합성\n두 줄", People, _scanner, map);

        var joined = PromptInput.Join([new Core.Llm.ContextLine("상대", first), new Core.Llm.ContextLine("나", second)]);

        Assert.Equal("상대: [인물1] 왔어\n나: 합성 두 줄", joined.Value);
    }

    [Fact]
    public void EmptyInputIsRejected() =>
        Assert.Throws<ArgumentException>(() => PromptInput.Redact("  ", People, _scanner, new PseudonymMap()));
}
