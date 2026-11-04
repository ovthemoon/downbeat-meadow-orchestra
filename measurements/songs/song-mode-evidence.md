# 곡 모드 증거 (2026-11-01, Mac)

곡 모드(`docs/superpowers/specs/2026-11-01-song-mode-design.md`)를 다 합친 나무(`45a54a1`, 이 문서와 문서 고침만 그 위에 얹었어요)에서 돌린 것이에요. 사람이 쓰는 설명은 `docs/conducting-play.md` 9절, Windows에서 볼 것은 `docs/windows-runbook.md` 세션 E예요.

## 1. 시험 합계

| 묶음 | 명령 | 결과 줄 | 처음(곡 모드 전) | 늘어난 것 |
|---|---|---|---|---|
| EditMode | `tools/unity_test.sh EditMode` | `[unity_test] EditMode: total=1828 passed=1828 failed=0 skipped_or_inconclusive=0 (unity exit 0)` | 1606 | +222 |
| PlayMode | `tools/unity_test.sh PlayMode` | `[unity_test] PlayMode: total=235 passed=235 failed=0 skipped_or_inconclusive=0 (unity exit 0)` | 217 | +18 |
| node | `npm test` | `# tests 218`, `# pass 218`, `# fail 0` | 205 | +13 |

- EditMode는 7.8초, PlayMode는 725초(약 12분)가 걸렸어요. PlayMode는 `--songs off` 기본값으로 돌아요(`PlayModeDefaults`). 곡 모드가 필요한 시험만 `--songs on`을 직접 줘요.
- 결과 파일은 `measurements/unit-tests/editmode.xml`, `playmode.xml`이에요. 시험 이름과 개수, 결과는 도구가 쓴 그대로이고, 이 컴퓨터의 사용자 폴더와 임시 폴더 경로만 `/Users/USER`와 `/tmp/`로 바꿨어요(저장소 글에 개인 경로를 두지 않으려고요).

## 2. Mac 플레이어와 70초 실행

- 빌드: `BUILD_LABEL=song-mode tools/unity_build.sh mac` → `builds/mac/Downbeat.app` 117.89 MiB(225 파일), 콘텐츠 포함 합계 124.75 MiB(`measurements/build/song-mode.json`의 `mac`). `builds/`는 git에 올라가지 않아요.
- 화면을 먼저 깨운 뒤(`caffeinate -u -t 5`) 실행했어요. 카드와 저장 파일이 이 Mac의 진짜 자료 폴더에 섞이지 않게 임시 폴더를 줬어요.

```
builds/mac/Downbeat.app/Contents/MacOS/Downbeat --songs on --input replay --mute \
  --cards-dir <임시>/cards --settings-file <임시>/settings.json --quit-after 72
```

`Player.log`의 곡 줄 (앞뒤 줄 몇 개와 함께, 경로는 뺐어요):

```
[Downbeat] loading sounds
[songs] book: 3 songs, 0 rejected
[Downbeat] ready
[input] Replay: recorded hand motion (replay/demo-hands.json)
[songs] start quiet-meadow at slot 16
[songs] end quiet-meadow
[songs] start bright-morning at slot 416
[RunAutomation] quit-after reached
```

- `[songs] start quiet-meadow at slot 16`, `[songs] end quiet-meadow`가 모두 있어요. 곡은 16번 칸(첫 마디 줄)에서 시작해 400번 칸(16 + 24마디 x 16칸)에서 끝나요. 끝난 뒤 2.5초(16칸) 만에 다음 방문이 열려 둘째 곡 `bright-morning`이 16칸 뒤에서 시작했어요.
- `Player.log` 57줄 가운데 `error`, `exception`, `warning`이 들어간 줄은 0개예요. 종료 코드는 0이에요.
- 곡이 끝나며 카드 한 장이 쓰였어요: `durationSec` 62.1, `noteCount` 129, `maxLevel` 3, **`"songId":"quiet-meadow"`, `"songTitle":"잔잔한 들판"`**.
- 같은 옵션으로 사진을 찍는 실행을 5번 더 했어요(짧은 3번, 65초 넘게 간 2번). 모두 `[songs] book: 3 songs, 0 rejected`와 `[songs] start quiet-meadow at slot 16`이 나왔고, 긴 두 번은 `[songs] end quiet-meadow`와 `[songs] start bright-morning at slot 416`도 나왔어요. 다섯 번 모두 `error`, `exception`, `warning` 줄이 0개예요.

## 3. 사진 (`--input replay`, 소리 끔)

| 파일 | 무엇 | 어떻게 찍었나 |
|---|---|---|
| `song-title-quiet-meadow.jpg` | 곡 이름 "잔잔한 들판"이 1마디에 화면 위쪽 가운데에 떠 있어요. 위 줄 칸(리코더)에 손이 있고, 아래 띠에는 "아직 오늘의 카드가 없어요" | `--capture <png> --capture-after 5 --capture-size 1920x1080 --quit-after 8`, 960 폭 JPG |
| `song-end-and-card.jpg` | 곡이 끝난 순간. 왼쪽 들판 가운데에 "끝", 오른쪽 기록 화면의 카드에 곡 이름 "잔잔한 들판"이 강조색으로 한 줄 | `--capture-screens <png> --capture-after 64.7 --quit-after 68`, 3840x1080 PNG를 1920 폭 JPG로 |
| `song-card-line.jpg` | 위 사진의 카드를 잘라 크게 (1분 02초, 자연 3, 곡 이름 줄) | 위 PNG에서 540x270만 잘랐어요 |

- 모두 이 저장소의 사진 길(`--capture`, `--capture-screens`, `RunAutomation.cs`, `ScreensCapture.cs`)로 찍은 앱 자신의 그림이에요. 바탕화면을 찍은 것이 아니에요. 설정 패널(IMGUI)은 이 길에 찍히지 않아요(설계대로).
- 곡 이름이 보이는 시각은 방문이 시작되고 바로 다음 마디 줄이에요. 앱을 켠 뒤 4초 지점에는 아직 없었고(`--capture-after 4`), 5초와 6초 지점에는 있었어요(둘 다 같은 곡 이름).
- "끝"이 있는 사진은 64.7초 지점의 한 번만 잡았어요. 66.5초 지점 사진(같은 카드, "끝" 없음)은 저장소에 두지 않았어요.

## 4. Windows 쪽 묶음 (올리지 않았어요)

- 빌드: `BUILD_LABEL=song-mode tools/unity_build.sh windows` → `builds/windows/Downbeat.exe`(0.64 MiB) 합계 108.13 MiB(272 파일), 콘텐츠 6.87 MiB(`measurements/build/song-mode.json`의 `windows`).
- 묶음: `tools/package_windows.sh`, `tools/package_windows.sh --tracker-only`. 둘 다 이 컴퓨터의 `builds/`에만 있어요. 올리지 않았고 게시도, 릴리스도 만들지 않았어요.

| 파일 | 바이트 | 안에 든 것 |
|---|---|---|
| `builds/downbeat-windows-2026-11-01.zip` | 49,129,512 (약 47 MiB, 310 파일을 도구가 센 값) | 플레이어, 손 추적, 감시 프로그램, 스크립트, 안내서. `content/content.json`에 곡 3개(`quiet-meadow`, `bright-morning`, `auld-lang-syne`, `contentVersion` 2026-11-01.1). `README-runbook.txt`에 세션 E |
| `builds/downbeat-hand-tracker-2026-11-01.zip` | 128,335 (38 파일) | 손 추적, 감시 프로그램, 스크립트, 안내서(플레이어 없음) |

## 5. 확인하지 못한 것

- **진짜 손.** 모든 실행은 녹화된 손(`--input replay`)이에요. 손 추적이 1초에 6장일 때의 가락은 시험(`Melody_AtSixPicturesASecond_StaysOnTheScore`)이 보지만 진짜 웹캠으로는 안 봤어요.
- **Windows 실행.** Windows 플레이어는 만들고 묶기만 했고 켜 보지 않았어요. 곡 모드를 Windows 노트북에서 보는 순서는 `docs/windows-runbook.md` 세션 E에 있어요.
- **귀로 듣기.** 모든 실행에 `--mute`를 줬어요. 소리가 악보대로인지는 시험(음 높이, 칸, 소리 파일 선택)으로 봤고 귀로는 듣지 않았어요.
- **Tab 설정 패널의 모습.** 배치 모드는 `OnGUI`를 부르지 않아서 자동 시험은 패널의 그림을 못 봐요. 모양은 Task 8에서 창이 있는 실행으로 한 번 봤고 이번에는 다시 보지 않았어요. 패널은 `--capture`, `--capture-screens`에 찍히지 않아요.
- **두 화면, 감시 프로그램이 다시 켠 뒤의 저장된 선택.** 시험은 저장 파일 읽고 쓰기까지 봤지만, 모니터 2대와 감시 프로그램이 앱을 다시 켜는 길로는 안 돌려 봤어요(세션 E의 E11, E12).
- **60초가 넘는 연속 자유 연주 밤샘.** 이 문서의 실행은 모두 72초 이하예요. `--soak` 밤샘은 하지 않았어요.
- **셋째 곡까지의 한 바퀴를 실행으로 본 것.** 70초 실행은 첫 곡과 둘째 곡의 시작까지만 봤어요. 곡 순서 1, 2, 3, 1은 시험(`SongSessionTests`, 곡 모드 PlayMode)이 봐요.

## 마지막 고침 뒤 (2026-11-01 오후)

- 전체 검토 뒤 작은 고침 4개(`--songs` 값 오타 경고, 곡이 없을 때 "곡 모드: 곡 없음", `DisplayRouter` 설명, 옛 시험 결과 파일의 개인 경로)를 더했어요(커밋 179d945, 134800a).
- 그 뒤 시험: EditMode 1832/1832, PlayMode 238/238, node 218/218. 이 표의 xml 파일은 위 1828/235 실행 때 것이에요.
- Windows 묶음은 고친 뒤 다시 만들었어요: `downbeat-windows-2026-11-01.zip` 49,129,973바이트, `downbeat-hand-tracker-2026-11-01.zip` 128,396바이트.
