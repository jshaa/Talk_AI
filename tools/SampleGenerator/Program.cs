using System.Globalization;
using TalkPro.Tools.SampleGenerator.Corpus;
using TalkPro.Tools.SampleGenerator.Output;

// Usage: dotnet run --project tools/SampleGenerator -- --out tests/TalkPro.Ingestion.Tests/Golden [--seed 20261002]
// Generates 100% synthetic golden data. No network, no input files.
string? output = null;
var seed = SampleCatalog.DefaultSeed;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--out" when i + 1 < args.Length:
            output = args[++i];
            break;
        case "--seed" when i + 1 < args.Length && ulong.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var parsed):
            seed = parsed;
            i++;
            break;
        default:
            Console.Error.WriteLine("Usage: TalkPro.SampleGenerator --out <.../Golden> [--seed <ulong>]");
            return 2;
    }
}

if (output is null || !GoldenWriter.IsGoldenDirectory(output))
{
    Console.Error.WriteLine("--out must name a directory called 'Golden'.");
    return 2;
}

var set = GoldenSet.Build(seed);
var deleted = GoldenWriter.Write(set, output);
Console.WriteLine(string.Create(
    CultureInfo.InvariantCulture,
    $"Wrote {set.Samples.Count} samples, {set.Files.Count()} files and {GoldenSet.ManifestFileName} (seed {seed}); removed {deleted} stale files."));
return 0;
