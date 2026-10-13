using System.Text;
using TalkPro.Tools.SampleGenerator.Corpus;

namespace TalkPro.Tools.SampleGenerator.Output;

/// <summary>
/// Strict encoders: any unrepresentable character throws instead of becoming '?'.
/// CP949 comes straight from <see cref="CodePagesEncodingProvider"/> without
/// <see cref="Encoding.RegisterProvider"/>, so nothing changes process-wide.
/// </summary>
public static class TextEncoders
{
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static Encoding StrictCp949 { get; } =
        CodePagesEncodingProvider.Instance.GetEncoding(949, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)
        ?? throw new InvalidOperationException("CP949 is not available in this runtime.");

    public static byte[] Encode(string text, GoldenEncoding encoding) => encoding switch
    {
        GoldenEncoding.Utf8 => StrictUtf8.GetBytes(text),
        GoldenEncoding.Utf8Bom => [.. Utf8Bom, .. StrictUtf8.GetBytes(text)],
        GoldenEncoding.Cp949 => StrictCp949.GetBytes(text),
        _ => throw new ArgumentOutOfRangeException(nameof(encoding)),
    };
}
