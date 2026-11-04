# Day 0 결과 (2026-10-29)

- 범위: S-1 박자 시계, S-2 손 인식 + Unity 연결, S-3 빌드 크기, S-6 소리 샘플. (S-4 Windows PC, S-5 이미지는 사용자 담당이라 하지 않았다.)
- 원칙: **직접 잰 숫자만** 적었다. 원본 데이터(JSON/CSV)는 `measurements/`, 만든 스크립트는 `tools/`에 있다. 증거 경로는 저장소 기준이다.
- 이 문서는 기획서 v0.2(`02-기획서-WebGL.md`)의 §6.3, §10.2, §12.1을 기준으로 판정했다.

## 1. 한눈에 보기

| 시험 | 판정 | 핵심 숫자 | 증거 |
|---|---|---|---|
| **S-1 박자 시계** | **경로 2(브라우저 Web Audio) 권고.** 경로 1(Unity `PlayScheduled`)은 이 Mac에서 기준을 안정적으로 못 넘음 | Web Audio: 60초 6회 모두 표준편차·최대 오차 0.01 ms 미만, 드리프트 0.0 ms/분. Unity `PlayScheduled`: 6회 중 1회만 통과(표준편차 1.7–7.8 ms, 최대 5.8–69.6 ms). 프레임마다 `Play()`: 표준편차 4.2–10.8 ms. `PlayScheduled`는 예약 시각을 지킴(12/12) | `measurements/s1/repeats-summary.md`, `measurements/s1/A-vsync2-kiosk/summary.md` |
| **S-2 손 인식 + Unity** | **연결 방식 합격. 추론은 GPU delegate일 때만 Unity 프레임 유지** | 1만 회 호출 0.6 ms(호출당 약 0.06 µs, 할당 0 B). 추론 GPU 9.9 ms(p50)·CPU 29.8 ms. Unity 72.0 fps → GPU 71.8 fps(프레임 5%가 25 ms 이상), CPU 36.2 fps | `measurements/s2/s2-hand-sway-vsync2/summary.md` |
| **S-3 빌드 크기** | **부분 합격.** 총량 통과, 파일당 5 MiB는 기본 설정에서 실패, 최적화하면 통과(여유 작음) | 템플릿 그대로 `wasm.br` 9.74 MiB. 최적화 + LTO + 후처리 제거: `wasm.br` 4.86 MiB, 합계 6.38 MiB. Built-in RP는 2.52 MiB | `measurements/s3/size-report.json`, `measurements/s3/boot-1/boot-summary.json` |
| **S-6 소리 샘플** | **합격** | VCSL(CC0) 13개 WAV, A 단조 5음 음계(A C D E G) 3악기 모두 확보, 합계 1.2 MB, 앞 무음 최대 25.2 ms → 2 ms 이하로 자름 | `content/audio/manifest.json`, `content/audio/LICENSES.md` |

## 2. 측정 환경 (모든 숫자 공통)

| 항목 | 값 |
|---|---|
| 컴퓨터 | Apple M4 Pro, macOS 15.7.3 (arm64). 다른 작업과 같이 쓰는 개인 Mac |
| 시스템 부하 | 기록된 런(`A2`, `A3`)의 load average는 1분 5–7, 5분 7–19, 15분 20–39였고(`run-summary.json`의 `loadAvgAtEnd`), 수동 확인에서는 한때 77 / 103 / 70까지 올랐다. 시험 프로세스는 nice +5로 돌았다. **Unity 경로의 지터는 이 부하의 영향을 받았을 수 있다** |
| 브라우저 | Chrome 156.0.8078.49, 시험용 임시 프로필, DevTools(CDP)로 조작 |
| Chrome 플래그 | `--autoplay-policy=no-user-gesture-required --disable-background-timer-throttling --disable-renderer-backgrounding --disable-backgrounding-occluded-windows`(키오스크 가정). S-2는 `--use-fake-device-for-media-stream --use-file-for-fake-video-capture` 추가 |
| Unity | 6000.3.9f1, URP 17.3.0 (2D 렌더러), Web, Release, Brotli, 스트리핑 High, IL2CPP 크기 최적화, 예외 끔 (day0 빌드). 멀티스레드·Decompression Fallback 안 씀 |
| 디스플레이 | 초기 점검은 120 Hz, 기록된 런은 **144 Hz**(rAF 간격 p50 6.9 ms). `vSyncCount=2`로 **Unity는 72 fps**(`vSyncCount=1`은 144 fps). 60 fps 환경은 재현하지 못함 |
| 오디오 | AudioContext 44,100 Hz, `baseLatency` 5.8 ms, 브라우저가 알려 준 `outputLatency` 205–208 ms(**추정치**, 출력 장치가 무엇인지 확인 안 함) |
| 클릭음 | 3 kHz 사인 버스트 30 ms(`tools/gen_click.mjs`, 48 kHz 16비트), 96 BPM, 1/16 격자(156.25 ms), 60초(384슬롯), 예약 선행 100 ms |
| 소리 출력 | 시험 중 스피커는 음소거(`mute=1`), 기록 탭은 음소거 앞에서 분기. AudioContext는 `running` 상태였다 |

## 3. S-1 박자 시계

### 3.1 방법
- 한 페이지에 세 경로: ① Unity `AudioSource.PlayScheduled(AudioSettings.dspTime 기준)` ② Unity 프레임마다 `Play()`(비교 기준) ③ Web Audio `start(when)`(rAF에서 100 ms 앞서 예약).
- OS 루프백 녹음은 못 쓴다. 대신 `web/public/js/tap.js`가 **`AudioContext` 생성자를 감싸** 모든 컨텍스트의 `destination` 앞에 GainNode를 두고 AudioWorklet 녹음기를 병렬로 붙인다. Unity가 만든 컨텍스트도 같은 방식으로 잡혔다(확인함: Unity 클릭이 녹음됨). 사용자가 듣는 소리는 바뀌지 않는다.
- 녹음(모노 float32)에서 클릭 시작을 오프라인으로 찾고(`tools/lib/onsets.mjs`, 문턱은 최대값의 10%, 서브샘플 보간) 이상 격자와의 차이를 낸다. 일정한 지연(출력 지연 등)은 첫 클릭에 흡수하고, 직선 추세를 **드리프트**로, 나머지를 **편차**(표준편차·p95·최댓값)로 보고한다. 슬롯 번호는 이웃 클릭과의 간격으로 이어 붙인다(드리프트가 반 슬롯을 넘어도 안 감김).
- 스트레스: 2초마다 메인 스레드 50 ms 막기(`stress`), 거기에 20초 지점에서 탭 가림 10초(`hidden`, DevTools로 새 탭 열기), 20초 지점에서 창 포커스 잃음 10초(`blur`, **합성 blur/focus 이벤트**).
- 합격 기준(§6.3 [제안값]): 표준편차 ≤ 3 ms, 최대 ≤ 10 ms, 분당 드리프트 ≤ 5 ms, 예약 시각 준수, 교란 뒤 유지.

### 3.2 정지 상태·메인 스레드 막기 (72 fps, 60초, 3회 반복)

| 시나리오 | 경로 | 표준편차 (ms) | p95 (ms) | 최댓값 (ms) | 드리프트 (ms/분) | 합격 |
|---|---|---|---|---|---|---|
| calm | Unity `PlayScheduled` | 1.74 – 3.88 (중앙 3.39) | 2.87 – 5.20 | 5.84 – 55.72 | -14.1 – +2.4 | 1/3 |
| calm | Unity `Play()` 프레임마다 | 4.21 – 4.87 | 7.12 – 9.15 | 8.62 – 12.38 | -15.2 – +0.8 | 0/3 |
| calm | **Web Audio** | **< 0.01** | < 0.01 | < 0.01 | 0.0 | **3/3** |
| stress | Unity `PlayScheduled` | 2.48 – 7.76 (중앙 3.55) | 3.29 – 11.56 | 6.23 – 69.59 | -27.4 – -1.1 | 0/3 |
| stress | Unity `Play()` 프레임마다 | 6.38 – 10.77 | 8.03 – 20.17 | 39.09 – 47.09 | -48.7 – +0.7 | 0/3 |
| stress | **Web Audio** | **< 0.01** | < 0.01 | < 0.01 | 0.0 | **3/3** |

- 모든 런에서 384슬롯 중 빠진 슬롯 0개.
- 144 fps 보조 런(`vSyncCount=1`, 1회, `B-vsync1-kiosk`): `PlayScheduled` calm 표준편차 3.71 ms·최대 12.39 ms, stress 최대 61.78 ms. 같은 런의 `Play()` calm은 표준편차 15.80 ms(이때 오디오 시계가 벽시계보다 96 ms 늦음 → 시스템 교란 의심). Web Audio는 0.00.
- `PlayScheduled`의 큰 최댓값(53–70 ms)은 모두 **한 박만 늦는 일회성**이다(예: `A2` calm 슬롯 227). 원인은 확인하지 못했다.
- Web Audio 표준편차 "0"은 **오디오 그래프 안**의 값이다(샘플 단위 정확, 크롬이 시작 시각을 서브샘플 보간). 장치 버퍼와 OS 지터는 이 방법으로 보이지 않는다.

### 3.3 탭 가림·포커스 잃음 (72 fps, 각 1회)

| 경로 | 탭 가림 10초 (`hidden`) | 포커스 잃음 10초 (`blur`, 합성) |
|---|---|---|
| Web Audio | rAF가 멈춰서 **63슬롯 예약 못 함**(소리 끊김). 돌아온 뒤 격자 유지: 표준편차 0.00, 최대 0.00 ms | 영향 없음(0.00) |
| Unity `PlayScheduled` | 64슬롯 못 침. 돌아온 뒤 첫 박들이 어긋남: 표준편차 5.19, **최대 65.10 ms** | `runInBackground`를 켠 경우 영향 없음(표준편차 1.86, 최대 5.28 ms) |
| Unity `Play()` | 64슬롯 못 침. 돌아온 뒤 최대 75.85 ms | 켠 경우 최대 58.12 ms |

- **템플릿 기본값 함정**: Unity URP 2D 템플릿의 `runInBackground`는 **꺼짐**(`ProjectSettings.asset: runInBackground: 0`). 이때 창이 포커스를 잃으면 Unity가 멈춘다. 합성 blur 10초 시험에서 `PlayScheduled`는 176슬롯, `Play()`는 64슬롯을 놓쳤고 Web Audio는 영향이 없었다(`measurements/s1/D-vsync2-runInBackground-off/`). 이 값 때문에 S-1 가림 시험과 S-2 첫 시도가 멈췄다. 이후 모든 시험은 `Application.runInBackground = true`로 했다.
- 탭 가림은 두 JS/C# 스케줄러(rAF·Unity 루프) 모두 멈춘다. 키오스크에서는 일어나지 않아야 하는 상황이지만, 일어나면 **예약 선행(100 ms)보다 긴 정지는 막을 수 없다.**
- 기본 크롬 설정(키오스크 플래그 없음)으로 가림 시험을 하려 했으나 첫 런이 끝나지 않아 포기했다(`C` 런). 창이 다른 창에 가려져 크롬이 페이지를 멈춘 것으로 추정하지만 원인을 확인하지는 않았다.

### 3.4 예약 시각 준수 (`PlayScheduled(dspTime + x)`)
- 12번 시험(0.25 / 0.5 / 1 / 2초 각 3번): **즉시 재생된 적 없음.** 실제 지연과 요청의 차이는 0.00 ms가 대부분이고 가장 큰 것은 -2.38 ms(0.25초 케이스). `measurements/s1/E-ahead/analysis.json`.
- 즉 "Web에서 `PlayScheduled`가 시각을 무시하고 즉시 재생한다"는 포럼 보고는 이 환경(Unity 6000.3.9f1, Chrome 156, macOS)에서 **재현되지 않았다.**

### 3.5 Unity 경로가 흔들리는 이유 (관찰과 코드 확인)
- 에디터에 설치된 `Audio.js`(`PlaybackEngines/WebGLSupport/BuildTools/lib/Audio.js`)를 읽었다: `JS_Sound_Play(…, delay)`가 `AudioContext.currentTime + delay`로 시작하고, `delay`는 엔진이 계산한다. 즉 예약은 **호출 시점 기준 상대 지연**이다.
- `AudioSettings.dspTime`은 **오디오 컨텍스트 시계가 아니었다.** 숨김 구간에서 10.001초를 한 번에 건너뛰었고(`maxDspStallSec`), `dspTime - ctx.currentTime`은 60초 동안 분당 0.3–9.0 ms씩 벌어졌다(stress 9.0).
- 그래서 `dspTime`으로 짠 격자는 오디오 출력과 분당 수 ms씩 어긋나고, 메인 스레드가 멈추면 호출 시점이 밀려 그만큼 틀어진다. (이 설명은 코드와 위 관찰에서 추정한 것이며 엔진 내부를 계측해 증명하지는 않았다.)

### 3.6 오디오 시계와 벽시계는 같지 않다
- Unity 없이 `AudioContext` 하나만 60초 돌려 비교(`measurements/clock-lag/F-clock-lag/results.json`): `currentTime`이 벽시계보다 앞선(+) 또는 늦은(-) 정도는 calm +1.9 ms, 50 ms 막기 -7.2 ms, 출력 탭(tap.js) 장착 calm -16.5 ms, 탭 + 막기 -11.4 ms.
- Unity 페이지의 S-1 런(`A`, 60초)에서는 -113 … 0 ms/런이었다(12런 중 6런이 10 ms 이하). 원인은 확인 못 했다.
- 의미: **화면 연출을 벽시계(`performance.now()`/`Time.time`)로 그리면 소리와 어긋날 수 있다.** 기획서 §6.1처럼 `getOutputTimestamp()`로 계속 다시 맞추는 방식이 필요하다.
- 페이지를 연 직후 첫 런에서 `AudioContext.currentTime`이 약 1초 동안 거의 멈춰 있어 그동안 예약한 클릭 3개가 겹쳐 울렸다(초기 시험에서 관찰, 원본 데이터는 폐기해 숫자는 인용하지 않는다). 격자 원점은 **시계가 실제로 움직인 뒤에** 잡아야 한다.

### 3.7 이 시험의 한계와 하네스에서 고친 오류
- 한계: Mac 한 대, 단일 런이 많음(N=3은 calm·stress만), 시스템 부하 변동, 장치 쪽 지터와 출력 지연은 못 잼, 포커스 잃음은 실제 OS 포커스가 아니라 합성 이벤트, Windows는 S-4에서 따로.
- 시험 중 찾아 고친 것(이 문서의 숫자는 모두 고친 뒤 값):
  1. 클릭 검출기: 서브샘플 어긋난 클릭이 8박마다 빠졌다(재무장 조건 오류). 고치고 회귀 테스트 추가.
  2. 격자 슬롯 번호: 드리프트가 반 슬롯을 넘으면 슬롯이 감겨 표준편차가 부풀려졌다. 이웃 간격으로 이어 붙이는 방식으로 바꾸고 테스트 추가.
  3. 시험 시작 직후 오디오 시계 정지로 첫 클릭 3개가 겹침 → 하네스가 시계가 움직일 때까지 기다리게 함.
  4. 합성 blur와 사용자의 실제 창 전환(런 중 몇 번 발생)을 구분하도록 분석을 고침.

## 4. S-2 손 인식 + Unity

### 4.1 방법
- 같은 페이지에 Unity 빌드와 `@mediapipe/tasks-vision` **0.10.35**(정확히 고정), 손 모델 `hand_landmarker.task`(float16 v1, 7,819,105 B)를 올렸다. 둘 다 로컬 서버에서 제공(웹 한도와 무관한 시험).
- 카메라 없음 → Chrome fake 카메라. 위키미디어 공용 **CC0** 손 사진("Left Hand of a Female Human")을 640×480 30 fps 2초 영상으로 만들어 좌우로 흔들었다(`tools/make_fake_camera.sh`). 손은 모든 프레임에서 인식됐다.
- C#이 매 프레임 `Day0_ReadHands(double[135])`를 호출해 JS의 최신 프레임을 당겨 읽는다(§5.2의 pull). 창 20초씩 Unity 프레임 시간을 쟀다.

### 4.2 결과 (`measurements/s2/s2-hand-sway-vsync2/`)

| 구간 (각 20초) | Unity fps | 프레임 ms p50 / p95 / p99 / max | 25 ms 초과 프레임 | 50 ms 초과 |
|---|---|---|---|---|
| 추적 없음 (전) | 72.0 | 14.0 / 15.0 / 15.0 / 20.0 | 0 | 0 |
| 매 프레임 읽기만 | 72.0 | 14.0 / 15.0 / 15.0 / 18.0 | 0 | 0 |
| **추적 GPU delegate** | **71.8** | 14.0 / 25.0 / 26.0 / 31.0 | **74** (약 5%) | 0 |
| 추적 CPU delegate | 36.2 | 31.0 / 65.0 / 67.9 / 74.0 | 390 | 71 |
| 추적 없음 (후) | 72.0 | 14.0 / 15.0 / 15.0 / 16.0 | 0 | 0 |

| 추론(JS, 메인 스레드, 같은 창) | 프레임 | 손 인식 | p50 / p95 / p99 / max (ms) | 모델 생성 |
|---|---|---|---|---|
| GPU delegate | 601 | 601 (100%) | 9.9 / 11.6 / 13.4 / 17.0 | 101 ms |
| CPU delegate | 468 | 468 (100%) | 29.8 / 30.7 / 31.7 / 36.8 | 58 ms |
| 손 없음(별도 probe, 손바닥 검출만 반복, 카메라 20 fps) GPU / CPU | 120 | 0 | 8.3 / 11.4 / 11.9 (GPU), 17.5 / 18.5 / 19.1 (CPU) | 90 / 60 ms |

- **추론 끝 → C#이 읽기까지**(전달 구간): GPU 중앙 0.2 ms, p95 0.3 ms, 최대 0.5 ms (n=601). 추론이 같은 메인 스레드의 rVFC에서 끝나고 Unity 프레임이 바로 이어서 읽기 때문이다.
- `requestVideoFrameCallback`의 `captureTime`은 fake 카메라 프레임 **전부(601/601, 468/468)에 들어 있었다**(§2.3 항목 6의 일부 확인). 콜백 시각과의 차이는 중앙 5.4 ms(GPU)였지만 이것은 fake 카메라 값이라 실제 웹캠의 노출·장치 지연은 알려 주지 않는다.

### 4.3 연결 비용 (1만 회, 추적 끔, `performance.now()` 분해능 0.1 ms)

| 호출 | 합계 | 호출당 | 관리 코드 할당 |
|---|---|---|---|
| `Day0_NowMs()` | 0.9 ms | 0.09 µs | - |
| **`Day0_ReadHands(double[135])` pull** | **0.6 ms** | **0.06 µs** | **0 B** |
| 같은 프레임을 JSON 문자열로 받기(2,534자), 1,000회 | 16.6 ms | 16.6 µs | 8,192,000 B (호출당 8,192 B) |
| JS에서 `SendMessage(JSON)`, 1만 회 | 151 ms | 15.1 µs (그중 stringify 3.6 µs) | Unity가 1만 회 모두 받음 |
| JS에서 `SendMessage(JSON)` + `JsonUtility` 파싱 | 193 ms | 19.3 µs | |

- **JS가 C#이 넘긴 `double[]`에 쓰는 방식은 안전했다**: `HEAPF64.set(...)`로 쓴 값을 C#이 그대로 읽었다(8바이트 정렬 확인 통과). 48 MB를 새로 할당해 메모리가 커진 뒤에도 같았다.
- 매 프레임 pull(0.06 µs, 할당 없음)과 JSON 문자열(16.6 µs, 호출마다 8 KB 할당)은 약 280배 차이다. `SendMessage`는 호출당 15–19 µs라 저빈도 이벤트에는 충분하다.

### 4.4 그 밖에 확인된 것
- 이 버전에서는 `detectForVideo(video)`를 **타임스탬프 없이 불러도 예외가 없었다**(`withoutTimestamp: accepted`). 그래도 우리 코드는 `performance.now()`를 넘긴다. 결과 키: `landmarks`, `worldLandmarks`, `handednesses`, `handedness`.
- 네트워크: 페이지가 낸 요청 22개가 전부 `localhost`였다. POST는 시험 결과 저장 1건뿐이고 영상·이미지 업로드는 없었다(로컬 서빙 기준. CDN으로 불러올 때의 요청은 안 봄).
- 콘솔 경고(MediaPipe): `OpenGL error checking is disabled`, `Using NORM_RECT without IMAGE_DIMENSIONS is only supported for the square ROI`.

### 4.5 판정과 한계
- 판정(§12.1 S-2): 추론이 켜져도 Unity 평균 프레임은 유지됨(GPU). 다만 **프레임의 약 5%가 25–31 ms로 늘어난다.** 눈에 띄는지는 사람이 봐야 한다. CPU delegate는 쓸 수 없다.
- 못 한 것: 실제 웹캠(카메라 지연), 웹 워커로 옮기기, 3840×1080 캔버스, MediaPipe 1.x, 실제 손이 들어오고 나가는 상황(손바닥 검출 경로는 "손 없음" probe로만 쟀다), LTO 빌드에서의 같은 측정.

## 5. S-3 빌드 크기

### 5.1 결과 (`measurements/s3/size-report.json`, Brotli, 압축 후 on-disk)

| 빌드 | 설정 | `wasm.br` | `data.br` | 전체 합계 | 5 MiB 초과 파일 |
|---|---|---|---|---|---|
| `s3-template` | **빈 URP 2D 템플릿 그대로**(패키지 전부, 스트리핑 Minimal, 코드 최적화 속도) | 10,213,948 B = **9.74 MiB** | 4.34 MiB | 14.21 MiB | wasm |
| `s3-lean-size` | 패키지 정리 + 스트리핑 High + IL2CPP 크기 최적화 + 예외 끔 + `DiskSize` | 5,539,781 B = **5.28 MiB** | 2.91 MiB | 8.30 MiB | wasm |
| `day0` | 위 + Day0 시험 코드(+32 KB) | 5.31 MiB | 2.91 MiB | 8.34 MiB | wasm |
| `s3-lean-size-lto` | + `DiskSizeLTO` (Day0 코드 포함) | 5,097,726 B = **4.86 MiB** | 2.91 MiB | 7.89 MiB | 없음 |
| `s3-pp-off` | + URP 후처리 데이터 제거(필름 그레인·SMAA 텍스처), `DiskSize` | 5.31 MiB | **1.40 MiB** | 6.83 MiB | wasm |
| `s3-pp-off-lto` | + 후처리 제거 + `DiskSizeLTO` | **4.86 MiB** | 1.41 MiB | **6.38 MiB** | 없음 |
| `s3-builtin` | URP 완전 제거, Built-in RP, 빈 씬(Camera만), `DiskSize` | 2,643,559 B = **2.52 MiB** | 0.60 MiB | 3.23 MiB | 없음 |

- 한도(OpenDesk `config.py`): 파일당 5 MiB, 합계 15 MiB, 파일 80개. 파일 수는 17–18개.
- `wasm.br` 하나가 최대 변수라는 §10.2의 예상이 맞았다. 템플릿 그대로는 파일당 한도를 넘는다.
- Brotli를 직접 품질 11·창 24비트로 다시 압축하면 Unity 기본 압축보다 2.3–3.7% 작다: `day0` 5.12 MiB(그래도 5 MiB 초과), LTO 4.74 MiB(여유 0.26 MiB). `tools/size_report.mjs --recompress`.
- 설정 이름 확인됨: `UnityEditor.WebGL.UserBuildSettings.codeOptimization`(값 `BuildTimes / RuntimeSpeed / RuntimeSpeedLTO / DiskSize / DiskSizeLTO`), `PlayerSettings.WebGL.nameFilesAsHashes`(속성 존재, 효과는 안 봄).
- `data.br`의 큰 부분은 URP 후처리 텍스처(필름 그레인 10장 × 262 KB 등)였고, 빼면 1.5 MiB가 줄었다(`*.asset-breakdown.json`). "Splash Screen Unity Logo" 내장 텍스처(2.8 MB 비압축)는 스플래시를 꺼도 패킹 목록에 남았다.
- 이 비교의 한계: 빌드는 모두 빈 씬이다. 실제 제품 코드(VContainer, 콘텐츠 로드 등)와 uGUI/TMP, Input System은 빠졌다. LTO 빌드의 실행 속도는 재지 않았다(S-1·S-2는 비-LTO `day0` 빌드로 잼).

### 5.2 로컬 서버 서빙과 Chrome 부팅
- `web/serve.mjs`(표준 라이브러리만)가 `.br`에 `Content-Encoding: br`와 안쪽 확장자의 `Content-Type`을 보낸다(`.wasm`은 `application/wasm`). COOP/COEP 헤더는 안 보낸다(OpenDesk 워커와 동일).
- Chrome 156은 `http://localhost`에서도 `Accept-Encoding: gzip, deflate, br, zstd`를 보냈다. 즉 **localhost에서 Brotli가 네이티브로 풀린다**(§2.3 항목 4 확인).
- `s3-template`, `s3-lean-size`, `day0`, `s3-lean-size-lto` 4개 모두 Chrome에서 부팅(`createUnityInstance` 423–824 ms), 렌더링 확인(스크린샷 `measurements/s3/boot-1/day0.png`). 압축 해제 크기: `s3-lean-size` wasm 22.0 MB, 템플릿 59.2 MB. 템플릿 빌드는 콘솔에 영속 데이터 경로 안내 1건.
- 공개 호스팅에 게시하는 시험은 하지 않았다(승인 전).

## 6. S-6 소리 샘플

- 출처: VCSL(github.com/sgossner/VCSL), **commit `c1ea7bcc3c7309650ab0da9d15c9cd1fbc4a4c7e` 고정**, 라이선스 **CC0 1.0**(저장소 `LICENSE` 원문 확인). 파일별 URL·원본 SHA-256은 `content/audio/manifest.json`.
- **발견: 파일 이름의 옥타브가 실제 음과 다르다.** 마림바와 리코더는 이름보다 **12반음 높게** 소리 난다(예: `Marimba_..._C4` = 524.5 Hz = C5, `SopRecorder_..._C5` = 1049 Hz = C6). 하프는 이름과 같다. YIN과 DFT로 측정했다. 아래 음 이름은 **측정한 소리 높이** 기준이다.
- 음계: **A 단조 5음(A C D E G)**. 같은 음이름 집합이다(C 장조 5음과 동일).

| 악기 | 샘플(소리 높이) | 음계 만들기 | 길이 |
|---|---|---|---|
| 하프(Concert Harp, mf) | A4, C5, E5, G5, D6 | A4 C5 E5 G5는 그대로, **D5는 D6을 -12반음**(재생 속도 0.5) | 1.3 s |
| 마림바(Outrigger, 보통 세기) | C5, G5 (+ 측정용 C5 센 세기) | A4 = C5 -3, C5, D5 = C5 +2, E5 = G5 -3, G5. **그대로 쓰는 음 2개, 나머지는 최대 3반음 이동** | 1.3 s |
| 소프라노 리코더(스타카토) | A#5, C6, D6, E6, G6 | C6 D6 E6 G6는 그대로, **A5는 A#5를 -1반음**. 지속음이 아니라 **스타카토**를 골랐다. 지속음 2개를 재 보니 원본이 10–13초이고 최대치 50% 도달이 C 16.6 ms, G 80.3 ms로 들쭉날쭉했다 | 0.44–0.52 s |

- `tools/prepare_samples.mjs`가 받은 파일을 자르고 처리한다: 모노, 앞 무음 제거(소리 시작 2 ms 전부터), 끝 150 ms 페이드, 최대치 -3 dBFS 정규화, 16비트 WAV. 합계 13개 **1.2 MB**(기획서 가정 1.5–2.5 MB보다 작음).
- 앞 무음(원본 → 자른 뒤, -60 dBFS 기준): 하프 16.1–25.2 ms → 0.00–0.11 ms, 마림바 5.5–6.1 ms → 1.0–1.95 ms, 리코더 0.0–4.3 ms → 0.00–0.19 ms.
- **'소리가 시작된 걸로 들리는 시점'(최대치의 50%에 도달하는 시간)**: 하프 4.8–11.1 ms, 마림바 3.1–7.0 ms, 리코더 12.0–34.1 ms. 박에 맞추려면 이만큼 **앞당겨 예약**해야 한다. 값은 `manifest.json`의 `trimmed.perceivedOnsetOffsetMs`.
- 음 높이 측정값(레이블 대비 센트): 하프 -6…-11, 마림바 -2…+4, 리코더 -9…+10. 악기끼리 최대 약 20센트 어긋난다. 필요하면 재생 속도로 보정한다(`manifest.json`의 `centsFromLabel`).
- 음계 매핑 15개는 `manifest.json`의 `notes`(대상 음, 파일, 반음 이동, `playbackRate`).

## 7. 하지 못한 것 · 확인하지 못한 것 · 실패한 것

- S-4(Windows PC), S-5(이미지)는 사용자 담당이라 안 했다. **S-1은 Windows에서 다시 돌려야 한다**(`CHROME_PATH` 환경 변수로 Chrome 경로 지정, Node 필요).
- S-1을 **기본 크롬 스로틀링 상태**로 못 쟀다(창이 가려지면 크롬이 페이지를 멈춤). 실제 OS 포커스 잃음도 못 쟀다(합성 blur만).
- OS 루프백 녹음과 장치 출력 지터, 실제 출력 지연은 못 쟀다. `outputLatency` 205–208 ms는 브라우저 추정치다.
- 실제 웹캠 지연, 웹 워커 추론, 3840×1080 FPS, 8시간 구동, 공개 호스팅 게시(OpenDesk 사이트 개수·슬러그)는 안 했다.
- LTO 빌드의 **실행 속도**, 실제 콘텐츠 크기에서의 `wasm.br`은 모른다.
- MediaPipe 손 모델 파일의 라이선스는 모델 다운로드 페이지에서 확인하지 못했다(NOTICE에 표시). 공개 저장소에 모델을 올리지 않는다.
- Unity 배치 모드에서 `-nographics`로 열면 이 URP 프로젝트 첫 임포트 중 에디터가 세그폴트로 죽었다. `-nographics` 없이 쓴다.
- `AudioSettings.dspTime`이 왜 호출 간 1–2 ms 단위로 움직이는지, `PlayScheduled`의 일회성 53–70 ms 지연의 원인은 확인하지 못했다.
- 호스트가 공유 Mac이라 시스템 부하가 변했다. Unity 경로의 편차는 **부하가 작은 전용 PC에서 더 좋아질 수 있다**(Web Audio 경로는 부하와 무관하게 0).

## 8. 기획서 v0.3에 반영할 권고

1. **박자 시계 엔진: 경로 2(Web Audio)로 확정.** Unity는 화면만 맡는다.
   - JS가 격자와 예약을 맡는다: `T(n)=origin+n×slot`을 **`AudioContext` 시간**으로 계산해 `start(when)`. C#은 어떤 음·어느 슬롯인지만 JS 함수로 넘긴다(저빈도 호출).
   - `dspTime`/`PlayScheduled`는 박자 시계로 쓰지 않는다. (정지 상태에서도 기준을 안정적으로 못 넘고, 메인 스레드가 막히면 더 나빠졌다.)
   - 화면 연출은 `getOutputTimestamp()`로 오디오 시간 → `performance.now()`를 계속 다시 맞춘다. 벽시계와 오디오 시계가 분당 수 ms–100 ms 벌어진 적이 있다.
   - 격자 원점은 `AudioContext.currentTime`이 실제로 움직이기 시작한 뒤에 잡는다.
   - 스케줄러가 rAF에서 돌면 탭이 가려질 때 멈춘다. 전시용은 예약 선행(100 ms)을 늘리거나 Web Worker 타이머로 옮길지 정한다(미시험).
2. **Unity 설정**: `Application.runInBackground = true`(템플릿 기본은 꺼짐). `Application.targetFrameRate`는 `vSyncCount`가 0보다 크면 무시되어 Unity가 디스플레이 주사율(144 fps)로 돈다. 프레임 상한이 필요하면 `vSyncCount`를 쓴다.
3. **손 인식**: GPU delegate를 쓴다(추론 9.9 ms, Unity 평균 프레임 유지). CPU delegate는 Unity를 36 fps로 떨어뜨리므로 불가. 프레임 약 5%가 25 ms 이상으로 늘어나는 점은 눈으로 확인 후, 필요하면 추론 주기 하향(§5.1 순서)이나 웹 워커를 시험한다. `detectForVideo`는 타임스탬프를 계속 넘긴다.
4. **JS↔Unity 연결**: 기획서의 pull + `double[]`(+`HEAPF64`)를 그대로 쓴다(1만 회 0.6 ms, 할당 0, 쓰기 안전 확인). `SendMessage`는 저빈도 이벤트에만(호출당 15–19 µs).
5. **크기·호스팅(§10.2)**:
   - 템플릿 기본으로는 OpenDesk 파일당 5 MiB를 못 맞춘다(9.74 MiB). 실행 가능한 순서: 패키지 정리 + 스트리핑 High + 크기 최적화 + 예외 끔 → `DiskSizeLTO` → URP 후처리 데이터 제거 → Brotli 11 재압축. 이렇게 해도 `wasm.br`는 4.74–4.86 MiB로 **여유가 0.14–0.26 MiB뿐**이다. 실제 코드를 더하면 넘을 가능성이 크다.
   - 그래서 두 가지 중 하나를 정해야 한다: **(a) Built-in 2D로 바꾸면 `wasm.br` 2.52 MiB, 합계 3.23 MiB**(기획서 §10.2 ③이 현실적), 또는 (b) 파일당 25 MiB인 Cloudflare Pages로 가고 URP를 유지.
   - 합계 15 MiB는 문제 없다(Unity 6.4–8.3 MiB + 소리 1.2 MB + 이미지 약 2.4 MB 가정).
   - Brotli는 `http://localhost`에서도 풀린다(Accept-Encoding에 `br` 포함).
6. **소리(§6.2)**: VCSL 파일 이름의 옥타브가 마림바·리코더에서 12반음 낮게 적혀 있다. 샘플은 `content/audio/`에 준비됨. 박 맞춤용 `perceivedOnsetOffsetMs`를 예약에 반영한다. 리코더는 스타카토를 쓴다(지속음은 원본이 10초 이상이고 attack이 들쭉날쭉).
7. **기획서 문구 정정 제안**: "포럼의 `PlayScheduled` 즉시 재생 보고"는 이 환경에서 재현되지 않음(§3.4). "§2.3 항목 2(JS가 C# 배열에 쓰기)"는 확인됨. "§2.3 항목 4(localhost Brotli)"는 확인됨. MediaPipe 0.10.35에서 `detectForVideo` 타임스탬프 생략은 허용(그래도 넘김). 파일당 한도 조건은 §5 표로 갱신.
8. 일정에 영향: Day 1 시작 조건 중 "엔진 확정"은 이 결과로 충족된다. **Windows PC에서 S-1을 같은 스크립트로 한 번 더 돌릴 것**을 권한다.

## 9. 만든 파일 (저장소 기준)

- 골격: `README.md`, `LICENSE`(MIT, 저작권자 표기 "Seungmoon Choi"는 확인 필요), `NOTICE`, `.gitignore`, `package.json`, `package-lock.json`, `docs/`, `measurements/`, `tools/`, `content/`, `web/`, `unity/Downbeat/`
- Unity: `unity/Downbeat/Assets/_Project/Scripts/Core/`(BeatGrid, FrameStats), `Scripts/Day0/`(시험 코드, 버릴 것), `Plugins/Web/Day0Bridge.jslib`, `Editor/`(WebBuild, Day0SceneBuilder), `Tests/EditMode/`(12개 테스트), `Scenes/Day0.unity`
- 웹: `web/serve.mjs`, `web/public/{s1,s2,boot,clock-lag}.html`, `web/public/js/*`
- 도구: `tools/run_s1.mjs`, `run_s1_all.sh`, `run_s1_rest.sh`, `run_s1_repeats.sh`, `analyze_s1.mjs`, `summarize_s1.mjs`, `summarize_repeats.mjs`, `run_s2.mjs`, `summarize_s2.mjs`, `run_boot.mjs`, `run_clock_lag.mjs`, `size_report.mjs`, `make_s3_variant.mjs`, `prepare_samples.mjs`, `sync_vendor.mjs`, `make_fake_camera.sh`, `gen_click.mjs`, `lib/*`
- 테스트: Node 51개(`npm test`), C# EditMode 12개(`measurements/unit-tests/editmode-results.xml`) 모두 통과
- 측정 원본: `measurements/s1/*`(런별 `analysis.json`, `summary.csv`, 메타 JSON; 원본 녹음 `raw-audio/`는 약 350 MB라 git 제외), `measurements/s2/*`, `measurements/s3/*`, `measurements/clock-lag/*`
