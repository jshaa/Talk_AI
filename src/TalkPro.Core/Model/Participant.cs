namespace TalkPro.Core.Model;

/// <summary>
/// MVP1 per-participant processing status (docs report ch. 9, simplified for MVP1).
/// Only <see cref="Owner"/> may become a Style/Profile subject in MVP1.
/// </summary>
public enum ParticipantConsent
{
    /// <summary>The importing user, explicitly selected by them. Never inferred automatically.</summary>
    Owner,

    /// <summary>Gave direct consent (reserved for MVP2; not assignable in MVP1).</summary>
    Consented,

    /// <summary>Default for everyone else: masked context only, no profiling, no export.</summary>
    ContextOnly,

    /// <summary>Removed from all processing by the user.</summary>
    Excluded,
}

/// <summary>A speaker in an export. <see cref="DisplayName"/> is personal data.</summary>
public sealed record Participant(ParticipantId Id, string DisplayName, ParticipantConsent Consent)
{
    /// <summary>Other display names the user confirmed as the same person (nickname changes).</summary>
    public IReadOnlyList<string> Aliases { get; init; } = [];

    /// <summary>Content-free representation; the compiler-generated one would print the name.</summary>
    public override string ToString() => $"Participant {{ Id = {Id}, Consent = {Consent} }}";
}
