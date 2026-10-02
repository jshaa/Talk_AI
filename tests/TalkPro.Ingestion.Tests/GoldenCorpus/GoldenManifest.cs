using System.Text.Json;
using TalkPro.Tests.Shared;

namespace TalkPro.Ingestion.Tests.GoldenCorpus;

/// <summary>Typed view of <c>Golden/manifest.json</c>, written by tools/SampleGenerator (the independent oracle).</summary>
internal sealed record GoldenManifest(string Generator, int ManifestVersion, ulong Seed, string Notice, int MaxLineLength, IReadOnlyList<GoldenSample> Samples)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static GoldenManifest Instance { get; } = Load();

    private static GoldenManifest Load()
    {
        var path = Path.Combine(RepositoryPaths.GoldenDirectory, "manifest.json");
        return JsonSerializer.Deserialize<GoldenManifest>(File.ReadAllBytes(path), Options)
            ?? throw new InvalidOperationException("Golden manifest is empty.");
    }
}

internal sealed record GoldenSample(
    string Id,
    string Case,
    string Format,
    string Description,
    string LineEnding,
    bool FinalNewline,
    string Status,
    int TotalLines,
    IReadOnlyList<GoldenFileInfo> Files,
    GoldenStatistics Statistics,
    IReadOnlyList<GoldenIssue> Issues,
    IReadOnlyList<GoldenEntry> Entries)
{
    public override string ToString() => Id;
}

internal sealed record GoldenFileInfo(string Name, string Encoding, int Bytes, string Sha256);

internal sealed record GoldenStatistics(
    int TotalLines,
    int MessageStarted,
    int Continuation,
    int SystemMessage,
    int Metadata,
    int Ignored,
    int Malformed,
    int Unsupported);

internal sealed record GoldenIssue(int Line, string Outcome, string Issue);

internal sealed record GoldenEntry(int StartLine, int EndLine, string Time, string? Speaker, string Kind, string SystemEvent, string Text);
