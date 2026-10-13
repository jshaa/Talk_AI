using System.Text;
using System.Text.RegularExpressions;
using TalkPro.Ingestion.Parsing;
using TalkPro.Ingestion.Tests.GoldenTests;
using TalkPro.Tools.FormatProbe;

namespace TalkPro.Ingestion.Tests;

/// <summary>
/// DD-021: the probe report must be content-free, because the user shares it to build real-format
/// adapters. Verified on every line of every synthetic golden file.
/// </summary>
public sealed partial class FormatProbeTests
{
    [GeneratedRegex(@"⟦[DHAX]\d+⟧|⟦[ECD]⟧|⇥|…", RegexOptions.None, 1000)]
    private static partial Regex Tokens();

    [Theory]
    [InlineData("[UserA] [15:15] 합성 테스트 메시지 001", "[⟦A5⟧] [⟦D2⟧:⟦D2⟧] ⟦H2⟧ ⟦H3⟧…")]
    [InlineData("2026년 10월 1일 오후 3:15, 김민수 : 안녕", "⟦D4⟧년 ⟦D2⟧월 ⟦D1⟧일 오후 ⟦D1⟧:⟦D2⟧, ⟦H3⟧ : ⟦H2⟧")]
    [InlineData("--------------- 2026년 10월 1일 수요일 ---------------", "--------------- ⟦D4⟧년 ⟦D2⟧월 ⟦D1⟧일 수요일 ---------------")]
    [InlineData("합성사용자 C 님이 들어왔습니다.", "⟦H5⟧ ⟦A1⟧ 님이 들어왔습니다.")]
    [InlineData("2026.10.01 15:15:07\tUserA\t내용", "⟦D4⟧.⟦D2⟧.⟦D2⟧ ⟦D2⟧:⟦D2⟧:⟦D2⟧⇥⟦A5⟧⇥⟦H2⟧")]
    [InlineData("October 1, 2026 at 3:15 PM, Name : text", "October ⟦D1⟧, ⟦D4⟧ ⟦A2⟧ ⟦D1⟧:⟦D2⟧ PM, ⟦A4⟧ : ⟦A4⟧…")]
    [InlineData("😀 이모지 메시지", "⟦E⟧ ⟦H3⟧ ⟦H3⟧…")]
    public void SkeletonKeepsStructureAndHidesContent(string line, string expected)
    {
        Assert.Equal(expected, LineSkeleton.Of(line));
    }

    [Fact]
    public void LongContentIsCut()
    {
        var skeleton = LineSkeleton.Of("[UserA] [15:15] " + new string('가', 500));

        Assert.EndsWith("…", skeleton, StringComparison.Ordinal);
        Assert.True(skeleton.Length < 80);
    }

    public static TheoryData<string> GoldenFiles => [.. GoldenData.Entries.Select(e => e.Path)];

    [Theory]
    [MemberData(nameof(GoldenFiles))]
    public void EverySkeletonOfEveryGoldenLineIsContentFree(string path)
    {
        if (!TextDecoding.TryDecode(GoldenData.Bytes(path), out var decoded))
        {
            return; // invalid-encoding sample: nothing to skeletonise
        }

        foreach (var line in LineSplitter.Split(decoded.Text))
        {
            var residue = Residue(LineSkeleton.Of(line));
            Assert.True(residue.Length == 0, $"content leaked into a skeleton: {residue.Length} letter/digit chars");
        }
    }

    [Fact]
    public void ProfileReportContainsNoNamesTextOrPaths()
    {
        var files = GoldenData.Entries.Where(e => e.Format is not null).Select(e => (ReadOnlyMemory<byte>)GoldenData.Bytes(e.Path)).ToList();

        var report = ProbeReport.Profile(files);

        foreach (var secret in new[] { "UserA", "User B", "합성", "SyntheticPerson01", "테스트", "제주도", "conversation", "Golden", ".txt", AppContext.BaseDirectory })
        {
            Assert.DoesNotContain(secret, report, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("file #1", report, StringComparison.Ordinal);
        Assert.Contains("encoding: Utf8", report, StringComparison.Ordinal);
        Assert.Contains("header skeletons:", report, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateReportShowsParseRateAndIssueSkeletonsOnly()
    {
        var report = ProbeReport.Validate([GoldenData.Bytes("a1/robustness.utf8.txt")], Adapters.All, GoldenData.Context);

        Assert.Contains("synthetic.android.v1", report, StringComparison.Ordinal);
        Assert.Contains("issue InvalidTimestamp: 3", report, StringComparison.Ordinal);
        Assert.Contains("issue line skeletons:", report, StringComparison.Ordinal);
        Assert.DoesNotContain("합성", report, StringComparison.Ordinal);
        Assert.DoesNotContain("UserA", report, StringComparison.Ordinal);
    }

    [Fact]
    public void UnsupportedFilesAreReportedWithoutContent()
    {
        var report = ProbeReport.Profile([new byte[] { 0x81, 0x20, 0x41 }]);

        Assert.Contains("encoding: unsupported", report, StringComparison.Ordinal);
    }

    /// <summary>What remains after removing tokens, allow-listed format words, punctuation and whitespace.</summary>
    private static string Residue(string skeleton)
    {
        var withoutTokens = Tokens().Replace(skeleton, " ");
        var builder = new StringBuilder();
        foreach (var word in Regex.Split(withoutTokens, @"[^\p{L}\p{Nd}]+", RegexOptions.None, TimeSpan.FromSeconds(1)))
        {
            if (word.Length > 0 && !LineSkeleton.StructuralHangul.Contains(word) && !LineSkeleton.StructuralLatin.Contains(word))
            {
                builder.Append(word);
            }
        }

        return builder.ToString();
    }
}
