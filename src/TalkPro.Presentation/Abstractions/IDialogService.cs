namespace TalkPro.Presentation.Abstractions;

/// <summary>
/// UI-toolkit-specific dialogs, implemented by the shell. ViewModels call the async members: WPF uses the
/// synchronous defaults below, MAUI (Android) overrides the async ones because its pickers and alerts are async.
/// </summary>
public interface IDialogService
{
    /// <summary>Lets the user pick an exported chat file. Returns null when cancelled.</summary>
    string? PickExportFile();

    bool Confirm(string title, string message);

    Task<string?> PickExportFileAsync() => Task.FromResult(PickExportFile());

    Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(Confirm(title, message));

    /// <summary>
    /// Called once the picked file has been read. A shell that copied the file (e.g. Android copies a picked
    /// document into the app cache) deletes its copy here, so the raw export does not linger on the device.
    /// </summary>
    void ReleasePickedFile(string path)
    {
    }
}

/// <summary>Whether the active network is metered (mobile data), so large model downloads can ask first. Optional.</summary>
public interface INetworkInfo
{
    bool IsMetered { get; }
}
