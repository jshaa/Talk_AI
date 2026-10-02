namespace TalkPro.Tools.SampleGenerator.Corpus;

/// <summary>One logical element of a synthetic conversation, rendered differently by each format writer.</summary>
public abstract record SampleItem;

/// <summary>A user message. <c>TextLines[0]</c> goes on the record line, the rest are continuation lines.</summary>
public sealed record MessageItem(DateTime Time, string Speaker, IReadOnlyList<string> TextLines) : SampleItem
{
    public MessageItem(DateTime time, string speaker, string text)
        : this(time, speaker, [text])
    {
    }
}

/// <summary>A system line. <see cref="ExpectedEvent"/> is <c>Join</c>, <c>Leave</c> or <c>Other</c>.</summary>
public sealed record SystemItem(DateTime Time, string Text, string ExpectedEvent) : SampleItem;

/// <summary>A deliberately broken record; each writer renders it in its own syntax.</summary>
public sealed record MalformedItem(MalformationKind Kind) : SampleItem;

/// <summary>
/// A format-independent raw line. <see cref="RawLineExpectation.Ignored"/> lines must be blank; while a
/// message is open they become (dropped) continuation lines. <see cref="RawLineExpectation.OrphanLine"/>
/// lines are only valid while no message is open; the writers reject corpus errors.
/// </summary>
public sealed record RawLineItem(string Line, RawLineExpectation Expectation) : SampleItem;

public enum MalformationKind
{
    InvalidDate,
    InvalidTime,
    ZeroYear,
    OutOfRangeDate,
    AbsurdNumbers,
    TooLongSpeaker,
    BlankSpeaker,
    TruncatedRecord,

    /// <summary>W1 only: a message line before any date marker.</summary>
    MissingDateContext,
}

public enum RawLineExpectation
{
    Ignored,
    OrphanLine,
    LineTooLong,
    NullCharacter,
}

public enum LineEnding
{
    CrLf,
    Lf,
}

public enum GoldenEncoding
{
    Utf8,
    Utf8Bom,
    Cp949,
}

/// <summary>A logical sample rendered once per format and encoded once per listed encoding.</summary>
public sealed record SampleCase(
    string Id,
    string Description,
    IReadOnlyList<SampleItem> Items,
    IReadOnlyList<GoldenEncoding> Encodings,
    LineEnding LineEnding = LineEnding.CrLf,
    bool FinalNewline = true,
    bool IncludeHeader = true)
{
    /// <summary>Format version written into the signature line; anything but 1 is <c>Unsupported</c>.</summary>
    public int SignatureVersion { get; init; } = 1;

    /// <summary>Additional (unknown) header lines, rendered after <c>Saved:</c>.</summary>
    public IReadOnlyList<string> ExtraHeaderLines { get; init; } = [];

    /// <summary>Format prefixes (<c>w1</c>, <c>a1</c>, <c>i1</c>) this case is rendered for; <see langword="null"/> = all.</summary>
    public IReadOnlyList<string>? Formats { get; init; }
}
