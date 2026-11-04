# 손으로 들판 오케스트라 연주하기: Windows 설치와 실행

목적: 이 PC의 웹캠으로 진짜 손을 인식시켜서 들판 오케스트라를 연주해요. 앱(`Downbeat.exe`)은 이미 있고, 여기서 설치하는 것은 손을 읽는 작은 프로그램(손 추적)이에요. 설치는 처음 한 번만 하면 되고, 관리자 권한이 필요 없어요.

한 줄 요약: **설치 스크립트를 한 번 실행하고, 그다음부터는 카메라 앱을 닫은 뒤 실행 스크립트 하나로 손 추적과 앱을 같이 켜요. 노트북이면 `-Backend msmf` 를 붙여요(4절 명령에 들어 있어요). 화질은 앱이 알아서 맞춰요.**

## 1. 무엇이 설치되나요

모두 내 계정 안에만 들어가요. 관리자 권한, Windows 설정 변경, PATH(환경 변수) 변경은 없어요.

| 무엇 | 어디에 | 크기 |
|---|---|---|
| uv 0.13.6 (Python 설치 도구) | `%LOCALAPPDATA%\Downbeat\uv\bin` | 약 30 MB |
| Python 3.12 (uv가 내려받아요) | `%LOCALAPPDATA%\Downbeat\uv\python` | 약 60 MB |
| 손 추적 패키지 (mediapipe, opencv 등)가 든 Python 환경 | `%LOCALAPPDATA%\Downbeat\venv` | 약 270 MB |
| 내려받은 파일 보관함 | `%LOCALAPPDATA%\Downbeat\uv\cache` | 약 270 MB |
| 손 인식 모델 `hand_landmarker.task` | `C:\Users\(내 이름)\.cache\downbeat` | 약 8 MB |

합쳐서 약 0.65 GB예요(Windows 노트북 한 대에서 설치한 뒤 잰 값). 빈 공간이 2 GB 이상 있으면 안심이에요.

- uv는 공식 설치 파일을 정해 둔 버전(0.13.6)으로 받아요. 받은 설치 파일은 지문(SHA-256)이 맞을 때만 실행해요.
- 손 인식 모델은 구글 공식 주소에서 받고, 지문(SHA-256)이 맞는 파일만 써요.
- `%LOCALAPPDATA%` 는 `C:\Users\(내 이름)\AppData\Local` 이에요. 탐색기 주소창에 `%LOCALAPPDATA%\Downbeat` 라고 적어도 열려요.
- Python 환경(파일 약 3,500개)과 실행 기록은 압축을 푼 폴더가 아니라 `%LOCALAPPDATA%\Downbeat` 에 들어가요. 이곳은 OneDrive가 동기화하지 않아서, 바탕화면에 풀었어도 패키지 파일이 OneDrive에 올라가지 않고, 설치하는 동안 OneDrive가 파일을 붙잡지도 않아요. 압축을 푼 폴더 안에는 작은 `tracker\downbeat_tracker.egg-info` 폴더 하나만 생겨요.
- 압축을 푼 폴더가 여러 개여도(새 판을 옛 판 옆에 풀었을 때, `downbeat-hand-tracker` 와 `downbeat-windows` 를 둘 다 풀었을 때) 환경은 하나를 같이 써요. 새 판의 패키지 목록(`tracker\requirements.lock.txt`, `tracker\pyproject.toml`)이 설치할 때와 같으면 설치 없이 바로 연주할 수 있어요. 다르면 연주 스크립트가 그 폴더에서 설치를 한 번 더 하라고 알려 줘요.
- **예전 판으로 설치했던 PC**: 예전에는 환경을 압축을 푼 폴더 안(`tracker\.venv`)에 만들었어요. 이제는 그 환경을 쓰지 않아서, 처음 연주할 때 설치를 한 번 더 하라고 알려 줘요. 내려받은 파일 보관함(`%LOCALAPPDATA%\Downbeat\uv\cache`)에 패키지가 남아 있으면 다시 내려받지 않아요. 남은 `tracker\.venv` 는 7절의 지우기 스크립트가 첫 질문에서 지울지 물어봐요.

## 2. 처음 한 번: 설치 (5~10분)

1. 앱 zip(`downbeat-windows`)을 **`C:\Downbeat`** 에 풀어 둔 상태에서 시작해요(탐색기에서 `C:\` 안에 새 폴더 `Downbeat` 를 만들고 그 안에 풀어요). **바탕화면이나 문서 폴더에는 풀지 않는 게 좋아요.** 그곳은 OneDrive가 동기화해서, 새로 푼 파일을 올리는 동안 CPU를 써요(손 추적 환경은 이제 압축을 푼 폴더 밖에 들어가요, 1절). 노트북에서 손 추적이 1초에 6장까지 느려졌을 때 OneDrive가 CPU 코어 0.8개, 탐색기가 1.6개를 쓰고 있었어요(Windows 쪽 측정). 바탕화면에 풀었어도 스크립트와 아래 명령은 바탕화면도 찾아요. 작은 묶음 `downbeat-hand-tracker` 를 따로 받았다면 같은 `C:\Downbeat` 에 풀어 주세요. 앱은 `downbeat-windows` 것을 그대로 써요.
2. 인터넷으로 받은 zip이면: zip을 오른쪽 클릭 → **속성** → 아래쪽 **차단 해제** 체크 → 확인한 다음에 풀어요. USB로 옮겼다면 건너뛰어요.
3. 다 풀었으면 zip 안을 보여 주던 탐색기 창은 닫아요. 열어 두면 탐색기가 CPU를 오래 붙잡고 있을 수 있어요.
4. 시작 메뉴에서 **PowerShell**을 열어요. 관리자 권한은 필요 없어요.
5. 아래 명령을 통째로 복사해서 붙여 넣고 Enter를 눌러요.

```powershell
& {
  $desk = [Environment]::GetFolderPath('Desktop')
  $roots = @('C:\Downbeat', $desk) | Where-Object { Test-Path -LiteralPath $_ }   # 먼저 C:\Downbeat, 없으면 바탕화면
  $script = Get-ChildItem $roots -Recurse -Depth 3 -Filter setup-hand-tracker.ps1 -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
  if (-not $script) { "C:\Downbeat 와 바탕화면에서 setup-hand-tracker.ps1 을 못 찾았어요. 압축을 풀었는지 확인해 주세요."; return }
  "사용할 판: $($script.FullName) (만든 때 $($script.LastWriteTime))"
  powershell -ExecutionPolicy Bypass -File $script.FullName
}
```

`-ExecutionPolicy Bypass` 는 이번 실행에만 적용돼요. PowerShell의 보안 설정 자체는 바뀌지 않아요. 아래 모든 스크립트가 같은 방식으로 실행돼요.

6. 스크립트가 "이 PC에 설치할 것"을 먼저 보여 줘요. 어디에 무엇이 들어가는지 읽고, 괜찮으면 **Y** 를 입력하고 Enter를 눌러요. **N** 이면 아무것도 설치하지 않고 끝나요.
7. 6단계가 차례로 돌아가요. 단계마다 `[완료]` 가 찍혀요.
   - [1/6] uv 설치 (이미 있으면 건너뛰어요)
   - [2/6] Python 3.12 가상 환경 만들기 (`%LOCALAPPDATA%\Downbeat\venv`)
   - [3/6] 손 추적 패키지 설치 (제일 오래 걸려요). 다른 폴더에서 이미 같은 패키지 목록으로 설치했다면 이 폴더의 `tracker` 를 쓰도록 연결만 해요(몇 초).
   - [4/6] 손 인식 모델 내려받기와 검사
   - [5/6] 카메라 없이 자체 점검: 손 사진이 움직이는 짧은 영상으로 손이 읽히는지 확인해요
   - [6/6] 이 PC에서 보이는 카메라 목록: 손 추적이 쓰는 번호와 함께 나와요(예: `0번: Integrated Webcam`). 휴대폰 연결 카메라나 OBS 같은 가상 카메라는 "가상 카메라"라고 표시돼요. Windows 카메라 앱이 켜져 있으면 닫으라고 알려 줘요.
8. 끝나면 "설치와 점검이 끝났어요"와 함께 **다음에 칠 명령**이 그대로 나와요. 그 명령 대신 4절의 명령을 써도 돼요(4절 명령에는 `-Quality Low -Backend msmf` 가 들어 있어요).

중간에 실패하면 이유와 해 볼 것이 빨간 글씨로 나와요. 같은 명령을 다시 실행하면 끝난 단계는 건너뛰고 이어서 해요.

## 3. 연주하기 전에 확인할 것 (1분)

손으로 연주하는 기본 방식(지휘하듯 연주, 4절 5번)은 손 추적이 1초에 5~10장만 처리해도 돼요. 예전 내려치기 방식(`-PlayMode strike`)은 손 추적이 보낸 바로 앞 장과 지금 장이 0.07초 안에 있어야 속도를 재서 **1초에 15장보다 많이(가능하면 25~30장)** 처리해야 음이 나요. 그래도 손 추적이 빠를수록 반응이 좋아요. 아래가 지켜지지 않으면 손이 느려지거나 보여도 소리가 나지 않을 수 있어요.

1. **Windows 카메라 앱을 닫아요.** 카메라가 잘 나오는지 카메라 앱으로 봤다면 그 창을 꼭 닫아요. 다른 창 뒤에 가려져 있어도 카메라를 쥐고 있어서, 손 추적은 까만 화면을 1초에 한 장씩만 받아요. 화상 회의(Zoom, Teams, 브라우저의 회의 탭)와 OBS도 닫아요.
2. **CPU를 많이 쓰는 창을 닫아요.** 영상이나 회의가 열린 브라우저 탭, zip 안을 보여 주는 탐색기 창이 대표적이에요. 작업 관리자(Ctrl+Shift+Esc)의 CPU 칸에서 확인할 수 있어요.
3. **노트북이면 충전기를 꽂아요.** 배터리로 돌면 CPU와 그래픽이 둘 다 느려져요. Windows의 전원 모드를 **최고 성능**으로 두면 더 나아질 수 있어요. 이건 사용자가 직접 정하는 설정이고, 스크립트는 전원 설정을 절대 바꾸지 않아요.
4. 노트북 카메라에 덮개(화면 위 작은 슬라이더)가 있다면 열고, 방을 밝게 해요. 어두우면 웹캠이 스스로 1초에 15장 이하로 느려질 수 있어요.

## 4. 손으로 연주하기

1. PowerShell에 아래 명령을 붙여 넣고 Enter를 눌러요.

```powershell
& {
  $extra = @('-Backend', 'msmf')   # 옵션은 여기에 적어요(아래 표). 예: @('-Backend', 'msmf', '-Camera', '1', '-Quality', 'Low')
  $desk = [Environment]::GetFolderPath('Desktop')
  $roots = @('C:\Downbeat', $desk) | Where-Object { Test-Path -LiteralPath $_ }   # 먼저 C:\Downbeat, 없으면 바탕화면
  $script = Get-ChildItem $roots -Recurse -Depth 3 -Filter play-with-hands.ps1 -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
  if (-not $script) { "C:\Downbeat 와 바탕화면에서 play-with-hands.ps1 을 못 찾았어요. 압축을 풀었는지 확인해 주세요."; return }
  "사용할 판: $($script.FullName) (만든 때 $($script.LastWriteTime))"
  powershell -ExecutionPolicy Bypass -File $script.FullName @extra
}
```

2. 이런 순서로 진행돼요.
   - 손 추적이 설치돼 있지 않거나, 설치된 패키지가 이 폴더의 손 추적과 맞지 않으면 앱을 켜기 전에 멈추고 칠 설치 명령을 보여 줘요. 예전 판으로 `tracker\.venv` 에 설치했던 PC도 처음 한 번은 이렇게 멈춰요(1절).
   - `-Backend msmf` 없이 켜면 쓸 카메라의 번호와 이름이 먼저 나와요(예: `카메라 0번: Integrated Webcam`). 가상 카메라라면 그렇다고 알려 줘요.
   - 손 추적이 창 없이 뒤에서 켜지고, 처음 3초 동안 카메라 화면이 제대로 들어오는지 스스로 확인해요(빠르기와 밝기). 보통 10초 안에 "카메라 화면이 잘 들어와요"가 나와요.
   - "앱 화질: Low" 가 나오고 앱이 전체 화면으로 열려요. 연주 중 손 추적은 1초에 25~30장쯤 처리해요. 가장 새로 만든 `Downbeat.exe` 를 알아서 골라요(스크립트 옆 폴더 먼저, 없으면 바탕화면).
3. 카메라 확인이 통과하지 못하면 앱을 켜기 전에 멈추고 이유를 알려 줘요.
   - "카메라가 화면을 1초에 한 장씩만 보내요": 다른 프로그램이 카메라를 잡고 있어요. 지금 켜져 있는 카메라 앱 이름도 같이 나와요.
   - "카메라 화면이 거의 까매요": 카메라 덮개나 방의 밝기를 확인해요.
   - "카메라는 열렸는데 화면이 한 장도 오지 않아요": 다른 프로그램이 카메라를 쓰고 있거나, 5절의 개인 정보 설정이 막고 있어요.

   원인을 고친 다음 **Y** 를 누르면 다시 확인해요. **N** 이면 그대로 앱을 켜요.
4. "Windows의 PC 보호" 창이 뜨면 **추가 정보 → 실행**을 눌러요(서명하지 않은 개인 시제품이라서 떠요).
5. 손을 카메라 앞에 들어요. **손을 들고 있기만 해도 음악이 박자에 맞춰 이어져요**(지휘하듯 연주, 기본이에요).
   - 손을 **높이** 들수록 소리가 커요. 내리면 작아져요. 가만히 들고 있어도 작게 계속 연주해요.
   - **좌우**는 악기예요: 왼쪽 하프, 가운데 리코더, 오른쪽 마림바. 음 높이는 곡이 정해요.
   - 손을 **많이 움직일수록 촘촘해져요.** 가만히 있으면 한 박에 하나, 천천히 흔들면 반 박마다, 빠르게 휘저으면 반의 반 박마다 울려요.
   - 손이 화면에서 나가면 1.5초에 걸쳐 잦아들어요.
   - 내려치기가 필요 없어서, 손 추적이 1초에 5~10장으로 느려져도 연주돼요. 예전 방식(손바닥을 아래로 빠르게 내려칠 때만 소리가 나요, 1초에 15장 이상 필요)은 `-PlayMode strike` 로 켜요.
6. 끝낼 때는 앱 화면을 한 번 클릭하고 **Alt+F4**. 앱이 꺼지면 손 추적도 저절로 꺼져요. 앱이 갑자기 죽어도 똑같이 꺼져요. PowerShell 창을 X로 닫았다면, 다음 실행 때 남아 있던 손 추적을 먼저 정리해요.
7. 앱을 끈 뒤 PowerShell에 손 추적의 마지막 상태가 나와요. 손이 한 번도 잡히지 않았다면 그렇다고 알려 줘요.

옵션은 위 명령의 `$extra = @(...)` 줄 안에 적어요. 여러 개면 쉼표로 이어요. 설치가 끝났을 때 나온 한 줄 명령 맨 끝에 그대로 붙여 써도 돼요(예: `... play-with-hands.ps1" -Quality Low`).

| 옵션 | 하는 일 |
|---|---|
| `-Quality Auto` | 화질을 앱이 알아서 맞춰요(`-Quality` 를 안 주면 이게 기본이에요). 내장 그래픽 노트북이면 Low 로 시작하고, 느려지면 한 단계씩 내려가요(런북 "자동 화질"). |
| `-Quality Low` | 앱 화질을 낮춰요(풀이 약 6분의 1). 그래픽 카드가 따로 없는 노트북(Intel 내장 그래픽 등)에서는 꼭 붙여요. High(앱 기본)로 켜면 그래픽이 꽉 차서 CPU도 같이 느려지고, 손 추적이 1초에 몇 장밖에 못 해서 내려치기를 못 잡아요. |
| `-PlayMode strike` | 손 연주 방식을 예전 내려치기로 바꿔요. 기본(`conduct`)은 손을 들고 있으면 연주해요(5번). 지연 측정(`-LatencyMode`)은 내려친 순간부터 재기 때문에 이 방식을 저절로 써요. |
| `-LogPose` | 손 모양 읽기를 확인하는 기록을 켜요. 앱 로그(`Player.log`)에 1초마다 손마다 `[pose]` 한 줄(손가락 비율 4개, 모양, 연주 여부, 칸)이 남아요. 힘을 뺀 손, 주먹, 반쯤 편 손을 몇 초씩 들고 그 줄을 보내 주세요. 연주는 달라지지 않아요. |
| `-Quality High` | 가장 좋은 화질로 고정해요(자동 화질이 꺼져요). 그래픽 카드가 따로 있는 전시 PC에서 써요. 옵션을 빼면 Auto 예요. |
| `-Camera 1` | 카메라 번호를 바꿔요(기본 0). 번호는 설치 [6/6]과 실행 시작 때 나온 목록 그대로예요. |
| `-Preview` | 카메라 화면에 손 점이 찍힌 확인용 창을 같이 띄워요. 아무것도 저장하지 않아요. 그 창에서 **q** 를 누르면 손 추적이 멈춰요. |
| `-SaveVideo C:\Downbeat\demo.mp4` | 시연 영상용으로 웹캠 화면을 이 파일에 저장해요. 좌우를 거울처럼 뒤집어서(앱 화면과 같은 방향), 1초에 30장으로 실제 시간대로 남겨요. 앱을 끄면 파일이 마무리돼요. 얼굴이 나오니 필요할 때만 써요. 이 PC에만 남고 어디로도 보내지 않아요. `$extra` 줄에는 `'-SaveVideo', 'C:\Downbeat\demo.mp4'` 처럼 두 칸으로 적어요. |
| `-SaveVideoPoints` | `-SaveVideo` 와 같이 쓰면 영상에 손 점 21개와 뼈대를 그려 넣어요(손 0은 주황, 손 1은 하늘색). |
| `-RecordApp C:\Downbeat\take1` | 시연 영상용으로 앱이 자기 화면과 소리를 이 새 폴더에 녹화해요(그림 JPEG와 소리 `audio.wav`가 같은 시계로). `-RecordSeconds` 가 지나면 앱이 스스로 꺼져요. **그 전에 창의 닫기 버튼이나 Alt+F4 로 꺼도 그때까지의 소리와 그림 시각(`audio.wav`, `frames.csv`, `recording.json`)이 저장돼요.** `recording.json`에 `"endedEarly": true` 와 실제로 찍은 초가 적혀요. **작업 관리자로 강제 종료하거나 앱이 멈춰서 끄면 소리가 저장되지 않고 그림만 남아요.** 이 고침이 들어가기 전의 앱 빌드도 중간에 끄면 그림만 남아요. `recording.json`의 `videoZeroUtc` 는 영상 0초가 화면과 소리에 나오는 UTC 시각이에요. `-SaveVideo` 영상의 `first picture at` 시각과 맞출 때 써요. 몇십 밀리초는 어긋날 수 있어요. `startedUtc` 는 소리를 만든 시각이라 출력 지연(`outputLatencySec`)만큼 빨라서 맞추는 데 쓰지 않아요. 이미 녹화가 든 폴더는 거절해요. `-SaveVideo` 와 같이 쓰면 웹캠과 앱을 한 번에 찍어요. |
| `-RecordSeconds 180` | 앱 녹화 길이(초, 기본 180). |
| `-RecordSize 1280x720`, `-RecordFps 30` | 앱 녹화의 크기와 1초당 장 수(기본 1280x720, 30). 이 노트북에서 1280x720은 앱이 1초에 약 21장, 960x540은 약 25장으로 돌았어요. 더 매끄럽게 찍으려면 `-RecordSize 960x540`. |
| `-Mouse` | 카메라 없이 마우스로 연주해요(무대를 클릭). 손 추적을 켜지 않아요. 소리가 나는지 먼저 확인할 때도 좋아요. |
| `-Backend msmf` | 카메라를 Media Foundation 방식으로 열어요. 기본(auto)은 Windows에서 DirectShow인데, 웹캠에 따라 DirectShow로는 1초에 15장밖에 안 와요. 이 노트북의 Integrated Webcam은 DirectShow 15장, msmf 30장이었어요. 그래서 4절 명령에 들어 있어요. msmf로 카메라가 안 열리면 빼 보세요. |

실행 기록은 `%LOCALAPPDATA%\Downbeat\logs` 폴더(`C:\Users\(내 이름)\AppData\Local\Downbeat\logs`)에 실행마다 한 파일씩 쌓여요(`tracker-날짜-시각.log`, 같은 이름의 `.out.txt`, `.err.txt`). 압축을 푼 폴더가 여러 개여도 한곳에 모여요. 여는 법: **Win+R** 을 누르고 `%LOCALAPPDATA%\Downbeat\logs` 를 적은 다음 Enter. 예전 판의 기록은 압축을 푼 폴더의 `tracker\logs` 에 남아 있어요. 안에는 카메라 확인 결과(`camera check: …`), 초당 처리 장수(`fps=`), 인식 속도, 잡힌 손의 수(`hands=`), 오류 메시지만 있어요. 연주 중에도 `fps=` 가 20 이상이면 좋고, 15보다 낮으면 음이 거의 나지 않아요.

## 5. 카메라 개인 정보 설정 확인 (스크립트는 바꾸지 않아요)

손 추적은 Windows에서 "데스크톱 앱"으로 카메라를 써요. 아래 설정이 꺼져 있으면 카메라 화면이 오지 않아요. 이 스크립트는 설정을 바꾸지 않으니, 필요하면 직접 켜 주세요.

Windows 11: **시작 → 설정 → 개인 정보 및 보안 → 카메라** (화면 이름은 Windows 버전에 따라 조금 달라요.)

1. **카메라 액세스** 가 켜져 있나요?
2. **앱에서 카메라에 액세스하도록 허용** 이 켜져 있나요?
3. 화면 아래쪽 **데스크톱 앱에서 카메라에 액세스하도록 허용** 이 켜져 있나요? (제일 자주 꺼져 있어요.)

Windows 10은 **설정 → 개인 정보 → 카메라** 에 같은 항목이 있어요. 스위치가 회색이라 못 바꾼다면 회사나 학교가 관리하는 PC일 수 있어요. 그때는 Claude에게 알려 주세요.

## 6. 잘 안 될 때

먼저 PowerShell에 나온 글을 봐요. 실패하면 이유와 해 볼 것, 로그 마지막 줄이 같이 나와요. 그 글을 Claude에게 보내 주면 원인을 찾기 쉬워요.

- **"카메라가 화면을 1초에 한 장씩만 보내요" / 앱 로그의 `[input]` 줄이 "Camera mode"와 "Mouse mode: no camera found"를 왔다 갔다 해요 / 손 추적 로그에 `fps=0.9 … hands=0` 이 이어져요**
  - 다른 프로그램이 카메라를 잡고 있는 거예요. 카메라는 "열리지만" 진짜 화면 대신 거의 까만 화면이 1초에 한 장씩 와요. 앱은 손 추적 소식이 2초 넘게 없을 때마다 마우스 모드로 바뀌어요.
  - **앱 화면에는 지금 어느 모드인지 알려 주는 표시가 없어요.** 모드가 바뀔 때마다 앱 로그(`Player.log`)에 `[input] Camera mode: strike downward with your hand` 또는 `[input] Mouse mode: no camera found` 같은 줄이 한 줄씩 남아요. 보는 법: PowerShell에 아래를 붙여 넣으면 가장 최근 5줄이 나와요(앱을 다시 켜면 `Player.log` 는 새로 쓰이고, 앞의 것은 같은 폴더의 `Player-prev.log` 에 남아요). 파일은 `C:\Users\(내 이름)\AppData\LocalLow\ovthemoon\Downbeat\Player.log` 에 있어요. 앱을 켤 때 `-logFile` 로 다른 곳을 정했다면 그 파일이에요.
    ```powershell
    Select-String -Path "$env:USERPROFILE\AppData\LocalLow\ovthemoon\Downbeat\Player.log" -Pattern '\[input\]' | Select-Object -Last 5 | ForEach-Object { $_.Line }
    ```
  - 가장 흔한 범인은 **Windows 카메라 앱**이에요. 작업 표시줄에서 찾아 닫아요. 화상 회의와 OBS도 닫아요.
  - 닫은 다음 스크립트가 묻는 곳에서 **Y** 를 누르거나, 실행 명령을 다시 붙여 넣어요.
- **손은 보이는데 소리가 안 나요 / 반응이 아주 느려요 / 로그의 `fps=` 가 15보다 작아요**
  - 앱은 손바닥이 아래로 내려가는 속도로 음을 내요. 손 추적의 앞 장과 지금 장 사이가 0.07초보다 길면(1초에 15장보다 적으면) 속도를 재지 못해서 음이 나지 않아요.
  - `-Quality Low` 를 붙였는지 확인해요.
  - 3절대로 CPU를 많이 쓰는 창을 닫고, 노트북은 충전기를 꽂아요. 방도 밝게 해요.
  - 앱을 켜기 전부터 `fps=` 가 15 근처에서 멈춰 있다면 카메라를 여는 방식(DirectShow) 때문이에요. `-Backend msmf` 를 붙였는지 확인해요(4절 명령에는 들어 있어요). 그래도 그대로면 방을 밝게 해 보고, Claude에게 로그를 보내 주세요.
- **"카메라 화면이 거의 까매요"**: 카메라 덮개를 열고, 방을 밝게 해요.
- **"카메라를 열지 못한 것 같아요" / "카메라는 열렸는데 화면이 한 장도 오지 않아요"**
  - 다른 프로그램이 카메라를 쓰고 있는지 확인해요.
  - 5절의 개인 정보 설정 3가지를 확인해요.
  - 카메라 번호를 확인해요. 설치 [6/6]의 목록에서 0번이 가상 카메라라면 `-Camera 1` 처럼 바꿔요.
  - 카메라를 여는 방식을 바꿔 봐요. `-Backend msmf` 를 쓰고 있다면 빼 보고(기본 DirectShow), 안 쓰고 있다면 붙여 봐요.
- **카메라 화면은 잘 오는데 손이 안 잡혀요**
  - `-Preview` 로 카메라 화면을 직접 봐요.
  - 손바닥이나 손등이 카메라를 향하고, 손 전체가 화면 안에 들어오게 해요.
  - 방이 밝고, 손 뒤가 너무 복잡하지 않은 쪽이 잘 잡혀요.
- **앱을 끈 뒤 "카메라 화면을 한 장도 받지 못했어요"가 나와요**: 카메라가 한 번도 열리지 않은 거예요. 위의 "카메라를 열지 못한 것 같아요"를 따라 해 봐요.
- **소리가 전혀 안 나요(마우스로 클릭해도)**: `-Mouse` 로 켜서 무대를 클릭해 봐요. 그래도 조용하면 스피커나 헤드폰이 켜져 있고 Windows의 기본 출력 장치로 잡혀 있는지, 소리가 꺼져 있지 않은지 확인해요.
- **설치 중 "손 인식 모델을 받지 못했어요"**: 회사나 학교 네트워크가 막았을 수 있어요. 집 네트워크나 휴대폰 핫스팟으로 바꿔서 다시 실행해 봐요.
- **설치 중 "Visual C++ 런타임" 안내**: PC에 Microsoft Visual C++ 런타임이 없는 거예요. 안내된 공식 주소에서 받아 설치한 뒤(관리자 승인 창이 한 번 떠요) 설치 스크립트를 다시 실행해요.

## 7. 전부 지우기

PowerShell에 아래 명령을 붙여 넣어요. 지우기 전에 항상 물어봐요.

```powershell
& {
  $desk = [Environment]::GetFolderPath('Desktop')
  $roots = @('C:\Downbeat', $desk) | Where-Object { Test-Path -LiteralPath $_ }   # 먼저 C:\Downbeat, 없으면 바탕화면
  $script = Get-ChildItem $roots -Recurse -Depth 3 -Filter remove-hand-tracker.ps1 -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
  if (-not $script) { "C:\Downbeat 와 바탕화면에서 remove-hand-tracker.ps1 을 못 찾았어요."; return }
  powershell -ExecutionPolicy Bypass -File $script.FullName
}
```

- 첫 번째 질문(예전 환경이 남아 있을 때만, 폴더마다 하나씩): 예전 판이 압축을 푼 폴더 안에 만든 Python 환경(`tracker\.venv`)을 지울까요? 이 폴더 것과, 바탕화면과 `C:\Downbeat` 에 푼 다른 묶음의 것까지 찾아서 경로를 보여 주고, OneDrive가 동기화하는 곳이면 그렇다고 표시해요. 새 판은 이 환경들을 쓰지 않아요. 다만 예전 판 폴더의 스크립트는 아직 자기 폴더의 환경을 써서, 지운 뒤 그 예전 판으로 손 연주를 하려면 그 폴더에서 설치를 다시 해야 해요. OneDrive 안의 것을 지우면 OneDrive가 "파일을 많이 지웠다"는 알림을 띄울 수 있어요. 이 환경의 파일이에요.
- 두 번째 질문: 손 추적의 Python 환경(`%LOCALAPPDATA%\Downbeat\venv`), 실행 기록(`%LOCALAPPDATA%\Downbeat\logs` 와 이 폴더의 `tracker\logs`), 손 인식 모델을 지울까요? 환경은 압축을 푼 모든 폴더가 같이 쓰니, 지우면 손으로 연주하기 전에 설치를 다시 해야 해요.
- 세 번째 질문: uv와 uv가 내려받은 Python(`%LOCALAPPDATA%\Downbeat\uv`)도 지울까요? 다른 프로그램에서 uv를 같이 쓰고 있다면 N 을 눌러요. 이 프로젝트가 설치하지 않은, 원래 있던 uv는 건드리지 않아요.
- 예전 환경만 치우고 계속 쓰려면 첫 번째 질문에만 **Y**, 나머지는 **N** 을 눌러요.
- 압축을 푼 `downbeat-windows` 폴더와 `downbeat-hand-tracker` 폴더는 직접 지워요.

설치할 때 PATH와 Windows 설정을 바꾸지 않았으니, 지운 뒤에 되돌릴 설정은 없어요.

## 8. 수집하지 않는 것, 어디로 가는지

- **기본으로는 영상을 저장하지 않아요.** 사진도 찍지 않아요. `-Preview` 창도 화면에 그리기만 해요. 카메라 확인도 화면의 빠르기와 밝기만 재고 숫자만 기록해요.
- 카메라 화면은 손 추적 프로그램 안에서만 쓰이고, 파일로 남지 않아요. **`-SaveVideo` 를 줄 때만** 웹캠 화면이 그 파일로 이 PC에 저장돼요(시연 영상용, 4절). 그 파일은 어디로도 보내지 않고, 지우는 것도 직접 해요.
- 앱으로 가는 것은 **손 점 좌표(손마다 21점)와 상태 숫자뿐**이에요. 이 PC 안(`127.0.0.1`, 포트 47800)에서만 오가고, 밖으로 나가지 않아요. 보낼 주소를 바꾸는 옵션 자체가 없어요.
- 손 추적 프로그램 **자신이** 인터넷에 접속하는 때는 설치할 때(uv, Python, 패키지, 손 인식 모델 내려받기)뿐이에요. 모델 파일이 지워져 있으면 실행할 때 한 번 다시 받아요.
- **하지만 손 인식에 쓰는 구글 라이브러리(MediaPipe)는 연주하는 동안에도 사용 통계를 구글(`play.googleapis.com`)로 보내요.** 구글이 [MediaPipe Tasks 개인정보 고지](https://developers.google.com/edge/mediapipe/solutions/tasks#mediapipe_tasks_privacy_notice)에 밝힌 내용이에요. 그 고지가 앱을 만든 쪽더러 쓰는 사람에게 알리라고 해서 여기에 적어요.
  - 보내는 것: 어떤 기능을 어떻게 썼는지(손 인식, 영상 모드), 처리한 장 수와 걸린 시간(평균·최대), 놓친 장 수, 오류 종류, 운영체제 종류, Python·MediaPipe 버전. 구글 서버에 연결하니 이 PC의 인터넷 주소(IP)도 구글에 보여요.
  - 보내지 않는 것: 카메라 영상과 사진, 손 좌표. 구글은 입력(영상)을 서버로 보내지 않는다고 밝혔어요.
  - **끄는 설정은 없어요.** 구글이 끄는 방법을 만들지 않겠다고 답했어요([MediaPipe 이슈 #6291](https://github.com/google-ai-edge/mediapipe/issues/6291), 2026년 7월). 보내지 않게 하려면 연주하는 동안 이 PC를 인터넷에서 떼어 두거나(와이파이 끄기, 랜선 빼기), 공유기나 방화벽에서 `play.googleapis.com` 을 막아야 해요. 설치 스크립트는 Windows 설정을 바꾸지 않으니 이것도 하지 않아요.
  - 이 노트북에서 재 봤어요(2026-11-01, 카메라 대신 테스트 영상으로): 손 추적을 켜고 **약 1분 뒤에** 구글(`play.googleapis.com` 주소 `172.217.112.4`)로 연결했고, **17분 뒤에 또** 연결했어요. 켜 두는 동안 이렇게 되풀이해요.
  - 오류 기록(`%LOCALAPPDATA%\Downbeat\logs\*.err.txt`, 4절)에 1분마다 남는 `Failed to send to clearcut: FAILED_PRECONDITION: Not valid for uploading until: …` 줄도 이 통계예요. 처음 보낸 뒤 다음에 보낼 수 있는 때(켠 지 약 16분)까지 미루는 중이라는 뜻이지, 아무것도 안 보냈다는 뜻이 아니에요. 연주가 잘못된 게 아니니 이 줄은 무시해도 돼요.
- 실행 기록(`%LOCALAPPDATA%\Downbeat\logs`, 감시 프로그램으로 켰을 때와 예전 판은 압축을 푼 폴더의 `tracker\logs`)에는 카메라 확인 결과, 초당 처리 장수, 인식 속도, 오류 메시지만 남고, 영상이나 얼굴 정보는 들어가지 않아요.

자세한 기술 내용은 `tracker\README.md` 에 있어요.
