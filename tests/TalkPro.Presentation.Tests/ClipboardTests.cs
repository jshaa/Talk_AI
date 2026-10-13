using Microsoft.Extensions.DependencyInjection;
using TalkPro.Presentation.Abstractions;
using TalkPro.Presentation.Services;

namespace TalkPro.Presentation.Tests;

public sealed class AutoClearClipboardTests
{
    private readonly FakeBackend _backend = new();
    private readonly ManualTimeProvider _time = new();

    [Fact]
    public void CopyKeepsTheDraftOutOfHistoryAndCloudAndClearsItAfter60Seconds()
    {
        using var clipboard = new AutoClearClipboard(_backend, _time);

        clipboard.CopyWithAutoClear("합성 초안", TimeSpan.FromSeconds(60));

        Assert.Equal("합성 초안", _backend.Current);
        Assert.Equal(
            [("ExcludeClipboardContentFromMonitorProcessing", 0u), ("CanIncludeInClipboardHistory", 0u), ("CanUploadToCloudClipboard", 0u)],
            _backend.LastFlags!.Select(f => (f.Format, f.Value)));
        var timer = Assert.Single(_time.Timers);
        Assert.Equal(TimeSpan.FromSeconds(60), timer.Due);

        timer.Fire();

        Assert.Null(_backend.Current);
        Assert.Equal(1, _backend.Clears);
    }

    [Fact]
    public void SomethingTheUserCopiedMeanwhileIsLeftAlone()
    {
        using var clipboard = new AutoClearClipboard(_backend, _time);
        clipboard.CopyWithAutoClear("합성 초안", TimeSpan.FromSeconds(60));
        _backend.Current = "사용자가 나중에 복사한 것";

        _time.Timers[0].Fire();

        Assert.Equal("사용자가 나중에 복사한 것", _backend.Current);
        Assert.Equal(0, _backend.Clears);
    }

    [Fact]
    public void ANewCopyReplacesThePreviousTimer()
    {
        using var clipboard = new AutoClearClipboard(_backend, _time);

        clipboard.CopyWithAutoClear("첫째", TimeSpan.FromSeconds(60));
        clipboard.CopyWithAutoClear("둘째", TimeSpan.FromSeconds(60));

        Assert.True(_time.Timers[0].Disposed);
        Assert.False(_time.Timers[1].Disposed);
    }

    [Fact]
    public void BusyClipboardStartsNoTimer()
    {
        _backend.Busy = true;
        using var clipboard = new AutoClearClipboard(_backend, _time);

        clipboard.CopyWithAutoClear("합성", TimeSpan.FromSeconds(60));

        Assert.Empty(_time.Timers);
    }

    [Fact]
    public void PresentationUsesTheAutoClearingClipboard()
    {
        var services = new ServiceCollection().AddTalkProPresentation();

        Assert.Equal(typeof(AutoClearClipboard), services.Single(s => s.ServiceType == typeof(IClipboardService)).ImplementationType);
    }

    private sealed class FakeBackend : IClipboardBackend
    {
        public string? Current { get; set; }

        public IReadOnlyList<ClipboardFlag>? LastFlags { get; private set; }

        public bool Busy { get; set; }

        public int Clears { get; private set; }

        public bool TrySetText(string text, IReadOnlyList<ClipboardFlag> flags)
        {
            if (Busy)
            {
                return false;
            }

            (Current, LastFlags) = (text, flags);
            return true;
        }

        public string? TryGetText() => Current;

        public void TryClear()
        {
            Current = null;
            Clears++;
        }
    }
}

/// <summary>TimeProvider whose timers fire only when the test says so (no package needed).</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    public List<ManualTimer> Timers { get; } = [];

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(callback, state, dueTime);
        Timers.Add(timer);
        return timer;
    }

    internal sealed class ManualTimer(TimerCallback callback, object? state, TimeSpan due) : ITimer
    {
        public TimeSpan Due { get; private set; } = due;

        public bool Disposed { get; private set; }

        public void Fire()
        {
            if (!Disposed)
            {
                callback(state);
            }
        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            Due = dueTime;
            return true;
        }

        public void Dispose() => Disposed = true;

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
