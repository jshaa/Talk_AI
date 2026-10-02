using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using TalkPro.Tools.SampleGenerator.Corpus;
using TalkPro.Tools.SampleGenerator.Formats;

namespace TalkPro.Tools.SampleGenerator.Output;

/// <summary>
/// Writes <c>manifest.json</c>: the list of golden files and the expected parse result of every sample.
/// Output is deterministic (fixed property order, <c>\n</c> newlines, invariant formatting).
/// </summary>
public static class ManifestWriter
{
    public const int ManifestVersion = 1;

    public static byte[] Write(ulong seed, IReadOnlyList<GoldenSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = true,
            NewLine = "\n",
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        }))
        {
            json.WriteStartObject();
            json.WriteString("generator", "TalkPro.SampleGenerator");
            json.WriteNumber("manifestVersion", ManifestVersion);
            json.WriteNumber("seed", seed);
            json.WriteString("notice", SyntheticVocabulary.Notice);
            json.WriteNumber("maxLineLength", SampleBuilder.MaxLineLength);
            json.WriteStartArray("samples");
            foreach (var sample in samples)
            {
                WriteSample(json, sample);
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }

    private static void WriteSample(Utf8JsonWriter json, GoldenSample sample)
    {
        var rendered = sample.Rendered;
        json.WriteStartObject();
        json.WriteString("id", sample.Id);
        json.WriteString("case", sample.Case.Id);
        json.WriteString("format", sample.Format.FormatId);
        json.WriteString("description", sample.Case.Description);
        json.WriteString("lineEnding", sample.Case.LineEnding.ToString());
        json.WriteBoolean("finalNewline", sample.Case.FinalNewline);
        json.WriteString("status", rendered.Lines.Count == 0 ? "EmptyInput" : "Success");
        json.WriteNumber("totalLines", rendered.Lines.Count);

        json.WriteStartArray("files");
        foreach (var file in sample.Files)
        {
            json.WriteStartObject();
            json.WriteString("name", file.Name);
            json.WriteString("encoding", GoldenEncoder.SourceEncodingName(file.Encoding));
            json.WriteNumber("bytes", file.Content.Length);
            json.WriteString("sha256", file.Sha256);
            json.WriteEndObject();
        }

        json.WriteEndArray();

        json.WriteStartObject("statistics");
        json.WriteNumber("totalLines", rendered.Lines.Count);
        foreach (var outcome in Enum.GetValues<ExpectedOutcome>())
        {
            json.WriteNumber(Camel(outcome.ToString()), rendered.Outcomes.Count(o => o.Outcome == outcome));
        }

        json.WriteEndObject();

        json.WriteStartArray("issues");
        for (var i = 0; i < rendered.Outcomes.Count; i++)
        {
            var outcome = rendered.Outcomes[i];
            if (outcome.Issue == ExpectedIssue.None)
            {
                continue;
            }

            json.WriteStartObject();
            json.WriteNumber("line", i + 1);
            json.WriteString("outcome", outcome.Outcome.ToString());
            json.WriteString("issue", outcome.Issue.ToString());
            json.WriteEndObject();
        }

        json.WriteEndArray();

        json.WriteStartArray("entries");
        foreach (var entry in rendered.Entries)
        {
            json.WriteStartObject();
            json.WriteNumber("startLine", entry.StartLine);
            json.WriteNumber("endLine", entry.EndLine);
            json.WriteString("time", entry.LocalTime.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture));
            if (entry.Speaker is null)
            {
                json.WriteNull("speaker");
            }
            else
            {
                json.WriteString("speaker", entry.Speaker);
            }

            json.WriteString("kind", entry.IsSystem ? "System" : "Text");
            json.WriteString("systemEvent", entry.SystemEvent);
            json.WriteString("text", entry.Text);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }

    private static string Camel(string name) => char.ToLowerInvariant(name[0]) + name[1..];
}
