using System.Collections.Immutable;
using TalkPro.Core.Model;
using TalkPro.Core.Parsing;

namespace TalkPro.Ingestion.Speakers;

/// <summary>
/// Immutable, UI-independent speaker review (DD-015). Exports carry no user IDs, so the user must
/// confirm the import attestation, pick the owner (never automatic), and merge, split or acknowledge
/// every suspicious speaker before the chat can be analysed.
/// Every operation returns a new review; the original never changes.
/// </summary>
public sealed class SpeakerReview
{
    private readonly ParsedChat _chat;
    private readonly ImmutableDictionary<MessageId, ParticipantId> _assignment;
    private readonly int _merges;
    private readonly int _splits;

    private SpeakerReview(
        ParsedChat chat,
        ImmutableArray<ReviewParticipant> participants,
        ImmutableDictionary<MessageId, ParticipantId> assignment,
        ImmutableArray<SpeakerSuspicion> suspicions,
        ParticipantId? ownerId,
        bool attestationConfirmed,
        int merges,
        int splits)
    {
        _chat = chat;
        Participants = participants;
        _assignment = assignment;
        Suspicions = suspicions;
        OwnerId = ownerId;
        AttestationConfirmed = attestationConfirmed;
        _merges = merges;
        _splits = splits;
    }

    public ChatFormatId FormatId => _chat.FormatId;

    public ImmutableArray<ReviewParticipant> Participants { get; }

    public ImmutableArray<SpeakerSuspicion> Suspicions { get; }

    /// <summary>Selected by the user only. Null until <see cref="SelectOwner"/> succeeds.</summary>
    public ParticipantId? OwnerId { get; }

    public bool AttestationConfirmed { get; }

    public bool CanComplete => Validate().Count == 0;

    public static SpeakerReview Start(ParsedChat chat, SpeakerReviewOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(chat);
        options ??= new SpeakerReviewOptions();

        var assignment = chat.Messages
            .Where(m => m.SpeakerId is not null)
            .ToImmutableDictionary(m => m.Id, m => m.SpeakerId!.Value);

        // Owner is never pre-selected, whatever the import says (DD-015).
        var participants = chat.Participants
            .Select(p => new ReviewParticipant(p.Id, p.DisplayName, [.. p.Aliases], ParticipantConsent.ContextOnly, 0))
            .ToImmutableArray();
        participants = Recount(participants, assignment);

        var suspicions = SuspicionDetector.Detect(participants, chat.Messages, assignment, options);
        return new SpeakerReview(chat, participants, assignment, suspicions, ownerId: null, attestationConfirmed: false, merges: 0, splits: 0);
    }

    public SpeakerReview ConfirmAttestation() => With(attestationConfirmed: true);

    public SpeakerReview WithdrawAttestation() => With(attestationConfirmed: false);

    public ReviewStep SelectOwner(ParticipantId participantId)
    {
        if (Find(participantId) is not { } candidate)
        {
            return Fail(ReviewError.UnknownParticipant);
        }

        if (candidate.Consent == ParticipantConsent.Excluded)
        {
            return Fail(ReviewError.ParticipantExcluded);
        }

        if (candidate.MessageCount == 0)
        {
            return Fail(ReviewError.OwnerHasNoMessages);
        }

        var participants = Participants.Select(p =>
            p.Id == participantId ? p with { Consent = ParticipantConsent.Owner }
            : p.Consent == ParticipantConsent.Owner ? p with { Consent = ParticipantConsent.ContextOnly }
            : p).ToImmutableArray();

        return Ok(With(participants: participants, ownerId: participantId));
    }

    public SpeakerReview ClearOwner()
    {
        var participants = Participants
            .Select(p => p.Consent == ParticipantConsent.Owner ? p with { Consent = ParticipantConsent.ContextOnly } : p)
            .ToImmutableArray();
        return With(participants: participants, clearOwner: true);
    }

    public ReviewStep SetExcluded(ParticipantId participantId, bool excluded)
    {
        if (Find(participantId) is not { } participant)
        {
            return Fail(ReviewError.UnknownParticipant);
        }

        if (participant.Consent == ParticipantConsent.Owner)
        {
            return Fail(ReviewError.OwnerCannotBeExcluded);
        }

        var consent = excluded ? ParticipantConsent.Excluded : ParticipantConsent.ContextOnly;
        return Ok(With(participants: Replace(participant with { Consent = consent })));
    }

    /// <summary>Treats <paramref name="absorb"/> as another name of <paramref name="keep"/> (e.g. nickname change).</summary>
    public ReviewStep Merge(ParticipantId keep, ParticipantId absorb)
    {
        if (Find(keep) is not { } target || Find(absorb) is not { } source)
        {
            return Fail(ReviewError.UnknownParticipant);
        }

        if (keep == absorb)
        {
            return Fail(ReviewError.SameParticipant);
        }

        var consent = target.Consent == ParticipantConsent.Owner || source.Consent == ParticipantConsent.Owner ? ParticipantConsent.Owner
            : target.Consent == ParticipantConsent.Excluded && source.Consent == ParticipantConsent.Excluded ? ParticipantConsent.Excluded
            : ParticipantConsent.ContextOnly;

        var aliases = target.Aliases
            .Append(source.DisplayName)
            .Concat(source.Aliases)
            .Where(a => !string.Equals(a, target.DisplayName, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToImmutableArray();

        var assignment = _assignment.SetItems(_assignment.Where(kv => kv.Value == absorb).Select(kv => KeyValuePair.Create(kv.Key, keep)));
        var participants = Recount(
            Participants.Where(p => p.Id != absorb).Select(p => p.Id == keep ? p with { Aliases = aliases, Consent = consent } : p).ToImmutableArray(),
            assignment);

        var suspicions = Suspicions.Select(s =>
        {
            if (!s.Participants.Contains(absorb))
            {
                return s;
            }

            var ids = s.Participants.Select(id => id == absorb ? keep : id).Distinct().ToImmutableArray();
            var status = s.Kind == SuspicionKind.PossibleAlias && ids.Length < 2 ? SuspicionStatus.Resolved : s.Status;
            return s with { Participants = ids, Status = status };
        }).ToImmutableArray();

        var ownerId = consent == ParticipantConsent.Owner ? keep : OwnerId;
        return Ok(new SpeakerReview(_chat, participants, assignment, suspicions, ownerId, AttestationConfirmed, _merges + 1, _splits));
    }

    /// <summary>Moves the given messages of <paramref name="source"/> to a new participant with the same name (same-name, different person).</summary>
    public ReviewStep Split(ParticipantId source, IReadOnlyCollection<MessageId> messagesToMove)
    {
        ArgumentNullException.ThrowIfNull(messagesToMove);
        if (Find(source) is not { } original)
        {
            return Fail(ReviewError.UnknownParticipant);
        }

        if (messagesToMove.Count == 0)
        {
            return Fail(ReviewError.NoMessagesSelected);
        }

        if (messagesToMove.Any(id => !_assignment.TryGetValue(id, out var owner) || owner != source))
        {
            return Fail(ReviewError.MessageNotFromParticipant);
        }

        var moving = messagesToMove.ToHashSet();
        if (moving.Count >= original.MessageCount)
        {
            return Fail(ReviewError.SplitWouldLeaveNoMessages);
        }

        var newId = SuspicionDetector.NextId(Participants);
        var created = new ReviewParticipant(newId, original.DisplayName, [], ParticipantConsent.ContextOnly, 0);
        var assignment = _assignment.SetItems(moving.Select(id => KeyValuePair.Create(id, newId)));
        var participants = Recount(Participants.Add(created), assignment);

        var suspicions = Suspicions
            .Select(s => s.Kind == SuspicionKind.PossibleNameReuse && s.Participants.Contains(source) && s.Status == SuspicionStatus.Open
                ? s with { Status = SuspicionStatus.Resolved }
                : s)
            .ToImmutableArray();

        return Ok(new SpeakerReview(_chat, participants, assignment, suspicions, OwnerId, AttestationConfirmed, _merges, _splits + 1));
    }

    /// <summary>Splits off every message of <paramref name="source"/> sent at or after <paramref name="from"/>.</summary>
    public ReviewStep SplitFrom(ParticipantId source, DateTimeOffset from) =>
        Split(source, [.. _chat.Messages.Where(m => _assignment.TryGetValue(m.Id, out var p) && p == source && m.Timestamp >= from).Select(m => m.Id)]);

    /// <summary>The user looked at the hint and keeps the speakers as they are.</summary>
    public ReviewStep Acknowledge(int suspicionId)
    {
        var index = -1;
        for (var i = 0; i < Suspicions.Length; i++)
        {
            if (Suspicions[i].Id == suspicionId)
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            return Fail(ReviewError.UnknownSuspicion);
        }

        if (Suspicions[index].Status != SuspicionStatus.Open)
        {
            return Fail(ReviewError.SuspicionNotOpen);
        }

        return Ok(With(suspicions: Suspicions.SetItem(index, Suspicions[index] with { Status = SuspicionStatus.Acknowledged })));
    }

    /// <summary>Message ids currently attributed to a participant, in file order (for sample display in the UI).</summary>
    public IReadOnlyList<MessageId> MessagesOf(ParticipantId participantId) =>
        [.. _chat.Messages.Where(m => _assignment.TryGetValue(m.Id, out var p) && p == participantId).Select(m => m.Id)];

    public IReadOnlyList<CompletionError> Validate()
    {
        var errors = new List<CompletionError>();
        if (!AttestationConfirmed)
        {
            errors.Add(CompletionError.AttestationNotConfirmed);
        }

        if (OwnerId is null)
        {
            errors.Add(CompletionError.OwnerNotSelected);
        }

        if (Suspicions.Any(s => s.Status == SuspicionStatus.Open))
        {
            errors.Add(CompletionError.OpenSuspicions);
        }

        return errors;
    }

    public ReviewCompletion Complete()
    {
        var errors = Validate();
        if (errors.Count > 0)
        {
            return new ReviewCompletion(null, errors);
        }

        var consent = Participants.ToDictionary(p => p.Id, p => p.Consent);
        var messages = new List<ChatMessage>(_chat.Messages.Count);
        var dropped = 0;
        foreach (var message in _chat.Messages)
        {
            if (message.SpeakerId is null)
            {
                messages.Add(message);
                continue;
            }

            var speaker = _assignment[message.Id];
            if (consent[speaker] == ParticipantConsent.Excluded)
            {
                dropped++;
                continue;
            }

            messages.Add(message with { SpeakerId = speaker });
        }

        var participants = Participants
            .Select(p => new Participant(p.Id, p.DisplayName, p.Consent) { Aliases = [.. p.Aliases] })
            .ToList();

        var summary = new ReviewSummary(
            participants.Count,
            _merges,
            _splits,
            Participants.Count(p => p.Consent == ParticipantConsent.Excluded),
            Suspicions.Count(s => s.Status == SuspicionStatus.Acknowledged),
            dropped);

        return new ReviewCompletion(new ReviewedChat(_chat.FormatId, _chat.Encoding, OwnerId!.Value, participants, messages, summary), []);
    }

    public override string ToString() =>
        $"SpeakerReview {{ Participants = {Participants.Length}, Owner = {OwnerId?.ToString() ?? "none"}, Attested = {AttestationConfirmed}, OpenSuspicions = {Suspicions.Count(s => s.Status == SuspicionStatus.Open)} }}";

    private static ImmutableArray<ReviewParticipant> Recount(ImmutableArray<ReviewParticipant> participants, ImmutableDictionary<MessageId, ParticipantId> assignment)
    {
        var counts = assignment.Values.GroupBy(v => v).ToDictionary(g => g.Key, g => g.Count());
        return [.. participants.Select(p => p with { MessageCount = counts.GetValueOrDefault(p.Id) })];
    }

    private ReviewParticipant? Find(ParticipantId id) => Participants.FirstOrDefault(p => p.Id == id);

    private ImmutableArray<ReviewParticipant> Replace(ReviewParticipant updated) =>
        [.. Participants.Select(p => p.Id == updated.Id ? updated : p)];

    private SpeakerReview With(
        ImmutableArray<ReviewParticipant>? participants = null,
        ImmutableArray<SpeakerSuspicion>? suspicions = null,
        ParticipantId? ownerId = null,
        bool clearOwner = false,
        bool? attestationConfirmed = null) =>
        new(
            _chat,
            participants ?? Participants,
            _assignment,
            suspicions ?? Suspicions,
            clearOwner ? null : ownerId ?? OwnerId,
            attestationConfirmed ?? AttestationConfirmed,
            _merges,
            _splits);

    private static ReviewStep Ok(SpeakerReview review) => new(review, ReviewError.None);

    private ReviewStep Fail(ReviewError error) => new(this, error);
}
