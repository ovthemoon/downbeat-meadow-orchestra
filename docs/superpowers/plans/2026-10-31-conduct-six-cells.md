# 지휘하는 6칸 구현 계획

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 손 자리 6칸(2행 x 3열) = 악기 6개, 움직임 = 소리 크기, 두 주먹 벌려 끝내기, 따라 하기 안내, 위 줄 공중 이펙트 / 아래 줄 풀, 음표 꼬리를 넣는다.

**Architecture:** 1단계에서 여러 곳에 걸치는 바탕(파트 6개, 칸 고르기, 손 모양, 음 정보에 손 Y, 화면 위 그리기 자리)을 세 갈래(S 소리, P 손 모양, M 파트와 칸)로 동시에 만든 뒤 합친다. 2단계는 그 위에서 네 갈래(A 끝내기, B 이펙트, C 꼬리와 칸 안내, D 안내)를 각자의 작업 폴더에서 동시에 만들고 통합 브랜치에 합친다.

**Tech Stack:** Unity 6000.3.9f1(URP, Mono), C# (`Downbeat.Core`는 엔진 참조 없음), NUnit EditMode/PlayMode, Node(`tools/*.mjs`, `node --test`), VCSL CC0 샘플.

**Spec:** `docs/superpowers/specs/2026-10-31-conduct-six-cells-design.md` (9절 "만들기 전 검토에서 정한 것"이 최종 결정)

## Global Constraints

- 통합 브랜치 `feat/conduct-six-cells`(작업 폴더 `<repo>`). 원본 작업 폴더 `<repo>`와 브랜치 `feat/meadow-orchestra`는 건드리지 않는다. push, 원격 추가, 릴리스 만들기 금지.
- 커밋 메시지: `type(scope): 영어 설명` (이 저장소 방식). **Co-Authored-By 줄을 넣지 않는다.**
- 커밋하지 않을 것: `unity/Downbeat/ProjectSettings/QualitySettings.asset`(시험이 antiAliasing을 바꿈, 커밋 전 `git checkout --`), `measurements/unit-tests/*.xml`(통합 때 다시 만듦, 커밋 전 `git checkout --`).
- 시험: `tools/unity_test.sh EditMode`, `tools/unity_test.sh PlayMode`, `npm test`(저장소 뿌리). 각 갈래는 끝날 때 셋 다 통과. 처음 기준: EditMode 1023, PlayMode 97, node 172 모두 통과.
- `Downbeat.Core`에는 `UnityEngine`을 쓰지 않는다. 손 모양은 x, y만 쓴다(녹화 손은 z가 0).
- 손 추적 프로그램(`tracker/`)과 UDP 형식은 바꾸지 않는다.
- 초당 6장(`1.0 / 6.0`초 간격)에서도 칸 고르기, 소리 크기, 손 모양, 끝내기가 동작해야 하고 시험으로 고정한다.
- 새 설정은 `content.json`에, 빠지면 C# 필드 초기값이 기본값(471cfee 방식), `tools/lib/content-build.mjs`의 DEFAULTS에 같은 값, 범위 검사는 `if (!(v >= a && v <= b)) errors.Add("x.y must be a..b")`, 새 최상위 블록은 `ContentSections.Changed` 목록에 더한다.
- `schemaVersion` 4. 파트 6개 필수. 옛(3파트) content는 거절되어 지금의 대체 순서로 켜진다.
- 화면 글: 한국어. 문서(`docs/*.md`)는 해요체.
- 저장소 문서와 코드에 지원 회사 이름, 저장소 밖 폴더 경로를 쓰지 않는다.

## Review Focus

- 보통 연주 중(편 손, 빠르게 휘저음, 손이 잠깐 가려짐) 두 주먹 끝내기가 잘못 걸리는 경우 → A가 "편 손 휘젓기 30초, 끝나지 않음" 시험(30/6 fps)을 둔다.
- 칸 경계(열 0.33/0.67, 행 `rowSplit`) 위에서 손이 떨 때 악기가 왔다 갔다 하는 경우 → M이 경계 ±0.01 진동 시험(파트가 바뀌지 않음)을 둔다.
- 손 하나는 주먹, 다른 손은 편 손일 때 → P가 "편 손만 연주" 시험, A가 "끝나지 않음" 시험을 둔다.
- 끝낸 직후 주먹을 편 채 그대로 있는 경우(곧바로 다시 시작) → A가 `restartLockSec` 시험을 둔다.
- 안내가 떠 있는 동안 편 손이 들어오면 연주가 바로 시작되는가 → D가 "안내 중 편 손 → 첫 음까지 지연이 안내 없을 때와 같음" 시험을 둔다.

---

## 1단계 (동시에 세 갈래, 각자 작업 폴더)

### 갈래 S: 새 악기 소리 3개 (작업 폴더 `<repo>`, 브랜치 `6c/samples`)

**Files:** Modify `tools/samples.config.json`, `tools/prepare_samples.mjs`(필요할 때만), Create `content/audio/{bell,lowstring,handdrum}_*.wav`, Modify `content/audio/manifest.json`, `content/audio/LICENSES.md`. `content/content.json`, `tools/lib/content-build.mjs`는 건드리지 않는다(갈래 M 몫).

**Interfaces:** Produces: 파일 접두사 `bell_`, `lowstring_`, `handdrum_`와 악기별 제안값(`rootMidi`, `maxPitchShift`, `gainDb`)을 커밋 메시지 본문과 `docs/conduct-six-cells-samples.md`(짧은 표)에 적는다. 갈래 M이 이 값으로 `instruments`를 채운다.

- [ ] VCSL 고정 커밋(c1ea7bcc…)에서 고른다. 후보 목록: 저장소 밖 `vcsl_paths.txt`가 없으면 `gh api "repos/sgossner/VCSL/git/trees/c1ea7bcc3c7309650ab0da9d15c9cd1fbc4a4c7e?recursive=1" --jq '.tree[].path'`.
  - 종(`bell`, 위 오른쪽): `Idiophones/Struck Idiophones/Hand Chimes/sus_*_r01_main.wav` (A4, C5, D5, E5, G는 F#/G#에서 옮김). 실제 울리는 높이는 YIN으로 확인한다.
  - 낮은 현(`lowstring`, 아래 왼쪽): VCSL에 첼로 뜯기가 없다. `Chordophones/Composite Chordophones/Strumstick/Finger/` 낮은 줄(D2, E2, G2, A2, E3, G3)을 쓴다. 화면 이름은 "낮은 현".
  - 손북(`handdrum`, 아래 오른쪽): `Membranophones/Struck Membranophones/Frame Drum/` 또는 `Conga/` 한두 개. 음 높이 없는 소리라 설정에 `"unpitched": true`를 두고 YIN을 건너뛰고 `tuneCents` 0, 이름표 음은 A로 둔다(검사기가 A 음계를 요구).
- [ ] 다듬기는 지금 값(`trim`) 그대로, 16비트 모노. `prepare_samples.mjs`가 음 높이 없는 소리와 2500 Hz 넘는 소리를 다룰 수 있게 필요한 만큼만 고친다(`round(null)` 오류 주의, 노드 시험 추가).
- [ ] `node tools/prepare_samples.mjs` 실행, 새 WAV의 YIN 결과가 이름표와 ±50센트 안(음 높이 없는 것 제외)인지 확인, `npm test` 통과, 커밋 `feat(audio): bell, low string and hand drum samples from VCSL`.

### 갈래 P: 손 모양 (작업 폴더 `<repo>`, 브랜치 `6c/pose`)

**Files:** Create `Scripts/Core/HandPose.cs`(enum + 분류기), `Scripts/Core/PoseSettings.cs`, `Scripts/Core/PoseContentValidator.cs`; Modify `Scripts/Core/HandFrame.cs`(`HandSample`, `HandFrameReader.TryReadHand`), `Scripts/Core/HandMotion.cs`(`HandMotion`, `HandMotionTracker`), `Scripts/App/FrameDump.cs`, `Scripts/Core/ContentModels.cs`(`PoseDto`), `Scripts/Core/ContentValidator.cs`, `Scripts/Content/ContentSections.cs`, `Scripts/Core/Conduct/ConductEngine.cs`(주먹 손 제외 한 곳), `content/content.json`, `tools/lib/content-build.mjs`; Test `Tests/EditMode/HandPoseTests.cs`, `Tests/EditMode/HandTestData.cs`, 기존 가짜 손 시험들의 생성자 호출.

**Interfaces (Produces):**
- `public enum HandPose { Unknown = 0, Open = 1, Fist = 2 }`
- `public static HandPose HandPoseClassifier.Classify(double[] buffer, int handBase, PoseSettings settings)` — 손가락 4개(검지 8, 중지 12, 약지 16, 새끼 20; 엄지 제외)마다 `끝-손목 거리 / 손목-중지 뿌리(9) 거리`(x는 aspect 보정, y 그대로). `>= openRatio`면 펴짐, `<= fistRatio`면 접힘. 펴짐 3개 이상 Open, 접힘 3개 이상 Fist, 나머지 Unknown.
- `PoseSettings { float OpenRatio = 1.45f; float FistRatio = 1.15f; int ConfirmFrames = 2; }`, `PoseSettings.From(PoseDto)`, content 블록 `pose { openRatio 1.45 (1.1..3), fistRatio 1.15 (0.5..1.5, < openRatio), confirmFrames 2 (1..10) }`.
- `HandSample.Pose`, `HandMotion.Pose`(새 생성자 인자, 맨 끝). `HandMotionTracker`는 새 모양이 `ConfirmFrames`장 연속일 때만 바꾼다(처음은 Unknown).
- `ConductEngine`: `Pose == Fist`인 손은 목소리에 넣지 않는다(Unknown, Open은 연주).
- 시험 도구: `HandTestData.PoseHand(int id, float x, float y, float size, HandPose pose)` — 21점을 모두 쓴다(펴짐 비율 1.8, 주먹 0.9). 녹화(ReplayDto) 만들기 도우미 `HandTestData.ReplayOf(Func<double, PosedHand[]>, double seconds, double fps)`.

- [ ] 실패하는 시험: `Classify_OpenHand_IsOpen`, `Classify_Fist_IsFist`, `Classify_ThreeFingersCurled_IsFist`, `Classify_HalfCurled_IsUnknown`, `Tracker_SwitchesPose_OnlyAfterConfirmFrames_At6Fps`, `ConductEngine_FistHand_PlaysNothing_OpenHandBesideIt_Plays`, `DemoHands_AreOpen_MostOfTheTime`(데모 녹화 80% 이상 Open), `OldContentWithoutPoseBlock_UsesDefaults`.
- [ ] 구현, 세 시험 묶음 통과, 커밋 `feat(hand): open and fist hand pose from the 21 points`.

### 갈래 M: 파트 6개와 칸, 움직임 = 크기 (작업 폴더 `<repo>`, 브랜치 `6c/parts`)

**Files:** Modify `Scripts/Core/NoteTypes.cs`, `Scripts/Core/HandInterpreter.cs`(내려치기 파트 고르기), `Scripts/Core/Conduct/ConductEngine.cs`, `Scripts/Core/Conduct/ConductSettings.cs`, `Scripts/Core/Conduct/ConductContentValidator.cs`, `Scripts/Core/PitchMapper.cs`, `Scripts/Core/QuantizedNoteScheduler.cs`, `Scripts/Core/ContentModels.cs`, `Scripts/Core/ContentValidator.cs`, `Scripts/Content/ContentSections.cs`, `Scripts/Core/Meadow/FlowerSpecies.cs`, `Scripts/Core/Meadow/ParticleCues.cs`, `Scripts/Core/Meadow/MeadowNoteRouter.cs`, `Scripts/Record/RecordScreenView.cs`, `Scripts/Record/RecordPainter.cs`, `Scripts/Record/FlowerIconPainter.cs`, `Scripts/Record/IdleMotes.cs`, `Scripts/Record/SessionCard.cs`, `Scripts/Input/PointerMapping.cs`, `Scripts/Input/DebugPointerInput.cs`, `Scripts/App/DownbeatApp.cs`, `content/content.json`, `tools/lib/content-build.mjs`, 시험 도우미(`ConductRig.DefaultGaps` 등). Create `Scripts/Core/CellPicker.cs`, `Scripts/Core/CellSettings.cs`, `Scripts/Core/HandScreenMap.cs`, `Scripts/Meadow/MeadowOverlayLayer.cs`.

**Interfaces (Produces):**
- `public enum Part { Harp = 0, Recorder = 1, Bell = 2, LowString = 3, Marimba = 4, HandDrum = 5 }` (값 = 행*3 + 열, 행 0 = 위). `Parts.Count = 6`, `Parts.Name` → `"harp","recorder","bell","lowstring","marimba","handdrum"`, `Parts.TryParse`는 옛 이름도 읽는다(`"strings"`→Harp, `"winds"`→Recorder, `"perc"`→Marimba; 옛 카드 읽기용). `Parts.Row(Part)`, `Parts.Column(Part)`, `Parts.At(int row, int column)`, `Parts.IsSky(Part)`(행 0).
- `CellSettings { float RowSplit = 0.5f; float RowHysteresis = 0.03f; string[] Labels; string[] Colors }` ← content 블록 `cells { rowSplit 0.5 (0.25..0.75), rowHysteresis 0.03 (0..0.1), labels ["하프","리코더","종","낮은 현","마림바","손북"], colors ["#F2C46B","#F4F1E6","#BFE3FF","#C9A27A","#E2453A","#E8A04A"] }` (labels/colors 길이 6, colors는 `#RRGGBB`).
- `public static Part CellPicker.Pick(float x01, float y01, Part previous, bool hasPrevious, float columnMargin, CellSettings cells)` — 열은 지금처럼 `HandInterpreter.PickBand(x*3, 3, prevCol, partHysteresis*3)`, 행은 `rowSplit`에 `rowHysteresis` 죽은 구간. 내려치기와 지휘가 같이 쓴다. 마우스(`PointerMapping`)는 무대 화면 위아래 반으로 행을 고른다.
- 셈여림 = 움직임: `ConductEngine`의 `VelocityForHeight` 대신 `ConductSettings.VelocityForMotion(float palms)` = `MinVelocity + (1 - MinVelocity) * clamp01((palms - StillPalms) / (LoudPalms - StillPalms))`, 새 `conduct.loudPalms` 4 (StillPalms < loudPalms <= 20). 두 손이 한 파트면 큰 쪽. 가만히 든 손은 `MinVelocity`로 한 박에 하나.
- 악구(`ConductSettings.DefaultPhrases`, content `conduct.phrases` 6개): harp `[0,2,4,7,4,2]`, recorder `[7,5,4,2]`, bell `[9,7,9,5]`, lowstring `[0,0,2,0]`, marimba `[0,0,4,0]`, handdrum `[0,0,0,0]`. `song.partMinGapSlots`: harp 1, recorder 2, bell 2, lowstring 2, marimba 1, handdrum 1.
- 악기: `instruments` 6개. 새 3개(bell, lowstring, handdrum)는 갈래 S가 끝나기 전까지 **임시로** 지금 샘플을 가리킨다(bell→harp 파일, lowstring→marimba 파일, handdrum→marimba 파일, content.json 옆 표시 없이 `content-build.mjs` DEFAULTS 주석 `// TEMP until 6c/samples lands`). 통합 때 바꾼다.
- `NoteIntent`, `ScheduledNote`, `MeadowNoteEvent`에 `float Y01`(손의 y, 0 위..1 아래; 마우스는 화면 y로, 녹화·내려치기도 손 y) 추가.
- 들판: 위 줄 파트 음은 꽃을 피우지 않고(`SpeciesMap.ForPart`가 위 줄에 "없음"), 풀을 둥글게 밀지도 않는다(`AddNoteRadial` 없음), 입자도 아직 없음(`ParticleCues.ForNote` 위 줄 → None; 갈래 B가 채움). 아래 줄은 열의 꽃(왼 bellflower, 가운데 dandelion, 오른 poppy)을 피우고 지금처럼 둥글게 민다. 마림바(아래 가운데)는 양귀비 꽃잎 입자를 그대로 쓴다. 꽃 배치와 종류 3개는 그대로.
- 기록: 아래 줄 음은 열의 꽃 그림, 위 줄 음은 `cells.colors` 색의 작은 빛 점(새 그림 종류 `LightDot`)으로 그린다. `FlowerGlow`, `IdleMotes` 색, 카드 파트 이름 6개. 옛 카드(3파트 이름)도 읽힌다.
- 화면 자리: `public static (float vx, float vy) HandScreenMap.Viewport(float x01, float y01, float overlayFraction)` = `(x01, 1 - y01 * (1 - overlayFraction))`(뷰포트, y 위가 1). 두 화면 방식이면 `overlayFraction` 0.
- `MeadowOverlayLayer`(MonoBehaviour, 런타임에 들판 카메라 자식으로 만든다, 씬 다시 만들기 없음): `Transform Root`, `Vector3 LocalPoint(float vx, float vy)`(카메라 앞 거리 1 평면), `int SortingOrderBase = 100`. 예전 `OverlayLabelView`(7f9c202^) 방식을 따른다. 2단계 갈래 B, C, D, A가 여기에 자기 자식을 단다. `MeadowStage.Overlay`로 꺼낸다.
- `schemaVersion` 4(`ContentValidator.SupportedSchema = 4`).

- [ ] 실패하는 시험: `Parts_RowAndColumn_RoundTrip`, `Parts_TryParse_ReadsLegacyNames`, `CellPicker_PicksSixCells_AtCellCentres`, `CellPicker_HandTremblingOnABoundary_KeepsItsCell`(±0.01 진동, 행·열 모두), `Conduct_StillHand_PlaysAtMinVelocity_InEveryCell`, `Conduct_FasterHand_IsLouder_At6And30Fps`, `Conduct_HeightNoLongerChangesLoudness`, `Content_ShippedHasSixInstrumentsAndPhrases`, `Content_ThreePartFile_IsRejected`, `Router_SkyNote_DoesNotBloomOrPushGrass`, `Router_GroundNote_BloomsTheFlowerOfItsColumn`, `Record_SkyNote_DrawsALightDot`, `Card_WithLegacyPartNames_StillLoads`, `HandScreenMap_SingleScreen_KeepsTheStripFree`, `Pointer_TopHalfIsSkyRow`.
- [ ] 기존 시험을 6파트에 맞게 고친다(X 자리 상수, `DefaultGaps`, 높이로 셈여림을 보던 시험은 움직임으로). 지운 시험은 커밋 메시지에 이유와 함께 적는다.
- [ ] PlayMode `ConductPlayModeTests`가 실제 장면에서 통과(새 파트로 음이 나고 오류 로그 없음).
- [ ] 세 시험 묶음 통과, `docs/conducting-play.md`에 "8. 6칸(2026-10-31)" 절 추가(만든 대로), 커밋 `feat(conduct): six cells in two rows, loudness from movement`.

### 1단계 통합 (오케스트레이터)

- [ ] `feat/conduct-six-cells`에 M, P, S 순서로 합친다. 충돌(ConductEngine, ContentModels, ContentValidator, content.json, content-build.mjs, ContentSections)은 양쪽을 모두 살린다.
- [ ] 임시 샘플 연결을 S의 실제 샘플로 바꾸고 `node tools/build_content.mjs`로 content.json을 다시 만든다.
- [ ] 세 시험 묶음 통과, 커밋.

## 2단계 (동시에 네 갈래, 1단계 통합 커밋에서 갈라진 각자 작업 폴더)

모든 갈래: 새 숫자는 각자 새 content 블록(아래 이름)에, 검사기는 새 파일(`<Name>ContentValidator.cs`)에. 화면 그림은 `MeadowStage.Overlay`(`MeadowOverlayLayer`)의 자식으로, 글리프(♪, ✓, 손 그림)는 글꼴에 기대지 말고 절차적 메시나 코드로 만든 텍스처로 그린다(한글 글자만 `TextMesh` + `record.fontNames`). Low 단계에서 개수를 줄이는 값을 둔다.

### 갈래 A: 두 주먹 끝내기 (`downbeat-6c-cutoff`, `6c/cutoff`)

**Interfaces (Produces):**
- `public sealed class CutoffGesture` (Core): `bool Update(double nowSec, IHandMotionView hands, float aspect)` — 두 손 모두 `Pose == Fist`, 두 손 가로 거리(aspect 보정, 손바닥 크기 평균으로 나눔)가 `cutoff.windowSec` 안에 `cutoff.spreadPalms` 이상 늘고, 끝 거리가 `cutoff.minApartPalms` 이상인 상태로 `cutoff.holdSec` 유지되면 한 번 true. 그 뒤 두 손이 주먹이 아닐 때까지 다시 true 안 됨.
- content `cutoff { spreadPalms 2.0 (0.5..8), minApartPalms 3.5 (1..12), windowSec 1.0 (0.3..3), holdSec 0.2 (0..1), fadeSec 0.25 (0.05..1), restartLockSec 1.5 (0..5), endWordSec 1.5 (0.3..5) }`.
- `ExperienceStateMachine.EndNow(double now)` — Idle로 가며 `SessionEnded`를 그대로 낸다. 그 뒤 `restartLockSec` 동안, 그리고 그 뒤에도 **Open 손**이 보일 때까지 새 방문을 시작하지 않는다(Unknown 손으로는 시작 안 함). 손이 나가서 끝난 경우는 지금 규칙 그대로.
- `IAudioEngine.FadeOutAll(double seconds)`(지금 울리는 소리와 예약된 연주 소리를 그 시간에 잦아들게; 자연 소리 예약은 그대로) — 구현체 5곳 모두.
- 끝날 때: `ConductEngine.Reset()`(목소리 비움), `PerformanceEnergy.Set(0)`, 카드는 `SessionEnded`로 바로 만든다. 화면 가운데에 "끝"(`TextMesh`, `endWordSec` 동안 나타났다 사라짐).
- `event Action<double> PerformanceCutoff`를 `DownbeatApp`(또는 새 `PerformanceSignals`)에 둔다. 갈래 D가 이 이름을 쓴다.
- 시험(30, 6 fps 모두): `TwoFistsSpread_EndsTheVisit_AndWritesTheCardAtOnce`, `OneFistOneOpen_NeverEnds`, `OpenHandsWavingWildly_ForThirtySeconds_NeverEnds`, `FistsTogetherWithoutSpreading_NeverEnds`, `AfterTheEnd_FistsStillUp_NoNewVisit`, `AfterTheEnd_OpenHandWithinLock_NoNewVisit_AfterLock_NewVisit`, `HandsLeaving_StillEndsTheOldWay`, `FadeOutAll_SilencesPlayerVoices_NotNature`.
- 커밋 `feat(conduct): two fists spread apart end the piece`.

### 갈래 B: 위 줄 공중 이펙트, 아래 줄 풀 (`downbeat-6c-effects`, `6c/effects`)

**Interfaces (Produces):**
- 위 줄 음(`Parts.IsSky`)은 들릴 때 `HandScreenMap.Viewport(X01, Y01, overlayFraction)` 자리의 공중에 이펙트: 하프 = 퍼지는 빛 고리, 리코더 = 떠오르는 민들레 씨(지금 씨 입자를 이 자리로), 종 = 반짝이며 천천히 떨어지는 별가루. 색은 `cells.colors`. 꽃은 피우지 않는다.
- 아래 줄 음: 낮은 현 = 손 쪽으로 길게 눕는 물결(가로로 긴 Sweep 변형), 마림바 = 지금처럼 꽃잎 + 둥근 바람, 손북 = 세고 넓은 둥근 울림(Radial, 큰 진폭). GPU/CPU 바람 계산이 같아야 하는 시험을 지킨다. `WindFieldGpu.Capacity` 16을 넘치지 않게 손북·낮은 현의 수명을 정한다.
- content `sky { ringCount, seedCount, starCount, lifeSec, lowCountScale 0.5 }`(범위는 갈래가 정함).
- 시험: `SkyNote_SpawnsItsEffect_AtTheHandsScreenPoint`, `SkyNote_NeverBlooms`, `LowString_LaysTheGrassInALongWave`, `HandDrum_PushesAWideRing`, `LowQuality_HalvesTheSkyParticles`, 바람 GPU/CPU 같음.
- 커밋 `feat(meadow): sky effects for the top row, three grass reactions for the bottom row`.

### 갈래 C: 음표 꼬리와 칸 안내 (`downbeat-6c-trail`, `6c/trail`)

**Interfaces (Produces):**
- 손마다 화면 점(`HandScreenMap.Viewport`)을 따라 음표(♪ 모양 절차 메시) 꼬리. 추적 장 사이는 부드럽게(지금 리본의 `HandRibbonFollow`와 같은 걸러내기를 화면 좌표에 적용). 움직임이 클수록 많이, 1초쯤 뒤 사라짐, 색은 손이 있는 칸의 `cells.colors`. 손이 사라지면 잦아듦. 주먹 손은 꼬리 없음.
- 칸 안내: 손이 새 칸에 들어가면 그 칸 이름(`cells.labels`)이 칸 안에 `trail.labelSec` 동안 떴다 사라지고, 칸 테두리가 은은하게 밝아진다. 6칸 격자는 평소엔 안 보인다(안내 중에는 갈래 D가 흐리게 그림).
- content `trail { notesPerSec 6 (0..30), maxNotes 40 (0..200), lifeSec 1.0 (0.2..4), size 0.035 (0.01..0.1), labelSec 1.2 (0..5), lowCountScale 0.5 }`.
- 시험: `Trail_FollowsTheHand_SmoothlyAt6Fps`(장 사이 위치가 이어짐), `Trail_MoreMovement_MoreNotes`, `Trail_FistHand_HasNoTrail`, `CellLabel_ShowsOnceOnEntering_NotWhileStaying`.
- 커밋 `feat(meadow): note trail and cell names on screen`.

### 갈래 D: 따라 하기 안내 (`downbeat-6c-tutorial`, `6c/tutorial`)

**Interfaces (Produces):**
- `public sealed class TutorialDirector` (Core): `TutorialFrame Update(double now, ExperienceState state, double idleSince, bool openHandVisible, bool cellChanged, bool bothFists)`. Idle이 `tutorial.idleDelaySec` 10 이상이면 나타남(`fadeInSec` 0.6). 단계 3개를 `stepSec` 5씩 돌고 반복. 그 단계 동작이 보이면 ✓(1: 편 손, 2: 칸 바뀜, 3: 두 손 모두 주먹), 다음 단계로는 시간이 차야 넘어감. Open 손이 보이면 `fadeOutSec` 0.8에 걸쳐 사라짐(연주는 기다리지 않음). 앱을 켠 뒤도 10초.
- 화면: 들판을 살짝 어둡게(`dim` 0.45), 가운데 큰 손 그림(편 손 / 두 주먹이 양쪽으로 벌어지는 움직임) + 한 줄 한국어: "손을 펴서 들어 보세요" / "손을 옮기면 악기가 바뀌어요" / "두 주먹을 쥐고 벌리면 끝". 2단계에는 6칸 격자와 이름을 흐리게. 단계 표시 점 3개와 ✓. 손 그림은 절차 메시(편 손: 손바닥 + 손가락 5개, 주먹: 둥근 사각형 + 마디 선).
- content `tutorial { idleDelaySec 10 (3..120), stepSec 5 (2..20), fadeInSec 0.6, fadeOutSec 0.8 (0..3), dim 0.45 (0..0.9), texts ["손을 펴서 들어 보세요","손을 옮기면 악기가 바뀌어요","두 주먹을 쥐고 벌리면 끝"] }`.
- 시험: `Appears_TenSecondsAfterTheVisitEnds`, `Appears_TenSecondsAfterStart`, `Steps_LoopEveryFiveSeconds`, `Check_ShowsWhenTheStepIsDone_ButDoesNotSkipTime`, `OpenHand_FadesItOut_AndPlayingIsNotDelayed`, `Fists_DoNotFadeIt`, `NeverShows_WhilePlaying`.
- 커밋 `feat(meadow): follow-along tutorial after ten quiet seconds`.

## 3단계: 통합과 마무리 (오케스트레이터)

- [ ] `feat/conduct-six-cells`에 A, B, C, D를 합친다(겹치는 곳: `ContentModels`, `ContentValidator`, `ContentSections`, content.json, content-build.mjs, `DownbeatApp`, `MeadowStage`). 세 시험 묶음 통과.
- [ ] 초당 6장 녹화 시험: 데모 녹화를 6장으로 솎은 것과 두 주먹 녹화(갈래 P 도우미)로 끝까지 돌려 음, 끝내기, 카드, 안내가 모두 나오는지.
- [ ] 독립 검토(전체 브랜치), 고칠 것 반영.
- [ ] Mac 플레이어로 화면 캡처 몇 장(안내, 위 줄 이펙트, 꼬리, "끝") → `measurements/visual/six-cells-*.jpg`.
- [ ] `tools/unity_build.sh mac`, `tools/unity_build.sh windows`, `tools/package_windows.sh`(업로드는 하지 않는다).
- [ ] `docs/conducting-play.md`, README 상태 줄, 단위 시험 결과(`measurements/unit-tests`) 갱신, 커밋.
