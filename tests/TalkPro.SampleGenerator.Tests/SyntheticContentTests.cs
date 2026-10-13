using System.Text;
using System.Text.RegularExpressions;
using TalkPro.Tools.SampleGenerator.Corpus;
using TalkPro.Tools.SampleGenerator.Output;

namespace TalkPro.SampleGenerator.Tests;

/// <summary>Verifies golden data is synthetic: allow-listed names only and no personal-data patterns.</summary>
public sealed partial class SyntheticContentTests
{
    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", RegexOptions.None, 1000)]
    private static partial Regex Email();

    [GeneratedRegex(@"(?<!\d)(01[016789]|\+82)[-. ]?\d{3,4}[-. ]?\d{4}(?!\d)", RegexOptions.None, 1000)]
    private static partial Regex Phone();

    [GeneratedRegex(@"(?<!\d)\d{6}-?[1-8]\d{6}(?!\d)", RegexOptions.None, 1000)]
    private static partial Regex ResidentNumber();

    [GeneratedRegex(@"(?<!\d)\d{4}[- ]?\d{4}[- ]?\d{4}[- ]?\d{4}(?!\d)", RegexOptions.None, 1000)]
    private static partial Regex CardNumber();

    [GeneratedRegex(@"[A-Za-z]:\\Users\\|/home/|/Users/", RegexOptions.None, 1000)]
    private static partial Regex UserPath();

    public static TheoryData<string> TextFiles =>
        // manifest.json is excluded: its SHA-256 hex strings can contain digit runs by chance.
        [.. GoldenCorpusGenerator.Generate().Select(f => f.RelativePath)
            .Where(p => p != GoldenCorpusGenerator.ManifestFileName && (p.EndsWith(".txt", StringComparison.Ordinal) || p.EndsWith(".json", StringComparison.Ordinal)))];

    [Theory]
    [MemberData(nameof(TextFiles))]
    public void GoldenFilesContainNoPersonalDataPatterns(string relativePath)
    {
        var text = Decode(GoldenCorpusGenerator.Generate().Single(f => f.RelativePath == relativePath).Content.AsSpan());

        Assert.False(Email().IsMatch(text), "e-mail address");
        Assert.False(Phone().IsMatch(text), "phone number");
        Assert.False(ResidentNumber().IsMatch(text), "resident registration number");
        Assert.False(CardNumber().IsMatch(text), "card number");
        Assert.False(UserPath().IsMatch(text), "user profile path");
    }

    [Fact]
    public void EveryAcceptedSpeakerCarriesASyntheticMarker()
    {
        Assert.All(SyntheticVocabulary.AcceptedSpeakers, name =>
            Assert.Contains(SyntheticVocabulary.SyntheticMarkers, marker => name.Contains(marker, StringComparison.Ordinal)));
    }

    [Fact]
    public void CorpusUsesOnlyAllowListedSpeakers()
    {
        var speakers = SyntheticCorpus.Build(GoldenCorpusGenerator.DefaultSeed)
            .SelectMany(c => c.Items)
            .OfType<MessageItem>()
            .Select(m => m.Speaker)
            .Distinct(StringComparer.Ordinal);

        Assert.All(speakers, s => Assert.Contains(s, SyntheticVocabulary.AcceptedSpeakers));
    }

    [Fact]
    public void ManifestDeclaresTheDataSynthetic()
    {
        var manifest = Encoding.UTF8.GetString(GoldenCorpusGenerator.Generate().Single(f => f.RelativePath == GoldenCorpusGenerator.ManifestFileName).Content.AsSpan());

        Assert.Contains("100% synthetic test data", manifest, StringComparison.Ordinal);
    }

    private static string Decode(ReadOnlySpan<byte> bytes) =>
        System.Text.Unicode.Utf8.IsValid(bytes) ? Encoding.UTF8.GetString(bytes) : TryCp949(bytes);

    private static string TryCp949(ReadOnlySpan<byte> bytes)
    {
        try
        {
            return TextEncoders.StrictCp949.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return string.Empty; // deliberately invalid-encoding sample
        }
    }
}
