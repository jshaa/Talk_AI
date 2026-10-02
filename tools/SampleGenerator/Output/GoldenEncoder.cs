using System.Text;
using System.Text.Unicode;
using TalkPro.Tools.SampleGenerator.Corpus;

namespace TalkPro.Tools.SampleGenerator.Output;

/// <summary>
/// Strict encoders for golden files. Nothing is ever substituted: a character the target encoding
/// cannot represent fails generation (CP949 policy, <c>docs/SYNTHETIC_FORMATS.md</c>).
/// CP949 comes from <see cref="CodePagesEncodingProvider.Instance"/> directly; <c>Encoding.RegisterProvider</c>
/// is never called, so there is no process-wide side effect (DD-011).
/// </summary>
public static class GoldenEncoder
{
    public const int Cp949CodePage = 949;

    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static Encoding Cp949 { get; } = CreateCp949();

    public static string FileToken(GoldenEncoding encoding) => encoding switch
    {
        GoldenEncoding.Utf8 => "utf8",
        GoldenEncoding.Utf8Bom => "utf8bom",
        GoldenEncoding.Cp949 => "cp949",
        _ => throw new ArgumentOutOfRangeException(nameof(encoding)),
    };

    /// <summary>Name of the matching <c>TalkPro.Core.Model.SourceEncoding</c> member a decoder must report.</summary>
    public static string SourceEncodingName(GoldenEncoding encoding) => encoding switch
    {
        GoldenEncoding.Utf8 => "Utf8",
        GoldenEncoding.Utf8Bom => "Utf8WithBom",
        GoldenEncoding.Cp949 => "Cp949",
        _ => throw new ArgumentOutOfRangeException(nameof(encoding)),
    };

    public static byte[] Encode(string text, GoldenEncoding encoding)
    {
        ArgumentNullException.ThrowIfNull(text);
        switch (encoding)
        {
            case GoldenEncoding.Utf8:
                return StrictUtf8.GetBytes(text);

            case GoldenEncoding.Utf8Bom:
                return [.. Utf8Bom, .. StrictUtf8.GetBytes(text)];

            case GoldenEncoding.Cp949:
                byte[] bytes;
                try
                {
                    bytes = Cp949.GetBytes(text);
                }
                catch (EncoderFallbackException ex)
                {
                    throw new InvalidOperationException("Sample is listed for CP949 but contains a character CP949 cannot represent.", ex);
                }

                // Otherwise a decoder following "strict UTF-8 before CP949" would report UTF-8.
                if (Utf8.IsValid(bytes))
                {
                    throw new InvalidOperationException("CP949 sample must contain non-ASCII Korean text so that it is not valid UTF-8.");
                }

                return bytes;

            default:
                throw new ArgumentOutOfRangeException(nameof(encoding));
        }
    }

    private static Encoding CreateCp949() =>
        CodePagesEncodingProvider.Instance.GetEncoding(Cp949CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)
        ?? throw new InvalidOperationException("CP949 is not available from the code pages provider.");
}
