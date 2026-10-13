using System.Security.Cryptography;
using System.Text;
using TalkPro.Core.Model;

namespace TalkPro.Security.Keys;

public sealed class VaultFormatException : CryptographicException
{
    public VaultFormatException()
    {
    }

    public VaultFormatException(string message)
        : base(message)
    {
    }

    public VaultFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// AES-256-GCM container for vault files (snapshots, vectors, raw imports).
/// Layout v1: header = "TPV1" | version(1) | dataset id(16) | purpose length(1) | purpose(ASCII) | nonce(12),
/// then tag(16), then ciphertext. The whole header is authenticated as associated data, so a file
/// moved to another dataset or purpose, or with any modified byte, fails to decrypt.
/// The key is an HKDF subkey of the dataset DEK per purpose; nonces are random per write.
/// </summary>
public static class VaultFileCrypto
{
    public const long MaxPlaintextBytes = 1L << 30;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private static readonly byte[] Magic = "TPV1"u8.ToArray();
    private const byte FormatVersion = 1;

    public static byte[] Encrypt(DatasetKey key, string purpose, ReadOnlySpan<byte> plaintext)
    {
        ArgumentNullException.ThrowIfNull(key);
        VaultPurpose.Validate(purpose);
        if (plaintext.Length > MaxPlaintextBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(plaintext), "Plaintext exceeds the vault file limit.");
        }

        var header = BuildHeader(key.DatasetId, purpose, RandomNumberGenerator.GetBytes(NonceSize));
        var output = new byte[header.Length + TagSize + plaintext.Length];
        header.CopyTo(output, 0);

        var subkey = key.DeriveSubkey(purpose);
        try
        {
            using var aes = new AesGcm(subkey, TagSize);
            aes.Encrypt(
                header.AsSpan(header.Length - NonceSize, NonceSize),
                plaintext,
                output.AsSpan(header.Length + TagSize),
                output.AsSpan(header.Length, TagSize),
                header);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(subkey);
        }

        return output;
    }

    /// <summary>Throws <see cref="VaultFormatException"/> for a foreign/garbled header and <see cref="AuthenticationTagMismatchException"/> for tampering or a wrong key.</summary>
    public static byte[] Decrypt(DatasetKey key, string purpose, ReadOnlySpan<byte> file)
    {
        ArgumentNullException.ThrowIfNull(key);
        VaultPurpose.Validate(purpose);

        var expectedHeaderLength = 4 + 1 + 16 + 1 + purpose.Length + NonceSize;
        if (file.Length < expectedHeaderLength + TagSize || !file[..4].SequenceEqual(Magic) || file[4] != FormatVersion)
        {
            throw new VaultFormatException("Not a vault file of a supported version.");
        }

        if (new Guid(file.Slice(5, 16)) != key.DatasetId.Value)
        {
            throw new VaultFormatException("Vault file belongs to a different dataset.");
        }

        if (file[21] != purpose.Length || !file.Slice(22, purpose.Length).SequenceEqual(Encoding.ASCII.GetBytes(purpose)))
        {
            throw new VaultFormatException("Vault file was written for a different purpose.");
        }

        var header = file[..expectedHeaderLength];
        var plaintext = new byte[file.Length - expectedHeaderLength - TagSize];
        var subkey = key.DeriveSubkey(purpose);
        try
        {
            using var aes = new AesGcm(subkey, TagSize);
            aes.Decrypt(
                header.Slice(header.Length - NonceSize, NonceSize),
                file[(expectedHeaderLength + TagSize)..],
                file.Slice(expectedHeaderLength, TagSize),
                plaintext,
                header);
            return plaintext;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(subkey);
        }
    }

    /// <summary>Encrypts and writes atomically: plaintext never touches the disk.</summary>
    public static void WriteFile(string path, DatasetKey key, string purpose, ReadOnlySpan<byte> plaintext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        AtomicFile.Write(path, Encrypt(key, purpose, plaintext));
    }

    public static byte[] ReadFile(string path, DatasetKey key, string purpose)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Decrypt(key, purpose, File.ReadAllBytes(path));
    }

    private static byte[] BuildHeader(DatasetId datasetId, string purpose, byte[] nonce)
    {
        var header = new byte[4 + 1 + 16 + 1 + purpose.Length + NonceSize];
        Magic.CopyTo(header, 0);
        header[4] = FormatVersion;
        datasetId.Value.TryWriteBytes(header.AsSpan(5, 16));
        header[21] = (byte)purpose.Length;
        Encoding.ASCII.GetBytes(purpose, header.AsSpan(22));
        nonce.CopyTo(header, 22 + purpose.Length);
        return header;
    }
}
