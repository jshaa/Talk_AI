using System.Collections.Immutable;
using TalkPro.Core.Model;

namespace TalkPro.Core.Parsing;

public enum ParserSection
{
    Header,
    Body,
}

/// <summary>
/// Base for adapter-specific state. <see cref="ToString"/> is sealed so derived records in adapter
/// assemblies cannot print their (possibly content-bearing) members.
/// </summary>
public abstract record AdapterState
{
    public sealed override string ToString() => GetType().Name;
}

/// <summary>A message being assembled from its record line and continuation lines.</summary>
public sealed record PendingEntry(int StartLine, int EndLine, DateTimeOffset Timestamp, string SpeakerName, ImmutableList<string> Lines)
{
    public override string ToString() => $"PendingEntry {{ Lines = {StartLine}-{EndLine}, LineCount = {Lines.Count} }}";

    /// <summary>Joins the lines with <c>\n</c>; trailing blank (empty or whitespace-only) continuation lines are dropped.</summary>
    internal ParsedEntry Complete()
    {
        var lines = Lines;
        while (lines.Count > 1 && string.IsNullOrWhiteSpace(lines[^1]))
        {
            lines = lines.RemoveAt(lines.Count - 1);
        }

        return new ParsedEntry(StartLine, StartLine + lines.Count - 1, Timestamp, SpeakerName, MessageKind.Text, string.Join('\n', lines));
    }
}

/// <summary>
/// Immutable parser state shared by all adapters: line position, section, date context, the open
/// message, seen speakers and an optional typed <see cref="AdapterState"/>.
/// Only the driver advances <see cref="LineNumber"/>.
/// </summary>
public sealed record ParserState
{
    private ParserState(ParseContext context, AdapterState? adapterState)
    {
        Context = context;
        AdapterState = adapterState;
    }

    public ParseContext Context { get; }

    /// <summary>Number of the line being processed (1-based); 0 before the first line.</summary>
    public int LineNumber { get; private init; }

    public ParserSection Section { get; private init; } = ParserSection.Header;

    public DateOnly? CurrentDate { get; private init; }

    public PendingEntry? Pending { get; private init; }

    /// <summary>Speaker names seen so far (participant context). Personal data.</summary>
    public ImmutableHashSet<string> SpeakerNames { get; private init; } = ImmutableHashSet.Create<string>(StringComparer.Ordinal);

    public AdapterState? AdapterState { get; private init; }

    public bool IsEntryOpen => Pending is not null;

    public bool IsMultiline => Pending is { Lines.Count: > 1 };

    public static ParserState Initial(ParseContext context, AdapterState? adapterState = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new ParserState(context, adapterState);
    }

    public ParserState AdvanceLine() => this with { LineNumber = LineNumber + 1 };

    public ParserState EnterBody() => Section == ParserSection.Body ? this : this with { Section = ParserSection.Body };

    public ParserState WithDate(DateOnly date) => this with { CurrentDate = date };

    /// <summary>Drops the date context, e.g. after an invalid date marker.</summary>
    public ParserState ClearDate() => CurrentDate is null ? this : this with { CurrentDate = null };

    public ParserState WithAdapterState(AdapterState adapterState)
    {
        ArgumentNullException.ThrowIfNull(adapterState);
        return this with { AdapterState = adapterState };
    }

    public T GetAdapterState<T>()
        where T : AdapterState =>
        AdapterState as T ?? throw new InvalidOperationException("Parser state was created by a different adapter.");

    /// <summary>Completes the open message, if any.</summary>
    public (ParserState State, ImmutableArray<ParsedEntry> Completed) FlushPending() =>
        Pending is null ? (this, []) : (this with { Pending = null }, [Pending.Complete()]);

    /// <summary>Completes the open message and opens a new one on the current line.</summary>
    public (ParserState State, ImmutableArray<ParsedEntry> Completed) StartMessage(DateTimeOffset timestamp, string speakerName, string firstLine)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(speakerName);
        ArgumentNullException.ThrowIfNull(firstLine);
        EnsureLineStarted();

        var (flushed, completed) = FlushPending();
        var next = flushed with
        {
            Pending = new PendingEntry(LineNumber, LineNumber, timestamp, speakerName, [firstLine]),
            SpeakerNames = flushed.SpeakerNames.Add(speakerName),
        };
        return (next, completed);
    }

    /// <summary>Appends the current line to the open message.</summary>
    public ParserState AppendContinuation(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (Pending is null)
        {
            throw new InvalidOperationException("No message is open.");
        }

        return this with { Pending = Pending with { EndLine = LineNumber, Lines = Pending.Lines.Add(line) } };
    }

    /// <summary>Completes the open message (if any) followed by a single-line system entry.</summary>
    public (ParserState State, ImmutableArray<ParsedEntry> Completed) CompleteSystemEntry(
        DateTimeOffset timestamp,
        string text,
        SystemEventType systemEvent = SystemEventType.Other)
    {
        ArgumentNullException.ThrowIfNull(text);
        EnsureLineStarted();

        var (flushed, completed) = FlushPending();
        var system = new ParsedEntry(LineNumber, LineNumber, timestamp, SpeakerName: null, MessageKind.System, text) { SystemEvent = systemEvent };
        return (flushed, completed.Add(system));
    }

    public override string ToString() =>
        $"ParserState {{ Line = {LineNumber}, Section = {Section}, EntryOpen = {IsEntryOpen}, Speakers = {SpeakerNames.Count} }}";

    private void EnsureLineStarted()
    {
        if (LineNumber < 1)
        {
            throw new InvalidOperationException("AdvanceLine must be called before processing a line.");
        }
    }
}
