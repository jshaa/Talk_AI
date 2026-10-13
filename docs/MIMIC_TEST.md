# 말투 AI 휴대폰 테스트 안내 (Android 앱 · iPhone/Android 웹) — DD-028

카카오톡 대화에서 **상대방 한 사람의 말투**를 배워 흉내 내는 AI입니다. 모델은 **휴대폰 안에서만** 돌고, 대화 내용은 어디에도 보내지 않습니다.

> 따라할 사람에게 **동의**를 받은 대화만 쓰세요. 생성된 문장을 그 사람이 보낸 것처럼 다른 사람에게 보내면 안 됩니다(메신저 피싱 악용 방지). 앱은 돈·계좌·인증번호·"폰 고장" 같은 답장을 표시하지 않습니다.

## 0. 한눈에 보기

```
카톡 내보내기(.txt) → 앱 ① 탭: 따라할 사람 선택·동의 → 말투 분석
   → ② 탭 "학습 시작" (내 PC의 Docker 학습 서버가 학습) → 모델 자동 수신·불러오기 → ③ 대화하기
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

## 3. 파인튜닝 — 내 PC로 버튼 한 번에 (권장)

PC에서 **Docker Desktop**으로 학습 서버를 켜 두면, 휴대폰 앱의 **"학습 시작"** 버튼으로 학습하고 끝나면 모델이 휴대폰으로 자동으로 받아져 바로 불러와집니다.

**PC 준비 (한 번만)**
1. [Docker Desktop](https://www.docker.com/products/docker-desktop/) 설치 (Windows는 WSL2 사용 설정).
2. NVIDIA 그래픽카드가 있으면 최신 NVIDIA 드라이버를 설치합니다(WSL2 GPU 지원 포함). 확인: PowerShell에서 `docker run --rm --gpus all ubuntu nvidia-smi`
3. 저장소를 받고 서버를 켭니다(처음 빌드는 10~20분, 이미지 약 10GB):
   ```powershell
   git clone https://github.com/jshaa/Talk_AI.git
   cd Talk_AI\training\server
   docker compose up -d --build                                     # NVIDIA GPU
   docker compose -f compose.yaml -f compose.cpu.yaml up -d --build # GPU 없음(느림, 0.5B만 권장)
   ```
4. PC 브라우저로 **http://localhost:8001** 을 열면 QR 코드가 나옵니다.

**휴대폰에서**
1. PC 화면의 **QR을 휴대폰 카메라로 찍기** → 말투 AI가 열리며 서버 주소·토큰이 자동 입력됩니다.
2. ① 탭: 대화 파일 → 따라할 사람 → 이름 → 동의 2개 → **말투 분석**
3. ② 탭 "내 PC로 학습": 기본 모델 선택 → **학습 시작** → 진행률 표시(모델 첫 다운로드 → 학습 → GGUF 변환)
4. 끝나면 모델을 자동으로 받아 **파인튜닝 모드로 불러옵니다** → ③ 대화하기

| 기본 모델 | NVIDIA GPU (VRAM) | 예상 시간 (답장 2천 개) | 휴대폰 |
|---|---|---|---|
| Qwen2.5-0.5B | 4GB↑ 또는 CPU | GPU 수 분 / CPU 1시간 이상 | 모두 |
| **Qwen2.5-1.5B** (권장) | 6GB↑ | 10~20분 | iPhone 웹, Android |
| Qwen2.5-3B (비상업) | 8GB↑ | 20~40분 | Android 앱 |

(시간은 추정치이며 PC 성능에 따라 다릅니다.)

**연결 방식과 개인정보**
- 기본은 `tunnel` 서비스(Cloudflare 무료 빠른 터널)가 `https://….trycloudflare.com` 주소를 만들어, 집 밖에서도·HTTPS 웹앱에서도 연결됩니다. 내용은 TLS로 암호화되지만 Cloudflare를 거칩니다. 주소는 서버를 다시 켤 때마다 바뀌니 QR을 다시 찍으세요.
- 같은 Wi-Fi에서만 쓰려면 `docker compose up -d trainer` 로 터널 없이 켜고, 휴대폰에서 **http://(PC IP):8000** 을 직접 엽니다(PC가 앱도 함께 제공). 이 방식은 브라우저 제약으로 휴대폰 추론이 싱글스레드라 느립니다. Windows 방화벽이 물으면 "개인 네트워크"만 허용하세요.
- 토큰(학습 서버 비밀번호)은 PC의 http://localhost:8001 에서만 보입니다.
- 올린 대화는 학습이 읽자마자 PC에서도 지워지고, PC에는 **가장 최근 모델 1개만** 남습니다(Docker 볼륨 `trainer-data`). 완전히 지우려면 `docker compose down -v`.
- 서버 끄기: `docker compose down` · 로그: `docker compose logs -f trainer`

### 3-1. (대안) Kaggle/Colab 무료 GPU

PC에 GPU가 없고 빠르게 하고 싶다면: ① 탭 **학습 데이터 내려받기** → https://colab.research.google.com/github/jshaa/Talk_AI/blob/master/training/train_persona_unsloth.ipynb → 런타임 **T4 GPU** → JSONL 업로드 → 모두 실행 → `persona-q4_k_m.gguf`를 휴대폰으로 옮겨 ② 탭 **"이 기기의 GGUF 선택"** (따라할 이름은 학습 때와 같게). 이 경우 대화가 Google 서버로 전송됩니다.

## 4. 확인할 점 (실기기 체크)

- ② 탭 상태 줄: `멀티스레드`인지(싱글스레드면 매우 느림), 첫 답장까지 걸린 시간, 앱이 메모리 부족으로 꺼지는지
- 블라인드 비교: 실제 답장과 AI 답장을 섞어 보여 줬을 때 구분이 되는지
- 돈·계좌·인증 관련 질문에 답장이 차단 문구로 바뀌는지

## 5. 알려진 제한

- 메시지 본문 속 **사람 이름·주소**는 자동으로 가리지 못합니다(내려받은 JSONL을 확인하세요).
- WebView/브라우저 실행이라 네이티브 llama.cpp보다 느립니다(1.5B 기준 대략 수 tok/s, 기기마다 다름 — 실측 필요, BACKLOG P.5).
- iOS 네이티브 앱은 Mac이 없어 만들지 않았습니다(DD-014). iPhone은 웹(홈 화면 추가)으로 씁니다.
