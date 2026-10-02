using TalkPro.Core.Model;

namespace TalkPro.Infrastructure.Storage;

/// <summary>
/// Local storage layout: <c>%LOCALAPPDATA%\TalkPro\{vault,logs,cache}</c>.
/// Never Documents/Desktop/OneDrive, which may be synchronised to the cloud.
/// </summary>
public sealed class AppPaths
{
    public const string ProductFolderName = "TalkPro";

    public AppPaths(string root, IReadOnlyCollection<string> forbiddenRoots)
    {
        StoragePathPolicy.EnsureAllowed(root, forbiddenRoots);
        Root = Path.GetFullPath(root);
        Vault = Path.Combine(Root, "vault");
        Logs = Path.Combine(Root, "logs");
        Cache = Path.Combine(Root, "cache");
    }

    public string Root { get; }

    public string Vault { get; }

    public string Logs { get; }

    public string Cache { get; }

    public static AppPaths CreateDefault()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify);
        if (string.IsNullOrEmpty(localAppData))
        {
            throw new UnsafeStoragePathException("Local application data folder is unavailable.");
        }

        return new AppPaths(Path.Combine(localAppData, ProductFolderName), StoragePathPolicy.DefaultForbiddenRoots());
    }

    public string DatasetDirectory(DatasetId datasetId) => Path.Combine(Vault, datasetId.ToString());

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Vault);
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(Cache);
    }
}

public static class StoragePathPolicy
{
    /// <summary>User folders that are commonly redirected to or synchronised by cloud storage.</summary>
    public static IReadOnlyCollection<string> DefaultForbiddenRoots()
    {
        var candidates = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments, Environment.SpecialFolderOption.DoNotVerify),
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop, Environment.SpecialFolderOption.DoNotVerify),
            Environment.GetEnvironmentVariable("OneDrive"),
            Environment.GetEnvironmentVariable("OneDriveConsumer"),
            Environment.GetEnvironmentVariable("OneDriveCommercial"),
        };
        return [.. candidates.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c!)];
    }

    public static void EnsureAllowed(string root, IReadOnlyCollection<string> forbiddenRoots)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(forbiddenRoots);

        if (!Path.IsPathFullyQualified(root))
        {
            throw new UnsafeStoragePathException("Storage root must be an absolute path.");
        }

        var normalizedRoot = WithTrailingSeparator(Path.GetFullPath(root));
        foreach (var forbidden in forbiddenRoots)
        {
            var normalizedForbidden = WithTrailingSeparator(Path.GetFullPath(forbidden));
            if (normalizedRoot.StartsWith(normalizedForbidden, StringComparison.OrdinalIgnoreCase))
            {
                // Path deliberately omitted: it contains the Windows user name.
                throw new UnsafeStoragePathException("Storage root is inside a user document or cloud-synchronised folder.");
            }
        }
    }

    private static string WithTrailingSeparator(string path) =>
        Path.EndsInDirectorySeparator(path) ? path : path + Path.DirectorySeparatorChar;
}

public sealed class UnsafeStoragePathException : Exception
{
    public UnsafeStoragePathException()
    {
    }

    public UnsafeStoragePathException(string message)
        : base(message)
    {
    }

    public UnsafeStoragePathException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
