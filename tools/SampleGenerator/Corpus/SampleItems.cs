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

public sealed record SystemItem(DateTime Time, string Text, string ExpectedEvent) : SampleItem;

/// <summary>A deliberately broken record; each writer renders it in its own syntax.</summary>
public sealed record MalformedItem(MalformationKind Kind) : SampleItem;

/// <summary>A format-independent raw line with its expected outcome (never inside an open message).</summary>
public sealed record RawLineItem(string Line, RawLineExpectation Expectation) : SampleItem;

public enum MalformationKind
{
    InvalidDate,
    InvalidTime,
    ZeroYear,
    OutOfRangeDate,
    TooLongSpeaker,
    BlankSpeaker,
    TruncatedRecord,
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
    bool IncludeHeader = true);
