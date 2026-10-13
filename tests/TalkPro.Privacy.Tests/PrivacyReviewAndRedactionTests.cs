using System.Globalization;
using TalkPro.Core.Model;
using TalkPro.Core.Parsing;
using TalkPro.Privacy;

namespace TalkPro.Privacy.Tests;

/// <summary>Phase 3.2: L4 decisions and purpose-specific views. Raw messages are never changed.</summary>
public sealed class PrivacyReviewAndRedactionTests
{
    private static readonly ParticipantId Owner = new("p_01");

    [Fact]
    public void DefaultsAreConservative()
    {
        var (_, review) = Reviewed();

        Assert.Equal(PrivacyAction.Mask, ActionOf(review, PiiType.ResidentId));
        Assert.Equal(PrivacyAction.Mask, ActionOf(review, PiiType.Phone));
        Assert.Equal(PrivacyAction.Mask, ActionOf(review, PiiType.Person));
        Assert.Equal(PrivacyAction.Exclude, ActionOf(review, PiiType.Health));
    }

    [Fact]
    public void HighRiskIdentifiersCannotBeKeptAndTopicsCannotBeMasked()
    {
        var (_, review) = Reviewed();
        var resident = review.Findings.First(f => f.Type == PiiType.ResidentId);
        var health = review.Findings.First(f => f.Type == PiiType.Health);

        Assert.Equal(PrivacyReviewError.ActionNotAllowed, review.SetAction(resident.Id, PrivacyAction.Keep).Error);
        Assert.Equal(PrivacyReviewError.ActionNotAllowed, review.SetAction(health.Id, PrivacyAction.Mask).Error);
        Assert.Equal(PrivacyReviewError.ActionNotAllowed, review.SetActionForType(PiiType.ResidentId, PrivacyAction.Keep).Error);
        Assert.Equal(PrivacyReviewError.UnknownFinding, review.SetAction(new FindingId(999), PrivacyAction.Keep).Error);
    }

    [Fact]
    public void BulkDecisionAppliesToEveryFindingOfTheType()
    {
        var (_, review) = Reviewed();

        var updated = review.SetActionForType(PiiType.Person, PrivacyAction.Keep).Review;

        Assert.All(updated.Findings.Where(f => f.Type == PiiType.Person), f => Assert.Equal(PrivacyAction.Keep, updated.ActionFor(f.Id)));
        Assert.Equal(PrivacyAction.Mask, review.ActionFor(review.Findings.First(f => f.Type == PiiType.Person).Id));
    }

    [Fact]
    public void DeleteWinsOverExcludeForTheSameMessage()
    {
        var (_, review) = Reviewed();
        var health = review.Findings.First(f => f.Type == PiiType.Health);
        var sameMessage = review.Findings.First(f => f.MessageId == health.MessageId && f.Id != health.Id);

        var updated = review.SetAction(sameMessage.Id, PrivacyAction.Delete).Review;

        Assert.Contains(health.MessageId, updated.DeletedMessages);
        Assert.DoesNotContain(health.MessageId, updated.ExcludedMessages);
        Assert.Equal(1, updated.Summary().DeletedMessages);
    }

    [Fact]
    public void ViewsDifferByPurpose()
    {
        var (chat, review) = Reviewed();
        var message = chat.Messages[0]; // "민수야 내 번호 010-1234-5678, 주민번호 …"
        var findings = review.Findings.Where(f => f.MessageId == message.Id).ToList();
        var map = new PseudonymMap();

        var display = PurposeRedactor.Render(message, findings, review.ActionFor, RedactionPurpose.Display, map)!;
        var search = PurposeRedactor.Render(message, findings, review.ActionFor, RedactionPurpose.LocalSearch, map)!;
        var llm = PurposeRedactor.Render(message, findings, review.ActionFor, RedactionPurpose.LlmPrompt, map)!;

        Assert.Contains("[주민등록번호]", display, StringComparison.Ordinal);
        Assert.Contains("010-1234-5678", display, StringComparison.Ordinal);
        Assert.Contains("민수", search, StringComparison.Ordinal);
        Assert.Contains("[주민등록번호]", search, StringComparison.Ordinal);
        Assert.Equal("[인물1]야 내 번호 [전화번호], 주민번호 [주민등록번호]", llm);
        Assert.Contains(PrivacyDetectionTests.ValidResidentId(), message.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void KeptHighRiskIdentifierIsStillMaskedForTheLlm()
    {
        var (chat, review) = Reviewed();
        var message = chat.Messages[0];
        var keepAll = review.SetActionForType(PiiType.Phone, PrivacyAction.Keep).Review.SetActionForType(PiiType.Person, PrivacyAction.Keep).Review;

        var llm = PurposeRedactor.Render(message, [.. keepAll.Findings.Where(f => f.MessageId == message.Id)], keepAll.ActionFor, RedactionPurpose.LlmPrompt, new PseudonymMap())!;

        Assert.Contains("010-1234-5678", llm, StringComparison.Ordinal);
        Assert.Contains("[주민등록번호]", llm, StringComparison.Ordinal);
    }

    [Fact]
    public void ExcludedAndDeletedMessagesHaveNoAnalysisView()
    {
        var (chat, review) = Reviewed();
        var healthMessage = chat.Messages[1];
        var findings = review.Findings.Where(f => f.MessageId == healthMessage.Id).ToList();

        Assert.Null(PurposeRedactor.Render(healthMessage, findings, review.ActionFor, RedactionPurpose.Analysis, new PseudonymMap()));
        Assert.Null(PurposeRedactor.Render(healthMessage, findings, review.ActionFor, RedactionPurpose.LocalSearch, new PseudonymMap()));
        Assert.NotNull(PurposeRedactor.Render(healthMessage, findings, review.ActionFor, RedactionPurpose.Display, new PseudonymMap()));

        var deleted = review.SetAction(findings[0].Id, PrivacyAction.Delete).Review;
        Assert.Null(PurposeRedactor.Render(healthMessage, findings, deleted.ActionFor, RedactionPurpose.Display, new PseudonymMap()));
    }

    [Fact]
    public void CloudTransferIsAlwaysRefused()
    {
        var (chat, review) = Reviewed();

        Assert.Throws<InvalidOperationException>(() => PurposeRedactor.Render(chat.Messages[2], [], review.ActionFor, RedactionPurpose.CloudTransfer, new PseudonymMap()));
    }

    [Fact]
    public void PseudonymsAreStablePerName()
    {
        var map = new PseudonymMap();

        Assert.Equal("[인물1]", map.Person("김민수"));
        Assert.Equal("[인물2]", map.Person("User B"));
        Assert.Equal("[인물1]", map.Person("김민수"));
        Assert.Equal("[기관1]", map.Organization("서울대병원"));
    }

    [Fact]
    public void BuilderDropsDeletedMessagesAndIndexesOnlyIncludedOnes()
    {
        var (chat, review) = Reviewed();
        var phone = review.Findings.First(f => f.Type == PiiType.Phone);
        var withDelete = review.SetAction(phone.Id, PrivacyAction.Delete).Review;

        var import = PrivacyImportBuilder.Build(chat, withDelete);

        Assert.Equal(1, import.DeletedMessageCount);
        Assert.DoesNotContain(import.Chat.Messages, m => m.Id == phone.MessageId);
        Assert.DoesNotContain(import.Findings, f => f.MessageId == phone.MessageId);
        Assert.Equal([chat.Messages[1].Id], import.ExcludedMessages);
        Assert.Equal([chat.Messages[2].Id], import.SearchText.Keys);
        Assert.All(import.Decisions.Values, a => Assert.NotEqual(PrivacyAction.Delete, a));
    }

    [Fact]
    public void SummaryAndStepsNeverPrintText()
    {
        var (_, review) = Reviewed();

        Assert.DoesNotContain("민수", review.ToString() + review.Summary() + review.SetAction(new FindingId(1), PrivacyAction.Mask), StringComparison.Ordinal);
    }

    private static PrivacyAction ActionOf(PrivacyReview review, PiiType type) => review.ActionFor(review.Findings.First(f => f.Type == type).Id);

    private static (ReviewedChat Chat, PrivacyReview Review) Reviewed()
    {
        var t = new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.FromHours(9));
        var participants = new[] { new Participant(Owner, "김민수", ParticipantConsent.Owner) };
        ChatMessage[] messages =
        [
            new(new MessageId("m0000001"), t, Owner, MessageKind.Text, $"민수야 내 번호 010-1234-5678, 주민번호 {PrivacyDetectionTests.ValidResidentId()}", 1),
            new(new MessageId("m0000002"), t.AddMinutes(1), Owner, MessageKind.Text, "김민수 엄마가 항암 치료 받으셔", 2),
            new(new MessageId("m0000003"), t.AddMinutes(2), Owner, MessageKind.Text, "합성 테스트 메시지 003", 3),
        ];
        var chat = new ReviewedChat(new ChatFormatId("synthetic.windows.v1"), SourceEncoding.Utf8, Owner, participants, messages, new ReviewSummary(1, 0, 0, 0, 0, 0));
        var findings = PrivacyScanner.CreateDefault().Scan(messages, participants);
        return (chat, PrivacyReview.Start(findings, messages.Length));
    }
}
