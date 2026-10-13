using TalkPro.Presentation.Abstractions;

namespace TalkPro.Mobile.Services;

/// <summary>
/// MAUI dialogs (async only). Android copies a picked document into the app cache; that copy is deleted as soon as
/// it has been read (<see cref="ReleasePickedFile"/>). Debug builds can also load a bundled synthetic sample chat.
/// Paths are never logged.
/// </summary>
internal sealed class MauiDialogService : IDialogService
{
    private const string FromFile = "파일에서 선택";
    private const string Sample = "합성 예시 대화 (테스트용)";

    public string? PickExportFile() => throw new NotSupportedException("Use PickExportFileAsync on mobile.");

    public bool Confirm(string title, string message) => throw new NotSupportedException("Use ConfirmAsync on mobile.");

    public async Task<string?> PickExportFileAsync()
    {
#if DEBUG
        var choice = await Page().DisplayActionSheetAsync("가져올 대화", "취소", null, FromFile, Sample);
        if (choice == Sample)
        {
            return await CopySampleAsync();
        }

        if (choice != FromFile)
        {
            return null;
        }
#endif
        var picked = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "메신저 대화 내보내기 파일 선택",
            FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>> { [DevicePlatform.Android] = ["text/plain"] }),
        });
        return picked?.FullPath;
    }

    public Task<bool> ConfirmAsync(string title, string message) => Page().DisplayAlertAsync(title, message, "예", "아니요");

    /// <summary>Deletes the app-cache copy of a picked or sample file. Files outside the cache are left alone.</summary>
    public void ReleasePickedFile(string path)
    {
        var cache = Path.GetFullPath(FileSystem.CacheDirectory) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(path);
        if (full.StartsWith(cache, StringComparison.Ordinal) && File.Exists(full))
        {
            File.Delete(full);
        }
    }

    public override string ToString() => nameof(MauiDialogService);

    private static Page Page() => Shell.Current ?? Application.Current!.Windows[0].Page!;

#if DEBUG
    private static async Task<string> CopySampleAsync()
    {
        var target = Path.Combine(FileSystem.CacheDirectory, "synthetic-sample-" + Guid.NewGuid().ToString("N") + ".txt");
        using var source = await FileSystem.OpenAppPackageFileAsync("samples/synthetic-sample.txt");
        using var output = File.Create(target);
        await source.CopyToAsync(output);
        return target;
    }
#endif
}
