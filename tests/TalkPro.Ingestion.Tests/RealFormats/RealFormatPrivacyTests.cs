using System.Text;
using TalkPro.Core.Model;
using TalkPro.Core.Parsing;
using TalkPro.Ingestion.Import;
using TalkPro.Ingestion.RealFormats;

namespace TalkPro.Ingestion.Tests.RealFormats;

/// <summary>Real-format adapters and their results reveal no content through ToString, exceptions or reports.</summary>
public sealed class RealFormatPrivacyTests
{
    private const string SecretName = "SECRET-SENDER";
    private const string SecretText = "SECRET-BODY 합성 비밀";

    private static readonly ParseContext Context = TestTimeZone.Context();

    private static readonly string[] SecretExport =
    [
        "SECRET-ROOM 님과 카카오톡 대화",
        "저장한 날짜 : 2026-10-02 14:23:11",
        "",
        "--------------- 2026년 10월 1일 목요일 ---------------",
        "[" + SecretName + "] [오후 3:15] " + SecretText,
        SecretText + " continuation",
        "[" + SecretName + "] [오후 13:15] " + SecretText,
        "[" + new string('S', 200) + "SECRET] [오후 3:16] " + SecretText,
    ];

    [Fact]
    public void AdapterAndStateToStringRevealNothing()
    {
        var adapter = new WindowsChatFormatAdapter();
        var state = adapter.CreateInitialState(Context);
        var rendered = new List<string> { adapter.ToString()! };
        foreach (var line in SecretExport)
        {
            state = state.AdvanceLine();
            var step = adapter.ParseLine(state, line);
            rendered.Add(step.ToString());
            rendered.Add(step.State.ToString());
            rendered.AddRange(step.Result.Completed.Select(e => e.ToString()));
            state = step.State;
        }

        rendered.Add(state.Pending?.ToString() ?? string.Empty);
        AssertNoSecret(string.Join('\n', rendered));
    }

    [Fact]
    public void ImportResultsRevealNothing()
    {
        var result = new ChatExportParser(RealFormatAdapters.All).Parse(Encoding.UTF8.GetBytes(string.Join("\r\n", SecretExport)), Context);
        var chat = result.Chat!;

        Assert.Equal(2, chat.Issues.Count);
        AssertNoSecret(string.Join(
            '\n',
            result.ToString(),
            chat.ToString(),
            chat.Statistics.ToString(),
            string.Join(',', chat.Issues),
            string.Join(',', chat.Messages),
            string.Join(',', chat.Participants)));
    }

    [Fact]
    public void ValidationReportRevealsOnlyMetadata()
    {
        var bytes = Encoding.UTF8.GetBytes(string.Join("\n", SecretExport));
        var result = new ChatExportParser(RealFormatAdapters.All).Parse(bytes, Context);

        var report = RealExportReport.Describe(1, new byte[32], result);

        AssertNoSecret(report);
        Assert.Contains("format=kakaotalk.windows.ko.v1", report, StringComparison.Ordinal);
        Assert.Contains("line 7: Malformed InvalidTimestamp", report, StringComparison.Ordinal);
        Assert.Contains("line 8: Malformed SpeakerNameTooLong", report, StringComparison.Ordinal);
    }

    [Fact]
    public void ParserInfrastructureExceptionsContainNoRawLine()
    {
        var adapter = new IosChatFormatAdapter();

        var notAdvanced = Assert.Throws<ArgumentException>(() => adapter.ParseLine(ParserState.Initial(Context), SecretText));
        var violation = Assert.Throws<InvalidOperationException>(() =>
            ChatExportParser.ParseLines([SecretText], new LineSkippingAdapter(), Context, SourceEncoding.Utf8));

        AssertNoSecret(notAdvanced.Message);
        AssertNoSecret(violation.Message);
    }

    [Fact]
    public void FailedDetectionReturnsOnlyAStatus()
    {
        var result = new ChatExportParser(RealFormatAdapters.All).Parse(Encoding.UTF8.GetBytes(SecretText + "\n" + SecretName), Context);

        Assert.Equal(ImportStatus.UnknownFormat, result.Status);
        Assert.Null(result.Chat);
        AssertNoSecret(result.ToString());
    }

    private static void AssertNoSecret(string rendered)
    {
        Assert.DoesNotContain("SECRET", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("비밀", rendered, StringComparison.Ordinal);
    }

    private sealed class LineSkippingAdapter : IChatFormatAdapter
    {
        public ChatFormatId FormatId { get; } = new("test.skipping");

        public double Detect(IReadOnlyList<string> sampleLines) => 1;

        public ParserState CreateInitialState(ParseContext context) => ParserState.Initial(context);

        public LineStep ParseLine(ParserState state, string line) => new(state.AdvanceLine(), LineParseResult.Ignored(state.LineNumber + 1));
    }
}
