using System.Globalization;
using System.Text;
using TalkPro.Core.Model;
using TalkPro.Core.Parsing;
using TalkPro.Ingestion.Import;
using TalkPro.Ingestion.RealFormats;

namespace TalkPro.Ingestion.Tests.RealFormats;

/// <summary>Parses the hand-written real-layout fixtures through the normal ingestion path.</summary>
public sealed class RealFormatParsingTests
{
    private static readonly ParseContext Context = TestTimeZone.Context();

    private static readonly ChatExportParser Parser = new(RealFormatAdapters.All);

    private static readonly Encoding StrictCp949 =
        CodePagesEncodingProvider.Instance.GetEncoding(949, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)!;

    public static TheoryData<string, string, string, bool> Variants()
    {
        var data = new TheoryData<string, string, string, bool>();
        foreach (var fixture in RealFormatFixtures.All)
        {
            string[] encodings = fixture.Cp949Compatible ? ["utf8", "utf8bom", "cp949"] : ["utf8", "utf8bom"];
            foreach (var encoding in encodings)
            {
                foreach (var ending in new[] { "\r\n", "\n" })
                {
                    foreach (var finalNewline in new[] { true, false })
                    {
                        data.Add(fixture.Name, encoding, ending == "\r\n" ? "crlf" : "lf", finalNewline);
                    }
                }
            }
        }

        return data;
    }

    public static TheoryData<string> FixtureNames => [.. RealFormatFixtures.All.Select(f => f.Name)];

    [Theory]
    [MemberData(nameof(Variants))]
    public void FixtureParsesToTheHandWrittenExpectation(string name, string encoding, string lineEnding, bool finalNewline)
    {
        var fixture = RealFormatFixtures.Get(name);
        var bytes = Encode(fixture, encoding, lineEnding == "crlf" ? "\r\n" : "\n", finalNewline);

        var result = Parser.Parse(bytes, Context);

        Assert.Equal(ImportStatus.Success, result.Status);
        var chat = result.Chat!;
        Assert.Equal(fixture.Format, chat.FormatId);
        Assert.Equal(
            encoding switch { "utf8bom" => SourceEncoding.Utf8WithBom, "cp949" => SourceEncoding.Cp949, _ => SourceEncoding.Utf8 },
            chat.Encoding);
        AssertMatches(fixture, chat);
    }

    /// <summary>Running the adapter directly gives the per-line outcomes; the driver-owned line number is never changed.</summary>
    [Theory]
    [MemberData(nameof(FixtureNames))]
    public void EveryLineHasTheExpectedOutcomeAndLineNumber(string name)
    {
        var fixture = RealFormatFixtures.Get(name);
        var adapter = RealFormatAdapters.All.Single(a => a.FormatId == fixture.Format);

        var state = adapter.CreateInitialState(Context);
        Assert.Equal(0, state.LineNumber);
        for (var i = 0; i < fixture.Lines.Count; i++)
        {
            state = state.AdvanceLine();
            var step = adapter.ParseLine(state, fixture.Lines[i].Text);

            Assert.Equal(i + 1, step.State.LineNumber);
            Assert.Equal(i + 1, step.Result.LineNumber);
            Assert.True(
                (fixture.Lines[i].Outcome, fixture.Lines[i].Issue) == (step.Result.Outcome, step.Result.Issue),
                $"{fixture.Name} line {i + 1}: expected {fixture.Lines[i].Outcome}/{fixture.Lines[i].Issue}, got {step.Result.Outcome}/{step.Result.Issue}");
            state = step.State;
        }
    }

    [Theory]
    [MemberData(nameof(FixtureNames))]
    public void ExplicitAdapterGivesTheSameResult(string name)
    {
        var fixture = RealFormatFixtures.Get(name);
        var adapter = RealFormatAdapters.All.Single(a => a.FormatId == fixture.Format);

        var result = ChatExportParser.Parse(Encode(fixture, "utf8", "\r\n", finalNewline: true), Context, adapter);

        AssertMatches(fixture, result.Chat!);
    }

    [Theory]
    [MemberData(nameof(FixtureNames))]
    public void NonCp949FixturesCannotBeRepresentedInCp949(string name)
    {
        var fixture = RealFormatFixtures.Get(name);
        var text = string.Join("\r\n", fixture.Lines.Select(l => l.Text));

        if (fixture.Cp949Compatible)
        {
            Assert.Equal(text, StrictCp949.GetString(StrictCp949.GetBytes(text)));
        }
        else
        {
            Assert.Throws<EncoderFallbackException>(() => StrictCp949.GetBytes(text));
        }
    }

    [Fact]
    public void FixturesCoverEveryPlatformAndEveryOutcomeTheyClaim()
    {
        Assert.Equal(
            [AndroidChatFormatAdapter.Id, IosChatFormatAdapter.Id, WindowsChatFormatAdapter.Id],
            RealFormatFixtures.All.Select(f => f.Format).Distinct().OrderBy(f => f.Value, StringComparer.Ordinal));

        var outcomes = RealFormatFixtures.All.SelectMany(f => f.Lines).Select(l => l.Outcome).ToHashSet();
        Assert.Superset(new HashSet<LineOutcome> { LineOutcome.MessageStarted, LineOutcome.Continuation, LineOutcome.SystemMessage, LineOutcome.Metadata, LineOutcome.Ignored, LineOutcome.Malformed }, outcomes);

        var events = RealFormatFixtures.All.SelectMany(f => f.Messages).Select(m => m.Event).ToHashSet();
        Assert.Superset(new HashSet<SystemEventType> { SystemEventType.Leave, SystemEventType.Invite, SystemEventType.Other }, events);
    }

    [Fact]
    public void SenderLabelsArePreservedAndMappedByCurrentRules()
    {
        var chat = Parser.Parse(Encode(RealFormatFixtures.Get("windows-12h"), "utf8", "\n", true), Context).Chat!;

        // Labels are kept verbatim; identical labels share one participant; no identity heuristics (Phase 1.6).
        Assert.Equal(["UserA", "User B", "Synth#Person (테스트)", "합성사용자 C"], chat.Participants.Select(p => p.DisplayName));
        Assert.All(chat.Participants, p => Assert.Equal(ParticipantConsent.ContextOnly, p.Consent));
        Assert.Equal(3, chat.Messages.Count(m => m.SpeakerId == chat.Participants[0].Id));
    }

    [Fact]
    public void MediaAndDeletedPlaceholdersRemainUserText()
    {
        // Placeholder words are indistinguishable from typed text in these layouts, so they are not reclassified.
        string[] lines = ["UserA 님과 카카오톡 대화", "저장한 날짜 : 2026-10-02 14:23:11", "", "--------------- 2026년 10월 1일 목요일 ---------------", "[UserA] [오후 3:15] 사진", "[UserA] [오후 3:16] 삭제된 메시지입니다."];

        var chat = ChatExportParser.ParseLines(lines, new WindowsChatFormatAdapter(), Context, SourceEncoding.Utf8);

        Assert.All(chat.Messages, m => Assert.Equal(MessageKind.Text, m.Kind));
    }

    [Fact]
    public void LongSenderLabelsAreRejected()
    {
        string[] lines = ["2026년 10월 1일 오후 3:15, " + new string('S', 129) + " : 합성", "2026년 10월 1일 오후 3:15,     : 합성"];

        var chat = ChatExportParser.ParseLines(lines, new AndroidChatFormatAdapter(), Context, SourceEncoding.Utf8);

        Assert.Equal([ParseIssue.SpeakerNameTooLong, ParseIssue.InvalidSpeakerName], chat.Issues.Select(i => i.Issue));
    }

    internal static byte[] Encode(RealFormatFixture fixture, string encoding, string lineEnding, bool finalNewline)
    {
        var text = string.Join(lineEnding, fixture.Lines.Select(l => l.Text)) + (finalNewline ? lineEnding : string.Empty);
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        return encoding switch
        {
            "utf8" => utf8.GetBytes(text),
            "utf8bom" => [0xEF, 0xBB, 0xBF, .. utf8.GetBytes(text)],
            "cp949" => StrictCp949.GetBytes(text),
            _ => throw new ArgumentOutOfRangeException(nameof(encoding)),
        };
    }

    private static void AssertMatches(RealFormatFixture fixture, ParsedChat chat)
    {
        var lines = fixture.Lines;
        int Count(LineOutcome outcome) => lines.Count(l => l.Outcome == outcome);
        Assert.Equal(
            new ParseStatistics(
                lines.Count,
                Count(LineOutcome.MessageStarted),
                Count(LineOutcome.SystemMessage),
                Count(LineOutcome.Continuation),
                Count(LineOutcome.Metadata),
                Count(LineOutcome.Ignored),
                Count(LineOutcome.Malformed),
                Count(LineOutcome.Unsupported)),
            chat.Statistics);

        Assert.Equal(
            lines.Select((l, i) => (Line: i + 1, l.Outcome, l.Issue)).Where(l => l.Issue != ParseIssue.None),
            chat.Issues.Select(i => (i.LineNumber, i.Outcome, i.Issue)));

        var names = chat.Participants.ToDictionary(p => p.Id, p => p.DisplayName);
        Assert.Equal(fixture.Messages.Count, chat.Messages.Count);
        for (var i = 0; i < fixture.Messages.Count; i++)
        {
            var expected = fixture.Messages[i];
            var actual = chat.Messages[i];
            var time = DateTime.ParseExact(expected.Time, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

            Assert.Equal(expected.Line, actual.SourceLine);
            Assert.Equal(new DateTimeOffset(time, TimeSpan.FromHours(9)), actual.Timestamp);
            Assert.Equal(expected.Speaker, actual.SpeakerId is { } id ? names[id] : null);
            Assert.Equal(expected.Speaker is null ? MessageKind.System : MessageKind.Text, actual.Kind);
            Assert.Equal(expected.Event, actual.SystemEvent);
            Assert.Equal(expected.Text, actual.Text);
        }

        Assert.Equal(
            fixture.Messages.Select(m => m.Speaker).OfType<string>().Distinct(StringComparer.Ordinal),
            chat.Participants.Select(p => p.DisplayName));
    }
}
