using TalkPro.Core.Model;

namespace TalkPro.Privacy.Tests;

public sealed class PurposeViewTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 9, 0, 0, TimeSpan.FromHours(9));

    private static readonly ChatMessage[] Messages =
    [
        Message("m0000001", "가나다 만나서 010-1234-5678로 연락함"),
        Message("m0000002", "합성 제외 표시만 있는 메시지"),
        Message("m0000003", "합성 평범한 메시지"),
    ];

    private static readonly (PiiFinding, PrivacyAction)[] Findings =
    [
        (new PiiFinding(new FindingId(1), new MessageId("m0000001"), 0, 3, PiiType.Person, 0.9, DetectionLayer.L2Dictionary), PrivacyAction.Mask),
        (new PiiFinding(new FindingId(2), new MessageId("m0000001"), 8, 13, PiiType.Phone, 1.0, DetectionLayer.L1Pattern), PrivacyAction.Mask),
    ];

    [Fact]
    public void AnalysisViewDropsFlaggedMessagesAndMasksDecidedSpans()
    {
        var view = PurposeRedactor.RenderAll(Messages, Findings, new HashSet<MessageId> { new("m0000002") }, RedactionPurpose.Analysis);

        Assert.Equal(["m0000001", "m0000003"], view.Select(m => m.Id.Value));
        Assert.Equal("[인물1] 만나서 [전화번호]로 연락함", view[0].Text);
        Assert.Equal(Messages[0].Timestamp, view[0].Timestamp);
    }

    [Fact]
    public void DisplayViewKeepsFlaggedMessagesAndUnmaskedText()
    {
        var view = PurposeRedactor.RenderAll(Messages, Findings, new HashSet<MessageId> { new("m0000002") }, RedactionPurpose.Display);

        Assert.Equal(3, view.Count);
        Assert.Equal(Messages[0].Text, view[0].Text);
    }

    [Fact]
    public void CloudTransferIsRefusedEvenForAnEmptyChat() =>
        Assert.Throws<InvalidOperationException>(() => PurposeRedactor.RenderAll([], [], new HashSet<MessageId>(), RedactionPurpose.CloudTransfer));

    private static ChatMessage Message(string id, string text) => new(new MessageId(id), T0, new ParticipantId("p_01"), MessageKind.Text, text, 1);
}
