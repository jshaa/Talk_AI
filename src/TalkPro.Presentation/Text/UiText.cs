using System.Globalization;
using TalkPro.Core.Llm;
using TalkPro.Core.Model;
using TalkPro.Persona.Style;
using TalkPro.Ingestion.Parsing;
using TalkPro.Ingestion.Speakers;
using TalkPro.Privacy;
using TalkPro.Presentation.Services;

namespace TalkPro.Presentation.Text;

/// <summary>Korean UI strings for domain codes. Never includes file paths or message content.</summary>
public static class UiText
{
    public static readonly CultureInfo Korean = CultureInfo.GetCultureInfo("ko-KR");

    public static string ForImport(ImportParseResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.FileError switch
        {
            ImportFileError.FileNotFound => "파일을 찾을 수 없습니다.",
            ImportFileError.ReadFailed => "파일을 읽을 수 없습니다. 다른 프로그램이 사용 중인지 확인해 주세요.",
            _ => result.ParseFailure switch
            {
                ParseFailure.Empty => "빈 파일입니다.",
                ParseFailure.TooLarge => "파일이 너무 큽니다(최대 512MB).",
                ParseFailure.UnsupportedEncoding => "인코딩을 확인할 수 없습니다. UTF-8 또는 CP949로 저장된 내보내기 파일만 지원합니다.",
                ParseFailure.UnknownFormat => "지원하는 내보내기 형식이 아닙니다. 지금은 카카오톡 PC(Windows/Mac)에서 '대화 내보내기'로 저장한 .txt 파일을 읽습니다. 휴대폰(Android·iPhone)에서 내보낸 형식은 아직 지원하지 않습니다.",
                ParseFailure.AmbiguousFormat => "파일 형식을 하나로 판별할 수 없습니다.",
                _ => "알 수 없는 오류가 발생했습니다.",
            },
        };
    }

    public static string For(ReviewError error) => error switch
    {
        ReviewError.None => string.Empty,
        ReviewError.UnknownParticipant => "대상을 찾을 수 없습니다.",
        ReviewError.SameParticipant => "같은 사람끼리는 병합할 수 없습니다.",
        ReviewError.ParticipantExcluded => "제외된 사람은 본인으로 지정할 수 없습니다. 먼저 포함으로 바꿔 주세요.",
        ReviewError.OwnerCannotBeExcluded => "본인은 제외할 수 없습니다.",
        ReviewError.OwnerHasNoMessages => "메시지가 없는 사람은 본인으로 지정할 수 없습니다.",
        ReviewError.NoMessagesSelected => "분리할 메시지가 없습니다. 날짜를 확인해 주세요.",
        ReviewError.MessageNotFromParticipant => "다른 사람의 메시지는 분리할 수 없습니다.",
        ReviewError.SplitWouldLeaveNoMessages => "모든 메시지를 옮길 수는 없습니다. 더 늦은 날짜를 선택해 주세요.",
        ReviewError.UnknownSuspicion => "확인할 항목을 찾을 수 없습니다.",
        ReviewError.SuspicionNotOpen => "이미 처리된 항목입니다.",
        _ => "요청을 처리할 수 없습니다.",
    };

    public static string For(CompletionError error) => error switch
    {
        CompletionError.AttestationNotConfirmed => "참여 확인 서약이 필요합니다.",
        CompletionError.OwnerNotSelected => "본인(나)을 선택해 주세요.",
        CompletionError.OpenSuspicions => "확인이 필요한 항목이 남아 있습니다.",
        _ => "완료할 수 없습니다.",
    };

    public static string For(ParticipantConsent consent) => consent switch
    {
        ParticipantConsent.Owner => "본인",
        ParticipantConsent.ContextOnly => "맥락만 사용",
        ParticipantConsent.Excluded => "제외",
        ParticipantConsent.Consented => "동의함",
        _ => consent.ToString(),
    };

    public static string Describe(SpeakerSuspicion suspicion, Func<ParticipantId, string> name)
    {
        ArgumentNullException.ThrowIfNull(suspicion);
        ArgumentNullException.ThrowIfNull(name);
        var names = string.Join(" / ", suspicion.Participants.Select(name));
        return suspicion.Reason switch
        {
            SuspicionReason.NormalizedNameMatch => $"이름이 거의 같습니다: {names} — 같은 사람이면 병합하세요.",
            SuspicionReason.NameContainment => $"한 이름이 다른 이름을 포함합니다: {names} — 닉네임 변경일 수 있습니다.",
            SuspicionReason.LongInactivityGap => $"{names}: 오랫동안(180일 이상) 말이 없다가 다시 등장합니다 — 같은 이름의 다른 사람일 수 있습니다.",
            SuspicionReason.LeftAndReturned => $"{names}: 나간 뒤 다시 대화합니다 — 같은 이름의 다른 사람일 수 있습니다.",
            _ => names,
        };
    }

    public static string For(PiiType type) => type switch
    {
        PiiType.Phone => "전화번호",
        PiiType.Email => "이메일",
        PiiType.ResidentId => "주민등록번호",
        PiiType.Card => "카드번호",
        PiiType.Account => "계좌번호",
        PiiType.IpAddress => "IP 주소",
        PiiType.Url => "링크(URL)",
        PiiType.Address => "주소",
        PiiType.Person => "사람 이름",
        PiiType.Title => "직함·호칭",
        PiiType.Organization => "기관·학교·병원",
        PiiType.Health => "건강",
        PiiType.MentalHealth => "정신건강",
        PiiType.Religion => "종교",
        PiiType.Political => "정치",
        PiiType.Sexual => "성생활",
        PiiType.Criminal => "범죄·법적 문제",
        PiiType.Finance => "금융 상태",
        _ => type.ToString(),
    };

    public static string For(PiiCategory category) => category switch
    {
        PiiCategory.HighRiskIdentifier => "고위험 식별번호",
        PiiCategory.Identifier => "연락처·식별정보",
        PiiCategory.Name => "이름",
        PiiCategory.Organization => "기관",
        PiiCategory.Sensitive => "민감한 주제",
        _ => category.ToString(),
    };

    public static string For(PrivacyAction action) => action switch
    {
        PrivacyAction.Keep => "유지",
        PrivacyAction.Mask => "마스킹",
        PrivacyAction.Exclude => "분석 제외",
        PrivacyAction.Delete => "삭제(저장 안 함)",
        _ => action.ToString(),
    };

    public static string For(PrivacyReviewError error) => error switch
    {
        PrivacyReviewError.None => string.Empty,
        PrivacyReviewError.ActionNotAllowed => "이 항목에는 선택할 수 없는 처리 방식입니다. 주민등록번호·카드·계좌번호는 유지할 수 없습니다.",
        PrivacyReviewError.UnknownFinding => "항목을 찾을 수 없습니다.",
        _ => "요청을 처리할 수 없습니다.",
    };

    public static string For(AnalyticsError error) => error switch
    {
        AnalyticsError.None => string.Empty,
        AnalyticsError.Unreadable => "데이터셋을 열 수 없습니다. 손상되었거나 다른 Windows 사용자 계정에서 만든 데이터일 수 있습니다.",
        AnalyticsError.NotPrivacyReviewed => "개인정보 검토 전에 가져온 데이터셋은 분석할 수 없습니다. 삭제한 뒤 다시 가져와 주세요.",
        AnalyticsError.NoOwner => "본인(나)이 지정되지 않은 데이터셋은 분석할 수 없습니다.",
        _ => "분석할 수 없습니다.",
    };

    public static string For(ModelRole role) => role switch
    {
        ModelRole.Chat => "대화",
        ModelRole.Embedding => "임베딩(검색)",
        _ => role.ToString(),
    };

    public static string For(ModelApproval approval) => approval switch
    {
        ModelApproval.Approved => "승인됨",
        ModelApproval.Candidate => "후보(개발용)",
        ModelApproval.LicenseReview => "라이선스 검토 필요(개발용)",
        _ => approval.ToString(),
    };

    public static string For(ModelInstallState state) => state switch
    {
        ModelInstallState.NotInstalled => "설치 안 됨",
        ModelInstallState.Ready => "사용 가능",
        ModelInstallState.DigestMismatch => "검증 실패(파일이 다름)",
        ModelInstallState.NotApproved => "이 빌드에서 사용 불가",
        _ => state.ToString(),
    };

    public static string For(LlmError error) => error switch
    {
        LlmError.None => string.Empty,
        LlmError.RuntimeNotRunning => "Ollama가 실행 중이 아닙니다.",
        LlmError.ModelNotInstalled => "사용할 수 있는 모델이 설치되어 있지 않습니다.",
        LlmError.ModelNotAllowed => "설치된 모델이 허용 목록의 검증을 통과하지 못했습니다.",
        LlmError.Timeout => "응답 시간이 초과되었습니다. PC 사양에 비해 모델이 클 수 있습니다.",
        LlmError.ResponseTooLarge => "응답이 너무 큽니다.",
        LlmError.InvalidResponse => "AI 응답을 해석할 수 없습니다.",
        LlmError.Cancelled => "취소되었습니다.",
        _ => "AI를 사용할 수 없습니다.",
    };

    public static string For(DraftError error) => error switch
    {
        DraftError.None => string.Empty,
        DraftError.Unreadable => "데이터셋을 열 수 없습니다.",
        DraftError.NotPrivacyReviewed => "개인정보 검토 전에 가져온 데이터셋은 사용할 수 없습니다. 삭제한 뒤 다시 가져와 주세요.",
        DraftError.NoOwner => "본인(나)이 지정되지 않은 데이터셋입니다.",
        DraftError.EmptyInput => "받은 메시지를 입력해 주세요.",
        DraftError.InputTooLong => "메시지가 너무 깁니다(최대 1,000자).",
        DraftError.Model => "로컬 AI를 사용할 수 없습니다.",
        DraftError.InvalidOutput => "AI가 알맞은 초안을 만들지 못했습니다. 다시 시도해 주세요.",
        _ => "초안을 만들 수 없습니다.",
    };

    public static string For(ReplyDirection direction) => direction switch
    {
        ReplyDirection.Accept => "수락",
        ReplyDirection.Hold => "보류",
        ReplyDirection.AskBack => "되묻기",
        _ => direction.ToString(),
    };

    public const string DraftAll = "초안 만들기";
    public const string RedraftAll = "전체 다시 생성";
    public const string SameDirectionWarning = "방향이 모두 같습니다. 서로 다른 답장을 원하면 초안마다 방향을 바꿔 보세요.";
    public const string DirectionChanged = "방향을 바꿨습니다. '이 초안만 다시 생성'을 누르면 이 초안만 새로 씁니다.";
    public const string DraftMissing = "이 방향의 초안을 만들지 못했습니다. '이 초안만 다시 생성'을 눌러 다시 시도해 주세요.";
    public const string Regenerating = "이 초안만 다시 쓰는 중입니다…";

    public static string For(ModelInstallError error) => error switch
    {
        ModelInstallError.None => "설치 완료",
        ModelInstallError.Network => "내려받지 못했습니다. 인터넷 연결을 확인한 뒤 다시 시도하면 이어서 받습니다.",
        ModelInstallError.HashMismatch => "내려받은 파일이 검증(SHA-256)을 통과하지 못해 삭제했습니다. 다시 시도해 주세요.",
        ModelInstallError.Storage => "저장 공간이 부족하거나 파일을 쓸 수 없습니다.",
        ModelInstallError.Cancelled => "취소되었습니다.",
        _ => "설치하지 못했습니다.",
    };

    public const string NotFoundInRecords = "기록에서 찾지 못했습니다.";

    public static string For(MemoryError error) => error switch
    {
        MemoryError.None => string.Empty,
        MemoryError.Unreadable => "데이터셋을 열 수 없습니다.",
        MemoryError.NotPrivacyReviewed => "개인정보 검토 전에 가져온 데이터셋은 검색할 수 없습니다. 삭제한 뒤 다시 가져와 주세요.",
        MemoryError.NoOwner => "본인(나)이 지정되지 않은 데이터셋입니다.",
        MemoryError.EmptyQuery => "찾을 내용을 입력해 주세요.",
        MemoryError.QueryTooLong => "검색어가 너무 깁니다(최대 200자).",
        MemoryError.Model => "로컬 AI를 사용할 수 없습니다.",
        _ => "검색할 수 없습니다.",
    };

    public static string For(MemoryIndexState state, int items, string? model) => state switch
    {
        MemoryIndexState.Ready => string.Create(Korean, $"기억 색인 준비됨 · 항목 {items:N0}개 · {model} — 뜻과 단어로 함께 찾습니다."),
        MemoryIndexState.Stale => "데이터가 바뀌어 기억 색인을 다시 만들어야 합니다. 그 전까지는 단어 검색만 합니다.",
        _ => "기억 색인이 없습니다. 지금은 단어 검색만 됩니다. '기억 색인 만들기'를 누르면 뜻으로도 찾습니다.",
    };

    public static string For(SearchMode mode) => mode switch
    {
        SearchMode.Hybrid => "검색 방식: 뜻 + 단어",
        _ => "검색 방식: 단어만",
    };

    public static string FormatDate(DateTimeOffset? value) =>
        value is { } v ? v.ToLocalTime().ToString("yyyy-MM-dd HH:mm", Korean) : "-";

    public static string Encoding(SourceEncoding encoding) => encoding switch
    {
        SourceEncoding.Utf8 => "UTF-8",
        SourceEncoding.Utf8WithBom => "UTF-8 (BOM)",
        SourceEncoding.Cp949 => "CP949",
        _ => encoding.ToString(),
    };
}
