using System.Security.Cryptography;
using System.Text;
using TalkPro.Security.Keys;

namespace TalkPro.Security.Tests.Keys;

public sealed class VaultFileCryptoTests : IDisposable
{
    private static readonly byte[] Plaintext = Encoding.UTF8.GetBytes("합성 테스트 메시지 001 SyntheticPlaintextMarker");
    private readonly TempDirectory _dir = new();

    [Fact]
    public void RoundTrips()
    {
        var (_, key) = KeyFixtures.NewKey(_dir.Path);
        using (key)
        {
            var file = VaultFileCrypto.Encrypt(key, "snapshot", Plaintext);

            Assert.Equal(Plaintext, VaultFileCrypto.Decrypt(key, "snapshot", file));
        }
    }

    [Fact]
    public void CiphertextNeverContainsThePlaintext()
    {
        var (_, key) = KeyFixtures.NewKey(_dir.Path);
        using (key)
        {
            var file = VaultFileCrypto.Encrypt(key, "snapshot", Plaintext);

            Assert.True(file.AsSpan().IndexOf("SyntheticPlaintextMarker"u8) < 0);
        }
    }

    [Fact]
    public void EachEncryptionUsesAFreshNonce()
    {
        var (_, key) = KeyFixtures.NewKey(_dir.Path);
        using (key)
        {
            Assert.NotEqual(VaultFileCrypto.Encrypt(key, "snapshot", Plaintext), VaultFileCrypto.Encrypt(key, "snapshot", Plaintext));
        }
    }

    [Fact]
    public void EmptyPlaintextIsSupported()
    {
        var (_, key) = KeyFixtures.NewKey(_dir.Path);
        using (key)
        {
            Assert.Empty(VaultFileCrypto.Decrypt(key, "cache", VaultFileCrypto.Encrypt(key, "cache", [])));
        }
    }

    [Theory]
    [InlineData("magic", 0)]
    [InlineData("version", 4)]
    [InlineData("dataset", 10)]
    [InlineData("purpose", 23)]
    [InlineData("nonce", 32)]
    [InlineData("tag", 45)]
    [InlineData("ciphertext", -1)]
    public void AnyModifiedByteIsDetected(string region, int offset)
    {
        var (_, key) = KeyFixtures.NewKey(_dir.Path);
        using (key)
        {
            var file = VaultFileCrypto.Encrypt(key, "snapshot", Plaintext);
            file[offset < 0 ? file.Length - 1 : offset] ^= 0x01;

            var ex = Record.Exception(() => VaultFileCrypto.Decrypt(key, "snapshot", file));

            Assert.True(ex is CryptographicException, $"{region}: {ex?.GetType().Name ?? "no exception"}");
        }
    }

    [Fact]
    public void TruncatedFileIsRejected()
    {
        var (_, key) = KeyFixtures.NewKey(_dir.Path);
        using (key)
        {
            var file = VaultFileCrypto.Encrypt(key, "snapshot", Plaintext);

            Assert.ThrowsAny<CryptographicException>(() => VaultFileCrypto.Decrypt(key, "snapshot", file.AsSpan(0, 30)));
        }
    }

    [Fact]
    public void FileOfAnotherPurposeIsRejected()
    {
        var (_, key) = KeyFixtures.NewKey(_dir.Path);
        using (key)
        {
            var file = VaultFileCrypto.Encrypt(key, "vectors", Plaintext);

            Assert.Throws<VaultFormatException>(() => VaultFileCrypto.Decrypt(key, "snapshot", file));
        }
    }

    [Fact]
    public void FileOfAnotherDatasetIsRejected()
    {
        var (store, key) = KeyFixtures.NewKey(_dir.Path);
        using (key)
        using (var other = store.Create(TalkPro.Core.Model.DatasetId.New()))
        {
            var file = VaultFileCrypto.Encrypt(key, "snapshot", Plaintext);

            Assert.Throws<VaultFormatException>(() => VaultFileCrypto.Decrypt(other, "snapshot", file));
        }
    }

    [Fact]
    public void WriteFileIsAtomicAndEncrypted()
    {
        var (_, key) = KeyFixtures.NewKey(_dir.Path);
        using (key)
        {
            var path = Path.Combine(_dir.Path, "snapshot.enc");

            VaultFileCrypto.WriteFile(path, key, "snapshot", Plaintext);
            VaultFileCrypto.WriteFile(path, key, "snapshot", Plaintext);

            Assert.Equal(Plaintext, VaultFileCrypto.ReadFile(path, key, "snapshot"));
            Assert.True(File.ReadAllBytes(path).AsSpan().IndexOf("SyntheticPlaintextMarker"u8) < 0);
            Assert.Empty(Directory.EnumerateFiles(_dir.Path, "*.tmp-*"));
        }
    }

    public void Dispose() => _dir.Dispose();
}
