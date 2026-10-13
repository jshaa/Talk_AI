using System.Text;
using System.Text.Unicode;
using TalkPro.Core.Model;

namespace TalkPro.Ingestion.Parsing;

public readonly record struct DecodedText(SourceEncoding Encoding, string Text)
{
    public override string ToString() => $"DecodedText {{ Encoding = {Encoding}, Length = {Text.Length} }}";
}

/// <summary>
/// Detects and decodes export bytes: UTF-8 BOM → strict UTF-8 → strict CP949 (DD-011).
/// Never decodes lossily. CP949 is taken from <see cref="CodePagesEncodingProvider"/> directly,
/// without <see cref="Encoding.RegisterProvider"/>, so there is no process-wide side effect.
/// </summary>
public static class TextDecoding
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static readonly Encoding StrictCp949 =
        CodePagesEncodingProvider.Instance.GetEncoding(949, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)
        ?? throw new InvalidOperationException("CP949 is not available in this runtime.");

    public static bool TryDecode(ReadOnlySpan<byte> bytes, out DecodedText decoded)
    {
        decoded = default;

        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            var payload = bytes[3..];
            if (!Utf8.IsValid(payload))
            {
                return false;
            }

            decoded = new DecodedText(SourceEncoding.Utf8WithBom, StrictUtf8.GetString(payload));
            return true;
        }

        // UTF-16/32 byte-order marks: not a supported export encoding.
        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]) || bytes.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
        {
            return false;
        }

        if (Utf8.IsValid(bytes))
        {
            decoded = new DecodedText(SourceEncoding.Utf8, StrictUtf8.GetString(bytes));
            return true;
        }

        try
        {
            decoded = new DecodedText(SourceEncoding.Cp949, StrictCp949.GetString(bytes));
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}

/// <summary>Splits on CRLF, LF or CR (same rules as <see cref="TextReader.ReadLine"/>); a final terminator adds no empty line.</summary>
public static class LineSplitter
{
    public static List<string> Split(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lines = new List<string>();
        var span = text.AsSpan();
        while (!span.IsEmpty)
        {
            var end = span.IndexOfAny('\r', '\n');
            if (end < 0)
            {
                lines.Add(span.ToString());
                break;
            }

            lines.Add(span[..end].ToString());
            var terminatorLength = span[end] == '\r' && end + 1 < span.Length && span[end + 1] == '\n' ? 2 : 1;
            span = span[(end + terminatorLength)..];
        }

        return lines;
    }
}
