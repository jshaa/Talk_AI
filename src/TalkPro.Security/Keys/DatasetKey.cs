using System.Security.Cryptography;
using System.Text;
using TalkPro.Core.Model;

namespace TalkPro.Security.Keys;

/// <summary>
/// A dataset's 256-bit data-encryption key (DEK). Never persisted in clear: stored only wrapped by
/// <see cref="IKeyProtector"/>. Per-purpose subkeys are derived with HKDF-SHA256, so different files
/// never share an AES key. <see cref="Dispose"/> zeroes the key bytes (best effort: the GC may have
/// copied them earlier; this is a documented limitation of managed memory).
/// </summary>
public sealed class DatasetKey : IDisposable
{
    public const int KeySizeBytes = 32;
    private const string SubkeyInfoPrefix = "TalkPro.Subkey.v1:";

    private readonly byte[] _key;
    private bool _disposed;

    internal DatasetKey(DatasetId datasetId, byte[] key)
    {
        if (key.Length != KeySizeBytes)
        {
            CryptographicOperations.ZeroMemory(key);
            throw new CryptographicException("Invalid dataset key length.");
        }

        DatasetId = datasetId;
        _key = key;
    }

    public DatasetId DatasetId { get; }

    public bool IsDisposed => _disposed;

    internal static DatasetKey Generate(DatasetId datasetId) => new(datasetId, RandomNumberGenerator.GetBytes(KeySizeBytes));

    /// <summary>Derives the AES-256 key for one purpose (e.g. <c>snapshot</c>, <c>vectors</c>, <c>raw</c>).</summary>
    public byte[] DeriveSubkey(string purpose)
    {
        VaultPurpose.Validate(purpose);
        ObjectDisposedException.ThrowIf(_disposed, this);
        return HKDF.DeriveKey(HashAlgorithmName.SHA256, _key, KeySizeBytes, salt: [], info: Encoding.ASCII.GetBytes(SubkeyInfoPrefix + purpose));
    }

    /// <summary>Raw key for wrapping only.</summary>
    internal ReadOnlySpan<byte> Material
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _key;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            CryptographicOperations.ZeroMemory(_key);
            _disposed = true;
        }
    }

    public override string ToString() => $"DatasetKey {{ Dataset = {DatasetId}, Disposed = {_disposed} }}";
}

/// <summary>Purpose labels are short lowercase ASCII identifiers; they are authenticated in file headers.</summary>
public static class VaultPurpose
{
    public const int MaxLength = 32;

    public static void Validate(string purpose)
    {
        ArgumentException.ThrowIfNullOrEmpty(purpose);
        if (purpose.Length > MaxLength || !purpose.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '-' or '.'))
        {
            throw new ArgumentException("Purpose must be 1-32 chars of lowercase ASCII letters, digits, '-' or '.'.", nameof(purpose));
        }
    }
}
