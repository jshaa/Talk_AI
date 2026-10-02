using System.Globalization;
using System.Text;

namespace TalkPro.Infrastructure.Logging;

/// <summary>
/// Appends redacted lines to a daily file under the app's local log directory and keeps a
/// bounded number of days. Logs are not encrypted because they are content-free by construction.
/// </summary>
public sealed class FileLogSink : ILogSink, IDisposable
{
    private readonly string _directory;
    private readonly TimeProvider _clock;
    private readonly Lock _gate = new();
    private StreamWriter? _writer;
    private DateOnly _currentDay;

    public FileLogSink(string directory, TimeProvider clock, int retentionDays = 7)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentOutOfRangeException.ThrowIfLessThan(retentionDays, 1);

        _directory = directory;
        _clock = clock;
        Directory.CreateDirectory(_directory);
        DeleteExpired(retentionDays);
    }

    public void Write(string line)
    {
        lock (_gate)
        {
            var today = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
            if (_writer is null || today != _currentDay)
            {
                _writer?.Dispose();
                _currentDay = today;
                var path = Path.Combine(_directory, FileNameFor(today));
                _writer = new StreamWriter(path, append: true, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };
            }

            _writer.WriteLine(line);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    private static string FileNameFor(DateOnly day) =>
        "app-" + day.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log";

    private void DeleteExpired(int retentionDays)
    {
        var cutoff = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime).AddDays(-retentionDays);
        foreach (var file in Directory.EnumerateFiles(_directory, "app-*.log"))
        {
            var stamp = Path.GetFileNameWithoutExtension(file)["app-".Length..];
            if (DateOnly.TryParseExact(stamp, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) && day < cutoff)
            {
                File.Delete(file);
            }
        }
    }
}
