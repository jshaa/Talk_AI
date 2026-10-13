using System.Text;
using System.Text.Json;
using TalkPro.Tools.SampleGenerator.Corpus;
using TalkPro.Tools.SampleGenerator.Output;

namespace TalkPro.SampleGenerator.Tests;

public sealed class GeneratorTests
{
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];

    [Fact]
    public void SameSeedProducesByteForByteIdenticalOutput()
    {
        var first = GoldenCorpusGenerator.Generate();
        var second = GoldenCorpusGenerator.Generate();

        Assert.Equal(first.Select(f => f.RelativePath), second.Select(f => f.RelativePath));
        Assert.All(first.Zip(second), pair => Assert.True(pair.First.Content.AsSpan().SequenceEqual(pair.Second.Content.AsSpan()), pair.First.RelativePath));
    }

    [Fact]
    public void GeneratedPathsAreUnique()
    {
        var paths = GoldenCorpusGenerator.Generate().Select(f => f.RelativePath).ToList();

        Assert.Equal(paths.Count, paths.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void SeedOnlyAffectsFilesWithRandomFiller()
    {
        var a = GoldenCorpusGenerator.Generate(1).ToDictionary(f => f.RelativePath);
        var b = GoldenCorpusGenerator.Generate(2).ToDictionary(f => f.RelativePath);

        Assert.False(a["w1/robustness.utf8.txt"].Content.AsSpan().SequenceEqual(b["w1/robustness.utf8.txt"].Content.AsSpan()));
        Assert.True(a["w1/single-message.utf8.txt"].Content.AsSpan().SequenceEqual(b["w1/single-message.utf8.txt"].Content.AsSpan()));
    }

    [Fact]
    public void CommittedGoldenFilesMatchTheGenerator()
    {
        foreach (var file in GoldenCorpusGenerator.Generate())
        {
            var path = GoldenLocation.PathOf(file.RelativePath);
            Assert.True(File.Exists(path), $"Missing golden file {file.RelativePath}; run the SampleGenerator.");
            Assert.True(File.ReadAllBytes(path).AsSpan().SequenceEqual(file.Content.AsSpan()), $"Golden file out of date: {file.RelativePath}");
        }
    }

    [Fact]
    public void GoldenFolderContainsOnlyGeneratedFiles()
    {
        // Guards the git-allowed Golden path against accidentally added real exports.
        var expected = GoldenCorpusGenerator.Generate().Select(f => f.RelativePath).Order(StringComparer.Ordinal).ToList();
        var actual = Directory.EnumerateFiles(GoldenLocation.Directory, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(GoldenLocation.Directory, p).Replace(Path.DirectorySeparatorChar, '/'))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ManifestHashesMatchFileContents()
    {
        var files = GoldenCorpusGenerator.Generate().ToDictionary(f => f.RelativePath);
        using var manifest = JsonDocument.Parse(files[GoldenCorpusGenerator.ManifestFileName].Content.AsMemory());

        foreach (var entry in manifest.RootElement.GetProperty("files").EnumerateArray())
        {
            var path = entry.GetProperty("path").GetString()!;
            var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(files[path].Content.AsSpan()));
            Assert.Equal(entry.GetProperty("sha256").GetString(), hash);
            Assert.True(files.ContainsKey(entry.GetProperty("expected").GetString()!), path);
        }
    }

    [Fact]
    public void Utf8BomFilesStartWithBomAndUtf8FilesDoNot()
    {
        var files = GoldenCorpusGenerator.Generate().Where(f => f.RelativePath.EndsWith(".txt", StringComparison.Ordinal)).ToList();

        Assert.All(files.Where(f => f.RelativePath.EndsWith(".utf8bom.txt", StringComparison.Ordinal)), f => Assert.True(f.Content.AsSpan().StartsWith(Bom), f.RelativePath));
        Assert.All(files.Where(f => f.RelativePath.EndsWith(".utf8.txt", StringComparison.Ordinal)), f => Assert.False(f.Content.AsSpan().StartsWith(Bom), f.RelativePath));
    }

    [Theory]
    [InlineData("w1")]
    [InlineData("a1")]
    [InlineData("i1")]
    public void Cp949FilesAreRealCp949WithTheSameLogicalText(string folder)
    {
        var files = GoldenCorpusGenerator.Generate().ToDictionary(f => f.RelativePath);
        foreach (var cp949 in files.Keys.Where(k => k.StartsWith(folder + "/", StringComparison.Ordinal) && k.EndsWith(".cp949.txt", StringComparison.Ordinal)))
        {
            var utf8Path = cp949.Replace(".cp949.txt", ".utf8.txt", StringComparison.Ordinal);
            var cpBytes = files[cp949].Content.AsSpan();

            Assert.False(System.Text.Unicode.Utf8.IsValid(cpBytes), $"{cp949} is not distinguishable from UTF-8");
            Assert.Equal(Encoding.UTF8.GetString(files[utf8Path].Content.AsSpan()), TextEncoders.StrictCp949.GetString(cpBytes));
        }
    }

    [Theory]
    [InlineData("😀")]
    [InlineData("𠀀")]
    [InlineData("Ελληνικά")]
    public void Cp949PolicyFailsLoudlyOnUnrepresentableCharacters(string text)
    {
        Assert.Throws<EncoderFallbackException>(() => TextEncoders.Encode(text, GoldenEncoding.Cp949));
    }

    [Fact]
    public void UnicodeCaseIsNeverGeneratedInCp949()
    {
        var unicode = SyntheticCorpus.Build(GoldenCorpusGenerator.DefaultSeed).Single(c => c.Id == "unicode");

        Assert.DoesNotContain(GoldenEncoding.Cp949, unicode.Encodings);
    }

    [Fact]
    public void LogicallyEqualSamplesDifferOnlyInEncodingAndLineEndings()
    {
        var files = GoldenCorpusGenerator.Generate().ToDictionary(f => f.RelativePath);
        var crlf = Encoding.UTF8.GetString(files["a1/conversation.utf8.txt"].Content.AsSpan());
        var lf = Encoding.UTF8.GetString(files["a1/conversation-lf-nofinal.utf8.txt"].Content.AsSpan());

        Assert.Equal(crlf.TrimEnd('\r', '\n').Replace("\r\n", "\n", StringComparison.Ordinal), lf);
    }

    [Theory]
    [InlineData(@"C:\work\tests\Golden", true)]
    [InlineData(@"C:\work\tests\Golden\", true)]
    [InlineData(@"C:\work\tests\NotGolden", false)]
    [InlineData(@"C:\Users\someone\Documents", false)]
    [InlineData(@"C:\work\golden", false)]
    public void OutputIsOnlyAllowedIntoAGoldenFolder(string path, bool allowed)
    {
        Assert.Equal(allowed, OutputPolicy.IsAllowedOutput(path));
    }
}
