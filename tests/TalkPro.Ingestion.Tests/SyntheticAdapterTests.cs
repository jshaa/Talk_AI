using TalkPro.Core.Model;
using TalkPro.Core.Parsing;
using TalkPro.Ingestion.Import;
using TalkPro.Ingestion.Synthetic;

namespace TalkPro.Ingestion.Tests;

/// <summary>Line-level behaviour of the synthetic adapters (format rules in docs/SYNTHETIC_FORMATS.md).</summary>
public sealed class SyntheticAdapterTests
{
    private readonly ParseContext _context = TestTimeZone.Context();

    [Theory]
    [InlineData("[UserA] [15:15] 합성", LineOutcome.MessageStarted, ParseIssue.None)]
    [InlineData("[UserA] [15:15]", LineOutcome.MessageStarted, ParseIssue.None)]
    [InlineData("* [15:16] 알림", LineOutcome.SystemMessage, ParseIssue.None)]
    [InlineData("=== 2026-10-02 (Fri) ===", LineOutcome.Metadata, ParseIssue.None)]
    [InlineData("[UserA] [3:15] 합성", LineOutcome.Malformed, ParseIssue.InvalidTimestamp)]
    [InlineData("[UserA] [24:00] 합성", LineOutcome.Malformed, ParseIssue.InvalidTimestamp)]
    [InlineData("[*] [15:15] 합성", LineOutcome.Malformed, ParseIssue.InvalidSpeakerName)]
    [InlineData("[] [15:15] 합성", LineOutcome.Malformed, ParseIssue.InvalidSpeakerName)]
    [InlineData("[UserA] [15:15]합성", LineOutcome.Malformed, ParseIssue.IncompleteRecord)]
    [InlineData("=== 2026-10-02 ===", LineOutcome.Malformed, ParseIssue.IncompleteRecord)]
    [InlineData("=== 1989-12-31 (Sun) ===", LineOutcome.Malformed, ParseIssue.TimestampOutOfRange)]
    [InlineData("본문 줄", LineOutcome.Continuation, ParseIssue.None)]
    public void WindowsLines(string line, LineOutcome outcome, ParseIssue issue) =>
        AssertLine(new SyntheticWindowsAdapter(), ["TalkPro Synthetic Export (W1)", "", "=== 2026-10-01 (Thu) ===", "[UserA] [15:00] 열린 메시지"], line, outcome, issue);

    [Theory]
    [InlineData("2026-10-01 오후 3:15 | UserA | 합성", LineOutcome.MessageStarted, ParseIssue.None)]
    [InlineData("2026-10-01 오후 3:15 | UserA | ", LineOutcome.MessageStarted, ParseIssue.None)]
    [InlineData("2026-10-01 오후 3:15 * 알림", LineOutcome.SystemMessage, ParseIssue.None)]
    [InlineData("2026-10-01 오후 0:15 | UserA | 합성", LineOutcome.Malformed, ParseIssue.InvalidTimestamp)]
    [InlineData("2026-10-01 저녁 3:15 | UserA | 합성", LineOutcome.Malformed, ParseIssue.IncompleteRecord)]
    [InlineData("2026-10-01 오후 3:15 | * | 합성", LineOutcome.Malformed, ParseIssue.InvalidSpeakerName)]
    [InlineData("1989-12-31 오후 3:15 | UserA | 합성", LineOutcome.Malformed, ParseIssue.TimestampOutOfRange)]
    [InlineData("2026/10/01 오후 3:15 | UserA | 합성", LineOutcome.Continuation, ParseIssue.None)]
    public void AndroidLines(string line, LineOutcome outcome, ParseIssue issue) =>
        AssertLine(new SyntheticAndroidAdapter(), ["TalkPro Synthetic Export (A1)", "", "2026-10-01 오후 3:00 | UserA | 열린 메시지"], line, outcome, issue);

    [Theory]
    [InlineData("2026.10.01 15:15:07\tUserA\t합성", LineOutcome.MessageStarted, ParseIssue.None)]
    [InlineData("2026.10.01 15:15:07\t*\t알림", LineOutcome.SystemMessage, ParseIssue.None)]
    [InlineData("2026.10.01 15:15:60\tUserA\t합성", LineOutcome.Malformed, ParseIssue.InvalidTimestamp)]
    [InlineData("2026.10.01 15:15:07\tUserA", LineOutcome.Malformed, ParseIssue.IncompleteRecord)]
    [InlineData("2026.10.01 15:15:07\t \t합성", LineOutcome.Malformed, ParseIssue.InvalidSpeakerName)]
    [InlineData("\t2026.10.01 15:15:07\tUserA\t합성", LineOutcome.Continuation, ParseIssue.None)]
    public void IosLines(string line, LineOutcome outcome, ParseIssue issue) =>
        AssertLine(new SyntheticIosAdapter(), ["TalkPro Synthetic Export (I1)", "", "2026.10.01 15:00:00\tUserA\t열린 메시지"], line, outcome, issue);

    [Fact]
    public void KoreanTwelveHourClockMapsNoonAndMidnight()
    {
        string[] lines =
        [
            "2026-10-01 오전 12:05 | UserA | 자정 직후",
            "2026-10-01 오전 11:59 | UserA | 오전",
            "2026-10-01 오후 12:00 | UserA | 정오",
            "2026-10-01 오후 11:59 | UserA | 밤",
        ];

        var chat = ChatExportParser.ParseLines(lines, new SyntheticAndroidAdapter(), _context, SourceEncoding.Utf8);

        Assert.Equal(["00:05", "11:59", "12:00", "23:59"], chat.Messages.Select(m => m.Timestamp.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void InvalidDateMarkerClearsTheDateContext()
    {
        string[] lines = ["=== 2026-10-01 (Thu) ===", "=== 2026-02-30 (Mon) ===", "[UserA] [15:15] 날짜 없음"];

        var chat = ChatExportParser.ParseLines(lines, new SyntheticWindowsAdapter(), _context, SourceEncoding.Utf8);

        Assert.Equal([ParseIssue.InvalidTimestamp, ParseIssue.MissingDateContext], chat.Issues.Select(i => i.Issue));
        Assert.Empty(chat.Messages);
    }

    [Fact]
    public void MalformedRecordClosesTheOpenMessageAndLaterTextIsOrphaned()
    {
        string[] lines = ["2026.10.01 15:00:00\tUserA\t첫 줄", "둘째 줄", "2026.13.01 15:00:00\tUserA\t잘못됨", "고아 줄"];

        var chat = ChatExportParser.ParseLines(lines, new SyntheticIosAdapter(), _context, SourceEncoding.Utf8);

        Assert.Equal("첫 줄\n둘째 줄", Assert.Single(chat.Messages).Text);
        Assert.Equal([ParseIssue.InvalidTimestamp, ParseIssue.OrphanLine], chat.Issues.Select(i => i.Issue));
    }

    [Fact]
    public void UnknownVersionIsUnsupportedButBodyIsStillParsed()
    {
        string[] lines = ["TalkPro Synthetic Export (I2)", "", "2026.10.01 15:00:00\tUserA\t합성"];

        var chat = ChatExportParser.ParseLines(lines, new SyntheticIosAdapter(), _context, SourceEncoding.Utf8);

        var issue = Assert.Single(chat.Issues);
        Assert.Equal((LineOutcome.Unsupported, ParseIssue.UnsupportedVersion), (issue.Outcome, issue.Issue));
        Assert.Single(chat.Messages);
    }

    [Fact]
    public void HeaderEndsAtTheFirstRecordEvenWithoutBlankLine()
    {
        string[] lines = ["TalkPro Synthetic Export (I1)", "Room: 합성", "2026.10.01 15:00:00\tUserA\t합성"];

        var chat = ChatExportParser.ParseLines(lines, new SyntheticIosAdapter(), _context, SourceEncoding.Utf8);

        Assert.Equal(2, chat.Statistics.Metadata);
        Assert.Single(chat.Messages);
    }

    [Fact]
    public void SystemEventsAreClassified()
    {
        string[] lines =
        [
            "2026.10.01 15:00:00\t*\tUserA 님이 들어왔습니다.",
            "2026.10.01 15:00:01\t*\tUserA 님이 나갔습니다.",
            "2026.10.01 15:00:02\t*\t합성 알림",
        ];

        var chat = ChatExportParser.ParseLines(lines, new SyntheticIosAdapter(), _context, SourceEncoding.Utf8);

        Assert.Equal([SystemEventType.Join, SystemEventType.Leave, SystemEventType.Other], chat.Messages.Select(m => m.SystemEvent));
        Assert.All(chat.Messages, m => Assert.Null(m.SpeakerId));
        Assert.Empty(chat.Participants);
    }

    [Fact]
    public void DetectionScoresAreExclusiveAcrossFormats()
    {
        string[][] samples =
        [
            ["[UserA] [15:15] 합성"],
            ["2026-10-01 오후 3:15 | UserA | 합성"],
            ["2026.10.01 15:15:07\tUserA\t합성"],
        ];

        for (var i = 0; i < samples.Length; i++)
        {
            for (var j = 0; j < SyntheticAdapters.All.Count; j++)
            {
                var score = SyntheticAdapters.All[j].Detect(samples[i]);
                Assert.True(i == j ? score >= FormatDetector.MinimumConfidence : score == 0, $"sample {i}, adapter {j}: {score}");
            }
        }
    }

    [Fact]
    public void LargePathologicalLinesDoNotHang()
    {
        var pathological = "2026-10-01 오후 3:15 | " + string.Concat(Enumerable.Repeat(" |x", 30_000));

        var chat = ChatExportParser.ParseLines([pathological], new SyntheticAndroidAdapter(), _context, SourceEncoding.Utf8);

        Assert.Equal(1, chat.Statistics.TotalLines);
    }

    private void AssertLine(IChatFormatAdapter adapter, string[] prefix, string line, LineOutcome outcome, ParseIssue issue)
    {
        var state = adapter.CreateInitialState(_context);
        foreach (var previous in prefix)
        {
            state = adapter.ParseLine(state.AdvanceLine(), previous).State;
        }

        var step = adapter.ParseLine(state.AdvanceLine(), line);

        Assert.Equal((outcome, issue), (step.Result.Outcome, step.Result.Issue));
        Assert.Equal(prefix.Length + 1, step.Result.LineNumber);
        Assert.Equal(prefix.Length + 1, step.State.LineNumber);
        if (outcome is LineOutcome.Malformed or LineOutcome.SystemMessage or LineOutcome.MessageStarted or LineOutcome.Metadata)
        {
            // A record line always completes the open message.
            Assert.False(outcome == LineOutcome.Malformed && step.State.IsEntryOpen);
            Assert.Single(step.Result.Completed, e => e.Kind == MessageKind.Text);
        }
    }
}
