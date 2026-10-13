using System.Security.Cryptography;
using TalkPro.Core.Model;
using TalkPro.Security.Keys;

namespace TalkPro.Security.Tests.Keys;

public sealed class DatasetKeyStoreTests : IDisposable
{
    private readonly TempDirectory _dir = new();
    private readonly TestKeyProtector _protector = new();

    [Fact]
    public void CreatedKeyCanBeReopenedWithTheSameMaterial()
    {
        var store = new DatasetKeyStore(_dir.Path, _protector);
        var id = DatasetId.New();

        using var created = store.Create(id);
        using var opened = store.Open(id);

        Assert.Equal(created.DeriveSubkey("snapshot"), opened.DeriveSubkey("snapshot"));
        Assert.Equal(id, opened.DatasetId);
    }

    [Fact]
    public void EveryDatasetGetsADifferentRandomKey()
    {
        var store = new DatasetKeyStore(_dir.Path, _protector);

        using var a = store.Create(DatasetId.New());
        using var b = store.Create(DatasetId.New());

        Assert.NotEqual(a.DeriveSubkey("snapshot"), b.DeriveSubkey("snapshot"));
    }

    [Fact]
    public void SubkeysDifferPerPurposeAndAreDeterministic()
    {
        using var key = new DatasetKeyStore(_dir.Path, _protector).Create(DatasetId.New());

        Assert.Equal(32, key.DeriveSubkey("vectors").Length);
        Assert.Equal(key.DeriveSubkey("vectors"), key.DeriveSubkey("vectors"));
        Assert.NotEqual(key.DeriveSubkey("vectors"), key.DeriveSubkey("snapshot"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Snapshot")]
    [InlineData("raw data")]
    [InlineData("this-purpose-label-is-far-too-long-for-a-header")]
    public void InvalidPurposesAreRejected(string purpose)
    {
        using var key = new DatasetKeyStore(_dir.Path, _protector).Create(DatasetId.New());

        Assert.ThrowsAny<ArgumentException>(() => key.DeriveSubkey(purpose));
    }

    [Fact]
    public void CreatingTwiceIsRejected()
    {
        var store = new DatasetKeyStore(_dir.Path, _protector);
        var id = DatasetId.New();
        store.Create(id).Dispose();

        Assert.Throws<InvalidOperationException>(() => store.Create(id));
    }

    [Fact]
    public void OpeningAMissingKeyFails()
    {
        Assert.Throws<DatasetKeyNotFoundException>(() => new DatasetKeyStore(_dir.Path, _protector).Open(DatasetId.New()));
    }

    [Fact]
    public void KeyFileNeverContainsTheRawKey()
    {
        var store = new DatasetKeyStore(_dir.Path, _protector);
        var id = DatasetId.New();
        using var key = store.Create(id);

        var file = File.ReadAllBytes(store.PathFor(id));

        Assert.True(file.AsSpan().IndexOf(key.Material) < 0, "raw DEK found in key file");
        Assert.True(file.AsSpan().IndexOf(key.DeriveSubkey("snapshot")) < 0, "subkey found in key file");
    }

    [Fact]
    public void KeyFileCopiedToAnotherDatasetIsRejected()
    {
        var store = new DatasetKeyStore(_dir.Path, _protector);
        var source = DatasetId.New();
        var target = DatasetId.New();
        store.Create(source).Dispose();

        File.Copy(store.PathFor(source), store.PathFor(target));

        Assert.ThrowsAny<CryptographicException>(() => store.Open(target));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(30)]
    [InlineData(-1)]
    public void TamperedKeyFileIsRejected(int offset)
    {
        var store = new DatasetKeyStore(_dir.Path, _protector);
        var id = DatasetId.New();
        store.Create(id).Dispose();
        var bytes = File.ReadAllBytes(store.PathFor(id));
        var index = offset < 0 ? bytes.Length - 1 : offset;
        bytes[index] ^= 0x5A;
        File.WriteAllBytes(store.PathFor(id), bytes);

        Assert.ThrowsAny<CryptographicException>(() => store.Open(id));
    }

    [Fact]
    public void DestroyMakesTheKeyUnrecoverable()
    {
        var store = new DatasetKeyStore(_dir.Path, _protector);
        var id = DatasetId.New();
        store.Create(id).Dispose();

        Assert.True(store.Destroy(id));

        Assert.False(store.Exists(id));
        Assert.False(File.Exists(store.PathFor(id)));
        Assert.Throws<DatasetKeyNotFoundException>(() => store.Open(id));
        Assert.False(store.Destroy(id));
    }

    [Fact]
    public void AtomicWriteLeavesNoTemporaryFiles()
    {
        var store = new DatasetKeyStore(_dir.Path, _protector);
        store.Create(DatasetId.New()).Dispose();

        Assert.Empty(Directory.EnumerateFiles(_dir.Path, "*.tmp-*"));
    }

    [Fact]
    public void DisposedKeyCannotBeUsed()
    {
        var key = new DatasetKeyStore(_dir.Path, _protector).Create(DatasetId.New());

        key.Dispose();

        Assert.True(key.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => key.DeriveSubkey("snapshot"));
        Assert.DoesNotContain("snapshot", key.ToString(), StringComparison.Ordinal);
    }

    public void Dispose() => _dir.Dispose();
}
