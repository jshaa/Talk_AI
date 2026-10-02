using System.Collections.Immutable;
using TalkPro.Core.Model;
using TalkPro.Core.Parsing;

namespace TalkPro.Core.Tests;

public sealed class LineParseResultTests
{
    private const string Secret = "합성비밀문장 SECRET-TEXT";

    [Fact]
    public void FactoriesProduceTheMatchingOutcomeWithoutIssue()
    {
        Assert.Equal(LineOutcome.MessageStarted, LineParseResult.MessageStarted(1, []).Outcome);
        Assert.Equal(LineOutcome.Continuation, LineParseResult.Continuation(2).Outcome);
        Assert.Equal(LineOutcome.SystemMessage, LineParseResult.SystemMessage(3, []).Outcome);
        Assert.Equal(LineOutcome.Metadata, LineParseResult.Metadata(4, []).Outcome);
        Assert.Equal(LineOutcome.Ignored, LineParseResult.Ignored(5).Outcome);

        Assert.All(
            new[] { LineParseResult.MessageStarted(1, []), LineParseResult.Continuation(1), LineParseResult.Ignored(1) },
            r => Assert.Equal(ParseIssue.None, r.Issue));
    }

    [Theory]
    [InlineData(ParseIssue.InvalidTimestamp)]
    [InlineData(ParseIssue.OrphanLine)]
    [InlineData(ParseIssue.NullCharacter)]
    public void MalformedCarriesItsIssue(ParseIssue issue)
    {
        var result = LineParseResult.Malformed(7, issue, []);

        Assert.Equal(LineOutcome.Malformed, result.Outcome);
        Assert.Equal(issue, result.Issue);
        Assert.Equal(7, result.LineNumber);
    }

    [Fact]
    public void MalformedAndUnsupportedRequireAnIssue()
    {
        Assert.Throws<ArgumentException>(() => LineParseResult.Malformed(1, ParseIssue.None, []));
        Assert.Throws<ArgumentException>(() => LineParseResult.Unsupported(1, ParseIssue.None, []));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void LineNumbersStartAtOne(int lineNumber)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LineParseResult.Ignored(lineNumber));
    }

    [Fact]
    public void DefaultCompletedArrayIsNormalisedToEmpty()
    {
        var result = LineParseResult.MessageStarted(1, default);

        Assert.True(result.Completed.IsEmpty);
        Assert.False(result.CompletesEntries);
    }

    [Fact]
    public void CompletedEntriesAreExposedAsMessageCompletedEvents()
    {
        ImmutableArray<ParsedEntry> completed = [Entry(Secret)];

        var result = LineParseResult.SystemMessage(9, completed);

        Assert.True(result.CompletesEntries);
        Assert.Equal(Secret, Assert.Single(result.Completed).Text);
    }

    [Fact]
    public void ResultsAreValueEqualAndImmutable()
    {
        Assert.Equal(LineParseResult.Continuation(3), LineParseResult.Continuation(3));
        Assert.All(
            typeof(LineParseResult).GetProperties(),
            p => Assert.True(p.SetMethod is null || p.SetMethod.ReturnParameter.GetRequiredCustomModifiers().Length > 0, p.Name));
    }

    [Fact]
    public void ToStringContainsNoContent()
    {
        var result = LineParseResult.Malformed(4, ParseIssue.InvalidSpeakerName, [Entry(Secret)]);
        var step = new LineStep(ParserState.Initial(TestContext.Create()), result);

        Assert.DoesNotContain("SECRET", result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET", step.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET", Entry(Secret).ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("UserSecretName", Entry(Secret).ToString(), StringComparison.Ordinal);
        Assert.Contains("InvalidSpeakerName", result.ToString(), StringComparison.Ordinal);
    }

    private static ParsedEntry Entry(string text) =>
        new(1, 1, DateTimeOffset.UnixEpoch, "UserSecretName", MessageKind.Text, text);
}
