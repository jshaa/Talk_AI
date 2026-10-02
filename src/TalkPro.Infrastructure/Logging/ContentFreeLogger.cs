using System.Globalization;
using Microsoft.Extensions.Logging;

namespace TalkPro.Infrastructure.Logging;

/// <summary>Destination for already-redacted log lines.</summary>
public interface ILogSink
{
    void Write(string line);
}

/// <summary>
/// Logger that never invokes the caller-supplied formatter (which would interpolate raw values)
/// and drops scopes (which may carry arbitrary objects).
/// </summary>
internal sealed class ContentFreeLogger(string categoryName, ILogSink sink, TimeProvider clock) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"{clock.GetUtcNow():O} [{logLevel}] {categoryName}({eventId.Id}): {ContentFreeLogFormatter.FormatState(state)}");

        if (exception is not null)
        {
            line += " | " + ContentFreeLogFormatter.FormatException(exception);
        }

        sink.Write(line);
    }
}

[ProviderAlias("ContentFree")]
public sealed class ContentFreeLoggerProvider(ILogSink sink, TimeProvider clock) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new ContentFreeLogger(categoryName, sink, clock);

    public void Dispose()
    {
    }
}
