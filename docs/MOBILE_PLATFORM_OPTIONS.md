# 모바일 앱 전환 검토 (DD-014 후보)

작성일: 2026-10-06 (같은 날 외부 검토 반영 개정) · 상태: **결정됨 — DD-014 (2026-10-06)**

## 1. 현재 상태
- 제품: Windows 데스크톱(WPF) Local-first 앱. 보고서 11장·13장·23장의 결정이 기준입니다.
- 코드 이식성:

| 영역 | 현재 대상 | 모바일 재사용 |
|---|---|---|
| Core, Ingestion, Privacy, Memory, Persona, Consent, Llm 추상화 | `net10.0` (플랫폼 무관) | 그대로 가능 |
| App (WPF) | `net10.0-windows` | 불가. 현재 Phase 0 껍데기뿐이라 교체 비용이 가장 낮은 시점 |
| Infrastructure (로그·경로 정책) | Windows 경로 기준 | 경로 정책만 플랫폼별로 분리 |
| Security (DPAPI, Phase 2 예정) | Windows 전용 | 아직 미구현이므로 처음부터 `IKeyProtector`로 추상화 |

## 2. 장점
| # | 장점 | 설명 |
|---|---|---|
| 1 | 데이터가 원래 휴대폰에 있음 | 카톡 내보내기를 휴대폰에서 바로 Share Intent(공유 시트)로 앱에 넘길 수 있습니다. PC로 옮기는 단계가 사라집니다. |
| 2 | "내 말투 답장 초안"의 쓸모가 큼 | 실제 답장을 휴대폰에서 쓰므로 초안을 복사해 붙여넣기까지가 짧습니다. |
| 3 | OS 보안 기본기 | 앱 샌드박스, 하드웨어 기반 키 저장소(Android Keystore, iOS Keychain/Secure Enclave), 기본 저장 암호화를 쓸 수 있습니다. |
| 4 | 시장 접근성 | 국내 메신저 사용은 모바일 중심입니다. |
| 5 | 기기 내장 무료 온디바이스 AI 선택지 | iOS 26 Foundation Models(약 3B, Apple Intelligence 지원 기기). Android ML Kit GenAI Prompt API(Gemini Nano)는 **2026-04 Beta 승격**, 단 Structured Output은 Alpha. 모델 파일 다운로드가 필요 없습니다. |
| 6 | 공용 라이브러리 재사용 | 파서·프라이버시·기억·페르소나 로직은 C# 라이브러리 그대로 씁니다. |

## 3. 단점과 위험
| # | 단점 | 영향 | 심각도 |
|---|---|---|---|
| 1 | **iOS 개발 환경과 배포 비용** | **개발과 개인 기기 테스트는 무료 Apple 계정으로 가능**합니다(서명 유효기간 등 제한 있음). 다만 .NET MAUI로 Windows에서 iOS를 빌드·서명하려면 **네트워크상의 Mac 빌드 호스트 + Xcode**가 필요합니다(VS 2026은 Hot Restart 미지원). **App Store·TestFlight 정식 배포는 Apple Developer Program(연 $99)**이 필요하므로, 비용 허용 여부는 배포 단계에서 결정합니다. | 중 |
| 2 | Android 배포 비용 | 에뮬레이터·USB 기기 개발과 테스트는 무료입니다. **Google Play 정식 배포에는 개발자 계정 일회성 $25**가 필요합니다. "모바일 배포까지 완전 무료"는 아닙니다. | 하 |
| 3 | 로컬 LLM 성능·품질 | **PoC 목표로는 1~4B급(양자화)이 현실적**입니다. 상위 기종에서는 더 큰 양자화 모델도 돌 수 있지만 RAM·속도·발열 차이가 큽니다. PC(8~30B) 대비 한국어 말투 재현 품질이 떨어질 수 있습니다. | 상 |
| 4 | C# ↔ 모바일 AI 런타임 연결 | LLamaSharp에 Android 지원이 추가됐지만, **.NET MAUI용 공식 통합 경로라고 보기는 어렵습니다**(MAUI 임베딩 이슈가 별도 지원 없이 종료된 사례 있음). Apple Foundation Models는 **Swift 네이티브 API**, ML Kit GenAI는 **Android 네이티브 API** 중심이라 C#에서 바로 편하게 호출할 수 없습니다. 플랫폼별 네이티브 런타임을 C# 인터페이스 뒤에 두는 구조가 필요합니다(4장). | 상 |
| 5 | 기기 내장 AI의 제약 | 지원 단말이 특정 기종으로 한정되고, Apple Intelligence 활성화 같은 설정 상태에 종속됩니다. Gemini Nano Prompt API는 앱이 화면 맨 앞에 있어야 합니다. 모델 선택권이 없고 출력 정책은 플랫폼이 정합니다. 한국어는 Apple Intelligence 지원 언어에 포함됩니다. ML Kit 쪽은 **API별 지원 언어 확인 필요**(개요 문서에 목록 없음). | 중 |
| 6 | 대량 처리 | 수만~수십만 메시지 임베딩·분석에 시간과 배터리가 듭니다. 백그라운드 실행 제한(특히 iOS) 때문에 가져오기 중 앱을 켜 둬야 할 수 있습니다. | 중 |
| 7 | **클라우드 자동 백업 유출 경로** | Android는 기본적으로 앱 데이터 상당 부분이 **Auto Backup** 대상입니다. `data-extraction-rules`(Android 12+)·`fullBackupContent`·`noBackupFilesDir()`로 명시적으로 제외해야 하고, iOS는 파일별 백업 제외 속성을 지정해야 합니다. 빠뜨리면 Local-only 원칙 위반입니다. | 상 |
| 8 | 모델 파일 배포 | 1.5~3GB 모델은 스토어 기본 설치 용량에 넣기 어려워 설치 후 다운로드가 필요합니다(PC도 결국 같은 문제). 5장 결정 3 참고. | 중 |
| 9 | 스토어 심사·정책 | 생성형 AI·사칭 관련 심사, 연령 등급, 개인정보 라벨(App Privacy, Data safety)이 필요합니다. Self Persona 중심이면 위험이 낮습니다. | 중 |
| 10 | 인앱결제 수수료 | 유료 기능은 스토어 결제 수수료 때문에 가격 모델(보고서 15장)을 다시 계산해야 합니다. | 중 |
| 11 | 일부 기능 영역 겹침 | '카나나 인 카카오톡'이 온디바이스 AI로 대화 맥락(요약·분석 등)을 활용하고 있어 **일부 기능 영역이 겹칩니다**. 단순 "카톡 AI 비서"로 포지셔닝하면 겹치지만, **카톡 데이터 → 완전 로컬 분석 → 나만의 말투·기억 → 개인화 답장·Self Persona**로 특화하면 방향이 다릅니다. | 하~중 |
| 12 | 개발 부담 | 플랫폼별 UI·권한·키 저장소·테스트 기기 관리가 필요합니다. 소수 인원 일정이 약 1.5~2배로 늘어납니다(추정). | 중 |
| 13 | 키보드 연동 한계 | 키보드 확장은 메모리 한도가 작아 LLM을 직접 돌릴 수 없습니다(앱 전환 방식으로 우회). | 하 |

## 4. 핵심 설계 원칙: UI보다 AI Runtime 추상화가 먼저
기기 내장 AI와 llama.cpp 계열 런타임은 모두 플랫폼 네이티브 API입니다. 그래서 UI 프레임워크(MAUI/Avalonia)를 고르기 전에 **언어 모델 런타임을 Core 인터페이스 뒤로 격리**해야 합니다. 이 인터페이스는 마스터 프롬프트 §15의 `ILlmClient`입니다(리뷰 제안 명칭 `ILocalLanguageModel`과 같은 역할).

```
ChatExportParser · MemoryEngine · StyleProfiler · PersonaPromptBuilder · GroundingVerifier   (net10.0, 공용)
                                   │
                                   ▼
                     ILlmClient / IEmbeddingClient   (Core 추상화)
                                   │
        ┌──────────────────┬───────┴──────────┬────────────────────────┬─────────────────────────┐
  WindowsLlamaRuntime  AndroidLlamaRuntime  AndroidGeminiNanoRuntime  IosFoundationModelRuntime  (OllamaClient: 개발용)
  (LLamaSharp/llama.cpp) (llama.cpp 바인딩)   (ML Kit, Kotlin 바인딩)   (Swift 바인딩)
```
- 플랫폼 런타임 구현은 플랫폼별 프로젝트(예: `TalkPro.Llm.Android`)에 둡니다. Core·Persona·Memory는 런타임을 모릅니다.
- 런타임마다 능력이 다르므로 `LlmCapabilities`(최대 컨텍스트, JSON/구조화 출력 지원, 스트리밍)를 노출합니다. Grounding 파이프라인은 구조화 출력이 없는 런타임에서도 동작해야 합니다(검증기는 생성기와 분리, 7.5장).
- 이렇게 하면 UI를 MAUI ↔ Avalonia로 바꿔도 핵심 로직은 영향을 받지 않습니다.

## 5. 선택지와 권장안
| 안 | 내용 | 평가 |
|---|---|---|
| A | Windows 전용 유지 | 데이터를 PC로 옮겨야 하는 불편이 남습니다. |
| B | 모바일 전용으로 즉시 전환 | 엔진 검증 전에 제약이 많은 환경으로 가는 것이라 위험합니다. |
| **C** | **Windows 먼저 + Android 실기기 PoC** | **권장.** 가장 싸게 실제 가능성을 확인합니다. |
| D | 지금 바로 크로스플랫폼 UI로 교체 | C의 PoC 성공 후 채택합니다. |

### Android PoC — UI 이식 없이 4가지만 확인
| # | 확인 항목 | 합격 기준(초안) |
|---|---|---|
| 1 | 카카오톡 내보내기 → Android Share Intent → 파싱 성공 | 실기기 내보내기 파일 줄 단위 파싱 성공률 ≥ 99% (실제 포맷 어댑터는 Phase 1.8 선행) |
| 2 | 1~4B급 로컬 모델로 한국어 말투 답장 초안 3개 생성 | 블라인드 평가에서 "내 말투 같음" 응답 다수, 사칭·사실 생성 위반 0 |
| 3 | 실기기 성능 측정: RAM, 첫 토큰 시간, tokens/sec, **10분 연속 사용 시 발열·스로틀링** | 기준 단말 2종(중급·상급)에서 기록. 첫 토큰 ≤ 3초 목표 |
| 4 | DB·원문·임베딩·말투 프로필을 **클라우드 백업에서 완전히 제외** | 백업·복원 후 앱 데이터가 복원되지 않음을 실측 확인 |

PoC가 만족스러우면 UI를 옮깁니다. **UI 후보는 .NET MAUI 우선**입니다. 이 앱은 Share Intent, Android Keystore, 백업 정책, ML Kit 같은 모바일 OS 기능과 강하게 엮이므로, Avalonia의 UI 장점보다 MAUI의 네이티브 플랫폼 연동이 더 중요합니다. 둘 다 무료이고, Avalonia도 Android/iOS를 지원합니다.

## 6. 결정 항목 — 확정 (DD-014, 2026-10-06)
| # | 항목 | 결정 | 비고 |
|---|---|---|---|
| 1 | 모바일 진행 방식 | **Windows 먼저 + Android PoC** | 현재 구조를 깨지 않음 |
| 2 | iOS 비용·Mac 예외 | **지금은 허용하지 않음** | iOS 자체가 개발 불가능한 것은 아닙니다. 정식 배포 단계에서 비용 허용 여부를 결정합니다 |
| 3 | 모델 다운로드 네트워크 | **허용 권장. 단 개인정보 전송과 분리** | 허용: 모델 다운로드, 앱 업데이트, 모델 버전 확인(서명·SHA-256 허용 목록). 금지: 카톡 원문, 사용자 메시지, 말투 프로필, 임베딩·메모리 DB 전송. 구현은 `TalkPro.Llm`에 한정하고(아키텍처 테스트 유지), 요청 본문에 콘텐츠가 없음을 테스트로 검증 |
| 4 | "나 자신과 대화" | **Phase 6.5 — Self Persona Chat** (MVP1 핵심과 분리) | Phase 6 파이프라인(내 메시지 → 말투 특징 → 관련 발화 검색 → 프롬프트)에 기억 검색과 Grounding 라벨을 더한 활용 방식. 새 엔진이 아님 |

참고: 다른 사람 말투로 대화하는 기능은 **동의·개인정보·사칭 위험을 줄이기 위한 제품 정책상** MVP1에서 제외했습니다(법으로 일률 금지된다는 뜻이 아님). 보고서 3장·23장 참고.

## 출처
- Apple Developer Program 등록·비용: https://developer.apple.com/programs/
- .NET MAUI Pair to Mac(Mac 빌드 호스트 필요): https://learn.microsoft.com/en-us/dotnet/maui/ios/pair-to-mac · Hot Restart(VS 2026 미지원): https://learn.microsoft.com/en-us/dotnet/maui/ios/hot-restart · https://dev.to/biozal/hot-restart-for-ios-is-not-included-in-visual-studio-2026-what-net-maui-developers-need-to-know-28n8
- Google Play 개발자 계정 등록비: https://support.google.com/googleplay/android-developer/answer/6112435
- LLamaSharp releases(Android 지원): https://github.com/SciSharp/LLamaSharp/releases · MAUI 임베딩 이슈: https://github.com/SciSharp/LLamaSharp/issues/954
- ML Kit GenAI 개요(Prompt API Beta): https://developers.google.com/ml-kit/genai · 릴리스 노트(2026-04-02 Prompt API 1.0.0-beta2, 2026-07-14 Structured Output Alpha): https://developers.google.com/ml-kit/release-notes · Prompt API 문서: https://developers.google.com/ml-kit/genai/prompt/android
- Apple Foundation Models: https://www.apple.com/newsroom/2025/09/apples-foundation-models-framework-unlocks-new-intelligent-app-experiences/ · 지원 언어(한국어 포함): https://rudrank.com/exploring-foundation-models-supported-languages-internationalization
- Android 백업 제외: https://developer.android.com/identity/data/autobackup
- Avalonia 지원 플랫폼: https://docs.avaloniaui.net/
- 카나나 인 카카오톡(온디바이스): https://kanana.kakao.com/ · https://www.etnews.com/20260625000265
