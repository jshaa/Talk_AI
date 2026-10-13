using System.Globalization;
using TalkPro.Core.Model;
using TalkPro.Privacy;
using TalkPro.Privacy.Detection;

namespace TalkPro.Privacy.Tests;

/// <summary>Phase 3.1 detectors. All inputs are synthetic (test card 4111…, generated resident numbers).</summary>
public sealed class PrivacyDetectionTests
{
    private static readonly DetectionContext Names = new(["김민수", "User B", "SyntheticPerson01"]);

    private static readonly (string Text, PiiType Type, string Expected)[] PositiveCases =
    [
        ("연락은 010-1234-5678 로 주세요", PiiType.Phone, "010-1234-5678"),
        ("번호 01012345678 남겨둘게", PiiType.Phone, "01012345678"),
        ("+82 10 1234 5678 국제번호", PiiType.Phone, "+82 10 1234 5678"),
        ("사무실 02-123-4567", PiiType.Phone, "02-123-4567"),
        ("메일은 synthetic.user@example.com 이야", PiiType.Email, "synthetic.user@example.com"),
        ($"주민번호 {ValidResidentId()} 맞아?", PiiType.ResidentId, ValidResidentId()),
        ("카드 4111 1111 1111 1111 결제", PiiType.Card, "4111 1111 1111 1111"),
        ("카드번호 4111111111111111", PiiType.Card, "4111111111111111"),
        ("국민은행 계좌 123456-12-123456 로 입금해줘", PiiType.Account, "123456-12-123456"),
        ("서버 192.168.10.25 로 접속", PiiType.IpAddress, "192.168.10.25"),
        ("링크 https://example.com/synthetic?x=1 확인", PiiType.Url, "https://example.com/synthetic?x=1"),
        ("서울시 합성구 테스트로 123 으로 와", PiiType.Address, "서울시 합성구 테스트로 123"),
        ("우리 집 101동 1203호야", PiiType.Address, "101동 1203호"),
        ("김민수 오늘 왔어?", PiiType.Person, "김민수"),
        ("민수야 내 번호 알지", PiiType.Person, "민수"),
        ("User B 가 그랬어", PiiType.Person, "User B"),
        ("김대리님 회의 몇 시예요", PiiType.Title, "김대리님"),
        ("서울대병원에서 만나", PiiType.Organization, "서울대병원"),
        ("합성고등학교 동창회", PiiType.Organization, "합성고등학교"),
    ];

    public static TheoryData<string, PiiType, string> Positives
    {
        get
        {
            var data = new TheoryData<string, PiiType, string>();
            foreach (var (text, type, expected) in PositiveCases)
            {
                data.Add(text, type, expected);
            }

            return data;
        }
    }

    private static readonly string[] NegativeCases =
    [
        "약속은 2026-01-05 오후 3:00 이야",
        "2026.01.05 15:00:00 에 출발",
        "가격은 12,000원이고 3개 샀어",
        "버전 1.2.3 업데이트했어",
        "합성 테스트 메시지 123456 번째",
        "카드 4111 1111 1111 1112 는 잘못된 번호",
        "123456-12-123456 은 그냥 코드야",
        "학교로 5분 걸려",
        "민수정 씨는 다른 사람",
    ];

    public static TheoryData<string> Negatives => [.. NegativeCases];

    [Theory]
    [MemberData(nameof(Positives))]
    public void DetectsSyntheticIdentifiers(string text, PiiType type, string expected)
    {
        var findings = Scan(text);

        Assert.Contains(findings, f => f.Type == type && text.Substring(f.Start, f.Length) == expected);
    }

    [Theory]
    [MemberData(nameof(Negatives))]
    public void IgnoresLookAlikes(string text)
    {
        var spans = Scan(text).Where(f => f.Category != PiiCategory.Sensitive && f.Type != PiiType.Organization).ToList();

        Assert.Empty(spans);
    }

    [Fact]
    public void ReportExampleIsFlaggedAsHealthAndInstitution()
    {
        // Report 4 / 8.2: individually harmless words that together reveal health information of a third party.
        var findings = Scan("우리 엄마 서울대병원에서 암 치료 중");

        Assert.Contains(findings, f => f.Type == PiiType.Health && f.Layer == DetectionLayer.L3Context);
        Assert.Contains(findings, f => f.Type == PiiType.Organization);
    }

    [Theory]
    [InlineData("요즘 우울증 때문에 정신과 다녀", PiiType.MentalHealth)]
    [InlineData("일요일에 예배 드리고 갈게", PiiType.Religion)]
    [InlineData("이번 총선 투표했어?", PiiType.Political)]
    [InlineData("대출 연체돼서 큰일이야", PiiType.Finance)]
    [InlineData("고소 당해서 경찰서 다녀왔어", PiiType.Criminal)]
    public void SensitiveTopicsGiveOneMessageLevelHint(string text, PiiType type)
    {
        var hints = Scan(text).Where(f => f.Type == type).ToList();

        Assert.Single(hints);
    }

    [Fact]
    public void ResidentIdChecksumOnlyAffectsConfidence()
    {
        var valid = ValidResidentId();
        var invalid = valid[..^1] + ((valid[^1] - '0' + 1) % 10).ToString(CultureInfo.InvariantCulture);

        Assert.True(Checksums.IsValidResidentIdChecksum(valid.Replace("-", "", StringComparison.Ordinal)));
        Assert.True(Scan(valid).Single().Confidence > Scan(invalid).Single().Confidence);
    }

    [Fact]
    public void AccountNeedsBankContextAndNeverSwallowsAPhone()
    {
        Assert.DoesNotContain(Scan("123456-12-123456"), f => f.Type == PiiType.Account);
        var phoneNearBank = Scan("입금 확인 전화 010-1234-5678");
        Assert.Contains(phoneNearBank, f => f.Type == PiiType.Phone);
        Assert.DoesNotContain(phoneNearBank, f => f.Type == PiiType.Account);
    }

    [Fact]
    public void ScannerAssignsSequentialIdsAndNonOverlappingSpans()
    {
        var messages = new[]
        {
            Message(1, "김민수 010-1234-5678 synthetic.user@example.com"),
            Message(2, "카드 4111 1111 1111 1111 그리고 대출"),
        };

        var findings = PrivacyScanner.CreateDefault().Scan(messages, [new Participant(new ParticipantId("p_01"), "김민수", ParticipantConsent.Owner)]);

        Assert.Equal(Enumerable.Range(1, findings.Count), findings.Select(f => f.Id.Value));
        foreach (var group in findings.Where(f => f.Category != PiiCategory.Sensitive).GroupBy(f => f.MessageId))
        {
            var spans = group.OrderBy(f => f.Start).ToList();
            Assert.All(spans.Zip(spans.Skip(1)), pair => Assert.True(pair.First.End <= pair.Second.Start));
        }

        Assert.Contains(findings, f => f.Type == PiiType.Finance);
    }

    [Fact]
    public void SyntheticEvaluationSetMeetsRecallAndPrecisionTargets()
    {
        // Span types only (L1 + L2). Targets from BACKLOG Phase 3: L1 recall ≥ 0.99.
        var truePositives = 0;
        var expected = 0;
        foreach (var (text, type, value) in PositiveCases)
        {
            expected++;
            if (Scan(text).Any(f => f.Type == type && text.Substring(f.Start, f.Length) == value))
            {
                truePositives++;
            }
        }

        var falsePositives = NegativeCases.Sum(text => Scan(text).Count(f => f.Category != PiiCategory.Sensitive && f.Type != PiiType.Organization));
        var recall = (double)truePositives / expected;
        var precision = (double)truePositives / (truePositives + falsePositives);

        Assert.True(recall >= 0.99, $"recall {recall:P1}");
        Assert.True(precision >= 0.95, $"precision {precision:P1}");
    }

    [Fact]
    public void FindingsAndContextNeverPrintText()
    {
        var finding = Scan("김민수 010-1234-5678").First();

        Assert.DoesNotContain("민수", finding.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("010", finding.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("민수", Names.ToString(), StringComparison.Ordinal);
    }

    internal static string ValidResidentId()
    {
        // Synthetic date 900101, gender digit 1, serial 23456, computed legacy checksum.
        const string first12 = "900101123456";
        int[] weights = [2, 3, 4, 5, 6, 7, 8, 9, 2, 3, 4, 5];
        var sum = first12.Select((c, i) => (c - '0') * weights[i]).Sum();
        var check = (11 - (sum % 11)) % 10;
        return "900101-123456" + check.ToString(CultureInfo.InvariantCulture);
    }

    private static List<PiiFinding> Scan(string text) =>
        [.. PrivacyScanner.CreateDefault().Scan([Message(1, text)], Names.KnownNames.Select((n, i) => new Participant(new ParticipantId($"p_{i + 1:D2}"), n, ParticipantConsent.ContextOnly)).ToList())];

    private static ChatMessage Message(int n, string text) =>
        new(new MessageId(string.Create(CultureInfo.InvariantCulture, $"m{n:D7}")), new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.FromHours(9)), new ParticipantId("p_01"), MessageKind.Text, text, n);
}
