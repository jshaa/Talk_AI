using System.Collections.Immutable;
using TalkPro.Core.Model;
using TalkPro.Core.Parsing;

namespace TalkPro.Ingestion.Tests.Contracts;

public sealed class ParserContractTests
{
    private const string Secret = "합성 비밀 문장 SyntheticSecret";
    private const string SecretName = "SyntheticSecretName";

    private static readonly ParseContext Context = new() { TimeZone = TimeZoneInfo.Utc };
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void InitialStateIsEmpty()
    {
        var state = ParserState.Initial(Context);

        Assert.Equal(0, state.LineNumber);
        Assert.Equal(ParserSection.Header, state.Section);
        Assert.Null(state.CurrentDate);
        Assert.Null(state.Pending);
        Assert.False(state.IsEntryOpen);
        Assert.False(state.IsMultiline);
        Assert.Empty(state.SpeakerNames);
        Assert.Null(state.AdapterState);
        Assert.Same(Context, state.Context);
    }

    [Fact]
    public void AdvanceLineIncrementsByOneAndIsImmutable()
    {
        var initial = ParserState.Initial(Context);

        var first = initial.AdvanceLine();
        var second = first.AdvanceLine();

        Assert.Equal(0, initial.LineNumber);
        Assert.Equal(1, first.LineNumber);
        Assert.Equal(2, second.LineNumber);
    }

    [Fact]
    public void ProcessingBeforeTheFirstLineIsRejected()
    {
        Assert.Throws<InvalidOperationException>(() => ParserState.Initial(Context).StartMessage(T0, "UserA", "x"));
    }

    [Fact]
    public void MultilineTransitionsAndCompletion()
    {
        var (started, completedOnStart) = ParserState.Initial(Context).AdvanceLine().StartMessage(T0, "UserA", "첫 줄");
        Assert.Empty(completedOnStart);
        Assert.True(started.IsEntryOpen);
        Assert.False(started.IsMultiline);

        var continued = started.AdvanceLine().AppendContinuation(string.Empty).AdvanceLine().AppendContinuation("셋째 줄").AdvanceLine().AppendContinuation(string.Empty);
        Assert.True(continued.IsMultiline);

        var (flushed, completed) = continued.FlushPending();
        var entry = Assert.Single(completed);
        Assert.False(flushed.IsEntryOpen);
        Assert.Equal("첫 줄\n\n셋째 줄", entry.Text);
        Assert.Equal(1, entry.StartLine);
        Assert.Equal(3, entry.EndLine);
        Assert.Equal("UserA", entry.SpeakerName);
        Assert.Equal(MessageKind.Text, entry.Kind);
    }

    [Fact]
    public void StartingAMessageCompletesTheOpenOne()
    {
        var (open, _) = ParserState.Initial(Context).AdvanceLine().StartMessage(T0, "UserA", "a");

        var (next, completed) = open.AdvanceLine().StartMessage(T0, "User B", "b");

        Assert.Equal("a", Assert.Single(completed).Text);
        Assert.Equal(2, next.Pending!.StartLine);
        Assert.Equal(2, next.SpeakerNames.Count);
    }

    [Fact]
    public void SystemEntryCompletesOpenMessageAndItself()
    {
        var (open, _) = ParserState.Initial(Context).AdvanceLine().StartMessage(T0, "UserA", "a");

        var (next, completed) = open.AdvanceLine().CompleteSystemEntry(T0, "UserC 님이 들어왔습니다.");

        Assert.Equal(2, completed.Length);
        Assert.Equal(MessageKind.Text, completed[0].Kind);
        Assert.Equal(MessageKind.System, completed[1].Kind);
        Assert.Null(completed[1].SpeakerName);
        Assert.False(next.IsEntryOpen);
    }

    [Fact]
    public void ContinuationWithoutOpenMessageIsRejected()
    {
        Assert.Throws<InvalidOperationException>(() => ParserState.Initial(Context).AdvanceLine().AppendContinuation("x"));
    }

    [Fact]
    public void NullInputsAreRejected()
    {
        var state = ParserState.Initial(Context).AdvanceLine();

        Assert.Throws<ArgumentNullException>(() => ParserState.Initial(null!));
        Assert.Throws<ArgumentNullException>(() => state.StartMessage(T0, "UserA", null!));
        Assert.Throws<ArgumentException>(() => state.StartMessage(T0, " ", "x"));
    }

    [Fact]
    public void AdapterStateIsTypedAndIsolated()
    {
        var state = ParserState.Initial(Context, new FakeAdapterState(Secret));

        Assert.Equal(Secret, state.GetAdapterState<FakeAdapterState>().Buffer);
        Assert.Throws<InvalidOperationException>(() => state.GetAdapterState<OtherAdapterState>());
    }

    [Fact]
    public void ResultFactoriesProduceTheRequestedOutcome()
    {
        Assert.Equal(LineOutcome.MessageStarted, LineParseResult.MessageStarted(1, []).Outcome);
        Assert.Equal(LineOutcome.Continuation, LineParseResult.Continuation(1).Outcome);
        Assert.Equal(LineOutcome.SystemMessage, LineParseResult.SystemMessage(1, []).Outcome);
        Assert.Equal(LineOutcome.Metadata, LineParseResult.Metadata(1, []).Outcome);
        Assert.Equal(LineOutcome.Ignored, LineParseResult.Ignored(1).Outcome);

        var malformed = LineParseResult.Malformed(3, ParseIssue.InvalidTimestamp, []);
        Assert.Equal(LineOutcome.Malformed, malformed.Outcome);
        Assert.Equal(ParseIssue.InvalidTimestamp, malformed.Issue);
        Assert.Equal(3, malformed.LineNumber);
        Assert.Equal(LineOutcome.Unsupported, LineParseResult.Unsupported(1, ParseIssue.UnsupportedVersion, []).Outcome);
    }

    [Fact]
    public void ResultIssueRulesAreEnforced()
    {
        Assert.Throws<ArgumentException>(() => LineParseResult.Malformed(1, ParseIssue.None, []));
        Assert.Throws<ArgumentException>(() => LineParseResult.Unsupported(1, ParseIssue.None, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => LineParseResult.Ignored(0));
        Assert.Empty(LineParseResult.MessageStarted(1, default).Completed);
    }

    [Fact]
    public void ResultExposesCompletedEntries()
    {
        var entry = new ParsedEntry(1, 1, T0, "UserA", MessageKind.Text, "x");

        var result = LineParseResult.MessageStarted(2, [entry]);

        Assert.True(result.CompletesEntries);
        Assert.Same(entry, Assert.Single(result.Completed));
    }

    [Fact]
    public void ToStringNeverExposesTextOrNames()
    {
        var entry = new ParsedEntry(1, 2, T0, SecretName, MessageKind.Text, Secret);
        var (state, _) = ParserState.Initial(Context, new FakeAdapterState(Secret)).AdvanceLine().StartMessage(T0, SecretName, Secret);
        var chat = new ParsedChat(
            new ChatFormatId("synthetic.windows.v1"),
            SourceEncoding.Utf8,
            [new Participant(new ParticipantId("p_01"), SecretName, ParticipantConsent.ContextOnly)],
            [new ChatMessage(new MessageId("m0000001"), T0, new ParticipantId("p_01"), MessageKind.Text, Secret, 1)],
            [new LineIssue(2, LineOutcome.Malformed, ParseIssue.OrphanLine)],
            new ParseStatistics(2, 1, 0, 0, 0, 0, 1, 0));

        object[] values =
        [
            entry,
            state,
            state.Pending!,
            state.AdapterState!,
            LineParseResult.MessageStarted(2, [entry]),
            new LineStep(state, LineParseResult.Continuation(1)),
            chat,
        ];

        Assert.All(values, v =>
        {
            var text = v.ToString()!;
            Assert.DoesNotContain("SyntheticSecret", text, StringComparison.Ordinal);
            Assert.DoesNotContain("비밀", text, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void FormatIdsAreRestrictedToSafeCharacters()
    {
        Assert.Equal("synthetic.ios.v1", new ChatFormatId("synthetic.ios.v1").ToString());
        Assert.Throws<ArgumentException>(() => new ChatFormatId("Synthetic iOS"));
        Assert.Throws<ArgumentException>(() => new ChatFormatId(" "));
    }

    [Fact]
    public void ContextEnforcesTheDateRange()
    {
        Assert.True(Context.IsInRange(new DateOnly(2026, 10, 1)));
        Assert.False(Context.IsInRange(new DateOnly(1989, 12, 31)));
        Assert.False(Context.IsInRange(new DateOnly(2999, 1, 1)));
    }

    private sealed record FakeAdapterState(string Buffer) : AdapterState;

    private sealed record OtherAdapterState : AdapterState;
}
