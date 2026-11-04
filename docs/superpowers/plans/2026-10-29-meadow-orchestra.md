# 들판 오케스트라 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 웹캠 손동작으로 해 질 녘 3D 들판을 연주하는 Unity URP 네이티브 앱(Windows 전시용 화면 2대 + Mac 시연용)과 별도 손 추적 앱, 전시 운영 도구를 만든다.

**Architecture:** Python 손 추적 앱이 웹캠을 혼자 쓰고 손 좌표만 로컬 UDP로 보낸다. Unity 앱은 Day 0~2의 박자 격자·손짓 해석·녹화 재생·콘텐츠 검증을 그대로 쓰고, 소리는 네이티브 `PlayScheduled`, 화면은 URP 3D 들판(배경 그림 + 실시간 풀·꽃·입자)과 기록 화면으로 바꾼다. Python 감시 앱이 두 앱을 켜고 다시 켜며, Unity는 1분마다 상태 CSV를 남긴다.

**Tech Stack:** Unity 6000.3.9f1, URP, VContainer 1.19.0, UI Toolkit, HLSL·컴퓨트 셰이더 / Python 3.12, MediaPipe Hand Landmarker(Tasks API), OpenCV, pytest / PowerShell(Windows 설치·자동 실행) / Node(기존 분석 도구).

**Spec:** `docs/superpowers/specs/2026-10-29-meadow-orchestra-design.md` (이 계획은 설계 문서를 근거로 한다. 두 문서를 함께 읽는다.)

## Global Constraints

- Unity 6000.3.9f1 고정. 렌더는 URP. Windows 빌드는 Mac에서 Windows(Mono) 모듈로 만든다(IL2CPP 아님). `-nographics`는 쓰지 않는다(Day 0에서 이 프로젝트 첫 임포트가 죽었다).
- 실행 중인 다른 Unity 에디터(ProjectH)와 unityMCP 도구는 건드리지 않는다. Unity는 이 프로젝트 경로로 batchmode CLI만 쓴다.
- 곡 값은 `content/content.json`의 `song`을 따른다: 96 BPM, `slotsPerBeat` 4, `beatsPerBar` 4, A단조 5음 음계, `pitchSteps` 10.
- 손 추적 앱은 Python 3.12. `mediapipe`는 macOS arm64와 Windows amd64 휠이 모두 있는 0.10.x 중 하나로 정확히 고정한다.
- 카메라 영상은 손 추적 앱 밖으로 나가지 않고 어디에도 저장하지 않는다. UDP에는 손 좌표만, 기록 카드에는 들판 렌더만 들어간다.
- 핵심 표현(풀, 바람, 꽃, 바람 줄기, 셰이더)은 직접 만든다. CC0·공개 라이선스 에셋은 출처를 `NOTICE`에 적고 쓴다. Unity Asset Store 에셋은 쓰지 않는다.
- 화면에 보이는 글은 한국어. 들판 화면 구석에 "개인 시제품 · 실제 전시 설치 아님"을 작게 둔다.
  - (2026-10-30 덧붙임) 사용자가 이 구석 글이 필요 없다고 해서 앱에서 뺐다(7f9c202). 앱 화면에는 이제 이 글이 없다. 글을 그리던 코드와 장면 물체, 내용의 `meadow.overlay`를 지웠고, 기록 화면이 같이 쓰던 글꼴 목록은 `record.fontNames`로 옮겼다. 화면 한 개일 때 기록 띠의 점수와 자연 줄은 글 자리를 비우던 여백 없이 아래 여백부터 시작한다(`measurements/visual/notice-removed.jpg`). 영상·포트폴리오·README 같은 결과물은 글로는 여전히 개인 시제품이라고 밝힌다.
- 손이 사라지거나 손 추적 앱이 끊기면 5초 안에 대기 흐름으로 간다.
- 성능 목표: Windows 전시 PC에서 들판 1920×1080 60fps(기록 화면 포함). 화질 단계는 데이터(`meadow.quality`)로 둔다.
- 잰 숫자만 문서에 쓴다. 원본은 `measurements/`, 만든 스크립트는 `tools/`.
- 푸시·공개 게시 금지. 커밋은 사용자가 허락한 경우에만, 로컬에서.
- 모든 조정 값은 `content.json`에 두고 코드에 숫자를 박지 않는다(아래 각 Task의 "초기값"은 `content.json`의 기본값이다).

## Review Focus

- 손 추적 앱이 Unity가 켜진 채로 다시 시작되어 순번(`seq`)이 0부터 다시 오는 경우 → Unity가 새 흐름으로 받아들이고 손 입력이 계속 된다. (Task 4 테스트)
- 두 손이 서로 다른 구역에서 같은 박에 연주하는 경우 → 두 악기가 모두 예약되고, 한 손이 사라져도 다른 손은 계속 된다. (Task 7 테스트)
- Windows에서 모니터가 1대만 연결된 채 시작하는 경우 → 기록 화면이 들판 위 겹침 모드로 바뀌고 앱은 정상 동작한다. (Task 10 테스트)
- 8시간 구동 중 자정이 지나는 경우 → 카드 저장 폴더가 새 날짜로 바뀌고 기록 화면이 비워지며 오류가 없다. (Task 11 테스트)
- 새 PC에 처음 설치해 실행 파일 옆 `content` 폴더가 없거나 깨진 경우 → 앱 안에 든 기본 콘텐츠로 시작하고 이유를 로그에 남긴다. (Task 12 테스트)

## 검토 1 후속 (2026-10-29, Tasks 1~4 검토 결과)

검토 판정은 "Task 5로 진행". 아래는 Task 5 앞에서 함께 처리한다.

- 작은 고침(Task 5 첫 커밋 전 `fix:` 커밋 하나): `UdpHandSource` 순번 되감기는 `seq < 16` 또는 `seq < lastSeq − 64`일 때만 새 흐름으로 보고 나머지 작은 역전은 늦은 패킷으로 버림, 프레임당 꺼내는 수를 큐 크기로 제한, `UdpSocketReceiver` 수신 실패 경고 빈도 제한, `VoicePool`은 시작 전인 보이스를 빈 것으로 보지 않음(`start ≤ now`), `NativeAudioEngine`은 자른 재생 속도를 백엔드에 넘김, 추적 앱 `--host` 제거(루프백 고정), pytest는 고정 패킷을 다시 쓰지 않고 같은지 확인(다시 만들기는 명시적 옵션), `tools/unity_test.sh`는 추적되지 않는 경로에 XML을 쓰고 통과 때만 `measurements/unit-tests/`로 복사, `measurements/native-clock/summary.md`의 멈춤 근거 문장을 실제 필드(`rafIntervalsMs.max`, `maxDspStallSec`, W5의 `framesOver100ms`)로 고침.
- 소리 듣는 귀(AudioListener)는 소리 엔진(`UnityAudioBackend`)이 하나만 가진다. 장면 카메라에는 붙이지 않고, Task 5 PlayMode 테스트에서 장면 안 AudioListener가 정확히 1개인지 확인한다.
- `PlayerSettings.companyName`을 `ovthemoon`으로 고정한다(Task 11 카드 저장 위치가 이 값을 따른다).
- 전시 화면 방식(테두리 없는 전체 화면 vs 독점 전체 화면)은 10/29 밤 Windows 박자 시계 측정(`docs/windows-check-2026-10-29.md` 3절) 결과를 보고 Task 10에서 정한다.
- Task 7 구현 메모(2026-10-29): 노트가 어디서 쳤는지(`X01`)를 `NoteIntent`·`ScheduledNote`·`HandStrike`에 실어 보낸다(손 x, 마우스 x; 모르면 NaN → 악기 구역 가운데). `MeadowNoteRouter`는 순수 로직이라 Core에 있다(`FlowerField`가 `IBloomTarget`). 꽃 수는 `meadow.quality[].flowerCount` 650(High)/260(Low)로 정했다(계획의 초기값 1800/700은 화면이 솜뭉치처럼 빽빽해졌다). 꽃은 `meadow.flowers.bloom.leadSec` 0.075(계획 0.05)에 피기 시작한다: 오픈 곡선이 처음에 빠르고(톡) 소리 시각에 반쯤 열리도록 맞춘 값이며, 눈이 따르는 시계는 오디오 블록(21 ms) 계단을 없앤 `IAudioEngine.VisualNow`(`SmoothClock`)다. 측정은 `measurements/day3/bloom-sync.md`.
- Task 7b 꽃 모양 다듬기(2026-10-29): 꽃은 무리(`FlowerPlacement.Drift` 잡음)로 서고 높이가 제각각이며 종 경계는 약 10 %(`blend` 0.1)에 걸쳐 들쭉날쭉 섞인다(어떤 악기가 울리는지는 여전히 노트의 x가 정한다). 봉오리는 절반 크기에 꽃받침(초록)이 감싸고, 빈 들판 힌트로 `budGlow` 0.22(이전 0.45의 절반). 꽃 수 High 480 / Low 190. 손 바람은 중심에서 0이고 반지름의 1/3 지점에서 가장 센 매끈한 부채꼴(`handPush` 0.6).
- Task 8 구현 메모(2026-10-29): 입자·바람 줄기 값은 모두 `meadow.particles`·`meadow.ribbon`(계획의 0.6초·0.4 m·0.5초 그대로)과 `meadow.quality[].fireflyMax`(High 80 / Low 30)에 있다. `ParticleCues`, `HandRibbonFade`는 순수 Core 로직이고 `WindField.SampleVector`가 입자가 따라갈 바람 방향을 준다(7b에서 추가). 반딧불은 땅 위 0.25~1.1 m에 한 마리씩 나고(상자 방출은 언덕·하늘로 빠졌다), 꽃가루는 항상 약간 떠 있다. `--fireflies L`, `--log-bloom-sync`처럼 캡처용 실행 인자만 늘었다. 성능은 `measurements/perf/particles-mac.json`(손 2개·박자마다 노트·반딧불 1단계).
- Task 9 구현 메모(2026-10-29): 에너지·흐름·악보는 Core(`Scripts/Core/Nature/`)의 순수 로직이고 값은 `content.json`의 `nature`(`energy`: 0.06 + 0.06·속도, 반감기 6초, 단계 0.2/0.45/0.7, 히스테리시스 0.05; `rules`; `leadBars` 1; `seed`)와 `experience`에 있으며 `NatureContentValidator`가 검사한다. 자연 소리는 내려받지 않고 `tools/prepare_nature_samples.mjs`(`tools/lib/nature-synth.mjs`, 난수 없음)가 합성한다: 종소리 5개(A5 C6 D6 E6 G6, 2.4초), 지속음 4개(A3 C4 E4 G4, 5.6초, 어택 0.4초), 낮은 북 1개(A2, 1초), 16비트 모노 32 kHz, 합계 2.2 MB. 피치는 구성상 정확하고(`centsFromLabel` 0) 합성 뒤 10센트 이내인지 측정으로 확인한다. `NatureAudioBridge`(App)는 자연 음을 1마디(96 BPM에서 2.5초) 앞서 소리 시계에 올리고 이미 지난 음은 떨어뜨린다(`Dropped`); 손 연주 음은 0.1초 그대로다. 자연 악기는 손 악기보다 −6 dB(`gainDb`). `NatureVisuals`(Meadow)는 하늘(`SetSunset(에너지)`)과 반딧불을 `visualSmoothSec` 2.5초로 에너지에 부드럽게 따르게 하고, 북이 들릴 때 먼 언덕 안개를 밝히며(셰이더 `_MeadowHillPulse`), 사람이 있는 동안 박마다 Sweep을 넣는다. 사람이 있는지는 `HandPresenceMonitor`가 정한다: 손 프레임이 0.5초 넘게 안 오면(끊긴 카메라, 끝난 녹화) 마지막 프레임에 손이 있어도 손 없음이고, 마우스·키보드 음은 `experience.inputPresenceSec` 2초 동안 손으로 친다. 캡처·측정용 실행 인자: `--hold-energy V`, `--log-experience PATH`, `--replay-loops N`, `--record-dir DIR --record-seconds N [--record-fps F] [--record-size WxH] [--record-frames 0] [--record-silent]`. 손으로 하늘·반딧불을 고정하는 `--sky`, `--fireflies`와 테스트는 `NatureVisuals.ControlsSky/ControlsFireflies`를 끈다.
- Task 9가 계획과 다른 점: (1) `NatureLayerConductor(NatureRules, slotsPerBar, pitchSteps, seed)`: 계획의 `(BeatGrid grid, slotsPerBar, seed)`와 달리 격자를 받지 않는다(악보는 슬롯 번호와 시드만으로 정해지고, 규칙과 음계 크기는 `content.json` 값이다). `Plan(from, to, level, output)`은 같다. (2) 지속음 4초 → 5.6초: 화음이 2마디(5초)마다 바뀌어도 다음 화음이 오르는 동안 끊기지 않게. (3) 실행은 60초가 아니라 100초: 마지막 음 뒤 손 없음 3초에 에너지가 0.05로 내려가는 약 20초가 더해져 Idle은 80.4초에 온다. (4) 녹화 재생 3번은 파일을 이어 붙이지 않고 `--replay-loops 3`으로 돌렸다. (5) 녹화 사이 1초 쉼과 마디 맞춤으로 손이 3.4초 없어져 Playing → Settling → Playing이 한 번씩 보인다(같은 방문, 새 세션 아님: `sessions` 1). (6) `PerformanceEnergy.Set`, `HandPresenceMonitor`, `NatureMapper`(`SamplePicker`를 `PitchMapper`와 나눔), `NatureVisualModel`, `BeatTicker`, `RunRecorder`는 계획에 없던 추가다.
- Task 9 결과(2026-10-29, Mac 플레이어 창 1280×720, 사진·영상은 1920×1080 오프스크린): 녹화 재생 3번(`measurements/day4/experience-run.json`)에서 에너지 단계 1은 3.05초, 2는 6.02초, 3은 11.0초에 오르고(최고 에너지 0.94, 손 음 42개, 자연 음 132개, 떨어진 음 0), 마지막 손이 사라진 뒤 Settling 60.1초, Idle 80.4초(방문 1회). 소리: `AudioOutputTap`으로 받은 100초 WAV(16비트 48 kHz, 최종 믹스의 첫 채널) 최대 −6.0 dBFS, 클리핑 샘플 0, 이웃 샘플 사이 0.10 이상 점프(클릭) 0(최대 0.076). 30초 미리보기 MP4(1920×1080 24 fps, 19.6 MB)에서 북이 들릴 때 언덕 안개가 켜지는 순간과 소리의 차이를 MP4 안에서 7쌍 쟀더니 중앙값 +4 ms(−37 ~ +19 ms, 한 프레임 41.7 ms 이내)(`measurements/day4/video-sync.json`). 성능(1920×1080 창, 녹화 재생 반복 + 에너지 0.85 고정으로 세 층 모두 재생): High 394 fps(프레임 p95 4.3 ms), Low 1045 fps(p95 1.5 ms). 자연 층 전(`particles-mac.json`)은 428 / 1118 fps(`measurements/perf/nature-mac.json`). 단계별 캡처 `measurements/visual/task9-levels.jpg`(왼쪽 위부터 에너지 0.05 / 0.3 / 0.55 / 0.85). 종소리·지속음·북의 크기 균형과 손 악기와의 섞임은 귀로 듣는 판정이 필요하고 아직 하지 않았다.
- Task 13 구현 메모(2026-10-29, 10~11번 작업보다 먼저, 밤샘 구동 때문에): `HealthAccumulator`/`HealthRow`는 Core(`Scripts/Core/Ops/`), 파일 쓰기(`HealthFileWriter`)와 주기(`HealthLogger`, 행 60초, 하트비트 5초)는 App. 계획의 열 18개에 `max_dsp_stall_s`(두 프레임 사이 오디오 시계가 한 번에 간 가장 큰 걸음: 검토 1 후속의 `maxDspStallSec`와 같은 정의), `working_set_mb`(프로세스 메모리), `nature_dropped`, `sessions`를 더해 22개다. 0.1초가 넘은 프레임은 `stalls-yyyyMMdd.csv`에 한 줄씩(시각, 가동 시간, 프레임 ms, 그 프레임의 오디오 걸음; 파일당 2000줄까지)이고, 로딩 직후 첫 2프레임은 뺀다. `notes_scheduled/late`, `voices_stolen`, `errors`는 한 행 동안 늘어난 수다. `Snapshot`은 계획의 서명 그대로이고 실행 상태(체험 상태, 에너지 단계, 방문 수, 메모리)는 `WithRun`으로 붙인다. 폴더를 못 만들면 `persistentDataPath/logs`로, 그것도 안 되면 경고 한 번 뒤 기록 없이 계속 돌아간다. 플레이어는 실행 파일 옆 `logs/`에 쓰고 에디터는 `--health-dir`가 있을 때만 쓴다. 앱을 끌 때 마지막 부분 행을 쓴다. `tracker_age_ms_p95`는 손 프레임의 나이를 알려 주는 입력이 아직 없어 비어 있다(녹화·UDP 입력 모두; 추적 앱 작업에서 채움). 밤샘용 인자: `--soak`(= `--input replay`, 기본 녹화 파일, `--mute`, `--replay-gaps 1,5,35`, 건강 기록 폴더 `measurements/soak-yyyyMMdd`; 명시한 인자가 이긴다), `--replay-gaps A,B,C`(한 번 재생한 뒤 쉬는 시간을 순서대로 돌림: 1초는 같은 방문이 이어지고, 5초는 가라앉았다 다시 깨고, 35초는 에너지가 식어 Idle까지 간다), `--health-dir PATH`, `--health-interval SEC`, `--allow-sleep`. 켜 둔 PC가 잠들지 않게 Windows 플레이어는 앱이 도는 동안 `SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED)`로 부탁하고(60초마다 다시, 종료 때 해제) 설정은 바꾸지 않는다. 다른 플랫폼과 Windows 에디터는 건드리지 않는다(`KeepAwake`, 테스트로 확인). Mac 창 640×360 `--soak --health-interval 10` 140초 실행: 행 14개(마지막은 종료 때 쓴 부분 행), fps 평균 119.3~119.8, 가장 긴 프레임 26 ms, 0.1초 넘은 프레임 0, `max_dsp_stall_s` 0.021, 방문 2회, Idle 82.6초와 다시 깨어남 95.5초(한 바퀴 95.0초: 재생 3번 16.1초씩 + 쉬는 1·5·35초 + 마디 맞춤). `measurements/day4/health-mac-soak-test.csv`.
- Task 10·11 구현 메모(2026-10-29, 두 작업을 한 커밋에 담았다: 같은 화면이 카드를 그린다): 기록 화면은 UI Toolkit이 아니라 코드로 만든 메시와 TextMesh(운영체제 한글 글꼴, 들판 구석 안내문과 같은 방식; 안내문은 2026-10-30 뺐다, 22행 참고)로 그린다. 앱에 UI 패키지가 없고(UGUI 없음) PanelSettings·테마 에셋 없이 들판과 같은 카메라 경로로 화면 밖 그림을 뽑을 수 있어서다(`Scripts/Record/`: `RecordScreenView`가 카메라·메시·글자를 맡고 `RecordPainter`가 메시·글자·썸네일 목록을 채운다, 쉐이더 `Resources/Record/RecordQuad`). 계획의 `RecordScreen.uxml/uss`, `Painter2D`, `DisplayRouter` 안의 카메라 처리는 이렇게 바뀌었다. 꽃 아이콘 3종(초롱꽃·민들레·양귀비, 색은 `meadow.flowers`의 핀 색)은 `FlowerIconPainter`가 화소마다 모양을 계산해 작은 텍스처 한 장에 그린다. 순수 로직은 Core(`Scripts/Core/Record/`): `DisplayPlan`/`DisplayAssignment`, `ScoreModel`/`ScoreLayout`(용량 256, 창 8초; 자연 악기 반짝임과 합류 시각도 가진다), `RecordLayout`(1080 기준 비율), `SessionRecorder`/`SessionCard`/`CardStore`, `CardText`. 값은 `content.json`의 `display`(`mode` auto, `meadowIndex` 0, `fullscreenMode` borderless, `overlayFraction` 0.28)와 `record`(창, 용량, 카드 수 6, 최소 음 3, 카드 점 64, 날짜 확인 60초, 색, 한국어 문구)에 있고 `RecordContentValidator`가 검사한다. **전체 화면 방식**(독점/테두리 없음)은 Windows 측정 뒤에 정하므로 내용 설정(`display.fullscreenMode`, 실행 인자 `--fullscreen-mode`)으로만 두었고 기본은 borderless다: Windows 플레이어가 전체 화면으로 시작했을 때만 적용하고, 창으로 시작하면 창으로 둔다. 화면 두 개 이상이면 들판은 `display.meadowIndex`의 화면, 기록은 다른 화면(`Display.Activate`, 기록 카메라 `targetDisplay`)이고, 한 개면 기록은 들판 카메라 앞 1 m에 걸린 아래 28 % 띠이며 Tab으로 숨긴다(`--display auto|dual|single`로 덮어씀, 화면 한 개는 늘 띠). 띠는 불투명이다: 반투명으로 하면 뒤쪽 반딧불의 HDR 빛이 번짐(Bloom)으로 비쳐 보인다. 들판 구석 안내문(`개인 시제품 · 실제 전시 설치 아님`)은 띠 위로 올라오도록 그리기 순서를 높였고, 띠의 점수·자연 악기 줄은 그 자리를 비워 둔다. (2026-10-30 뺐다, 22행 참고: 안내문도 그 자리 여백도 이제 없다.) 카드: 방문이 끝나면(Idle 복귀) 음 3개 이상일 때만 카드 한 장(JSON + 480×270 PNG)을 `persistentDataPath/cards/yyyy-MM-dd/`(에디터는 임시 캐시, `--cards-dir`로 바꿈)에 쓰고, 썸네일은 방문 중 에너지 단계가 새 최고가 될 때마다 들판 카메라를 한 번 그린 것의 마지막(띠와 카메라 영상 없음)이다. 카드에는 시각, 연주 시간(첫 손에서 마지막 음까지), 음 수, 합류한 자연 악기 수, 점수 64점이 들어가고 사람을 알아볼 수 있는 것은 없다(시험으로 확인). 날짜는 방문이 시작된 날이고 `dateCheckSec` 60초마다 확인해 날짜가 바뀌면 빈 하루로 시작한다. 계획과 다른 점: (1) 화면에는 최신 12장이 아니라 **최신 6장 한 줄**(`record.cardsShown` 1~12, 12는 두 줄): 2~3 m에서 읽히도록 카드를 크게 하려고(2) 카드 1장은 1080 기준 약 285×234 px(썸네일 16:9 + 시간·자연 악기 수 글자). (3) `RecordCoordinator`(App)가 점수·방문·카드 연결을 맡는다. (4) 실행 인자: `--display`, `--no-record`, `--cards-dir`, `--record-offscreen`(기록을 1920×1080 그림에 매 프레임 그림: 두 번째 화면 비용 측정용), `--capture-screens PATH`(두 화면을 3840×1080 한 장으로).
- Task 10·11 결과(Mac 플레이어, 1920×1080 창, 녹화 재생 반복 + 에너지 0.85 고정): 기록 없이 High 394.8 fps(프레임 p95 4.1 ms)·Low 1131.1 fps, 띠(화면 한 개) High 375.8 fps(p95 4.4 ms)·Low 1023.9 fps, 띠 + 1920×1080 두 번째 화면 그림 High 371.1 fps(p95 4.4 ms)·Low 972.2 fps(`measurements/perf/record-*-mac.json`). 두 번째 화면은 실제 모니터가 아니라 같은 크기의 그림에 그린 근사다(이 Mac에는 한 화면뿐). 캡처 `measurements/visual/task10-screens-playing.jpg`(들판 | 기록 나란히, 연주 중, 카드 2장)와 `measurements/visual/task10-single.jpg`(한 화면 띠). 이 Mac에서 Unity는 화면을 3개로 보고하므로(`Display.displays.Length`) 인자 없이 켜면 auto가 두 화면 방식이 되어 창에는 띠가 안 보인다: 이 Mac에서는 `--display single`을 쓴다. Windows 두 화면 동작과 전체 화면 방식 비교는 Task 14의 Windows 세션에서 본다(아직 시험 못 함). Tab 키로 숨김·보임은 코드(`DisplayRouter.ToggleStrip`)만 시험했고 키 입력은 눈으로 보지 않았다.
- 번짐(Bloom) 수정(2026-10-29, 체크포인트 3 뒤): **그때까지의 모든 화면 승인은 창과 다른 그림을 본 것이었다.** `MeadowCapture`는 8비트 렌더 텍스처에 MSAA 없이 그렸고, 그 그림에는 Bloom이 거의 없었고(HDR이 잘림) 풀잎이 가늘었다(풀은 alpha-to-coverage라 MSAA가 필요하다). 실제 창에서는 일몰 하늘 단계의 햇빛 받은 풀끝과 이슬 반짝임(HDR 수십)이 Bloom으로 번져 들판 아래쪽이 흰 덩어리로 뒤덮였다. 사용자가 반딧불이라 본 덩어리는 반딧불이 아니었다: 반딧불을 끄고(`--fireflies 0`) 찍어도 같았다. 수정: (1) `MeadowCapture.Render`가 창과 같은 길을 간다(반정밀 선형 HDR 대상, 활성 파이프라인 에셋의 MSAA, 후처리, 그래픽 카드의 sRGB 인코딩). `--capture-compare PREFIX`(`WindowCompare`)가 같은 프레임에서 창의 뒷버퍼(`ScreenCapture`)와 이 그림을 저장하고 평균 절대 차이를 로그로 남긴다: High 1920×1080 0.22, Low 1920×1080 0.21, High 1280×720 0.38, Low 1280×720 0.24 (255 중, 이전 방식은 29~33). (2) Bloom: 문턱 1.0→1.2, 세기 0.22→0.3, 퍼짐 0.65→0.35, **한계(clamp) 65472→6**(`UrpSetup`; 한 화소가 Bloom에 주는 양의 상한). (3) 반딧불: 입자 꼭짓점 색은 8비트라 1을 넘지 못해 `fireflyGlow`가 아무 효과가 없었다. 이제 쉐이더의 `_Gain`(재질)이 HDR 배율이고(`fireflyGlow` 9), 빛 점 모양(`SoftDiscTexture.CreateGlow`)에 따뜻한 흰 노랑 `#e6ff8a`, 크기 0.07 m, 한 번에 High 300 / Low 100마리까지(`fireflyMax`). (4) 보는 법: `tools/bloom_metrics.py`(들판 부분에서 가장 넓은 번짐의 폭, 흰색으로 날아간 화소 비율, 지역 대비), 실행 인자 `--bloom-threshold/-intensity/-scatter/-clamp`(이 실행만 값 바꿔 보기). 결과(1920×1080, 들판, 에너지 단계 3 / 연주 중): 가장 넓은 번짐 5.3 % → 1.6 % / 14.9 % → 4.4 %, 1.5 %보다 넓은 번짐 20 → 3 / 39 → 18(남은 것은 손이 가른 풀의 햇빛 받은 자리), 흰색으로 날아간 화소 3.2 % → 1.0 % / 5.9 % → 1.5 %, 지역 대비 11.5 → 21.0 / 12.0 → 23.5. 들판 평균 밝기는 107 → 68(번짐이 들판을 뿌옇게 올리던 만큼 어두워졌다). 시험: PlayMode `GlowPlayModeTests`(해 질 녘에 반딧불이 모두 나온 들판에 덩어리가 없고 Bloom이 한계로 눌려 있음; 이전 Bloom 프로필로 돌리면 둘 다 실패하는 것을 확인). 창 캡처 전후 `measurements/visual/firefly-fix-before-after-window.jpg`, 수치 `measurements/visual/bloom-metrics-before-after.json`. 예전 `task5/6/6b/6c-compare.jpg`는 MSAA·Bloom 없는 그림이라 승인 근거로 쓰지 않는다(기록으로만 남김). `task7b-bloom`, `task7b-flowerbed-crop`, `task8-fireflies`, `task9-levels`, `task10-*`는 새 방식으로 다시 찍었다.
- Task 14 구현 메모(2026-10-30): 감시 앱은 `supervisor/downbeat_supervisor`(표준 라이브러리만, Python 3.12)이고 손 추적의 가상 환경 Python으로 돈다. 규칙: 꺼지면 3초 뒤 다시 켬, 앱의 `heartbeat.txt`가 30초 넘게 멈추면(메인 스레드가 얼면 멈춘다) 끄고 다시 켬, 시작 뒤 90초 동안은 하트비트가 없어도 봐줌(앞 실행이 남긴 낡은 하트비트는 이번 실행의 것으로 치지 않음), 10분 안에 5번 넘게 다시 켜면 60초 쉼. 기록은 `logs/supervisor-yyyyMMdd.csv`(`timestamp_utc,process,event,detail`). 검토 3의 제안대로 **소리 장치 소실 신호**를 더했다: 하트비트 둘째 줄이 `audio=ok|stalled`(오디오 시계가 3초 넘게 안 움직이면 stalled)이고 20초 넘게 stalled면 앱을 다시 켠다. 감시가 못 보는 것: 하트비트는 계속 뛰는데 그림이 까맣거나 깨진 것, 기록 화면이 엉뚱한 모니터에 뜬 것(상태 CSV도 얼음 감지기가 아니다). 계획과 다른 점: (1) 스크립트는 `ops/windows`가 아니라 `tools/windows/`(다른 에이전트가 만든 손 추적 설치 스크립트 옆, 이름·경로 그대로 둠)에 있고 `setup.ps1`은 따로 만들지 않았다(`setup-hand-tracker.ps1`이 가상 환경과 모델을 맡고, 감시 앱은 같은 Python을 쓴다). `env-report.json`은 `collect-results.ps1`이 모을 때 만든다(GPU·드라이버, 모니터, 카메라·소리 장치 이름, Python, 디스크). (2) 자동 시작은 작업 스케줄러가 아니라 **시작 프로그램 폴더의 바로가기 1개**(`install-autostart.ps1`, Y 질문, `uninstall-autostart.ps1`로 지움): 로그온 트리거 작업은 관리자 권한을 요구하는 경우가 많아서, 관리자 권한과 시스템 설정 변경 없이 되는 쪽을 골랐다. 자동 로그인은 건드리지 않는다. (3) 새 스크립트: `run-supervised.ps1`(`-Camera -Backend -Mouse -Replay -Soak`), `stop-supervised.ps1`, `install-autostart.ps1`, `uninstall-autostart.ps1`, `collect-results.ps1`, 공용 함수 `_supervisor-common.ps1`(기존 `_common.ps1`은 고치지 않음). (4) 순서표는 `docs/windows-runbook.md`(zip 안에서는 `README-runbook.txt`): 세션 A(설치, 화면 2대 확인, 두 화면 캡처, 전체 화면 방식 비교), B(고장 6가지와 되돌리는 명령), C(8시간), D(지연 측정은 Task 15에서 채움). B4·B5는 외부 콘텐츠 되돌리기(Task 12)가 들어가기 전이라 앱이 못 켜지는 것이 정상이라고 적었다. (5) `ops/mac/run-demo.command`: 카메라가 있으면 손(`system_profiler`로 카메라를 열지 않고 확인), 없으면 `--input replay`. (6) `tools/package_windows.sh`가 감시 앱·새 스크립트·순서표도 담는다(전체, `--tracker-only` 모두). **PowerShell 스크립트는 이 Mac에서 돌려 보지 못했다**(PowerShell 5.1 문법만 쓰고 UTF-8 BOM, 따옴표 친 경로; 괄호·BOM·PS7 문법을 훑는 간이 검사만 통과). 감시 앱 자체는 Mac에서 실제 플레이어로 확인했다: 플레이어를 죽이면 3초 뒤 다시 켜지고(`exit code=-15`), 프로세스를 얼리면(SIGSTOP) 하트비트가 30초 멈춘 뒤 `heartbeat_stale` → 다시 켬, Ctrl+C로 플레이어도 같이 끝남.
- 바람·배경 다듬기(2026-10-30, 사용자: "배경이 정지해 있어 지루하다, 풀이 파들파들 떨린다"): **떨림의 원인**은 바꾸기 전 빌드에 `--wind-mute`와 `--frame-dump`(Unity 시계를 1/20초씩 세워 같은 그림을 같은 순서로 뽑는다)만 더해 항목별로 끄고, 10초(1920×1080, High, 그림 아래쪽 절반)를 비교해 찾았다(`tools/wind_metrics.py`, 값은 `measurements/visual/wind-shiver-before-after.json`). (1) 블레이드마다 다른 위상(`phase * 2.0`): **대기 상태의 원인**. 셰이더 식으로 계산하면 10 cm 떨어진 두 블레이드의 휨 차이 ÷ 휨 크기의 중앙값이 0.36, 90백분위가 1.54(위상을 같게 하면 0.08/0.17)이고, 화면에서 위상을 끄면 국소 이동으로 설명되지 않는 변화 비율이 0.825→0.737. (2) 장마다 도는 방향: 주원인은 아님(끄면 그림 변화 −9%, 설명 안 되는 비율은 그대로). 다만 1 m 떨어지면 방향이 25°, 3 m에서 74° 달라 "한 방향"이 없었다. (3) 박마다 오는 파도(22 m/s·폭 1.8 m라서 한 블레이드 위에 0.16초, 0.625초마다): **연주 중의 원인**. 박 파도와 음 고리를 켜면 그림 변화 25.8→46.3(+79%), 이동 장 거칠기 0.9→4.5 px. (4) 이슬 반짝임: 아님(끄면 25.80→25.79). 대기 상태에서 그림이 변하는 것은 전부 산들바람 몫이다(끄면 25.8→1.0). 덤으로 찾은 것: `_Time.y`가 float라 3일쯤 켜 두면 한 걸음이 한 프레임이 된다 → 새 바람은 double 시계에서 작게 감은 숫자만 받는다(400일 값으로 시험).
  **새 바람**(`meadow.breeze`, 검증 범위 포함. C# `BreezeModel`이 기준이고 HLSL `MeadowBreeze.hlsl`이 같은 식이다; GPU와 CPU를 견주는 `BreezeGpuParityPlayModeTests`): 한 방향(32°: 오른쪽 안쪽)으로 풀이 살짝 눕고(lean), 그 둘레로 천천히 흔들리며(6.5 m 파장, 3.7초), 폭 약 4 m의 큰 돌풍이 평균 4.6초마다(간격 11 m, 2.4 m/s) 들판을 굴러간다(돌풍마다 세기와 자리가 해시로 달라 규칙적이지 않다. 앞이 가파르고 뒤가 완만하며 뒤에서 22%만큼 되돌아 흔들리고, 줄기 끝은 뿌리보다 1.1 m 늦게 만난다). 블레이드마다 다른 것은 뻣뻣함(0.8~1.2배)과 끝의 작고 느린 움직임(0.43 Hz)뿐이다. 이웃(10 cm) 두 블레이드의 휨 차이는 중앙값 0.127(전 0.36), 90백분위 0.286(전 1.54)이고 100%가 같은 쪽으로 눕는다(`BreezeModelTests`). 꽃도 같은 바람을 받는다. 박 파도는 22→7 m/s, 폭 1.8→2.6 m(한 블레이드 위에 0.74초), 음 고리는 9→4.5 m/s(1.16초)로 바꿨다: 그림 변화 중 파도 몫 20.5→14.3(−30%). 돌풍과 구름 그림자는 프레임마다 128×96 m를 256×192칸(반 미터)에 한 번만 계산해(`BreezeField`, 최대 60 Hz) 풀·꽃·땅·먼 그림이 읽는다(각자 계산하면 High가 237 fps까지 내려갔다).
  **살아 있는 배경**(`meadow.sky.clouds`, `meadow.breeze.plate`): 돌풍 줄무늬가 판(plate)에 그려진 먼 들판까지 이어진다(판의 그림 행을 땅 위의 점으로 바꿔 같은 장을 읽는 곱셈 값이라, 3D 땅이 판으로 번지는 띠에서도 이음새가 없다). 하늘에는 구름 2층(높이 22 m와 48 m, 같은 속도 2 m/s라서 낮은 층이 더 빨라 보인다)이 바람 방향으로 흐른다. 구름은 정거장마다 정한 색조와 밝기만 바꾸고 판 하늘의 평균 밝기는 +4~10% 안에 둔다(세 장의 판과 하늘 호의 교차 전환을 따른다). 같은 속도와 방향의 큰 구름 그림자가 들판과 판 위를 미끄러진다. 씨앗·꽃잎·꽃가루도 바람 방향으로 천천히 흐른다(`particles.windDrift`). 새 떼(선택)는 만들지 않았다. 기록용으로 `--replay-delay SEC`(첫 재생 전에 가만히 있는 시간)를 더했다.
  **측정**: 배경 변화(대기 상태, 10초, 회색 단계): 하늘 0.03→3.0, 먼 들판 0.01→3.9, 언덕 0.02→1.1(전→후). 속도(Mac, 1920×1080 창, 녹화 재생 반복): High 366.5→323.4 fps, Low 972.1→813.1 fps(`measurements/perf/wind-clouds-mac.json`). Windows 노트북(Intel UHD)에서는 재 보지 못했다. 한계: 풀이 아주 가늘어서 어떤 움직임이든 그림 변화 지표가 크게 나오므로, 이웃 블레이드가 함께 움직이는지는 휨 벡터 통계가 직접 증거이고 화면 지표(설명 안 되는 비율 0.825→0.753)는 보조다. 영상은 `captures/wind/wind-clip-28s.mp4`(가만히 12초, 이어서 연주; git 밖).
- Task 12 구현 메모(2026-10-30): 앱은 켤 때 콘텐츠를 ① `Downbeat.exe`(Mac은 `.app`) 옆 `content/` → ② 쓸 수 없으면 `last-known-good/`(`Application.persistentDataPath`) → ③ 없으면 앱에 든 `StreamingAssets/default-content/` 순으로 고른다(`ContentLocator.Resolve`, 시험은 메모리 속 가짜 파일 시스템 `FakeFileProbe`). 폴더가 쓸 만한지는 `ContentFolderValidator`가 본다: JSON이 읽히는지, 범위·중복 id(`ContentValidator`), content.json이 가리키는 소리·그림 파일이 모두 있는지, 그리고 **파일이 온전해 보이는지**(`MediaFileCheck`, 디코딩 없이 바이트만: WAV는 RIFF/fmt/data 조각과 data 크기가 파일 길이를 넘지 않는지, PNG는 IEND, JPEG는 FF D9 끝). 문제는 첫 번째만이 아니라 전부 보고한다. 계획과 다른 점: (1) 외부 콘텐츠를 last-known-good로 복사하는 일은 `Resolve`가 아니라 **실제로 읽기에 성공한 뒤**(`ContentLocator.Promote`, 내려받다 깨진 그림이 사본을 오염시키지 않게)에 하고, 복사는 옆 폴더에 다 만든 뒤 바꿔치기라서 도중에 실패해도 옛 사본이 남는다. (2) 검증은 JsonUtility가 필요해서 `Scripts/Core`가 아니라 Content 어셈블리(`ContentFolderValidator`)에 있다. (3) 읽기에 실패한 곳은 건너뛰고 다음 곳으로 간다(검증은 통과했지만 그림 디코딩이 실패한 경우). (4) `logs/content-source.txt`(건강 기록 옆)에 어느 콘텐츠로 켰는지와 이유를 남긴다. (5) 실행 중 다시 읽기: F5(`ContentReload`)는 외부 콘텐츠만 다시 읽고(다른 곳으로 물러서지 않는다), 망가졌으면 화면은 그대로 두고 이유만 로그에 남기며, 멀쩡하면 **meadow 섹션만** 다음 마디부터 들판을 다시 짓는다(소리 엔진·곡 격자·손 입력·자연 악보·화면은 켤 때 정해지므로 바뀐 섹션 이름을 로그에 적고 다음 시작에 반영). 키보드 없이 시험하려고 `--reload-content-at 6,14`(그 초에 F5를 스스로 누름)를 더했다. (6) `NativeBuild`가 빌드 뒤 `content/`를 실행 파일 옆과 `StreamingAssets/default-content/`에 복사하고(Windows `Downbeat_Data/StreamingAssets`, Mac `Downbeat.app/Contents/Resources/Data/StreamingAssets`), `tools/package_windows.sh`가 zip에 둘 다 있는지 확인한다. 결과(Step 5, Mac 플레이어): 실제 파일로 7가지 경우(A~G)를 돌려 `measurements/day5/content-swap.json`에 남겼다 — 정상(A: External, 사본 생김), 망가진 JSON(B: LastKnownGood+이유), 소리 파일 하나 없음(C: LastKnownGood, 파일 이름이 이유에), 폴더도 사본도 없음(D: BuiltIn), 그 상태에서 `--soak`(F: 기본 콘텐츠의 replay 파일로 방문 흐름이 Playing까지 감), 실행 중 F5(E: 멀쩡한 변경은 "다음 마디 1.0초 뒤" 반영, 망가진 JSON은 거절). (7) 실제로 돌려 보다가 찾은 결함: Windows 메모장과 PowerShell `Set-Content -Encoding UTF8`은 content.json 앞에 BOM을 붙이는데 `JsonUtility`가 이를 거부해서, 검증(File.ReadAllText는 BOM을 떼어 줌)은 통과하고 읽기(UnityWebRequest)는 실패해 **고친 내용이 조용히 무시되고 사본으로 켜졌다**. Mac에서 재현(G)하고 `TextBom.Strip`으로 고쳤다(replay 파일 읽기에도). 계획의 `content-swap.md`는 `.json`으로 바꿨다(다른 증거 파일과 같은 형식). 시험하지 않은 것: Windows(코드와 빌드 단계는 같다), 키보드 F5 자체(1줄이고 `--reload-content-at`이 같은 `Request()`를 부른다), `replay/demo-hands.json`은 content.json이 가리키는 파일이 아니라서 검증하지 않는다(없으면 replay 입력만 실패한다). 런북 B4·B5와 "콘텐츠는 어디서 읽나요" 문단을 고쳤다.
- Task 15 구현 메모(2026-10-30): `--latency-mode`면 손(또는 마우스)으로 친 음이 **들리는 시각**(이미 있는 그림 사건 대기열 `NoteVisualQueue`가 음 소리 시각에 맞춰 내보내는 것)에 화면 오른쪽 위 120×120 px 흰 네모를 딱 1프레임 켠다(`LatencyMarker`가 IMGUI로 그림, 상태와 자리는 Core `LatencyFlash`). 소리는 그대로다. Mac 플레이어에서 창 사진 926장 중 10장에 네모가 960×540 창의 (840..959, 0..119)에 나왔고(음 14개, 기록기가 일부만 잡음) 끝에 "14개 음에 번쩍였다"는 로그가 남았다. `tools/latency_from_marks.py --csv --fps`는 표준 라이브러리만 쓰고, 칸이 빈 줄은 건너뛰어 세고, 소리·네모가 손보다 먼저인 줄은 읽기 실수로 보고 빼서 이름을 알리며, 30번 미만이면 경고한다(`tools/tests`, tracker 가상 환경의 pytest로 13개). 90백분위는 이웃한 두 값 사이를 직선으로 잇는다(numpy 기본과 같다). `docs/latency-method.md`(휴대폰 240fps로 손·화면·스피커를 한 장면에, 프레임 읽는 법, 30번 이상)는 zip에 `README-latency.txt`로 들어가고 런북 세션 D가 가리킨다. Windows에서는 `play-with-hands.ps1 -LatencyMode`와 `run-supervised.ps1 -LatencyMode`로 켠다(`windows.json`의 변수 `latency`와 단어 `--latency-mode`, 감시 설정 시험 3개; 두 스크립트는 PowerShell을 돌릴 수 없는 Mac에서는 BOM·괄호·PS7 문법 간이 검사만 했고 Windows에서는 돌려 보지 못했다). 소리 시점은 영상 편집기의 소리 파형에서 읽으므로 소리도 같이 느려지는 휴대폰이어야 한다. 실제 지연 숫자는 아직 재지 않았다(사용자 Windows 세션 D).
- Windows 쪽 변경 반영(2026-10-30): Windows 노트북의 Unity 에디터에서 나온 커밋 2개를 `git am -3`으로 얹었다(충돌 없음). (1) `HandRibbonFollow`(One Euro 필터, `ribbon.followMinHz/followBeta/followSpeedHz`): 화면은 60 fps인데 손 자리는 추적기에서 15~30번/초 고르지 않게 와서 손 바람 리본이 가만히 있는 손에도 떨리던 것을 고른다(그리기만 매끄럽게 하고 음 판정은 건드리지 않는다). 이 에디터 시험은 Mac에서 처음 돌려 봤다: EditMode 720개 통과(HandRibbonFollowTests 8개 포함), npm 내용 시험 통과. Mac 플레이어 실창 녹화(`--input replay`, 14초)로 확인: 필터를 켠 것과 끈 것(`followMinHz 1000`)의 리본 머리 이동이 거의 같고(그림당 20.9 vs 20.7 px, 가속 32.3 vs 31.7 px) 한 번 치는 연속 6장에서 머리가 283→411 px로 끊김 없이 이어져, 눈에 띄는 늦음이나 튐은 없다(`measurements/visual/ribbon-follow-check.json`). 가만히 있는 손과 실제 추적기(15~30 fps)에서의 효과는 Mac 재생으로는 못 봤고 단위 시험이 맡는다. (2) `NativeBuild`의 Mac 전용 줄을 `#if UNITY_EDITOR_OSX`로 감싸 Mac 빌드 지원이 없는 Windows 에디터에서도 컴파일된다. 덧붙임: `play-with-hands.ps1`과 `run-supervised.ps1`에 `-LatencyMode`(앱에 `--latency-mode`)를 더했다.
- 검토 5 후속(2026-10-30, 722eb86..f60cf5a): 지적마다 직접 확인한 뒤 고쳤다. **M1 마지막 사본 교체가 원자적이 아님**(맞음: 지우고 옮기는 순서): 옛 사본을 `.old`로 옮겨 두고 새 사본을 제자리로 옮긴 다음 `.old`를 되는 만큼 지운다. 새 자리로 옮기다 실패하면 옛 사본을 되돌리고, 전원이 두 이동 사이에서 끊기면 `.old`가 남는데 `RecoverInterruptedReplace`가 다음 교체와 불러오기 전에 되돌린다. 복사본의 읽기 전용 표시를 지운다. 진짜 임시 폴더로 시험(읽기 전용, 이전 실패의 찌꺼기, 옮기기 실패 되돌리기, 전원 끊김 복구). **M2 사본이 소리를 읽기 전에 만들어짐**(맞음: 헤더만 보는 검사를 통과하지만 Unity가 못 여는 WAV를 실제로 만들어 보니, 검증은 통과하고 Unity는 빈 클립을 돌려준다): 이제 불러오는 쪽이 각 소리를 Unity로 한 번 열어 보고(`TrialDecode`), 못 열면 그 사본을 버리고 다음 사본으로 간다(진짜 폴더와 Unity 디코더로 시험). 사본은 앱이 소리까지 읽고 준비된 뒤에만 만든다(`ConfirmLoaded`; F5는 들판을 다시 지은 뒤). 런북 문장도 맞게 고쳤다. 소리 시험이 통과한 뒤 진짜 읽기가 실패하는 경우는 같은 Unity 코드·같은 파일이라 기대하지 않는다. **M3 다시 지을 때 샘**(맞음, 그리고 더 있었다): 지형 메쉬 외에 배경 사각형 메쉬와, 안내 글자(`PrototypeNotice`) 오브젝트·글꼴 재질·글꼴 질감·글자 메쉬가 지을 때마다 하나씩 남았고 글자는 겹쳐 그려졌다. 모두 고쳤다(글꼴은 이름이 같으면 다시 쓴다). 다시 지은 뒤 Mesh·Material·Texture2D·RenderTexture·GameObject의 이름별 개수가 늘지 않는지 보는 시험. **M4 F5가 버리는 것**: 아직 안 들린 음의 꽃과 입자 신호는 다시 지을 때 이어서 넘긴다(시험). 이미 핀 꽃은 봉오리로 돌아간다(런북에 적음). 불러오다 예외가 나면 `_loading`이 영영 남던 것은 직접 확인했고(두 번째 F5가 무시됨), Unity는 안쪽 코루틴이 던지면 바깥 코루틴의 finally를 돌리지 않아서 손으로 한 걸음씩 돌려 예외를 거절로 바꾼다. **L2 돌풍 폭 검증**(맞음: 폭 5, 간격 2면 셀 가장자리에서 0.02 넘게 튄다는 것을 시험으로 확인): `4.5 x width + lagMetres + frontWobble <= 2 x spacing`. 경계에 있는 값에서 튐은 1.5e-3 미만. **L3 보이지 않는 BOM 글자**(맞음: 소스에 진짜 U+FEFF가 들어 있었다): `\uFEFF` 글자열로 바꿨고 저장소 전체에 안쪽 BOM이 없음을 확인했다. **L6** 런북에 예전 형식 content 폴더 줄을 더했다. M5, L1은 요청대로 두었다.
- 최종 검토 후속(2026-10-30, main...3e69284): 지적마다 직접 확인한 뒤 고쳤다. **H1 소리 장치가 없으면 재시작 고리에 증거도 없음**(맞음: `BuildOps`가 소리 대기 루프 뒤에서만 불려 하트비트·상태 CSV가 없었다 → 감시가 90초에 `heartbeat_missing` → `taskkill /F` → 반복): 앱은 이제 **켜지는 순간(`Start`)부터** 하트비트와 상태 CSV를 쓴다(불러오는 동안의 프레임은 세지 않고, 하트비트와 줄은 계속). 하트비트 둘째 줄은 `audio=starting|ok|stalled|failed`(`starting`: 소리 시계가 한 번도 안 움직임, `failed`: `--audio-timeout`(기본 30초) 동안 소리가 안 올라와 앱이 포기), 앱이 어떤 이유로든 포기하면 `app=failed` 줄이 더해진다. 상태가 바뀌면 다음 박을 기다리지 않고 바로 쓴다. 상태 CSV 끝에 `audio_state` 열이 생겼다(`experience_state`는 불러오는 동안 `Loading`, 포기한 뒤 `Failed`). 감시: **켠 지 2분을 못 채운 시작이 3번 연달아 나오면**(퇴장·죽임·시작 실패 모두 센다) 쉬는 시간이 60, 120, 240, 480, 600초(상한)로 늘고(`backoff_escalated`), 2분 동안 멀쩡히 돈 시작이 한 번 나오면 처음으로 돌아간다(`recovered`). 3·120초·600초의 이유는 `supervisor/configs/windows.json`의 `_comment`에 있다(한 번 시도가 설정값으로 약 55초(소리 포기 30초 + 20초 + 3초)이면 8시간에 약 520번, 상한 600초에서는 약 50번: 계산이지 측정이 아니다). 소리가 켜진 뒤에도 `audio=starting`이 180초 넘게 이어지면 `audio_not_started`로 다시 켠다. 시험: 감시 `test_escalation.py`의 10개(늘어나는 쉬는 시간과 상한, 한 번 멀쩡하면 되돌아옴, 잠깐 멀쩡했다 다시 끊기는 것은 안 되돌림, 퇴장도 셈, 시작 실패도 셈, 소리를 안 말하는 옛 앱은 멀쩡, `starting`·`failed`·`app=failed`)와 설정·기록 파일 시험, 앱은 EditMode `HealthLoggerTests`와 **진짜 장면에 소리 시계가 안 움직이는 오디오 엔진을 끼운 PlayMode `HealthFromBootPlayModeTests`**(하트비트가 `audio=starting`으로 먼저 생기고, 포기하면 `audio=failed`/`app=failed`, CSV 줄이 쌓이고 `Failed`로 적힘; `BuildOps`를 옛 자리로 되돌리면 이 시험이 빨개지는 것을 확인했다). 확인하지 못한 것: 진짜 Windows에서 소리 장치가 꺼졌을 때 Unity의 DSP 시계가 멈추는지(시험은 멈춘 시계를 흉내 낸다). **H2 얼어붙은 손 추적은 다시 켜지지 않음**(맞음: `heartbeatFile`이 Unity에만 있었다): 손 추적이 프레임 반복에서 `--heartbeat` 파일을 쓴다(읽기 뒤에 쓰므로 `read()`가 안 돌아오면 멈춘다; 카메라가 없어도 다시 여는 반복이 살아 있으니 계속 쓴다; 모델 실패 반복도 쓴다). `windows.json`·`mac.json`의 손 추적에 `heartbeatFile`과 프로세스별 `heartbeatTimeoutSec` 45초(카메라 열기가 Windows 노트북에서 0.3초였고, 45초는 열기가 살아 있는 최장보다 길다)를 달았다. 시험: 손 추적 `test_heartbeat.py` 9개(파일 내용·간격·상태 바뀌면 바로·폴더 만들기·쓸 수 없어도 오류 없음·반복이 읽기 뒤에 보고함·25초 막힘이 25초 침묵으로 남음·카메라 없어도 계속·모델 실패 계속), 실제 명령줄 시험(`--heartbeat`), 감시 `test_escalation.py`의 4개(손 추적만 멈추면 손 추적만 다시 켬)와 설정 시험(두 설정의 `--heartbeat` 경로 = `heartbeatFile`). 감시 README의 "못 보는 것"에 같은 그림을 계속 보내는 얼어붙은 카메라를 적었다. **M1** `windows-hand-setup.md` 6절이 없는 "Camera mode / Mouse mode" 화면 표시를 말하고 있었다 → `Player.log`의 `[input]` 줄과 보는 명령(경로 `...\AppData\LocalLow\ovthemoon\Downbeat\Player.log`)으로 고쳤다. **M2** `play-with-hands.ps1`만 `OPENCV_VIDEOIO_MSMF_ENABLE_HW_TRANSFORMS=0`을 줬다 → 손 추적 `capture.py`가 msmf일 때 카메라를 열기 직전(그리고 `make_source`에서 OpenCV가 불러지기 전)에 스스로 세우고(사용자가 정한 값은 그대로), `windows.json`의 손 추적 환경에도 넣었다(시험 6개: 열 때 환경 변수가 이미 있음, dshow·auto는 그대로, 사용자 값 유지). `import cv2` 전이어야 하는지: OpenCV는 이 변수를 카메라를 시작할 때 읽는다고 알고 있어 둘 다 했다; Windows에서 느린 시작이 실제로 사라지는지는 이 Mac에서 확인할 수 없다. **M3** 8시간 절차 둘의 머리에 차이를 적었다: 점검 문서 5절 = 앱 혼자(감시 없음, 재시작이 고장을 가리지 않음, 서류의 "8시간 안정" 숫자), 런북 세션 C = 감시 프로그램과 함께한 전시 리허설(몇 번 다시 켜졌는지는 `supervisor-날짜.csv`로 센다). **M5** `tracker_age_ms_p95`는 항상 비어 있었다(`HealthLogger`가 NaN을 넘김). 스펙 §8의 상태 로그 항목은 "화면별 fps, 메모리, 손 추적 fps, 손 인식 여부, 오류 수"이고 손 추적 나이는 스펙에 없다(우리가 더한 열). 앱 안에서 잴 수 있는 뜻으로 구현했다: **손 좌표 묶음이 소켓에 도착한 뒤 메인 스레드가 읽을 때까지 기다린 시간**(`PacketRing`이 도착 시각을 같이 들고 있고 `UdpHandSource.PacketAgeMs`가 읽을 때 뺀다; 한 프레임의 가장 새 묶음, 녹화 재생이면 비어 있음). 카메라에서 추적 앱이 보내기까지의 시간은 서로 다른 프로세스의 시계라 비교할 수 없어 들어 있지 않다(런북 표에 적음). `fps_avg`는 들판과 기록 두 화면을 한 앱이 한 번에 그려서 **두 화면 합친 한 값**이다(스펙의 "화면별 fps"는 이 한 값으로 대신한다고 런북 표에 적음). **M6** Mac(Apple M4 Pro)에서 새로 복사한 앱·콘텐츠로 5번 잰 켠 뒤 첫 하트비트 **1.2초**, `audio=ok` 1.3초, `[Downbeat] ready` 1.8초(`measurements/day5/boot-to-first-heartbeat.json`, `tools/measure_boot_time.py`; 파일 캐시는 비우지 못했다). Windows 노트북은 재지 못해서 `windows.json`의 `heartbeatGraceSec`을 90→180초로 늘렸다(Mac 첫 하트비트의 150배; 근거는 `_comment`). **L1** 로그 폴더를 못 만들면 `persistentDataPath`에 대신 쓰는데 감시는 앱 폴더 `logs`만 읽는다 → 앱이 `Debug.LogError`로 크게 적고(`[health] the heartbeat and the health log cannot be written in …`), 런북에 한 줄 더했다. **L2** 손 추적 `_all_infer`가 프레임마다 늘었다(30fps 8시간에 27 MB) → 10만 개 저수지 표본(55분까지는 정확, 그 뒤는 고른 표본)으로 바꾸고 최댓값·평균은 따로 정확히 센다; 손 추적 로그는 5 MB×3 회전, 감시가 받는 `<이름>.out.txt`는 시작할 때와 1분마다 5 MB를 넘으면 `.1`~`.3`으로 밀어낸다(자식이 열어 둔 파일은 복사한 뒤 그 자리에서 비운다; Windows에서는 이 부분을 돌려 보지 못했다). **L3** 설치 스크립트가 보여 주던 크기(uv 100, Python 90, 패키지 300, 보관 200 MB)를 Windows 노트북에서 잰 크기(30, 60, 270, 270 MB)로 맞췄다. **L4** 추적되던 `.pyc` 파일을 `git rm --cached`. **L5** `LaunchArgs`가 `--mute -logFile x`에서 `-logFile`을 `--mute`의 값으로 삼켰다(빨간 시험으로 확인) → 대시와 글자로 시작하는 토큰은 값이 아니다(`-1`, `-0.5`, `-.5` 같은 음수는 그대로 값). 시험 개수: Unity EditMode 754, PlayMode 53, 손 추적 pytest 101, 감시 pytest 103, tools 13, npm 158. 하지 않은 것: M4(측정 파일 속 개인 경로)는 Task 16 공개 전 정리에서 한다.
- 검증 후속(2026-10-30, 17064b6의 회귀 지적): **H-1 하트비트가 "앱이 Running까지 갔다"는 뜻이 아니게 됐다**(맞음: `NativeAudioEngine.Tick`이 `Configure`나 샘플 읽기와 상관없이 DspTime을 복사하므로 소리 시계가 한 번 움직이면 `audio=ok`가 되는데, `Boot`가 예외로 끝나면 `_state`가 `Loading`에 영원히 남고 감시는 멀쩡하다고 보고 2분 뒤 쉬는 시간 되돌리기까지 했다 → 밤새 까만 화면). 하트비트에 **`app=loading|running|failed` 줄**(셋째 줄, 항상 쓴다; 옛 앱은 없음)을 더했다: 둘째 줄 `audio=` 순서는 그대로고, 파서와 문서를 같이 고쳤다. 감시는 `app=loading`이 `heartbeatGraceSec`(Windows 180초)를 넘으면 `app_not_ready`로 끄고 다시 켜며(`audio=starting`이 그 시간을 넘으면 예전대로 `audio_not_started`), `app=loading`인 동안은 멀쩡한 시작으로 치지 않아 2분 안정 되돌리기에 들지 않는다(그 시작은 불안정한 시작으로 센다). 앱은 `Boot`를 `GuardedRoutine`(코루틴과 그 안의 코루틴을 손으로 한 걸음씩 돌려 예외를 콜백으로 넘김)으로 감싸서, 예외가 새면 `Debug.LogException`과 `Fail("boot: 종류: 메시지")`로 `app=failed`가 되고 이유가 로그에 남는다(중첩 코루틴에서 난 예외는 바깥 코루틴의 catch를 거치지 않으므로 직접 내려가며 돌린다). 시험: 감시 5개(켜지는 중 유예 안, 유예 뒤 `app_not_ready`, 그런 시작 3번이 불안정으로 세어져 `backoff_escalated`, 켜지는 중은 안정 되돌리기에 안 듦, 켜지는 중→떴어요는 멀쩡), 파서 2개 묶음, EditMode `GuardedRoutineTests` 5개와 `HealthLoggerTests` 2개, **진짜 장면에 소리 시계는 가는데 부팅이 멈추거나(`HangingRepository`) 던지는(`ThrowingAudioEngine`은 위쪽, `ThrowingRepository`는 중첩 코루틴 안) 구성을 끼운 PlayMode 3개**: 멈춘 부팅은 끝까지 `audio=ok app=loading`이고 `app=running`이 한 번도 안 나오며, 던진 부팅은 `app=failed`와 `boot:` 메시지로 끝난다. `GuardedRoutine`을 빼면 던지는 두 시험이 빨개지는 것을 확인했다. **L-1** 손 추적이 모델을 내려받는 동안(처음 켤 때) 첫 하트비트가 없었다: `run()`이 모델을 읽기 전에 `camera=starting` 한 번을 쓰고, `ensure_model`이 64 KB 조각마다 `progress`를 불러 `main`이 이 하트비트를 갱신한다(한 번에 `read()`하던 것을 조각으로 바꿈; 끊기면 `.part`를 지우고 `ModelError`). 런북에 "먼저 `setup-hand-tracker.ps1` 를 돌려 모델을 받아 두라"는 말을 더했다. **L-2** 런북 표의 손 추적 하트비트 둘째 줄을 `starting/ok/lost/model_failed`로 고쳤다. **L-3** 회전이 거절되면 다음 시도는 60초 뒤에만 한다(`ROLLOVER_RETRY_SEC`, 시계 주입 시험). 시험 개수: Unity EditMode 761, PlayMode 56, 손 추적 pytest 108, 감시 pytest 116, tools 13, npm 158. Windows에서는 돌려 보지 못했다.
- 공개 전 정리(Task 16): 커밋된 로그·XML 속 `/Users/...` 절대 경로와 지원 폴더 이름을 지운다.
- 들판 배치 값 변경(Task 6b, 2026-10-29): 3D 풀은 화면 0.55행에서 0.47행 사이 띠에서 개수와 키가 줄어 사라지고, 그 위 중경(언덕, 안개, 나무)은 배경 그림이 맡는다. 그래서 손이 닿는 들판의 먼 끝을 풀이 사라지는 띠 가운데에 둔다. `meadow.layout`: `farZ` 38 → **8**(화면 0.50행), `fieldWidth` 40 → **13.2**(farZ에서의 폭 = 16:9 화면 폭), `nearZ` 2 → **2.3**(카메라 높이 1.7, 화각 50, 피치 −11.6°에서 화면 아래 가장자리가 닿는 깊이), `horizonScreenY` 0.2 → **0.30**. 0.30은 진짜 지평선(0.28)이 아니라 `ScreenYForDepth` 핀홀 모델을 카메라에 맞춘 값이다: 깊이 2.3~8 m에서 모델과 실제 카메라 투영의 차이가 최대 화면 높이의 1.4 %(0.28이면 2.4 %). PlayMode `Layout_PutsGroundAtTheScreenRowTheCameraDrawsItAt`가 2 % 이내를 지킨다. 풀은 깊이 6.6 m까지 가득, 9.8 m에서 0이고, 지면이 배경 그림으로 넘어가는 안개 띠는 6.2~10.2 m(지면을 따라 잰 깊이, 거리가 아님). `terrain.zMax` 26 → 20. (Task 6c: the hand-over is now a band of picture rows, not depths: `meadow.grass.fadeFromRow` 0.57 → `fadeToRow` 0.47 for the blades, `meadow.haze.fromRow` 0.61 → `toRow` 0.52 for the ground, so no field edge or terrain bump can ever show; `farZ` 8 stays at row 0.50, inside both bands.)

## 시각 다듬기 목록 (Task 9 이후, 2026-10-29)

Task 9까지 한 시각 작업에서 눈에 띄지만 아직 고치지 않은 것이었고, 2026-10-30 Part D 에서 1~9를 하나씩 커밋으로 고쳤다(각 줄 끝에 상태와 커밋). 우선순위 순서가 아니라 발견 순서다. 다듬기 차례에 사용자가 순서를 정한다.

1. 가까운 양귀비는 꽃잎이 겹치지 않은 납작한 원판으로 보인다(가장자리 말림, 꽃잎 사이 그림자 없음). **했음(0cb5faa):** 꽃잎 4장이 겹친 컵 모양, 말린 가장자리, 안쪽이 어두운 그림자. 가까이서 찍은 사진 `measurements/visual/partd-poppy-closeup.jpg`.
2. 풀끝 빛(`grass.tipGlow`)과 카메라 Bloom이 약해서, 역광의 반짝임이 배경 그림만큼 살아나지 않는다. **했음(dc082e6):** 해가 낮고 풀 뒤에 있을 때 풀끝에 은은한 테두리 빛(`grass.tipRim` 0.5, High 에만). Bloom 은 건드리지 않았고 창 사진에서 넘치지 않는다.
3. 언덕(`hummock`)의 기복은 풀 높이 차이에 그쳐 실제 지형의 굴곡으로 읽히지 않는다. **했음(dbf8799):** 가까운 땅에 진짜 언덕 기복(`terrain.relief*`), 풀은 그 위에 심고, 3D 땅이 배경 그림으로 넘어가는 선은 그대로. 기복 크기는 Mac 에서 눈으로 정한 값이라 Windows 에서 다시 봐야 한다.
4. 반딧불은 수가 적고(최대 80) 창백한 원판이다. 종소리와 함께 깜빡이지 않는다(후보: 종소리가 들릴 때 가장 가까운 반딧불이 밝아짐). **했음(66b272d):** 종소리가 들리면 그 소리가 들리는 자리에서 가장 가까운 몇 마리(`fireflyAnswerCount`, 반경 안)가 잠깐 밝아졌다 꺼지고(`particles.fireflyAnswer*`), 색이 더 따뜻하다(`fireflyColor`).
5. 홀씨는 작은 점이라 먼 곳에서는 안 보인다. **했음(503bf47):** 먼 홀씨와 꽃잎도 화면에서 최소 몇 점 크기를 지킨다(`particleMinPixels`). 5 m 넘게 멀면 안개(haze)에 묻혀 어차피 안 보여서 한계는 거기까지다.
6. 손이 풀을 가르는 바람이 빗질한 자국 같은 패치로 읽힌다. **했음(8e1c9cc):** 손이 부드럽고 가장자리가 풀어진 지역 돌풍이다: 손이 가는 쪽으로 풀이 기울고 손 뒤에서 되돌아 흔들린다. Low 에서는 되돌아 흔들림 없이 기울임만(비용).
7. 언덕 안개 맥동(`nature.hillPulse`)은 가로 띠(`hillPulseRow` 0.4 ± 0.12) 하나라서 언덕 모양을 따르지 않고 납작하다. **했음(a056fe6):** 안개 맥동이 그림에 그려진 언덕 안개를 따라간다(`nature.hillPulseFollow`).
8. 하늘은 에너지로만 움직인다. 100초 실행에서 하늘은 최고 0.85(일몰은 1.0)까지만 갔다. 노을에 닿는 기준을 에너지 그대로 둘지 단계 3 기준으로 맞출지 정해야 한다. **했음(7e3661b):** 강하게 오래 연주하면 노을에 닿고, 가만히 든 손은 안 닿는다(`nature.skyEnergyLow/High` 0.15/0.9). 이 두 값은 지휘 시뮬레이션의 30초 에너지(`measurements/day5/conduct-energy-sim.json`)로 정한 것이고, 노을에 닿는 기준을 어디에 둘지는 사용자가 최종으로 정할 일이다.
9. 첫 손이 들어올 때(Waking) 따로 깨어나는 연출이 없다. 봉오리가 한꺼번에 반짝이는 정도가 후보다. **했음(97e3c8e):** 첫 손이 들어오면 봉오리가 물결처럼 차례로 반짝이고 부드러운 돌풍이 지나간다(`meadow.wake`).
10. (고침, 위 번짐 수정) 1920×1080 실제 창에서 단계 3의 들판이 흰 덩어리로 뒤덮이던 것. 원인은 반딧불이 아니라 일몰 단계 풀끝·이슬 반짝임의 Bloom이었다.
11. 기록 화면은 가만히 있는 시간이 길다(대기). 카드가 없을 때 안내문 숨쉬기 말고는 움직임이 없고, 카드가 같은 연주(녹화 재생)면 같은 카드가 나란히 놓인다. 대기 중에 아주 느리게 떠다니는 꽃잎이나 반딧불 점 정도를 더할 수 있다. **했음(6896b78, 20faea1):** 두 번째 화면에서 반딧불 점과 꽃잎 24개가 패널과 글자 뒤로 아주 느리게(화면 높이의 약 2 %/초) 떠다닌다(꽃잎은 네 번째마다, 세 꽃 색, 한 번에 그림, 연주 중에는 30 %로 흐려졌다가 대기가 되면 돌아옴; `record.idleMotes` 등). 카드마다 만든 시각이 사진 모서리에 붙는다("오후 3:42", `record.text.timeFormat`; `SessionCard`가 이미 시작 시각을 갖고 있어 카드 파일은 그대로). Mac Low 프레임 시간 +0.4 %(1.101 → 1.105 ms, 오차 ±1 % 안). 사진 `measurements/visual/record-idle-a.jpg`, `-b.jpg`. 주의: 1분 안에 만든 두 카드는 같은 분이 찍힐 수 있다.
12. (고침, 배경이 정적이라 심심하다는 지적) 모든 등급에서 배경이 움직인다: 흐르는 구름, 들판과 먼 그림 위의 구름 그림자, 먼 들판 위의 돌풍 일렁임. 약한 등급(`skyDetail` 0)은 예전엔 구름과 일렁임을 뺐지만 이제는 구름을 시작할 때 구워 둔 무늬 판(`CloudTile`, 1024×1024)에서 읽고(픽셀당 두 번의 소음 계산 대신 층당 한 번 읽기), 일렁임과 그림자는 이미 구워진 바람 장(256×192)에서 읽는다. 움직임은 그대로이고 구름 결만 조금 부드럽다. 시험: 두 장 사이 차이가 약한 등급 0.0135, 강한 등급 0.0137(30초 간격), 한 프레임 간격은 0.00003(깜빡임 없음).
13. (고침, 사용자: "풀이 흔들리는게 아니라 움직임에 따라 늘어나는 느낌") 풀잎·꽃줄기가 센 바람에 늘어남 → 길이 그대로 휘게. 옛 식은 끝을 한계 없이 옆으로 끌어(`horizontal = s·키·t²`) 빠른 손 가까이(s 1.5~3)에서 잎 길이가 s=1 ×1.34, s=2 ×2.2, s=3 ×3.2가 되어 긴 줄무늬로 번졌다. **했음(0ce370c, 5cb9944; 사진 도구 12bbaf5):** 잎과 줄기(굽힘 절반, 머리는 줄기 끝에 얹힘)가 자기 길이의 원호를 따라 휜다. 전체 굽힘각 θ = max·u/(1+u⁴)^¼, u = 2.4·s/max, `meadow.wind.maxBendDeg` 120°(검사 10..150). 처음 값(75°, 기울기 2.8, 무릎 16제곱)은 s ≥ 0.55인 잎을 전부 75°에 못 박아(잎 스스로의 기울기가 0.5~0.9인 때가 많다) 돌풍이 와도 못 움직였고(20000장 모형에서 6.3 %가 얼어붙음) 센 손도 더 눕히지 못했다(s 0.6·1·1.5·2·3에서 모두 75°) → 120°·부드러운 무릎으로 고쳤다. 돌풍 하나에 끝이 움직이는 양(잎 키 단위, 잎 스스로의 기울기를 아무 방향으로 둔 모형): 옛 식 0.37, 처음 값 0.246(아래 4분위 0.116, 얼어붙음 6.3 %), 지금 0.294(아래 4분위 0.226, 0 %). 센 손일수록 더 눕는다: s 0.6·1·1.5·2·3에서 78·107·117·119·120°. 대기 들판은 s ≤ 0.6에서 끝 방향이 옛 식과 최대 3.1° 안(100° 이상의 max에서 5° 안; 75°면 9°). 길이는 120°·150°에서도 그대로이고 끝이 발 아래로 가지 않으며, HLSL 도우미 `MeadowBladeCurve`(MeadowWind.hlsl)와 C# 거울 `BladeBend`는 시험 18개와 GPU 일치 시험(2e-7, 네 max)으로 묶었다. 같은 순간 전후 사진 `measurements/visual/grass-bend-idle.jpg`(시각을 맞춘 같은 프레임), `grass-bend-hand.jpg`(`--frame-dump-hand`로 손도 같은 순간; 전 = 옛 늘어나는 빌드), Low 프레임 시간 옛 1.089 → 지금 1.080 ms(−0.9 %, 오차 ±1.3 % 안, `measurements/day5/render-cost-grass-bend.json`). 움직임(프레임 간 평균 절대 휘도 차, 들판, High 20 fps, 8칸 흐림): 대기 옛 3.07 / 75° 2.81 / 120° 2.93, 손 옛 7.59 / 6.72 / 7.31. 알아둘 것: 대기 들판도 화소 단위로는 같지 않다(같은 시각 정렬로 들판 평균 절대 휘도 차 12.6/255, 16칸 흐림 4.0; 평균 밝기 31 → 31): 옛 식은 약한 바람에서도 잎을 2~9 % 짧게 만들고 끝을 처지게 했는데 이제 잎이 제 길이로 선다. 돌풍에도 가장 기울어진 잎(기울기 0.9가 바람 방향)은 0.09만 움직인다(0.15는 max 150이어야 한다).

## 검토 2 후속 (2026-10-29, Tasks 5~8 검토 결과)

검토 판정은 "수정과 함께 진행". 아래를 Task 9 앞에서 `fix:` 커밋 하나로 처리했다.

- M1 소리 보이스: 페이드 중인 보이스는 백엔드가 소스를 멈추는 시각(페이드 + `FadeStopMarginSec` 30 ms)까지 풀에서 비지 않은 것으로 센다(`IAudioBackend.FadeReleaseMarginSec`). 빠른 연속 음에서 페이드를 끊는 클릭이 돌아오지 않는다. `VoiceFader`는 잠금과 세대 번호로 상태를 보호해, 메인 스레드의 `Reset`/`BeginFade`가 오디오 콜백 끝의 되쓰기에 덮이지 않는다. 테스트 각각.
- M2 `SmoothClock`: 오디오 시계가 뒤로 뛰면(장치 재설정, 8절 장애 주입) `_last`도 지워 `VisualNow`가 미래에 박히지 않는다. 테스트.
- L1 재타격 멈춤: `BloomRun`(Core)이 이전 피움을 새 피움 시작까지 계속 평가하고 그 값에서 새로 오른다. 테스트(Core, PlayMode).
- L3 입자 블렌딩: 꽃잎은 알파 블렌드, 홀씨·반딧불·꽃가루는 가산(`_DstBlend`). 테스트.
- L4 상수의 콘텐츠화: 입자(`maxSeeds`, `spawn*`, 반딧불 깜빡임·잡음·바닥·가장자리), 리본(`minVertexDistance`, `headAlpha`, `midAlpha`, `midWidth`), 손 속도 미리보기(`wind.handLookAheadSec`), 데모 파도(`meadow.demo`)를 `content.json`으로 옮기고 검증한다. 구조상 상수(손 2개, 레이 반복 3)는 코드에 남는다.
- L5 `MeadowStage.Configure` 재호출·꺼짐: 앞 빌드의 바람 GPU 버퍼와 라우터 구독을 지우고, `OnDisable`에서 GPU 버퍼를 돌려주며 `OnEnable`에서 다시 짓는다(`WindFieldGpu.LiveInstances`로 시험).
- L2 약한 시험: `IdleMeadow…`는 꽃을 껐을 때 바뀌는 화소가 들판에는 있고 하늘에는 없음을 확인하고, `ScriptedPlay…`는 입자 수, 반딧불, 리본, 열린 꽃, 바람 고리를 확인한다.
- 스펙 4.2 "가까운 꽃이 크다": 3D 원근으로 자동으로 성립함을 PlayMode 시험으로 고정했다(가장 가까운 꽃이 가장 먼 꽃보다 화면에서 1.8배 넘게 크다).
- 문서: 위 Task 5(Neutral)·Task 6(마우스 Radial 제거) 문구를 고쳤다.

## 준비물 (코드 작업 전, Claude가 만든다)

- 완료(10/29): `prototype/meadow-refs/meadow_front_b_bluehour.png`(대기), `meadow_front_b.png`(연주 중), `meadow_front_b_sunset.png`(절정). 셋 다 1672×941, 같은 구도. 프롬프트는 `prompts.md`.
- 배경 그림 3장은 Task 5에서 `content/img/meadow/backdrop_bluehour.jpg`, `backdrop_idle.jpg`(= front_b), `backdrop_sunset.jpg`(2048×1152 이하, JPEG 품질 85)로 넣는다.

## 파일 구조 (새로 생기거나 크게 바뀌는 것)

```
tracker/                              Python 손 추적 앱 (새로)
  pyproject.toml
  downbeat_tracker/{__main__,config,capture,landmarker,hand_ids,protocol,sender,stats}.py
  tests/
supervisor/                           Python 감시 앱 (새로)
  downbeat_supervisor/{__main__,config,process,heartbeat,events}.py
  tests/
ops/windows/                          setup.ps1, install-autostart.ps1, uninstall-autostart.ps1, collect-results.ps1, RUNBOOK.md
ops/mac/                              run-demo.command
tests/fixtures/hand-udp-v1/           Python이 만들고 Unity가 읽는 공통 UDP 예시 패킷
docs/protocol/hand-udp-v1.md          UDP 형식 문서
unity/Downbeat/Assets/_Project/
  Scripts/Core/                       (기존) + HandUdpCodec, Meadow/*, Nature/*, Record/*, Ops/*
  Scripts/Audio/NativeAudioEngine.cs  (EditorAudioEngine을 승격)
  Scripts/App/UdpHandSource.cs, ContentLocator(수정), HealthLogger, CardStoreIO, DisplayRouter
  Scripts/Meadow/                     새 asmdef Downbeat.Meadow: 들판 뷰(MonoBehaviour)
  Scripts/Record/                     새 asmdef Downbeat.Record: 기록 화면 뷰
  Shaders/{Grass,GrassPlacement.compute,Flower,BackdropBlend,WindRibbon}.*
  Editor/{NativeBuild,MeadowSceneBuilder}.cs
  Scenes/Meadow.unity
archive/web/                          그만두는 웹 코드(컴파일되지 않는 위치)
```

---

### Task 1: 네이티브·URP 바탕과 빌드·테스트 스크립트

**Files:**
- Create: `unity/Downbeat/Assets/_Project/Editor/NativeBuild.cs`, `tools/unity_test.sh`, `tools/unity_build.sh`
- Modify: `unity/Downbeat/Packages/manifest.json`(URP 추가), `ProjectSettings/*`(Standalone, Linear, URP 에셋), `Scripts/App/DownbeatLifetimeScope.cs`
- Move: `Scripts/Audio/WebAudioEngine.cs`, `Scripts/App/{JsHandSource,PagePort}.cs`, `Plugins/Web/*`, `Assets/WebGLTemplates/*`, `Editor/WebBuild.cs` → `archive/web/` (Assets 밖)

**Interfaces:**
- Produces: `Downbeat.EditorTools.NativeBuild.BuildWindows()`(실제 네임스페이스는 `Downbeat.EditorTools`, 검토 1에서 확인), `NativeBuild.BuildMac()` (batchmode `-executeMethod`, 출력 `builds/windows/Downbeat.exe`, `builds/mac/Downbeat.app`); `tools/unity_test.sh <EditMode|PlayMode>` → 결과 XML `measurements/unit-tests/<mode>.xml`, 통과·실패 수를 출력하고 실패 시 0이 아닌 종료 코드.

- [ ] **Step 1:** Unity Hub CLI로 6000.3.9f1에 `windows-mono` 모듈 설치(Mac Standalone은 이미 있음). 설치 로그를 `measurements/env/unity-modules.txt`에 남긴다.
- [ ] **Step 2:** `tools/unity_test.sh EditMode` 작성 후 실행 → 기존 137개 통과 확인(웹 코드 이동 전 기준선).
- [ ] **Step 3:** 웹 전용 파일을 `archive/web/`로 옮기고 `DownbeatLifetimeScope`에서 `WebAudioEngine`·`JsHandSource` 등록을 지운다. 오디오는 `EditorAudioEngine`을 모든 플랫폼에 등록. 웹 전용 테스트가 있으면 함께 옮긴다.
- [ ] **Step 4:** URP 패키지 추가, URP 파이프라인 에셋 2개(`URP_High`, `URP_Low`)와 Global Volume 프로필 생성, 색 공간 Linear, 빌드 대상 Standalone.
- [ ] **Step 5:** `tools/unity_test.sh EditMode` → 전부 통과(옮긴 테스트 수만큼 줄어든 숫자를 기록).
- [ ] **Step 6:** `tools/unity_build.sh mac` / `windows` 실행 → 두 빌드가 생기고 Mac 빌드가 실행되어 첫 화면까지 뜬다(기존 Day 2 장면 그대로여도 됨). 빌드 크기를 `measurements/build/task1.json`에.
- [ ] **Step 7:** Commit (허락 시): `chore: switch to native standalone with URP`

### Task 2: 네이티브 오디오 엔진과 박자 시계 재측정

**Files:**
- Create: `Scripts/Audio/NativeAudioEngine.cs`(EditorAudioEngine 이름 변경·승격), `Scripts/Audio/AudioOutputTap.cs`, `Tests/EditMode/NativeAudioEngineTests.cs`, `tools/run_native_clock.sh`
- Modify: `Scripts/App/DownbeatLifetimeScope.cs`

**Interfaces:**
- Consumes: `IAudioEngine`(기존, 바꾸지 않음).
- Produces: `NativeAudioEngine : IAudioEngine` — `Now = AudioSettings.dspTime`, `HeardNow = dspTime − 출력 지연 추정(DSP 버퍼 길이 × 버퍼 수 / 샘플레이트)`; 샘플은 `file://` 경로에서 `UnityWebRequestMultimedia`로 WAV 로드; 보이스 풀 `maxVoices`(content `audio.maxVoices` 32), 가장 오래된 보이스부터 훔침(`Stats.Stolen` 증가).
- Produces: `AudioOutputTap : MonoBehaviour` — `AudioListener`에 붙어 `OnAudioFilterRead`로 받은 출력을 메모리에 모았다가 `--measure-clock` 실행 시 Day 0 S-1 실행 폴더와 같은 구성으로 저장(WAV + 예약 시각 JSON). 저장 형식은 `tools/analyze_s1.mjs`가 읽는 형식을 그대로 따른다.

- [ ] **Step 1:** 실패하는 테스트: `NativeAudioEngineTests.Schedule_ReturnsRequestedTime_WhenInFuture`(예약 시각이 그대로 반환), `Schedule_CountsLate_WhenTimeHasPassed`(`Stats.Late` 1 증가), `Schedule_StealsOldestVoice_WhenPoolFull`(보이스 2개 설정, 3번째 예약 시 `Stats.Stolen == 1`).
- [ ] **Step 2:** 실행 → FAIL 확인.
- [ ] **Step 3:** `NativeAudioEngine` 구현, `DownbeatLifetimeScope`에 등록.
- [ ] **Step 4:** EditMode 전체 통과.
- [ ] **Step 5:** `tools/run_native_clock.sh`: Mac 빌드를 `--measure-clock`으로 60초 × 3회(정지, 2초마다 메인 스레드 50ms 막기 포함) 실행 → `tools/analyze_s1.mjs`로 분석 → `measurements/native-clock/summary.md`. 합격 기준 표준편차 ≤ 3ms, 최대 ≤ 10ms, 분당 드리프트 ≤ 5ms.
- [ ] **Step 6:** 결과 기록. 불합격이면 멈추고 원인(버퍼 크기, 예약 여유)과 숫자를 보고한다(설계 6절: 대안은 사용자와 정함).
- [ ] **Step 7:** Commit (허락 시): `feat(audio): native scheduled audio engine with clock measurement`

### Task 3: 손 추적 앱(Python)과 UDP 형식 v1

**Files:**
- Create: `tracker/pyproject.toml`, `tracker/downbeat_tracker/*.py`, `tracker/tests/test_protocol.py`, `tracker/tests/test_hand_ids.py`, `tracker/tests/test_pipeline_video.py`, `docs/protocol/hand-udp-v1.md`, `tests/fixtures/hand-udp-v1/{two-hands,no-hands}.{bin,json}`

**Interfaces:**
- Produces (UDP v1, 리틀엔디언, 한 데이터그램 = 한 프레임): 헤더 `magic "DBH1"(4) | version u8=1 | state u8(TrackerState 값과 같음: 2 Running, 4 CameraMissing, 5 CameraError, 6 ModelFailed) | hand_count u8(0~2) | reserved u8 | seq u64 | capture_ms f64 | infer_end_ms f64 | send_ms f64 | aspect f32` = 44바이트, 손마다 `id i32 | score f32 | 21 × (x f32, y f32, z f32)` = 260바이트. 좌표는 카메라 이미지 기준 0~1(좌우 반전 안 함). 시간은 추적 앱 시작 기준 단조 시계 ms.
- Produces (Python): `protocol.encode_frame(frame: TrackedFrame) -> bytes`, `protocol.decode_frame(data: bytes) -> TrackedFrame`(잘못된 magic·길이·version은 `ProtocolError`), `hand_ids.HandIdAssigner.assign(hands: list[Hand2D], t_ms: float) -> list[int]`(웹 `hand-ids.js`와 같은 규칙: 손목 거리 기준 이어붙임, 0.25 화면폭 넘게 튀거나 300ms 넘게 사라지면 새 id), CLI `python -m downbeat_tracker --camera 0 --port 47800 [--video PATH] [--loop] [--preview] [--max-fps 30] [--log PATH]`.
- 동작: 손이 없어도 매 프레임 보낸다(hand_count 0). 카메라를 잃으면 `state=4 or 5`로 초당 2회 보내며 1초마다 다시 연다. 모델 로드 실패는 `state=6`. 1초마다 stats 한 줄(fps, 추론 ms p50, 손 수)을 로그에.

- [ ] **Step 1:** 실패하는 테스트: `test_roundtrip_two_hands`(encode→decode 값 동일, 길이 44+2×260), `test_rejects_bad_magic`, `test_rejects_truncated`, `test_writes_fixtures`(픽스처 `.bin`과 같은 값의 `.json`을 만들고 다시 읽어 일치), `test_ids_follow_moving_hand`, `test_new_id_after_gap_300ms`, `test_new_id_after_jump`.
- [ ] **Step 2:** `cd tracker && python3.12 -m pytest -q` → FAIL.
- [ ] **Step 3:** `protocol.py`, `hand_ids.py` 구현.
- [ ] **Step 4:** 위 테스트 통과.
- [ ] **Step 5:** 실패하는 통합 테스트 `test_pipeline_video`: Day 2의 `tools/make_strike_video.py`로 만든 손 영상(또는 저장소에 둘 짧은 CC0 손 영상)을 `--video`로 넣고, 로컬 UDP로 받은 프레임 중 손이 있는 프레임이 50% 이상이고 seq가 증가하는지 확인. 모델 파일은 첫 실행 때 공식 주소에서 받아 캐시(`~/.cache/downbeat/hand_landmarker.task`)하고 저장소에 넣지 않는다.
- [ ] **Step 6:** `capture.py`, `landmarker.py`(VIDEO 모드, `num_hands=2`), `sender.py`, `stats.py`, `__main__.py` 구현 → 통합 테스트 통과. 영상 입력 기준 추론 ms·fps를 `measurements/tracker/mac-video.json`에.
- [ ] **Step 7:** `docs/protocol/hand-udp-v1.md`에 위 형식과 픽스처 위치를 적는다.
- [ ] **Step 8:** Commit (허락 시): `feat(tracker): python hand tracker with UDP v1 protocol`

### Task 4: Unity UDP 손 입력

**Files:**
- Create: `Scripts/Core/HandUdpCodec.cs`, `Scripts/App/UdpHandSource.cs`, `Tests/EditMode/HandUdpCodecTests.cs`, `Tests/EditMode/UdpHandSourceTests.cs`
- Modify: `Scripts/App/DownbeatLifetimeScope.cs`, `Scripts/Core/InputMode.cs`(모드 `Hand`가 UDP를 뜻하도록), `content/content.json`(`hand.udpPort` 47800, `hand.trackerTimeoutSec` 2.0, `hand.mirrorX` true)

**Interfaces:**
- Consumes: 기존 `HandBufferLayout`, `HandFrameReader.TryRead(double[] buffer, double receivedMs, out HandFrame frame)`.
- Produces: `HandUdpCodec.TryDecodeToBuffer(ReadOnlySpan<byte> packet, bool mirrorX, double[] buffer, out long seq) -> bool` — 패킷을 기존 `double[136]` 배치로 옮긴다(좌우 반전은 여기서). 이후 `HandFrameReader`가 `HandFrame`을 만든다.
- Produces: `UdpHandSource : IHandSource, IDisposable` — 백그라운드 스레드가 `127.0.0.1:udpPort`에서 받아 최근 8개만 보관(넘치면 오래된 것 버림), `Poll`이 메인 스레드에서 꺼내 `HandFrame`으로 바꾼다. `trackerTimeoutSec` 동안 패킷이 없으면 `State = TrackerState.CameraMissing`이고 손 0개 프레임을 낸다. seq가 줄어들면(추적 앱 재시작) `Generation`을 1 올리고 새 흐름으로 받는다. 포트를 열 수 없으면 `State = TrackerState.CameraError`와 로그, 5초마다 다시 시도.

- [ ] **Step 1:** 실패하는 테스트: `Decode_TwoHandsFixture_MatchesJson`(Task 3 픽스처 `.bin`을 읽어 `.json` 값과 비교, `mirrorX` false), `Decode_MirrorsX_WhenRequested`(x → 1−x), `Decode_RejectsBadMagic`, `Source_ReportsCameraMissing_AfterTimeout`(가짜 시계로 2.0초 경과), `Source_BumpsGeneration_WhenSeqResets`(Review Focus 1), `Source_DropsOldest_WhenMoreThan8Queued`. 소켓 대신 테스트용 `IPacketReceiver`를 주입할 수 있게 한다.
- [ ] **Step 2:** 실행 → FAIL.
- [ ] **Step 3:** 구현, `?input=hand`가 아닌 실행 인자 `--input hand|mouse|replay`로 모드를 고른다(`InputModeParser`가 명령줄 인자를 읽게 수정).
- [ ] **Step 4:** EditMode 통과.
- [ ] **Step 5:** 수동 통합: Mac에서 추적 앱을 `--video`로 돌리고 Unity 빌드를 `--input hand`로 실행 → 손 커서가 움직이고 내리치기에 소리가 난다. 30초 로그(`FrameStats`)에서 손 추적이 렌더 fps를 떨어뜨리지 않음을 확인해 `measurements/day3/udp-smoke.md`에.
- [ ] **Step 6:** Commit (허락 시): `feat(input): UDP hand source`

### Task 5: 들판 배치, 지형, 카메라, 배경 그림

**Files:**
- Create: `Scripts/Core/Meadow/MeadowLayout.cs`, `Scripts/Core/Meadow/MeadowTerrain.cs`, `Scripts/Meadow/{Downbeat.Meadow.asmdef, BackdropView.cs, TerrainView.cs, MeadowCameraRig.cs}`, `Shaders/BackdropBlend.shader`, `Editor/MeadowSceneBuilder.cs`, `Scenes/Meadow.unity`, `Tests/EditMode/MeadowLayoutTests.cs`
- Modify: `Scripts/Core/ContentModels.cs`, `Scripts/Core/ContentValidator.cs`, `content/content.json`(`theme.performers` 등 캐릭터 항목 제거, `meadow` 추가)

**Interfaces:**
- Produces: `MeadowLayout(float fieldWidth, float nearZ, float farZ, float horizonScreenY)` — `Vector2 HandToField(float handX01, float handY01)`(화면 아래=가까운 z, 위=먼 z), `float DepthForPitchStep(int step, int pitchSteps)`(높은 음=먼 z). 초기값 `fieldWidth` 40, `nearZ` 2, `farZ` 38, `horizonScreenY` 0.2.
- Produces: `MeadowTerrain.Height(float x, float z, int seed) -> float` — 결정적, 범위 `[0, meadow.terrain.maxHeight]`(초기값 1.2), 경사가 완만해야 함(인접 1m 차이 ≤ 0.25).
- Produces: `SkyArc.Weights(float t01) -> (float blueHour, float mid, float sunset)` (`Scripts/Core/Meadow/SkyArc.cs`) — t 0 = 푸른 저녁만, 0.5 = 연주 중 그림만, 1 = 노을만, 사이는 이웃 두 장만 선형으로 섞음, 합은 항상 1, 범위 밖 t는 0..1로 자름.
- Produces: `BackdropView.SetSunset(float t01)` — `SkyArc.Weights`로 셰이더 `_Sunset`(배경 그림 3장 섞기)을 움직이고, 주광 색·세기와 안개 색도 content `meadow.sky.stops`(3단계: blueHour, mid, sunset 각각 lightColor, lightIntensity, fogColor)를 같은 가중치로 섞어 맞춘다. 대기(에너지 0)는 푸른 저녁에서 시작한다.
- content `meadow.camera`: `height` 1.7, `pitchDeg` −8, `fovDeg` 50(배경 그림 구도에 맞춰 조정하고 값만 바꿈).

- [ ] **Step 1:** 실패하는 테스트: `HandToField_BottomIsNear_TopIsFar`, `HandToField_XSpansFieldWidth`, `DepthForPitchStep_HigherIsFarther`, `Terrain_IsDeterministic`, `Terrain_StaysInRange`, `Terrain_SlopeIsGentle`(격자 표본), `SkyArc_AtZero_IsBlueHourOnly`, `SkyArc_AtHalf_IsMidOnly`, `SkyArc_AtOne_IsSunsetOnly`, `SkyArc_WeightsSumToOne`(0..1을 0.05 간격), `SkyArc_ClampsOutOfRange`.
- [ ] **Step 2:** 실행 → FAIL.
- [ ] **Step 3:** `MeadowLayout`, `MeadowTerrain` 구현. `ContentModels`에 `MeadowDto`(camera, terrain, wind, flowers, quality), 검증기에 범위 검사 추가.
- [ ] **Step 4:** EditMode 통과.
- [ ] **Step 5:** `MeadowSceneBuilder`(batchmode `-executeMethod`)로 `Meadow.unity` 생성: 카메라, 배경 그림 쿼드(먼 곳, 안개 뒤), 지형 메시(지형 함수로 생성), Global Volume(Bloom, Color Adjustments, Tonemapping Neutral; 처음 계획의 ACES는 완성된 배경 그림을 어둡고 탁하게 해서 Neutral로 바꿨다), 안개, 왼쪽 아래 작은 글씨 "개인 시제품 · 실제 전시 설치 아님". `Meadow.unity`를 빌드 첫 장면으로.
- [ ] **Step 6:** 확인: PlayMode 테스트 `MeadowScene_RendersBackdropAndTerrain`(장면 로드, 1초 뒤 화면 캡처를 `measurements/visual/task5.png`로 저장, 배경 그림 영역 평균 밝기 > 0.1). 캡처를 `prototype/meadow-refs/meadow_front_b.png`와 나란히 놓은 비교 이미지를 함께 저장해 사람이 구도를 확인한다.
- [ ] **Step 7:** Commit (허락 시): `feat(meadow): layout, terrain, camera and backdrop`

### Task 6: 바람장과 실시간 풀

**Files:**
- Create: `Scripts/Core/Meadow/WindField.cs`, `Scripts/Meadow/GrassRenderer.cs`, `Shaders/GrassPlacement.compute`, `Shaders/Grass.shader`, `Tests/EditMode/WindFieldTests.cs`, `Tests/PlayMode/GrassPerformanceTests.cs`

**Interfaces:**
- Produces: `readonly struct WindWave(float originX, float originZ, double startTime, float amplitude, float speed, float lifeSec, WindWaveKind kind, float glow)`, `enum WindWaveKind { Sweep, Radial }`(Sweep은 −x 가장자리에서 +x로, Radial은 원점에서 원형으로).
- Produces: `WindWaveGpu` — 셰이더의 구조체와 같은 배치인 blittable struct(`float4 originSpeed`(originX, originZ, speed, kind), `float4 ampLifeGlowAge`(amplitude, lifeSec, glow, age)). 시간 대신 `age = now − startTime`을 넘겨 셰이더가 double을 다루지 않게 한다.
- Produces: `WindField(int capacity)` — `void Add(in WindWave wave)`(가득 차면 가장 오래된 것 교체), `int CopyActive(double now, WindWaveGpu[] destination)`, `float SampleBend(float x, float z, double now)`(CPU 판정용, 셰이더와 같은 식), `float SampleGlow(float x, float z, double now)`. capacity 16.
- 셰이더와 CPU의 공통 식: 파면 거리 `d`(Sweep: `x − (originX + speed·age)`, Radial: `|p − origin| − speed·age`), 세기 `amplitude · exp(−d² / width²) · max(0, 1 − age / lifeSec)`, `width` content `meadow.wind.waveWidth` 1.8m.
- Produces: `GrassRenderer` — 컴퓨트 셰이더로 풀잎 위치(지형 높이 위, 꽃 자리 피함)를 `GraphicsBuffer`에 만들고 `Graphics.RenderMeshIndirect`로 그린다. 매 프레임 `WindField.CopyActive` 결과와 손 위치(최대 2, 반지름 `meadow.wind.handRadius` 2.5m)를 셰이더에 넘긴다. 밀도는 `meadow.quality[tier].grassPerSqm`(초기값 High 400, Low 120). 풀끝 역광 테두리, 깊이 안개, 바람 세기에 따른 밝기.
- content `meadow.wind`: `ambientAmplitude` 0.08, `beatSweep`{amplitude 0.08, energyGain 0.32, downbeatGain 1.4, speed 22, lifeSec 2.4}, `noteRadial`{amplitude 0.35, velocityGain 0.45, speed 9, lifeSec 2.0}.

- [ ] **Step 1:** 실패하는 테스트: `Sweep_FrontMovesWithSpeed`, `Radial_PeaksAtRing`, `Wave_FadesToZeroAtLife`, `Add_ReplacesOldest_WhenFull`, `CopyActive_SkipsExpired`, `Glow_IsZeroForNonGlowWaves`.
- [ ] **Step 2:** 실행 → FAIL.
- [ ] **Step 3:** `WindField` 구현.
- [ ] **Step 4:** EditMode 통과.
- [ ] **Step 5:** `GrassRenderer`와 셰이더 구현. 박마다 Sweep 파도를 넣는 연결은 Task 9에서 하고, 여기서는 디버그 키(Space)로 Sweep, 마우스 클릭으로 Radial을 넣는다. (Task 7에서 마우스 클릭 Radial은 없앴다: 사용자 결정으로 모든 노트가 자기 Radial 파도를 가진다. Space Sweep은 박 연결 전까지 남는다.)
- [ ] **Step 6:** PlayMode `GrassPerformanceTests.High_TierHolds60FpsOnThisMac`(1920×1080, 10초 평균 fps와 프레임 p95를 `measurements/perf/grass-mac.json`에 기록. 이 Mac에서 60fps 미만이면 실패로 표시하고 숫자를 남긴다). 캡처 `measurements/visual/task6.png`.
- [ ] **Step 7:** Commit (허락 시): `feat(meadow): GPU grass with wind field`

### Task 7: 손 연주 → 들판 이벤트, 꽃 3종

**Files:**
- Create: `Scripts/Core/Meadow/{MeadowNoteEvent,FlowerPlacement,FlowerSelector,BloomTimeline,SpeciesMap}.cs`, `Scripts/Meadow/{FlowerField,FlowerMeshFactory,MeadowNoteRouter}.cs`, `Shaders/Flower.shader`, `Tests/EditMode/{FlowerPlacementTests,FlowerSelectorTests,BloomTimelineTests,MeadowNoteRouterTests}.cs`

**Interfaces:**
- Consumes: `ScheduledNote`(기존), `MeadowLayout`(Task 5), `WindField`(Task 6), `HandMotionTracker`(기존).
- Produces: `enum FlowerSpecies { Bellflower, Dandelion, Poppy }` — `Part.Strings`↔`Bellflower`, `Part.Winds`↔`Dandelion`, `Part.Perc`↔`Poppy`.
- Produces: `SpeciesMap.Weight(FlowerSpecies s, float x01) -> float` — 세 구역 중심 0.17/0.5/0.83, 경계 섞임 폭 `meadow.flowers.blend` 0.08. 악기 선택은 기존 `Parts.FromThirds` 그대로(경계는 화면 표현만 섞인다).
- Produces: `readonly struct MeadowNoteEvent(Part part, int pitchStep, float velocity, double audibleTime, Vector2 fieldPos)` — `fieldPos`의 x는 연주한 손의 x, z는 `DepthForPitchStep`.
- Produces: `FlowerPlacement.Generate(int seed, int count, MeadowLayout layout) -> FlowerInstance[]`(`FlowerInstance(FlowerSpecies species, float x, float z, float scale)`), 종류는 `SpeciesMap` 가중치로 뽑는다. `FlowerSelector.Select(FlowerInstance[] flowers, FlowerSpecies species, Vector2 target, int count, List<int> output)` — 같은 종류 중 가까운 순. 피는 수 `count = 1 + round(velocity · meadow.flowers.maxPerNote)`(초기값 6).
- Produces: `BloomTimeline.Value(double now, double audibleTime) -> float` — `audibleTime − 0.05`부터 0.35초에 0→1, 3초 유지, 4초에 걸쳐 0.15(반쯤 오므린 봉오리)로. 값은 `meadow.flowers.bloom`.
- `MeadowNoteRouter`가 예약된 음마다 `MeadowNoteEvent`를 만들어 꽃 선택·피움, 하프 음은 `glow` 있는 Radial 파도(풀끝 반짝임), 모든 음에 Radial 바람 파도를 넣는다.
- 꽃 메시: `FlowerMeshFactory`가 코드로 만든다(초롱꽃=종 모양 꽃잎 5장 아래로, 민들레=솜털 구+줄기, 양귀비=넓은 꽃잎 4장). 봉오리↔핀 상태는 셰이더 `_Bloom`(인스턴스별 버퍼).
- content `meadow.flowers`: `count` 1800(High)/700(Low), `maxPerNote` 6, `blend` 0.08, `bloom`.

- [ ] **Step 1:** 실패하는 테스트: `Placement_IsDeterministic`, `Placement_LeftThirdIsMostlyBellflower`(80% 이상), `Selector_ReturnsNearestOfSpecies`, `Selector_CountGrowsWithVelocity`, `Bloom_OpensAtAudibleTime`, `Bloom_SettlesToBudLevel`, `Router_TwoHandsDifferentZones_BothBloom`(Review Focus 2: 같은 박에 Strings·Perc 두 음 → 두 종류 모두 선택), `Router_HarpAddsGlowWave`.
- [ ] **Step 2:** 실행 → FAIL.
- [ ] **Step 3:** Core 클래스 구현.
- [ ] **Step 4:** EditMode 통과.
- [ ] **Step 5:** `FlowerField`, `FlowerMeshFactory`, `Flower.shader`, `MeadowNoteRouter` 구현하고 `DownbeatApp`의 기존 화면 출력(`NoteVisualQueue` 소비)을 `MeadowNoteRouter`로 바꾼다. 기존 `StageView`·`PerformerView`·`AudienceView`·`SpriteFactory`는 `archive/`로.
- [ ] **Step 6:** 확인: 녹화 재생(`--input replay`)으로 20초 실행해 캡처 3장(`measurements/visual/task7-*.png`)과 함께 "피는 꽃 순간 − 소리 예약 시각" 차이를 로그로 재서 `measurements/day3/bloom-sync.json`(중앙값 |차이| ≤ 1프레임).
- [ ] **Step 7:** Commit (허락 시): `feat(meadow): flower species as instruments`

### Task 8: 입자와 손 바람 줄기

**Files:**
- Create: `Scripts/Core/Meadow/ParticleCues.cs`, `Scripts/Meadow/{ParticleDirector,HandWindView}.cs`, `Shaders/WindRibbon.shader`, `Tests/EditMode/ParticleCuesTests.cs`

**Interfaces:**
- Consumes: `MeadowNoteEvent`(Task 7), `HandMotionTracker`, `MeadowLayout`.
- Produces: `ParticleCues.ForNote(in MeadowNoteEvent e) -> ParticleCue`(`ParticleCue(ParticleKind kind, int count, Vector2 fieldPos, double startTime)`; `Winds`→`DandelionSeeds` count `4 + round(8·velocity)`, `Perc`→`Petals` count `3 + round(6·velocity)`, `Strings`→`None`(풀끝 반짝임으로 대신)).
- `ParticleDirector`: 홀씨·꽃잎(바람장 방향으로 흐름), 반딧불(개수는 Task 9의 에너지가 정함, 여기서는 `SetFireflyLevel(float)`만), 꽃가루(항상 조금). Unity 기본 Particle System(Shuriken) 사용, 텍스처는 코드로 만든 둥근 그라디언트.
- `HandWindView`: 손마다 빛 리본(TrailRenderer, 가산 혼합, 길이 0.6초). 손 위치는 `MeadowLayout.HandToField` 뒤 지형 높이 + 0.4m. 손이 사라지면 0.5초에 걸쳐 흐려진다.

- [ ] **Step 1:** 실패하는 테스트: `Winds_EmitsSeeds_ScaledByVelocity`, `Perc_EmitsPetals`, `Strings_EmitsNothing`.
- [ ] **Step 2:** FAIL 확인 → **Step 3:** 구현 → **Step 4:** 통과.
- [ ] **Step 5:** 뷰 구현, 녹화 재생으로 캡처 `measurements/visual/task8.png`, 성능 재측정(`GrassPerformanceTests`와 같은 방식, 입자 포함)을 `measurements/perf/particles-mac.json`에.
- [ ] **Step 6:** Commit (허락 시): `feat(meadow): particles and hand wind ribbon`

### Task 9: 연주 에너지, 흐름 상태, 자연 악기

**Files:**
- Create: `Scripts/Core/Nature/{PerformanceEnergy,ExperienceStateMachine,NatureLayerConductor}.cs`, `Scripts/App/NatureAudioBridge.cs`, `Scripts/Meadow/NatureVisuals.cs`, `Tests/EditMode/{PerformanceEnergyTests,ExperienceStateMachineTests,NatureLayerConductorTests}.cs`, `tools/prepare_nature_samples.mjs`
- Modify: `content/content.json`(`nature`, `experience`, `instruments`에 자연 악기 3종), `content/audio/*`, `NOTICE`, `Scripts/App/DownbeatApp.cs`

**Interfaces:**
- Produces: `PerformanceEnergy(EnergySettings s)` — `AddNote(float velocity)`(오름 `0.06 + 0.06·velocity`), `Advance(double dtSec)`(반감기 6초), `float Value`(0~1), `int Level`(0~3, 기준 0.2/0.45/0.7, 내려갈 때 0.05 히스테리시스).
- Produces: `enum ExperienceState { Idle, Waking, Playing, Settling }`, `ExperienceStateMachine(ExperienceSettings s)` — `Update(double now, bool handsPresent, bool noteFired, float energy)`, `ExperienceState State`, `event Action<double> SessionStarted`, `event Action<double> SessionEnded`. 규칙: Idle→Waking 손 등장, Waking→Playing 첫 음 또는 1.0초, Playing→Settling 손 없음 3.0초, Settling→Playing 손 재등장, Settling→Idle 에너지 < 0.05 그리고 Settling 6초 이상(이때 `SessionEnded`). 손 추적 끊김은 손 없음과 같다(5초 안에 대기 흐름, 전역 조건).
- Produces: `NatureLayerConductor(BeatGrid grid, int slotsPerBar, int seed)` — `void Plan(long fromSlot, long toSlot, int level, List<NatureNote> output)`; `NatureNote(NatureLayer layer, long slot, int pitchStep, float gain)`, `enum NatureLayer { Fireflies, Sky, Hills }`. 규칙(초기값): Level ≥ 1 반딧불=종소리, 박 사이 슬롯(`slot % 4 == 2`)에서 확률 0.35, 음은 위쪽 다섯 음 중 시드 난수. Level ≥ 2 하늘=긴 화음, 마디 첫 슬롯에서 5음 음계 인덱스 [0,1,3](A·C·E)와 [1,3,4](C·E·G)를 2마디마다 교대(실제 음은 `PitchMapper`로). Level 3 언덕=낮은 북, 마디 첫 슬롯과 셋째 박(`slot % 16 == 8`, gain 0.6).
- 소리 재료: VCSL 고정 커밋에서 종소리(글로켄슈필·차임), 지속음(오르간·현·목소리 등), 낮은 북(프레임 드럼·베이스 드럼·팀파니)을 찾는다. 지속음이 없으면 `prepare_nature_samples.mjs`로 직접 합성한 WAV를 만든다(사인파 3개 겹침, 어택 0.4초, 길이 4초). 출처·합성 여부를 `NOTICE`와 `content/audio/LICENSES.md`에.
- `NatureVisuals`: 에너지 → `BackdropView.SetSunset(energy)`, `ParticleDirector.SetFireflyLevel(level ≥ 1 ? energy : 0)`, Level 3에서 북이 울릴 때 먼 언덕 안개 밝기 맥동. 박마다 Sweep 파도(`meadow.wind.beatSweep`)를 넣는 것도 여기서.

- [x] **Step 1:** 실패하는 테스트: `Energy_RisesWithNotes_DecaysWithHalfLife`, `Energy_LevelUsesHysteresis`, `State_IdleToWakingOnHand`, `State_SettlesAfter3sWithoutHands`, `State_EndsSessionWhenEnergyLowAfter6s`, `State_TrackerLossCountsAsNoHands`, `Conductor_NoNotesAtLevel0`, `Conductor_FirefliesOnlyOnOffbeats`, `Conductor_SkyChordEveryTwoBars`, `Conductor_DrumsOnlyAtLevel3`, `Conductor_IsDeterministicForSeed`.
- [x] **Step 2:** FAIL → **Step 3:** 구현 → **Step 4:** 통과.
- [x] **Step 5:** 소리 재료 준비, `NatureAudioBridge`(자연 음을 `IAudioEngine.Schedule`로 예약, 손 악기보다 −6dB 기본. 자연 악기는 정해진 악보라서 소리 시계 기준 최소 1마디(`nature.leadBars` 1, 96 BPM에서 2.5초) 앞서 예약한다. 검토 1: Mac 창 모드에서 본 약 1초 멈춤에도 자연 음이 빠지지 않게. 손 연주 음은 지금처럼 0.1초 앞 예약), `NatureVisuals`, `DownbeatApp` 연결.
- [x] **Step 6:** 확인: 녹화 재생을 3번 이어 붙여 60초 실행 → 에너지 단계가 0→3으로 오르고 손을 치운 뒤 Idle로 돌아오는 로그(`measurements/day4/experience-run.json`)와 단계별 캡처 4장, 오디오 탭 녹음(Task 2의 `AudioOutputTap`)을 남긴다. 귀로 듣는 판정은 사용자에게 요청한다.
- [x] **Step 7:** Commit (허락 시): `feat(nature): energy, experience flow and nature layers`

### Task 10: 화면 배치와 실시간 꽃 악보

**Files:**
- Create: `Scripts/Core/Record/{DisplayPlan,ScoreModel,ScoreLayout}.cs`, `Scripts/App/DisplayRouter.cs`, `Scripts/Record/{Downbeat.Record.asmdef, RecordScreenView.cs, FlowerIconPainter.cs}`, `UI/RecordScreen.uxml`, `UI/RecordScreen.uss`, `Tests/EditMode/{DisplayPlanTests,ScoreModelTests,ScoreLayoutTests}.cs`

**Interfaces:**
- Produces: `enum DisplayMode { Dual, SingleOverlay }`, `DisplayPlan.Resolve(int displayCount, string preference) -> DisplayMode`(`preference` = content `display.mode`: "auto"|"dual"|"single"; "auto"는 2대 이상이면 Dual). 
- `DisplayRouter`: Dual이면 `Display.displays[1].Activate()`, 기록 카메라 `targetDisplay = 1`, 들판은 0. 어느 모니터가 들판인지 `display.meadowIndex`(초기값 0). SingleOverlay면 기록 화면을 들판 아래 28% 띠로 겹쳐 띄우고 Tab 키로 숨김·보임.
- Produces: `ScoreModel(int capacity)` — `Add(ScoreMark mark)`(`ScoreMark(double time, Part part, int pitchStep, float velocity)`), `SetLayerActive(NatureLayer layer, bool active, double time)`, `CopyVisible(double now, double windowSec, List<ScoreMark> output)`. capacity 256, 창 8초.
- Produces: `ScoreLayout.Position(in ScoreMark m, double now, double windowSec, float width, float height, int pitchSteps) -> Vector2`(오른쪽 끝=지금, 위=높은 음).
- `RecordScreenView`(UI Toolkit): 위쪽 오선과 꽃 아이콘(`FlowerIconPainter`가 `Painter2D`로 그린 초롱꽃·민들레·양귀비), 아래쪽 자연 악기 줄 3개(반딧불·하늘·언덕, 합류 시 켜짐), 맨 아래 카드 목록 자리(Task 11).

- [x] **Step 1:** 실패하는 테스트: `Resolve_OneDisplay_IsSingleOverlay`(Review Focus 3), `Resolve_TwoDisplays_Auto_IsDual`, `Resolve_ForcedSingle_WithTwoDisplays`, `Score_DropsOldest_WhenFull`, `Score_CopyVisible_RespectsWindow`, `Layout_NowIsRightEdge`, `Layout_HigherPitchIsHigher`.
- [x] **Step 2:** FAIL → **Step 3:** 구현 → **Step 4:** 통과.
- [x] **Step 5:** 뷰 구현과 연결(예약된 음 → `ScoreModel.Add`, 자연 악기 단계 → `SetLayerActive`). Mac에서 SingleOverlay 캡처 `measurements/visual/task10-single.png`. Dual은 Task 14의 Windows 세션에서 확인.
- [x] **Step 6:** Commit (허락 시): `feat(record): display routing and live flower score`

### Task 11: 오늘의 들판 카드

**Files:**
- Create: `Scripts/Core/Record/{SessionRecorder,SessionCard,CardStore}.cs`, `Scripts/App/{CardStoreIO,ClimaxSnapshot}.cs`, `Scripts/Record/CardListView.cs`, `Tests/EditMode/{SessionRecorderTests,CardStoreTests}.cs`

**Interfaces:**
- Consumes: `ExperienceStateMachine.SessionStarted/SessionEnded`(Task 9), `ScheduledNote`, `PerformanceEnergy.Level`.
- Produces: `SessionCard`(불변): `string Id`, `DateTime StartedLocal`, `double DurationSec`, `int NoteCount`, `int MaxLevel`, `IReadOnlyList<ScoreMark> ScoreLine`(최대 64개로 줄임), `string ThumbnailFile`.
- Produces: `SessionRecorder` — `OnSessionStarted(double now, DateTime localNow)`, `OnNote(in ScheduledNote note)`, `OnLevel(int level, double now)`(최고 단계가 처음 될 때 `ClimaxReached` 이벤트), `SessionCard OnSessionEnded(double now)`. 음이 3개 미만인 세션은 카드를 만들지 않는다(`null`).
- Produces: `CardStore(ICardFileSystem fs, string rootDir)` — `Save(SessionCard card, byte[] thumbnailPng)`, `IReadOnlyList<SessionCard> LoadDay(DateTime localDate)`(깨진 JSON은 건너뛰고 경고), `string DayFolder(DateTime localDate)` = `root/yyyy-MM-dd/`. 루트는 `Application.persistentDataPath/cards`. 화면에는 최신 12장.
- `ClimaxSnapshot`: `ClimaxReached` 때 들판 카메라를 480×270 RenderTexture로 한 번 렌더해 PNG로.
- 날짜 바뀜: `CardListView`가 1분마다 오늘 날짜를 확인하고 바뀌면 목록을 새 날짜로 다시 읽는다.

- [x] **Step 1:** 실패하는 테스트: `Recorder_NoCardForTinySession`, `Recorder_CardHasCountsAndMaxLevel`, `Recorder_ScoreLineIsCappedAt64`, `Store_SaveThenLoadDay_RoundTrips`(가짜 파일 시스템), `Store_SkipsCorruptJson`, `Store_DayFolderChangesAtMidnight`(23:59:30에 저장한 카드와 00:00:30에 저장한 카드가 다른 폴더, Review Focus 4).
- [x] **Step 2:** FAIL → **Step 3:** 구현 → **Step 4:** 통과.
- [x] **Step 5:** `CardStoreIO`, `ClimaxSnapshot`, `CardListView` 연결. 녹화 재생 세션 2번 → 카드 2장이 기록 화면에 보이는 캡처 `measurements/visual/task11.png`.
- [x] **Step 6:** Commit (허락 시): `feat(record): today's meadow cards`

### Task 12: 실행 파일 밖 콘텐츠와 되돌리기

**Files:**
- Modify: `Scripts/Content/{ContentLocator,ContentRepository}.cs`, `Scripts/Core/ContentValidator.cs`, `Editor/NativeBuild.cs`(빌드 뒤 `content/`를 실행 파일 옆과 `StreamingAssets/default-content/`에 복사)
- Create: `Tests/EditMode/ContentLocatorTests.cs`, `Tests/EditMode/ContentValidatorNativeTests.cs`

**Interfaces:**
- Produces: `ContentLocator.Resolve(string exeDir, string streamingAssetsDir, string persistentDir, IFileProbe probe) -> ContentSource`(`ContentSource(string rootDir, ContentOrigin origin, string reason)`, `enum ContentOrigin { External, LastKnownGood, BuiltIn }`). 순서: 실행 파일 옆 `content/`(Mac은 `.app` 옆) → 검증 실패나 없음이면 `persistentDataPath/last-known-good/` → 그것도 없으면 `StreamingAssets/default-content/`. 외부 콘텐츠가 검증을 통과하면 last-known-good로 복사한다.
- 검증 대상(설계 7절, 기획서 5종): JSON 깨짐, 없는 파일 참조, 범위 밖 값, 중복 id, 손상된 이미지·WAV. 실행 중 다시 읽기는 F5 키.

- [x] **Step 1:** 실패하는 테스트: `Resolve_UsesExternal_WhenValid`, `Resolve_FallsBackToLastKnownGood_WhenExternalBroken`, `Resolve_UsesBuiltIn_WhenNothingElse`(Review Focus 5, 이유 문자열 포함), `Validator_Rejects{BrokenJson,MissingFile,OutOfRange,DuplicateId,CorruptWav,CorruptImage}`, `Validator_AcceptsShippedContent`.
- [x] **Step 2:** FAIL → **Step 3:** 구현 → **Step 4:** 통과.
- [x] **Step 5:** Mac 빌드로 수동 확인: 실행 중 `content.json`의 `meadow.wind.beatSweep.amplitude`를 바꾸고 F5 → 다음 마디부터 반영, 깨진 JSON으로 바꾸고 F5 → 화면 그대로·로그에 이유. 결과 `measurements/day5/content-swap.md`.
- [ ] **Step 6:** Commit (허락 시): `feat(content): external content with last-known-good fallback`

### Task 13: 상태 로그와 하트비트

**Files:**
- Create: `Scripts/Core/Ops/HealthAccumulator.cs`, `Scripts/App/HealthLogger.cs`, `Tests/EditMode/HealthAccumulatorTests.cs`

**Interfaces:**
- Produces: `HealthAccumulator` — `AddFrame(float deltaSec)`, `AddTrackerPacket(double ageMs)`, `SetHandsPresent(bool)`, `HealthRow Snapshot(DateTime utcNow, double uptimeSec, long monoBytes, long totalAllocatedBytes, AudioStats audio, int errors)` 후 내부 누계 초기화. `HealthRow.ToCsv()`와 `HealthRow.CsvHeader`: `timestamp_utc,uptime_s,fps_avg,fps_min_1s,frame_ms_p95,frame_ms_max,frames_over_100ms,mono_mb,alloc_mb,tracker_fps,tracker_age_ms_p95,hands_present_ratio,notes_scheduled,notes_late,voices_stolen,errors,experience_state,energy_level`.
- `HealthLogger`: 60초마다 `<exe dir>/logs/health-yyyyMMdd.csv`에 한 줄(날짜가 바뀌면 새 파일), 5초마다 `<exe dir>/logs/heartbeat.txt`에 UTC 시각 한 줄. 로그 폴더를 못 쓰면 `persistentDataPath/logs`로.

- [x] **Step 1:** 실패하는 테스트: `Snapshot_ComputesFpsAndP95`, `Snapshot_ResetsAfterRow`, `Csv_HeaderMatchesColumns`, `Csv_UsesInvariantCulture`(소수점이 쉼표인 지역 설정에서도 `.`).
- [x] **Step 2:** FAIL → **Step 3:** 구현 → **Step 4:** 통과.
- [x] **Step 5:** Mac 빌드 실행 → CSV 줄 생김, 하트비트 갱신 확인(5분 대신 `--health-interval 10`으로 140초: 행 14개).
- [x] **Step 6:** Commit (허락 시): `feat(ops): health csv and heartbeat`

### Task 14: 감시 앱, Windows 설치·자동 실행, 실행 순서표

**Files:**
- Create: `supervisor/pyproject.toml`, `supervisor/downbeat_supervisor/*.py`, `supervisor/tests/{test_restart,test_heartbeat,test_events}.py`, `supervisor/configs/{windows,mac}.json`, `ops/windows/{setup.ps1,install-autostart.ps1,uninstall-autostart.ps1,collect-results.ps1,RUNBOOK.md}`, `ops/mac/run-demo.command`

**Interfaces:**
- Produces: `python -m downbeat_supervisor --config PATH` — 설정의 프로세스(추적 앱, Unity)를 켜고, 꺼지면 `restartDelaySec` 3초 뒤 다시 켠다. Unity 하트비트가 `heartbeatTimeoutSec` 30초 넘게 멈추면 Unity를 끄고 다시 켠다. 10분 안에 5번 넘게 다시 켜지면 60초 쉬고 이어간다. 모든 사건을 `logs/supervisor-yyyyMMdd.csv`(`timestamp_utc,process,event,detail`)에.
- `setup.ps1`: Python 3.12 확인(없으면 설치 안내만 출력하고 멈춤) → `tracker`·`supervisor` 가상환경 생성과 설치 → 모델 파일 미리 받기 → `env-report.json` 생성(GPU, 모니터, 카메라, 파이썬, 디스크; 오늘 밤 점검표의 명령과 같은 항목).
- `install-autostart.ps1`: 작업 스케줄러에 '로그온 시 감시 앱 실행'(현재 사용자, 관리자 권한 불필요한 방식 우선). `uninstall-autostart.ps1`은 되돌림.
- `collect-results.ps1`: `logs/`, `measurements/` 결과, `env-report.json`을 날짜 이름 zip으로 바탕화면에.
- `RUNBOOK.md`(한국어, 사용자가 혼자 따라 하는 순서표): 세션 A 설치와 첫 실행(화면 2대 배치 확인, Dual 캡처), 세션 B 장애 주입 6종(카메라 뽑기·꽂기, 추적 앱 강제 종료, Unity 강제 종료, `content.json` 손상, 소리 파일 삭제, 오디오 장치 분리) — 각 단계에서 감시 앱 로그에 시각이 남으니 사용자는 표에 '언제 했는지'만 적는다, 세션 C 8시간 구동(휴대폰 손 영상 반복 재생을 웹캠 앞에, 안 되면 `--input replay`), 세션 D 지연 측정(Task 15).

- [x] **Step 1:** 실패하는 테스트(가짜 자식 프로세스 = 작은 Python 스크립트): `test_restarts_after_exit`, `test_restarts_unity_on_stale_heartbeat`, `test_backs_off_after_5_restarts_in_10_min`, `test_events_csv_rows`.
- [x] **Step 2:** `cd supervisor && python3.12 -m pytest -q` → FAIL.
- [x] **Step 3:** 구현 → **Step 4:** 통과.
- [x] **Step 5:** PowerShell 스크립트와 `RUNBOOK.md` 작성. Mac에는 PowerShell이 없으므로 스크립트는 사용자가 Windows 세션 A에서 처음 실행한다. 실행 결과(zip)를 받아 고칠 점을 반영하는 단계를 RUNBOOK 끝에 둔다.
- [x] **Step 6:** `ops/mac/run-demo.command`: 추적 앱과 Unity를 함께 켜는 면접 시연용 실행 파일(카메라가 없으면 `--input replay`).
- [x] **Step 7:** Commit (허락 시): `feat(ops): supervisor, windows setup and runbook`

### Task 15: 지연 측정 모드

**Files:**
- Create: `Scripts/App/LatencyMarker.cs`, `tools/latency_from_marks.py`, `tools/tests/test_latency_from_marks.py`, `docs/latency-method.md`

**Interfaces:**
- `LatencyMarker`: 실행 인자 `--latency-mode`일 때, 예약된 손 음마다 들리는 시각에 들판 화면 오른쪽 위 120×120px 흰 사각형을 1프레임 켠다(휴대폰 영상에서 소리·화면을 함께 보기 위해). 소리는 그대로.
- `tools/latency_from_marks.py --csv marks.csv --fps 240`: 열 `trial,hand_down_frame,sound_frame,flash_frame` → 시도별 손→소리 ms, 손→화면 ms와 중앙값·p90·최대를 JSON으로. 빈 칸 행은 건너뛰고 개수를 보고.
- `docs/latency-method.md`: 휴대폰 슬로모션으로 손·화면·스피커를 한 장면에 담는 법, 프레임 세는 법, 30회 이상.

- [x] **Step 1:** 실패하는 테스트: `test_computes_ms_from_frames`, `test_skips_incomplete_rows`, `test_reports_median_p90_max`.
- [x] **Step 2:** FAIL → **Step 3:** 구현 → **Step 4:** 통과.
- [ ] **Step 5:** Commit (허락 시): `feat(measure): latency marker and analysis`

### Task 16: 시연 자료와 결과 문서

> **공개 전 주의(검토 2, 2026-10-29):** 지금까지의 `.git`은 약 356 MiB이고 이력에 큰 PNG와 `/Users/...` 절대 경로가 들어 있다. 이력을 다시 쓰지 않는다. 공개 저장소는 **새로 squash한 브랜치(이력 없이 현재 파일만)**로 만들어, 무거운 이력과 개인 경로가 공개되지 않게 한다. 캡처와 측정 원본은 이제 `captures/`(무시됨)에 쌓이고, 추적하는 것은 작업마다 JPEG(q85, ≤1920 px) 1~2장과 JSON뿐이다(`DOWNBEAT_KEEP_CAPTURES=1`로 일부러 복사).


**Files:**
- Create: `content/replay/meadow-demo.json`(사용자가 Windows 세션에서 녹화한 실제 손 또는 가짜 손 영상 녹화), `docs/meadow-results.md`, `docs/media/meadow-demo.mp4`, `docs/media/meadow-*.png`
- Modify: `README.md`, `NOTICE`

- [ ] **Step 1:** 녹화 재생 결정성 테스트(기존 `ReplayTests`)에 새 녹화 파일을 추가 → 같은 파일 두 번 실행 시 같은 음 목록.
- [ ] **Step 2:** Unity Recorder(패키지 `com.unity.recorder`)로 녹화 재생 60초를 1920×1080 영상으로 만들고, `AudioOutputTap` 녹음을 붙인다. Dual 화면 영상은 Windows 세션에서 화면 녹화로 1컷.
- [ ] **Step 3:** `docs/meadow-results.md`: 무엇이 되는지, 측정값(네이티브 박자 시계, fps, 손 추적 fps, 지연, 8시간 로그 그래프, 장애 주입 결과)과 조건, 시험하지 않은 것, 직접 만든 것과 가져온 것 목록.
- [ ] **Step 4:** `docs/meadow-results.md` 끝에 "숫자 ↔ 원본" 표(숫자, 문장 위치, `measurements/` 파일 경로)를 만들고, 표에 없는 숫자가 본문에 없음을 확인.
- [ ] **Step 5:** Commit (허락 시): `docs: meadow orchestra results and demo media`

### Task 17: 지휘하듯 연주하기 (conducting play)

**왜:** Windows 노트북(i7-10610U, Intel UHD)에서 손 추적이 중앙값 6장/초(한 장 141 ms)까지 떨어져서 내려치기(속도를 70 ms 창 안의 앞 장과 견주는 규칙)로는 첫 1분에 0개 음이 났다. 사용자가 10-30에 정한 것: **손 높이 = 소리 크기, 가만히 든 손은 작게 계속 연주, 많이 움직일수록 촘촘히.** 설계 메모: `docs/conducting-play.md`(Windows 쪽, 156c91c). 만든 대로는 그 문서 7절.

**Mac 쪽이 정한 것 (메모가 비워 둔 곳):**
1. 악구 자리: 목소리마다 음 하나에 한 칸씩 가고, 마디 첫 박마다 처음으로 돌아간다.
2. 에너지: 연주한 음의 에너지에 몫을 곱한다: 가만히 든 손(움직임이 `stillPalms` 미만) 0.25, 움직이면 0.40. 에너지 상수와 자연 층 문턱(0.2/0.45/0.7)은 그대로. 30초 시뮬레이션(진짜 엔진과 `PerformanceEnergy`): 가만히 1단계(0.26~0.39), 천천히 움직이면 2단계(0.51~0.62), 보통 이상 3단계(0.85 이상). `measurements/day5/conduct-energy-sim.json`.
3. 내려치기는 처음 판에 없다. 옛 길은 `conduct.mode: "strike"` 뒤에 그대로 있다.
4. 기본 방식은 `conduct`.
5. 실행 옵션 `--play-mode conduct|strike`가 내용을 덮어쓴다. 지연 측정(`--latency-mode`, `-LatencyMode`)은 내려친 순간부터 재므로 `strike`를 저절로 붙인다(앱과 두 스크립트 모두). `-PlayMode`는 세 스크립트에도 더했다.
6. 마우스 입력은 그대로.
7. 녹화 손과 `--soak`은 조건 방식에서도 음이 난다(`demo-hands.json` 16초에 114개 음, 같은 입력은 같은 음). EditMode와 PlayMode 시험.
8. 내용: `conduct` 블록(`content-build.mjs`, C# `ConductDto`, `ConductContentValidator`의 범위와 `stillPalms < halfBeatPalms < quarterBeatPalms`, 악구 칸 < `pitchSteps`). 메모에 없던 `holdMs`(800 ms)를 더했다: `hand.maxGapMs`(250 ms)로는 손 추적이 6장/초일 때 한 번 놓친 손이 사라진 것이 된다.
9. 메모의 가정 중 하나는 맞지 않았다: `QuantizedNoteScheduler`는 `RequestedAt`을 "그 칸"이 아니라 "지금"으로 읽는다. 그래서 칸의 시각에서 `minLeadSec`를 뺀 시각을 요청 시각으로 준다.

- [x] **Step 1:** 시험 먼저: 메모 §5의 모든 경우(가만히 든 손 1박 1음·셈여림, 움직임 2와 5의 간격, 리코더 최소 2칸, 높이 순서, 손이 나가고 돌아옴, 손 추적 6장/초와 들쭉날쭉 5~30장/초, 같은 칸 두 번 없음, 두 손 한 파트, 녹화 손). 변이 검사로 시험이 민감한지 확인했다(데드밴드, 마디 악구 되돌리기, 칸 요청 시각, 옛 그림을 본 것으로 치기).
- [x] **Step 2:** 구현(`ConductEngine`/`ConductPlayer`, `HandPlayInput`, `NoteIntent.EnergyWeight`, `PerformanceEnergy.AddNote(v, weight)`).
- [x] **Step 3:** 내용과 검증(`conduct` 블록, JS 시험 3개, `ConductContentTests`).
- [x] **Step 4:** 진짜 장면 PlayMode 시험(녹화 손을 카메라로, 기본은 conduct, `--play-mode strike`는 옛 길).
- [x] **Step 5:** 스크립트와 문서(`-PlayMode`, `-LatencyMode`가 strike, 손 설치 안내 5번, 지연 측정 안내).
- [ ] **Step 6 (사용자, Windows):** 가만히 든 손·흔든 손·휘저은 손의 느낌과 손 추적 6장/초일 때의 음 수.

### Task 18: 약한 노트북에서도 돌게 (Part B 가벼운 Low, Part C 자동 화질)

**왜:** Windows 노트북(Intel UHD)이 Low 에서도 그래픽 칩 93%로 꽉 차고, 칩과 CPU가 한 전력을 나눠 쓰니 손 추적이 굶는다. 사용자는 처음 보는 면접용 노트북에서도 아무도 설정을 고르지 않고 잘 돌기를 원한다.

**B (가벼운 Low):** `meadow.quality` 등급마다 `renderScale`, `upscaler`, `msaa`, `targetFps`, `bloom`(필터·다운스케일·반복), `skyDetail`(검증됨). Low: 0.7배 크기를 Linear로 늘리고, Dual/Quarter/4 번짐, 구름과 먼 들판 일렁임 빼기(나중에 Part D에서 바꿈: 배경이 정적이라는 지적으로 이제는 빼지 않고 구운 구름 무늬 판에서 읽는다), 30fps 상한(수직 동기화가 켜져 있으면 Unity가 상한을 무시하므로 상한이 있는 동안 끈다). `--render-scale X`, `--max-fps N`(스크립트 `-MaxFps`). **코디네이터 메모와 달랐던 것:** Low 의 MSAA 는 이미 꺼져 있었다(High 4, Low 1; URP 자산); 2배로 올리면 +10%라 그대로 둔다. URP 업스케일러는 Auto/Bilinear/Nearest/FSR 1.0(셰이더 모델 4.5 필요)/STP(컴퓨트)이고 안전한 Linear를 골랐다. `GraphicsSettings.currentRenderPipeline`은 등급을 바꾼 그 프레임에는 옛 자산을 가리켜서 등급의 자산을 인덱스로 가져온다. Mac(M4 Pro, 1080p 창, 상한 없이, 3번 평균, `measurements/day5/render-levers-mac.json`)에서 Low 프레임 시간 1.23 ms 기준: 0.85배 −10%, 0.7배 −19%, 0.6배 −22%, 구름·일렁임 빼기 −6%(구름·일렁임을 빼던 때의 값), 번짐 끄기 −5%(Dual/Quarter/4로만 바꾸면 변화 없음), MSAA 2배 +10%, 세 가지 합쳐 −25%; High: 0.85배 −5%. 이 Mac의 GPU는 빠르고 타일 방식이라 Intel UHD 숫자가 아니다. FrameTimingManager의 GPU 시간은 Metal에서 나오지만 GPU 클럭이 부하에 따라 바뀌어 값이 흔들려 비교에는 쓰지 않았다. 감시 프로그램은 손 추적을 AboveNormal 우선순위로 켜고(`priority`), `play-with-hands.ps1`도 켠 뒤 준다. 문서와 한 줄 명령은 `C:\Downbeat`를 먼저 찾고(OneDrive가 바탕화면을 동기화하며 0.8코어를 썼다), 충전기와 사용자가 직접 정하는 최고 성능 전원 모드를 적었다.

**C (자동 화질):** `--quality Auto`(기본)이거나 `--quality`가 없으면 자동. `High|Low`, `--render-scale`, `--max-fps`는 고정. 시작: 그래픽 장치 이름 말(Intel, UHD, Iris … 약한 쪽, Arc/GeForce/RTX/Apple M 강한 쪽)과 메모리(2048 MB 미만)로 사다리의 첫 단계를 고르고 이유를 로그에 남긴다; 켠 뒤 8초가 확인한다. 사다리 High@1.0 → High@0.85 → Low@1.0 → Low@0.85 → Low@0.7 → Low@0.6(`autoQuality` 블록, 모두 내용값과 검증 범위). 내려가는 때: 최근 4초의 절반 넘는 시간이 예산의 1.3배를 넘는 프레임, 또는 손 추적이 1초에 12장 아래로 8초 넘고 앱이 바쁠 때(`gpu_ms`가 있으면 예산 절반 넘게 쓸 때). 올라가는 때: 90초 머문 뒤 4초 동안 느린 프레임 0과 GPU 여유; 올랐다가 30초 안에 내려오면 다음 시도 대기가 두 배(최대 30분)이고 한 번 버티면 하나씩 용서. 등급(풀·꽃) 바꿈은 들판이 Idle일 때만, 그 전에는 그림 크기만 먼저 내려간다. 크기 변화는 0.5초에 걸쳐 0.025씩. `[quality]` 로그, 상태 CSV 끝에 `render_scale`, `quality_level`(-1 고정). 시험: `AutoQualityControllerTests` 27개(시뮬레이션: GPU 부하·손 추적 굶음·여유 뒤에만 오름·1시간 경계 시계열에서 변경이 갈수록 느려짐·사다리 한계·Idle 대기·시작 단계), `QualityModeTests`, PlayMode `AutoQualityPlayModeTests`(진짜 장면에서 단계 강제, 고정, 숫자 지정). **확인하지 못한 것:** D3D11에서 FrameTimingManager가 GPU 시간을 주는지(그래서 프레임 시간만으로도 판단하고, 올라가는 것은 시도 대기 배가 규칙이 막는다), Windows 전체, Intel UHD에서 Auto가 머무는 단계.
**리뷰(A~C) 뒤 수정 (10-30, 한 커밋 `fix(quality,play): review of A-C`):** (HIGH-1) 등급 바꿈이 연주 때문에 기다리는 동안에도 그림 크기는 사다리 값보다 아래(바닥 0.5)까지 10초마다 0.1씩 내려가요; 등급을 되돌리느라 기다린 시도는 30초 창과 상관없이 실패로 쳐서 다음 시도가 두 배 늦어요; 등급을 넘는 올라감(Low→High)은 연주 중에 본 프레임 20초(느린 프레임 2% 이하)가 있어야 해요; 기다리던 올라감은 프레임이 느려지면 취소해요. (MEDIUM-2) 손 추적 굶음으로 내려간 단계는 20초 안에 장 수가 20% 올라야 하고, 연달아 2번 오르지 않으면 10분 동안 무시해요(프레임 시간만으로 판단); `[quality]` 로그와 CSV `starve_ignored`. (MEDIUM-3) 그림 크기 비율은 창의 카메라에만: `beginCameraRendering` 에서 카메라마다 에셋 값을 바꾸고 `endCameraRendering` 에서 돌려놔서 기록 화면(둘째 화면)과 캡처는 1.0 (URP 17 의 `InitializeCameraData` 가 `BeginCameraRendering` 뒤에 에셋 값을 읽는 것을 소스에서 확인; PlayMode 시험 `OnlyTheWindowIsScaled_ACaptureIsDrawnAtFullScale`). (MEDIUM-4) `--render-scale`/`--max-fps` 만 주면 그 숫자만 고정, 등급은 Auto 가 골라요(사다리는 등급당 한 칸, 예산은 고정한 상한). (L5) 긴 프레임이 건너뛴 마디 줄에서도 악구를 처음으로, (L6) 손 추적이 250 ms 넘게 놓쳤다가 800 ms 안에 되찾으면 움직임을 지키고, 놓친 틈의 거리는 속도로 세지 않아요, (L7) 띠를 넘으면 떠나는 파트는 0.3초에 잦아들어요, (L8) 약한 노트북은 Low@0.7 에서 시작, (L9) 내용을 다시 읽거나 들판을 껐다 켜도 제어기의 그림 크기가 유지돼요(`QualityApplied` 이벤트); `conduct`/`autoQuality` 는 F5 가 "다음 시작부터"로 알려요, (L10) CSV 끝에 `starve_ignored`, `quality_changes`, (L11) 화질 틱은 입력 뒤로 옮기고 손이 있는 프레임은 한가함이 아니에요, (L12) "오르내림 없음" 문구는 "변경이 갈수록 느려짐"(한도: 시도 대기가 최대 30분까지 두 배씩)으로 고쳤어요. 전시 PC 는 Auto 를 쓰지 않고 High/Low 로 고정해요.
**더 단순한 Auto (10-30, 두 번째 리뷰 뒤 `fix(quality): simpler Auto - no tier climbing at run time; meadow-only render scale`):** 등급 올라감 탐침을 두 번 연달아 리뷰가 찾았으므로 그 논리를 없앴다. 규칙: **등급(High/Low)은 시작할 때(그래픽 장치 추정 + 첫 8초) 정해지고, 실행 중에는 더 좋은 쪽으로 올라가지 않으며, 더 싼 쪽으로는 많아야 한 번(들판이 한가할 때까지 기다리고 그동안 그림 크기가 내려감) 내려간다.** 같은 등급 안에서는 그림 크기만 오르내리고, 오르는 것은 연주 중에 본 프레임 20초(느린 프레임 2% 이하)가 있을 때만, 실패할 때마다 두 배 늦게(최대 30분); 올라간 단계는 거기서 20초 연주해야 버텼다고 치고(시계로는 용서하지 않음). `_lastUpCrossedTier`와 등급 올라감 탐침 용서는 없앴다. 한계: 시도 대기는 바꾸는 속도를 한정할 뿐이라 같은 등급의 두 크기 사이를 느리게 오가는 일은 남는다. **포기하는 것:** 시작을 Low로 한 중간 PC는 그 실행 내내 Low이고, 일시적인 급증 뒤에는 방문자가 한동안 연주할 때까지 낮은 크기에 머문다(일부러). 손 추적 실험: 실행 중이 아니거나 장이 2초 넘게 안 오면 취소(성공도 실패도 아님); 실패 횟수는 쿨다운 뒤에도 유지되어 무시 시간이 두 배씩(10분, 20분 … 최대 4시간). 그림 크기는 들판 카메라 하나(참조로 지정)에만 걸려서 `meadowIndex`/`recordIndex` 가 바뀌어도 기록 화면은 1.0. CSV `quality_level` 은 지금 적용 중인 단계. 시험: 검토자의 시나리오 B~F(`aq.py`)를 EditMode로 옮김.

**D (시각 다듬기, 10-30; 커밋 97e3c8e 8e1c9cc 0cb5faa 66b272d a056fe6 7e3661b 503bf47 dc082e6 dbf8799):** 위 "시각 다듬기 목록" 1~9를 하나씩 커밋으로 했다(깨어남 물결과 돌풍, 부드러운 지역 돌풍인 손 바람, 겹친 꽃잎의 양귀비, 종소리에 답하는 따뜻한 반딧불, 그림 속 언덕 안개를 따르는 맥동, 강하게 오래 치면 노을에 닿는 하늘 규칙, 먼 홀씨와 꽃잎의 화면 크기 하한, 낮은 해의 풀끝 테두리 빛, 진짜 언덕 기복). 제약: 가장 낮은 등급과 Auto의 가장 낮은 단계에서도 성립, B의 절약을 되돌리지 않음, 깜빡임 없음, 새 Bloom 넘침 없음. Low 비용은 같은 방법(Mac M4 Pro, 1080p 창, 상한 없이, 3번 평균씩 4번을 번갈아 12번, `measurements/day5/render-cost-partd-final.json`)으로 다듬기 전 0.980 ms, 아홉 가지 뒤 0.998 ms(+1.9%), 움직이는 배경까지 1.029 ms(+5.0%, 아홉 가지 뒤보다 +3.0%; 평균의 오차 1~2%). 아홉 가지는 처음 재었을 때 +9.2%였고, 약한 등급의 손 바람을 단순한 기울임으로, 안개 맥동을 조용할 때 건너뛰게, 홀씨는 기본 정점 배치 그대로 두어 +4.4~5.2%로 줄였다(`perf(visual): keep Part D cheap at the weak tier`); 마지막에 번갈아 4번 잰 값은 +1.9%였다(같은 Mac에서도 한 번 재면 ±5% 흔들린다). **배경은 모든 등급에서 움직인다**(마지막 요청): 약한 등급이 구름과 먼 들판 일렁임을 빼던 것을, 구름은 켤 때 한 번 구운 1024×1024 무늬 판(`CloudTile`, 읽기 한 번 × 두 층)에서 읽고 먼 들판 일렁임과 구름 그림자는 이미 구워진 바람 장에서 읽도록 바꿨다(`skyDetail`은 "움직임 여부"가 아니라 "하늘을 그리는 방식": 1은 점마다 소음 계산, 0은 판에서 읽기). 같은 시각(`--sky 0.5`)에서 Low 창 14초 녹화로 하늘 띠는 이웃 장면끼리 0.26단계(255 중, 최대 0.32: 깜빡임 없음), 4초 간격 1.9, 12초 간격 3.2로 꾸준히 달라진다. **Mac에서 정한 것이라 사용자가 정할 일:** 하늘 규칙 0.15/0.9, 종소리 자리, 언덕 기복의 크기, 홀씨 하한(안개 때문에 약 5 m 밖은 어차피 안 보임), 약한 등급의 구름 결(조금 부드러움). Intel UHD, D3D11 GPU 시간, Auto가 어느 등급에 머무는지는 Windows에서 재 봐야 한다. 시각 다듬기 목록 11(기록 화면 대기 움직임)은 하지 않았다.

---

## 순서와 날짜 (목표)

| 날짜 | Task |
|---|---|
| 10/29 밤 | (사용자) Windows 점검표 |
| 10/30 | 1, 2, 3, 4 |
| 10/31 | 5, 6 |
| 11/1 | 7, 8 |
| 11/2 | 9 |
| 11/3 | 10, 11, 12 |
| 11/4 | 13, 14 → (사용자) Windows 세션 A·B |
| 11/5 | 고친 점 반영, 15 → (사용자) 세션 C 8시간(밤) |
| 11/6 | (사용자) 세션 D 지연 측정, 16 |
| 11/7 | 결과 문서 마무리 |

밀리면 설계 10절의 줄이는 순서를 따른다.
