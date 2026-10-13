using System.Text.Json;
using TalkPro.Core.Parsing;
using TalkPro.Ingestion.Formats.Synthetic;
using TalkPro.Ingestion.Parsing;

namespace TalkPro.Ingestion.Tests.GoldenTests;

internal sealed record GoldenEntry(string Path, string Case, string? Format, string Encoding, string ExpectedPath);

/// <summary>Reads the synthetic golden corpus copied to the test output directory.</summary>
internal static class GoldenData
{
    public static readonly TimeZoneInfo SyntheticZone =
        TimeZoneInfo.CreateCustomTimeZone("TalkPro-Synthetic+09", TimeSpan.FromHours(9), "Synthetic +09:00", "Synthetic +09:00");

    public static readonly ParseContext Context = new() { TimeZone = SyntheticZone };

    private static readonly string Root = System.IO.Path.Combine(AppContext.BaseDirectory, "Golden");

    public static IReadOnlyList<GoldenEntry> Entries { get; } = LoadManifest();

    public static ChatExportParser Parser { get; } = new(SyntheticFormats.All);

    public static byte[] Bytes(string relativePath) => File.ReadAllBytes(PathOf(relativePath));

    public static JsonDocument Expected(GoldenEntry entry) => JsonDocument.Parse(File.ReadAllBytes(PathOf(entry.ExpectedPath)));

    public static ChatParseOutcome Parse(string relativePath) => Parser.Parse(Bytes(relativePath), Context);

    public static GoldenEntry Entry(string relativePath) => Entries.Single(e => e.Path == relativePath);

    private static string PathOf(string relativePath) =>
        System.IO.Path.Combine(Root, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));

    private static List<GoldenEntry> LoadManifest()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(PathOf("manifest.json")));
        return [.. manifest.RootElement.GetProperty("files").EnumerateArray().Select(e => new GoldenEntry(
            e.GetProperty("path").GetString()!,
            e.GetProperty("case").GetString()!,
            e.GetProperty("format").GetString(),
            e.GetProperty("encoding").GetString()!,
            e.GetProperty("expected").GetString()!))];
    }
}
