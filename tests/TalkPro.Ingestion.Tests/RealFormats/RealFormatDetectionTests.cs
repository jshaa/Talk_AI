using System.Text;
using TalkPro.Core.Parsing;
using TalkPro.Ingestion.Import;
using TalkPro.Ingestion.RealFormats;
using TalkPro.Ingestion.Synthetic;
using TalkPro.Ingestion.Tests.GoldenCorpus;
using TalkPro.Tests.Shared;

namespace TalkPro.Ingestion.Tests.RealFormats;

/// <summary>Detection is content-based and the six layouts (3 real, 3 synthetic) never collide.</summary>
public sealed class RealFormatDetectionTests
{
    private static readonly ParseContext Context = TestTimeZone.Context();

    private static readonly IReadOnlyList<IChatFormatAdapter> AllAdapters = [.. RealFormatAdapters.All, .. SyntheticAdapters.All];

    public static TheoryData<string> FixtureNames => [.. RealFormatFixtures.All.Select(f => f.Name)];

    public static TheoryData<string> GoldenFiles => [.. GoldenManifest.Instance.Samples.SelectMany(s => s.Files).Select(f => f.Name)];

    [Theory]
    [MemberData(nameof(FixtureNames))]
    public void OnlyTheMatchingAdapterReachesTheThreshold(string name)
    {
        var fixture = RealFormatFixtures.Get(name);
        var lines = fixture.Lines.Select(l => l.Text).ToList();

        foreach (var adapter in AllAdapters)
        {
            var score = adapter.Detect(lines);
            if (adapter.FormatId == fixture.Format)
            {
                Assert.True(score >= 0.6, $"{adapter.FormatId}: {score}");
            }
            else if (adapter is RealExportAdapterBase)
            {
                // Same header, no record of this layout.
                Assert.Equal(RealExportAdapterBase.HeaderOnlyConfidence, score);
            }
            else
            {
                Assert.True(score == 0, $"synthetic {adapter.FormatId} scored {score} on {fixture.Name}");
            }
        }

        Assert.Same(
            AllAdapters.Single(a => a.FormatId == fixture.Format),
            FormatDetector.Select(AllAdapters, lines, Context));
    }

    /// <summary>No real adapter claims any synthetic golden file; synthetic detection is unchanged.</summary>
    [Theory]
    [MemberData(nameof(GoldenFiles))]
    public void RealAdaptersNeverClaimSyntheticGoldenFiles(string fileName)
    {
        var bytes = File.ReadAllBytes(Path.Combine(RepositoryPaths.GoldenDirectory, fileName));
        Assert.True(EncodingDetector.TryDecode(bytes, out _, out var text));
        var lines = LineSplitter.Split(text);

        Assert.All(RealFormatAdapters.All, adapter => Assert.Equal(0, adapter.Detect(lines)));
    }

    [Theory]
    [InlineData("합성 일반 텍스트 문서입니다.\n둘째 줄\n셋째 줄")]
    [InlineData("Date,User,Message\n2026-10-01 15:15:00,\"UserA\",\"합성\"")]
    [InlineData("UserA chat\nDate Saved : Oct 2, 2026 at 14:23\n\nThursday, October 1, 2026\nOct 1, 2026 at 15:15, UserA : synthetic")]
    [InlineData("[UserA] [오후 3:15] 헤더 없는 단일 줄")]
    [InlineData("2026년 10월 1일 오후 3:15, UserA : 헤더 없는 단일 줄")]
    [InlineData("오후 3:15, UserA : 헤더 없는 단일 줄")]
    [InlineData("합성 테스트방 카카오톡 대화")]
    [InlineData("합성 테스트방 카카오톡 대화\n저장한 날짜 : 2026-10-02 14:23:11")]
    [InlineData("합성 테스트방 카카오톡 대화\n저장한 날짜 : 2026-10-02 14:23:11\n\n본문처럼 보이지만 레코드가 없는 줄")]
    [InlineData("저장한 날짜 : 2026-10-02 14:23:11\n합성 테스트방 카카오톡 대화\n[UserA] [오후 3:15] 순서가 바뀐 헤더")]
    public void ArbitraryTruncatedOrUnsupportedInputIsNotClaimed(string text)
    {
        var parser = new ChatExportParser(AllAdapters);

        var result = parser.Parse(Encoding.UTF8.GetBytes(text), Context);

        Assert.Equal(ImportStatus.UnknownFormat, result.Status);
        Assert.All(RealFormatAdapters.All, adapter => Assert.True(adapter.Detect(LineSplitter.Split(text)) < FormatDetector.MinimumConfidence));
    }

    [Fact]
    public void ContinuationTextLookingLikeAnotherLayoutDoesNotFlipDetection()
    {
        string[] lines =
        [
            "UserA 님과 카카오톡 대화",
            "저장한 날짜 : 2026-10-02 14:23:11",
            "",
            "--------------- 2026년 10월 1일 목요일 ---------------",
            "[UserA] [오후 3:15] 다른 형식처럼 보이는 본문",
            "2026년 10월 1일 목요일",
            "2026년 10월 1일 오후 3:15, UserB : 인용",
            "[UserB] [오후 3:16] 답장",
        ];

        Assert.Same(RealFormatAdapters.All[0], FormatDetector.Select(AllAdapters, lines, Context));
    }

    [Fact]
    public void ProductionDetectionDoesNotDependOnSyntheticMarkers()
    {
        var lines = RealFormatFixtures.Get("android").Lines.Select(l => l.Text).ToList();

        Assert.DoesNotContain(lines, l => l.Contains(SyntheticAdapterBase.SignaturePrefix, StringComparison.Ordinal));
        Assert.Same(RealFormatAdapters.All[1], FormatDetector.Select(RealFormatAdapters.All, lines, Context));
    }
}
