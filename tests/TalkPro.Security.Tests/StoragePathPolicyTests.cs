using TalkPro.Core.Model;
using TalkPro.Infrastructure.Storage;

namespace TalkPro.Security.Tests;

/// <summary>Vault data must stay out of Documents/Desktop/OneDrive (master prompt §17).</summary>
public sealed class StoragePathPolicyTests
{
    private static readonly string OneDrive = Path.Combine(Path.GetTempPath(), "fake-user", "OneDrive");

    [Fact]
    public void RootInsideForbiddenFolderIsRejected()
    {
        var root = Path.Combine(OneDrive, "TalkPro");

        Assert.Throws<UnsafeStoragePathException>(() => new AppPaths(root, [OneDrive]));
    }

    [Fact]
    public void ComparisonIsCaseInsensitive()
    {
        var root = Path.Combine(OneDrive.ToUpperInvariant(), "TalkPro");

        Assert.Throws<UnsafeStoragePathException>(() => new AppPaths(root, [OneDrive]));
    }

    [Fact]
    public void SiblingFolderWithSamePrefixIsAllowed()
    {
        var root = OneDrive + "Backup" + Path.DirectorySeparatorChar + "TalkPro";

        var paths = new AppPaths(root, [OneDrive]);

        Assert.Equal(Path.GetFullPath(root), paths.Root);
    }

    [Fact]
    public void RelativeRootIsRejected()
    {
        Assert.Throws<UnsafeStoragePathException>(() => new AppPaths("TalkPro", []));
    }

    [Fact]
    public void ExceptionDoesNotDiscloseThePath()
    {
        var root = Path.Combine(OneDrive, "TalkPro");

        var ex = Assert.Throws<UnsafeStoragePathException>(() => new AppPaths(root, [OneDrive]));

        Assert.DoesNotContain("fake-user", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DatasetDirectoriesLiveUnderTheVault()
    {
        var paths = new AppPaths(Path.Combine(Path.GetTempPath(), "talkpro-root"), []);
        var id = DatasetId.New();

        var directory = paths.DatasetDirectory(id);

        Assert.Equal(paths.Vault, Path.GetDirectoryName(directory));
        Assert.EndsWith(id.ToString(), directory, StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultPolicyForbidsTheDocumentsFolder()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments, Environment.SpecialFolderOption.DoNotVerify);
        Assert.SkipWhen(string.IsNullOrEmpty(documents), "No Documents folder on this machine.");

        Assert.Contains(documents, StoragePathPolicy.DefaultForbiddenRoots());
    }
}
