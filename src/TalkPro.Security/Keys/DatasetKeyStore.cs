using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using TalkPro.Core.Model;

namespace TalkPro.Security.Keys;

public sealed class DatasetKeyNotFoundException : Exception
{
    public DatasetKeyNotFoundException()
        : base("No key exists for this dataset (never created or destroyed).")
    {
    }

    public DatasetKeyNotFoundException(string message)
        : base(message)
    {
    }

    public DatasetKeyNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Stores one wrapped DEK per dataset in a directory kept apart from the vault data, so deleting a
/// dataset can destroy the key first (crypto-shredding, DD-007).
/// File format v1: "TPK1" | version(1) | protector kind(1) | dataset id(16) | length(4, LE) | wrapped DEK.
/// The DPAPI entropy is bound to the dataset id, so a key file copied to another dataset fails to unwrap.
/// </summary>
public sealed class DatasetKeyStore
{
    private static readonly byte[] Magic = "TPK1"u8.ToArray();
    private const byte FormatVersion = 1;
    private const int HeaderSize = 4 + 1 + 1 + 16 + 4;
    private const int MaxWrappedSize = 64 * 1024;

    private readonly string _directory;
    private readonly IKeyProtector _protector;

    public DatasetKeyStore(string directory, IKeyProtector protector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(protector);
        _directory = directory;
        _protector = protector;
    }

    public bool Exists(DatasetId datasetId) => File.Exists(PathFor(datasetId));

    /// <summary>Generates and stores a new DEK. Fails if the dataset already has one.</summary>
    public DatasetKey Create(DatasetId datasetId)
    {
        var path = PathFor(datasetId);
        if (File.Exists(path))
        {
            throw new InvalidOperationException("A key already exists for this dataset.");
        }

        var key = DatasetKey.Generate(datasetId);
        try
        {
            var wrapped = _protector.Protect(key.Material, Purpose(datasetId));
            var file = new byte[HeaderSize + wrapped.Length];
            Magic.CopyTo(file, 0);
            file[4] = FormatVersion;
            file[5] = (byte)_protector.Kind;
            datasetId.Value.TryWriteBytes(file.AsSpan(6, 16));
            BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(22, 4), wrapped.Length);
            wrapped.CopyTo(file, HeaderSize);

            Directory.CreateDirectory(_directory);
            AtomicFile.Write(path, file);
            return key;
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }

    /// <summary>Unwraps the dataset's DEK. Throws <see cref="DatasetKeyNotFoundException"/> if it does not exist.</summary>
    public DatasetKey Open(DatasetId datasetId)
    {
        var path = PathFor(datasetId);
        if (!File.Exists(path))
        {
            throw new DatasetKeyNotFoundException();
        }

        var file = File.ReadAllBytes(path);
        if (file.Length < HeaderSize || !file.AsSpan(0, 4).SequenceEqual(Magic) || file[4] != FormatVersion)
        {
            throw new CryptographicException("Unrecognised key file format.");
        }

        if (file[5] != (byte)_protector.Kind)
        {
            throw new CryptographicException("Key file was protected by a different platform mechanism.");
        }

        if (new Guid(file.AsSpan(6, 16)) != datasetId.Value)
        {
            throw new CryptographicException("Key file belongs to a different dataset.");
        }

        var length = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(22, 4));
        if (length <= 0 || length > MaxWrappedSize || HeaderSize + length != file.Length)
        {
            throw new CryptographicException("Corrupted key file.");
        }

        return new DatasetKey(datasetId, _protector.Unprotect(file.AsSpan(HeaderSize, length), Purpose(datasetId)));
    }

    /// <summary>
    /// Crypto-shredding: overwrites the wrapped key with random bytes (write-through), then deletes it.
    /// Returns false if no key existed. Afterwards every ciphertext of the dataset is unrecoverable.
    /// SSD wear-levelling may keep old blocks physically; that is why the key is DPAPI-wrapped too.
    /// </summary>
    public bool Destroy(DatasetId datasetId)
    {
        var path = PathFor(datasetId);
        if (!File.Exists(path))
        {
            return false;
        }

        var length = (int)Math.Min(new FileInfo(path).Length, MaxWrappedSize + HeaderSize);
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            stream.Write(RandomNumberGenerator.GetBytes(length));
            stream.Flush(flushToDisk: true);
        }

        File.Delete(path);
        return !File.Exists(path);
    }

    public string PathFor(DatasetId datasetId) => Path.Combine(_directory, datasetId + ".key");

    private static byte[] Purpose(DatasetId datasetId) => Encoding.ASCII.GetBytes("TalkPro.DatasetKey.v1:" + datasetId);
}

/// <summary>Write-to-temp-then-rename in the same directory; the temp file only ever holds the final bytes.</summary>
internal static class AtomicFile
{
    public static void Write(string path, ReadOnlySpan<byte> content)
    {
        var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(content);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }
}
