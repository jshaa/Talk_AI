using System.Security.Cryptography;
using TalkPro.Tools.SampleGenerator.Corpus;
using TalkPro.Tools.SampleGenerator.Formats;

namespace TalkPro.Tools.SampleGenerator.Output;

public sealed record GoldenFile(string Name, GoldenEncoding Encoding, byte[] Content)
{
    public string Sha256 => Convert.ToHexStringLower(SHA256.HashData(Content));
}

/// <summary>One case rendered in one format, with all of its encoded files.</summary>
public sealed record GoldenSample(SampleCase Case, SyntheticFormat Format, RenderedSample Rendered, IReadOnlyList<GoldenFile> Files)
{
    public string Id => Format.Prefix + "." + Case.Id;
}

/// <summary>The complete, in-memory golden data set. Building it performs no I/O.</summary>
public sealed record GoldenSet(ulong Seed, IReadOnlyList<GoldenSample> Samples, byte[] Manifest)
{
    public const string ManifestFileName = "manifest.json";

    public IEnumerable<GoldenFile> Files => Samples.SelectMany(s => s.Files);

    public static GoldenSet Build(ulong seed)
    {
        var samples = new List<GoldenSample>();
        foreach (var sampleCase in SampleCatalog.Create(seed))
        {
            foreach (var format in SyntheticFormat.All)
            {
                if (sampleCase.Formats is { } formats && !formats.Contains(format.Prefix, StringComparer.Ordinal))
                {
                    continue;
                }

                var rendered = format.Render(sampleCase);
                var text = Join(rendered.Lines, sampleCase.LineEnding, sampleCase.FinalNewline);
                var files = sampleCase.Encodings
                    .Select(encoding => new GoldenFile(
                        format.Prefix + "." + sampleCase.Id + "." + GoldenEncoder.FileToken(encoding) + ".txt",
                        encoding,
                        GoldenEncoder.Encode(text, encoding)))
                    .ToList();
                samples.Add(new GoldenSample(sampleCase, format, rendered, files));
            }
        }

        return new GoldenSet(seed, samples, ManifestWriter.Write(seed, samples));
    }

    /// <summary>Joins lines with the case's terminator. An empty file has no lines and no terminator.</summary>
    public static string Join(IReadOnlyList<string> lines, LineEnding lineEnding, bool finalNewline)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0)
        {
            return string.Empty;
        }

        var terminator = lineEnding == LineEnding.CrLf ? "\r\n" : "\n";
        var text = string.Join(terminator, lines);
        return finalNewline ? text + terminator : text;
    }
}
