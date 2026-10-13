# 말투 AI 휴대폰 테스트 안내 (Android 앱 · iPhone/Android 웹) — DD-028

카카오톡 대화에서 **상대방 한 사람의 말투**를 배워 흉내 내는 AI입니다. 모델은 **휴대폰 안에서만** 돌고, 대화 내용은 어디에도 보내지 않습니다.

> 따라할 사람에게 **동의**를 받은 대화만 쓰세요. 생성된 문장을 그 사람이 보낸 것처럼 다른 사람에게 보내면 안 됩니다(메신저 피싱 악용 방지). 앱은 돈·계좌·인증번호·"폰 고장" 같은 답장을 표시하지 않습니다.

## 0. 한눈에 보기

```
카톡 내보내기(.txt) → 앱 ① 탭: 따라할 사람 선택·동의 → 학습 데이터(JSONL)
   → Kaggle/Colab 무료 T4: training/train_persona_unsloth.ipynb (약 20~40분)
   → persona-q4_k_m.gguf → 휴대폰으로 복사 → 앱 ② 탭 "이 기기의 GGUF 선택" → ③ 대화하기
```

학습 없이 먼저 체험: ② 탭에서 **"기본 모델 + 실제 예시"** → 프리셋 Qwen2.5-1.5B 내려받기(약 1.1GB, Wi-Fi) → ③ 대화하기.

## 1. 웹 (iPhone Safari · Android Chrome 공용)

- 주소: **https://jshaa.github.io/Talk_AI/** (master에 반영 후 1회 설정: GitHub 저장소 *Settings → Pages → Source: GitHub Actions*)
- iPhone: Safari로 열고 공유 → **홈 화면에 추가**하면 앱처럼 실행됩니다. 처음 한 번 자동 새로고침(멀티스레드 활성화)이 있습니다.
- 모델 파일 넣기: 파일 앱(iCloud 아님 권장)에 `.gguf`를 저장 → ② 탭 "이 기기의 GGUF 선택". 나눠진 파일(`-00001-of-0000N.gguf`)은 **모두 함께** 선택.
- 권장 모델: iPhone은 **1.5B 이하**(Safari 탭 메모리 한계). 0.5B는 가볍지만 말투 품질이 낮습니다.
- PC에서 직접 띄우기(개발용): `cd apps/mimic && npm ci && npm run dev` → `http://localhost:5173`. 같은 Wi-Fi 휴대폰에서 `http://192.168.x.x`로 열면 보안 컨텍스트가 아니어서 싱글스레드(느림)로만 동작하니, 휴대폰 테스트는 위 HTTPS 주소를 쓰세요.

## 2. Android 앱 (APK)

- 받기: GitHub *Actions → mimic* 최신 실행 → Artifacts **maltu-ai-debug-apk** → 압축 해제 → `app-debug.apk`를 휴대폰에 설치("출처를 알 수 없는 앱" 1회 허용).
- 직접 빌드(Android Studio 또는 SDK 필요): `cd apps/mimic && npm ci && npm run android:apk` → `android/app/build/outputs/apk/debug/app-debug.apk`
- 요구: Android 7+(최신 Android System WebView), RAM 6GB 이상 권장(3B 모델은 8GB 이상).
- 앱 데이터는 클라우드 백업·기기 이전에서 제외됩니다. 인터넷은 모델을 내려받을 때만 씁니다.

## 3. 파인튜닝 (무료 GPU)

1. 앱 ① 탭: 파일 선택 → 따라할 사람 선택 → 이름(별명) 입력 → 동의 2개 체크 → **말투 분석** → **학습 데이터 내려받기**.
   - PC라면: `python training/prepare_kakao.py 내보내기.txt --list` → `python training/prepare_kakao.py 내보내기.txt --target "카톡 이름" --name "별명"`
   - 답장 **1,000개 이상**이면 효과가 좋습니다(300개 미만은 약함).
2. Kaggle: *Create → New Notebook → File → Import Notebook* 로 `training/train_persona_unsloth.ipynb` 업로드 → *Settings*: Accelerator **GPU T4 x1**, Internet **On** → *Add Data*: `persona_train.jsonl`을 **Private** 데이터셋으로 추가 → *Run All*.
   - Colab: 노트북 열기 → 런타임 유형 **T4 GPU** → 왼쪽 파일 패널에 JSONL 업로드 → 모두 실행.
3. 끝나면 `export/persona-q4_k_m.gguf`(또는 나눠진 파일들)를 내려받아 휴대폰으로 옮깁니다.
4. 앱 ② 탭: **파인튜닝한 내 모델** 선택, **따라할 이름**을 1단계 별명과 똑같이 입력(시스템 프롬프트가 학습 때와 같아야 함) → GGUF 선택 → ③ 대화하기.
5. 개인정보: Kaggle/Colab에 올린 데이터는 그 서비스로 전송됩니다. 끝나면 데이터셋·출력물을 삭제하고, 모델 파일은 공개 저장소에 올리지 마세요.

| 기본 모델 (`BASE_MODEL`) | GGUF 크기 | 권장 기기 | 라이선스 |
|---|---|---|---|
| unsloth/Qwen2.5-0.5B-Instruct | 약 0.4GB | 저사양·빠른 확인 | Apache-2.0 |
| **unsloth/Qwen2.5-1.5B-Instruct** (기본) | 약 1.1GB | iPhone 웹, Android | Apache-2.0 |
| unsloth/Qwen2.5-3B-Instruct | 약 2GB | Android 앱(RAM 8GB↑) | Qwen Research(**비상업**) |

## 4. 확인할 점 (실기기 체크)

- ② 탭 상태 줄: `멀티스레드`인지(싱글스레드면 매우 느림), 첫 답장까지 걸린 시간, 앱이 메모리 부족으로 꺼지는지
- 블라인드 비교: 실제 답장과 AI 답장을 섞어 보여 줬을 때 구분이 되는지
- 돈·계좌·인증 관련 질문에 답장이 차단 문구로 바뀌는지

## 5. 알려진 제한

- 메시지 본문 속 **사람 이름·주소**는 자동으로 가리지 못합니다(내려받은 JSONL을 확인하세요).
- WebView/브라우저 실행이라 네이티브 llama.cpp보다 느립니다(1.5B 기준 대략 수 tok/s, 기기마다 다름 — 실측 필요, BACKLOG P.5).
- iOS 네이티브 앱은 Mac이 없어 만들지 않았습니다(DD-014). iPhone은 웹(홈 화면 추가)으로 씁니다.
