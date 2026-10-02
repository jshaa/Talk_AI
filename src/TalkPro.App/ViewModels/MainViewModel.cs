using CommunityToolkit.Mvvm.ComponentModel;

namespace TalkPro.App.ViewModels;

public sealed record NavigationSection(string Title, string Description, string AvailableIn);

public sealed class MainViewModel : ObservableObject
{
    public string LocalOnlyNotice { get; } =
        "모든 대화 데이터는 이 PC 안에서만 처리됩니다 · 외부 전송 없음 · 사용 통계 수집 없음";

    // Prior notice required by the AI Basic Act (Art. 31(1)) for generative-AI-based products.
    public string GenerativeAiNotice { get; } =
        "이 앱은 생성형 AI(로컬 모델)를 사용합니다. AI가 만든 결과물에는 'AI 생성' 표시가 붙습니다.";

    public IReadOnlyList<NavigationSection> Sections { get; } =
    [
        new("가져오기", "메신저 TXT 내보내기 파일 가져오기", "Phase 1"),
        new("개인정보 검토", "민감정보 확인 · 마스킹 · 본인 화자 선택", "Phase 3"),
        new("대화 분석", "나의 말투 · 시간대 · 사건 타임라인", "Phase 4"),
        new("내 말투 답장 초안", "AI 생성 초안 3개 (자동 발송 없음)", "Phase 6"),
        new("기억 검색", "실제 메시지 근거만 표시", "Phase 7"),
        new("데이터 삭제", "데이터셋 암호화 키 파기 및 삭제", "Phase 2"),
    ];
}
