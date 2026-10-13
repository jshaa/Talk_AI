using TalkPro.Core.Model;
using TalkPro.Infrastructure.Storage;
using TalkPro.Infrastructure.Vault;
using TalkPro.Security.Keys;

namespace TalkPro.Security.Tests.Keys;

public sealed class DatasetDeletionServiceTests : IDisposable
{
    private readonly TempDirectory _root = new();
    private readonly AppPaths _paths;
    private readonly DatasetKeyStore _keys;
    private readonly DatasetDeletionService _service;

    public DatasetDeletionServiceTests()
    {
        _paths = new AppPaths(_root.Path, []);
        _paths.EnsureCreated();
        _keys = new DatasetKeyStore(_paths.Keys, new TestKeyProtector());
        _service = new DatasetDeletionService(_paths, _keys);
    }

    [Fact]
    public void KeysLiveOutsideTheVault()
    {
        Assert.False(_paths.Keys.StartsWith(_paths.Vault, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DeletesKeyAndEveryDatasetArtifact()
    {
        var id = DatasetId.New();
        var ciphertext = CreateDataset(id);

        var report = _service.Delete(id);

        Assert.True(report.KeyExisted);
        Assert.True(report.KeyDestroyed);
        Assert.True(report.Complete, string.Join(", ", report.Residuals));
        Assert.Equal(9, report.FilesDeleted);
        Assert.False(Directory.Exists(_paths.DatasetDirectory(id)));
        Assert.False(Directory.Exists(_paths.DatasetCacheDirectory(id)));
        Assert.Throws<DatasetKeyNotFoundException>(() => _keys.Open(id));
        Assert.True(File.ReadAllBytes(ciphertext).Length > 0, "an external copy of a vault file survives but has no key");
    }

    [Fact]
    public void OtherDatasetsAreUntouched()
    {
        var deleted = DatasetId.New();
        var kept = DatasetId.New();
        CreateDataset(deleted);
        CreateDataset(kept);

        _service.Delete(deleted);

        Assert.True(_keys.Exists(kept));
        Assert.True(File.Exists(Path.Combine(_paths.DatasetDirectory(kept), "data.db-wal")));
        using var key = _keys.Open(kept);
    }

    [Fact]
    public void DeletingTwiceIsHarmless()
    {
        var id = DatasetId.New();
        CreateDataset(id);
        _service.Delete(id);

        var second = _service.Delete(id);

        Assert.False(second.KeyExisted);
        Assert.True(second.Complete);
        Assert.Equal(0, second.FilesDeleted);
    }

    [Fact]
    public void ResidualsAreReportedAsAppRelativePaths()
    {
        var id = DatasetId.New();
        CreateDataset(id);

        var residuals = _service.Residuals(id);

        Assert.NotEmpty(residuals);
        Assert.All(residuals, r => Assert.False(Path.IsPathRooted(r), r));
        Assert.Contains(residuals, r => r.EndsWith("data.db-shm", StringComparison.Ordinal));
    }

    [Fact]
    public void ReportAndNoticeContainNoPaths()
    {
        var id = DatasetId.New();
        CreateDataset(id);

        var report = _service.Delete(id);

        Assert.DoesNotContain(_root.Path, report.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("원본 TXT", DatasetDeletionService.UserNotice, StringComparison.Ordinal);
    }

    public void Dispose() => _root.Dispose();

    /// <summary>Creates a key, an encrypted file, and the SQLite sidecar names the vault must never leave behind.</summary>
    private string CreateDataset(DatasetId id)
    {
        using var key = _keys.Create(id);
        var vault = _paths.DatasetDirectory(id);
        var cache = _paths.DatasetCacheDirectory(id);
        Directory.CreateDirectory(Path.Combine(vault, "adapter"));
        Directory.CreateDirectory(cache);

        VaultFileCrypto.WriteFile(Path.Combine(vault, "data.snapshot"), key, "snapshot", "합성 스냅샷"u8);
        foreach (var name in new[] { "raw.enc", "data.db", "data.db-wal", "data.db-shm", "data.db-journal", "vectors.bin", "chats.db" })
        {
            File.WriteAllBytes(Path.Combine(vault, name), [1, 2, 3]);
        }

        File.WriteAllBytes(Path.Combine(cache, "embedding-cache.bin"), [4, 5, 6]);

        var outside = Path.Combine(_root.Path, "external-copy-" + id + ".snapshot");
        File.Copy(Path.Combine(vault, "data.snapshot"), outside);
        return outside;
    }
}
