using TalkPro.Presentation.Abstractions;

namespace TalkPro.Presentation.Services;

/// <summary>A clipboard format set to a DWORD value alongside the text.</summary>
public sealed record ClipboardFlag(string Format, uint Value)
{
    public override string ToString() => $"ClipboardFlag {{ {Format} = {Value} }}";
}

/// <summary>
/// Formats that ask Windows to keep a copied draft out of clipboard history (Win+V), out of cloud clipboard
/// sync and away from clipboard monitors.
/// https://learn.microsoft.com/windows/win32/dataxchg/clipboard-formats#cloud-clipboard-and-clipboard-history-formats
/// </summary>
public static class ClipboardPrivacy
{
    public const string ExcludeFromMonitoring = "ExcludeClipboardContentFromMonitorProcessing";
    public const string CanIncludeInHistory = "CanIncludeInClipboardHistory";
    public const string CanUploadToCloud = "CanUploadToCloudClipboard";

    public static IReadOnlyList<ClipboardFlag> Flags { get; } =
    [
        new(ExcludeFromMonitoring, 0),
        new(CanIncludeInHistory, 0),
        new(CanUploadToCloud, 0),
    ];
}

/// <summary>The platform clipboard (WPF in the shell). Implementations swallow "clipboard busy" errors.</summary>
public interface IClipboardBackend
{
    /// <summary>Puts <paramref name="text"/> and the given flags on the clipboard; false if the clipboard was busy.</summary>
    bool TrySetText(string text, IReadOnlyList<ClipboardFlag> flags);

    string? TryGetText();

    void TryClear();
}

/// <summary>
/// Copies with <see cref="ClipboardPrivacy.Flags"/> and clears the clipboard after the given time if it still
/// holds the same text (so something the user copied meanwhile is left alone). A new copy cancels the
/// previous timer. The text is never logged.
/// </summary>
public sealed class AutoClearClipboard(IClipboardBackend backend, TimeProvider time) : IClipboardService, IDisposable
{
    private readonly Lock _gate = new();
    private ITimer? _timer;

    public void CopyWithAutoClear(string text, TimeSpan clearAfter)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(clearAfter, TimeSpan.Zero);
        if (!backend.TrySetText(text, ClipboardPrivacy.Flags))
        {
            return;
        }

        lock (_gate)
        {
            _timer?.Dispose();
            _timer = time.CreateTimer(_ => ClearIfUnchanged(text), null, clearAfter, Timeout.InfiniteTimeSpan);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }

    public override string ToString() => "AutoClearClipboard";

    private void ClearIfUnchanged(string text)
    {
        if (backend.TryGetText() == text)
        {
            backend.TryClear();
        }
    }
}
