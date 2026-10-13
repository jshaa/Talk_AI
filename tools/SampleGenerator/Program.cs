using System.Globalization;
using TalkPro.Tools.SampleGenerator.Output;

namespace TalkPro.Tools.SampleGenerator;

/// <summary>
/// Usage: TalkPro.SampleGenerator --output &lt;path\to\Golden&gt; [--seed N] [--check]
/// Writes only synthetic data, only into a directory named "Golden". Never reads input files
/// other than comparing its own previous output in --check mode. Never deletes files.
/// </summary>
internal static class Program
{
    private const int Ok = 0;
    private const int Usage = 2;
    private const int Drift = 3;

    private static int Main(string[] args)
    {
        string? output = null;
        var seed = GoldenCorpusGenerator.DefaultSeed;
        var check = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--output" when i + 1 < args.Length:
                    output = args[++i];
                    break;
                case "--seed" when i + 1 < args.Length && ulong.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var parsed):
                    seed = parsed;
                    i++;
                    break;
                case "--check":
                    check = true;
                    break;
                default:
                    return Fail("Usage: TalkPro.SampleGenerator --output <Golden folder> [--seed N] [--check]");
            }
        }

        if (output is null)
        {
            return Fail("--output is required.");
        }

        var root = Path.GetFullPath(output);
        if (!OutputPolicy.IsAllowedOutput(root))
        {
            return Fail("Refusing to write: the output folder must be named 'Golden'.");
        }

        var files = GoldenCorpusGenerator.Generate(seed);
        return check ? Check(root, files) : Write(root, files);
    }

    private static int Write(string root, IReadOnlyList<GeneratedFile> files)
    {
        foreach (var file in files)
        {
            var path = Path.Combine(root, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, file.Content.AsSpan());
        }

        var stale = Extras(root, files);
        Console.WriteLine($"Generated {files.Count} synthetic files.");
        if (stale.Count > 0)
        {
            Console.Error.WriteLine($"{stale.Count} file(s) in the Golden folder are not produced by the generator. Remove them manually:");
            stale.ForEach(Console.Error.WriteLine);
            return Drift;
        }

        return Ok;
    }

    private static int Check(string root, IReadOnlyList<GeneratedFile> files)
    {
        var drift = 0;
        foreach (var file in files)
        {
            var path = Path.Combine(root, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(file.Content.AsSpan()))
            {
                Console.Error.WriteLine($"Out of date: {file.RelativePath}");
                drift++;
            }
        }

        var extras = Extras(root, files);
        extras.ForEach(e => Console.Error.WriteLine($"Unexpected: {e}"));
        drift += extras.Count;
        Console.WriteLine(drift == 0 ? "Golden data is up to date." : $"{drift} difference(s).");
        return drift == 0 ? Ok : Drift;
    }

    private static List<string> Extras(string root, IReadOnlyList<GeneratedFile> files)
    {
        var expected = files.Select(f => f.RelativePath).ToHashSet(StringComparer.Ordinal);
        return [.. Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(root, p).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(p => !expected.Contains(p))
            .Order(StringComparer.Ordinal)];
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return Usage;
    }
}
