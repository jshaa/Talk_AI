using System.Collections.Immutable;
using TalkPro.Core.Diagnostics;
using TalkPro.Core.Model;
using TalkPro.Core.Parsing;

namespace TalkPro.Ingestion.Speakers;

/// <summary>Statement the user must confirm before any analysis (report 3.7, master prompt §4).</summary>
public static class ImportAttestation
{
    public const string Text = "나는 이 대화의 참여자이며 내가 접근 권한을 가진 기기에서 직접 내보낸 데이터입니다.";
}

/// <summary>Why a review operation was rejected. The review is left unchanged.</summary>
public enum ReviewError
{
    None,
    UnknownParticipant,
    SameParticipant,
    ParticipantExcluded,
    OwnerCannotBeExcluded,
    OwnerHasNoMessages,
    NoMessagesSelected,
    MessageNotFromParticipant,
    SplitWouldLeaveNoMessages,
    UnknownSuspicion,
    SuspicionNotOpen,
}

/// <summary>Why the review cannot be completed yet.</summary>
public enum CompletionError
{
    AttestationNotConfirmed,
    OwnerNotSelected,
    OpenSuspicions,
}

public enum SuspicionKind
{
    /// <summary>Two speaker names may be the same person (e.g. nickname change).</summary>
    PossibleAlias,

    /// <summary>One speaker name may belong to different people (e.g. same name, rejoined).</summary>
    PossibleNameReuse,
}

public enum SuspicionReason
{
    NormalizedNameMatch,
    NameContainment,
    LongInactivityGap,
    LeftAndReturned,
}

public enum SuspicionStatus
{
    Open,
    Acknowledged,
    Resolved,
}

/// <summary>A heuristic hint for the user. Never applied automatically. IDs only, no names.</summary>
public sealed record SpeakerSuspicion(int Id, SuspicionKind Kind, SuspicionReason Reason, ImmutableArray<ParticipantId> Participants, SuspicionStatus Status) : ILogSafe
{
    public override string ToString() =>
        $"SpeakerSuspicion {{ Id = {Id}, Kind = {Kind}, Reason = {Reason}, Participants = {string.Join(",", Participants)}, Status = {Status} }}";
}

/// <summary>A speaker as currently reviewed. <see cref="DisplayName"/> and <see cref="Aliases"/> are personal data.</summary>
public sealed record ReviewParticipant(ParticipantId Id, string DisplayName, ImmutableArray<string> Aliases, ParticipantConsent Consent, int MessageCount)
{
    public override string ToString() =>
        $"ReviewParticipant {{ Id = {Id}, Consent = {Consent}, Messages = {MessageCount}, Aliases = {Aliases.Length} }}";
}

public sealed record SpeakerReviewOptions
{
    /// <summary>A silence at least this long makes one name a possible re-use by a different person.</summary>
    public TimeSpan NameReuseGap { get; init; } = TimeSpan.FromDays(180);

    /// <summary>Shorter normalized names are not used for containment matching (too many false hits).</summary>
    public int MinimumContainmentLength { get; init; } = 2;

    public override string ToString() => $"SpeakerReviewOptions {{ NameReuseGap = {NameReuseGap.TotalDays}d }}";
}

/// <summary>Result of a review operation: the new review on success, the unchanged one with an error otherwise.</summary>
public readonly record struct ReviewStep(SpeakerReview Review, ReviewError Error)
{
    public bool Succeeded => Error == ReviewError.None;

    public override string ToString() => $"ReviewStep {{ Error = {Error} }}";
}

/// <summary>Outcome of <see cref="SpeakerReview.Complete"/>.</summary>
public sealed record ReviewCompletion(ReviewedChat? Chat, IReadOnlyList<CompletionError> Errors)
{
    public bool Succeeded => Chat is not null;

    public override string ToString() => $"ReviewCompletion {{ Succeeded = {Succeeded}, Errors = {string.Join(",", Errors)} }}";
}
