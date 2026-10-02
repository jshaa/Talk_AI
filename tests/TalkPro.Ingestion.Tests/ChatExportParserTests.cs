using System.Text;
using TalkPro.Core.Model;
using TalkPro.Core.Parsing;
using TalkPro.Ingestion.Import;
using TalkPro.Ingestion.Synthetic;

namespace TalkPro.Ingestion.Tests;

public sealed class ChatExportParserTests
{
    private readonly ParseContext _context = TestTimeZone.Context();

    [Fact]
    public void EmptyInputIsReportedAsSuch()
    {
        var parser = new ChatExportParser(SyntheticAdapters.All);

        Assert.Equal(ImportStatus.EmptyInput, parser.Parse([], _context).Status);
        Assert.Equal(ImportStatus.EmptyInput, ChatExportParser.Parse([], _context, new SyntheticIosAdapter()).Status);
    }

    [Fact]
    public void UnrecognisedContentIsUnknownFormat()
    {
        var parser = new ChatExportParser(SyntheticAdapters.All);

        var result = parser.Parse(Encoding.UTF8.GetBytes("합성 아무 형식도 아닌 줄\r\n둘째 줄\r\n"), _context);

        Assert.Equal(ImportStatus.UnknownFormat, result.Status);
        Assert.Null(result.Chat);
    }

    [Fact]
    public void UndecodableInputIsUnsupportedEncoding()
    {
        var parser = new ChatExportParser(SyntheticAdapters.All);

        Assert.Equal(ImportStatus.UnsupportedEncoding, parser.Parse([0xB0, 0x0A, 0x81], _context).Status);
    }

    [Fact]
    public void BytesThatTheCp949TableMapsStillEndAsUnknownFormat()
    {
        var parser = new ChatExportParser(SyntheticAdapters.All);

        // .NET's CP949 table maps some non-text bytes (e.g. 0xFF) without loss; detection then rejects the content.
        Assert.Equal(ImportStatus.UnknownFormat, parser.Parse([0xFF, 0xFE, 0xFD], _context).Status);
    }

    [Fact]
    public void DetectionUsesContentNotFileNames()
    {
        var parser = new ChatExportParser(SyntheticAdapters.All);
        string[] headerless = ["2026.10.01 15:15:07\tUserA\t합성 테스트 메시지 001"];

        var result = parser.Parse(Encoding.UTF8.GetBytes(string.Join("\n", headerless)), _context);

        Assert.Equal(SyntheticIosAdapter.Id, result.Chat!.FormatId);
    }

    [Fact]
    public void TiedDetectionIsRejected()
    {
        var detected = FormatDetector.Select([new FixedScoreAdapter("test.a", 0.9), new FixedScoreAdapter("test.b", 0.9)], ["x"], _context);

        Assert.Null(detected);
    }

    [Fact]
    public void LowConfidenceIsRejected()
    {
        Assert.Null(FormatDetector.Select([new FixedScoreAdapter("test.a", 0.49)], ["x"], _context));
        Assert.NotNull(FormatDetector.Select([new FixedScoreAdapter("test.a", 0.5)], ["x"], _context));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void InvalidConfidenceIsAContractViolation(double score)
    {
        Assert.Throws<InvalidOperationException>(() => FormatDetector.Select([new FixedScoreAdapter("test.a", score)], ["x"], _context));
    }

    [Fact]
    public void DriverGuardsRejectLongLinesAndNulBeforeTheAdapter()
    {
        var context = _context with { MaxLineLength = 40 };
        string[] lines =
        [
            "2026.10.01 15:15:07\tUserA\t열린 메시지",
            "2026.10.01 15:15:08\tUserA\t" + new string('x', 40),
            "널\0문자",
        ];

        var chat = ChatExportParser.ParseLines(lines, new SyntheticIosAdapter(), context, SourceEncoding.Utf8);

        Assert.Equal(
            [(2, ParseIssue.LineTooLong), (3, ParseIssue.NullCharacter)],
            chat.Issues.Select(i => (i.LineNumber, i.Issue)));
        Assert.Equal("열린 메시지", Assert.Single(chat.Messages).Text);
    }

    [Fact]
    public void AdapterThatChangesTheLineNumberIsRejected()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ChatExportParser.ParseLines(["SECRET-LINE"], new LineSkippingAdapter(), _context, SourceEncoding.Utf8));

        Assert.DoesNotContain("SECRET", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParserRequiresUniqueAdapters()
    {
        Assert.Throws<ArgumentException>(() => new ChatExportParser([]));
        Assert.Throws<ArgumentException>(() => new ChatExportParser([new SyntheticIosAdapter(), new SyntheticIosAdapter()]));
    }

    [Fact]
    public void NullInputsAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => ChatExportParser.ParseLines(null!, new SyntheticIosAdapter(), _context, SourceEncoding.Utf8));
        Assert.Throws<ArgumentNullException>(() => ChatExportParser.ParseLines([null!], new SyntheticIosAdapter(), _context, SourceEncoding.Utf8));
        Assert.Throws<ArgumentNullException>(() => new SyntheticIosAdapter().ParseLine(ParserState.Initial(_context).AdvanceLine(), null!));
        Assert.Throws<ArgumentException>(() => new SyntheticIosAdapter().ParseLine(ParserState.Initial(_context), "line"));
        Assert.Equal(0, new SyntheticIosAdapter().Detect([]));
    }

    [Fact]
    public void ResultToStringContainsNoContent()
    {
        var parser = new ChatExportParser(SyntheticAdapters.All);
        var bytes = Encoding.UTF8.GetBytes(
            "2026.10.01 15:15:07\tSECRET-NAME\tSECRET-TEXT\n2026.13.01 15:15:07\tSECRET-NAME\tSECRET-BAD\n고아 SECRET-ORPHAN");

        var result = parser.Parse(bytes, _context);
        var rendered = string.Join(
            " | ",
            result.ToString(),
            result.Chat!.ToString(),
            result.Chat.Statistics.ToString(),
            string.Join(",", result.Chat.Issues),
            string.Join(",", result.Chat.Messages),
            string.Join(",", result.Chat.Participants));

        Assert.DoesNotContain("SECRET", rendered, StringComparison.Ordinal);
        Assert.Contains("Malformed", rendered, StringComparison.Ordinal);
    }

    private sealed class FixedScoreAdapter(string id, double score) : IChatFormatAdapter
    {
        public ChatFormatId FormatId { get; } = new(id);

        public double Detect(IReadOnlyList<string> sampleLines) => score;

        public ParserState CreateInitialState(ParseContext context) => ParserState.Initial(context);

        public LineStep ParseLine(ParserState state, string line) => new(state, LineParseResult.Ignored(state.LineNumber));
    }

    private sealed class LineSkippingAdapter : IChatFormatAdapter
    {
        public ChatFormatId FormatId { get; } = new("test.skipping");

        public double Detect(IReadOnlyList<string> sampleLines) => 1;

        public ParserState CreateInitialState(ParseContext context) => ParserState.Initial(context);

        public LineStep ParseLine(ParserState state, string line)
        {
            var skipped = state.AdvanceLine();
            return new(skipped, LineParseResult.Ignored(skipped.LineNumber));
        }
    }
}
