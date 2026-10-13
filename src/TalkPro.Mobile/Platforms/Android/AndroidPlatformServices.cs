using System.Security.Cryptography;
using Android.Content;
using Android.Net;
using Android.OS;
using Android.Security.Keystore;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;
using TalkPro.Presentation.Abstractions;
using TalkPro.Presentation.Services;
using TalkPro.Security.Keys;

namespace TalkPro.Mobile.Platforms.Android;

/// <summary>
/// KEK in the Android Keystore (DD-014 PoC): a non-exportable AES-256-GCM key that never leaves the secure
/// hardware/TEE. Dataset keys are wrapped with it; <c>purpose</c> is bound as associated data.
/// Wrapped blob: [iv length][iv][ciphertext+tag].
/// </summary>
internal sealed class AndroidKeystoreKeyProtector : IKeyProtector
{
    private const string Store = "AndroidKeyStore";
    private const string Alias = "talkpro.kek.v1";
    private const string Transformation = "AES/GCM/NoPadding";
    private const int TagBits = 128;

    public KeyProtectorKind Kind => KeyProtectorKind.AndroidKeystore;

    public byte[] Protect(ReadOnlySpan<byte> secret, ReadOnlySpan<byte> purpose)
    {
        try
        {
            var cipher = Cipher.GetInstance(Transformation)!;
            cipher.Init(Javax.Crypto.CipherMode.EncryptMode, Key());
            cipher.UpdateAAD(purpose.ToArray());
            var sealedBytes = cipher.DoFinal(secret.ToArray())!;
            var iv = cipher.GetIV()!;
            return [(byte)iv.Length, .. iv, .. sealedBytes];
        }
        catch (Java.Lang.Exception ex)
        {
            throw new CryptographicException("Android Keystore could not wrap the key.", ex);
        }
    }

    public byte[] Unprotect(ReadOnlySpan<byte> protectedSecret, ReadOnlySpan<byte> purpose)
    {
        if (protectedSecret.Length < 2 || protectedSecret[0] is 0 || protectedSecret.Length <= 1 + protectedSecret[0])
        {
            throw new CryptographicException("Wrapped key is malformed.");
        }

        var ivLength = protectedSecret[0];
        try
        {
            var cipher = Cipher.GetInstance(Transformation)!;
            cipher.Init(Javax.Crypto.CipherMode.DecryptMode, Key(), new GCMParameterSpec(TagBits, protectedSecret.Slice(1, ivLength).ToArray()));
            cipher.UpdateAAD(purpose.ToArray());
            return cipher.DoFinal(protectedSecret[(1 + ivLength)..].ToArray())!;
        }
        catch (Java.Lang.Exception ex)
        {
            throw new CryptographicException("Android Keystore could not unwrap the key.", ex);
        }
    }

    public override string ToString() => nameof(AndroidKeystoreKeyProtector);

    private static IKey Key()
    {
        var store = KeyStore.GetInstance(Store)!;
        store.Load(null);
        if (store.GetKey(Alias, null) is IKey existing)
        {
            return existing;
        }

        var generator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, Store)!;
        generator.Init(new KeyGenParameterSpec.Builder(Alias, KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
            .SetBlockModes(KeyProperties.BlockModeGcm)
            .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)
            .SetKeySize(256)
            .SetRandomizedEncryptionRequired(true)
            .Build());
        return generator.GenerateKey()!;
    }
}

/// <summary>
/// Clipboard for AI drafts on Android: marked sensitive (hidden from the Android 13+ clipboard preview and
/// keyboard suggestions) and cleared again by <see cref="AutoClearClipboard"/>. Text is never logged.
/// </summary>
internal sealed class AndroidClipboardBackend : IClipboardBackend
{
    private const string SensitiveExtra = "android.content.extra.IS_SENSITIVE";

    public bool TrySetText(string text, IReadOnlyList<ClipboardFlag> flags) => OnMain(() =>
    {
        var clip = ClipData.NewPlainText("TalkPro 초안", text)!;
        var extras = new PersistableBundle();
        extras.PutBoolean(SensitiveExtra, true);
        clip.Description!.Extras = extras;
        Manager().PrimaryClip = clip;
        return true;
    }, false);

    public string? TryGetText() => OnMain(() => Manager().PrimaryClip?.GetItemAt(0)?.Text, null);

    public void TryClear() => OnMain(() =>
    {
        Manager().ClearPrimaryClip();
        return true;
    }, false);

    public override string ToString() => nameof(AndroidClipboardBackend);

    private static ClipboardManager Manager() =>
        (ClipboardManager)global::Android.App.Application.Context.GetSystemService(Context.ClipboardService)!;

    private static T OnMain<T>(Func<T> action, T fallback)
    {
        try
        {
            return MainThread.IsMainThread ? action() : MainThread.InvokeOnMainThreadAsync(action).GetAwaiter().GetResult();
        }
        catch (Java.Lang.Exception)
        {
            return fallback;
        }
    }
}

/// <summary>Metered network (mobile data, metered Wi-Fi) so the 4.9 GB model download asks first.</summary>
internal sealed class AndroidNetworkInfo : INetworkInfo
{
    public bool IsMetered =>
        global::Android.App.Application.Context.GetSystemService(Context.ConnectivityService) is ConnectivityManager manager && manager.IsActiveNetworkMetered;

    public override string ToString() => nameof(AndroidNetworkInfo);
}
