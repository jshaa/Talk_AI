namespace TalkPro.Core.Parsing;

/// <summary>
/// Format-specific line interpreter. Implementations are pure: no I/O, no logging, no shared mutable
/// state. Encoding, line splitting, guards and format selection belong to the ingestion driver.
/// </summary>
public interface IChatFormatAdapter
{
    ChatFormatId FormatId { get; }

    /// <summary>
    /// Confidence in [0, 1] that <paramref name="sampleLines"/> (the first lines of a decoded export)
    /// are in this format. Must not throw on arbitrary input.
    /// </summary>
    double Detect(IReadOnlyList<string> sampleLines);

    ParserState CreateInitialState(ParseContext context);

    /// <summary>
    /// Interprets the line identified by <see cref="ParserState.LineNumber"/>. The returned state must
    /// keep the same line number. <paramref name="line"/> has no line terminator.
    /// </summary>
    LineStep ParseLine(ParserState state, string line);
}

public readonly record struct LineStep(ParserState State, LineParseResult Result)
{
    public override string ToString() => Result.ToString();
}
