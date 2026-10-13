using System.Globalization;
using TalkPro.Core.Diagnostics;

namespace TalkPro.Core.Model;

/// <summary>
/// What a counterpart allowed in their own signed consent (L3, DD-028). Everything not listed here — emotion
/// analysis, fine-tuning, cloud transfer, commercial use, sharing, sensitive categories, use after death — can
/// never be granted.
/// </summary>
[Flags]
public enum ConsentScope
{
    None = 0,

    /// <summary>Analysing and learning the person's way of writing. Required by every other scope.</summary>
    StyleProfile = 1,

    /// <summary>Chatting with an AI that writes in the person's style.</summary>
    PersonaChat = 2,

    /// <summary>Using the person's messages as memory/evidence for that chat.</summary>
    MemoryRetrieval = 4,

    /// <summary>Showing the person's stored messages verbatim as quotes.</summary>
    VerbatimQuote = 8,
}

/// <summary>Effective state of a counterpart's consent at a point in time (report §9.2).</summary>
public enum ConsentStatus
{
    /// <summary>No consent registered: the participant stays context-only.</summary>
    None,

    /// <summary>Valid, bound to the chat and within its period: use within the granted scope only.</summary>
    Granted,

    /// <summary>The period ended: derived data is purged, nothing is used beyond context.</summary>
    Expired,

    /// <summary>Withdrawn by the person or the owner: derived data is purged and the token cannot be re-registered.</summary>
    Revoked,
}

/// <summary>Random 128-bit consent identifier (32 lowercase hex characters). Carries no personal data.</summary>
public readonly record struct ConsentId : ILogSafe
{
    public const int Length = 32;

    public ConsentId(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length != Length || !value.All(char.IsAsciiHexDigitLower))
        {
            throw new ArgumentException("A consent id is 32 lowercase hex characters.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public static ConsentId New() => new(Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)));

    public static bool TryParse(string? value, out ConsentId id)
    {
        id = default;
        if (value is not { Length: Length } || !value.All(char.IsAsciiHexDigitLower))
        {
            return false;
        }

        id = new ConsentId(value);
        return true;
    }

    public override string ToString() => Value;
}

/// <summary>
/// A counterpart's verified consent as registered in one dataset. <see cref="Participant"/> is the speaker who sent
/// the confirmation code in the chat. <see cref="Token"/> is the original signed token kept as evidence.
/// </summary>
public sealed record ConsentGrant(
    ConsentId Id,
    ParticipantId Participant,
    ConsentScope Scope,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset RegisteredAt,
    MessageId ConfirmationMessage,
    string Token)
{
    /// <summary>Set once the grant was revoked (by the person or the owner).</summary>
    public DateTimeOffset? RevokedAt { get; init; }

    public ConsentStatus StatusAt(DateTimeOffset now) =>
        RevokedAt is not null ? ConsentStatus.Revoked
        : now >= ExpiresAt ? ConsentStatus.Expired
        : ConsentStatus.Granted;

    /// <summary>True only while granted and when every flag of <paramref name="scope"/> was granted.</summary>
    public bool Allows(ConsentScope scope, DateTimeOffset now) =>
        scope != ConsentScope.None && StatusAt(now) == ConsentStatus.Granted && (Scope & scope) == scope;

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"ConsentGrant {{ Id = {Id}, Participant = {Participant}, Scope = {Scope}, Revoked = {RevokedAt is not null} }}");
}
