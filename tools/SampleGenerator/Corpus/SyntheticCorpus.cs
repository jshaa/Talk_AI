namespace TalkPro.Tools.SampleGenerator.Corpus;

/// <summary>
/// Logical golden cases. Every string here is synthetic. The seed only drives long filler text.
/// </summary>
public static class SyntheticCorpus
{
    /// <summary>Letters only (no digits) so random filler can never resemble a phone/account number.</summary>
    private const string Cp949SafeAlphabet = "합성테스트가나다라마바사아자차카타파하 abcdefghijklmnopqrstuvwxyz";

    private const string AsciiAlphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ ";

    public const int MaxLineLength = 100_000;

    public static IReadOnlyList<SampleCase> Build(ulong seed)
    {
        var random = new DeterministicRandom(seed);
        GoldenEncoding[] all = [GoldenEncoding.Utf8, GoldenEncoding.Utf8Bom, GoldenEncoding.Cp949];
        GoldenEncoding[] utf8Only = [GoldenEncoding.Utf8];

        var conversation = Conversation(random);
        return
        [
            new("header-only", "Header lines only, no records.", [], all),
            new("single-message", "One message.", [Msg(2026, 10, 1, 15, 15, SyntheticVocabulary.UserA, "합성 테스트 메시지 001")], all),
            new("single-participant", "One speaker, LF line endings.", SingleParticipant(), utf8Only, LineEnding.Lf),
            new("conversation", "CP949-safe kitchen sink: boundaries, multiline, system lines, look-alike content.", conversation, all),
            new("conversation-lf-nofinal", "Same logical content as 'conversation' with LF and no final newline.", conversation, utf8Only, LineEnding.Lf, FinalNewline: false),
            new("unicode", "Emoji, supplementary characters, mixed scripts (not CP949-representable).", Unicode(), [GoldenEncoding.Utf8, GoldenEncoding.Utf8Bom], LineEnding.Lf),
            new("headerless", "Body without header: detection must use record patterns.", Headerless(), utf8Only, IncludeHeader: false),
            new("robustness", "Malformed, orphan, oversized and control-character lines; truncated last record.", Robustness(random), utf8Only, FinalNewline: false),
        ];
    }

    private static MessageItem Msg(int y, int mo, int d, int h, int mi, string speaker, params string[] lines) =>
        new(new DateTime(y, mo, d, h, mi, 0, DateTimeKind.Unspecified), speaker, lines);

    private static SystemItem Sys(int y, int mo, int d, int h, int mi, string text, string expectedEvent) =>
        new(new DateTime(y, mo, d, h, mi, 0, DateTimeKind.Unspecified), text, expectedEvent);

    private static List<SampleItem> SingleParticipant() =>
    [
        Msg(2026, 9, 30, 8, 0, SyntheticVocabulary.UserA, "합성 테스트 메시지 101"),
        Msg(2026, 9, 30, 8, 1, SyntheticVocabulary.UserA, "합성 테스트 메시지 102"),
        Msg(2026, 9, 30, 8, 2, SyntheticVocabulary.UserA, "합성 테스트 메시지 103", "혼자 쓴 둘째 줄"),
    ];

    private static List<SampleItem> Conversation(DeterministicRandom random) =>
    [
        Msg(2025, 12, 31, 23, 58, SyntheticVocabulary.UserA, "합성 테스트 메시지 001"),
        Msg(2025, 12, 31, 23, 59, SyntheticVocabulary.UserB, "합성 테스트 메시지 002 English text 12345"),
        Sys(2026, 1, 1, 0, 0, SyntheticVocabulary.UserC + " 님이 들어왔습니다.", "Join"),
        Msg(2026, 1, 1, 0, 0, SyntheticVocabulary.UserC, "합성 테스트 메시지 003 (같은 시각)"),
        Msg(2026, 1, 1, 0, 0, SyntheticVocabulary.UserA, "합성 테스트 메시지 004 (같은 시각)"),
        Msg(2026, 1, 1, 0, 5, SyntheticVocabulary.UserB, "합성 테스트 메시지 005 첫 줄", "둘째 줄: 줄바꿈 포함", "", "빈 줄 다음 넷째 줄"),
        Msg(2026, 1, 1, 0, 3, SyntheticVocabulary.SpecialName, "합성 테스트 메시지 006 (시간 역전) 특수문자 !@#$%^&*()_+-=[]{};':\",./<>?~` | 끝"),
        Msg(2026, 1, 1, 0, 10, SyntheticVocabulary.UserA, string.Empty),
        Msg(2026, 1, 1, 0, 11, SyntheticVocabulary.UserA, "   "),
        Msg(2026, 1, 1, 0, 12, SyntheticVocabulary.UserB, "약속은 2026-01-05 오후 3:00 또는 2026.01.05 15:00:00 형식 (날짜처럼 보이는 본문)"),
        Msg(2026, 1, 1, 0, 13, SyntheticVocabulary.UserB, "헤더처럼 보이는 본문", "TalkPro Synthetic Export (W1)", "Room: 합성 테스트방 99", "Saved: 2026-01-01 00:13:00"),
        Msg(2026, 1, 1, 0, 14, SyntheticVocabulary.UserA, "구분선처럼 보이는 본문 === 2026-01-02 (Fri) === 및 ----- 구분 -----", "== 이 줄은 구분선이 아님 =="),
        Msg(2026, 1, 1, 0, 15, SyntheticVocabulary.UserA, "합성 테스트 메시지 007 중복처럼 보이는 메시지"),
        Msg(2026, 1, 1, 0, 15, SyntheticVocabulary.UserA, "합성 테스트 메시지 007 중복처럼 보이는 메시지"),
        Sys(2026, 1, 1, 0, 20, SyntheticVocabulary.UserC + " 님이 나갔습니다.", "Leave"),
        Sys(2026, 1, 1, 0, 21, "합성 시스템 안내: 채팅방 설정이 변경되었습니다.", "Other"),
        Msg(2026, 1, 31, 23, 59, SyntheticVocabulary.Person01, "합성 테스트 메시지 008 월 경계 직전"),
        Msg(2026, 2, 1, 0, 0, SyntheticVocabulary.Person01, "합성 테스트 메시지 009 월 경계 직후 - 확장 완성형 한글 똠 펲 햏 뷁"),
        Msg(2026, 2, 1, 12, 0, SyntheticVocabulary.UserA, "정오(오후 12시) 경계 메시지"),
        Msg(2026, 2, 2, 0, 30, SyntheticVocabulary.UserA, "자정 이후(오전 12시 30분) 경계 메시지"),
        Msg(2026, 2, 2, 9, 5, SyntheticVocabulary.LongName, "긴 이름(119자) 참가자의 합성 메시지"),
        Msg(2026, 2, 2, 9, 6, SyntheticVocabulary.UserB, "긴 합성 메시지: " + random.Text(Cp949SafeAlphabet, 2_000)),
        Msg(2026, 2, 2, 9, 7, SyntheticVocabulary.UserB, "끝에 공백이 있는 줄   ", "    앞에 공백이 있는 줄", "매우 많은 공백:" + new string(' ', 200) + "끝"),
    ];

    private static List<SampleItem> Unicode() =>
    [
        Msg(2026, 4, 1, 9, 0, SyntheticVocabulary.UserA, "이모지 😀👍🏽 그리고 가족 👨‍👩‍👧 합성"),
        Msg(2026, 4, 1, 9, 1, SyntheticVocabulary.EmojiName, "보충 평면 문자 𠀀 𝄞 𐐷 합성"),
        Msg(2026, 4, 1, 9, 2, SyntheticVocabulary.UserB, "혼합 언어 Mixed 日本語 テスト Ελληνικά Русский العربية 中文 합성"),
        Msg(2026, 4, 1, 9, 3, SyntheticVocabulary.UserA, "결합 문자 é 와 자모 가 합성", "NBSP 와 전각 공백　포함"),
    ];

    private static List<SampleItem> Headerless() =>
    [
        Msg(2026, 5, 1, 7, 0, SyntheticVocabulary.UserA, "헤더 없는 합성 메시지 001"),
        Msg(2026, 5, 1, 7, 1, SyntheticVocabulary.UserB, "헤더 없는 합성 메시지 002"),
        Sys(2026, 5, 1, 7, 2, SyntheticVocabulary.UserB + " 님이 나갔습니다.", "Leave"),
        Msg(2026, 5, 1, 7, 3, SyntheticVocabulary.UserA, "헤더 없는 합성 메시지 003"),
    ];

    private static List<SampleItem> Robustness(DeterministicRandom random) =>
    [
        new RawLineItem(string.Empty, RawLineExpectation.Ignored),
        new RawLineItem(string.Empty, RawLineExpectation.Ignored),
        new RawLineItem(new string(' ', 500), RawLineExpectation.Ignored),
        new RawLineItem("알 수 없는 합성 줄 (열린 메시지 없음)", RawLineExpectation.OrphanLine),
        Msg(2026, 3, 1, 10, 0, SyntheticVocabulary.UserA, "비정상 줄 테스트 직전 합성 메시지", "제어문자 포함\u0007벨 및 \u001B이스케이프 합성"),
        new MalformedItem(MalformationKind.InvalidDate),
        new RawLineItem("고아 줄: 비정상 레코드 다음 합성 줄", RawLineExpectation.OrphanLine),
        Msg(2026, 3, 1, 10, 1, SyntheticVocabulary.UserA, "시간 오류 테스트 직전 합성 메시지"),
        new MalformedItem(MalformationKind.InvalidTime),
        new MalformedItem(MalformationKind.ZeroYear),
        new MalformedItem(MalformationKind.OutOfRangeDate),
        new MalformedItem(MalformationKind.TooLongSpeaker),
        new MalformedItem(MalformationKind.BlankSpeaker),
        Msg(2026, 3, 1, 10, 3, SyntheticVocabulary.UserB, "허용되는 긴 줄 " + random.Text(AsciiAlphabet, 20_000)),
        new RawLineItem("허용 길이 초과 " + random.Text(AsciiAlphabet, MaxLineLength), RawLineExpectation.LineTooLong),
        Msg(2026, 3, 1, 10, 4, SyntheticVocabulary.UserB, "추가 필드 테스트 | extra | field\textra2\tfield2 [extra3] 합성"),
        Msg(2026, 3, 1, 10, 5, SyntheticVocabulary.UserA, "NUL 문자 테스트 직전 합성 메시지"),
        new RawLineItem("NUL\0포함 합성 줄", RawLineExpectation.NullCharacter),
        new RawLineItem("\0\0\0", RawLineExpectation.NullCharacter),
        Msg(2026, 3, 1, 10, 6, SyntheticVocabulary.UserA, "마지막 정상 합성 메시지"),
        new MalformedItem(MalformationKind.TruncatedRecord),
    ];
}
