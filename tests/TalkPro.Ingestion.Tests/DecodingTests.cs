using System.Text;
using TalkPro.Core.Model;
using TalkPro.Ingestion.Import;

namespace TalkPro.Ingestion.Tests;

public sealed class DecodingTests
{
    private static readonly Encoding Cp949 = CodePagesEncodingProvider.Instance.GetEncoding(949)!;

    [Fact]
    public void Utf8WithoutBom()
    {
        Assert.True(EncodingDetector.TryDecode(Encoding.UTF8.GetBytes("합성 테스트"), out var encoding, out var text));

        Assert.Equal(SourceEncoding.Utf8, encoding);
        Assert.Equal("합성 테스트", text);
    }

    [Fact]
    public void Utf8WithBomStripsTheBom()
    {
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("합성")];

        Assert.True(EncodingDetector.TryDecode(bytes, out var encoding, out var text));

        Assert.Equal(SourceEncoding.Utf8WithBom, encoding);
        Assert.Equal("합성", text);
    }

    [Fact]
    public void Cp949IsDetectedAfterStrictUtf8Fails()
    {
        Assert.True(EncodingDetector.TryDecode(Cp949.GetBytes("합성 테스트 똠"), out var encoding, out var text));

        Assert.Equal(SourceEncoding.Cp949, encoding);
        Assert.Equal("합성 테스트 똠", text);
    }

    [Fact]
    public void BomOnlyDecodesToEmptyText()
    {
        Assert.True(EncodingDetector.TryDecode([0xEF, 0xBB, 0xBF], out var encoding, out var text));

        Assert.Equal(SourceEncoding.Utf8WithBom, encoding);
        Assert.Empty(text);
    }

    [Theory]
    [InlineData(new byte[] { 0xEF, 0xBB, 0xBF, 0xB0, 0xA1 })] // BOM followed by CP949 bytes: never reinterpreted.
    [InlineData(new byte[] { 0xFF, 0xFE, 0x41, 0x00 })]       // UTF-16 LE BOM: unsupported.
    [InlineData(new byte[] { 0x81 })]                         // Truncated CP949 lead byte.
    public void UndecodableInputIsRejectedWithoutSubstitution(byte[] bytes)
    {
        Assert.False(EncodingDetector.TryDecode(bytes, out _, out var text));
        Assert.Null(text);
    }

    [Fact]
    public void DecodingDoesNotRegisterCodePagesGlobally()
    {
        EncodingDetector.TryDecode(Cp949.GetBytes("합성"), out _, out _);

        Assert.ThrowsAny<Exception>(() => Encoding.GetEncoding(949));
    }

    [Theory]
    [InlineData("", new string[0])]
    [InlineData("a", new[] { "a" })]
    [InlineData("a\r\n", new[] { "a" })]
    [InlineData("a\n", new[] { "a" })]
    [InlineData("a\r\nb", new[] { "a", "b" })]
    [InlineData("a\nb\r\nc\n", new[] { "a", "b", "c" })]
    [InlineData("\r\n", new[] { "" })]
    [InlineData("\n\n", new[] { "", "" })]
    [InlineData("a\rb\r\n", new[] { "a\rb" })]
    [InlineData("a\r\r\n", new[] { "a\r" })]
    public void LinesSplitOnLfAndCrLfOnly(string text, string[] expected)
    {
        Assert.Equal(expected, LineSplitter.Split(text));
    }
}
