# 들판 오케스트라 (Downbeat)

웹캠 앞에서 손을 움직이면, 해 질 녘 3D 들판이 박자에 맞춰 연주되는 Unity 시제품입니다.
**개인 시제품이며, 실제 전시에 설치한 작품이 아닙니다.**

*A personal Unity prototype: move your hands in front of a webcam and a 3D meadow at dusk plays music on the beat. Not a real exhibition installation.*

- 시연 영상: https://youtu.be/jWMInxaSkUs (2분 21초, Windows 노트북 웹캠으로 실제 연주. 앱 화면에 웹캠 화면을 작게 겹치고 앞뒤만 잘랐습니다. 찍고 붙인 법: [`docs/windows-test-2026-11-02-1040.md`](docs/windows-test-2026-11-02-1040.md) 4절)
- 기간: 2026.10.28~ (기획 10.28, 개발 10.29부터)
- 사용 기술: Unity 6 URP, C#, VContainer / Python, MediaPipe Hand Landmarker

![전시용 배치: 왼쪽은 손으로 연주하는 들판, 오른쪽은 꽃 악보와 곡 이름이 적힌 오늘의 카드가 쌓이는 기록 화면](measurements/songs/song-end-and-card.jpg)

| 곡이 시작될 때 | 위 줄 손: 하늘 이펙트 | 따라 하기 안내 |
|---|---|---|
| ![곡 이름이 뜬 들판](measurements/songs/song-title-quiet-meadow.jpg) | ![위 줄 손과 하늘 이펙트](measurements/visual/six-cells-effects-sky.jpg) | ![따라 하기 안내](measurements/visual/six-cells-tutorial-2.jpg) |

## 무엇을 하나요

- **6칸, 악기 6개**: 화면이 위아래 2줄 x 좌우 3칸이고, 칸마다 악기가 하나씩 있습니다(위: 하프, 리코더, 종 / 아래: 낮은 현, 마림바, 손북). 손을 크게 움직일수록 소리가 커지고 음이 촘촘해집니다. 소리는 박자 칸(96 BPM, 16분음표)에 맞춰 나서 박자에서 벗어나지 않습니다.
- **곡 모드**: 방문마다 곡 3개를 차례로 칩니다. 위 줄 손은 악보의 가락을, 아래 줄 손은 그 마디의 화음을 냅니다. 곡이 끝나거나 두 주먹을 쥐고 양쪽으로 벌리면 "끝"이 뜨고, 곡 이름이 적힌 카드가 기록 화면에 쌓입니다. Tab 설정 창에서 예전 자유 연주로 바꿀 수 있습니다.
- **들판의 반응**: 아래 줄 손 밑에서는 풀이 눕고 꽃이 피고, 위 줄에서는 하늘에 빛이 생깁니다. 반딧불이 종소리에 답하고, 오래 세게 연주하면 하늘이 노을까지 갑니다. 연주가 끝나고 10초가 지나면 따라 하기 안내가 돕니다.
- **화면 2대**: 한쪽은 들판, 한쪽은 꽃 악보와 '오늘의 들판' 카드가 쌓이는 기록 화면입니다. 모니터가 1대뿐이면 기록 화면이 들판 아래에 띠로 나옵니다.
- **개인정보**: 기본 설정에서는 카메라 영상이 손 추적 앱 밖으로 나가지 않고 저장되지 않습니다. Unity 앱은 손 좌표만 받습니다. 시연 영상을 찍을 때만 켜는 저장 옵션(`-SaveVideo`)이 따로 있습니다. 다만 손 인식에 쓰는 구글 라이브러리(MediaPipe)는 연주 중에도 사용 통계(처리 장 수, 걸린 시간, 오류 종류 등)를 구글로 보내고, 끄는 설정이 없습니다. 영상과 손 좌표는 보내지 않습니다. 자세히: [`docs/windows-hand-setup.md`](docs/windows-hand-setup.md) 8절

## 구조

```
웹캠 → 손 추적 앱 (Python, MediaPipe; 카메라 영상은 여기서만)
          │ 손 좌표만 (UDP)
          ▼
       Unity 들판 앱 (박자 격자, 손짓 해석, 곡, URP 들판) → 화면 2대 + 소리
감시 앱: 두 앱을 켜고, 멈추면 다시 켭니다. 상태 로그: 1분마다 CSV, 5초마다 하트비트
```

| 폴더 | 내용 |
|---|---|
| [`unity/Downbeat/`](unity/Downbeat/) | Unity 프로젝트(들판, 곡, 기록 화면, 소리, 자동 화질, 상태 로그) |
| [`tracker/`](tracker/README.md) | Python 손 추적 앱. 손 좌표 형식은 [`docs/protocol/hand-udp-v1.md`](docs/protocol/hand-udp-v1.md) |
| [`supervisor/`](supervisor/README.md) | 두 앱을 켜고 지켜보다 멈추면 다시 켜는 감시 앱 |
| [`content/`](content/) | 곡, 악기, 들판 값(`content.json`), 소리, 배경 그림. 다시 빌드하지 않고 바꿉니다 |
| [`tools/`](tools/) | 빌드, 측정, Windows 실행 스크립트 |
| [`docs/`](docs/) | 설계, 계획, 측정 방법, Windows 점검 문서 |
| [`measurements/`](measurements/) | 문서에 적은 숫자의 원본(JSON, CSV, 로그, 사진) |
| [`archive/`](archive/) | 방향을 바꾸기 전의 웹(WebGL) 시제품 코드 |

## 현장을 생각해 먼저 시험한 것

- **약한 노트북에 맞춘 연주 방식**: 내장 그래픽 노트북(i7-10610U)에서 다른 프로그램이 CPU를 나눠 쓰자 손 추적이 초당 6장까지 떨어졌고, 손을 내려치는 순간을 재는 방식으로는 첫 1분에 음이 하나도 나지 않았습니다. 그래서 지휘하듯 연주하는 방식으로 바꿨습니다. 기록: [`docs/conducting-play.md`](docs/conducting-play.md)
- **설정 없이 맞춰지는 화질**: 시작할 때 그래픽 장치 이름과 첫 8초로 화질 단계를 고릅니다. 실행 중에 단계는 올리지 않고, 렌더 배율만 느려지면 낮추고 여유가 보이면 천천히 올립니다.
- **다시 빌드하지 않는 콘텐츠 교체**: 곡, 악기, 들판 값은 실행 파일 옆 `content` 폴더에 둡니다. 깨진 JSON, 없는 파일, 범위 밖 값이 있으면 마지막 정상본이나 앱 안의 기본 내용으로 시작하고 이유를 로그에 남깁니다. 악기가 못 내는 음이 있는 곡은 그 곡만 빼고 시작합니다. 들판 값은 실행 중에 F5로 바로 바뀌고, 곡과 악기 같은 나머지는 다시 켤 때 바뀝니다.
- **멈춰도 다시 켜기**: 감시 앱이 하트비트가 끊긴 앱을 다시 켜고, 짧은 시간에 여러 번 죽으면 기다리는 시간을 늘립니다. 기록: [`supervisor/README.md`](supervisor/README.md)
- **멈추면 스스로 꺼지기**: 밤샘 시험에서 그래픽 드라이버가 재시작된 뒤 앱이 멈춘 채 닫기 신호도 듣지 않았습니다. 그래서 그래픽 장치를 잃거나 화면이 20초 넘게 멈추면 앱이 스스로 꺼져, 감시 앱이 바로 다시 켜게 했습니다(Windows에서 다시 확인하기 전). 기록: [`docs/windows-soak-2026-11-02.md`](docs/windows-soak-2026-11-02.md)

## 측정

| 값 | 결과 | 조건과 원본 |
|---|---|---|
| 약한 노트북 손 추적 | 초당 6장(중앙값) | i7-10610U 내장 그래픽, 다른 프로그램이 CPU를 나눠 쓸 때, 한 장 인식에 141 ms. [`docs/conducting-play.md`](docs/conducting-play.md) 1절 |
| 밤샘 구동(앱 혼자) | 6시간 57분 오류 없음, 그 뒤 그래픽 드라이버 재시작으로 멈춤 | 같은 노트북, 미리 기록한 손 움직임으로 연주 264바퀴, 화면 평균 29.9장/초, 늦은 음 0, 메모리 증가 시간당 약 0.06 MB. 멈춘 뒤 스스로 꺼지게 고쳤습니다(Windows 확인 전). [`docs/windows-soak-2026-11-02.md`](docs/windows-soak-2026-11-02.md) |

## 어떻게 만들었나요

구현은 AI 코딩 도구(Claude Code)로 했습니다. 저는 무엇을 만들지와 연주 규칙, 받아들일 기준을 정하고, 테스트와 Windows 노트북 직접 연주를 통과한 것만 받아들였습니다. 제가 정한 것의 예:

- 캐릭터 단원 대신 **자연이 주인공**인 들판, 그리고 **꽃 종류로 악기를 나누는** 배치 ([설계 문서](docs/superpowers/specs/2026-10-29-meadow-orchestra-design.md) 2절)
- 약한 노트북에서 음이 안 나자 정한 **지휘 연주 규칙**, 그리고 **위아래 2줄 x 3칸, 움직임 크기 = 소리 크기, 두 주먹으로 끝내기** ([6칸 설계](docs/superpowers/specs/2026-10-31-conduct-six-cells-design.md))
- **곡 모드**: 곡 3개를 방문마다 돌아가며, 위 줄은 가락, 아래 줄은 화음, 자유 연주는 Tab 스위치로 남기기 ([곡 모드 설계](docs/superpowers/specs/2026-11-01-song-mode-design.md))
- "풀이 흔들리는게 아니라 움직임에 따라 늘어나는 느낌"이라는 제 지적에서 시작한 **풀 휘기 수정** (계획 문서의 다듬기 13번)

| 날짜 | 한 일 |
|---|---|
| 10.28 | 기획 |
| 10.29 | 웹(WebGL) 시제품 Day 0~2, 같은 날 설치형 Unity 앱과 들판 테마로 방향을 바꿈 |
| 10.30 | Windows 노트북 시험, 지휘 연주, 자동 화질, 화면 다듬기 |
| 10.31 | 6칸 악기, 두 주먹 끝내기, 따라 하기 안내, 하늘 이펙트, 음표 꼬리 |
| 11.01 | 손이 화면에 보이는 자리 고침, 곡 모드, Tab 설정 창 |
| 11.02 | 시연 영상 촬영 기능과 시연 영상, 밤샘 구동 |
| 11.03 | 멈추면 스스로 꺼지기 |

설계와 계획: [`docs/superpowers/specs/`](docs/superpowers/specs/), [`docs/superpowers/plans/`](docs/superpowers/plans/). 개발 중에 쓰던 README 원문은 [`DEVELOPMENT.md`](DEVELOPMENT.md)에 있습니다.

## 실행

실행 파일은 저장소에 들어 있지 않습니다. 웹캠과 Python 손 추적 앱이 필요합니다.

- **Windows**: 손 추적 설치는 [`docs/windows-hand-setup.md`](docs/windows-hand-setup.md), 실행과 측정은 [`docs/windows-runbook.md`](docs/windows-runbook.md)
- **Mac 시연**: [`ops/mac/run-demo.command`](ops/mac/run-demo.command). 카메라가 있으면 손으로, 없으면 녹화한 손으로 연주합니다
- **빌드**: [`tools/unity_build.sh`](tools/unity_build.sh)

## 이 공개판에 대해

개인 작업 저장소의 한 시점을 이력 없이 옮긴 것입니다. 방향을 바꾸기 전 캐릭터 무대의 그림과 그 화면 사진/영상은 넣지 않았습니다. 측정 기록 속 내 컴퓨터 경로는 `<repo>`와 `~`로 바꿨고, 숫자는 그대로입니다.

## 라이선스

코드는 MIT([`LICENSE`](LICENSE))입니다. 악기 소리(VCSL, CC0), 배경 그림(생성형 AI), 손 인식 모델처럼 외부에서 온 것의 출처는 [`NOTICE`](NOTICE)에 있습니다. 곡 3개 중 "잔잔한 들판"과 "밝은 아침"은 AI(Claude)로 앱 틀에 맞춰 작곡했고, "올드 랭 사인"은 스코틀랜드 전승 선율(공유 저작물)을 편곡했습니다(`content/content.json`의 `songs`).
