# Day 1 결과 (2026-10-29): 처음으로 연주되는 Downbeat

> 공개판 안내: 이 문서가 말하는 Day 1~2 화면 사진과 영상(옛 캐릭터 무대)은 공개 저장소에 넣지 않았다. 숫자와 측정 원본은 그대로다.

- 범위: 기획서 v0.3 §12.2 Day 1(경로 2에 맞춘 것). 마우스·키보드로 3악기를 치면 소리가 박 격자에 맞춰 나고, 무대 단원이 같은 순간에 반응한다. 카메라·손 인식은 Day 2.
- 원칙은 Day 0과 같다: **직접 잰 숫자만** 적는다. 원본은 `measurements/day1/`, 스크립트는 `tools/`.
- 사용자가 볼 것: `docs/media/day1-screenshot.png`(1600×600), `docs/media/day1-demo.mp4`(12.4초, **소리 포함**).

## 1. 한눈에 보기

| 완료 기준 | 결과 | 증거 |
|---|---|---|
| Built-in 웹 빌드가 로컬 Brotli 서버에서 켜진다 | **됨.** Built-in RP 2D, URP 제거. Chrome 156에서 부팅, 콘솔 오류 0 | `measurements/day1/smoke/`, `tools/run_day1_smoke.mjs` |
| 시작 버튼 → 소리 | **됨.** 버튼 클릭(사용자 제스처)으로 AudioContext를 깨운 뒤 Unity가 시작. 자동재생 플래그 없이 동작 | 같은 스모크 시험(`outputLatency` 208 ms로 `running`) |
| 클릭이 격자에 맞는 음이 된다 | **됨.** 불규칙 클릭 150번이 150음이 되고(버려진 것 0), 예약 시각은 격자에서 2.2×10⁻¹² ms 이내. 출력 녹음에서 찾은 음 시작은 예약보다 평균 +0.05 ms, 표준편차 0.09 ms, 최대 0.44 ms 어긋남 | `measurements/day1/day1-measure/summary.json` |
| 스프라이트 번쩍임이 박과 맞는다 | **됨.** 번쩍임은 소리가 **들리는 시각**(출력 지연 포함)에 맞춰 풀림. 예약 시각 대비 중앙 -3.1 ms(-7.0 ~ +4.9 ms). 녹화 영상에서는 그림이 소리보다 중앙 +17 ms 뒤(-14 ~ +69 ms) | `summary.json`, `measurements/day1/demo/demo-sync.json` |
| 크기 보고 | 사이트 **34개 파일 7.66 MiB**(OpenDesk 한도 안). `wasm.br` **2.714 MiB**(파일당 한도 5 MiB의 54%) | `measurements/day1/site-report.json`, `build-size.json` |

테스트: Node **89개**, Unity EditMode **50개**, Unity PlayMode **1개**, 모두 통과.

## 2. 무엇이 되는가

- 시작 화면에서 **Start** 한 번 → 소리·내용 로드 → 무대와 관객용 화면이 나온다(`index.html`, Unity WebGL 템플릿).
- 무대(왼쪽 2/3)를 클릭한다.
  - 가로 3등분: **왼쪽 하프 / 가운데 리코더 / 오른쪽 마림바**.
  - 높이: 10단계 음 높이(A 단조 5음 × 2옥타브, 위쪽이 높은 음). 위·아래 15%는 쓰지 않아 끝 음을 누르기 쉽다.
  - 세기: 클릭이 빠를수록 크게(0.5 → 1.0). **Shift = 크게, Ctrl/Alt = 작게**.
  - 키보드: `A S D F G H J K L ;`가 10단계 음, 마우스가 가리키는 악기로 친다. `Space`는 포인터 위치에서 친다.
- 소리는 클릭 순간이 아니라 **가장 가까운 1/16 슬롯**(96 BPM, 156.25 ms)에 난다. 예약 선행 100 ms.
- 단원이 **연주 그림으로 바뀌고**, 살짝 뛰고 커지며 악기별 색으로 빛난다. 평소에는 조금씩 흔들린다.
- 관객용 화면(오른쪽 1/3): 박 선과 음 높이 줄 위에 **음 점**이 오른쪽에서 "지금 선"으로 흘러온다. 클릭이 받아들여지면 점이 바로 생기고, 들리는 순간 커진다.
- Unity Editor의 Play 모드에서도 소리가 난다(Unity 오디오를 쓰는 대체 엔진).

시간 흐름(예): 클릭 → 최소 100 ms 앞의 슬롯 예약 → 그 시각 + 출력 지연(브라우저 추정 208 ms)에 소리가 들리고 같은 순간 그림이 바뀐다. 그래서 **클릭에서 소리까지 약 0.3~0.47초**가 걸린다(4번 점검에서 0.35~0.42초, 폐기용 디버그 스크립트라 원본은 없음). 지연을 줄이려면 `minLeadSec`(content.json)을 낮춘다. 실제 스피커에서의 종단 지연은 아직 안 쟀다.

## 3. 구조 (한 줄씩)

| 폴더 (`unity/Downbeat/Assets/_Project/Scripts/`) | 하는 일 |
|---|---|
| `Core` (UnityEngine 없음) | `BeatGrid`, `QuantizedNoteScheduler`(격자 맞춤·파트별 최소 간격·밀기/버리기), `PitchMapper`(측정한 음 높이로 샘플·재생 속도), `NoteVisualQueue`, `ContentValidator`, `IAudioEngine` 등 |
| `Audio` | `WebAudioEngine`(웹 빌드, 페이지 스크립트 호출), `EditorAudioEngine`(Play 모드) |
| `Input` | `DebugPointerInput`, `PointerMapping`(좌표 → 파트·음) |
| `Content` | `ContentRepository`(UnityWebRequest로 `content.json`과 이미지) |
| `Views` | `StageView`, `PerformerView`, `AudienceView`, `ViewLayout`(`Camera.rect` 분할) |
| `App` | VContainer 조립(`DownbeatLifetimeScope`), 게임 루프 `DownbeatApp` |

- **VContainer 1.19.0**(MIT)을 UPM git 주소로 고정(`manifest.json`, `NOTICE`). `link.xml`로 IL2CPP 스트리핑 때문에 반사로 만드는 객체가 사라지지 않게 했다.
- **Day 0 시험 코드는 `Assets/_Day0/`로 옮겼다.** `DOWNBEAT_DAY0` 심볼이 없으면 컴파일되지 않고, 그 jslib은 꺼 두었다. 확인: 빌드 결과의 `framework.js`에 `Day0_` 문자열 0개(`Downbeat_Audio_*` 10개, `Downbeat_Page_*` 4개 있음).
- **소리는 페이지가 맡는다**(`Assets/WebGLTemplates/Downbeat/js/`).
  - `audio-clock.js`: 오디오 시계 ↔ 페이지 시계 변환, 시계가 실제로 움직였는지 판단.
  - `note-player.js`: 절대 오디오 시각에 `start(when)`, 늦은 요청 처리, 동시 발음 상한(가장 오래된 것을 페이드로 끊음).
  - `downbeat-audio.js`: Unity가 부르는 API, `start-page.js`: 시작 버튼.
- **Unity → JS 음표 호출은 원시값 인자만 쓴다**(`int, double, double, double`). Day 0에서 문자열·JSON 호출은 호출당 15~19 µs와 할당이 있었고, `double[]` pull·원시 호출은 1만 회에 1 ms 안팎이며 할당이 없었다. 프레임당 호출은 시계 읽기 2번 + 음표마다 1번.
- Day 0 적용 사항: `runInBackground` 켬(프로젝트 설정 + 코드), 격자 원점은 오디오 시계가 **0.25초 이상 움직인 뒤**에 잡음(`ClockWatcher`), 번쩍임은 `getOutputTimestamp()`로 **매 프레임** 오디오 시계에 다시 맞춤.
- **데이터로 정한 것**(코드에 숫자 없음): 악기별 샘플·실제 음 높이(`soundingMidi`)·튜닝 오차·소리 시작 보정, 단원별 `scale`·발 위치·발 기준점, 파트 색, 곡(BPM·음계·파트별 최소 간격), 오디오(마스터 -6 dB, 최대 32음, 예약 선행). 파일: `content/content.json`(`tools/build_content.mjs`가 측정값으로 채움).

### 이미지 (§5)
- 코디네이터가 처리한 `images-processed/` 9장을 `content/img/forest/`로 복사했다(`images-raw/`는 안 씀). 출처·권리 문구는 `NOTICE`에 적었다. `docs/assets-prompts.md`는 건드리지 않았다.
- `tools/measure_sprites.py`가 알파 영역과 **발 기준점**을 잰다(`content/img/forest/sprites.json`). 하프 몸통 높이 659 px, 리코더 715 px, 마림바 679 px.
- 하프는 `scale 0.78`, 리코더·마림바 `0.74`로 눈으로 맞췄다(데이터 값, 코드 아님). 스프라이트는 100 px = 1 단위, 무대는 16×9 단위.
- 단원은 가로 3등분의 가운데(-5.33 / 0 / +5.33)에 서서 **클릭 위치와 그림이 겹치게** 했다.

### 화면 배치
- 한 캔버스 8:3을 `Camera.rect`로 나눈다: 무대 2/3(16:9), 관객용 1/3. 기획서의 "좌우 반반"과 다르다. 이유: 무대 배경이 16:9라 반반(8:9)이면 단원 셋이 잘린다. `?view=stage`, `?view=audience`로 한쪽만 볼 수 있다.

## 4. 측정 (`tools/run_day1_measure.mjs`)

Chrome 156.0.8078.49(144 Hz 화면), 이 Mac(M4 Pro), 소리는 기록만 하고 스피커는 음소거. 마림바 C5 한 음, Shift로 세기를 고정. 박 정확도는 **음이 서로 겹쳐 울리므로** 문턱 방식 대신 **정합 필터**로 쟀다: 각 음 자신의 샘플 앞 25 ms를 파형 템플릿으로, 예약 시작 ±0.9 ms 창에서 찾는다(`tools/lib/matched.mjs`, 테스트 5개).

| 시험 | 음 수 | 격자에서 벗어남(예약 시각) | 출력에서 찾은 시작 − 예약 시작 |
|---|---|---|---|
| 불규칙 촘촘히(클릭 간격 120~970 ms) | 150 | 최대 2.2×10⁻¹² ms | 찾음 150/150(창 끝 0). 평균 +0.053 ms, 표준편차 0.086 ms, p95 0.28 ms, 최대 0.44 ms. 최저 상관 0.80 |
| 불규칙 떨어져서(1.4~2.4초, 앞 음이 끝난 뒤) | 50 | 최대 1.8×10⁻¹¹ ms | 평균 +0.038 ms, 표준편차 0.00002 ms, 최대 0.038 ms. 최저 상관 0.997 |

- 촘촘한 쪽의 0.09 ms 흔들림은 **앞 음의 울림이 템플릿 위치 추정을 살짝 미는 것**(테스트로 재현, 0.1 ms 안쪽)이고, 떨어진 음에서는 표준편차가 샘플 하나보다 훨씬 작다. 앞 음 끝에서 다음 음 시작이 슬롯에 맞는지는 후자가 증명한다.
- 클릭 간격 히스토그램(촘촘히, 연속한 두 음의 슬롯 차): 1슬롯 간격 18번 … 6슬롯 간격 13번(`summary.json`). `late 0`, `stolen 0`, `missingSample 0`(총 200음).
- 이 방법은 **오디오 그래프 안**의 위치다. 장치·OS 지터와 실제 출력 지연은 포함되지 않는다(Day 0과 같은 한계).

**시각 vs 소리 (페이지 기록).** 매 번쩍임마다 "그림이 풀린 프레임의 들리는 시각 − 그 음의 예약 시각"을 기록했다.

| | 개수 | 중앙 | p05 ~ p95 | 최소 ~ 최대 |
|---|---|---|---|---|
| 촘촘히 | 150 | -3.1 ms | -6.6 ~ +1.6 ms | -6.95 ~ +4.9 ms |
| 떨어져서 | 50 | -3.0 ms | -6.8 ~ +0.6 ms | -6.96 ~ +1.4 ms |

음수 = 들리기 **전**에 풀림. 프레임 절반(144 Hz에서 약 3.5 ms) 앞서 풀도록 `FlashLeadSec` 7 ms를 뒀기 때문이다. 풀린 그림은 그 프레임 끝에 화면에 나오므로 실제로는 소리와 한 프레임 안쪽이다.

**영상 안에서 눈으로 본 것.** 데모 영상에서 단원 영역의 밝기 증가(그림 교체 + 빛)를 영상 프레임에서 찾아, 같은 시각 축에 놓은 예약 음 시각과 맞췄다(`tools/check_demo_sync.py`).

| 번쩍임 | 맞춘 것 | 그림 − 소리(중앙) | p10 ~ p90 | 최소 ~ 최대 |
|---|---|---|---|---|
| 37 | 37/37 | **+17 ms** | -5.9 ~ +35.8 ms | -14.2 ~ +69.1 ms |

해상도는 영상 1프레임(33 ms)이고 화면 녹화 프레임 시각의 흔들림이 더해진다. **영상에서 그림이 소리보다 평균 약 반 프레임 늦다**는 정도로만 읽는다. (영상의 소리는 페이지의 오디오 출력 지점을 녹음해 브라우저의 출력 시각표로 "들리는 시각"에 옮겨 붙인 것이다. 스피커로 직접 들은 것이 아니다.)

### 시험 중 고친 것
- 데모 영상의 처음 판은 그림이 소리보다 0.29초 빨랐다. 화면 녹화가 시작 구간보다 늦게 시작해 영상 시간축이 밀린 것이었다. 녹화를 1.2초 먼저 시작하고 첫 프레임을 구간 시작까지 늘렸다(`tools/lib/screencast-video.mjs`, 테스트 3개). 위 표는 고친 뒤 값이다.
- 처음 판 번쩍임은 흐릿했다(가산 셰이더 `_TintColor` 기본 회색 0.5). 값을 올리고 번쩍임·음 점 모양을 다듬었다.

## 5. 크기 (`tools/assemble_site.mjs` → `dist/site`, OpenDesk 한도 검사)

| 묶음 | 바이트 | MiB |
|---|---|---|
| Unity 빌드(`Build/`) | 3,644,921 | 3.48 |
| 이미지(`content/img/`) | 3,216,928 | 3.07 |
| 소리(`content/audio/`) | 1,145,554 | 1.09 |
| 그 밖(html, js) | 23,879 | 0.02 |
| **합계(34개 파일)** | **8,031,282** | **7.66** |

- 한도: 15 MiB, 파일당 5 MiB, 80개, 깊이 4, 허용 확장자. **전부 통과**(문제 0).
- 가장 큰 파일 `Build/downbeat.wasm.br` 2,845,838 B = **2.714 MiB**(5 MiB까지 2.29 MiB 여유), `data.br` 0.666 MiB.
- Day 0의 빈 Built-in 씬은 2.52 MiB였으니 앱 코드·VContainer·`link.xml`(어셈블리 전체 보존)로 **+0.19 MiB**.
- 참고(빌드 옵션): `DiskSizeLTO` 빌드는 `wasm.br` 2.582 MiB(합계 3.35 MiB, 비LTO는 3.49 MiB). 시험·측정·영상은 **비LTO**로 했고 LTO의 실행 속도는 안 쟀다. Brotli 11 재압축은 이득이 거의 없다(2.700 MiB).
- 이미지가 가장 크다(3.07 MiB, 기획서 예산 2.4 MB 근처보다 약간 큼). PNG 단원 6장이 2.9 MB.

## 6. 테스트

- **Unity EditMode 50개**(`measurements/unit-tests/editmode-results.xml`): 격자 슬롯(불규칙 400클릭이 정확히 슬롯 위, 최소 선행 유지), 밀기·버리기·파트별 최소 간격, 음계 10단계(A4 C5 D5 E5 G5 A5 C6 D6 E6 G6), 측정한 음 높이로 선택(리코더 A5 = A#5 -1반음)·튜닝 보정·소리 시작 보정, 번쩍임 대기열, 세기, 포인터 좌표 매핑, `content.json` 검증(저장소의 실제 파일 포함). Day 0의 `BeatGrid`·`FrameStats` 12개 포함.
- **Unity PlayMode 1개**(`playmode-results.xml`, 1.65초): Editor에서 실제 씬을 열어 저장소의 `content/`로 부팅하고 음 하나를 예약한다(배치 모드에서 오디오 시계가 돌았다).
- **Node 89개**(`npm test`): 페이지 오디오 21개(시계 6, `NotePlayer` 7, 엔진 8), 정합 필터 5, 사이트 한도 2, 영상 조립 3, `content.json` 생성 6, 그 밖 Day 0 것.
- 순수 로직 커버리지 숫자는 따로 재지 않았다(Core의 새 로직에 테스트가 붙어 있다).

## 7. 하지 않은 것 · 확인하지 못한 것

- **손 인식 입력·카메라**: Day 2. `IPlayInput`이 자리를 비워 두었다.
- **대기 자동 연주, 상태 머신, 안내 그림(`idle_hint`)**: 아직 없다. 이미지는 데이터에 있고 Day 1 화면에서는 읽지 않는다.
- **내용 교체·검증 되돌리기**: `ContentValidator`는 있으나 3단 되돌리기·폴링은 없다. 한 번 읽고 끝이다.
- **귀로 듣는 확인**: 영상의 소리는 오디오 출력 지점 녹음이고, 스피커로 직접 들은 느낌은 사용자가 확인해야 한다. 마림바·리코더의 **윗 옥타브**(샘플에서 최대 12반음 이동)는 소리 품질을 평가하지 않았다. 필요하면 `rootMidi`/`maxPitchShift`를 줄이거나 음 수를 5로 줄인다(데이터).
- **파트별 최소 간격**은 기획서(2/4/2)가 아니라 1/2/1로 뒀다(클릭 놀이가 답답하지 않도록, 데이터 값). 연주해 보며 정한다.
- **Editor Play 모드의 소리**는 배치 모드 테스트로만 확인했다(스케줄까지). 귀로는 못 들었다.
- Windows·Safari·Firefox, 키오스크 8시간, 공개 호스팅 게시(승인 전), 키보드 `A S D…`의 브라우저 실제 입력은 시험하지 않았다(좌표·키 매핑은 단위 시험, Shift 고정은 측정에서 사용).
- `outputLatency` 208 ms는 브라우저 추정치이고 실제 장치는 확인하지 않았다. 영상의 소리를 옮기는 데도 이 시각표를 썼다.
- 코드 정리 후보: `Day0Bridge.jslib`가 꺼진 채 `_Day0`에 남아 있다. 필요 없어지면 폴더째 지워도 된다.

## 8. 다시 해 보려면

```sh
# Unity (프로젝트 루트에서)
UNITY=/Applications/Unity/Hub/Editor/6000.3.9f1/Unity.app/Contents/MacOS/Unity
cd unity/Downbeat
$UNITY -batchmode -projectPath "$PWD" -buildTarget WebGL -executeMethod Downbeat.EditorTools.DownbeatSceneBuilder.Create   # 씬·머티리얼
$UNITY -batchmode -quit -projectPath "$PWD" -buildTarget WebGL -executeMethod Downbeat.EditorTools.WebBuild.Run \
  -dbScene Assets/_Project/Scenes/Main.unity -dbOut Builds/downbeat -dbProfile size -dbTemplate PROJECT:Downbeat
$UNITY -batchmode -projectPath "$PWD" -runTests -testPlatform EditMode -testResults ../../measurements/unit-tests/editmode-results.xml
$UNITY -batchmode -projectPath "$PWD" -runTests -testPlatform PlayMode -testResults ../../measurements/unit-tests/playmode-results.xml

# 사이트 조립 → 로컬 서버 → 브라우저 시험
cd ../..
node tools/assemble_site.mjs                                   # dist/site + 크기 보고
node web/serve.mjs --port 8780 --mount /=dist/site             # http://localhost:8780/ 에서 직접 해 볼 수 있다
node tools/run_day1_smoke.mjs                                  # 시작 → 클릭 → 스크린샷
node tools/run_day1_measure.mjs                                # 박 정확도·시각 vs 소리
node tools/record_demo.mjs && python3 tools/pick_screenshot.py docs/media docs/media/day1-screenshot.png
python3 tools/check_demo_sync.py docs/media/day1-demo.mp4 measurements/day1/demo/demo-meta.json
```

## 9. 만든 파일

- Unity: `Assets/_Project/Scripts/{Core,Audio,Input,Content,Views,App}/`, `Plugins/Web/Downbeat.jslib`, `Editor/DownbeatSceneBuilder.cs`(+`WebBuild.cs` 갱신), `Materials/GlowAdditive.mat`, `Scenes/Main.unity`, `Tests/EditMode/*`(7개 파일), `Tests/PlayMode/*`, `Assets/WebGLTemplates/Downbeat/{index.html, js/*}`, `Assets/link.xml`, `Assets/_Day0/`(옮긴 Day 0 코드)
- 웹 테스트: `web/test/{audio-clock,note-player,downbeat-audio}.test.mjs`, `fake-audio-context.mjs`
- 내용: `content/content.json`, `content/img/forest/*`(+`sprites.json`)
- 도구: `tools/assemble_site.mjs`, `build_content.mjs`, `measure_sprites.py`, `run_day1_smoke.mjs`, `run_day1_measure.mjs`, `record_demo.mjs`, `pick_screenshot.py`, `check_demo_sync.py`, `lib/{day1-browser,matched,content-build,screencast-video}.mjs`와 각 테스트
- 측정: `measurements/day1/{day1-measure,demo,smoke}/`, `site-report.json`, `build-size.json`, `measurements/unit-tests/*`
- 미디어: `docs/media/day1-screenshot.png`, `docs/media/day1-demo.mp4`
