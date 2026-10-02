namespace TalkPro.Core.Parsing;

/// <summary>Input-independent parsing settings and limits shared by every adapter.</summary>
public sealed record ParseContext
{
    public static readonly DateOnly DefaultMinDate = new(1990, 1, 1);
    public static readonly DateOnly DefaultMaxDate = new(2100, 12, 31);

    /// <summary>Exports carry no offset; local times are interpreted in this zone.</summary>
    public required TimeZoneInfo TimeZone { get; init; }

    /// <summary>Lines longer than this are rejected before reaching an adapter.</summary>
    public int MaxLineLength { get; init; } = 100_000;

    public int MaxSpeakerNameLength { get; init; } = 128;

    public DateOnly MinDate { get; init; } = DefaultMinDate;

    public DateOnly MaxDate { get; init; } = DefaultMaxDate;

    public static ParseContext ForLocalTimeZone() => new() { TimeZone = TimeZoneInfo.Local };

    public bool IsInRange(DateOnly date) => date >= MinDate && date <= MaxDate;

    public DateTimeOffset ToTimestamp(DateTime localTime)
    {
        var unspecified = DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, TimeZone.GetUtcOffset(unspecified));
    }

    public override string ToString() => $"ParseContext {{ TimeZone = {TimeZone.Id}, MaxLineLength = {MaxLineLength} }}";
}
