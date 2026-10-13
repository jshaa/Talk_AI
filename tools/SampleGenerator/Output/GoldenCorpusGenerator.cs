using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using TalkPro.Tools.SampleGenerator.Corpus;
using TalkPro.Tools.SampleGenerator.Writers;

namespace TalkPro.Tools.SampleGenerator.Output;

/// <summary>A generated golden file: path relative to the Golden folder (forward slashes) and exact bytes.</summary>
public sealed record GeneratedFile(string RelativePath, ImmutableArray<byte> Content);

/// <summary>
/// Renders the synthetic corpus into golden export files, expected-result JSON and a manifest.
/// Pure and deterministic: same seed → byte-for-byte identical output. No I/O.
/// </summary>
public static class GoldenCorpusGenerator
{
    public const ulong DefaultSeed = 20261002UL;
    public const string ManifestFileName = "manifest.json";
    public const int TimeZoneOffsetHours = 9;

    private static readonly TimeSpan Offset = TimeSpan.FromHours(TimeZoneOffsetHours);

    private static readonly JsonWriterOptions JsonOptions = new()
    {
        Indented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    public static IReadOnlyList<FormatWriter> Writers { get; } = [new WindowsW1Writer(), new AndroidA1Writer(), new IosI1Writer()];

    public static IReadOnlyList<GeneratedFile> Generate(ulong seed = DefaultSeed)
    {
        var files = new List<GeneratedFile>();
        var manifestEntries = new List<ManifestEntry>();

        foreach (var writer in Writers)
        {
            foreach (var sample in SyntheticCorpus.Build(seed))
            {
                var rendered = Render(writer, sample);
                var expectedPath = $"{writer.Folder}/{sample.Id}.expected.json";
                files.Add(new GeneratedFile(expectedPath, [.. ExpectedJson(writer, sample, rendered)]));

                foreach (var encoding in sample.Encodings)
                {
                    var path = $"{writer.Folder}/{sample.Id}.{EncodingSuffix(encoding)}.txt";
                    var bytes = TextEncoders.Encode(Join(rendered.Lines, sample.LineEnding, sample.FinalNewline), encoding);
                    files.Add(new GeneratedFile(path, [.. bytes]));
                    manifestEntries.Add(new ManifestEntry(path, sample.Id, writer.FormatId, encoding, sample.LineEnding, sample.FinalNewline, expectedPath, Sha256(bytes)));
                }
            }
        }

        var writtenExpectations = new HashSet<string>(StringComparer.Ordinal);
        foreach (var common in CommonFiles())
        {
            files.Add(common.File);
            if (writtenExpectations.Add(common.ExpectedPath))
            {
                files.Add(new GeneratedFile(common.ExpectedPath, [.. FailureJson(common.CaseId, common.ExpectedFailure)]));
            }

            manifestEntries.Add(new ManifestEntry(common.File.RelativePath, common.CaseId, null, common.Encoding, null, null, common.ExpectedPath, Sha256([.. common.File.Content])));
        }

        files.Add(new GeneratedFile(ManifestFileName, [.. ManifestJson(seed, manifestEntries)]));
        return files;
    }

    public static string EncodingSuffix(GoldenEncoding encoding) => encoding switch
    {
        GoldenEncoding.Utf8 => "utf8",
        GoldenEncoding.Utf8Bom => "utf8bom",
        GoldenEncoding.Cp949 => "cp949",
        _ => throw new ArgumentOutOfRangeException(nameof(encoding)),
    };

    internal static RenderedCase Render(FormatWriter writer, SampleCase sample)
    {
        var result = new RenderedCase();
        if (sample.IncludeHeader)
        {
            result.Lines.AddRange(writer.HeaderLines);
            result.Metadata += writer.HeaderLines.Count(l => l.Length > 0);
            result.Ignored += writer.HeaderLines.Count(l => l.Length == 0);
        }

        DateOnly? dateContext = null;
        var messageOpen = false;

        foreach (var item in sample.Items)
        {
            switch (item)
            {
                case MessageItem message:
                    EmitDateMarkerIfNeeded(writer, result, ref dateContext, message.Time);
                    RenderMessage(writer, sample, result, message);
                    messageOpen = true;
                    break;

                case SystemItem system:
                    EmitDateMarkerIfNeeded(writer, result, ref dateContext, system.Time);
                    result.Lines.Add(writer.System(system.Time, system.Text));
                    result.Messages.Add(new ExpectedMessage(result.Lines.Count, Stamp(system.Time), null, "System", system.ExpectedEvent, system.Text));
                    result.SystemMessages++;
                    messageOpen = false;
                    break;

                case MalformedItem malformed:
                    var bad = writer.Malformed(malformed.Kind);
                    result.Lines.Add(bad.Line);
                    result.Issues.Add(new ExpectedIssue(result.Lines.Count, "Malformed", bad.Issue));
                    messageOpen = false;
                    break;

                case RawLineItem raw:
                    if (messageOpen && raw.Expectation is RawLineExpectation.Ignored or RawLineExpectation.OrphanLine)
                    {
                        throw new InvalidOperationException($"Case '{sample.Id}': raw line expectation '{raw.Expectation}' requires that no message is open.");
                    }

                    result.Lines.Add(raw.Line);
                    if (raw.Expectation == RawLineExpectation.Ignored)
                    {
                        result.Ignored++;
                    }
                    else
                    {
                        result.Issues.Add(new ExpectedIssue(result.Lines.Count, "Malformed", raw.Expectation.ToString()));
                        messageOpen = false;
                    }

                    break;

                default:
                    throw new InvalidOperationException("Unknown sample item.");
            }
        }

        return result;
    }

    private static void RenderMessage(FormatWriter writer, SampleCase sample, RenderedCase result, MessageItem message)
    {
        if (!writer.IsValidSpeaker(message.Speaker) || !SyntheticVocabulary.AcceptedSpeakers.Contains(message.Speaker))
        {
            throw new InvalidOperationException($"Case '{sample.Id}': speaker is not an accepted synthetic name for {writer.FormatId}.");
        }

        if (message.TextLines.Count == 0 || message.TextLines.Any(l => l.Contains('\r', StringComparison.Ordinal) || l.Contains('\n', StringComparison.Ordinal) || l.Contains('\0', StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"Case '{sample.Id}': message lines must be non-empty and free of CR, LF and NUL.");
        }

        if (message.TextLines.Count > 1 && message.TextLines[^1].Length == 0)
        {
            throw new InvalidOperationException($"Case '{sample.Id}': a message must not end with an empty line (trimmed by parsers).");
        }

        foreach (var continuation in message.TextLines.Skip(1))
        {
            if (writer.IsReservedPrefix(continuation))
            {
                throw new InvalidOperationException($"Case '{sample.Id}': continuation line starts with a reserved prefix of {writer.FormatId}.");
            }
        }

        result.Lines.Add(writer.Message(message.Time, message.Speaker, message.TextLines[0]));
        var startLine = result.Lines.Count;
        result.Lines.AddRange(message.TextLines.Skip(1));
        result.Messages.Add(new ExpectedMessage(startLine, Stamp(message.Time), message.Speaker, "Text", "None", string.Join('\n', message.TextLines)));
        result.MessagesStarted++;
        result.Continuations += message.TextLines.Count - 1;
    }

    private static void EmitDateMarkerIfNeeded(FormatWriter writer, RenderedCase result, ref DateOnly? dateContext, DateTime time)
    {
        var date = DateOnly.FromDateTime(time);
        if (!writer.UsesDateMarkers || dateContext == date)
        {
            return;
        }

        result.Lines.Add(writer.DateMarker(date));
        result.Metadata++;
        dateContext = date;
    }

    private static string Join(List<string> lines, LineEnding ending, bool finalNewline)
    {
        var separator = ending == LineEnding.CrLf ? "\r\n" : "\n";
        var text = string.Join(separator, lines);
        return finalNewline && lines.Count > 0 ? text + separator : text;
    }

    private static string Stamp(DateTime time) =>
        new DateTimeOffset(time, Offset).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);

    private static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static List<CommonFile> CommonFiles() =>
    [
        new("empty", new GeneratedFile("common/empty.utf8.txt", []), GoldenEncoding.Utf8, "Empty"),
        new("empty", new GeneratedFile("common/empty.utf8bom.txt", [0xEF, 0xBB, 0xBF]), GoldenEncoding.Utf8Bom, "Empty"),
        new("blank-lines", new GeneratedFile("common/blank-lines.utf8.txt", [.. Encoding.ASCII.GetBytes("\r\n\r\n   \r\n\t\r\n")]), GoldenEncoding.Utf8, "UnknownFormat"),
        // 0x81 0x20: CP949 lead byte followed by an invalid trail byte; also invalid UTF-8.
        // (Single 0x80/0xFF are NOT rejected: .NET CP949 maps them to U+0080 / U+F8F7.)
        new("invalid-encoding", new GeneratedFile("common/invalid-encoding.txt", [0x81, 0x20, 0x41, 0x81, 0x20]), GoldenEncoding.Utf8, "UnsupportedEncoding"),
    ];

    private static byte[] ExpectedJson(FormatWriter writer, SampleCase sample, RenderedCase rendered) => WriteJson(json =>
    {
        json.WriteStartObject();
        json.WriteNumber("schema", 1);
        json.WriteString("case", sample.Id);
        json.WriteString("description", sample.Description);
        json.WriteString("format", writer.FormatId);
        json.WriteString("timeZoneOffset", "+09:00");
        json.WriteNull("expectedFailure");
        json.WriteNumber("totalLines", rendered.Lines.Count);

        json.WriteStartObject("statistics");
        json.WriteNumber("messagesStarted", rendered.MessagesStarted);
        json.WriteNumber("systemMessages", rendered.SystemMessages);
        json.WriteNumber("continuations", rendered.Continuations);
        json.WriteNumber("metadata", rendered.Metadata);
        json.WriteNumber("ignored", rendered.Ignored);
        json.WriteNumber("malformed", rendered.Issues.Count);
        json.WriteNumber("unsupported", 0);
        json.WriteEndObject();

        json.WriteStartArray("participants");
        foreach (var name in rendered.Messages.Where(m => m.Speaker is not null).Select(m => m.Speaker!).Distinct(StringComparer.Ordinal))
        {
            json.WriteStringValue(name);
        }

        json.WriteEndArray();

        json.WriteStartArray("messages");
        foreach (var m in rendered.Messages)
        {
            json.WriteStartObject();
            json.WriteNumber("startLine", m.StartLine);
            json.WriteString("timestamp", m.Timestamp);
            if (m.Speaker is null)
            {
                json.WriteNull("speaker");
            }
            else
            {
                json.WriteString("speaker", m.Speaker);
            }

            json.WriteString("kind", m.Kind);
            json.WriteString("systemEvent", m.SystemEvent);
            json.WriteString("text", m.Text);
            json.WriteEndObject();
        }

        json.WriteEndArray();

        json.WriteStartArray("issues");
        foreach (var issue in rendered.Issues)
        {
            json.WriteStartObject();
            json.WriteNumber("line", issue.Line);
            json.WriteString("outcome", issue.Outcome);
            json.WriteString("issue", issue.Issue);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();
    });

    private static byte[] FailureJson(string caseId, string failure) => WriteJson(json =>
    {
        json.WriteStartObject();
        json.WriteNumber("schema", 1);
        json.WriteString("case", caseId);
        json.WriteString("expectedFailure", failure);
        json.WriteEndObject();
    });

    private static byte[] ManifestJson(ulong seed, IReadOnlyList<ManifestEntry> entries) => WriteJson(json =>
    {
        json.WriteStartObject();
        json.WriteNumber("schema", 1);
        json.WriteString("generator", "TalkPro.SampleGenerator");
        json.WriteNumber("seed", seed);
        json.WriteString("notice", SyntheticVocabulary.Notice);
        json.WriteString("cp949Policy", "Cases are generated in CP949 only when every character is representable; generation fails on any unrepresentable character (no '?' substitution).");
        json.WriteStartArray("acceptedSpeakers");
        foreach (var name in SyntheticVocabulary.AcceptedSpeakers)
        {
            json.WriteStringValue(name);
        }

        json.WriteEndArray();
        json.WriteStartArray("files");
        foreach (var e in entries)
        {
            json.WriteStartObject();
            json.WriteString("path", e.Path);
            json.WriteString("case", e.CaseId);
            if (e.FormatId is null)
            {
                json.WriteNull("format");
            }
            else
            {
                json.WriteString("format", e.FormatId);
            }

            json.WriteString("encoding", EncodingSuffix(e.Encoding));
            json.WriteString("lineEnding", e.LineEnding?.ToString());
            if (e.FinalNewline is { } finalNewline)
            {
                json.WriteBoolean("finalNewline", finalNewline);
            }
            else
            {
                json.WriteNull("finalNewline");
            }

            json.WriteString("expected", e.ExpectedPath);
            json.WriteString("sha256", e.Sha256);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();
    });

    private static byte[] WriteJson(Action<Utf8JsonWriter> write)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer, JsonOptions))
        {
            write(json);
        }

        buffer.WriteByte((byte)'\n');
        return buffer.ToArray();
    }

    internal sealed class RenderedCase
    {
        public List<string> Lines { get; } = [];

        public List<ExpectedMessage> Messages { get; } = [];

        public List<ExpectedIssue> Issues { get; } = [];

        public int MessagesStarted { get; set; }

        public int SystemMessages { get; set; }

        public int Continuations { get; set; }

        public int Metadata { get; set; }

        public int Ignored { get; set; }
    }

    internal sealed record ExpectedMessage(int StartLine, string Timestamp, string? Speaker, string Kind, string SystemEvent, string Text);

    internal sealed record ExpectedIssue(int Line, string Outcome, string Issue);

    private sealed record ManifestEntry(string Path, string CaseId, string? FormatId, GoldenEncoding Encoding, LineEnding? LineEnding, bool? FinalNewline, string ExpectedPath, string Sha256);

    private sealed record CommonFile(string CaseId, GeneratedFile File, GoldenEncoding Encoding, string ExpectedFailure)
    {
        public string ExpectedPath => $"common/{CaseId}.expected.json";
    }
}
