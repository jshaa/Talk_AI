using System.Text.RegularExpressions;
using TalkPro.Core.Model;

namespace TalkPro.Privacy.Detection;

/// <summary>A span found by a detector, before overlap resolution and id assignment.</summary>
public readonly record struct RawFinding(int Start, int Length, PiiType Type, double Confidence)
{
    public override string ToString() => $"RawFinding {{ Type = {Type}, Start = {Start}, Length = {Length} }}";
}

/// <summary>Names known for the dataset (participants and their aliases). Personal data.</summary>
public sealed record DetectionContext(IReadOnlyList<string> KnownNames)
{
    public static DetectionContext From(IEnumerable<Participant> participants) =>
        new([.. participants.SelectMany(p => p.Aliases.Prepend(p.DisplayName)).Where(n => n.Trim().Length >= 2).Distinct(StringComparer.Ordinal)]);

    public override string ToString() => $"DetectionContext {{ Names = {KnownNames.Count} }}";
}

public interface IPiiDetector
{
    DetectionLayer Layer { get; }

    IEnumerable<RawFinding> Detect(string text, DetectionContext context);
}

/// <summary>L1: regular-expression identifiers with checksum validation where one exists.</summary>
public sealed partial class PatternDetector : IPiiDetector
{
    private const int Timeout = 250;

    public DetectionLayer Layer => DetectionLayer.L1Pattern;

    [GeneratedRegex(@"(?<![\d-])(?:\+82[- ]?1[016789]|01[016789])[- ]?\d{3,4}[- ]?\d{4}(?![\d-])", RegexOptions.CultureInvariant, Timeout)]
    private static partial Regex Mobile();

    [GeneratedRegex(@"(?<![\d-])0(?:2|[3-6][1-5])[- ]?\d{3,4}[- ]?\d{4}(?![\d-])", RegexOptions.CultureInvariant, Timeout)]
    private static partial Regex Landline();

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", RegexOptions.CultureInvariant, Timeout)]
    private static partial Regex Email();

    [GeneratedRegex(@"(?<!\d)\d{2}(?:0[1-9]|1[0-2])(?:0[1-9]|[12]\d|3[01])[- ]?[1-8]\d{6}(?!\d)", RegexOptions.CultureInvariant, Timeout)]
    private static partial Regex ResidentId();

    [GeneratedRegex(@"(?<![\d-])(?:\d{4}[- ]){3}\d{1,7}(?![\d-])|(?<!\d)\d{13,19}(?!\d)", RegexOptions.CultureInvariant, Timeout)]
    private static partial Regex Card();

    [GeneratedRegex(@"(?<![\d-])\d{2,6}-\d{2,6}-\d{2,8}(?:-\d{1,3})?(?![\d-])", RegexOptions.CultureInvariant, Timeout)]
    private static partial Regex AccountShape();

    [GeneratedRegex(@"계좌|입금|송금|이체|은행|농협|국민|신한|우리|하나|기업|카카오뱅크|카뱅|토스|새마을|우체국|수협", RegexOptions.CultureInvariant, Timeout)]
    private static partial Regex AccountContext();

    [GeneratedRegex(@"(?<![\d.])(?:(?:25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)\.){3}(?:25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)(?![\d.])", RegexOptions.CultureInvariant, Timeout)]
    private static partial Regex IpV4();

    [GeneratedRegex(@"(?:https?://|www\.)[^\s<>""']+", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, Timeout)]
    private static partial Regex Url();

    [GeneratedRegex(@"(?:[가-힣]{1,10}(?:특별시|광역시|특별자치시|특별자치도|시|도)\s)?[가-힣]{1,10}(?:시|군|구)\s[가-힣0-9]{1,15}(?:로|길)\s?\d{1,4}(?:-\d{1,4})?|(?:[가-힣]{1,10}(?:특별시|광역시|시|도)\s)?[가-힣]{1,10}(?:시|군|구)\s[가-힣]{1,10}(?:동|읍|면|리)\s\d{1,5}(?:-\d{1,5})?|(?<!\d)\d{1,4}동\s?\d{1,4}호", RegexOptions.CultureInvariant, Timeout)]
    private static partial Regex Address();

    public IEnumerable<RawFinding> Detect(string text, DetectionContext context)
    {
        ArgumentNullException.ThrowIfNull(text);
        var found = new List<RawFinding>();
        var phones = new List<(int Start, int End)>();

        foreach (var regex in new[] { Mobile(), Landline() })
        {
            foreach (Match m in regex.Matches(text))
            {
                found.Add(new RawFinding(m.Index, m.Length, PiiType.Phone, 0.95));
                phones.Add((m.Index, m.Index + m.Length));
            }
        }

        foreach (Match m in Email().Matches(text))
        {
            found.Add(new RawFinding(m.Index, m.Length, PiiType.Email, 0.98));
        }

        foreach (Match m in ResidentId().Matches(text))
        {
            // Numbers issued since 2020-10 have no checksum, so an invalid checksum lowers confidence only.
            found.Add(new RawFinding(m.Index, m.Length, PiiType.ResidentId, Checksums.IsValidResidentIdChecksum(Digits(m.Value)) ? 0.97 : 0.75));
        }

        foreach (Match m in Card().Matches(text))
        {
            var digits = Digits(m.Value);
            if (digits.Length is >= 13 and <= 19 && Checksums.IsValidLuhn(digits))
            {
                found.Add(new RawFinding(m.Index, m.Length, PiiType.Card, 0.9));
            }
        }

        foreach (Match m in AccountShape().Matches(text))
        {
            var digits = Digits(m.Value).Length;
            var overlapsPhone = phones.Any(p => m.Index < p.End && p.Start < m.Index + m.Length);
            if (digits is >= 10 and <= 16 && !overlapsPhone && AccountContext().IsMatch(Window(text, m.Index, m.Length, 20)))
            {
                found.Add(new RawFinding(m.Index, m.Length, PiiType.Account, 0.7));
            }
        }

        foreach (Match m in IpV4().Matches(text))
        {
            found.Add(new RawFinding(m.Index, m.Length, PiiType.IpAddress, 0.6));
        }

        foreach (Match m in Url().Matches(text))
        {
            found.Add(new RawFinding(m.Index, m.Length, PiiType.Url, 0.9));
        }

        foreach (Match m in Address().Matches(text))
        {
            found.Add(new RawFinding(m.Index, m.Length, PiiType.Address, 0.6));
        }

        return found;
    }

    private static string Digits(string value) => string.Concat(value.Where(char.IsAsciiDigit));

    private static string Window(string text, int start, int length, int margin)
    {
        var from = Math.Max(0, start - margin);
        var to = Math.Min(text.Length, start + length + margin);
        return text[from..to];
    }
}

/// <summary>
/// L2: dictionary-based names. Participant names and aliases, Korean given-name forms of 3-syllable
/// names followed by a vocative or particle ("민수야"), titles ("김대리"), and organisation suffixes
/// ("서울대병원"). Third-party names that are not participants are only caught via titles; a real
/// NER model is deferred until its license is reviewed.
/// </summary>
public sealed partial class DictionaryDetector : IPiiDetector
{
    private const int Timeout = 250;
    private const string Particles = "야아이씨님가는은랑을를의도와과한에";

    public DetectionLayer Layer => DetectionLayer.L2Dictionary;

    [GeneratedRegex(@"(?<![가-힣])[가-힣]{1,3}(?:본부장|대리|과장|차장|부장|팀장|실장|이사|상무|전무|사장|대표|선생|교수|원장|주임|사원)(?:님)?", RegexOptions.CultureInvariant, Timeout)]
    private static partial Regex Title();

    [GeneratedRegex(@"(?<![가-힣A-Za-z0-9])[가-힣A-Za-z0-9]{2,15}?(?:대학교병원|대학병원|병원|한의원|의원|치과|대학교|초등학교|중학교|고등학교|은행|교회|성당|센터)", RegexOptions.CultureInvariant, Timeout)]
    private static partial Regex Organization();

    public IEnumerable<RawFinding> Detect(string text, DetectionContext context)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(context);
        var found = new List<RawFinding>();

        foreach (var name in context.KnownNames)
        {
            AddOccurrences(text, name, 0.9, found, requireBoundary: false);
            if (name.Length == 3 && name.All(IsHangulSyllable))
            {
                AddOccurrences(text, name[1..], 0.7, found, requireBoundary: true);
            }
        }

        foreach (Match m in Title().Matches(text))
        {
            found.Add(new RawFinding(m.Index, m.Length, PiiType.Title, 0.6));
        }

        foreach (Match m in Organization().Matches(text))
        {
            found.Add(new RawFinding(m.Index, m.Length, PiiType.Organization, 0.6));
        }

        return found;
    }

    private static void AddOccurrences(string text, string name, double confidence, List<RawFinding> found, bool requireBoundary)
    {
        for (var index = text.IndexOf(name, StringComparison.Ordinal); index >= 0; index = text.IndexOf(name, index + name.Length, StringComparison.Ordinal))
        {
            if (requireBoundary)
            {
                var before = index == 0 || !IsHangulSyllable(text[index - 1]);
                var afterIndex = index + name.Length;
                var after = afterIndex >= text.Length || !IsHangulSyllable(text[afterIndex]) || Particles.Contains(text[afterIndex], StringComparison.Ordinal);
                if (!before || !after)
                {
                    continue;
                }
            }

            found.Add(new RawFinding(index, name.Length, PiiType.Person, confidence));
        }
    }

    private static bool IsHangulSyllable(char c) => c is >= '가' and <= '힣';
}

/// <summary>
/// L3 (minimal, master prompt §5): keyword candidates for sensitive topics. Message-level hints for the
/// user's review, not a classifier. Keywords are deliberately conservative to limit over-exclusion.
/// </summary>
public sealed class SensitiveKeywordDetector : IPiiDetector
{
    private static readonly (PiiType Type, string[] Keywords)[] Lexicon =
    [
        (PiiType.Health, ["항암", "암 진단", "암 치료", "암 수술", "암이래", "입원", "수술", "진단받", "진단 받", "처방", "당뇨", "고혈압", "투석", "임신", "난임", "유산", "응급실", "확진", "치료 중", "치료중"]),
        (PiiType.MentalHealth, ["우울증", "정신과", "공황", "불안장애", "심리상담", "상담치료", "자해", "자살", "트라우마", "조울", "ADHD", "수면제", "항우울"]),
        (PiiType.Religion, ["예배", "미사에", "미사를", "기도해", "하나님", "부처님", "신앙", "세례", "불교", "기독교", "천주교", "이슬람"]),
        (PiiType.Political, ["투표", "선거", "대선", "총선", "국회의원", "탄핵", "민주당", "국민의힘"]),
        (PiiType.Sexual, ["성관계", "피임", "콘돔", "성병", "동성애", "성적 지향", "커밍아웃"]),
        (PiiType.Criminal, ["전과", "구속", "기소", "재판", "고소", "경찰서", "벌금형", "집행유예", "수사 중"]),
        (PiiType.Finance, ["대출", "연체", "신용불량", "파산", "연봉", "월급", "카드값", "신용점수", "채무", "빚"]),
    ];

    public DetectionLayer Layer => DetectionLayer.L3Context;

    public IEnumerable<RawFinding> Detect(string text, DetectionContext context)
    {
        ArgumentNullException.ThrowIfNull(text);
        foreach (var (type, keywords) in Lexicon)
        {
            foreach (var keyword in keywords)
            {
                var index = text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
                if (index >= 0)
                {
                    yield return new RawFinding(index, keyword.Length, type, 0.5);
                    break; // one hint per topic per message
                }
            }
        }
    }
}

public static class Checksums
{
    private static readonly int[] ResidentWeights = [2, 3, 4, 5, 6, 7, 8, 9, 2, 3, 4, 5];

    /// <summary>Legacy resident registration number checksum (numbers issued before 2020-10).</summary>
    public static bool IsValidResidentIdChecksum(string digits)
    {
        ArgumentNullException.ThrowIfNull(digits);
        if (digits.Length != 13 || !digits.All(char.IsAsciiDigit))
        {
            return false;
        }

        var sum = 0;
        for (var i = 0; i < 12; i++)
        {
            sum += (digits[i] - '0') * ResidentWeights[i];
        }

        return (11 - (sum % 11)) % 10 == digits[12] - '0';
    }

    public static bool IsValidLuhn(string digits)
    {
        ArgumentNullException.ThrowIfNull(digits);
        if (digits.Length < 2 || !digits.All(char.IsAsciiDigit))
        {
            return false;
        }

        var sum = 0;
        var doubleIt = false;
        for (var i = digits.Length - 1; i >= 0; i--)
        {
            var d = digits[i] - '0';
            if (doubleIt)
            {
                d *= 2;
                if (d > 9)
                {
                    d -= 9;
                }
            }

            sum += d;
            doubleIt = !doubleIt;
        }

        return sum % 10 == 0;
    }
}
