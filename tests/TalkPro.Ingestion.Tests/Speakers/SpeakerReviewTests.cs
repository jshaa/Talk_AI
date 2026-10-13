using System.Globalization;
using TalkPro.Core.Model;
using TalkPro.Core.Parsing;
using TalkPro.Ingestion.Speakers;
using TalkPro.Ingestion.Tests.GoldenTests;

namespace TalkPro.Ingestion.Tests.Speakers;

public sealed class SpeakerReviewTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 9, 0, 0, TimeSpan.FromHours(9));

    // ---- start state ----------------------------------------------------------------------

    [Fact]
    public void StartsWithoutOwnerAttestationOrExclusions()
    {
        var review = SpeakerReview.Start(Chat(("UserA", 0), ("User C", 1), ("UserA", 2)));

        Assert.Null(review.OwnerId);
        Assert.False(review.AttestationConfirmed);
        Assert.All(review.Participants, p => Assert.Equal(ParticipantConsent.ContextOnly, p.Consent));
        Assert.Equal([2, 1], review.Participants.Select(p => p.MessageCount));
        Assert.False(review.CanComplete);
    }

    [Fact]
    public void OwnerIsNeverSelectedAutomaticallyEvenForASingleSpeaker()
    {
        var review = SpeakerReview.Start(Chat(("UserA", 0), ("UserA", 1)));

        Assert.Null(review.OwnerId);
        Assert.Contains(CompletionError.OwnerNotSelected, review.Complete().Errors);
    }

    [Fact]
    public void CompletionRequiresAttestationAndOwner()
    {
        var review = SpeakerReview.Start(Chat(("UserA", 0), ("User C", 1)));

        var completion = review.Complete();

        Assert.False(completion.Succeeded);
        Assert.Null(completion.Chat);
        Assert.Equal([CompletionError.AttestationNotConfirmed, CompletionError.OwnerNotSelected], completion.Errors);
        Assert.Contains("직접 내보낸 데이터", ImportAttestation.Text, StringComparison.Ordinal);
    }

    // ---- owner -------------------------------------------------------------------------------

    [Fact]
    public void SelectingAnOwnerReplacesThePreviousOne()
    {
        var review = SpeakerReview.Start(Chat(("UserA", 0), ("User C", 1)));

        var first = Ok(review.SelectOwner(P(1)));
        var second = Ok(first.SelectOwner(P(2)));

        Assert.Equal(P(2), second.OwnerId);
        Assert.Equal(ParticipantConsent.ContextOnly, second.Participants.Single(p => p.Id == P(1)).Consent);
        Assert.Equal(ParticipantConsent.Owner, second.Participants.Single(p => p.Id == P(2)).Consent);
        Assert.Single(second.Participants, p => p.Consent == ParticipantConsent.Owner);
    }

    [Fact]
    public void InvalidOwnerChoicesAreRejectedWithoutChangingTheReview()
    {
        var review = SpeakerReview.Start(Chat(("UserA", 0), ("User C", 1)));
        var excluded = Ok(review.SetExcluded(P(2), excluded: true));

        AssertRejected(review, review.SelectOwner(P(9)), ReviewError.UnknownParticipant);
        AssertRejected(excluded, excluded.SelectOwner(P(2)), ReviewError.ParticipantExcluded);
    }

    [Fact]
    public void OwnerWithoutMessagesIsRejected()
    {
        var review = SpeakerReview.Start(Chat(("UserA", 0), ("User C", 1)));
        var merged = Ok(review.Merge(P(1), P(2)));
        var split = Ok(SpeakerReview.Start(Chat(("UserA", 0), ("UserA", 1))).Split(P(1), [M(1)]));

        // A participant created by a split always has messages; build the zero-message case via the chat itself.
        var chat = Chat(("UserA", 0));
        var withSilent = chat with { Participants = [.. chat.Participants, new Participant(P(2), "SilentSynthUser", ParticipantConsent.ContextOnly)] };
        var silent = SpeakerReview.Start(withSilent);

        AssertRejected(silent, silent.SelectOwner(P(2)), ReviewError.OwnerHasNoMessages);
        Assert.Single(merged.Participants);
        Assert.Equal(2, split.Participants.Length);
    }

    [Fact]
    public void ClearOwnerReturnsToContextOnly()
    {
        var review = Ok(SpeakerReview.Start(Chat(("UserA", 0))).SelectOwner(P(1))).ClearOwner();

        Assert.Null(review.OwnerId);
        Assert.Equal(ParticipantConsent.ContextOnly, review.Participants[0].Consent);
    }

    // ---- merge (alias / nickname change) -----------------------------------------------------

    [Fact]
    public void MergeRemapsMessagesAndRecordsTheAlias()
    {
        var review = SpeakerReview.Start(Chat(("UserA", 0), ("SynthNick", 1), ("UserA", 2), ("SynthNick", 3)));

        var merged = Ok(review.Merge(P(1), P(2)));

        var participant = Assert.Single(merged.Participants);
        Assert.Equal("UserA", participant.DisplayName);
        Assert.Equal(["SynthNick"], participant.Aliases);
        Assert.Equal(4, participant.MessageCount);
        Assert.Equal([M(0), M(1), M(2), M(3)], merged.MessagesOf(P(1)));
    }

    [Fact]
    public void MergingIntoOrFromTheOwnerKeepsTheOwner()
    {
        var review = Ok(SpeakerReview.Start(Chat(("UserA", 0), ("SynthNick", 1))).SelectOwner(P(2)));

        var merged = Ok(review.Merge(P(1), P(2)));

        Assert.Equal(P(1), merged.OwnerId);
        Assert.Equal(ParticipantConsent.Owner, merged.Participants.Single().Consent);
    }

    [Fact]
    public void InvalidMergesAreRejected()
    {
        var review = SpeakerReview.Start(Chat(("UserA", 0), ("User C", 1)));

        AssertRejected(review, review.Merge(P(1), P(1)), ReviewError.SameParticipant);
        AssertRejected(review, review.Merge(P(1), P(7)), ReviewError.UnknownParticipant);
    }

    // ---- split (same name, different people) --------------------------------------------------

    [Fact]
    public void SplitMovesSelectedMessagesToANewParticipantWithTheSameName()
    {
        var review = SpeakerReview.Start(Chat(("UserA", 0), ("User C", 1), ("UserA", 2), ("UserA", 3)));

        var split = Ok(review.Split(P(1), [M(2), M(3)]));

        var created = split.Participants.Single(p => p.Id == P(3));
        Assert.Equal("UserA", created.DisplayName);
        Assert.Equal(ParticipantConsent.ContextOnly, created.Consent);
        Assert.Equal([M(2), M(3)], split.MessagesOf(P(3)));
        Assert.Equal([M(0)], split.MessagesOf(P(1)));
    }

    [Fact]
    public void SplitFromATimestampMovesLaterMessages()
    {
        var review = SpeakerReview.Start(Chat(("UserA", 0), ("UserA", 10), ("UserA", 400)));

        var split = Ok(review.SplitFrom(P(1), T0.AddDays(300)));

        Assert.Equal([M(2)], split.MessagesOf(P(2)));
    }

    [Fact]
    public void InvalidSplitsAreRejected()
    {
        var review = SpeakerReview.Start(Chat(("UserA", 0), ("User C", 1), ("UserA", 2)));

        AssertRejected(review, review.Split(P(1), []), ReviewError.NoMessagesSelected);
        AssertRejected(review, review.Split(P(1), [M(1)]), ReviewError.MessageNotFromParticipant);
        AssertRejected(review, review.Split(P(1), [M(0), M(2)]), ReviewError.SplitWouldLeaveNoMessages);
        AssertRejected(review, review.Split(P(5), [M(0)]), ReviewError.UnknownParticipant);
    }

    // ---- exclusion ---------------------------------------------------------------------------

    [Fact]
    public void ExcludedSpeakersAreDroppedButSystemLinesAreKept()
    {
        var chat = Chat(("UserA", 0), ("User C", 1), (null, 2), ("User C", 3));
        var review = Ok(Ok(SpeakerReview.Start(chat).SelectOwner(P(1))).SetExcluded(P(2), excluded: true)).ConfirmAttestation();

        var completed = review.Complete().Chat!;

        Assert.Equal([M(0), M(2)], completed.Messages.Select(m => m.Id));
        Assert.Equal(2, completed.Summary.DroppedMessages);
        Assert.Equal(1, completed.Summary.Excluded);
        Assert.Equal(ParticipantConsent.Excluded, completed.Participants.Single(p => p.Id == P(2)).Consent);
    }

    [Fact]
    public void OwnerCannotBeExcluded()
    {
        var review = Ok(SpeakerReview.Start(Chat(("UserA", 0))).SelectOwner(P(1)));

        AssertRejected(review, review.SetExcluded(P(1), excluded: true), ReviewError.OwnerCannotBeExcluded);
    }

    [Fact]
    public void ExclusionCanBeUndone()
    {
        var review = Ok(Ok(SpeakerReview.Start(Chat(("UserA", 0), ("User C", 1))).SetExcluded(P(2), true)).SetExcluded(P(2), false));

        Assert.Equal(ParticipantConsent.ContextOnly, review.Participants.Single(p => p.Id == P(2)).Consent);
    }

    // ---- suspicions ---------------------------------------------------------------------------

    [Theory]
    [InlineData("User B", "userb", SuspicionReason.NormalizedNameMatch)]
    [InlineData("SynthUser", "SynthUser2", SuspicionReason.NameContainment)]
    [InlineData("합성사용자", "합성사용자 C", SuspicionReason.NameContainment)]
    public void SimilarNamesAreFlaggedAsPossibleAliases(string first, string second, SuspicionReason reason)
    {
        var review = SpeakerReview.Start(Chat((first, 0), (second, 1)));

        var suspicion = Assert.Single(review.Suspicions);
        Assert.Equal(SuspicionKind.PossibleAlias, suspicion.Kind);
        Assert.Equal(reason, suspicion.Reason);
        Assert.Equal([P(1), P(2)], suspicion.Participants);
        Assert.Contains(CompletionError.OpenSuspicions, review.Validate());
    }

    [Fact]
    public void DistinctNamesAreNotFlagged()
    {
        Assert.Empty(SpeakerReview.Start(Chat(("UserA", 0), ("User C", 1))).Suspicions);
    }

    [Fact]
    public void LongSilenceIsFlaggedAsPossibleNameReuse()
    {
        var review = SpeakerReview.Start(Chat(("UserA", 0), ("UserA", 200)));

        var suspicion = Assert.Single(review.Suspicions);
        Assert.Equal(SuspicionKind.PossibleNameReuse, suspicion.Kind);
        Assert.Equal(SuspicionReason.LongInactivityGap, suspicion.Reason);
    }

    [Fact]
    public void ReturningAfterALeaveEventIsFlagged()
    {
        var chat = Chat(("UserA", 0), ("User C", 1), (null, 2), ("User C", 3));
        chat = chat with { Messages = [.. chat.Messages.Select(m => m.Kind == MessageKind.System ? m with { Text = "User C 님이 나갔습니다.", SystemEvent = SystemEventType.Leave } : m)] };

        var review = SpeakerReview.Start(chat);

        var suspicion = Assert.Single(review.Suspicions);
        Assert.Equal(SuspicionReason.LeftAndReturned, suspicion.Reason);
        Assert.Equal([P(2)], suspicion.Participants);
    }

    [Fact]
    public void MergingResolvesTheAliasSuspicion()
    {
        var review = SpeakerReview.Start(Chat(("User B", 0), ("userb", 1)));

        var merged = Ok(review.Merge(P(1), P(2)));

        Assert.Equal(SuspicionStatus.Resolved, Assert.Single(merged.Suspicions).Status);
        Assert.DoesNotContain(CompletionError.OpenSuspicions, merged.Validate());
    }

    [Fact]
    public void SplittingResolvesTheNameReuseSuspicion()
    {
        var review = SpeakerReview.Start(Chat(("UserA", 0), ("UserA", 200)));

        var split = Ok(review.SplitFrom(P(1), T0.AddDays(100)));

        Assert.Equal(SuspicionStatus.Resolved, Assert.Single(split.Suspicions).Status);
    }

    [Fact]
    public void AcknowledgingUnblocksCompletion()
    {
        var review = SpeakerReview.Start(Chat(("User B", 0), ("userb", 1)));
        var id = review.Suspicions[0].Id;

        var acknowledged = Ok(Ok(review.Acknowledge(id)).SelectOwner(P(1))).ConfirmAttestation();
        var completion = acknowledged.Complete();

        Assert.True(completion.Succeeded);
        Assert.Equal(1, completion.Chat!.Summary.AcknowledgedSuspicions);
        AssertRejected(acknowledged, acknowledged.Acknowledge(id), ReviewError.SuspicionNotOpen);
        AssertRejected(acknowledged, acknowledged.Acknowledge(999), ReviewError.UnknownSuspicion);
    }

    // ---- completion & invariants -------------------------------------------------------------

    [Fact]
    public void CompletionProducesExactlyOneOwnerAndRemappedMessages()
    {
        var review = Ok(Ok(SpeakerReview.Start(Chat(("UserA", 0), ("SynthNick", 1), ("User C", 2))).Merge(P(1), P(2))).SelectOwner(P(1)))
            .ConfirmAttestation();

        var chat = review.Complete().Chat!;

        Assert.Equal(P(1), chat.OwnerId);
        Assert.Single(chat.Participants, p => p.Consent == ParticipantConsent.Owner);
        Assert.Equal([P(1), P(1), P(3)], chat.Messages.Select(m => m.SpeakerId!.Value));
        Assert.Equal(1, chat.Summary.Merges);
        Assert.DoesNotContain(chat.Participants, p => p.Consent == ParticipantConsent.Consented);
    }

    [Fact]
    public void WithdrawingTheAttestationBlocksCompletionAgain()
    {
        var review = Ok(SpeakerReview.Start(Chat(("UserA", 0))).SelectOwner(P(1))).ConfirmAttestation().WithdrawAttestation();

        Assert.Equal([CompletionError.AttestationNotConfirmed], review.Validate());
    }

    [Fact]
    public void OperationsNeverMutateTheOriginalReview()
    {
        var original = SpeakerReview.Start(Chat(("UserA", 0), ("SynthNick", 1), ("UserA", 2)));

        _ = original.SelectOwner(P(1));
        _ = original.Merge(P(1), P(2));
        _ = original.Split(P(1), [M(2)]);
        _ = original.SetExcluded(P(2), true);
        _ = original.ConfirmAttestation();

        Assert.Null(original.OwnerId);
        Assert.False(original.AttestationConfirmed);
        Assert.Equal(2, original.Participants.Length);
        Assert.All(original.Participants, p => Assert.Equal(ParticipantConsent.ContextOnly, p.Consent));
    }

    [Fact]
    public void ToStringNeverExposesNamesOrText()
    {
        var review = Ok(Ok(SpeakerReview.Start(Chat(("SyntheticSecretName", 0), ("SyntheticSecretNameX", 1))).Merge(P(1), P(2))).SelectOwner(P(1)))
            .ConfirmAttestation();
        var completion = review.Complete();

        object[] values = [review, review.Participants[0], review.Suspicions[0], new ReviewStep(review, ReviewError.None), completion, completion.Chat!, completion.Chat!.Summary];

        Assert.All(values, v => Assert.DoesNotContain("Secret", v.ToString()!, StringComparison.Ordinal));
    }

    [Fact]
    public void GoldenConversationCompletesAfterOwnerAndAttestation()
    {
        var parsed = GoldenData.Parse("a1/conversation.utf8.txt").Chat!;
        var review = SpeakerReview.Start(parsed);
        Assert.Empty(review.Suspicions);

        var owner = review.Participants.Single(p => p.DisplayName == "UserA").Id;
        var completed = Ok(review.SelectOwner(owner)).ConfirmAttestation().Complete().Chat!;

        Assert.Equal(owner, completed.OwnerId);
        Assert.Equal(parsed.Messages.Count, completed.Messages.Count);
        Assert.Equal(parsed.Messages.Select(m => m.SpeakerId), completed.Messages.Select(m => m.SpeakerId));
    }

    // ---- helpers -----------------------------------------------------------------------------

    private static ParticipantId P(int n) => new(string.Create(CultureInfo.InvariantCulture, $"p_{n:D2}"));

    private static MessageId M(int n) => new(string.Create(CultureInfo.InvariantCulture, $"m{n:D7}"));

    private static SpeakerReview Ok(ReviewStep step)
    {
        Assert.Equal(ReviewError.None, step.Error);
        return step.Review;
    }

    private static void AssertRejected(SpeakerReview before, ReviewStep step, ReviewError expected)
    {
        Assert.Equal(expected, step.Error);
        Assert.False(step.Succeeded);
        Assert.Same(before, step.Review);
    }

    /// <summary>Synthetic chat: (speaker or null for a system line, day offset). Speakers get p_NN in order of first appearance.</summary>
    private static ParsedChat Chat(params (string? Speaker, int Day)[] lines)
    {
        var ids = new Dictionary<string, ParticipantId>(StringComparer.Ordinal);
        var participants = new List<Participant>();
        var messages = new List<ChatMessage>();
        for (var i = 0; i < lines.Length; i++)
        {
            var (speaker, day) = lines[i];
            ParticipantId? speakerId = null;
            if (speaker is not null)
            {
                if (!ids.TryGetValue(speaker, out var id))
                {
                    id = P(ids.Count + 1);
                    ids.Add(speaker, id);
                    participants.Add(new Participant(id, speaker, ParticipantConsent.ContextOnly));
                }

                speakerId = id;
            }

            var kind = speaker is null ? MessageKind.System : MessageKind.Text;
            messages.Add(new ChatMessage(M(i), T0.AddDays(day), speakerId, kind, $"합성 테스트 메시지 {i:D3}", i + 1)
            {
                SystemEvent = kind == MessageKind.System ? SystemEventType.Other : SystemEventType.None,
            });
        }

        return new ParsedChat(new ChatFormatId("synthetic.windows.v1"), SourceEncoding.Utf8, participants, messages, [], new ParseStatistics(lines.Length, lines.Length, 0, 0, 0, 0, 0, 0));
    }
}
