using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using TalkPro.Core.Model;
using TalkPro.Ingestion.Parsing;

namespace TalkPro.Ingestion.Speakers;

/// <summary>
/// Content-free heuristics that flag speakers for the user's attention. Exports have no user IDs,
/// so these are hints only: nothing is merged or split without an explicit user decision.
/// </summary>
internal static class SuspicionDetector
{
    public static ImmutableArray<SpeakerSuspicion> Detect(
        IReadOnlyList<ReviewParticipant> participants,
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyDictionary<MessageId, ParticipantId> assignment,
        SpeakerReviewOptions options)
    {
        var found = ImmutableArray.CreateBuilder<SpeakerSuspicion>();
        DetectAliases(participants, options, found);
        DetectNameReuse(participants, messages, assignment, options, found);
        return found.ToImmutable();
    }

    /// <summary>Lower-noise comparison key: compatibility-normalized letters and digits, upper case.</summary>
    public static string Normalize(string name) => Normalize(name, compatibilityFormAvailable: !OperatingSystem.IsBrowser());

    /// <summary>
    /// The browser runtime (web viewer, DD-026) supports only NFC/NFD. There, full-width ASCII and the ideographic
    /// space — the compatibility forms that occur in names — are folded by hand before NFC, giving the same key.
    /// </summary>
    internal static string Normalize(string name, bool compatibilityFormAvailable)
    {
        ArgumentNullException.ThrowIfNull(name);
        var normalized = compatibilityFormAvailable ? name.Normalize(NormalizationForm.FormKC) : FoldWidth(name).Normalize(NormalizationForm.FormC);
        var builder = new StringBuilder(name.Length);
        foreach (var rune in normalized.EnumerateRunes())
        {
            if (Rune.IsLetterOrDigit(rune))
            {
                builder.Append(Rune.ToUpperInvariant(rune).ToString());
            }
        }

        return builder.ToString();
    }

    private static string FoldWidth(string name)
    {
        var chars = name.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = chars[i] switch
            {
                >= '！' and <= '～' => (char)(chars[i] - 0xFEE0), // full-width ASCII
                '　' => ' ', // ideographic space
                _ => chars[i],
            };
        }

        return new string(chars);
    }

    private static void DetectAliases(IReadOnlyList<ReviewParticipant> participants, SpeakerReviewOptions options, ImmutableArray<SpeakerSuspicion>.Builder found)
    {
        var keys = participants.Select(p => Normalize(p.DisplayName)).ToList();
        for (var i = 0; i < participants.Count; i++)
        {
            for (var j = i + 1; j < participants.Count; j++)
            {
                SuspicionReason? reason = null;
                if (keys[i].Length > 0 && string.Equals(keys[i], keys[j], StringComparison.Ordinal))
                {
                    reason = SuspicionReason.NormalizedNameMatch;
                }
                else if (Contains(keys[i], keys[j], options.MinimumContainmentLength) || Contains(keys[j], keys[i], options.MinimumContainmentLength))
                {
                    reason = SuspicionReason.NameContainment;
                }

                if (reason is { } r)
                {
                    found.Add(new SpeakerSuspicion(found.Count + 1, SuspicionKind.PossibleAlias, r, [participants[i].Id, participants[j].Id], SuspicionStatus.Open));
                }
            }
        }
    }

    private static bool Contains(string longer, string shorter, int minimumLength) =>
        shorter.Length >= minimumLength && longer.Length > shorter.Length && longer.Contains(shorter, StringComparison.Ordinal);

    private static void DetectNameReuse(
        IReadOnlyList<ReviewParticipant> participants,
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyDictionary<MessageId, ParticipantId> assignment,
        SpeakerReviewOptions options,
        ImmutableArray<SpeakerSuspicion>.Builder found)
    {
        var byName = participants.ToDictionary(p => p.DisplayName, p => p.Id, StringComparer.Ordinal);
        var left = new HashSet<ParticipantId>();
        var flagged = new Dictionary<ParticipantId, SuspicionReason>();
        var lastSeen = new Dictionary<ParticipantId, DateTimeOffset>();

        foreach (var message in messages)
        {
            if (message.Kind == MessageKind.System)
            {
                if (message.SystemEvent == SystemEventType.Leave
                    && SystemEventClassifier.TryGetSubject(message.Text, out var subject)
                    && byName.TryGetValue(subject, out var leaver))
                {
                    left.Add(leaver);
                }

                continue;
            }

            if (!assignment.TryGetValue(message.Id, out var speaker))
            {
                continue;
            }

            if (left.Contains(speaker))
            {
                flagged.TryAdd(speaker, SuspicionReason.LeftAndReturned);
            }

            if (lastSeen.TryGetValue(speaker, out var previous) && message.Timestamp - previous >= options.NameReuseGap)
            {
                flagged.TryAdd(speaker, SuspicionReason.LongInactivityGap);
            }

            if (!lastSeen.TryGetValue(speaker, out var seen) || message.Timestamp > seen)
            {
                lastSeen[speaker] = message.Timestamp;
            }
        }

        foreach (var participant in participants.Where(p => flagged.ContainsKey(p.Id)))
        {
            found.Add(new SpeakerSuspicion(found.Count + 1, SuspicionKind.PossibleNameReuse, flagged[participant.Id], [participant.Id], SuspicionStatus.Open));
        }
    }

    /// <summary>Next free id in the <c>p_NN</c> scheme used by <see cref="ParticipantResolver"/>.</summary>
    public static ParticipantId NextId(IEnumerable<ReviewParticipant> participants)
    {
        var max = participants
            .Select(p => int.TryParse(p.Id.Value.AsSpan(2), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max();
        return new ParticipantId(string.Create(CultureInfo.InvariantCulture, $"p_{max + 1:D2}"));
    }
}
