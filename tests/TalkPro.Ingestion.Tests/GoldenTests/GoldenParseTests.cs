using System.Globalization;
using System.Text.Json;
using TalkPro.Core.Model;
using TalkPro.Ingestion.Parsing;

namespace TalkPro.Ingestion.Tests.GoldenTests;

/// <summary>Every golden export must parse into exactly the generator's expected logical result.</summary>
public sealed class GoldenParseTests
{
    public static TheoryData<string> FormatFiles => [.. GoldenData.Entries.Where(e => e.Format is not null).Select(e => e.Path)];

    /// <summary>Supported samples; the robustness case is malformed by design and verified line-by-line above.</summary>
    public static TheoryData<string> SupportedFiles => [.. GoldenData.Entries.Where(e => e.Format is not null && e.Case != "robustness").Select(e => e.Path)];

    public static TheoryData<string> FailureFiles => [.. GoldenData.Entries.Where(e => e.Format is null).Select(e => e.Path)];

    [Theory]
    [MemberData(nameof(FormatFiles))]
    public void ParsedResultMatchesExpectation(string path)
    {
        var entry = GoldenData.Entry(path);
        using var expected = GoldenData.Expected(entry);
        var root = expected.RootElement;

        var outcome = GoldenData.Parse(path);

        Assert.True(outcome.IsSuccess, $"Failure: {outcome.Failure}");
        var chat = outcome.Chat!;
        Assert.Equal(root.GetProperty("format").GetString(), chat.FormatId.Value);
        Assert.Equal(ExpectedEncoding(entry.Encoding), chat.Encoding);
        Assert.Equal(root.GetProperty("totalLines").GetInt32(), chat.Statistics.TotalLines);
        AssertStatistics(root.GetProperty("statistics"), chat.Statistics);
        AssertParticipants(root.GetProperty("participants"), chat);
        AssertMessages(root.GetProperty("messages"), chat);
        AssertIssues(root.GetProperty("issues"), chat);
    }

    [Theory]
    [MemberData(nameof(FailureFiles))]
    public void InvalidInputsFailWithTheExpectedReason(string path)
    {
        using var expected = GoldenData.Expected(GoldenData.Entry(path));

        var outcome = GoldenData.Parse(path);

        Assert.False(outcome.IsSuccess);
        Assert.Null(outcome.Chat);
        Assert.Equal(expected.RootElement.GetProperty("expectedFailure").GetString(), outcome.Failure.ToString());
    }

    [Theory]
    [MemberData(nameof(FormatFiles))]
    public void FormatIsDetectedFromContentAlone(string path)
    {
        // Only bytes are passed to the parser; the file name plays no role.
        var outcome = GoldenData.Parser.Parse(GoldenData.Bytes(path), GoldenData.Context);

        Assert.Equal(GoldenData.Entry(path).Format, outcome.Chat?.FormatId.Value);
    }

    [Theory]
    [MemberData(nameof(SupportedFiles))]
    public void SupportedSamplesMeetTheParseRateGate(string path)
    {
        // Gate (BACKLOG Phase 1): line parse success ≥ 99 % on supported samples.
        var stats = GoldenData.Parse(path).Chat!.Statistics;

        var rate = (double)(stats.TotalLines - stats.Malformed - stats.Unsupported) / Math.Max(1, stats.TotalLines);

        Assert.True(rate >= 0.99, $"Parse rate {rate:P2}");
    }

    private static SourceEncoding ExpectedEncoding(string encoding) => encoding switch
    {
        "utf8" => SourceEncoding.Utf8,
        "utf8bom" => SourceEncoding.Utf8WithBom,
        "cp949" => SourceEncoding.Cp949,
        _ => throw new ArgumentOutOfRangeException(nameof(encoding)),
    };

    private static void AssertStatistics(JsonElement expected, ParseStatistics actual)
    {
        Assert.Equal(expected.GetProperty("messagesStarted").GetInt32(), actual.MessagesStarted);
        Assert.Equal(expected.GetProperty("systemMessages").GetInt32(), actual.SystemMessages);
        Assert.Equal(expected.GetProperty("continuations").GetInt32(), actual.Continuations);
        Assert.Equal(expected.GetProperty("metadata").GetInt32(), actual.Metadata);
        Assert.Equal(expected.GetProperty("ignored").GetInt32(), actual.Ignored);
        Assert.Equal(expected.GetProperty("malformed").GetInt32(), actual.Malformed);
        Assert.Equal(expected.GetProperty("unsupported").GetInt32(), actual.Unsupported);
    }

    private static void AssertParticipants(JsonElement expected, ParsedChat chat)
    {
        var names = expected.EnumerateArray().Select(e => e.GetString()!).ToList();

        Assert.Equal(names, chat.Participants.Select(p => p.DisplayName));
        Assert.Equal(
            names.Select((_, i) => string.Create(CultureInfo.InvariantCulture, $"p_{i + 1:D2}")),
            chat.Participants.Select(p => p.Id.Value));
        Assert.All(chat.Participants, p => Assert.Equal(ParticipantConsent.ContextOnly, p.Consent));
    }

    private static void AssertMessages(JsonElement expected, ParsedChat chat)
    {
        var expectedMessages = expected.EnumerateArray().ToList();
        Assert.Equal(expectedMessages.Count, chat.Messages.Count);
        var names = chat.Participants.ToDictionary(p => p.Id, p => p.DisplayName);

        for (var i = 0; i < expectedMessages.Count; i++)
        {
            var e = expectedMessages[i];
            var m = chat.Messages[i];
            var line = e.GetProperty("startLine").GetInt32();

            Assert.Equal(line, m.SourceLine);
            Assert.Equal(string.Create(CultureInfo.InvariantCulture, $"m{line:D7}"), m.Id.Value);
            Assert.Equal(DateTimeOffset.Parse(e.GetProperty("timestamp").GetString()!, CultureInfo.InvariantCulture), m.Timestamp);
            Assert.Equal(TimeSpan.FromHours(9), m.Timestamp.Offset);
            Assert.Equal(e.GetProperty("speaker").GetString(), m.SpeakerId is { } id ? names[id] : null);
            Assert.Equal(e.GetProperty("kind").GetString(), m.Kind.ToString());
            Assert.Equal(e.GetProperty("systemEvent").GetString(), m.SystemEvent.ToString());
            Assert.Equal(e.GetProperty("text").GetString(), m.Text);
        }
    }

    private static void AssertIssues(JsonElement expected, ParsedChat chat)
    {
        var expectedIssues = expected.EnumerateArray()
            .Select(e => (e.GetProperty("line").GetInt32(), e.GetProperty("outcome").GetString(), e.GetProperty("issue").GetString()))
            .ToList();

        Assert.Equal(expectedIssues, chat.Issues.Select(i => (i.LineNumber, (string?)i.Outcome.ToString(), (string?)i.Issue.ToString())));
    }
}
