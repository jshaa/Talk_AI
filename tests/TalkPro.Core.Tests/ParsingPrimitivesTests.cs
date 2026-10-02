using TalkPro.Core.Diagnostics;
using TalkPro.Core.Model;
using TalkPro.Core.Parsing;

namespace TalkPro.Core.Tests;

public sealed class ParsingPrimitivesTests
{
    [Theory]
    [InlineData("synthetic.windows.v1")]
    [InlineData("real-format.v2")]
    public void FormatIdsAcceptLowercaseAsciiDotsAndDashes(string value)
    {
        Assert.Equal(value, new ChatFormatId(value).ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Synthetic.Windows")]
    [InlineData("synthetic windows")]
    [InlineData("합성.v1")]
    public void FormatIdsRejectAnythingElse(string value)
    {
        Assert.ThrowsAny<ArgumentException>(() => new ChatFormatId(value));
    }

    [Fact]
    public void LogSafeTypesAreMarked()
    {
        Assert.True(typeof(ILogSafe).IsAssignableFrom(typeof(ChatFormatId)));
        Assert.True(typeof(ILogSafe).IsAssignableFrom(typeof(LineIssue)));
        Assert.True(typeof(ILogSafe).IsAssignableFrom(typeof(ParseStatistics)));

        // Content-bearing types must never be marked log-safe.
        Assert.False(typeof(ILogSafe).IsAssignableFrom(typeof(ParserState)));
        Assert.False(typeof(ILogSafe).IsAssignableFrom(typeof(ParsedEntry)));
        Assert.False(typeof(ILogSafe).IsAssignableFrom(typeof(PendingEntry)));
        Assert.False(typeof(ILogSafe).IsAssignableFrom(typeof(LineParseResult)));
    }

    [Fact]
    public void ContextRangeAndTimeZone()
    {
        var context = TestContext.Create();

        Assert.True(context.IsInRange(new DateOnly(1990, 1, 1)));
        Assert.True(context.IsInRange(new DateOnly(2100, 12, 31)));
        Assert.False(context.IsInRange(new DateOnly(1989, 12, 31)));
        Assert.False(context.IsInRange(new DateOnly(2101, 1, 1)));

        var timestamp = context.ToTimestamp(new DateTime(2026, 10, 1, 15, 15, 0, DateTimeKind.Local));
        Assert.Equal(TimeSpan.FromHours(9), timestamp.Offset);
        Assert.Equal(new DateTime(2026, 10, 1, 15, 15, 0), timestamp.DateTime);
    }

    [Fact]
    public void ContextDefaults()
    {
        var context = TestContext.Create();

        Assert.Equal(100_000, context.MaxLineLength);
        Assert.Equal(128, context.MaxSpeakerNameLength);
    }
}
