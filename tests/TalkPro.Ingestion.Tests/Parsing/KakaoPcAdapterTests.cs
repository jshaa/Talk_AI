using TalkPro.Core.Model;
using TalkPro.Core.Parsing;
using TalkPro.Ingestion.Formats.Kakao;
using TalkPro.Ingestion.Formats.Synthetic;
using TalkPro.Ingestion.Parsing;

namespace TalkPro.Ingestion.Tests.Parsing;

/// <summary>
/// KakaoTalk PC export (DD-027) on hand-written synthetic fixtures of the same shape
/// (RealFormats/Golden/kakao-pc). No real conversation text is used.
/// </summary>
public sealed class KakaoPcAdapterTests
{
    private static readonly ChatExportParser Parser = new([.. KakaoFormats.All, .. SyntheticFormats.All]);
    private static readonly TimeSpan Kst = TimeSpan.FromHours(9);

    [Theory]
    [InlineData("basic.utf8.txt", SourceEncoding.Utf8)]
    [InlineData("basic.utf8bom.txt", SourceEncoding.Utf8WithBom)]
    public void BasicExportIsParsed(string file, SourceEncoding encoding)
    {
        var chat = Parse(file);

        Assert.Equal(KakaoPcAdapter.Id, chat.FormatId);
        Assert.Equal(encoding, chat.Encoding);
        Assert.Equal(["합성사용자A", "합성사용자B"], chat.Participants.Select(p => p.DisplayName));
        Assert.Empty(chat.Issues);
        Assert.Equal(9, chat.Messages.Count);
        Assert.DoesNotContain(chat.Participants, p => p.DisplayName.Contains("가상친구", StringComparison.Ordinal));
    }

    [Fact]
    public void KoreanTwelveHourClockAndDateSeparatorsAreHonoured()
    {
        var times = Parse("basic.utf8.txt").Messages.Select(m => m.Timestamp).ToList();

        Assert.Equal(new DateTimeOffset(2025, 12, 31, 23, 58, 0, Kst), times[0]);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, Kst), times[2]);   // 오전 12:00
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 31, 0, Kst), times[3]);  // 오전 12:31
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 12, 5, 0, Kst), times[4]);  // 오후 12:05
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 13, 46, 0, Kst), times[5]); // 오후 1:46
    }

    [Fact]
    public void LinesWithoutAHeaderContinueTheMessageEvenWhenTheyStartWithABracket()
    {
        var message = Parse("basic.utf8.txt").Messages[1];

        Assert.Equal("합성 문장 둘\n여러 줄 합성 메시지의 둘째 줄\n[링크처럼] 보이는 셋째 줄", message.Text.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void PlaceholdersEmptyTextAndTrailingSpacesAreKept()
    {
        var messages = Parse("basic.utf8.txt").Messages;

        // Media placeholders are kept as text for now (known issue, DD-027).
        Assert.Equal(("이모티콘", MessageKind.Text), (messages[5].Text, messages[5].Kind));
        Assert.Equal("사진", messages[6].Text);
        Assert.EndsWith("줄 ", messages[7].Text, StringComparison.Ordinal);
        Assert.Equal(string.Empty, messages[8].Text);
    }

    [Fact]
    public void MalformedLinesAreReportedNotGuessed()
    {
        var chat = Parse("malformed.utf8.txt");

        Assert.Equal(
            [ParseIssue.MissingDateContext, ParseIssue.InvalidTimestamp, ParseIssue.InvalidTimestamp],
            chat.Issues.Select(i => i.Issue));
        Assert.Contains(chat.Messages, m => m.Text == "정상 합성 메시지");
    }

    [Theory]
    [InlineData("[이름] [오후 1:46] 본문", true)]
    [InlineData("[이름 [괄호]] [오전 12:00] 본문", true)]
    [InlineData("[이름] [오후 1:46]", true)]
    [InlineData("[이름] [13:46] 본문", false)]
    [InlineData("[이름] [PM 1:46] 본문", false)]
    [InlineData("이름 [오후 1:46] 본문", false)]
    public void OnlyFullMessageLinesStartARecord(string line, bool isRecord)
    {
        var adapter = new KakaoPcAdapter();
        var state = adapter.CreateInitialState(new ParseContext { TimeZone = TimeZoneInfo.Utc }).AdvanceLine().EnterBody().WithDate(new DateOnly(2026, 1, 1));
        state = adapter.ParseLine(state, "[이전] [오전 9:00] 열린 메시지").State.AdvanceLine();

        var result = adapter.ParseLine(state, line).Result;

        Assert.Equal(isRecord ? LineOutcome.MessageStarted : LineOutcome.Continuation, result.Outcome);
    }

    [Fact]
    public void DetectionPrefersTheKakaoHeaderAndSyntheticFilesStaySynthetic()
    {
        var adapter = new KakaoPcAdapter();

        Assert.Equal(1.0, adapter.Detect(["가상친구 님과 카카오톡 대화", "저장한 날짜 : 2026-10-13 09:00:00", "", "---"]));
        Assert.Equal(0.0, adapter.Detect(["TalkPro Synthetic Export (W1)", "Room: 합성", "[UserA] [10:00] 합성"]));
        Assert.Equal(SyntheticWindowsAdapter.Id, Parser.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Golden", "w1", "conversation.utf8.txt")), new ParseContext { TimeZone = TimeZoneInfo.Utc }).Chat!.FormatId);
    }

    [Fact]
    public void TheAdapterIsRegisteredInEveryBuild()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();

        IngestionServiceCollectionExtensions.AddTalkProIngestion(services);

        Assert.Contains(services, s => s.ImplementationInstance is KakaoPcAdapter);
    }

    private static ParsedChat Parse(string file)
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "RealFormats", "Golden", "kakao-pc", file));
        var outcome = Parser.Parse(bytes, new ParseContext { TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Seoul") });
        Assert.True(outcome.IsSuccess, outcome.ToString());
        return outcome.Chat!;
    }
}
