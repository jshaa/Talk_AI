using System.Security.Cryptography;
using TalkPro.Core.Model;
using TalkPro.Security.Keys;

namespace TalkPro.Security.Tests.Keys;

/// <summary>
/// Platform-independent stand-in for DPAPI in tests: AES-GCM with a per-instance random key and the
/// purpose as associated data, so wrong purposes and tampering fail like they do with DPAPI.
/// </summary>
internal sealed class TestKeyProtector : IKeyProtector
{
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

    public KeyProtectorKind Kind => KeyProtectorKind.WindowsDpapi;

    public byte[] Protect(ReadOnlySpan<byte> secret, ReadOnlySpan<byte> purpose)
    {
        var output = new byte[12 + 16 + secret.Length];
        RandomNumberGenerator.Fill(output.AsSpan(0, 12));
        using var aes = new AesGcm(_key, 16);
        aes.Encrypt(output.AsSpan(0, 12), secret, output.AsSpan(28), output.AsSpan(12, 16), purpose);
        return output;
    }

    public byte[] Unprotect(ReadOnlySpan<byte> protectedSecret, ReadOnlySpan<byte> purpose)
    {
        var plain = new byte[protectedSecret.Length - 28];
        using var aes = new AesGcm(_key, 16);
        aes.Decrypt(protectedSecret[..12], protectedSecret[28..], protectedSecret.Slice(12, 16), plain, purpose);
        return plain;
    }
}

/// <summary>Temporary key/vault directories under the temp folder, removed on dispose.</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "talkpro-sec", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}

internal static class KeyFixtures
{
    public static (DatasetKeyStore Store, DatasetKey Key) NewKey(string directory, IKeyProtector? protector = null)
    {
        var store = new DatasetKeyStore(directory, protector ?? new TestKeyProtector());
        return (store, store.Create(DatasetId.New()));
    }
}
