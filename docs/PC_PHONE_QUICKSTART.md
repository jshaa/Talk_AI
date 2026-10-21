# PC에서 학습 → 휴대폰(iPhone·Android 웹)에서 쓰기 — 빠른 시작

Windows + Docker Desktop + NVIDIA GPU(8GB, 예: RTX 3070 Laptop) 기준 요약입니다. 자세한 설명은 [MIMIC_TEST.md](MIMIC_TEST.md).

```
휴대폰 웹앱(PC가 HTTPS 터널로 직접 제공: https://….trycloudflare.com — Wi-Fi·LTE 어디서나)
  ① 카톡 대화 .txt → 따라할 사람·동의 → 말투 분석
  ② "학습 시작" ──(JSONL, HTTPS 터널)──▶ PC Docker 학습 서버(GPU LoRA → GGUF)
  ② 끝나면 GGUF 자동 다운로드·불러오기 ◀── PC
  ③ 대화하기 (모델은 휴대폰 브라우저 안에서만 실행)
```

## 1. PC: 학습 서버 켜기 (처음 한 번 빌드 10~20분, 이미지 약 10GB)

Docker Desktop이 켜져 있어야 합니다(작업 표시줄 고래 아이콘이 "running").

```powershell
git clone https://github.com/jshaa/Talk_AI.git
cd Talk_AI\training\server
docker compose up -d --build
```

- PC 브라우저로 **http://localhost:8001** → QR 코드가 보이면 준비 완료(터널 주소를 받는 데 10초쯤 걸릴 수 있음).
- 로그: `docker compose logs -f trainer` · 끄기: `docker compose down` · 학습 데이터·모델까지 삭제: `docker compose down -v`
- 학습 코드(`persona_train.py`)를 고쳤다면 `docker compose up -d --build` 로 다시 빌드해야 반영됩니다.

### Docker Desktop이 켜지자마자 꺼질 때

`%LOCALAPPDATA%\Docker\log\host\com.docker.backend.exe.log` 에 `initializing Secrets Engine ... engine.sock ... cannot be accessed` 가 있으면, 예전 실행에서 남은 소켓 파일 때문입니다. Docker Desktop을 완전히 끈 뒤 PowerShell에서:

```powershell
Rename-Item "$env:LOCALAPPDATA\docker-secrets-engine" docker-secrets-engine.old
```

(소켓 파일은 바로 지워지지 않아 폴더 이름을 바꿉니다.) 그다음 Docker Desktop을 다시 실행하세요(안 되면 PC 재시작). GPU 확인: `docker run --rm --gpus all ubuntu nvidia-smi`

## 2. 휴대폰: 연결하고 학습

1. PC에서 http://localhost:8001 을 열고 **"주소 복사"** → 카카오톡 나에게 보내기·메일 등으로 휴대폰에 보내서 엽니다(QR을 찍어도 같음). PC가 터널로 제공하는 말투 AI 웹앱이 열리고 서버 주소·토큰이 자동 입력됩니다. 같은 Wi-Fi가 아니어도(LTE·다른 곳) 됩니다.
   - PowerShell로 바로 복사: `(iwr http://localhost:8001/link).Content | Set-Clipboard`
   - 이 주소에는 학습 서버 토큰이 들어 있습니다. 믿을 수 있는 사람에게만 보내세요.
   - 카카오톡 안에서 열리면 메뉴에서 **Safari/Chrome으로 열기**를 고르세요(카톡 내장 브라우저는 모델 저장·멀티스레드가 제한될 수 있음).
   - iPhone: Safari에서 열기 → 공유 → **홈 화면에 추가**하면 앱처럼 쓸 수 있습니다.
   - Android: Chrome에서 열기 → 메뉴 → **홈 화면에 추가**.
   - ② 탭 상태 줄에 `멀티스레드`가 보여야 빠릅니다.
2. ① 탭: 카카오톡 대화 내보내기 파일 → 따라할 사람 → 동의 2개 → **말투 분석**
3. ② 탭 "내 PC로 학습": 기본 모델 고르기 → **학습 시작** → 진행률(기본 모델 첫 다운로드 → 학습 → GGUF 변환)
4. 끝나면 휴대폰이 모델을 자동으로 받아 불러옵니다 → ③ 대화하기

PC에 이미 학습된 모델이 있으면(아래 2-1) 앱을 열 때 ② 탭에 **"학습된 모델 불러오기"** 버튼이 나옵니다. 누르면 받아서 바로 씁니다(1.5B 기준 약 1.3GB, Wi-Fi 권장).

### 2-1. PC에서 파일로 바로 학습 (휴대폰 없이 시작)

```powershell
cd Talk_AI
python training\prepare_kakao.py 대화.txt --list
python training\server\submit.py 대화.txt --target "따라할 이름" --exclude 플레이봇 채팅봇
```

- 첫 줄은 대화 참여자 이름 확인용입니다.
- `--exclude`: 봇 같은 다른 참여자를 빼서 1:1 대화로 학습합니다. 빼지 않으면 단체방으로 보고 상대 말 앞에 `참여자1:`이 붙어, 휴대폰에서 직접 입력할 때와 형식이 달라집니다.
- `--base`: 기본 `qwen2.5-1.5b`. Android 전용으로 맥락을 더 잘 이해하는 모델을 원하면 `qwen3-4b`.
- PC에는 가장 최근 작업 1개만 남습니다. 새로 학습하면 이전 모델은 지워집니다.

### 주소에 대해

- 터널 주소는 터널을 다시 켤 때마다 바뀝니다(`docker compose down` 후 `up`, PC 재시작 등). 그때는 http://localhost:8001 에서 새 연결 주소를 복사해 다시 보내세요. 주소가 바뀌면 휴대폰은 새 사이트로 보므로 모델을 다시 받습니다. 주소가 같으면 브라우저 캐시를 써서 다시 받지 않습니다.
- 같은 Wi-Fi에서는 `http://(PC IP):8000`으로도 열리지만, https가 아니라 싱글스레드라 느립니다.
- GitHub Pages 판(https://jshaa.github.io/Talk_AI/)을 쓰려면 `APP_URL` 환경변수를 그 주소로 두고 서버를 켜세요. PC에서 고친 앱 기능은 GitHub에 올려 배포해야 반영됩니다.

### 2-2. PC에서 답장 (권장: Qwen3-4B · PC)

PC가 모델을 돌려 답장을 만들고, 휴대폰은 연결 주소로 **대화만** 합니다. 휴대폰 메모리 한계가 없어 더 긴 대화를 보고 답하고, iPhone도 됩니다.

```powershell
python training\server\submit.py 대화.txt --target "따라할 이름" --exclude 플레이봇 채팅봇 --base qwen3-4b-pc
```

- 결과는 휴대폰용 파일이 아니라 PC에 남는 작은 어댑터입니다. 끝나면 휴대폰에서 연결 주소를 열기만 하면 ② 탭이 자동으로 **PC에서 답장**으로 바뀝니다. 내려받을 것이 없습니다.
- 대화 흐름을 잘 따라가도록 이렇게 학습·답장합니다.
  - 학습 샘플을 최대 24턴·3,000자로 길게 만들고, 답장할 때도 최근 약 3,000자를 봅니다.
  - "어어엉", "ㅋㅋㅋ"처럼 맞장구만 있는 답은 10번 중 3번만 학습하고 나머지는 맥락으로만 씁니다(모든 말에 맞장구만 치는 습관 방지).
  - 답장 후보를 3개 만든 뒤, 같은 말 반복·상대 말 따라 하기·질문에 맞장구만 하는 답을 피해서 고릅니다(온도 0.8).
- 학습할 때는 답장마다 `[2줄 질문]` 같은 숨은 표시(몇 줄인지, 질문이 있는지)를 붙여 보여 주고, 답장할 때는 PC가 표시를 골라(주로 1~4줄, 한동안 질문이 없었으면 질문할 확률을 올림) 짧게 답하고 가끔 되묻게 합니다.
- 첫 메시지는 PC가 모델을 GPU에 올리느라 1분쯤 걸릴 수 있습니다("불러오는 중" 안내가 나오면 잠시 뒤 다시 보내세요).
- PC와 Docker 서버가 켜져 있어야 하고, 대화 내용(최근 약 3,000자)이 내 PC로 전송됩니다. PC는 대화를 저장하지 않습니다.
- PC가 새 학습을 하는 동안에는 PC 답장을 쓸 수 없습니다(GPU를 학습에 씀).

### 2-3. 더 큰 모델: Colab에서 Qwen3-8B 학습 → PC에서 답장

8GB GPU로는 8B 모델을 **학습**할 수 없지만, llama.cpp로 **답장**은 됩니다(4비트 약 5.4GB, RTX 3070 Laptop에서 초당 약 45토큰). 그래서 학습만 무료 Google Colab(T4 16GB)에서 합니다.

1. [training/train_pc_8b_colab.ipynb](../training/train_pc_8b_colab.ipynb)를 Colab에서 열고(GitHub에서 열기 또는 파일 업로드) **런타임 → T4 GPU**.
2. 1번 셀에 `TARGET`(따라할 이름)·`EXCLUDE`(봇 등)를 넣고 위에서부터 실행 → 카톡 내보내기 파일을 올립니다. 학습 1.5~2.5시간.
3. 마지막에 받은 `persona-8b-….zip`(약 180MB)을 PC에서 **http://localhost:8001** → **"Colab에서 학습한 8B 모델 가져오기"**로 고릅니다.
   - PC가 어댑터를 llama.cpp 형식으로 바꾸고, 처음 한 번 8B 기본 모델(약 5GB)을 받은 뒤 `llm` 서비스가 GPU에 올립니다. "준비됐습니다"가 나오면 끝.
4. 휴대폰에서 연결 주소를 열면 자동으로 **PC에서 답장**이 됩니다(Qwen3-4B · PC와 같은 방식: 긴 맥락, 숨은 표시, 후보 3개 중 고르기).

- 가져오면 PC에 있던 이전 모델은 지워지고, 반대로 PC에서 새로 학습을 시작하면 8B 답장은 멈춥니다(GPU를 학습에 씀). PC에는 언제나 최근 모델 하나만 있습니다.
- `llm` 서비스는 llama.cpp 공식 CUDA 이미지(`ghcr.io/ggml-org/llama.cpp:server-cuda`, 버전 고정)로 `docker compose up -d`에 함께 켜지고, 가져온 모델이 없으면 아무것도 하지 않습니다.
- PR이 master에 합쳐지기 전이면 노트북 1번 셀의 `REPO_REF`를 `"pc-reply-trainer"`로 바꾸세요.

## 3. VRAM 8GB PC에서 고를 모델

| 기본 모델 | 8GB GPU 학습 | 쓸 휴대폰(웹) | 비고 |
|---|---|---|---|
| **Qwen3-8B · PC에서 답장** | ❌ (Colab에서 학습) | 모두(대화만) | 대화 이해가 가장 좋음. 2-3 참고 |
| **Qwen3-4B · PC에서 답장** (권장) | ✅ | 모두(대화만) | 가장 긴 맥락. PC가 켜져 있어야 함 |
| **Qwen2.5-1.5B** (기본) | ✅ 여유 | iPhone·Android | 처음엔 이걸로. 약 1.3GB |
| Qwen3-4B-Instruct-2507 | ⚠️ 8GB 경계 | Android(RAM 8GB↑) | 맥락 이해가 가장 좋음. 약 2.5GB. 메모리 부족이면 1.5B로 |
| Qwen2.5-0.5B | ✅ | 모두 | 빠르지만 맥락·말투 품질 낮음 |

iPhone Safari는 탭 메모리 한계 때문에 1.5B 이하만 권장합니다.

**디스크 공간:** 4B 학습은 마지막 병합 단계에서 16비트 원본(약 8GB)을 받고 병합본(약 8GB)을 써서 Docker 디스크에 20GB 이상 여유가 필요합니다. C:에 여유가 적으면 빌드 전에 Docker Desktop → Settings → Resources → Advanced → **Disk image location**을 다른 드라이브(예: `D:\DockerData`)로 바꾸세요. C:가 가득 차면 Docker가 멈추고 학습이 병합 단계에서 실패합니다.

## 4. 대화 흐름(맥락)에 맞게 답하도록 학습되는 방식

- 대화를 3시간 이상 끊긴 곳에서 "세션"으로 나누고, 상대가 연달아 보낸 메시지는 한 턴으로 합칩니다.
- 한 학습 샘플 = 최대 12턴(1,500자)의 연속 대화. **따라할 사람의 답장만** 정답으로 학습하고, 앞 대화는 맥락으로 씁니다.
- 긴 세션은 앞 4턴을 겹쳐서 잘라, 모든 답장이 앞 대화와 함께 학습됩니다. 겹친 답장은 다음 샘플에서 **맥락으로만** 쓰여 같은 문장을 두 번 외우지 않습니다.
- 토큰 한도를 넘는 샘플은 버리지 않고 오래된 주고받음부터 잘라 최근 맥락을 남깁니다.
- 휴대폰에서는 최근 대화(약 1,200자)를 함께 넣어 답장을 만듭니다. 주제를 바꾸려면 ③ 탭에서 대화를 지우세요.

품질 팁: 답장 1,000개 이상, 같은 사람과의 대화 여러 개, 가능하면 더 큰 모델. 학습 문장을 그대로 따라 하면 에폭을 1로 줄여 다시 학습하세요.

## 5. 개인정보

- 대화 JSONL은 휴대폰 → 내 PC로만 갑니다(Cloudflare 터널은 TLS로 암호화된 통로). PC는 학습이 읽자마자 지우고, 가장 최근 모델 1개만 남깁니다.
- 토큰(서버 비밀번호)은 PC의 http://localhost:8001 에서만 보입니다. QR을 다른 사람에게 보여 주지 마세요.
- 따라할 사람의 동의를 받은 대화만 쓰고, 생성된 문장을 그 사람이 보낸 것처럼 쓰지 마세요.
