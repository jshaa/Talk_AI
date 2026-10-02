using System.Text;
using Microsoft.Extensions.Logging;
using TalkPro.Core.Model;
using TalkPro.Core.Parsing;
using TalkPro.Infrastructure.Logging;
using TalkPro.Ingestion.Import;
using TalkPro.Ingestion.RealFormats;

namespace TalkPro.Privacy.Tests;

/// <summary>Results of the real-format adapters reach logs only as structured metadata.</summary>
public sealed partial class RealFormatLoggingTests
{
    private const string SecretName = "SECRET-SENDER";
    private const string SecretText = "SECRET-BODY 합성 비밀";
    private const string SecretPath = "/home/SECRET-USER/private-exports/SECRET-ROOM.txt";

    private static readonly ParseContext Context = new()
    {
        TimeZone = TimeZoneInfo.CreateCustomTimeZone("TalkPro-Test+09", TimeSpan.FromHours(9), "Test +09", "Test +09"),
    };

    private readonly MemorySink _sink = new();
    private readonly ILogger _logger;

    public RealFormatLoggingTests()
    {
        using var provider = new ContentFreeLoggerProvider(_sink, TimeProvider.System);
        _logger = provider.CreateLogger("Import");
    }

    [Theory]
    [InlineData("windows")]
    [InlineData("android")]
    [InlineData("ios")]
    public void EverythingAPlatformProducesIsRedactedOrMetadata(string platform)
    {
        var chat = Parse(platform);
        var (state, _) = ParserState.Initial(Context).AdvanceLine().StartMessage(DateTimeOffset.UnixEpoch, SecretName, SecretText);
        var issue = chat.Issues[0];

        Log.Summary(_logger, chat.FormatId, chat.Messages.Count, chat.Statistics.Malformed, chat.Participants.Count);
        Log.Issue(_logger, chat.FormatId, issue.LineNumber, issue.Outcome, issue.Issue);
        Log.Accidental(_logger, chat.Messages[0], chat.Participants[0], state, SecretPath);

        var lines = string.Join('\n', _sink.Lines);
        Assert.DoesNotContain("SECRET", lines, StringComparison.Ordinal);
        Assert.DoesNotContain("비밀", lines, StringComparison.Ordinal);
        Assert.DoesNotContain("private-exports", lines, StringComparison.Ordinal);
        Assert.Contains("format=kakaotalk." + platform + ".ko.v1", lines, StringComparison.Ordinal);
        Assert.Contains("outcome=Malformed issue=InvalidTimestamp", lines, StringComparison.Ordinal);
        Assert.Contains("[redacted:ChatMessage]", lines, StringComparison.Ordinal);
        Assert.Contains("[redacted:Participant]", lines, StringComparison.Ordinal);
    }

    private static ParsedChat Parse(string platform)
    {
        string[] body = platform switch
        {
            "windows" =>
            [
                "--------------- 2026년 10월 1일 목요일 ---------------",
                "[" + SecretName + "] [오후 3:15] " + SecretText,
                "[" + SecretName + "] [오후 13:15] " + SecretText,
            ],
            "android" =>
            [
                "",
                "2026년 10월 1일 오후 3:15, " + SecretName + " : " + SecretText,
                "2026년 10월 1일 오후 13:15, " + SecretName + " : " + SecretText,
            ],
            _ =>
            [
                "",
                "2026년 10월 1일 목요일",
                "오후 3:15, " + SecretName + " : " + SecretText,
                "오후 13:15, " + SecretName + " : " + SecretText,
            ],
        };
        string[] lines = ["SECRET-ROOM 님과 카카오톡 대화", "저장한 날짜 : 2026-10-02 14:23:11", "", .. body];
        var result = new ChatExportParser(RealFormatAdapters.All).Parse(Encoding.UTF8.GetBytes(string.Join("\n", lines)), Context);
        return result.Chat!;
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Imported format={FormatId} messages={MessageCount} malformed={MalformedCount} participants={ParticipantCount}")]
        public static partial void Summary(ILogger logger, ChatFormatId formatId, int messageCount, int malformedCount, int participantCount);

        [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Line format={FormatId} line={LineNumber} outcome={Outcome} issue={Issue}")]
        public static partial void Issue(ILogger logger, ChatFormatId formatId, int lineNumber, LineOutcome outcome, ParseIssue issue);

        [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Accidental message={Message} participant={Participant} state={State} path={Path}")]
        public static partial void Accidental(ILogger logger, ChatMessage message, Participant participant, ParserState state, string path);
    }

    private sealed class MemorySink : ILogSink
    {
        public List<string> Lines { get; } = [];

        public void Write(string line) => Lines.Add(line);
    }
}
