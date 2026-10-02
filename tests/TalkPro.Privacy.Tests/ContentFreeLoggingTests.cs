using Microsoft.Extensions.Logging;
using TalkPro.Core.Model;
using TalkPro.Infrastructure.Logging;

namespace TalkPro.Privacy.Tests;

/// <summary>Conversation content must never reach logs (master prompt §14).</summary>
public sealed partial class ContentFreeLoggingTests
{
    private const string Secret = "민수야 내 번호 010-1234-5678";

    private readonly MemorySink _sink = new();
    private readonly ILogger _logger;

    public ContentFreeLoggingTests()
    {
        using var provider = new ContentFreeLoggerProvider(_sink, TimeProvider.System);
        _logger = provider.CreateLogger("Test");
    }

    [Fact]
    public void StringValuesAreRedactedWhileSafeValuesAreKept()
    {
        var datasetId = DatasetId.New();

        Log.Imported(_logger, Secret, 3, datasetId);

        var line = Assert.Single(_sink.Lines);
        Assert.DoesNotContain("민수", line, StringComparison.Ordinal);
        Assert.DoesNotContain("010", line, StringComparison.Ordinal);
        Assert.Contains("[redacted:String]", line, StringComparison.Ordinal);
        Assert.Contains("count=3", line, StringComparison.Ordinal);
        Assert.Contains(datasetId.ToString(), line, StringComparison.Ordinal);
    }

    [Fact]
    public void UnstructuredStateIsDroppedEntirely()
    {
        _logger.Log(LogLevel.Information, new EventId(7), Secret, exception: null, (state, _) => state);

        var line = Assert.Single(_sink.Lines);
        Assert.DoesNotContain("민수", line, StringComparison.Ordinal);
        Assert.Contains(ContentFreeLogFormatter.UnstructuredEntry, line, StringComparison.Ordinal);
    }

    [Fact]
    public void ExceptionMessageAndDataAreNeverWritten()
    {
        var exception = CaptureThrown(Secret);
        exception.Data["payload"] = Secret;

        Log.Failed(_logger, exception);

        var line = Assert.Single(_sink.Lines);
        Assert.DoesNotContain("민수", line, StringComparison.Ordinal);
        Assert.Contains(typeof(InvalidOperationException).FullName!, line, StringComparison.Ordinal);
    }

    [Fact]
    public void InnerExceptionMessagesAreNeverWritten()
    {
        var outer = new InvalidOperationException("outer", CaptureThrown(Secret));

        Log.Failed(_logger, outer);

        Assert.DoesNotContain("민수", Assert.Single(_sink.Lines), StringComparison.Ordinal);
    }

    [Fact]
    public void ScopesAreNotCaptured()
    {
        Assert.Null(_logger.BeginScope(Secret));
    }

    [Theory]
    [InlineData(42, "42")]
    [InlineData(true, "true")]
    [InlineData(MessageKind.Photo, "Photo")]
    [InlineData('x', "[redacted:Char]")]
    public void RenderValueAllowsOnlyContentFreeTypes(object value, string expected)
    {
        Assert.Equal(expected, ContentFreeLogFormatter.RenderValue(value));
    }

    private static InvalidOperationException CaptureThrown(string message)
    {
        try
        {
            throw new InvalidOperationException(message);
        }
        catch (InvalidOperationException ex)
        {
            return ex;
        }
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Imported {Text} count={Count} dataset={DatasetId}")]
        public static partial void Imported(ILogger logger, string text, int count, DatasetId datasetId);

        [LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = "Failed")]
        public static partial void Failed(ILogger logger, Exception exception);
    }

    private sealed class MemorySink : ILogSink
    {
        public List<string> Lines { get; } = [];

        public void Write(string line) => Lines.Add(line);
    }
}
