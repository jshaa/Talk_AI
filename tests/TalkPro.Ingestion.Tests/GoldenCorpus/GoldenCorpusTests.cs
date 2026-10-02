using System.Globalization;
using System.Security.Cryptography;
using TalkPro.Core.Model;
using TalkPro.Core.Parsing;
using TalkPro.Ingestion.Import;
using TalkPro.Ingestion.Synthetic;
using TalkPro.Tests.Shared;

namespace TalkPro.Ingestion.Tests.GoldenCorpus;

/// <summary>
/// Parses every golden file with content-based detection and compares the result with the
/// expectations the SampleGenerator derived from its logical corpus (not from parser code).
/// </summary>
public sealed class GoldenCorpusTests
{
    private static readonly ParseContext Context = new() { TimeZone = TestTimeZone.Plus9 };

    private static readonly ChatExportParser Parser = new(SyntheticAdapters.All);

    public static TheoryData<string> SampleIds => [.. GoldenManifest.Instance.Samples.Select(s => s.Id)];

    public static TheoryData<string> FileNames => [.. GoldenManifest.Instance.Samples.SelectMany(s => s.Files).Select(f => f.Name)];

    [Theory]
    [MemberData(nameof(FileNames))]
    public void FileMatchesTheExpectedLogicalResult(string fileName)
    {
        var sample = GoldenManifest.Instance.Samples.Single(s => s.Files.Any(f => f.Name == fileName));
        var file = sample.Files.Single(f => f.Name == fileName);

        var result = Parser.Parse(Read(file), Context);

        Assert.Equal(Enum.Parse<ImportStatus>(sample.Status), result.Status);
        if (result.Status != ImportStatus.Success)
        {
            Assert.Null(result.Chat);
            return;
        }

        var chat = result.Chat!;
        Assert.Equal(sample.Format, chat.FormatId.Value);
        Assert.Equal(Enum.Parse<SourceEncoding>(file.Encoding), chat.Encoding);

        AssertStatistics(sample.Statistics, chat.Statistics);
        Assert.Equal(
            sample.Issues.Select(i => (i.Line, Enum.Parse<LineOutcome>(i.Outcome), Enum.Parse<ParseIssue>(i.Issue))),
            chat.Issues.Select(i => (i.LineNumber, i.Outcome, i.Issue)));
        AssertMessages(sample.Entries, chat);
    }

    /// <summary>UTF-8, UTF-8 BOM and CP949 variants of one logical sample must give identical results.</summary>
    [Theory]
    [MemberData(nameof(SampleIds))]
    public void EncodingsOfOneSampleParseIdentically(string sampleId)
    {
        var sample = GoldenManifest.Instance.Samples.Single(s => s.Id == sampleId);
        var chats = sample.Files.Select(f => Parser.Parse(Read(f), Context)).ToList();

        var reference = chats[0];
        Assert.All(chats, chat =>
        {
            Assert.Equal(reference.Status, chat.Status);
            if (chat.Chat is null)
            {
                return;
            }

            Assert.Equal(reference.Chat!.FormatId, chat.Chat.FormatId);
            Assert.Equal(reference.Chat.Messages, chat.Chat.Messages);
            Assert.Equal(reference.Chat.Issues, chat.Chat.Issues);
            Assert.Equal(reference.Chat.Statistics, chat.Chat.Statistics);
            Assert.Equal(
                reference.Chat.Participants.Select(p => (p.Id, p.DisplayName, p.Consent)),
                chat.Chat.Participants.Select(p => (p.Id, p.DisplayName, p.Consent)));
        });
    }

    /// <summary>The explicitly chosen adapter gives the same result as detection (detection only selects).</summary>
    [Theory]
    [MemberData(nameof(SampleIds))]
    public void ExplicitAdapterMatchesDetection(string sampleId)
    {
        var sample = GoldenManifest.Instance.Samples.Single(s => s.Id == sampleId);
        var adapter = SyntheticAdapters.All.Single(a => a.FormatId.Value == sample.Format);
        var bytes = Read(sample.Files[0]);

        var detected = Parser.Parse(bytes, Context);
        var explicitResult = ChatExportParser.Parse(bytes, Context, adapter);

        Assert.Equal(detected.Status, explicitResult.Status);
        Assert.Equal(detected.Chat?.Messages, explicitResult.Chat?.Messages);
    }

    /// <summary>
    /// Phase 1 gate (BACKLOG): line classification success >= 99 % on the synthetic corpus. A file's lines
    /// count as matched only if its statistics and its complete issue list equal the expectation.
    /// </summary>
    [Fact]
    public void PhaseOneGateLineClassificationRate()
    {
        int total = 0, matched = 0;
        foreach (var sample in GoldenManifest.Instance.Samples.Where(s => s.Status == "Success"))
        {
            foreach (var file in sample.Files)
            {
                var chat = Parser.Parse(Read(file), Context).Chat;
                total += sample.TotalLines;
                if (chat is not null && Matches(sample, chat))
                {
                    matched += sample.TotalLines;
                }
            }
        }

        Assert.True(total > 2_000, "Golden corpus unexpectedly small.");
        Assert.True(matched >= total * 0.99, $"Line classification rate {matched}/{total} is below 99 %.");
    }

    [Fact]
    public void ManifestNamesMatchCoreEnums()
    {
        var manifest = GoldenManifest.Instance;

        Assert.All(manifest.Samples.SelectMany(s => s.Issues), i =>
        {
            Assert.True(Enum.TryParse<LineOutcome>(i.Outcome, out _), i.Outcome);
            Assert.True(Enum.TryParse<ParseIssue>(i.Issue, out _), i.Issue);
        });
        Assert.All(manifest.Samples.SelectMany(s => s.Entries), e =>
        {
            Assert.True(Enum.TryParse<MessageKind>(e.Kind, out _), e.Kind);
            Assert.True(Enum.TryParse<SystemEventType>(e.SystemEvent, out _), e.SystemEvent);
        });
        Assert.Equal(new ParseContext { TimeZone = TestTimeZone.Plus9 }.MaxLineLength, manifest.MaxLineLength);
    }

    [Fact]
    public void ManifestCoversAllFormatsAndEncodings()
    {
        var samples = GoldenManifest.Instance.Samples;

        Assert.Equal(
            [SyntheticAndroidAdapter.Id.Value, SyntheticIosAdapter.Id.Value, SyntheticWindowsAdapter.Id.Value],
            samples.Select(s => s.Format).Distinct().Order(StringComparer.Ordinal));
        Assert.Equal(
            ["Cp949", "Utf8", "Utf8WithBom"],
            samples.SelectMany(s => s.Files).Select(f => f.Encoding).Distinct().Order(StringComparer.Ordinal));
    }

    private static byte[] Read(GoldenFileInfo file)
    {
        var bytes = File.ReadAllBytes(Path.Combine(RepositoryPaths.GoldenDirectory, file.Name));
        Assert.Equal(file.Bytes, bytes.Length);
        Assert.Equal(file.Sha256, Convert.ToHexStringLower(SHA256.HashData(bytes)));
        return bytes;
    }

    private static bool Matches(GoldenSample sample, ParsedChat chat)
    {
        var s = sample.Statistics;
        var a = chat.Statistics;
        return (s.TotalLines, s.MessageStarted, s.SystemMessage, s.Continuation, s.Metadata, s.Ignored, s.Malformed, s.Unsupported)
                == (a.TotalLines, a.MessagesStarted, a.SystemMessages, a.Continuations, a.Metadata, a.Ignored, a.Malformed, a.Unsupported)
            && sample.Issues.Select(i => (i.Line, i.Outcome, i.Issue))
                .SequenceEqual(chat.Issues.Select(i => (i.LineNumber, i.Outcome.ToString(), i.Issue.ToString())));
    }

    private static void AssertStatistics(GoldenStatistics expected, ParseStatistics actual) =>
        Assert.Equal(
            (expected.TotalLines, expected.MessageStarted, expected.SystemMessage, expected.Continuation, expected.Metadata, expected.Ignored, expected.Malformed, expected.Unsupported),
            (actual.TotalLines, actual.MessagesStarted, actual.SystemMessages, actual.Continuations, actual.Metadata, actual.Ignored, actual.Malformed, actual.Unsupported));

    private static void AssertMessages(IReadOnlyList<GoldenEntry> expected, ParsedChat chat)
    {
        Assert.Equal(expected.Count, chat.Messages.Count);
        var names = chat.Participants.ToDictionary(p => p.Id, p => p.DisplayName);
        for (var i = 0; i < expected.Count; i++)
        {
            var entry = expected[i];
            var message = chat.Messages[i];
            var time = DateTime.ParseExact(entry.Time, "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);

            Assert.Equal(new DateTimeOffset(time, TimeSpan.FromHours(9)), message.Timestamp);
            Assert.Equal(TimeSpan.FromHours(9), message.Timestamp.Offset);
            Assert.Equal(Enum.Parse<MessageKind>(entry.Kind), message.Kind);
            Assert.Equal(Enum.Parse<SystemEventType>(entry.SystemEvent), message.SystemEvent);
            Assert.Equal(entry.Text, message.Text);
            Assert.Equal(entry.StartLine, message.SourceLine);
            Assert.Equal("m" + entry.StartLine.ToString("D7", CultureInfo.InvariantCulture), message.Id.Value);
            Assert.Equal(entry.Speaker, message.SpeakerId is { } id ? names[id] : null);
        }

        // Participants: one per distinct speaker, in order of first appearance, never auto-assigned Owner.
        var speakers = expected.Select(e => e.Speaker).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        Assert.Equal(speakers, chat.Participants.Select(p => p.DisplayName));
        Assert.Equal(
            Enumerable.Range(1, speakers.Count).Select(n => "p_" + n.ToString("D2", CultureInfo.InvariantCulture)),
            chat.Participants.Select(p => p.Id.Value));
        Assert.All(chat.Participants, p => Assert.Equal(ParticipantConsent.ContextOnly, p.Consent));
    }
}
