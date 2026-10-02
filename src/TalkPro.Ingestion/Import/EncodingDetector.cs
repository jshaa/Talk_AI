using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Unicode;
using TalkPro.Core.Model;

namespace TalkPro.Ingestion.Import;

/// <summary>
/// Decodes an export without loss (DD-011): UTF-8 BOM → strict UTF-8 → strict CP949, otherwise the
/// input is rejected. Nothing is ever replaced by <c>?</c> or U+FFFD.
/// CP949 is taken from <see cref="CodePagesEncodingProvider.Instance"/> directly; <c>Encoding.RegisterProvider</c>
/// is never called, so decoding has no process-wide side effect.
/// </summary>
public static class EncodingDetector
{
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static readonly Encoding StrictCp949 =
        CodePagesEncodingProvider.Instance.GetEncoding(949, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)
        ?? throw new InvalidOperationException("CP949 is not available from the code pages provider.");

    public static bool TryDecode(ReadOnlySpan<byte> content, out SourceEncoding encoding, [NotNullWhen(true)] out string? text)
    {
        if (content.StartsWith(Utf8Bom))
        {
            // A BOM declares UTF-8; invalid bytes after it are not reinterpreted as CP949.
            var body = content[Utf8Bom.Length..];
            encoding = SourceEncoding.Utf8WithBom;
            text = Utf8.IsValid(body) ? StrictUtf8.GetString(body) : null;
            return text is not null;
        }

        if (Utf8.IsValid(content))
        {
            encoding = SourceEncoding.Utf8;
            text = StrictUtf8.GetString(content);
            return true;
        }

        encoding = SourceEncoding.Cp949;
        try
        {
            text = StrictCp949.GetString(content);
            return true;
        }
        catch (DecoderFallbackException)
        {
            text = null;
            return false;
        }
    }
}
