using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using TalkPro.Presentation.Services;

namespace TalkPro.App.Services;

/// <summary>
/// WPF clipboard for <see cref="AutoClearClipboard"/>. Calls are marshalled to the UI thread (the clear timer
/// fires on a worker thread). Each flag is written as a 4-byte DWORD so Windows keeps the draft out of clipboard
/// history and cloud sync. "Clipboard busy" errors are swallowed; the user can copy again. Text is never logged.
/// </summary>
internal sealed class WpfClipboardBackend : IClipboardBackend
{
    public bool TrySetText(string text, IReadOnlyList<ClipboardFlag> flags) => OnUi(() =>
    {
        var data = new DataObject();
        data.SetText(text, TextDataFormat.UnicodeText);
        foreach (var flag in flags)
        {
            data.SetData(flag.Format, new MemoryStream(BitConverter.GetBytes(flag.Value)));
        }

        Clipboard.SetDataObject(data, copy: true);
        return true;
    }, false);

    public string? TryGetText() => OnUi(() => Clipboard.ContainsText() ? Clipboard.GetText() : null, null);

    public void TryClear() => OnUi(() =>
    {
        Clipboard.Clear();
        return true;
    }, false);

    private static T OnUi<T>(Func<T> action, T fallback)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted)
        {
            return fallback;
        }

        try
        {
            return dispatcher.CheckAccess() ? action() : dispatcher.Invoke(action);
        }
        catch (COMException)
        {
            return fallback;
        }
    }
}
