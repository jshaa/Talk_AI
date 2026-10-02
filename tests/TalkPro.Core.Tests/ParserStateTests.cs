using TalkPro.Core.Model;
using TalkPro.Core.Parsing;

namespace TalkPro.Core.Tests;

public sealed class ParserStateTests
{
    private static readonly DateTimeOffset Time = new(2026, 10, 1, 15, 15, 0, TimeSpan.FromHours(9));

    private readonly ParseContext _context = TestContext.Create();

    [Fact]
    public void InitialStateIsEmpty()
    {
        var state = ParserState.Initial(_context);

        Assert.Same(_context, state.Context);
        Assert.Equal(0, state.LineNumber);
        Assert.Equal(ParserSection.Header, state.Section);
        Assert.Null(state.CurrentDate);
        Assert.Null(state.Pending);
        Assert.Empty(state.SpeakerNames);
        Assert.Null(state.AdapterState);
        Assert.False(state.IsEntryOpen);
        Assert.False(state.IsMultiline);
    }

    [Fact]
    public void InitialStateRequiresContext()
    {
        Assert.Throws<ArgumentNullException>(() => ParserState.Initial(null!));
    }

    [Fact]
    public void AdvanceLineIncrementsWithoutMutating()
    {
        var initial = ParserState.Initial(_context);

        var first = initial.AdvanceLine();
        var second = first.AdvanceLine();

        Assert.Equal(0, initial.LineNumber);
        Assert.Equal(1, first.LineNumber);
        Assert.Equal(2, second.LineNumber);
    }

    [Fact]
    public void LinesMustBeStartedBeforeRecordsAreCreated()
    {
        var state = ParserState.Initial(_context);

        Assert.Throws<InvalidOperationException>(() => state.StartMessage(Time, "UserA", "text"));
        Assert.Throws<InvalidOperationException>(() => state.CompleteSystemEntry(Time, "text"));
    }

    [Fact]
    public void MultilineMessageTransitions()
    {
        var state = ParserState.Initial(_context).EnterBody().AdvanceLine();

        var (opened, completedOnStart) = state.StartMessage(Time, "UserA", "첫 줄");
        Assert.Empty(completedOnStart);
        Assert.True(opened.IsEntryOpen);
        Assert.False(opened.IsMultiline);

        var continued = opened.AdvanceLine().AppendContinuation("둘째 줄");
        Assert.True(continued.IsMultiline);
        Assert.Equal(1, continued.Pending!.StartLine);
        Assert.Equal(2, continued.Pending.EndLine);

        var (next, completed) = continued.AdvanceLine().StartMessage(Time, "User B", "다음 메시지");
        var entry = Assert.Single(completed);
        Assert.Equal("첫 줄\n둘째 줄", entry.Text);
        Assert.Equal((1, 2), (entry.StartLine, entry.EndLine));
        Assert.Equal("UserA", entry.SpeakerName);
        Assert.Equal(3, next.Pending!.StartLine);
        Assert.Equal(["User B", "UserA"], next.SpeakerNames.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TrailingBlankContinuationLinesAreDropped()
    {
        var (state, _) = ParserState.Initial(_context).AdvanceLine().StartMessage(Time, "UserA", "본문");
        state = state.AdvanceLine().AppendContinuation(string.Empty);
        state = state.AdvanceLine().AppendContinuation("가운데");
        state = state.AdvanceLine().AppendContinuation(string.Empty);
        state = state.AdvanceLine().AppendContinuation("   ");

        var (flushed, completed) = state.FlushPending();

        var entry = Assert.Single(completed);
        Assert.Equal("본문\n\n가운데", entry.Text);
        Assert.Equal(3, entry.EndLine);
        Assert.False(flushed.IsEntryOpen);
    }

    [Fact]
    public void EmptyAndWhitespaceOnlyFirstLinesAreKept()
    {
        var (empty, _) = ParserState.Initial(_context).AdvanceLine().StartMessage(Time, "UserA", string.Empty);
        var (spaces, _) = ParserState.Initial(_context).AdvanceLine().StartMessage(Time, "UserA", "   ");

        Assert.Equal(string.Empty, Assert.Single(empty.FlushPending().Completed).Text);
        Assert.Equal("   ", Assert.Single(spaces.FlushPending().Completed).Text);
    }

    [Fact]
    public void SystemEntryCompletesTheOpenMessageFirst()
    {
        var (open, _) = ParserState.Initial(_context).AdvanceLine().StartMessage(Time, "UserA", "메시지");

        var (state, completed) = open.AdvanceLine().CompleteSystemEntry(Time, "UserA 님이 나갔습니다.", SystemEventType.Leave);

        Assert.Equal(2, completed.Length);
        Assert.Equal(MessageKind.Text, completed[0].Kind);
        Assert.Equal(MessageKind.System, completed[1].Kind);
        Assert.Equal(SystemEventType.Leave, completed[1].SystemEvent);
        Assert.Null(completed[1].SpeakerName);
        Assert.False(state.IsEntryOpen);
    }

    [Fact]
    public void SystemEventDefaultsToOther()
    {
        var (_, completed) = ParserState.Initial(_context).AdvanceLine().CompleteSystemEntry(Time, "알림");

        Assert.Equal(SystemEventType.Other, Assert.Single(completed).SystemEvent);
    }

    [Fact]
    public void FlushWithoutOpenMessageCompletesNothing()
    {
        var state = ParserState.Initial(_context);

        var (flushed, completed) = state.FlushPending();

        Assert.Empty(completed);
        Assert.Same(state, flushed);
    }

    [Fact]
    public void NullAndEmptyInputPolicy()
    {
        var state = ParserState.Initial(_context).AdvanceLine();

        Assert.Throws<ArgumentNullException>(() => state.StartMessage(Time, null!, "text"));
        Assert.Throws<ArgumentException>(() => state.StartMessage(Time, string.Empty, "text"));
        Assert.Throws<ArgumentException>(() => state.StartMessage(Time, "   ", "text"));
        Assert.Throws<ArgumentNullException>(() => state.StartMessage(Time, "UserA", null!));
        Assert.Throws<ArgumentNullException>(() => state.CompleteSystemEntry(Time, null!));
        Assert.Throws<InvalidOperationException>(() => state.AppendContinuation("orphan"));

        var (open, _) = state.StartMessage(Time, "UserA", "text");
        Assert.Throws<ArgumentNullException>(() => open.AppendContinuation(null!));
        Assert.True(open.AppendContinuation(string.Empty).IsMultiline);
    }

    [Fact]
    public void DateContextCanBeSetAndCleared()
    {
        var date = new DateOnly(2026, 10, 1);
        var state = ParserState.Initial(_context);

        var dated = state.WithDate(date);
        var cleared = dated.ClearDate();

        Assert.Equal(date, dated.CurrentDate);
        Assert.Null(cleared.CurrentDate);
        Assert.Same(state, state.ClearDate());
    }

    [Fact]
    public void EnterBodyIsIdempotent()
    {
        var body = ParserState.Initial(_context).EnterBody();

        Assert.Equal(ParserSection.Body, body.Section);
        Assert.Same(body, body.EnterBody());
    }

    [Fact]
    public void AdapterStateIsTypedAndOwnedByOneAdapter()
    {
        var state = ParserState.Initial(_context, new CounterState(1));

        Assert.Equal(1, state.GetAdapterState<CounterState>().Count);
        Assert.Equal(2, state.WithAdapterState(new CounterState(2)).GetAdapterState<CounterState>().Count);
        Assert.Throws<InvalidOperationException>(() => state.GetAdapterState<OtherState>());
        Assert.Throws<InvalidOperationException>(() => ParserState.Initial(_context).GetAdapterState<CounterState>());
        Assert.Throws<ArgumentNullException>(() => state.WithAdapterState(null!));
    }

    [Fact]
    public void ToStringContainsNoTextOrSpeakerNames()
    {
        var (state, _) = ParserState.Initial(_context, new SecretState("ADAPTER-SECRET"))
            .AdvanceLine()
            .StartMessage(Time, "SECRET-NAME", "SECRET-TEXT");

        var rendered = string.Join(
            " | ",
            state.ToString(),
            state.Pending!.ToString(),
            state.AdapterState!.ToString(),
            _context.ToString());

        Assert.DoesNotContain("SECRET", rendered, StringComparison.Ordinal);
        Assert.Contains("EntryOpen = True", rendered, StringComparison.Ordinal);
        Assert.Equal(nameof(SecretState), state.AdapterState.ToString());
    }

    private sealed record CounterState(int Count) : AdapterState;

    private sealed record OtherState : AdapterState;

    private sealed record SecretState(string Value) : AdapterState;
}
