using System.Globalization;
using TalkPro.Core.Diagnostics;

namespace TalkPro.Core.Model;

/// <summary>Random identifier of an imported dataset (one vault, one DEK).</summary>
public readonly record struct DatasetId(Guid Value) : ILogSafe
{
    public static DatasetId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("N", CultureInfo.InvariantCulture);
}

/// <summary>
/// Stable message identifier derived from the source position (e.g. <c>m0000012</c>), never from
/// content: hashes of short messages can be reversed by dictionary attack (DD-010).
/// </summary>
public readonly record struct MessageId : ILogSafe
{
    public MessageId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

/// <summary>
/// Dataset-local participant identifier (e.g. "p_01"). Not a display name: messenger exports
/// carry no user IDs, so identity is only established through explicit user confirmation.
/// </summary>
public readonly record struct ParticipantId : ILogSafe
{
    public ParticipantId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
