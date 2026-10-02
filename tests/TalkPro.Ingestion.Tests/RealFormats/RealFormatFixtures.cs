using TalkPro.Core.Model;
using TalkPro.Core.Parsing;
using TalkPro.Ingestion.RealFormats;
using static TalkPro.Core.Parsing.LineOutcome;

namespace TalkPro.Ingestion.Tests.RealFormats;

/// <summary>One fixture line and the outcome defined by hand from the format grammar (docs/REAL_FORMATS.md).</summary>
internal sealed record FixtureLine(string Text, LineOutcome Outcome, ParseIssue Issue = ParseIssue.None);

/// <summary>An expected message. <see cref="Speaker"/> = <see langword="null"/> means a system entry.</summary>
internal sealed record ExpectedMessage(int Line, string Time, string? Speaker, string Text, SystemEventType Event = SystemEventType.None);

/// <summary>
/// A hand-written, sanitized fixture of a real export layout. Structure follows the documented
/// grammar; every name and text is synthetic. Expectations are never produced by parser code.
/// </summary>
internal sealed record RealFormatFixture(string Name, ChatFormatId Format, bool Cp949Compatible, IReadOnlyList<FixtureLine> Lines, IReadOnlyList<ExpectedMessage> Messages)
{
    public override string ToString() => Name;
}

internal static class RealFormatFixtures
{
    private static readonly ChatFormatId Windows = WindowsChatFormatAdapter.Id;
    private static readonly ChatFormatId Android = AndroidChatFormatAdapter.Id;
    private static readonly ChatFormatId Ios = IosChatFormatAdapter.Id;

    public static IReadOnlyList<RealFormatFixture> All { get; } =
    [
        WindowsTwelveHour(),
        WindowsTwentyFourHour(),
        WindowsUnicode(),
        WindowsTruncated(),
        AndroidKorean(),
        AndroidUnicode(),
        IosTimeOnly(),
        IosDotted(),
        IosUnicode(),
    ];

    public static RealFormatFixture Get(string name) => All.Single(f => f.Name == name);

    private static FixtureLine L(string text, LineOutcome outcome, ParseIssue issue = ParseIssue.None) => new(text, outcome, issue);

    private static RealFormatFixture WindowsTwelveHour() => new(
        "windows-12h",
        Windows,
        Cp949Compatible: true,
        [
            L("합성 테스트방 01 님과 카카오톡 대화", Metadata),                          // 1
            L("저장한 날짜 : 2026-10-02 14:23:11", Metadata),                            // 2
            L(string.Empty, Ignored),                                                     // 3 header ends
            L("--------------- 2026년 10월 1일 목요일 ---------------", Metadata),       // 4
            L("[UserA] [오후 3:15] 합성 인용 [User B] [오후 3:10] 이전 말", MessageStarted), // 5 sender ends at the first "] [time]"
            L("[User B] [오후 3:16] 첫 줄", MessageStarted),                              // 6
            L("둘째 줄", Continuation),                                                   // 7
            L(string.Empty, Continuation),                                                // 8 blank inside a message
            L("넷째 줄 [UserA] [오후 3:17] 본문 속 형식", Continuation),                  // 9 record-looking text not at line start
            L("[공지] 오후 3:00 회의", Continuation),                                     // 10 bracketed text is not a record
            L("UserA님이 나갔습니다.", Continuation),                                     // 11 untimed notice: indistinguishable from text
            L("[Synth#Person (테스트)] [오후 11:59] 자정 직전", MessageStarted),          // 12
            L("--------------- 2026년 10월 2일 금요일 ---------------", Metadata),       // 13
            L("[합성사용자 C] [오전 12:00] 자정", MessageStarted),                        // 14
            L("[UserA] [오후 12:00] 정오", MessageStarted),                               // 15
            L("[] [오후 12:01] 합성 시스템 알림", SystemMessage),                         // 16 empty sender = notice
            L("[UserA] [오후 13:05] 잘못된 시각", Malformed, ParseIssue.InvalidTimestamp), // 17
            L("합성 고아 줄", Malformed, ParseIssue.OrphanLine),                          // 18
            L("[UserA] [오후 12:02]", MessageStarted),                                    // 19 empty text
            L("   ", Continuation),                                                       // 20 trailing blank, dropped
            L("[User B] [오후 12:03] 마지막 메시지", MessageStarted),                     // 21 flushed at end of input
        ],
        [
            new(5, "2026-10-01 15:15", "UserA", "합성 인용 [User B] [오후 3:10] 이전 말"),
            new(6, "2026-10-01 15:16", "User B", "첫 줄\n둘째 줄\n\n넷째 줄 [UserA] [오후 3:17] 본문 속 형식\n[공지] 오후 3:00 회의\nUserA님이 나갔습니다."),
            new(12, "2026-10-01 23:59", "Synth#Person (테스트)", "자정 직전"),
            new(14, "2026-10-02 00:00", "합성사용자 C", "자정"),
            new(15, "2026-10-02 12:00", "UserA", "정오"),
            new(16, "2026-10-02 12:01", null, "합성 시스템 알림", SystemEventType.Other),
            new(19, "2026-10-02 12:02", "UserA", string.Empty),
            new(21, "2026-10-02 12:03", "User B", "마지막 메시지"),
        ]);

    private static RealFormatFixture WindowsTwentyFourHour() => new(
        "windows-24h",
        Windows,
        Cp949Compatible: true,
        [
            L("UserA 님과 카카오톡 대화", Metadata),                                     // 1
            L("저장한 날짜 : 2026년 10월 2일 오후 2:23", Metadata),                      // 2
            L(string.Empty, Ignored),                                                     // 3
            L("--------------- 2026년 10월 1일 목요일 ---------------", Metadata),       // 4
            L("[UserA] [20:35] 24시간제 메시지", MessageStarted),                          // 5
            L("[User B] [00:05] 시간 역순 메시지", MessageStarted),                        // 6 order preserved
            L("[UserA] [24:00] 잘못된 시각", Malformed, ParseIssue.InvalidTimestamp),       // 7
            L("--------------- 2026년 2월 30일 월요일 ---------------", Malformed, ParseIssue.InvalidTimestamp), // 8 clears date
            L("[UserA] [09:00] 날짜 없음", Malformed, ParseIssue.MissingDateContext),       // 9
            L("--------------- 2026년 10월 2일 금요일 ---------------", Metadata),       // 10
            L("[UserA] [09:01] 마지막", MessageStarted),                                   // 11
        ],
        [
            new(5, "2026-10-01 20:35", "UserA", "24시간제 메시지"),
            new(6, "2026-10-01 00:05", "User B", "시간 역순 메시지"),
            new(11, "2026-10-02 09:01", "UserA", "마지막"),
        ]);

    private static RealFormatFixture WindowsUnicode() => new(
        "windows-unicode",
        Windows,
        Cp949Compatible: false,
        [
            L("UserA 님과 카카오톡 대화", Metadata),
            L("저장한 날짜 : 2026-10-02 14:23:11", Metadata),
            L(string.Empty, Ignored),
            L("--------------- 2026년 10월 1일 목요일 ---------------", Metadata),
            L("[Synthetic🙂User] [오후 3:15] 이모지 😀🎉 보충 평면 𝄞", MessageStarted),
            L("ZWJ 👨‍👩‍👧‍👦 国旗 🇰🇷", Continuation),
        ],
        [new(5, "2026-10-01 15:15", "Synthetic🙂User", "이모지 😀🎉 보충 평면 𝄞\nZWJ 👨‍👩‍👧‍👦 国旗 🇰🇷")]);

    /// <summary>A record cut inside its timestamp has no record shape, so it is kept as text (documented limitation).</summary>
    private static RealFormatFixture WindowsTruncated() => new(
        "windows-truncated",
        Windows,
        Cp949Compatible: true,
        [
            L("UserA 님과 카카오톡 대화", Metadata),
            L("저장한 날짜 : 2026-10-02 14:23:11", Metadata),
            L(string.Empty, Ignored),
            L("--------------- 2026년 10월 1일 목요일 ---------------", Metadata),
            L("[UserA] [오후 3:15] 잘린 레코드 앞", MessageStarted),
            L("[User B] [오후 3:16] 잘린 메", MessageStarted),
            L("[UserA] [오후 3:", Continuation),
        ],
        [
            new(5, "2026-10-01 15:15", "UserA", "잘린 레코드 앞"),
            new(6, "2026-10-01 15:16", "User B", "잘린 메\n[UserA] [오후 3:"),
        ]);

    private static RealFormatFixture AndroidKorean() => new(
        "android",
        Android,
        Cp949Compatible: true,
        [
            L("합성 테스트방 01 카카오톡 대화", Metadata),                                       // 1
            L("저장한 날짜 : 2026년 10월 2일 오후 2:23", Metadata),                             // 2
            L(string.Empty, Ignored),                                                            // 3 header ends
            L(string.Empty, Ignored),                                                            // 4
            L("2026년 10월 1일 오후 3:15", Metadata),                                            // 5 date line
            L("2026년 10월 1일 오후 3:15, UserA : 합성 테스트 메시지 001", MessageStarted),      // 6
            L("2026년 10월 1일 오후 3:16, User B : 첫 줄 : 콜론 포함", MessageStarted),          // 7 sender ends at first " : "
            L("UserA : 본문 속 화자 형식", Continuation),                                       // 8
            L(string.Empty, Continuation),                                                       // 9
            L("2026년 10월 1일 오후 3:17 이후 일정", Continuation),                              // 10 timestamp-like text
            L("2026년 10월 1일 오후 11:59, 합성사용자 C : 자정 직전", MessageStarted),          // 11
            L(string.Empty, Continuation),                                                       // 12 trailing blank, dropped
            L("2026년 10월 2일 오전 12:00", Metadata),                                           // 13 date line completes 11
            L("2026년 10월 2일 오전 12:00, Synth#Person (테스트) : 자정", MessageStarted),       // 14
            L("2026년 10월 2일 오후 12:00, UserA : 정오", MessageStarted),                       // 15
            L("2026년 10월 2일 오후 12:01, User B님이 나갔습니다.", SystemMessage),               // 16
            L("2026년 10월 2일 오후 12:02, UserA님이 합성사용자 C님을 초대하였습니다.", SystemMessage), // 17
            L("2026년 10월 2일 오후 12:03,  : 합성 시스템 알림", SystemMessage),                 // 18 empty sender
            L("2026년 10월 2일 오후 13:04, UserA : 잘못된 시각", Malformed, ParseIssue.InvalidTimestamp),  // 19
            L("2026년 13월 2일 오후 1:04, UserA : 잘못된 날짜", Malformed, ParseIssue.InvalidTimestamp),   // 20
            L("2101년 1월 1일 오전 9:00, UserA : 범위 밖", Malformed, ParseIssue.TimestampOutOfRange),     // 21
            L("합성 고아 줄", Malformed, ParseIssue.OrphanLine),                                 // 22
            L("2026년 10월 2일 오후 12:05, UserA : ", MessageStarted),                           // 23 empty text
            L("2026년 10월 2일 오후 12:06, User B : 마지막", MessageStarted),                    // 24
        ],
        [
            new(6, "2026-10-01 15:15", "UserA", "합성 테스트 메시지 001"),
            new(7, "2026-10-01 15:16", "User B", "첫 줄 : 콜론 포함\nUserA : 본문 속 화자 형식\n\n2026년 10월 1일 오후 3:17 이후 일정"),
            new(11, "2026-10-01 23:59", "합성사용자 C", "자정 직전"),
            new(14, "2026-10-02 00:00", "Synth#Person (테스트)", "자정"),
            new(15, "2026-10-02 12:00", "UserA", "정오"),
            new(16, "2026-10-02 12:01", null, "User B님이 나갔습니다.", SystemEventType.Leave),
            new(17, "2026-10-02 12:02", null, "UserA님이 합성사용자 C님을 초대하였습니다.", SystemEventType.Invite),
            new(18, "2026-10-02 12:03", null, "합성 시스템 알림", SystemEventType.Other),
            new(23, "2026-10-02 12:05", "UserA", string.Empty),
            new(24, "2026-10-02 12:06", "User B", "마지막"),
        ]);

    private static RealFormatFixture AndroidUnicode() => new(
        "android-unicode",
        Android,
        Cp949Compatible: false,
        [
            L("UserA 님과 카카오톡 대화", Metadata),
            L("저장한 날짜 : 2026년 10월 2일 오후 2:23", Metadata),
            L(string.Empty, Ignored),
            L(string.Empty, Ignored),
            L("2026년 10월 1일 오후 3:15", Metadata),
            L("2026년 10월 1일 오후 3:15, Synthetic🙂User : 이모지 😀 보충 평면 𝄞", MessageStarted),
        ],
        [new(6, "2026-10-01 15:15", "Synthetic🙂User", "이모지 😀 보충 평면 𝄞")]);

    private static RealFormatFixture IosTimeOnly() => new(
        "ios-time-only",
        Ios,
        Cp949Compatible: true,
        [
            L("UserA 님과 카카오톡 대화", Metadata),                                  // 1
            L("저장한 날짜 : 2026년 10월 2일 오후 2:23", Metadata),                   // 2
            L(string.Empty, Ignored),                                                  // 3
            L(string.Empty, Ignored),                                                  // 4
            L("2026년 10월 1일 목요일", Metadata),                                     // 5 date header
            L("오후 3:15, UserA : 합성 테스트 메시지 001", MessageStarted),            // 6
            L("오후 3:16, User B : 첫 줄", MessageStarted),                            // 7
            L("오후 3:00에 만나요", Continuation),                                     // 8 time-like text
            L(string.Empty, Continuation),                                             // 9
            L("UserA : 본문 속 화자 형식", Continuation),                              // 10
            L("오후 11:59, 합성사용자 C : 자정 직전", MessageStarted),                 // 11
            L("2026년 10월 2일 금요일", Metadata),                                     // 12
            L("오전 12:00, Synth#Person (테스트) : 자정", MessageStarted),             // 13
            L("오후 12:00, UserA : 정오", MessageStarted),                             // 14
            L("오후 12:01, User B님이 나갔습니다.", SystemMessage),                     // 15
            L("오전 12:02,  : 합성 시스템 알림", SystemMessage),                       // 16 order preserved
            L("오후 0:30, UserA : 잘못된 시각", Malformed, ParseIssue.InvalidTimestamp),   // 17
            L("2026년 2월 30일 월요일", Malformed, ParseIssue.InvalidTimestamp),           // 18 clears date
            L("오후 1:00, UserA : 날짜 없음", Malformed, ParseIssue.MissingDateContext),   // 19
            L("합성 고아 줄", Malformed, ParseIssue.OrphanLine),                       // 20
            L("2026년 10월 3일 토요일", Metadata),                                     // 21
            L("오후 12:05, UserA : 마지막", MessageStarted),                           // 22
        ],
        [
            new(6, "2026-10-01 15:15", "UserA", "합성 테스트 메시지 001"),
            new(7, "2026-10-01 15:16", "User B", "첫 줄\n오후 3:00에 만나요\n\nUserA : 본문 속 화자 형식"),
            new(11, "2026-10-01 23:59", "합성사용자 C", "자정 직전"),
            new(13, "2026-10-02 00:00", "Synth#Person (테스트)", "자정"),
            new(14, "2026-10-02 12:00", "UserA", "정오"),
            new(15, "2026-10-02 12:01", null, "User B님이 나갔습니다.", SystemEventType.Leave),
            new(16, "2026-10-02 00:02", null, "합성 시스템 알림", SystemEventType.Other),
            new(22, "2026-10-03 12:05", "UserA", "마지막"),
        ]);

    private static RealFormatFixture IosDotted() => new(
        "ios-dotted",
        Ios,
        Cp949Compatible: true,
        [
            L("UserA 님과 카카오톡 대화", Metadata),                                           // 1
            L("저장한 날짜 : 2026. 10. 2. 오후 2:23", Metadata),                               // 2
            L(string.Empty, Ignored),                                                           // 3
            L(string.Empty, Ignored),                                                           // 4
            L("2026년 10월 1일 목요일", Metadata),                                              // 5
            L("2026. 10. 1. 오후 3:15, UserA : 합성 테스트 메시지 001", MessageStarted),        // 6
            L("2026. 10. 1. 오후 3:16, User B : 둘째", MessageStarted),                         // 7
            L("이어지는 줄", Continuation),                                                     // 8
            L("2026. 10. 1. 오후 11:59, User B님이 나갔습니다.", SystemMessage),                 // 9
            L("2026. 10. 2. 오전 12:00, UserA : 날짜 헤더 없이 바뀐 날짜", MessageStarted),     // 10 own date
            L("2026. 2. 30. 오전 9:00, UserA : 잘못된 날짜", Malformed, ParseIssue.InvalidTimestamp), // 11
            L("2026. 10. 2. 오후 12:00, User B : 정오", MessageStarted),                        // 12
        ],
        [
            new(6, "2026-10-01 15:15", "UserA", "합성 테스트 메시지 001"),
            new(7, "2026-10-01 15:16", "User B", "둘째\n이어지는 줄"),
            new(9, "2026-10-01 23:59", null, "User B님이 나갔습니다.", SystemEventType.Leave),
            new(10, "2026-10-02 00:00", "UserA", "날짜 헤더 없이 바뀐 날짜"),
            new(12, "2026-10-02 12:00", "User B", "정오"),
        ]);

    private static RealFormatFixture IosUnicode() => new(
        "ios-unicode",
        Ios,
        Cp949Compatible: false,
        [
            L("UserA 님과 카카오톡 대화", Metadata),
            L("저장한 날짜 : 2026년 10월 2일 오후 2:23", Metadata),
            L(string.Empty, Ignored),
            L(string.Empty, Ignored),
            L("2026년 10월 1일 목요일", Metadata),
            L("오후 3:15, Synthetic🙂User : 이모지 😀 보충 평면 𝄞", MessageStarted),
            L("다국어 English 日本語 Ελληνικά", Continuation),
        ],
        [new(6, "2026-10-01 15:15", "Synthetic🙂User", "이모지 😀 보충 평면 𝄞\n다국어 English 日本語 Ελληνικά")]);
}
