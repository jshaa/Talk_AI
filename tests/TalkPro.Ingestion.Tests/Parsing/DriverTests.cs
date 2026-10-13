using System.Text;
using System.Text.RegularExpressions;
using TalkPro.Core.Model;
using TalkPro.Core.Parsing;
using TalkPro.Ingestion.Formats.Synthetic;
using TalkPro.Ingestion.Parsing;
using TalkPro.Ingestion.Tests.GoldenTests;

namespace TalkPro.Ingestion.Tests.Parsing;

public sealed class DriverTests
{
    private static readonly ParseContext Context = GoldenData.Context;

    [Theory]
    [InlineData("a\r\nb\r\n", new[] { "a", "b" })]
    [InlineData("a\nb", new[] { "a", "b" })]
    [InlineData("a\rb\r\n\r\nc", new[] { "a", "b", "", "c" })]
    [InlineData("\n", new[] { "" })]
    [InlineData("", new string[0])]
    public void LineSplitterFollowsReadLineRules(string text, string[] expected)
    {
        Assert.Equal(expected, LineSplitter.Split(text));
    }

    [Fact]
    public void DecodingRecognisesBomUtf8AndCp949()
    {
        Assert.True(TextDecoding.TryDecode([0xEF, 0xBB, 0xBF, 0x41], out var bom));
        Assert.Equal(new DecodedText(SourceEncoding.Utf8WithBom, "A"), bom);

        Assert.True(TextDecoding.TryDecode("합성"u8, out var utf8));
        Assert.Equal(SourceEncoding.Utf8, utf8.Encoding);

        Assert.True(TextDecoding.TryDecode(GoldenData.Bytes("w1/single-message.cp949.txt"), out var cp949));
        Assert.Equal(SourceEncoding.Cp949, cp949.Encoding);
        Assert.Contains("합성 테스트 메시지 001", cp949.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void DecodingRejectsUtf16AndInvalidBytes()
    {
        Assert.False(TextDecoding.TryDecode([0xFF, 0xFE, 0x41, 0x00], out _));
        Assert.False(TextDecoding.TryDecode([0xEF, 0xBB, 0xBF, 0xC3, 0x28], out _));
        Assert.False(TextDecoding.TryDecode([0x81, 0x20], out _)); // CP949 lead byte + invalid trail byte
        Assert.False(TextDecoding.TryDecode([0x41, 0xB0], out _)); // CP949 lead byte truncated at end of input
    }

    [Fact]
    public void Cp949SupportDoesNotRegisterAProcessWideProvider()
    {
        Assert.True(TextDecoding.TryDecode(GoldenData.Bytes("i1/conversation.cp949.txt"), out _));

        Assert.Throws<NotSupportedException>(() => Encoding.GetEncoding(949));
    }

    [Fact]
    public void AdapterThatChangesTheLineNumberIsRejected()
    {
        var parser = new ChatExportParser([new LineNumberTamperingAdapter()]);

        Assert.Throws<InvalidOperationException>(() => parser.Parse("x\ny"u8, Context));
    }

    [Fact]
    public void RegexTimeoutBecomesAMalformedLine()
    {
        var parser = new ChatExportParser([new TimeoutAdapter()]);

        var chat = parser.Parse("x\nslow\ny"u8, Context).Chat!;

        var issue = Assert.Single(chat.Issues);
        Assert.Equal(2, issue.LineNumber);
        Assert.Equal(ParseIssue.PatternTimeout, issue.Issue);
    }

    [Fact]
    public void NearTiedDetectionIsAmbiguous()
    {
        var parser = new ChatExportParser([new FixedScoreAdapter("fake.a", 0.8), new FixedScoreAdapter("fake.b", 0.75)]);

        Assert.Equal(ParseFailure.AmbiguousFormat, parser.Parse("x"u8, Context).Failure);
    }

    [Fact]
    public void WeakDetectionIsUnknown()
    {
        var parser = new ChatExportParser([new FixedScoreAdapter("fake.a", 0.4)]);

        Assert.Equal(ParseFailure.UnknownFormat, parser.Parse("x"u8, Context).Failure);
    }

    [Fact]
    public void NoRegisteredAdaptersMeansUnknownFormat()
    {
        Assert.Equal(ParseFailure.UnknownFormat, new ChatExportParser([]).Parse(GoldenData.Bytes("w1/conversation.utf8.txt"), Context).Failure);
    }

    [Theory]
    [InlineData("w1")]
    [InlineData("a1")]
    [InlineData("i1")]
    public void AdaptersAreIndependentAndDoNotClaimOtherFormats(string folder)
    {
        var lines = LineSplitter.Split(Encoding.UTF8.GetString(GoldenData.Bytes($"{folder}/headerless.utf8.txt")));
        var owner = GoldenData.Entry($"{folder}/headerless.utf8.txt").Format;

        foreach (var adapter in SyntheticFormats.All)
        {
            var score = adapter.Detect(lines);
            if (adapter.FormatId.Value == owner)
            {
                Assert.True(score >= ChatExportParser.MinimumDetectionScore, $"{adapter.FormatId}: {score}");
            }
            else
            {
                Assert.True(score < ChatExportParser.MinimumDetectionScore, $"{adapter.FormatId} claimed {owner}: {score}");
            }
        }
    }

    [Fact]
    public void UnknownSignatureVersionIsUnsupported()
    {
        var adapter = new SyntheticWindowsAdapter();
        var state = adapter.CreateInitialState(Context).AdvanceLine();

        var step = adapter.ParseLine(state, "TalkPro Synthetic Export (W9)");

        Assert.Equal(LineOutcome.Unsupported, step.Result.Outcome);
        Assert.Equal(ParseIssue.UnsupportedVersion, step.Result.Issue);
        Assert.Equal(0, adapter.Detect(["TalkPro Synthetic Export (W9)"]));
    }

    [Fact]
    public void AdapterNullAndEmptyInputPolicy()
    {
        var adapter = new SyntheticAndroidAdapter();
        var fresh = adapter.CreateInitialState(Context);

        Assert.Throws<InvalidOperationException>(() => adapter.ParseLine(fresh, "x"));
        Assert.Throws<ArgumentNullException>(() => adapter.ParseLine(fresh.AdvanceLine(), null!));
        Assert.Equal(LineOutcome.Ignored, adapter.ParseLine(fresh.AdvanceLine(), string.Empty).Result.Outcome);
        Assert.Equal(0, adapter.Detect([]));
    }

    [Fact]
    public void Android12HourClockBoundaries()
    {
        var adapter = new SyntheticAndroidAdapter();
        var state = adapter.CreateInitialState(Context).AdvanceLine();

        var midnight = adapter.ParseLine(state, "2026-02-02 오전 12:30 | UserA | x");
        var noon = adapter.ParseLine(state, "2026-02-01 오후 12:00 | UserA | x");

        Assert.Equal(0, midnight.State.Pending!.Timestamp.Hour);
        Assert.Equal(12, noon.State.Pending!.Timestamp.Hour);
    }

    private sealed class LineNumberTamperingAdapter : IChatFormatAdapter
    {
        public ChatFormatId FormatId { get; } = new("fake.tamper");

        public double Detect(IReadOnlyList<string> sampleLines) => 1;

        public ParserState CreateInitialState(ParseContext context) => ParserState.Initial(context);

        public LineStep ParseLine(ParserState state, string line) => new(state.AdvanceLine(), LineParseResult.Ignored(state.LineNumber));
    }

    private sealed class TimeoutAdapter : IChatFormatAdapter
    {
        public ChatFormatId FormatId { get; } = new("fake.timeout");

        public double Detect(IReadOnlyList<string> sampleLines) => 1;

        public ParserState CreateInitialState(ParseContext context) => ParserState.Initial(context);

        public LineStep ParseLine(ParserState state, string line) =>
            line == "slow"
                ? throw new RegexMatchTimeoutException("synthetic", "pattern", TimeSpan.FromMilliseconds(1))
                : new(state, LineParseResult.Ignored(state.LineNumber));
    }

    private sealed class FixedScoreAdapter(string id, double score) : IChatFormatAdapter
    {
        public ChatFormatId FormatId { get; } = new(id);

        public double Detect(IReadOnlyList<string> sampleLines) => score;

        public ParserState CreateInitialState(ParseContext context) => ParserState.Initial(context);

        public LineStep ParseLine(ParserState state, string line) => new(state, LineParseResult.Ignored(state.LineNumber));
    }
}
