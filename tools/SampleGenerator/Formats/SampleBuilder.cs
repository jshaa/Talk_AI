namespace TalkPro.Tools.SampleGenerator.Formats;

/// <summary>
/// Collects rendered lines together with their expected outcomes, following the common rules of
/// <c>docs/SYNTHETIC_FORMATS.md</c>. Throws <see cref="InvalidOperationException"/> on corpus errors
/// (a sample that would not mean what it claims), so a broken corpus can never produce golden data.
/// </summary>
public sealed class SampleBuilder(SyntheticFormat format)
{
    /// <summary>Driver guard of the parser (<c>ParseContext.MaxLineLength</c> default).</summary>
    public const int MaxLineLength = 100_000;

    private readonly List<string> _lines = [];
    private readonly List<ExpectedLine> _outcomes = [];
    private readonly List<ExpectedEntry> _entries = [];

    public bool IsMessageOpen { get; private set; }

    /// <summary>Date context of formats with date markers (W1).</summary>
    public DateOnly? DateContext { get; set; }

    public int NextLineNumber => _lines.Count + 1;

    public void Header(string line, ExpectedOutcome outcome, ExpectedIssue issue = ExpectedIssue.None)
    {
        if (format.IsReserved(line) || string.IsNullOrWhiteSpace(line))
        {
            throw CorpusError("Header lines must be non-blank and must not start with a reserved record prefix.");
        }

        Add(line, outcome, issue);
    }

    /// <summary>The blank line that ends the header.</summary>
    public void HeaderSeparator() => Add(string.Empty, ExpectedOutcome.Ignored);

    /// <summary>A record line that only sets context (W1 date marker). Completes the open message.</summary>
    public void ContextRecord(string line)
    {
        EnsureRecord(line);
        Add(line, ExpectedOutcome.Metadata);
        IsMessageOpen = false;
    }

    public void StartMessage(string recordLine, DateTime localTime, string speaker, IReadOnlyList<string> textLines)
    {
        EnsureRecord(recordLine);
        if (textLines.Count == 0)
        {
            throw CorpusError("A message needs at least its record line.");
        }

        var startLine = NextLineNumber;
        Add(recordLine, ExpectedOutcome.MessageStarted);
        for (var i = 1; i < textLines.Count; i++)
        {
            var continuation = textLines[i];
            EnsurePlainLine(continuation);
            if (format.IsReserved(continuation))
            {
                throw CorpusError("A continuation line must not start with a reserved record prefix of the format.");
            }

            Add(continuation, ExpectedOutcome.Continuation);
        }

        // Trailing blank continuation lines are dropped when the message completes.
        var kept = textLines.Count;
        while (kept > 1 && string.IsNullOrWhiteSpace(textLines[kept - 1]))
        {
            kept--;
        }

        _entries.Add(new ExpectedEntry(startLine, startLine + kept - 1, localTime, speaker, "None", string.Join('\n', textLines.Take(kept))));
        IsMessageOpen = true;
    }

    public void System(string line, DateTime localTime, string text, string expectedEvent)
    {
        EnsureRecord(line);
        _entries.Add(new ExpectedEntry(NextLineNumber, NextLineNumber, localTime, Speaker: null, expectedEvent, text));
        Add(line, ExpectedOutcome.SystemMessage);
        IsMessageOpen = false;
    }

    public void Malformed(string line, ExpectedIssue issue)
    {
        EnsurePlainLine(line);
        if (!format.IsReserved(line))
        {
            throw CorpusError("A malformed record must start with a reserved prefix, otherwise it is a continuation or orphan line.");
        }

        Add(line, ExpectedOutcome.Malformed, issue);
        IsMessageOpen = false;
    }

    public void Raw(string line, Corpus.RawLineExpectation expectation)
    {
        switch (expectation)
        {
            case Corpus.RawLineExpectation.Ignored:
                if (!string.IsNullOrWhiteSpace(line))
                {
                    throw CorpusError("Ignored raw lines must be blank.");
                }

                EnsurePlainLine(line);
                Add(line, IsMessageOpen ? ExpectedOutcome.Continuation : ExpectedOutcome.Ignored);
                break;

            case Corpus.RawLineExpectation.OrphanLine:
                EnsurePlainLine(line);
                if (IsMessageOpen || string.IsNullOrWhiteSpace(line) || format.IsReserved(line))
                {
                    throw CorpusError("An orphan line must be non-blank, unreserved and follow a closed record.");
                }

                Add(line, ExpectedOutcome.Malformed, ExpectedIssue.OrphanLine);
                break;

            case Corpus.RawLineExpectation.LineTooLong:
                if (line.Length <= MaxLineLength || line.Contains('\0', StringComparison.Ordinal))
                {
                    throw CorpusError("A too-long line must exceed the driver limit and contain no NUL.");
                }

                Add(line, ExpectedOutcome.Malformed, ExpectedIssue.LineTooLong);
                IsMessageOpen = false;
                break;

            case Corpus.RawLineExpectation.NullCharacter:
                if (!line.Contains('\0', StringComparison.Ordinal) || line.Length > MaxLineLength)
                {
                    throw CorpusError("A NUL line must contain U+0000 and respect the length limit.");
                }

                Add(line, ExpectedOutcome.Malformed, ExpectedIssue.NullCharacter);
                IsMessageOpen = false;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(expectation));
        }
    }

    public RenderedSample Build() => new([.. _lines], [.. _outcomes], [.. _entries]);

    private static InvalidOperationException CorpusError(string reason) => new("Synthetic corpus error: " + reason);

    private static void EnsurePlainLine(string line)
    {
        if (line.Contains('\n', StringComparison.Ordinal) || line.Contains('\0', StringComparison.Ordinal) || line.EndsWith('\r'))
        {
            throw CorpusError("Lines must not contain LF or NUL and must not end with CR.");
        }

        if (line.Length > MaxLineLength)
        {
            throw CorpusError("Line exceeds the driver limit.");
        }
    }

    private void EnsureRecord(string line)
    {
        EnsurePlainLine(line);
        if (!format.IsReserved(line))
        {
            throw CorpusError("A record line must start with a reserved prefix of its format.");
        }
    }

    private void Add(string line, ExpectedOutcome outcome, ExpectedIssue issue = ExpectedIssue.None)
    {
        _lines.Add(line);
        _outcomes.Add(new ExpectedLine(outcome, issue));
    }
}
