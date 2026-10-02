using System.Text;
using Microsoft.Extensions.Logging;
using TalkPro.Core.Model;
using TalkPro.Core.Parsing;
using TalkPro.Infrastructure.Logging;
using TalkPro.Ingestion.Import;
using TalkPro.Ingestion.Synthetic;

namespace TalkPro.Privacy.Tests;

/// <summary>
/// Parser diagnostics may only reach logs as structured metadata (format id, line number, outcome,
/// issue, counts). Lines, message text, speaker names and paths must never be rendered.
/// </summary>
public sealed partial class ParserLoggingTests
{
    private const string SecretName = "SyntheticSecretSpeaker";
    private const string SecretText = "합성 비밀 본문 SECRET-BODY";

    private static readonly ParseContext Context = new()
    {
        TimeZone = TimeZoneInfo.CreateCustomTimeZone("TalkPro-Test+09", TimeSpan.FromHours(9), "Test +09", "Test +09"),
    };

    private readonly MemorySink _sink = new();
    private readonly ILogger _logger;

    public ParserLoggingTests()
    {
        using var provider = new ContentFreeLoggerProvider(_sink, TimeProvider.System);
        _logger = provider.CreateLogger("Parser");
    }

    [Fact]
    public void ParseSummaryRendersOnlyMetadata()
    {
        var chat = Parse();
        var issue = chat.Issues[0];

        Log.Summary(_logger, chat.FormatId, chat.Messages.Count, chat.Statistics.Malformed, chat.Statistics);
        Log.LineRejected(_logger, chat.FormatId, issue.LineNumber, issue.Outcome, issue.Issue);

        var lines = string.Join('\n', _sink.Lines);
        AssertNoContent(lines);
        Assert.Contains("synthetic.ios.v1", lines, StringComparison.Ordinal);
        Assert.Contains("messages=1", lines, StringComparison.Ordinal);
        Assert.Contains("malformed=2", lines, StringComparison.Ordinal);
        Assert.Contains("line=2 outcome=Malformed issue=InvalidTimestamp", lines, StringComparison.Ordinal);
        Assert.Contains("LineIssue", ContentFreeLogFormatter.RenderValue(issue), StringComparison.Ordinal);
    }

    [Fact]
    public void ContentBearingParserTypesAreRedacted()
    {
        var chat = Parse();
        var (state, _) = ParserState.Initial(Context).AdvanceLine().StartMessage(DateTimeOffset.UnixEpoch, SecretName, SecretText);
        var entry = new ParsedEntry(1, 1, DateTimeOffset.UnixEpoch, SecretName, MessageKind.Text, SecretText);

        object[] values = [state, entry, state.Pending!, chat, chat.Messages[0], chat.Participants[0], LineParseResult.Continuation(1), SecretText];

        Assert.All(values, value => Assert.StartsWith("[redacted:", ContentFreeLogFormatter.RenderValue(value), StringComparison.Ordinal));
    }

    [Fact]
    public void AccidentallyLoggedParserObjectsLeakNothing()
    {
        var chat = Parse();
        var (state, _) = ParserState.Initial(Context).AdvanceLine().StartMessage(DateTimeOffset.UnixEpoch, SecretName, SecretText);

        Log.Accidental(_logger, state, chat.Messages[0], "/home/SyntheticUser/exports/chat.txt");

        var line = Assert.Single(_sink.Lines);
        AssertNoContent(line);
        Assert.DoesNotContain("exports", line, StringComparison.Ordinal);
    }

    [Fact]
    public void ParserExceptionsAreLoggedWithoutMessage()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ChatExportParser.ParseLines([SecretText], new ThrowingAdapter(), Context, SourceEncoding.Utf8));

        Log.Failed(_logger, ex);

        var line = Assert.Single(_sink.Lines);
        AssertNoContent(line);
        Assert.Contains(typeof(InvalidOperationException).FullName!, line, StringComparison.Ordinal);
    }

    private static ParsedChat Parse()
    {
        var export = string.Join(
            "\n",
            "2026.10.01 15:15:07\t" + SecretName + "\t" + SecretText,
            "2026.13.01 15:15:07\t" + SecretName + "\t" + SecretText,
            "고아 " + SecretText);
        var result = new ChatExportParser(SyntheticAdapters.All).Parse(Encoding.UTF8.GetBytes(export), Context);
        return result.Chat!;
    }

    private static void AssertNoContent(string rendered)
    {
        Assert.DoesNotContain("SECRET", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("Secret", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("비밀", rendered, StringComparison.Ordinal);
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Parsed format={FormatId} messages={MessageCount} malformed={MalformedCount} {Statistics}")]
        public static partial void Summary(ILogger logger, ChatFormatId formatId, int messageCount, int malformedCount, ParseStatistics statistics);

        [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Rejected format={FormatId} line={LineNumber} outcome={Outcome} issue={Issue}")]
        public static partial void LineRejected(ILogger logger, ChatFormatId formatId, int lineNumber, LineOutcome outcome, ParseIssue issue);

        [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Accidental state={State} message={Message} path={Path}")]
        public static partial void Accidental(ILogger logger, ParserState state, ChatMessage message, string path);

        [LoggerMessage(EventId = 4, Level = LogLevel.Error, Message = "Parse failed")]
        public static partial void Failed(ILogger logger, Exception exception);
    }

    private sealed class ThrowingAdapter : IChatFormatAdapter
    {
        public ChatFormatId FormatId { get; } = new("test.throwing");

        public double Detect(IReadOnlyList<string> sampleLines) => 1;

        public ParserState CreateInitialState(ParseContext context) => ParserState.Initial(context);

        public LineStep ParseLine(ParserState state, string line) => throw new InvalidOperationException("Adapter bug while reading: " + line);
    }

    private sealed class MemorySink : ILogSink
    {
        public List<string> Lines { get; } = [];

        public void Write(string line) => Lines.Add(line);
    }
}
