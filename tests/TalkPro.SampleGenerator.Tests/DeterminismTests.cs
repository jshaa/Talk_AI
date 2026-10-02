using TalkPro.Tests.Shared;
using TalkPro.Tools.SampleGenerator.Corpus;
using TalkPro.Tools.SampleGenerator.Output;

namespace TalkPro.SampleGenerator.Tests;

public sealed class DeterminismTests
{
    [Fact]
    public void SameSeedProducesByteIdenticalOutput()
    {
        var first = GoldenSet.Build(SampleCatalog.DefaultSeed);
        var second = GoldenSet.Build(SampleCatalog.DefaultSeed);

        Assert.Equal(first.Files.Select(f => f.Name), second.Files.Select(f => f.Name));
        Assert.All(first.Files.Zip(second.Files), pair => Assert.Equal(pair.First.Content, pair.Second.Content));
        Assert.Equal(first.Manifest, second.Manifest);
    }

    [Fact]
    public void DifferentSeedChangesOnlySeededContent()
    {
        var baseline = GoldenSet.Build(SampleCatalog.DefaultSeed);
        var other = GoldenSet.Build(SampleCatalog.DefaultSeed + 1);

        var changed = baseline.Files.Zip(other.Files)
            .Where(pair => !pair.First.Content.AsSpan().SequenceEqual(pair.Second.Content))
            .Select(pair => pair.First.Name.Split('.')[1])
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(["multi-participant", "text-variety"], changed.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void PrngSequenceIsAFixedContract()
    {
        // SplitMix64 reference values for seed 0; guards against accidental changes of the generator.
        var random = new DeterministicRandom(0);

        Assert.Equal(0xE220A8397B1DCDAFUL, random.NextUInt64());
        Assert.Equal(0x6E789E6AA1B965F4UL, random.NextUInt64());
        Assert.Equal(0x06C45D188009454FUL, random.NextUInt64());
    }

    /// <summary>
    /// Checked-in golden data must equal the generator output exactly, and the folder must contain
    /// nothing else (a real export dropped into the git-allowed folder fails this test).
    /// Regenerate with: dotnet run --project tools/SampleGenerator -- --out tests/TalkPro.Ingestion.Tests/Golden
    /// </summary>
    [Fact]
    public void CheckedInGoldenDataMatchesTheGenerator()
    {
        var set = GoldenSet.Build(SampleCatalog.DefaultSeed);
        var expected = set.Files.ToDictionary(f => f.Name, f => f.Content, StringComparer.Ordinal);
        expected.Add(GoldenSet.ManifestFileName, set.Manifest);

        var actual = Directory.EnumerateFiles(RepositoryPaths.GoldenDirectory, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(RepositoryPaths.GoldenDirectory, path))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), actual);
        Assert.All(expected, pair => Assert.True(
            pair.Value.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(RepositoryPaths.GoldenDirectory, pair.Key))),
            pair.Key + " differs from the generator output."));
    }

    [Fact]
    public void WriterOnlyAcceptsTheGoldenDirectory()
    {
        var set = GoldenSet.Build(SampleCatalog.DefaultSeed);
        var elsewhere = Path.Combine(Path.GetTempPath(), "talkpro-tests", Guid.NewGuid().ToString("N"), "exports");

        Assert.Throws<ArgumentException>(() => GoldenWriter.Write(set, elsewhere));
        Assert.False(Directory.Exists(elsewhere));
        Assert.True(GoldenWriter.IsGoldenDirectory(Path.Combine("x", "Golden") + Path.DirectorySeparatorChar));
        Assert.False(GoldenWriter.IsGoldenDirectory(Path.Combine("x", "golden")));
    }

    [Fact]
    public void WriterRemovesOnlyItsOwnStaleFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "talkpro-tests", Guid.NewGuid().ToString("N"));
        var golden = Path.Combine(root, GoldenWriter.GoldenDirectoryName);
        Directory.CreateDirectory(golden);
        var stale = Path.Combine(golden, "w1.removed-case.utf8.txt");
        var foreign = Path.Combine(golden, "notes.txt");
        File.WriteAllText(stale, "old");
        File.WriteAllText(foreign, "foreign");

        try
        {
            var deleted = GoldenWriter.Write(GoldenSet.Build(SampleCatalog.DefaultSeed), golden);

            Assert.Equal(1, deleted);
            Assert.False(File.Exists(stale));
            Assert.True(File.Exists(foreign));
            Assert.True(File.Exists(Path.Combine(golden, GoldenSet.ManifestFileName)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
