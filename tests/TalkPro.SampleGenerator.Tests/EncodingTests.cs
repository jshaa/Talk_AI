using System.Text;
using System.Text.Unicode;
using TalkPro.Tools.SampleGenerator.Corpus;
using TalkPro.Tools.SampleGenerator.Output;

namespace TalkPro.SampleGenerator.Tests;

public sealed class EncodingTests
{
    private static readonly GoldenSet Set = GoldenSet.Build(SampleCatalog.DefaultSeed);

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    [Fact]
    public void EveryFormatHasAllThreeEncodings()
    {
        foreach (var prefix in new[] { "w1", "a1", "i1" })
        {
            var encodings = Set.Samples.Where(s => s.Format.Prefix == prefix).SelectMany(s => s.Files).Select(f => f.Encoding).ToHashSet();
            Assert.Equal([GoldenEncoding.Utf8, GoldenEncoding.Utf8Bom, GoldenEncoding.Cp949], encodings.Order());
        }
    }

    [Fact]
    public void Utf8FilesHaveNoBomAndAreValidUtf8()
    {
        var files = Set.Files.Where(f => f.Encoding == GoldenEncoding.Utf8).ToList();

        Assert.NotEmpty(files);
        Assert.All(files, f =>
        {
            Assert.False(f.Content.AsSpan().StartsWith((byte[])[0xEF, 0xBB, 0xBF]), f.Name);
            Assert.True(Utf8.IsValid(f.Content), f.Name);
        });
    }

    [Fact]
    public void Utf8BomFilesAreBomPlusTheUtf8Bytes()
    {
        foreach (var sample in Set.Samples)
        {
            var bom = sample.Files.SingleOrDefault(f => f.Encoding == GoldenEncoding.Utf8Bom);
            if (bom is null)
            {
                continue;
            }

            var plain = sample.Files.Single(f => f.Encoding == GoldenEncoding.Utf8);
            Assert.Equal([0xEF, 0xBB, 0xBF], bom.Content[..3]);
            Assert.Equal(plain.Content, bom.Content[3..]);
        }
    }

    [Fact]
    public void Cp949FilesDecodeStrictlyToTheSameTextAndAreNotUtf8()
    {
        var checkedFiles = 0;
        foreach (var sample in Set.Samples)
        {
            var cp949 = sample.Files.SingleOrDefault(f => f.Encoding == GoldenEncoding.Cp949);
            if (cp949 is null)
            {
                continue;
            }

            var plain = sample.Files.Single(f => f.Encoding == GoldenEncoding.Utf8);
            Assert.Equal(StrictUtf8.GetString(plain.Content), GoldenEncoder.Cp949.GetString(cp949.Content));
            Assert.False(Utf8.IsValid(cp949.Content), cp949.Name);
            checkedFiles++;
        }

        Assert.True(checkedFiles >= 3 * 15, "Expected CP949 variants for most cases.");
    }

    [Fact]
    public void Cp949RoundTripsHangulOutsideKsX1001()
    {
        const string Uhc = "똠방각하 쀍 햏 뷁";

        var bytes = GoldenEncoder.Encode(Uhc, GoldenEncoding.Cp949);

        Assert.Equal(Uhc, GoldenEncoder.Cp949.GetString(bytes));
        Assert.Contains(Set.Files.Where(f => f.Encoding == GoldenEncoding.Cp949), f => GoldenEncoder.Cp949.GetString(f.Content).Contains(Uhc, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("이모지 😀")]
    [InlineData("보충 평면 𠀀")]
    [InlineData("عربي")]
    public void Cp949PolicyFailsLoudlyInsteadOfSubstituting(string text)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => GoldenEncoder.Encode(text, GoldenEncoding.Cp949));

        Assert.IsType<EncoderFallbackException>(ex.InnerException);
    }

    [Fact]
    public void Cp949PolicyRejectsAsciiOnlyTextThatWouldBeDetectedAsUtf8()
    {
        Assert.Throws<InvalidOperationException>(() => GoldenEncoder.Encode("ascii only", GoldenEncoding.Cp949));
    }

    [Fact]
    public void NonCp949CasesAreOnlyGeneratedInUtf8Variants()
    {
        var unicode = Set.Samples.Where(s => s.Case.Id == "unicode").SelectMany(s => s.Files).ToList();

        Assert.NotEmpty(unicode);
        Assert.DoesNotContain(unicode, f => f.Encoding == GoldenEncoding.Cp949);
    }

    [Fact]
    public void GenerationDoesNotRegisterCodePagesGlobally()
    {
        _ = GoldenEncoder.Cp949;

        Assert.ThrowsAny<Exception>(() => Encoding.GetEncoding(949));
    }

    [Fact]
    public void LineEndingsFollowTheCase()
    {
        foreach (var sample in Set.Samples.Where(s => s.Rendered.Lines.Count > 0))
        {
            var text = StrictUtf8.GetString(sample.Files.Single(f => f.Encoding == GoldenEncoding.Utf8).Content);
            var crlf = text.Split("\r\n").Length - 1;
            var lf = text.Split('\n').Length - 1;
            if (sample.Case.LineEnding == LineEnding.Lf)
            {
                Assert.Equal(0, crlf);
            }
            else
            {
                Assert.Equal(lf, crlf);
            }

            Assert.Equal(sample.Case.FinalNewline, text.EndsWith('\n'));
            Assert.Equal(sample.Rendered.Lines.Count - (sample.Case.FinalNewline ? 0 : 1), lf);
        }
    }

    [Fact]
    public void EmptyCaseIsZeroBytesOrBomOnly()
    {
        var empty = Set.Samples.Where(s => s.Case.Id == "empty").SelectMany(s => s.Files).ToList();

        Assert.All(empty.Where(f => f.Encoding == GoldenEncoding.Utf8), f => Assert.Empty(f.Content));
        Assert.All(empty.Where(f => f.Encoding == GoldenEncoding.Utf8Bom), f => Assert.Equal([0xEF, 0xBB, 0xBF], f.Content));
    }
}
