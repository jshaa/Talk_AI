using System.Globalization;
using static TalkPro.Tools.SampleGenerator.Corpus.SyntheticVocabulary;

namespace TalkPro.Tools.SampleGenerator.Corpus;

/// <summary>
/// The logical golden corpus. Every name and text is synthetic (see <see cref="SyntheticVocabulary"/>).
/// Random filler comes from <see cref="DeterministicRandom"/>, consumed in a fixed order, so the same
/// seed always produces the same corpus.
/// </summary>
public static class SampleCatalog
{
    public const ulong DefaultSeed = 20261002;

    /// <summary>Length of the rejected line in <c>very-long-line</c> (driver limit is 100,000).</summary>
    public const int TooLongLineLength = 100_001;

    private static readonly GoldenEncoding[] AllEncodings = [GoldenEncoding.Utf8, GoldenEncoding.Utf8Bom, GoldenEncoding.Cp949];

    /// <summary>Content not representable in CP949 (emoji, supplementary characters, other scripts).</summary>
    private static readonly GoldenEncoding[] UnicodeEncodings = [GoldenEncoding.Utf8, GoldenEncoding.Utf8Bom];

    /// <summary>Large or narrowly scoped samples: one encoding keeps the repository small.</summary>
    private static readonly GoldenEncoding[] Utf8Only = [GoldenEncoding.Utf8];

    private static readonly string[] FillerWords =
    [
        "합성", "테스트", "샘플", "데이터", "메시지", "확인", "완료", "예정", "오늘", "내일",
        "synthetic", "sample", "alpha", "beta", "gamma", "ok", "42", "7", "ㅋㅋ", "ㅎㅎ", "!", "?",
    ];

    private const string LongTextAlphabet = "가나다라마바사아자차카타파하합성테스트 abcdefghijklmnopqrstuvwxyz0123456789 ";

    public static IReadOnlyList<SampleCase> Create(ulong seed)
    {
        var random = new DeterministicRandom(seed);
        return
        [
            Empty(),
            HeaderOnly(),
            SingleMessage(),
            SingleParticipant(),
            MultiParticipant(random),
            Multiline(),
            LineEndingLf(),
            NoFinalNewline(),
            TextVariety(random),
            Unicode(),
            Timestamps(),
            SystemMessages(),
            Names(),
            Malformed(),
            ExtraFields(),
            TruncatedLastRecord(),
            BlankLines(),
            NoHeader(),
            UnsupportedVersion(),
            VeryLongLine(),
            W1MissingDateContext(),
        ];
    }

    private static DateTime T(int month, int day, int hour, int minute, int second = 0, int year = 2026) =>
        new(year, month, day, hour, minute, second, DateTimeKind.Unspecified);

    private static MessageItem Msg(DateTime time, string speaker, params string[] lines) => new(time, speaker, lines);

    private static SampleCase Empty() =>
        new("empty", "빈 파일 (0 bytes, BOM 변형은 BOM만)", [], UnicodeEncodings, IncludeHeader: false);

    private static SampleCase HeaderOnly() =>
        new("header-only", "헤더만 있고 메시지가 없는 파일", [], AllEncodings);

    private static SampleCase SingleMessage() =>
        new("single-message", "정상 단일 메시지", [Msg(T(10, 1, 15, 15, 7), UserA, "합성 테스트 메시지 001")], AllEncodings);

    private static SampleCase SingleParticipant() =>
        new(
            "single-participant",
            "참가자 1명, 여러 메시지",
            [
                Msg(T(10, 1, 9, 0, 1), Person01, "합성 테스트 메시지 001"),
                Msg(T(10, 1, 9, 1, 2), Person01, "합성 테스트 메시지 002"),
                Msg(T(10, 1, 9, 2, 3), Person01, "Synthetic test message 003"),
            ],
            AllEncodings);

    /// <summary>Seeded chatter of five speakers that crosses midnight (date change).</summary>
    private static SampleCase MultiParticipant(DeterministicRandom random)
    {
        string[] speakers = [UserA, UserB, UserC, SpecialName, Person01];
        var time = T(10, 1, 23, 30, 0);
        var items = new List<SampleItem>();
        for (var i = 1; i <= 40; i++)
        {
            time = time.AddSeconds(random.Next(240) + 1);
            var speaker = speakers[random.Next(speakers.Length)];
            var lineCount = random.Next(5) == 0 ? 2 + random.Next(2) : 1;
            var lines = new string[lineCount];
            lines[0] = "합성 테스트 메시지 " + i.ToString("D3", CultureInfo.InvariantCulture) + " " + Words(random, 2 + random.Next(6));
            for (var j = 1; j < lineCount; j++)
            {
                lines[j] = Words(random, 1 + random.Next(6));
            }

            items.Add(new MessageItem(time, speaker, lines));
        }

        return new("multi-participant", "여러 참가자, 시드 기반 합성 대화, 자정을 넘는 날짜 변경", items, AllEncodings);
    }

    private static SampleCase Multiline() =>
        new(
            "multiline",
            "여러 줄 메시지, 연속 빈 줄, 헤더/날짜/구분자처럼 보이는 본문 줄 (CRLF)",
            [
                Msg(T(10, 1, 10, 0), UserA, "첫 줄", "둘째 줄", "셋째 줄"),
                Msg(T(10, 1, 10, 1), UserB, "가운데 빈 줄 앞", "", "", "가운데 빈 줄 뒤"),
                Msg(T(10, 1, 10, 2), UserC, "끝에 빈 줄이 있는 메시지", "", "   "),
                Msg(T(10, 1, 10, 3), UserA, "헤더처럼 보이는 줄:", "TalkPro Synthetic Export (W1)", "Room: 합성 테스트방 01", "Saved: 2026-10-02 14:23:11"),
                Msg(T(10, 1, 10, 4), UserB, "날짜처럼 보이는 줄:", "날짜: 2026-10-01 15:15:07", "2026/10/01 (Thu) 합성 일정", "(2026.10.01 15:15:07) 합성 메모", "20261001 오후 3:15"),
                Msg(T(10, 1, 10, 5), UserC, "구분자처럼 보이는 줄:", "| UserA | 합성 가짜 필드", "\tUserA\t합성 가짜 탭 필드", "===", "== 2026-10-01 (Thu) ==", "*", "* 합성 별표 줄", "----------", "]] [[ 괄호 줄"),
                Msg(T(10, 1, 10, 6), UserA, "   앞뒤 공백 유지   ", "\t탭으로 시작하는 줄", "줄 끝 공백   "),
                new RawLineItem(string.Empty, RawLineExpectation.Ignored),
                new SystemItem(T(10, 1, 10, 7), "합성 시스템 알림: 여러 줄 테스트 종료", "Other"),
                new RawLineItem(string.Empty, RawLineExpectation.Ignored),
                Msg(T(10, 1, 10, 8), UserB, "마지막 메시지", "마지막 줄"),
            ],
            AllEncodings);

    private static SampleCase LineEndingLf() =>
        new(
            "line-ending-lf",
            "LF 줄바꿈",
            [
                Msg(T(10, 1, 11, 0), UserA, "LF 줄바꿈 메시지 001"),
                Msg(T(10, 1, 11, 1), UserB, "LF 여러 줄 첫 줄", "LF 여러 줄 둘째 줄"),
                new SystemItem(T(10, 1, 11, 2), "SyntheticPerson01 님이 들어왔습니다.", "Join"),
                Msg(T(10, 1, 11, 3), Person01, "LF 줄바꿈 메시지 003"),
            ],
            AllEncodings,
            LineEnding.Lf);

    private static SampleCase NoFinalNewline() =>
        new(
            "no-final-newline",
            "마지막 줄에 줄바꿈이 없는 파일 (마지막 레코드는 여러 줄)",
            [
                Msg(T(10, 1, 12, 0), UserA, "줄바꿈 없는 파일 메시지 001"),
                Msg(T(10, 1, 12, 1), UserB, "마지막 메시지 첫 줄", "마지막 메시지 끝 줄 (newline 없음)"),
            ],
            AllEncodings,
            FinalNewline: false);

    /// <summary>CP949-representable content: Korean (incl. UHC-only syllables), English, digits, symbols, Hanja, kana, Greek.</summary>
    private static SampleCase TextVariety(DeterministicRandom random) =>
        new(
            "text-variety",
            "한글/영어/숫자/특수문자/혼합 언어, 빈 메시지, 공백 메시지, 많은 공백, 제어문자, 긴 메시지 (CP949 표현 가능)",
            [
                Msg(T(10, 1, 13, 0), UserA, "합성 테스트 한글 메시지입니다"),
                Msg(T(10, 1, 13, 1), UserB, "Synthetic English test message."),
                Msg(T(10, 1, 13, 2), UserC, "0123456789 3.14159 -42 +7 1,234,567 1e10"),
                Msg(T(10, 1, 13, 3), UserA, "특수문자 !@#$%^&*()_+-=[]{};':\",./<>?`~\\|"),
                Msg(T(10, 1, 13, 4), UserB, "혼합 mixed 텍스트 123 漢字 ひらがな カタカナ αβγ"),
                Msg(T(10, 1, 13, 5), UserC, "CP949 확장 한글: 똠방각하 쀍 햏 뷁"),
                Msg(T(10, 1, 13, 6), UserA, string.Empty),
                Msg(T(10, 1, 13, 7), UserB, "   "),
                Msg(T(10, 1, 13, 8), UserC, "합성" + new string(' ', 200) + "끝"),
                Msg(T(10, 1, 13, 9), UserA, "제어문자\u0001\u0007\u001B\u007F 포함, 가운데 CR 앞\r뒤"),
                Msg(T(10, 1, 13, 10), UserB, "긴 메시지: " + random.Text(LongTextAlphabet, 4_000)),
            ],
            AllEncodings);

    /// <summary>Characters CP949 cannot represent; per the CP949 policy this case has no CP949 variant.</summary>
    private static SampleCase Unicode() =>
        new(
            "unicode",
            "emoji, 보충 평면 문자, ZWJ 시퀀스, 국기, 결합 문자, 다국어 (CP949 표현 불가 → UTF-8 계열만)",
            [
                Msg(T(10, 1, 14, 0), EmojiName, "이모지 😀🎉👍🏽"),
                Msg(T(10, 1, 14, 1), UserA, "ZWJ 👨‍👩‍👧‍👦 국기 🇰🇷"),
                Msg(T(10, 1, 14, 2), UserB, "보충 평면 𠀀𠜎𝄞"),
                Msg(T(10, 1, 14, 3), UserC, "한국어 English 日本語 Ελληνικά Русский العربية עברית हिन्दी"),
                Msg(T(10, 1, 14, 4), UserA, "결합 문자 é ä"),
                Msg(T(10, 1, 14, 5), EmojiName, "여러 줄 😀", "둘째 줄 𝄞"),
            ],
            UnicodeEncodings);

    private static SampleCase Timestamps() =>
        new(
            "timestamps",
            "동일 timestamp, 중복처럼 보이는 메시지, 시간 역순, 날짜/월/연도 경계, 정오/자정, 윤일",
            [
                Msg(T(10, 1, 9, 0, 0), UserA, "동일 시각 메시지 1"),
                Msg(T(10, 1, 9, 0, 0), UserB, "동일 시각 메시지 2"),
                Msg(T(10, 1, 9, 5, 0), UserA, "중복처럼 보이는 메시지"),
                Msg(T(10, 1, 9, 5, 0), UserA, "중복처럼 보이는 메시지"),
                Msg(T(10, 1, 12, 0, 0), UserC, "시간 순서 역전 앞"),
                Msg(T(10, 1, 11, 0, 0), UserC, "시간 순서 역전 뒤 (더 이른 시각)"),
                Msg(T(10, 1, 23, 59, 59), UserA, "날짜 경계 직전"),
                Msg(T(10, 2, 0, 0, 0), UserB, "날짜 경계 직후 (자정)"),
                Msg(T(10, 2, 0, 30, 0), UserA, "오전 12시 30분"),
                Msg(T(10, 2, 12, 0, 0), UserB, "정오"),
                Msg(T(10, 2, 12, 30, 0), UserA, "오후 12시 30분"),
                Msg(T(10, 31, 23, 59, 0), UserC, "월 경계 직전"),
                Msg(T(11, 1, 0, 1, 0), UserA, "월 경계 직후"),
                Msg(T(12, 31, 23, 59, 59), UserB, "연도 경계 직전"),
                Msg(T(1, 1, 0, 0, 0, year: 2027), UserC, "연도 경계 직후"),
                Msg(T(12, 31, 23, 0, 0), UserA, "연도 역순 (이전 연도)"),
                Msg(T(2, 29, 10, 0, 0, year: 2028), UserB, "윤일"),
            ],
            AllEncodings);

    private static SampleCase SystemMessages() =>
        new(
            "system-messages",
            "시스템 메시지, 입장/퇴장, 열린 메시지를 닫는 시스템 줄",
            [
                Msg(T(10, 1, 16, 0), UserA, "시스템 메시지 전 메시지", "둘째 줄"),
                new SystemItem(T(10, 1, 16, 1), UserC + " 님이 들어왔습니다.", "Join"),
                Msg(T(10, 1, 16, 2), UserC, "입장 후 첫 메시지"),
                new SystemItem(T(10, 1, 16, 3), UserB + " 님이 나갔습니다.", "Leave"),
                new SystemItem(T(10, 1, 16, 4), "합성 시스템 알림: 방 이름이 변경되었습니다.", "Other"),
                new SystemItem(T(10, 1, 16, 5), Person01 + " 님이 들어왔습니다.", "Join"),
                Msg(T(10, 1, 16, 6), Person01, "시스템 메시지 후 메시지"),
            ],
            AllEncodings);

    private static SampleCase Names() =>
        new(
            "names",
            "이름에 공백/특수문자/한글, 119자 이름",
            [
                Msg(T(10, 1, 17, 0), UserB, "이름에 공백이 있는 참가자"),
                Msg(T(10, 1, 17, 1), SpecialName, "이름에 특수문자가 있는 참가자"),
                Msg(T(10, 1, 17, 2), UserC, "한글 이름 참가자"),
                Msg(T(10, 1, 17, 3), LongName, "긴 이름 참가자 (119자)"),
                Msg(T(10, 1, 17, 4), Person01, "일반 이름 참가자"),
                Msg(T(10, 1, 17, 5), SpecialName, "같은 참가자의 두 번째 메시지"),
            ],
            AllEncodings);

    private static SampleCase Malformed() =>
        new(
            "malformed",
            "잘못된 날짜/시각, 0년, 비정상 숫자 범위, 범위 밖 날짜, 너무 긴/빈 이름, 고아 줄, 알 수 없는 줄, NUL 문자",
            [
                Msg(T(10, 1, 18, 0), UserA, "정상 메시지 (malformed 앞)", "이어지는 줄"),
                new MalformedItem(MalformationKind.InvalidDate),
                Msg(T(10, 1, 18, 1), UserB, "정상 메시지 002"),
                new MalformedItem(MalformationKind.InvalidTime),
                new MalformedItem(MalformationKind.ZeroYear),
                new MalformedItem(MalformationKind.AbsurdNumbers),
                new MalformedItem(MalformationKind.OutOfRangeDate),
                new MalformedItem(MalformationKind.TooLongSpeaker),
                new MalformedItem(MalformationKind.BlankSpeaker),
                new RawLineItem("### 알 수 없는 합성 지시문 ###", RawLineExpectation.OrphanLine),
                new RawLineItem("unknown synthetic line 001", RawLineExpectation.OrphanLine),
                Msg(T(10, 1, 18, 2), UserC, "NUL 줄 앞 메시지"),
                new RawLineItem("합성\0널 문자 포함 줄", RawLineExpectation.NullCharacter),
                new RawLineItem("NUL 줄 뒤의 고아 줄", RawLineExpectation.OrphanLine),
                Msg(T(10, 1, 18, 3), UserA, "정상 메시지 (마지막)"),
            ],
            AllEncodings);

    private static SampleCase ExtraFields() =>
        new(
            "extra-fields",
            "예상하지 못한 추가 헤더/필드 (본문에 포함됨)",
            [
                Msg(T(10, 1, 19, 0), UserA, "합성 | 추가 | 필드"),
                Msg(T(10, 1, 19, 1), UserB, "합성\t추가\t탭 필드"),
                Msg(T(10, 1, 19, 2), UserC, "[추가] 괄호 필드 [15:15]"),
                Msg(T(10, 1, 19, 3), UserA, "* [별표] 필드 | 혼합 \t 구분자"),
            ],
            AllEncodings)
        {
            ExtraHeaderLines = ["Export-Option: synthetic-extra-field=1", "Unknown-Header: 합성 추가 헤더"],
        };

    private static SampleCase TruncatedLastRecord() =>
        new(
            "truncated-last-record",
            "일부만 잘린 마지막 레코드 (newline 없음)",
            [
                Msg(T(10, 1, 20, 0), UserA, "잘린 레코드 앞 메시지"),
                Msg(T(10, 1, 20, 1), UserB, "잘린 레코드 직전 메시지", "둘째 줄"),
                new MalformedItem(MalformationKind.TruncatedRecord),
            ],
            AllEncodings,
            FinalNewline: false);

    private static SampleCase BlankLines() =>
        new(
            "blank-lines",
            "연속된 빈 줄, 공백만 있는 줄, 매우 많은 공백",
            [
                Msg(T(10, 1, 21, 0), UserA, "빈 줄 테스트 메시지 001"),
                new RawLineItem(string.Empty, RawLineExpectation.Ignored),
                new RawLineItem("     ", RawLineExpectation.Ignored),
                new SystemItem(T(10, 1, 21, 1), "합성 시스템 알림: 빈 줄 테스트", "Other"),
                new RawLineItem(string.Empty, RawLineExpectation.Ignored),
                new RawLineItem(string.Empty, RawLineExpectation.Ignored),
                new RawLineItem(string.Empty, RawLineExpectation.Ignored),
                new RawLineItem(new string(' ', 500), RawLineExpectation.Ignored),
                new RawLineItem("\t \t", RawLineExpectation.Ignored),
                Msg(T(10, 1, 21, 2), UserB, "빈 줄 테스트 메시지 002"),
            ],
            AllEncodings);

    private static SampleCase NoHeader() =>
        new(
            "no-header",
            "헤더 없이 레코드로 시작 (내용 기반 형식 판별)",
            [
                Msg(T(10, 1, 22, 0), UserA, "헤더 없는 파일 메시지 001"),
                new SystemItem(T(10, 1, 22, 1), UserB + " 님이 들어왔습니다.", "Join"),
                Msg(T(10, 1, 22, 2), UserB, "헤더 없는 파일 메시지 002", "둘째 줄"),
            ],
            AllEncodings,
            IncludeHeader: false);

    private static SampleCase UnsupportedVersion() =>
        new(
            "unsupported-version",
            "알 수 없는 포맷 버전 서명 (Unsupported, 본문은 계속 해석)",
            [
                Msg(T(10, 1, 23, 0), UserA, "버전 9 파일 메시지 001"),
                Msg(T(10, 1, 23, 1), UserB, "버전 9 파일 메시지 002"),
            ],
            Utf8Only)
        {
            SignatureVersion = 9,
        };

    private static SampleCase VeryLongLine() =>
        new(
            "very-long-line",
            "100,001자 한 줄 (LineTooLong) 앞뒤의 정상 메시지",
            [
                Msg(T(10, 1, 8, 0), UserA, "매우 긴 줄 앞 메시지"),
                new RawLineItem(Repeat("SyntheticLongLine-", TooLongLineLength), RawLineExpectation.LineTooLong),
                Msg(T(10, 1, 8, 1), UserB, "매우 긴 줄 뒤 메시지"),
            ],
            Utf8Only);

    private static SampleCase W1MissingDateContext() =>
        new(
            "missing-date-context",
            "W1 전용: 날짜 표시 줄 전에 나온 메시지 (MissingDateContext)",
            [
                new MalformedItem(MalformationKind.MissingDateContext),
                Msg(T(10, 1, 7, 0), UserA, "날짜 표시 뒤 정상 메시지"),
            ],
            Utf8Only)
        {
            Formats = ["w1"],
        };

    private static string Words(DeterministicRandom random, int count)
    {
        var words = new string[count];
        for (var i = 0; i < count; i++)
        {
            words[i] = FillerWords[random.Next(FillerWords.Length)];
        }

        return string.Join(' ', words);
    }

    private static string Repeat(string unit, int length)
    {
        var text = string.Concat(Enumerable.Repeat(unit, (length / unit.Length) + 1));
        return text[..length];
    }
}
