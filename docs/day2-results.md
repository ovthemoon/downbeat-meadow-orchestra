# Day 2 결과 (2026-10-29): 웹캠 손이 악기를 친다

> 공개판 안내: 이 문서가 말하는 Day 1~2 화면 사진과 영상(옛 캐릭터 무대)은 공개 저장소에 넣지 않았다. 숫자와 측정 원본은 그대로다.

- 범위: 기획서 §12.2 Day 2. 페이지 안에서 카메라와 MediaPipe 손 인식이 돌고, Unity가 손 좌표를 받아 소리와 화면으로 바꾼다. 카메라가 없거나 거부되면 마우스로 넘어간다. 손 녹화·재생이 된다.
- 원칙은 Day 0·1과 같다: **직접 잰 숫자만** 적는다. 원본은 `measurements/day2/`, 스크립트는 `tools/`.
- **이 Mac의 진짜 웹캠은 한 번도 켜지 않았다.** 모든 시험은 Chrome의 가짜 카메라(영상 파일)로 했다. 영상은 CC0 손 사진 한 장을 움직여 만든 것이다(`tools/make_strike_video.py`). 사람 손이 아니다.
- 코디네이터 지침(상호작용 설계가 바뀔 수 있음)을 반영했다. **"내리치면 음이 난다"는 규칙은 임시**이고, 손의 위치·속도는 매 프레임 화면 쪽에 그대로 나간다(§3). 무대 장면은 다듬지 않았다.
- 사용자가 볼 것: `docs/media/day2-screenshot.png`(1600×600), `docs/media/day2-demo.mp4`(14.0초, **소리 포함**, 6.0 MiB).

## 1. 한눈에 보기

| 완료 기준 | 결과 | 증거 |
|---|---|---|
| 손을 내리치면 박에 맞는 소리 | **됨(가짜 카메라).** 150초 동안 132음, 버려진 것 0, 늦은 것 0. 모든 음이 격자 슬롯에 놓임 | `measurements/day2/latency/` |
| 좌/중/우로 악기 전환 | **됨.** 영상에 넣은 14번의 내리치기가 14음이 되었고, 악기 순서가 영상 계획과 같다(하프·하프·리코더·리코더·마림바·마림바·리코더·하프·마림바·리코더·하프·마림바·리코더·하프) | `measurements/day2/replay/determinism-summary.json` |
| 카메라 거부·없음이면 마우스로 자동 전환, 화면에 표시 | **됨.** 7가지 상황이 모두 통과(아래 §4.5). 무대 왼쪽 위 배지가 이유를 적는다 | `measurements/day2/start-flow/` |
| 같은 녹화 파일이 같은 소리 순서 | **됨.** 실제 제품 페이지에서 같은 파일을 두 번 재생: 14음, 순서·세기·격자 슬롯·요청 시각이 모두 같다. 게임이 프레임을 읽는 빈도를 바꿔도(72/60/20 Hz, 불규칙) 같다(EditMode) | `measurements/day2/replay/`, `ReplayTests` |
| 녹화 파일 1개 동봉 | **됨.** `content/replay/demo-hands.json` 16.07초, 483프레임, 158 KiB. `?input=replay`로 웹캠 없이 손 연주를 볼 수 있다 | 같은 폴더 |
| 개인정보 문구와 네트워크 증거 | **됨.** 시작 화면에 한국어·영어 문구와 외부 주소 목록. 45초 동안 페이지 요청은 GET뿐, 본문 있는 요청 0, WebSocket 0 (§4.6) | `measurements/day2/privacy/` |
| 모델 라이선스 | **확인함.** 공식 모델 카드에 Apache-2.0이라고 적혀 있다(§8). `NOTICE` 갱신 | `NOTICE` |
| Chrome(Mac)과 Windows PC에서 동작 | **Chrome(Mac)만.** Windows PC는 아직 못 했다 | §7 |
| 진짜 카메라의 손 → 소리 지연 | **못 쟀다. 사용자의 카메라와 휴대폰이 있어야 한다.** 아래 §4.3에 소프트웨어 구간 숫자만 있다 | §7 |

테스트: Node **143개**, Unity EditMode **137개**, Unity PlayMode **1개**, 모두 통과. Day 1의 박 격자 측정도 다시 돌려 퇴보가 없음을 확인했다(§6).

## 2. 무엇이 되는가

1. 시작 화면: 개인정보 문구, **Start** 버튼, "카메라 없이 해 보기" 링크 둘(마우스로 / 녹화된 손 데모).
2. **Start 한 번**이 소리를 깨우고(사용자 제스처), 이어서 카메라를 요청한다. 카메라 허용 창, MediaPipe 내려받기, Unity 로딩이 **동시에** 진행된다.
3. 카메라가 준비되는 동안(또는 허용 창이 떠 있는 동안)에는 마우스로 칠 수 있다. 준비되면 손으로 넘어간다.
4. 손 모드: 손을 카메라 앞에서 **아래로 빠르게 내리친다.**
   - 거울 화면 기준 왼쪽·가운데·오른쪽 1/3이 하프·리코더·마림바.
   - 손바닥 높이가 음 높이(10단계), 내리치는 속도가 세기(0.3 ~ 1.0).
   - 소리는 Day 1과 같은 격자 예약기를 거친다(가장 가까운 1/16 슬롯).
   - 오른쪽 위에 카메라 작은 창(좌우 반전)과 손 뼈대가 보인다. 무대에는 손 위치를 따라가는 점이 있다(손 속도가 빠를수록 커짐).
5. 카메라가 거부·없음·오류·모델 로딩 실패면 **마우스 모드로 자동 전환**하고 배지가 이유를 적는다.
6. 주소 인자: `?input=hand`(기본) · `mouse` · `replay`(`&file=replay/…json`, `&loop=0`), `?preview=0`(작은 창 숨김).
7. 손 모드에서 **R 키**: 손 움직임 녹화 시작/정지. 정지하면 `downbeat-hands-….json`이 내려받아진다(브라우저에서 확인함).
8. 손 제스처는 "내리치기(트리거) + 높이 + 속도 + 좌우" 세 가지 읽기뿐이다. 주먹·핀치 같은 다른 제스처는 인식하지 않는다.

## 3. 구조

```
[카메라] → <video>(숨김) → tracker.js: MediaPipe HandLandmarker(GPU) → 손 ID 이어 붙이기
                                   └→ hand buffer: Float64Array(136)  ←── Unity가 프레임마다 당겨 읽음
                                                        (Downbeat_Hands_Read, 1 KB 복사, 할당 0)
Unity:  JsHandSource ─┐
        ReplayHandSource ─┴→ HandFrame ─→ HandMotionTracker ─→ IHandMotionView ─→ 화면(HandCursorView, 이후 물·식물 …)
                                                  └→ IHandInterpreter(임시: 내리치기→음) ─→ NoteIntent ─→ Day 1 격자 예약기 ─→ Web Audio
        SwitchingPlayInput: 마우스 / 손 / 녹화 중 하나만 음을 낸다. 손이 안 되면 마우스로.
```

**설계가 바뀌어도 남는 것**과 **바뀔 수 있는 것**을 나눴다.

- 남는 것: 트래커(`tracker.js`), `double[]` 다리, 손 ID 이어 붙이기, 카메라 시작 흐름과 마우스 대체, 개인정보 문구, 녹화·재생, 측정 도구.
- **`HandMotionTracker` / `IHandMotionView`**: 손마다 위치(x, y), 속도(x, y, 단위는 영상 폭·높이당 초), 팔레트 길이 기준 속도, 점수, 팔레트 크기를 **매 프레임** 준다. 물결이나 식물처럼 연속으로 반응하는 화면은 이것만 읽으면 된다. 음 이벤트가 필요 없다.
- **`IHandInterpreter`**: 얇은 교체 가능한 층이다. 지금은 `HandInterpreter`(내리치기 → 악기·높이·세기). 음이 없는 설계라면 아무것도 내지 않는 구현으로 바꾸면 된다. 규칙은 다듬지 않았다.
- 제스처 수치는 `content/content.json`의 `hand` 항목(데이터)이라 다시 빌드하지 않고 바꾼다. 처음 값은 추측이다:

| 이름 | 값 | 뜻 |
|---|---|---|
| `triggerSpeed` / `rearmSpeed` | 3 / 1 | 팔레트 길이/초. 이 속도로 내려가면 트리거, 1 아래로 느려져야 다시 칠 수 있음 |
| `fullSpeed` / `minVelocity` | 9 / 0.3 | 이 속도에서 세기 1.0, 겨우 트리거되면 0.3 |
| `peakWaitMs` | 50 | 트리거 뒤 속도 정점을 기다리는 최대 시간(세기는 정점에서) |
| `velocityWindowMs` | 70 | 속도를 재는 구간 |
| `minRetriggerMs` / `maxGapMs` | 150 / 250 | 같은 손 재트리거 금지 / 이 시간 넘게 안 보이면 기록 초기화 |
| `heightHysteresis` / `partHysteresis` | 0.015 / 0.03 | 경계에서 단계가 깜빡이지 않게 하는 여유 |
| `minScore` | 0.5 | 이보다 낮은 손은 무시 |

- 좌표는 영상 기준 0~1, **x는 이미 거울 반전**(0 = 거울에서 본 왼쪽), y는 위가 0.
- 팔레트 중심 = 손목과 네 손가락 뿌리 다섯 점의 평균. 팔레트 크기(손목~가운데 손가락 뿌리, 또는 검지~새끼 뿌리 중 큰 쪽)로 속도를 나눠서 **카메라에 가깝든 멀든 같은 동작이 같은 값**이 된다.
- 손 ID: 직전 프레임과 가장 가까운 손으로 이어 붙인다(0, 1). 두 손의 모든 짝을 따져 전체 거리가 가장 짧은 짝을 고른다. 잠깐(0.4초) 안 보인 손은 ID를 예약해 둔다. 한 프레임에 0.35 넘게 튀면 다른 손으로 본다.
- 버퍼 배치 `double[136]`: `[0]` seq, `[1]` 촬영 시각 ms, `[2]` 추론 끝 ms, `[3]` 트래커 상태, `[4]` 손 수, 이어서 손마다 `[id, score, x0,y0,z0 … x20,y20,z20]` 65개, 마지막 `[135]` 영상 가로/세로 비. Day 0의 135칸에서 **맨 끝 한 칸(가로세로 비)만 늘었다.** 두 언어의 테스트가 숫자를 못 박는다.
- 트래커 상태: 꺼짐 · 시작 중 · 실행 중 · 카메라 거부 · 카메라 없음 · 카메라 오류 · 모델 실패. 상태는 버퍼에 들어 있어서 Unity가 같은 한 번의 호출로 알게 된다.
- MediaPipe는 **CDN에서 불러온다**(번들하지 않음): `cdn.jsdelivr.net`(코드, wasm 11 MB)와 `storage.googleapis.com`(모델 7.8 MB). 버전은 0.10.35로 고정. 주소를 바꾸는 `?mpbase=`·`?model=`은 **같은 사이트 경로만** 받는다(만든 링크로 임의 코드를 불러오게 하지 못하도록. 테스트 있음).
- 녹화 파일 형식 `downbeat-hands` v1: 프레임마다 시각(ms)과 손별 21점의 x,y(소수 넷째 자리). 영상은 들어 있지 않다. 재생 파일은 요청 경로를 상대 `.json` 경로로만 받는다.
- 재생: 녹화는 **격자의 마디선**에서 시작하고 **오디오 시계**로 흐른다. 각 음은 녹화에서 그 프레임이 가졌던 시각에 요청된다. 그래서 같은 파일이면 같은 슬롯에 같은 음이 난다. 반복 재생(`loop`, 기본 켜짐)은 끝나고 1초 쉰 뒤 다음 마디선에서 다시 시작한다.

## 4. 측정

환경: MacBook(이 Mac), Chrome 156.0.8078.49, 화면 주사율 120 Hz, `hardwareConcurrency` 14, 다른 작업과 함께 사용. 가짜 카메라 640×480 30 fps. Unity 제품 빌드와 MediaPipe(GPU 위임)가 같은 페이지에서 동시에 돈다. 카메라 작은 창은 켠 채로(기본 설정).

### 4.1 손 인식 추론 시간 (`tools/run_day2_perf.mjs`, 30초 창)

| 영상 속 손 | 추론 프레임 수 | p50 | p95 | 최대 |
|---|---|---|---|---|
| 0개 | 900 | 12.1 ms | 12.8 ms | 19.0 ms |
| 1개 | 855 | 15.5 ms | 26.3 ms | 34.0 ms |
| 2개 | 833 | 17.4 ms | 27.7 ms | 35.5 ms |

- 2손 영상에서 두 손이 다 보인 프레임이 509개(18.2 / 28.1 ms), 한 손만 보인 프레임이 324개(14.9 / 26.2 ms). 두 손이 겹치는 구간이 있어서다.
- 처리된 프레임 간격: 중앙 33.3 ms, p95 58.3 ms(1손). 30 fps 영상에서 30초 동안 855프레임이 처리됐다(추론이 길어진 프레임 뒤에 한 프레임씩 건너뜀).
- Day 0(Unity 없는 시험 페이지, 손 있음)은 p50 9.9 ms였다. **지금이 더 느리다**(15.5 ms). 원인은 조사하지 않았다. Unity가 같은 GPU로 120 fps를 그리는 것이 의심되지만 확인하지 못했다.
- 카메라 + 모델 준비까지: 0.38 ~ 0.52초(CDN, 캐시 끔). 이 회선과 시각에서 잰 값이다.

### 4.2 Unity 프레임 (페이지 `performance.now()`로 Unity 루프 프레임마다 기록)

| | 프레임 수 | fps | p50 | p95 | 최대 | 25 ms 넘는 프레임 |
|---|---|---|---|---|---|---|
| 추적 끔(마우스 모드) | 3599 | 120.0 | 8.3 ms | 9.1 ms | 17.0 ms | 0 (0%) |
| 추적 켬, 영상에 손 0개 | 3598 | 119.9 | 7.6 ms | 21.1 ms | 27.5 ms | 1 (0.03%) |
| 추적 켬, 손 1개 | 3033 | **101.1** | 6.5 ms | 31.2 ms | 42.7 ms | **382 (12.6%)** |
| 추적 켬, 손 2개 | 2965 | **98.9** | 6.8 ms | 31.6 ms | 42.2 ms | **456 (15.4%)** |
| 손 1개, 작은 창 끔 | 3042 | 101.3 | 7.3 ms | 30.1 ms | 102.8 ms | 379 (12.5%) |

- **손이 보이면 Unity가 눈에 띄게 끊긴다.** 추론이 메인 스레드에서 동기로 돌기 때문이다(기획서가 위험으로 적어 둔 것). 120 Hz 화면의 프레임 예산 8.3 ms를 추론 15~27 ms가 두세 프레임 막는다. 손이 없을 때는 거의 영향이 없다.
- 카메라 작은 창은 비용이 거의 없다(끄나 켜나 같음).
- 이번에는 **대응하지 않았다.** 기획서의 순서(추론 주기 낮추기 → 웹 워커 → 추적 창 분리)가 그대로 후보다. 손이 보일 때 25 ms 넘는 프레임 12~15%가 눈에 거슬리는지는 사람이 봐야 안다.
- Day 0의 "72 fps, 프레임 5%가 25~31 ms"와 직접 비교하면 안 된다. 그때는 수직동기 2로 72 fps에 맞춘 시험 빌드였고, 지금은 제품 빌드가 화면 주사율(120)로 돈다.

### 4.3 소프트웨어 지연 (`tools/run_day2_latency.mjs`, 150초, 132음)

모든 시각은 한 페이지의 `performance.now()`다. 숫자는 가짜 카메라 기준이고, **진짜 카메라의 노출·USB 지연은 들어 있지 않다.** "촬영 시각"은 `requestVideoFrameCallback`의 `captureTime`(가짜 카메라 프레임에는 있었다).

| 구간 | 중앙 | p95 | 최대 |
|---|---|---|---|
| 트리거 프레임 → 판정 프레임 (속도 정점 기다림) | 66.4 ms | 70.8 | 79.4 |
| 촬영 → 추론 끝 | 23.4 ms | 30.1 | 37.6 |
| 추론 끝 → Unity가 읽음 | 0.3 ms | 0.4 | 0.7 |
| Unity가 읽음 → 음 예약 | 0.0 ms | 0.1 | 0.1 |
| **판정 프레임 촬영 → 음 예약** | **23.8 ms** | 30.4 | 38.0 |
| 트리거 프레임 촬영 → 음 예약 | 91.2 ms | 97.9 | 105.4 |
| 음 예약 → 들리는 시각 추정 (선행 100 ms + 격자 기다림 + 출력 지연 추정) | 197.9 ms | 267.4 | 276.9 |
| 판정 프레임 촬영 → 들리는 시각 추정 | 220.6 ms | 292.0 | 306.2 |

- 격자 기다림(요청 → 슬롯)은 179 ms 중앙(100 ms 선행 + 0~156 ms)이고 따로 보고한다.
- 출력 지연은 브라우저의 추정치(`outputLatency` 17 ms, 이번 출력 장치). Day 1에서는 같은 Mac이 208 ms를 보고했다. 출력 장치에 따라 크게 달라진다.
- 속도 정점을 기다리는 66 ms(30 fps에서 두 프레임)가 알고리즘이 더하는 가장 큰 지연이다. `peakWaitMs`를 0으로 하면 없어지고, 대신 세기를 정점이 아닌 첫 프레임 속도로 정한다. 데이터 값이라 바로 바꿔 볼 수 있다.
- 한 가지 확인된 성질: 높이는 **속도가 기준을 넘은 순간의 손 위치**로 읽는다. 빠른 내리치기는 그때 이미 아래로 내려와 있어서, 영상에서 의도한 높이보다 1~2단계 낮게 읽힌 경우가 있었다(14번 중 최대 2단계). 임시 규칙의 성질이다.

### 4.4 같은 녹화 → 같은 소리 (`tools/run_day2_replay.mjs`, `ReplayTests`)

- 실제 제품 페이지에서 `?input=replay&loop=0`을 **새 페이지로 두 번** 재생했다. 매번 14음, 버려진 것 0, 늦은 것 0.
- 두 번의 비교: 악기·높이·세기 순서 **같음**, 첫 음 대비 슬롯 번호 **같음**, 마디 안 위치 **같음**, 첫 요청 대비 요청 시각 **같음**.
- EditMode: 같은 녹화를 72 Hz, 60 Hz, 20 Hz, 불규칙 간격으로 읽어도 음 목록이 같다. 동봉한 실제 `demo-hands.json`도 같은 시험을 통과한다. 손 해석기는 벽시계를 보지 않고 프레임 시각만 보기 때문이다.
- 동봉 파일의 음 순서(하프 6, 7 · 리코더 6, 7 · 마림바 5, 6 · 리코더 5 · 하프 3 · 마림바 4 · 리코더 6 · 하프 7 · 마림바 7 · 리코더 5 · 하프 5, 단계는 0~9).

### 4.5 시작 흐름과 마우스 대체 (`tools/run_day2_startflow.mjs`)

각 상황에서 Start를 누르고, 트래커 상태와 배지 문구를 읽고, 무대를 마우스로 세 번 클릭해 음이 나는지 본다.

| 상황 | 어떻게 만들었나 | 트래커 상태 | 배지 | 마우스 클릭 |
|---|---|---|---|---|
| 카메라 정상 | 가짜 카메라 | 실행 중 | Camera mode: strike downward … | 클릭은 음을 내지 않음, 음은 손에서만 |
| 권한 거부 | **Chrome의 실제 "권한 요청 거부" 모드** | 거부 | Mouse mode: camera permission was denied | 3음 |
| 카메라 없음 | `getUserMedia`가 `NotFoundError`를 내게 바꿈(**모의**) | 없음 | Mouse mode: no camera found | 3음 |
| 카메라 열기 실패 | `NotReadableError`(**모의**) | 오류 | Mouse mode: the camera could not be opened | 3음 |
| 허용 창이 계속 떠 있음 | 응답 없는 약속(**모의**) | 시작 중 | Mouse mode for now: camera is starting | 3음 |
| 모델을 못 받음 | `?mpbase=/no-such-dir`(실제로 404) | 모델 실패 | Mouse mode: the hand model could not be loaded | 3음 |
| 마우스를 직접 고름 | `?input=mouse` | (카메라를 요청하지 않음) | Mouse mode: click the stage | 3음, `getUserMedia` 호출 0번 |

7가지 모두 통과. 모의한 세 가지는 브라우저 API 결과를 흉내 낸 것이고 실제 기기 상태는 아니다. 어떤 경우에도 진짜 웹캠을 열지 않도록 가짜 장치 플래그를 항상 준다.

### 4.6 개인정보: 영상이 나가지 않는다는 증거 (`tools/run_day2_privacy.mjs`)

조건: 가짜 카메라 2손 영상, 손 추적 45초 동안(1304프레임 처리, 같은 시간 녹화도 켬), 캐시 끔. 기록은 둘이다: 페이지의 DevTools 네트워크 기록, 그리고 **Chrome 자체 net-log**(모든 프로세스; 34 MB라 git에는 넣지 않고 요약만 둔다).

| 확인 | 결과 |
|---|---|
| 페이지가 보낸 요청 | 45개: 이 사이트 41, `cdn.jsdelivr.net` 3(코드 둘, wasm 하나), `storage.googleapis.com` 1(모델) |
| GET이 아닌 요청 / 본문이 있는 요청 | **0 / 0** |
| WebSocket 생성 / 보낸 프레임 | 0 / 0 |
| 가장 긴 주소 | 110자 (영상 조각을 실은 긴 주소가 없다) |
| 워커·서비스 워커 | 없음(대상 종류는 `browser_ui`, `page`뿐) |
| 녹화(R 기능)를 켠 동안 | 네트워크 요청 없음 |
| **양성 대조**: 같은 페이지에서 카메라 작은 창 그림 1장을 일부러 POST | 두 기록 모두 잡음(DevTools: POST, net-log: 업로드 3,628 바이트). 탐지가 작동한다는 증거 |

- net-log가 "이 페이지가 시작했다"고 분류한 요청은 45개이고, 그 가운데 44개는 DevTools와 같은 요청(같은 호스트 구성)이며 나머지 1개가 양성 대조 POST다. 맨 위 문서 이동 1개는 net-log에서 Chrome 쪽으로 잡혀서 DevTools의 45개와 합이 맞는다. 이 외에 **Chrome 브라우저 자신**의 요청이 24개 있었다(업데이트, 최적화 모델 확인 등. 새 프로필의 Chrome이 하는 일이며 이 페이지와 무관). 그 가운데 POST는 6개, 본문은 0~1.6 KB로 카메라 영상이 실릴 크기가 아니다.
- DevTools 기록에 이미지 4장이 `net::ERR_ABORTED`로 남아 있다. 응답 상태는 200이고 화면에 나온다. 원인은 조사하지 않았다(Unity의 이미지 불러오기 방식으로 보임).
- **한계**: 이 빌드, 이 45초에 대한 증거다. 공개하기 전에 같은 스크립트를 다시 돌려야 한다. 또 CDN에서 불러온 코드가 카메라가 있는 페이지에서 돈다. 버전은 고정했지만 해시로 검증하지는 않는다(전시용은 로컬 사본을 쓰도록 `?mpbase=`를 만들어 두었다).
- 시작 화면 문구(한국어·영어): 영상은 이 브라우저 안에서만 처리하며 저장하거나 보내지 않는다, 손 좌표만 쓴다, 외부에서 받는 것은 손 인식 코드와 모델뿐이다(주소 목록은 설정에서 만들어 붙이므로 실제 주소와 어긋나지 않는다).

## 5. 크기 (`tools/assemble_site.mjs`, OpenDesk 한도 검사)

| | 값 |
|---|---|
| 사이트 전체 | **44개 파일, 8,242,792 B = 7.86 MiB** (한도 15 MiB, 80개) |
| `Build/downbeat.wasm.br` | 2,859,017 B = **2.727 MiB** (파일당 한도 5 MiB의 54%). Day 1(2.714 MiB)보다 약 0.01 MiB 늘었다 |
| 새로 생긴 파일 | 손 관련 JS 8개(합계 약 0.02 MiB), `content/replay/demo-hands.json` 161,829 B |
| MediaPipe | **사이트에 없음**(CDN) |

한도 안이다. 위반 0. 원본: `measurements/day2/site-report.json`.

## 6. 테스트

- Node 143개(Day 1의 89개 + 54): 손 버퍼, 손 ID, 카메라 오류 분류, 트래커(가짜 카메라·모델로 성공·거부·없음·모델 실패·두 실패·탐지 오류·정지), 설정 주소 검사, 녹화기, 녹화 자르기, 입력 모드, 페이지 상태.
- Unity EditMode 137개(50 + 87): 버퍼 읽기(나쁜 값 거르기), `HandMotionTracker`, 내리치기 해석(안 움직임, 느린 이동, 한 번만 침, 다시 치려면 느려져야 함, 좌우 3등분, 높이, 세기, 정점, 가깝고 멀어도 같음, 두 손, 사라진 손, 낮은 점수, 틈 뒤 재등장, 재트리거, 같은 입력 같은 출력, 리셋, 경계 여유), 녹화 검증·재생·마디선 시작·반복, 동봉 파일, 입력 모드와 전환(마우스 대체), 콘텐츠 검증(손 설정).
- Unity PlayMode 1개: 통과(마우스 입력으로 음 하나).
- **Day 1 퇴보 시험**(`tools/run_day1_measure.mjs --run day2-regression`, 같은 스크립트를 마우스 모드로): 150클릭 → 150음, 버려진 것 0, 예약 시각이 격자에서 4.4×10⁻¹² ms 이내(Day 1: 2.2×10⁻¹²). 번쩍임 시각은 소리 예약 대비 중앙 -2.6 ms(Day 1: -3.1 ms). **오디오 시작 위치의 숫자는 Day 1과 비교하면 안 된다.** 오디오 장치가 바뀌어 AudioContext가 48 kHz(Day 1은 44.1 kHz)가 되었고, 측정용 정합 필터의 견본이 44.1 kHz 그대로라 상관이 0.22로 낮다(Day 1은 0.99). 격자에 맞는지와 늦은 음이 없는지는 소리 위치와 무관하게 확인된다. 원본: `measurements/day1/day2-regression/`.
- Day 1 스크립트(`run_day1_*`, `record_demo.mjs`)가 기본값이 카메라가 된 새 페이지에서 **진짜 웹캠을 건드리지 않도록** `?input=mouse`를 붙였다.

## 7. 하지 않은 것 · 확인하지 못한 것

**진짜 카메라가 필요한 것: 사용자가 해야 한다.**
- **진짜 웹캠의 손 → 소리 지연.** 카메라 노출·USB 지연, 사람 동작의 정점 판정이 들어가는 종단 지연은 못 쟀다. 기획서 §11.2 M-B 그대로 휴대폰으로 손·기준선·스피커를 같이 찍어야 한다(N ≥ 30). 지금 있는 숫자(§4.3)는 소프트웨어 구간뿐이고 가짜 카메라 기준이다.
- **진짜 손.** 시험 영상은 사진 한 장을 이동만 시킨 것이라 손이 돌거나 쥐었다 펴지 않는다. 방 조명, 진짜 움직임 번짐, 피부색·옷 소매 등의 영향, 오인식·헛침, 제스처 수치(§3 표)의 적절함은 **모른다.** 임시 값이다.
- 진짜 카메라에서 `captureTime`이 있는지(없으면 콜백 시각을 쓰는데 상한값이 된다).

**이번 범위 밖 또는 하지 않은 것**
- Windows PC, Safari, Firefox에서 동작: 확인 안 함.
- 손이 보일 때 Unity 프레임이 끊기는 문제(§4.2): 대응하지 않았다.
- 카메라가 끊겨도 5초 안에 대기로 가는 처리, 상태 머신, 대기 자동 연주, `idle_hint`, 콘텐츠 핫리로드: Day 3~4 범위라 안 했다.
- 데모 영상의 소리·그림 어긋남은 이번 영상으로는 재 보지 않았다. 같은 방법(Day 1에서 중앙 그림이 소리보다 +17 ms 뒤)으로 만들었고, 무대에 손 점이 떠 있어 Day 1의 밝기 기준 검사 스크립트를 그대로 쓸 수 없다.
- 카메라 없음·열기 실패·허용 대기는 API 결과를 흉내 낸 시험이다(§4.5). 권한 거부와 모델 실패만 실제 브라우저 동작이다.
- 두 손의 ID 이어 붙이기는 단위 시험과 2손 영상의 손 수 집계로만 확인했다. 브라우저에서 ID 번호가 프레임마다 맞게 이어지는지 따로 기록하지는 않았다.
- CDN 코드는 해시 검증을 하지 않는다(§4.6).
- 사용자가 귀로 들어 본 것: 아직 아니다(소리 값은 데이터로 두었다).

## 8. MediaPipe 모델 라이선스 (Day 0에서 못 한 것)

- 다운로드 페이지에는 라이선스가 없고, `.task` 안의 두 `.tflite` 파일에도 라이선스 문구가 없었다.
- 손 랜드마커 문서 페이지(developers.google.com/edge/mediapipe/solutions/vision/hand_landmarker)의 "model card" 링크를 따라가 **공식 모델 카드 PDF**("MediaPipe Hands (Lite/Full)", 2021년 10월판)를 읽었다. 2쪽 "LICENSED UNDER"에 **Apache License, Version 2.0**이라고 적혀 있다. 같은 카드의 의도된 사용에는 "제스처 인식, 손 제어"가 있고, 범위 밖으로 "감시·신원 인식"을 명시한다. 장갑·가려진 손은 부적절하고, 어두운 곳·움직임 번짐 같은 조건은 시험되지 않았다고 한다.
- 이 카드는 "Hands (Lite/Full)" 계열의 카드이고, `hand_landmarker.task`는 그 계열의 번들이다(문서 페이지가 이 카드로 연결한다). 번들 자체의 메타데이터에는 별도 라이선스 표기가 없다.
- 모델 파일은 **저장소와 사이트에 넣지 않는다.** 방문자 브라우저가 구글 저장소에서 직접 받는다. `NOTICE`를 갱신했다(출처 주소·확인한 날짜 포함).

## 9. 다시 해 보려면

```sh
# 가짜 카메라 영상 만들기 (손 사진은 NOTICE의 CC0 사진, 저장소에는 없음)
python3 -I tools/make_strike_video.py --image hand.png --out hands1.y4m --hands 1     # 0, 1, 2

# Unity (프로젝트 루트에서). EditMode 테스트, 웹 빌드
UNITY=/Applications/Unity/Hub/Editor/6000.3.9f1/Unity.app/Contents/MacOS/Unity
cd unity/Downbeat
$UNITY -batchmode -projectPath "$PWD" -runTests -testPlatform EditMode -testResults ../../measurements/unit-tests/editmode-results.xml
$UNITY -batchmode -quit -projectPath "$PWD" -buildTarget WebGL -executeMethod Downbeat.EditorTools.WebBuild.Run \
  -dbScene Assets/_Project/Scenes/Main.unity -dbOut Builds/downbeat -dbProfile size -dbTemplate PROJECT:Downbeat

# 사이트 조립 → 브라우저 시험 (README의 목록)
node tools/assemble_site.mjs --report measurements/day2/site-report.json
node tools/run_day2_startflow.mjs --video hands1.y4m
```

진짜 카메라로 해 보려면 `node web/serve.mjs --port 8780 --mount /=dist/site`를 켜고 Chrome으로 `http://localhost:8780/`를 연다(`localhost`는 카메라가 허용되는 안전한 주소다).

## 10. 만든 파일

**Unity (`unity/Downbeat/Assets/_Project/`)**
- `Scripts/Core/`: `HandFrame.cs`(버퍼 배치, 읽기), `HandMotion.cs`(연속 손 데이터), `HandInterpreter.cs`(임시 내리치기 규칙), `HandInterpreterSettings.cs`, `InputMode.cs`(`?input=`과 대체 판정), `ReplayData.cs`(형식, 검사, 재생기, 경로 검사), `PitchHeightMap.cs`(높이→음, 마우스와 공유), `Interfaces.cs`·`NoteTypes.cs`·`ContentModels.cs`·`ContentValidator.cs`·`BeatGrid.cs`·`VelocityMapper.cs`(확장)
- `Scripts/Input/`: `HandPlayInput.cs`, `SwitchingPlayInput.cs`, `DebugPointerInput.cs`·`PointerMapping.cs`(수정)
- `Scripts/App/`: `JsHandSource.cs`, `ReplayHandSource.cs`, `IPreparable.cs`, `PagePort.cs`, `DownbeatApp.cs`, `DownbeatLifetimeScope.cs`(수정)
- `Scripts/Views/HandCursorView.cs`(연속 데이터가 화면에 닿는지 보이기 위한 점), `Plugins/Web/Downbeat.jslib`(수정)
- `Tests/EditMode/`: `HandFrameReaderTests`, `HandMotionTrackerTests`, `HandInterpreterTests`, `ReplayTests`, `InputModeTests`, `HandTestData`, `FakeAudioEngine` 외 기존 파일 보강

**페이지 (`Assets/WebGLTemplates/Downbeat/`)**: `index.html`(개인정보 문구, 배지, 작은 창), `js/tracker.js`, `hand-buffer.js`, `hand-ids.js`, `camera.js`, `tracker-config.js`, `hand-recorder.js`, `hand-preview.js`, `input-mode.js`, `page-state.js`, `start-page.js`

**데이터**: `content/content.json`(`hand` 항목), `content/replay/demo-hands.json`

**도구 (`tools/`)**: `make_strike_video.py`, `run_day2_{smoke,startflow,replay,privacy,perf,latency,record,recordkey}.mjs`, `record_demo_day2.mjs`, `lib/{day2-browser,netlog,replay-trim}.mjs`

**시험 (`web/test/`)**: `hand-buffer`, `hand-ids`, `camera`, `tracker`, `tracker-config`, `hand-recorder`, `input-mode`, `page-state`, `replay-trim`

**측정 원본**: `measurements/day2/{perf,latency,privacy,replay,start-flow,demo,smoke,record}`, `measurements/unit-tests/`, `measurements/day1/day2-regression/`

**미디어**: `docs/media/day2-demo.mp4`(6.0 MiB), `docs/media/day2-screenshot.png`
