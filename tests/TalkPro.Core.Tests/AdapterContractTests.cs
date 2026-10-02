using System.Reflection;
using TalkPro.Core.Parsing;

namespace TalkPro.Core.Tests;

/// <summary>
/// The contract can be implemented with Core alone: a minimal adapter written here (without any
/// Ingestion code) supports detection, header/body, multiline, system, malformed and typed state.
/// </summary>
public sealed class AdapterContractTests
{
    private readonly ParseContext _context = TestContext.Create();

    [Fact]
    public void MinimalAdapterHandlesEveryOutcome()
    {
        var adapter = new ArrowAdapter();
        string[] lines = ["#arrow", "> UserA: 첫 줄", "  둘째 줄", "! 알림", "?? 이상한 줄", ""];

        var (results, entries) = Run(adapter, lines);

        Assert.Equal(
            [LineOutcome.Metadata, LineOutcome.MessageStarted, LineOutcome.Continuation, LineOutcome.SystemMessage, LineOutcome.Malformed, LineOutcome.Ignored],
            results.Select(r => r.Outcome));
        Assert.Equal(ParseIssue.OrphanLine, results[4].Issue);
        Assert.Equal(["첫 줄\n  둘째 줄", "알림"], entries.Select(e => e.Text));
        Assert.Equal([1, 2, 3, 4, 5, 6], results.Select(r => r.LineNumber));
    }

    [Fact]
    public void AdapterStateTracksAdapterSpecificContext()
    {
        var (results, _) = Run(new ArrowAdapter(), ["#arrow", "> UserA: 하나", "> UserB: 둘"], out var finalState);

        Assert.Equal(3, results.Count);
        Assert.Equal(2, finalState.GetAdapterState<ArrowAdapter.Counter>().Messages);
    }

    [Fact]
    public void DetectionIsContentBased()
    {
        var adapter = new ArrowAdapter();

        Assert.Equal(1.0, adapter.Detect(["#arrow"]));
        Assert.Equal(0.0, adapter.Detect(["anything else"]));
        Assert.Equal(0.0, adapter.Detect([]));
    }

    [Fact]
    public void ContractHasNoIoOrLoggingTypes()
    {
        var forbidden = new[] { typeof(Stream), typeof(TextReader), typeof(FileInfo) };
        var signatureTypes = typeof(IChatFormatAdapter).GetMethods()
            .SelectMany(m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType))
            .ToList();

        Assert.DoesNotContain(signatureTypes, t => forbidden.Any(f => f.IsAssignableFrom(t)));
        Assert.DoesNotContain(
            typeof(IChatFormatAdapter).Assembly.GetReferencedAssemblies(),
            a => a.Name!.StartsWith("Microsoft.Extensions", StringComparison.Ordinal));
    }

    [Fact]
    public void ParserStateCannotBeMutatedFromOutsideCore()
    {
        var setters = typeof(ParserState).GetProperties()
            .Select(p => p.SetMethod)
            .Where(s => s is not null && (s.IsPublic || s.IsFamily || s.IsFamilyOrAssembly));

        Assert.Empty(setters);
        Assert.True(typeof(ParserState).IsSealed);
        Assert.DoesNotContain(typeof(ParserState).GetConstructors(BindingFlags.Public | BindingFlags.Instance), _ => true);
    }

    private (List<LineParseResult> Results, List<ParsedEntry> Entries) Run(IChatFormatAdapter adapter, string[] lines) =>
        Run(adapter, lines, out _);

    private (List<LineParseResult> Results, List<ParsedEntry> Entries) Run(IChatFormatAdapter adapter, string[] lines, out ParserState finalState)
    {
        var state = adapter.CreateInitialState(_context);
        var results = new List<LineParseResult>();
        var entries = new List<ParsedEntry>();
        foreach (var line in lines)
        {
            state = state.AdvanceLine();
            var step = adapter.ParseLine(state, line);
            Assert.Equal(state.LineNumber, step.State.LineNumber);
            results.Add(step.Result);
            entries.AddRange(step.Result.Completed);
            state = step.State;
        }

        entries.AddRange(state.FlushPending().Completed);
        finalState = state;
        return (results, entries);
    }

    /// <summary>Toy format: <c>#arrow</c> header, <c>&gt; name: text</c>, <c>! system</c>, indented continuation.</summary>
    private sealed class ArrowAdapter : IChatFormatAdapter
    {
        public ChatFormatId FormatId { get; } = new("test.arrow.v1");

        public double Detect(IReadOnlyList<string> sampleLines) => sampleLines.Count > 0 && sampleLines[0] == "#arrow" ? 1 : 0;

        public ParserState CreateInitialState(ParseContext context) => ParserState.Initial(context, new Counter(0));

        public LineStep ParseLine(ParserState state, string line)
        {
            var n = state.LineNumber;
            if (state.Section == ParserSection.Header && line == "#arrow")
            {
                return new(state.EnterBody(), LineParseResult.Metadata(n, []));
            }

            if (line.StartsWith("> ", StringComparison.Ordinal))
            {
                var colon = line.IndexOf(": ", StringComparison.Ordinal);
                var (next, completed) = state.StartMessage(DateTimeOffset.UnixEpoch, line[2..colon], line[(colon + 2)..]);
                var counter = next.GetAdapterState<Counter>();
                return new(next.WithAdapterState(counter with { Messages = counter.Messages + 1 }), LineParseResult.MessageStarted(n, completed));
            }

            if (line.StartsWith("! ", StringComparison.Ordinal))
            {
                var (next, completed) = state.CompleteSystemEntry(DateTimeOffset.UnixEpoch, line[2..]);
                return new(next, LineParseResult.SystemMessage(n, completed));
            }

            if (state.IsEntryOpen && line.StartsWith(' '))
            {
                return new(state.AppendContinuation(line), LineParseResult.Continuation(n));
            }

            if (line.Length == 0)
            {
                return new(state, LineParseResult.Ignored(n));
            }

            var (flushed, done) = state.FlushPending();
            return new(flushed, LineParseResult.Malformed(n, ParseIssue.OrphanLine, done));
        }

        public sealed record Counter(int Messages) : AdapterState;
    }
}
