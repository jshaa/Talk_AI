using System.Text;
using System.Text.RegularExpressions;
using TalkPro.Tools.SampleGenerator.Corpus;
using TalkPro.Tools.SampleGenerator.Formats;
using TalkPro.Tools.SampleGenerator.Output;

namespace TalkPro.SampleGenerator.Tests;

/// <summary>Golden data must be recognisably synthetic and free of personal-data patterns.</summary>
public sealed partial class SyntheticContentTests
{
    private static readonly GoldenSet Set = GoldenSet.Build(SampleCatalog.DefaultSeed);

    [Fact]
    public void EverySpeakerComesFromTheSyntheticVocabulary()
    {
        var speakers = Set.Samples.SelectMany(s => s.Rendered.Entries).Select(e => e.Speaker).OfType<string>().ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(speakers);
        Assert.All(speakers, s => Assert.Contains(s, SyntheticVocabulary.AcceptedSpeakers));
    }

    [Fact]
    public void EveryVocabularyNameCarriesASyntheticMarker()
    {
        var names = SyntheticVocabulary.AcceptedSpeakers.Append(SyntheticVocabulary.TooLongName).Append(SyntheticVocabulary.RoomTitle);

        Assert.All(names, name => Assert.Contains(SyntheticVocabulary.SyntheticMarkers, marker => name.Contains(marker, StringComparison.Ordinal)));
    }

    [Fact]
    public void GoldenTextContainsNoPersonalDataPatterns()
    {
        foreach (var file in Set.Files)
        {
            var text = Decode(file);
            Assert.False(PhoneNumber().IsMatch(text), file.Name + " contains a phone-number-like pattern.");
            Assert.False(EmailAddress().IsMatch(text), file.Name + " contains an e-mail-like pattern.");
            Assert.False(ResidentRegistrationNumber().IsMatch(text), file.Name + " contains an RRN-like pattern.");
            Assert.False(Url().IsMatch(text), file.Name + " contains a URL.");
        }
    }

    [Fact]
    public void ManifestDeclaresTheDataSynthetic()
    {
        var manifest = Encoding.UTF8.GetString(Set.Manifest);

        Assert.Contains(SyntheticVocabulary.Notice, manifest, StringComparison.Ordinal);
        Assert.DoesNotMatch(PhoneNumber(), manifest);
        Assert.DoesNotMatch(EmailAddress(), manifest);
    }

    [Fact]
    public void CorpusCoversTheRequiredBoundaryCases()
    {
        var caseIds = Set.Samples.Select(s => s.Case.Id).ToHashSet(StringComparer.Ordinal);
        string[] required =
        [
            "empty", "header-only", "single-message", "single-participant", "multi-participant", "multiline",
            "line-ending-lf", "no-final-newline", "text-variety", "unicode", "timestamps", "system-messages",
            "names", "malformed", "extra-fields", "truncated-last-record", "blank-lines", "no-header",
            "unsupported-version", "very-long-line", "missing-date-context",
        ];
        Assert.All(required, id => Assert.Contains(id, caseIds));

        var issues = Set.Samples.SelectMany(s => s.Rendered.Outcomes).Select(o => o.Issue).ToHashSet();
        Assert.All(Enum.GetValues<ExpectedIssue>(), issue => Assert.Contains(issue, issues));

        var outcomes = Set.Samples.SelectMany(s => s.Rendered.Outcomes).Select(o => o.Outcome).ToHashSet();
        Assert.All(Enum.GetValues<ExpectedOutcome>(), outcome => Assert.Contains(outcome, outcomes));
    }

    [Fact]
    public void CorpusErrorsAreRejected()
    {
        var format = new W1Format();
        var orphanInsideMessage = new SampleCase(
            "bad",
            "orphan while open",
            [new MessageItem(new DateTime(2026, 10, 1, 9, 0, 0), SyntheticVocabulary.UserA, "x"), new RawLineItem("orphan", RawLineExpectation.OrphanLine)],
            [GoldenEncoding.Utf8]);
        var reservedContinuation = new SampleCase(
            "bad",
            "reserved continuation",
            [new MessageItem(new DateTime(2026, 10, 1, 9, 0, 0), SyntheticVocabulary.UserA, ["x", "[UserA] [09:00] looks like a record"])],
            [GoldenEncoding.Utf8]);

        Assert.Throws<InvalidOperationException>(() => format.Render(orphanInsideMessage));
        Assert.Throws<InvalidOperationException>(() => format.Render(reservedContinuation));
    }

    private static string Decode(GoldenFile file) => file.Encoding switch
    {
        GoldenEncoding.Cp949 => GoldenEncoder.Cp949.GetString(file.Content),
        GoldenEncoding.Utf8Bom => Encoding.UTF8.GetString(file.Content, 3, file.Content.Length - 3),
        _ => Encoding.UTF8.GetString(file.Content),
    };

    [GeneratedRegex(@"(?<![0-9])01[016789][- .]?[0-9]{3,4}[- .]?[0-9]{4}(?![0-9])", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PhoneNumber();

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+\.[A-Za-z]{2,}", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex EmailAddress();

    [GeneratedRegex(@"(?<![0-9])[0-9]{6}-[1-4][0-9]{6}(?![0-9])", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ResidentRegistrationNumber();

    [GeneratedRegex(@"https?://|www\.", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Url();
}
