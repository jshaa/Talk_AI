using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace TalkPro.Security.Keys;

/// <summary>Identifies the platform mechanism that wrapped a dataset key. Stored in key files.</summary>
public enum KeyProtectorKind : byte
{
    /// <summary>Windows DPAPI, CurrentUser scope.</summary>
    WindowsDpapi = 1,

    /// <summary>Reserved for the Android Keystore runtime (DD-014 PoC).</summary>
    AndroidKeystore = 2,
}

/// <summary>
/// Platform key protection (the KEK level). Wraps a dataset key so that only the same OS user on the
/// same device can unwrap it. <paramref name="purpose"/> binds the wrapped blob to one dataset.
/// </summary>
public interface IKeyProtector
{
    KeyProtectorKind Kind { get; }

    byte[] Protect(ReadOnlySpan<byte> secret, ReadOnlySpan<byte> purpose);

    /// <summary>Throws <see cref="CryptographicException"/> if the blob was tampered with or the purpose differs.</summary>
    byte[] Unprotect(ReadOnlySpan<byte> protectedSecret, ReadOnlySpan<byte> purpose);
}

/// <summary>Windows DPAPI with <see cref="DataProtectionScope.CurrentUser"/>: other Windows users cannot unwrap.</summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiKeyProtector : IKeyProtector
{
    public const DataProtectionScope Scope = DataProtectionScope.CurrentUser;

    public KeyProtectorKind Kind => KeyProtectorKind.WindowsDpapi;

    public byte[] Protect(ReadOnlySpan<byte> secret, ReadOnlySpan<byte> purpose) =>
        ProtectedData.Protect(secret.ToArray(), purpose.ToArray(), Scope);

    public byte[] Unprotect(ReadOnlySpan<byte> protectedSecret, ReadOnlySpan<byte> purpose) =>
        ProtectedData.Unprotect(protectedSecret.ToArray(), purpose.ToArray(), Scope);
}
