> 이 파일은 개발 중에 쓰던 README 원문이다(영문 위주). 상태 줄은 그때 기준이라 지금과 다를 수 있다. 소개는 [README.md](README.md)에 있다.
> 옛 캐릭터 무대의 화면 사진과 영상(`docs/media/`)은 공개판에 넣지 않았다.

# Downbeat (웹캠 손동작 오케스트라)

Personal prototype: webcam hand gestures play instruments that are quantized to a beat grid. **Not a real exhibition installation.**

**시연 영상 (2026-11-02, Windows 노트북): [들판 오케스트라 시연](https://youtu.be/jWMInxaSkUs)** (YouTube 일부 공개, 2:21). 찍고 붙인 법: [docs/windows-test-2026-11-02-1040.md](docs/windows-test-2026-11-02-1040.md).

**Direction change (2026-10-29): the project moves from a browser app to a native Unity URP app (Windows exhibition build + Mac demo build)
with a separate Python hand-tracking app, the "meadow orchestra".** Design: [docs/superpowers/specs/2026-10-29-meadow-orchestra-design.md](docs/superpowers/specs/2026-10-29-meadow-orchestra-design.md),
plan: [docs/superpowers/plans/2026-10-29-meadow-orchestra.md](docs/superpowers/plans/2026-10-29-meadow-orchestra.md).
The Day 0-2 web prototype (WebGL template, Web Audio engine, in-page MediaPipe, their Node tests) is parked in [`archive/web/`](archive/web/README.md); its results docs below stay as the record.

Status: **지휘하는 6칸 (2026-10-31, Mac에서 만들고 시험했어요)**: 손 자리 6칸(2행 x 3열)이 악기 6개(위 줄 하프·리코더·종, 아래 줄 낮은 현·마림바·손북)이고, 소리 크기는 손의 움직임이 정해요. 두 주먹을 쥐고 벌리면 연주가 끝나고(소리가 잦아들고, 카드가 바로 나오고, 편 손이 보여야 다시 시작해요), 10초 조용하면 따라 하기 안내가 떠요. 위 줄 음은 공중 이펙트, 아래 줄 음은 풀의 반응이고, 손마다 음표 꼬리와 들어간 칸의 이름이 나와요. `--log-pose`로 손 모양 읽기를 로그로 볼 수 있어요. 손은 화면에서 보이는 자리에 있어요(위 줄은 하늘, 아래 줄은 풀, 두 줄은 들판 끝 지평선에서 만나요. 줄이 바뀌는 죽은 구간은 소리와 화면이 같은 줄을 써요). 시험은 모두 통과했어요: node 205, Unity EditMode 1606, PlayMode 217(`measurements/unit-tests/`). **Windows에서는 아직 돌려 보지 않았어요**(런북: [docs/windows-runbook.md](docs/windows-runbook.md), 동작 규칙: [docs/conducting-play.md](docs/conducting-play.md)). 그 앞의 Day 2(웹캠 손 추적 -> Unity, 마우스 대체, 녹화·재생)와 Day 1(마우스 연주, 박자 격자, 무대·관객 화면)은 그대로 아래 기록에 남아 있어요.
Results: [docs/day2-results.md](docs/day2-results.md), [docs/day1-results.md](docs/day1-results.md) (screenshots and demo videos in `docs/media/`), feasibility tests: [docs/day0-results.md](docs/day0-results.md).
The strike-to-note rule is a placeholder: hand position and velocity are also available to views every frame (`IHandMotionView`).

## Layout

| Path | Contents |
|---|---|
| `unity/Downbeat/` | Unity 6000.3.9f1 project: native Standalone (Mono), Universal Render Pipeline (`Assets/_Project/Settings/`: `URP_High`, `URP_Low`, `MeadowVolumeProfile`), Linear colour space, VContainer. Product code in `Assets/_Project/Scripts/{Core,Audio,Input,Content,Views,App}`; the Day 0 test code is parked in `Assets/_Day0` (compiled only with `DOWNBEAT_DAY0`). |
| `tracker/` | The Python hand tracker (webcam or video -> MediaPipe Hand Landmarker -> local UDP, format in [docs/protocol/hand-udp-v1.md](docs/protocol/hand-udp-v1.md)). Python 3.12 venv in `tracker/.venv` (git-ignored), `pytest` tests. |
| `archive/web/` | The parked Day 0-2 web code (WebGL template with the page scripts, `WebAudioEngine`, `JsHandSource`, `PagePort`, `Downbeat.jslib`, `WebBuild`) and the Node tests of the page scripts (still run by `npm test`). Outside `Assets/`, so Unity does not compile it. |
| `builds/` | Native player builds made by `tools/unity_build.sh` (git-ignored). |
| `web/` | `serve.mjs` local server (Brotli-aware, request log, result upload), `public/` Day 0 test pages (parked), `test/replay-trim.test.mjs` |
| `tools/` | Node scripts: Chrome runner (CDP), onset analysis, sample preparation, vendor sync |
| `content/` | `content.json` (data: samples, pitches, layout, song, hand tuning), `img/meadow/` backdrop plates, `audio/` prepared CC0 samples + `manifest.json` + `LICENSES.md`, `replay/demo-hands.json` (recorded hand landmarks for `?input=replay`) |
| `measurements/` | Raw data and summaries of every measured number (raw audio recordings are git-ignored) |
| `captures/` | Full-size pictures and numbers of test and measurement runs (git-ignored). `DOWNBEAT_KEEP_CAPTURES=1` copies the few kept ones to `measurements/` (JPEG q85, at most 1920 px wide). |
| `docs/` | Results and notes |

## Running the tests

```sh
npm ci                      # installs the pinned @mediapipe/tasks-vision
node tools/sync_vendor.mjs  # web/vendor (MediaPipe wasm + hand model), verified by SHA-256
npm test                    # Node unit tests (onset analysis, server, JS beat grid, ...)

# Python tracker (uv venv --python 3.12 tracker/.venv && uv pip install --python tracker/.venv/bin/python -e 'tracker[dev]'); never opens a camera in tests
tracker/.venv/bin/python -m pytest -q tracker
tracker/.venv/bin/python tools/measure_tracker_video.py      # fps and detection ms on a video -> measurements/tracker/mac-video.json

# Unity native (batchmode, Unity Hub editor 6000.3.9f1 with the Windows (Mono) module; no running editor is touched)
tools/unity_test.sh EditMode          # -> measurements/unit-tests/editmode.xml, prints passed/failed, non-zero exit on failure
tools/unity_test.sh PlayMode          # -> measurements/unit-tests/playmode.xml
tools/unity_build.sh mac              # -> builds/mac/Downbeat.app      (+ content/ next to it)
tools/unity_build.sh windows          # -> builds/windows/Downbeat.exe  (+ Downbeat_Data, content/)
# a built player takes: --input hand|mouse|replay  --view stage|audience  --quit-after SEC  --capture ABS_PATH.png [--capture-after SEC]  --mute  --log-frame-stats SEC  --log-pose (a [pose] line per hand per second in Player.log)
#   --measure-clock --out measurements/<dir> [--scenario calm|stress] [--duration SEC]   beat-clock measurement instead of the game
tools/run_native_clock.sh             # Mac player, 3 x (calm + stress) x 60 s -> measurements/native-clock/summary.md (Task 2)
# and the usual Unity options (-screen-fullscreen 0 -screen-width 960 -screen-height 540 -logFile PATH)

# Unity Web (the parked Day 0-2 web build; needs the Web module and archive/web/Editor/WebBuild.cs copied back into Assets)
UNITY=/Applications/Unity/Hub/Editor/6000.3.9f1/Unity.app/Contents/MacOS/Unity
cd unity/Downbeat
$UNITY -batchmode -quit -projectPath "$PWD" -buildTarget WebGL \
  -executeMethod Downbeat.EditorTools.WebBuild.Run \
  -dbScene Assets/_Project/Scenes/Day0.unity -dbOut Builds/day0 -dbProfile size
$UNITY -batchmode -projectPath "$PWD" -runTests -testPlatform EditMode -testResults ../../measurements/unit-tests/editmode-results.xml
# Do not pass -nographics: it crashed the editor while importing this URP project.

# Browser tests (launch a separate Chrome with a throw-away profile)
node tools/run_s1.mjs --build day0 --scenarios calm,stress,hidden,blur --duration 60 --vsync 2
node tools/analyze_s1.mjs measurements/s1/<run>
node tools/run_s2.mjs --video /path/to/hand.y4m --vsync 2
```

Day 2 browser tests (each starts its own Chrome with a FAKE camera video; they never open the real webcam):

```sh
python3 -I tools/make_strike_video.py --image <CC0 hand photo> --out hands1.y4m --hands 1   # also --hands 0 and 2; see NOTICE for the photo
node tools/run_day2_smoke.mjs --video hands1.y4m           # chain works: camera -> tracker -> Unity -> notes
node tools/run_day2_startflow.mjs --video hands1.y4m       # denied / missing / broken / pending camera, model failure, mouse: all fall back
node tools/run_day2_replay.mjs                             # same recording twice -> identical notes and grid slots
node tools/run_day2_privacy.mjs --video hands2.y4m         # network evidence (DevTools log + Chrome net-log) and a positive control
node tools/run_day2_perf.mjs --videos0 hands0.y4m --videos1 hands1.y4m --videos2 hands2.y4m
node tools/run_day2_latency.mjs --video hands1.y4m
node tools/run_day2_record.mjs --video hands1.y4m          # records content/replay/demo-hands.json from the tracker
node tools/record_demo_day2.mjs --video hands1.y4m         # docs/media/day2-demo.mp4
```

## Play it locally (Day 1, Day 2)

```sh
node tools/assemble_site.mjs                       # builds dist/site from the Unity Web build + content, checks OpenDesk limits
node web/serve.mjs --port 8780 --mount /=dist/site
# open http://localhost:8780/ , press Start
#   camera (default): strike downward in front of the webcam; left/middle/right third = harp/recorder/marimba, height = pitch, speed = strength
#   ?input=mouse               click the stage instead (no camera permission is asked)
#   ?input=replay[&file=replay/demo-hands.json][&loop=0]   recorded hand motion, no camera needed
#   a denied / missing camera falls back to the mouse by itself and the badge on the stage says why
#   R key (camera mode): start / stop recording the hand motion; stopping downloads a replay file
#   ?preview=0 hides the camera inset; ?mpbase=/vendor/mediapipe&model=/vendor/models/hand_landmarker.task use local MediaPipe copies (same-site paths only)
```

## License

Code: MIT, see [LICENSE](LICENSE). Third-party material: see [NOTICE](NOTICE).
