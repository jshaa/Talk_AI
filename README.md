# Talk_AI (TalkPro)

카카오톡 대화 내보내기 파일로 **대화 분석 · 내 말투 답장 초안 · 근거 기반 기억 검색**을 제공하는 개인용 AI입니다.
대화 원문은 사용자의 PC·휴대폰 밖으로 나가지 않는 **Local-first, Consent-first** 구조를 원칙으로 합니다.

> 기획·법률 검토·아키텍처의 기준 문서는 [docs/Persona_AI_상용화_검토보고서.md](docs/Persona_AI_상용화_검토보고서.md)입니다.

## 핵심 원칙

- **로컬 처리**: 파싱, 개인정보 탐지, 분석, 임베딩, 검색, 생성 모두 기기 안에서 수행. 네트워크는 모델 다운로드·업데이트에만 사용(`TalkPro.Llm`에 한정, 아키텍처 테스트로 강제)
- **본인 중심**: 본인 발화만 모델링. 상대방 데이터는 맥락으로만 사용하고, 상대 말투 모방(말투 AI)은 동의 확인 후에만
- **개인정보 보호**: 주민·카드·계좌번호 등 자동 마스킹, 민감 주제 기본 제외, 데이터셋별 암호화(AES-256-GCM, DPAPI/Android Keystore), 삭제 시 키 파기(crypto-shredding)
- **AI 표시**: 생성 결과에 항상 "AI 생성" 표시, 자동 발송 기능 없음

## 구성

| 앱 | 위치 | 설명 | 상태 |
|---|---|---|---|
| Windows 앱 | `src/TalkPro.App` (WPF) | 가져오기 → 화자 검토 → 개인정보 검토 → 암호화 저장, 분석, 답장 초안, 기억 검색, 로컬 AI(Ollama) | MVP1 핵심 기능 완료 |
| Android PoC | `src/TalkPro.Mobile` (.NET MAUI) | Windows 앱과 같은 화면 + 기기 안 AI(Phi-4-mini, ONNX Runtime GenAI) | PoC |
| iPhone 웹 뷰어 | `src/TalkPro.Web` (Blazor WASM) | 브라우저 안에서 분석·키워드 검색만 (업로드·저장 없음) | 테스트용 |
| 말투 AI | `apps/mimic` (Vite + Capacitor) | 동의한 상대의 말투를 학습한 GGUF 모델과 대화 (웹/PWA, Android APK) | 진행 중 |
| 학습 서버 | `training/server` (Docker) | PC GPU로 LoRA 파인튜닝 → GGUF, QR 연결, PC에서 답장 | 진행 중 |

자세한 진행 현황은 [docs/BACKLOG.md](docs/BACKLOG.md)를 참고하세요.

## 저장소 구조

```
src/
  TalkPro.Core            도메인 모델·계약 (플랫폼 독립)
  TalkPro.Ingestion       카톡 내보내기 파서, 화자 검토
  TalkPro.Privacy         PII 탐지(L1~L4), 용도별 마스킹
  TalkPro.Security        키 계층, Vault 암호화, 데이터 삭제
  TalkPro.Infrastructure  암호화 SQLite(FTS5) 데이터셋 저장소
  TalkPro.Persona         대화 분석, 말투 프로필, 답장 초안
  TalkPro.Memory          세션 분할, 임베딩 색인, 하이브리드 검색
  TalkPro.Llm             Ollama / ONNX GenAI 클라이언트, 모델 허용목록
  TalkPro.Consent         동의 관리 (예정)
  TalkPro.Presentation    공용 ViewModel
  TalkPro.App / Mobile / Web   Windows · Android · 웹 UI
tests/                    단위·통합·아키텍처·보안 테스트
tools/                    SampleGenerator, FormatProbe, OnDeviceSmoke, WebViewerHost
apps/mimic/               말투 AI 웹·Android 앱
training/                 데이터 전처리, Colab/Unsloth 노트북, Docker 학습 서버
docs/                     설계·테스트·운영 문서
```

## 빠른 시작

### Windows 앱
요구 사항: Windows 11, .NET SDK 10.0.401 이상(`global.json`), Visual Studio 2026 / 17.14+ 권장. AI 기능은 [Ollama](https://ollama.com)와 `qwen3:8b`, `bge-m3` 모델이 필요합니다.

```powershell
dotnet build TalkPro.sln
dotnet test
```

Visual Studio에서는 **TalkPro.App**을 시작 프로젝트로 지정 후 실행합니다. 수동 확인 절차는 [docs/MANUAL_UI_TEST.md](docs/MANUAL_UI_TEST.md).
`TalkPro.Mobile`은 .NET MAUI Android 워크로드가 설치되어 있어야 빌드됩니다.

### 말투 AI (웹/Android)
```bash
cd apps/mimic
npm ci
npm run dev
```
배포판: https://jshaa.github.io/Talk_AI/ · 휴대폰 테스트는 [docs/MIMIC_TEST.md](docs/MIMIC_TEST.md), PC 학습 연동은 [docs/PC_PHONE_QUICKSTART.md](docs/PC_PHONE_QUICKSTART.md).

## 문서

| 문서 | 내용 |
|---|---|
| [Persona_AI_상용화_검토보고서](docs/Persona_AI_상용화_검토보고서.md) | 법적 리스크, 아키텍처, BM·가격, 경쟁, 로드맵 (기준 문서) |
| [DESIGN_DECISIONS](docs/DESIGN_DECISIONS.md) | 설계 결정(DD-001~DD-030)과 미해결 이슈 |
| [BACKLOG](docs/BACKLOG.md) | Phase별 작업 항목과 상태 |
| [DEPENDENCIES](docs/DEPENDENCIES.md) | 패키지·모델 라이선스 등록부 |
| [MOBILE_PLATFORM_OPTIONS](docs/MOBILE_PLATFORM_OPTIONS.md) | 모바일 전환 검토와 결정(DD-014) |
| [MOBILE_TEST](docs/MOBILE_TEST.md) | Android APK · iPhone 웹 뷰어 테스트 |
| [MIMIC_TEST](docs/MIMIC_TEST.md) | 말투 AI 휴대폰 테스트·파인튜닝 |
| [PC_PHONE_QUICKSTART](docs/PC_PHONE_QUICKSTART.md) | PC 학습 서버 → 휴대폰 사용 빠른 시작 |
| [REAL_FORMAT_PROTOCOL](docs/REAL_FORMAT_PROTOCOL.md) | 실제 카톡 내보내기 형식 수집 절차 |
| [SYNTHETIC_FORMATS](docs/SYNTHETIC_FORMATS.md) | 테스트용 합성 형식(W1/A1/I1)과 인코딩 정책 |
| [SECURITY_TESTS](docs/SECURITY_TESTS.md) | 자동·수동 보안 테스트 범위 |
| [MANUAL_UI_TEST](docs/MANUAL_UI_TEST.md) | Debug 빌드 수동 UI 점검 |

## 지원 형식

- 카카오톡 PC(Windows/Mac) 내보내기: 지원 (`kakao.pc.ko.v1`)
- 말투 AI(`apps/mimic`): PC·Android·iOS·Mac CSV 내보내기 지원
- Windows 앱의 Android·iPhone 내보내기: 미지원 — [REAL_FORMAT_PROTOCOL](docs/REAL_FORMAT_PROTOCOL.md) 절차로 샘플 보고서 수집 중
