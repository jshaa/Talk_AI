using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using TalkPro.Core.Model;
using TalkPro.Security.Keys;

namespace TalkPro.Security.Tests.Keys;

/// <summary>
/// Real DPAPI on the current Windows user. "Another Windows user cannot unwrap" cannot be automated
/// in a single-user test run; it follows from <see cref="DpapiKeyProtector.Scope"/> = CurrentUser and is
/// covered by the manual check in docs/SECURITY_TESTS.md.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiKeyProtectorTests : IDisposable
{
    private static readonly byte[] Secret = RandomNumberGenerator.GetBytes(32);
    private static readonly byte[] Purpose = Encoding.ASCII.GetBytes("TalkPro.DatasetKey.v1:test");
    private readonly TempDirectory _dir = new();

    [Fact]
    public void UsesTheCurrentUserScope()
    {
        Assert.Equal(DataProtectionScope.CurrentUser, DpapiKeyProtector.Scope);
    }

    [Fact]
    public void WrapsAndUnwraps()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "DPAPI is Windows-only.");
        var protector = new DpapiKeyProtector();

        var wrapped = protector.Protect(Secret, Purpose);

        Assert.True(wrapped.AsSpan().IndexOf(Secret) < 0, "wrapped blob must not contain the secret");
        Assert.Equal(Secret, protector.Unprotect(wrapped, Purpose));
    }

    [Fact]
    public void WrongPurposeIsRejected()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "DPAPI is Windows-only.");
        var protector = new DpapiKeyProtector();
        var wrapped = protector.Protect(Secret, Purpose);

        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(wrapped, Encoding.ASCII.GetBytes("TalkPro.DatasetKey.v1:other")));
    }

    [Fact]
    public void TamperedBlobIsRejected()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "DPAPI is Windows-only.");
        var protector = new DpapiKeyProtector();
        var wrapped = protector.Protect(Secret, Purpose);
        wrapped[^5] ^= 0xFF;

        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(wrapped, Purpose));
    }

    [Fact]
    public void KeyStoreRoundTripsWithDpapi()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "DPAPI is Windows-only.");
        var store = new DatasetKeyStore(_dir.Path, new DpapiKeyProtector());
        var id = DatasetId.New();

        using var created = store.Create(id);
        var ciphertext = VaultFileCrypto.Encrypt(created, "snapshot", "합성 테스트 스냅샷"u8);
        using var reopened = store.Open(id);

        Assert.Equal("합성 테스트 스냅샷"u8.ToArray(), VaultFileCrypto.Decrypt(reopened, "snapshot", ciphertext));
    }

    public void Dispose() => _dir.Dispose();
}
