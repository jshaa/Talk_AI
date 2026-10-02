using TalkPro.Infrastructure.Logging;

namespace TalkPro.Security.Tests;

public sealed class FileLogSinkTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "talkpro-logs", Guid.NewGuid().ToString("N"));

    [Fact]
    public void ExpiredLogFilesAreDeletedOnStartup()
    {
        Directory.CreateDirectory(_directory);
        var expired = Path.Combine(_directory, "app-20260901.log");
        var recent = Path.Combine(_directory, "app-20260930.log");
        File.WriteAllText(expired, "old");
        File.WriteAllText(recent, "new");

        using var sink = new FileLogSink(_directory, new FixedClock(new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero)), retentionDays: 7);

        Assert.False(File.Exists(expired));
        Assert.True(File.Exists(recent));
    }

    [Fact]
    public void LinesAreAppendedToTheDailyFile()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero));

        using (var sink = new FileLogSink(_directory, clock))
        {
            sink.Write("first");
            sink.Write("second");
        }

        var lines = File.ReadAllLines(Path.Combine(_directory, "app-20261002.log"));
        Assert.Equal(["first", "second"], lines);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
