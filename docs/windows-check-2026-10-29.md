# 오늘 밤 Windows PC 점검 (10분 + 선택 7분)

목적: 들판 오케스트라를 이 PC에서 얼마나 높은 화질로 돌릴 수 있을지, 무엇을 설치해야 하는지 알아내기. 아무것도 설치하거나 설정을 바꾸지 않아요.

## 1. 정보 모으기 (복사해서 붙여 넣기)

1. 시작 메뉴에서 **PowerShell**을 열어요(관리자 권한은 필요 없어요).
2. 아래 명령을 통째로 복사해서 붙여 넣고 Enter를 눌러요.

```powershell
$out = Join-Path ([Environment]::GetFolderPath('Desktop')) "downbeat-pc-check.txt"
& {
  "== OS"; (Get-CimInstance Win32_OperatingSystem | Select-Object Caption, Version, OSArchitecture | Format-List | Out-String).Trim()
  "== CPU / RAM"; (Get-CimInstance Win32_Processor | Select-Object -ExpandProperty Name); "RAM GB: " + [math]::Round((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory / 1GB, 1)
  "== GPU"; (Get-CimInstance Win32_VideoController | Select-Object Name, DriverVersion, CurrentHorizontalResolution, CurrentVerticalResolution, CurrentRefreshRate | Format-List | Out-String).Trim()
  "== Displays"; Add-Type -AssemblyName System.Windows.Forms; [System.Windows.Forms.Screen]::AllScreens | ForEach-Object { "{0} primary={1} bounds={2}" -f $_.DeviceName, $_.Primary, $_.Bounds }
  "== Cameras"; (Get-PnpDevice -Class Camera, Image -PresentOnly -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FriendlyName)
  "== Audio out"; (Get-CimInstance Win32_SoundDevice | Select-Object -ExpandProperty Name)
  "== Python"; try { (python --version) 2>&1 } catch { "python 없음" }; try { (py -0) 2>&1 } catch { "py 런처 없음" }
  "== Disk C free GB"; [math]::Round((Get-PSDrive C).Free / 1GB, 1)
} *>&1 | Out-File -Encoding utf8 $out
"저장됨: $out"
```

3. 바탕화면에 생긴 `downbeat-pc-check.txt` 파일을 Claude에게 보내 주세요. 붙여 넣기여도 괜찮아요.

이 파일에는 PC 부품 이름과 화면 해상도만 들어가요. 개인 파일이나 계정 정보는 들어가지 않아요.

## 2. 눈으로 확인할 것

| # | 확인 | 메모 |
|---|---|---|
| 1 | 모니터 2대가 어떻게 놓여 있나요? (좌우 나란히, 위아래 등) 어느 쪽이 들판, 어느 쪽이 기록 화면이면 좋을까요? | |
| 2 | 웹캠은 어디에 달려 있나요? (모니터 위, 따로 세움 등) 앉은 자리에서 손을 들었을 때 화면에 손이 잘 보이나요? (Windows '카메라' 앱으로 확인하고, **확인이 끝나면 카메라 앱을 닫아요.** 켜 둔 채로는 손 추적이 카메라를 쓰지 못해요.) | |
| 3 | 소리는 스피커인가요, 헤드폰인가요? | |
| 4 | 이 PC를 밤새 켜 둘 때 절전으로 꺼지지 않게 바꿔도 되나요? (오늘은 바꾸지 않아요) | |
| 5 | Python이나 Unity를 설치해도 되는 PC인가요? | |

## 3. (선택, 약 7분) 박자 시계와 들판 화면 측정

두 가지를 재요. 설치는 필요 없고, 받은 실행 파일만 켜요.

- **박자 시계**: Mac에서 창을 띄워 돌리면 7번 중 3번, 앱이 약 1초 멈칫해서 박 몇 개를 놓쳤어요. Windows에서도 그런지 봐요.
- **들판 화면**: 바람에 흔들리는 풀과 꽃은 그래픽 카드에서 직접 계산해요. 이 PC에서 제대로 그려지는지, 1초에 몇 장 그리는지(fps) 봐요. 꽃 모양은 아직 다듬기 전이라 단순해 보여도 괜찮아요.

1. Mac에서 만든 `downbeat-windows-2026-10-29.zip`을 이 PC의 바탕화면에 옮겨요(USB나 클라우드 드라이브 등). 오늘 받은 zip이 여러 개라면 가장 늦게 만든 것(19:48 판)을 쓰세요. 바탕화면에 옛 판이 같이 풀려 있어도, 아래 명령이 가장 새 판을 골라요.
2. 인터넷(클라우드)으로 받았다면: zip을 오른쪽 클릭 → **속성** → 아래쪽 **차단 해제** 체크 → 확인. USB로 옮겼다면 건너뛰어요.
3. zip을 오른쪽 클릭 → **모두 압축 풀기** → 바탕화면에 풀어요.
4. PowerShell에 아래 명령을 통째로 붙여 넣고 Enter를 눌러요. 6번 실행하고 약 7분 걸려요.
   - 중간에 화면이 4번 전체 화면으로 바뀌어요(들판 화면). **끝날 때까지 마우스와 키보드를 만지지 말아 주세요.** 다른 창을 앞에 띄우면 결과가 달라져요.
   - 소리는 나지 않게 해 뒀어요.
   - "Windows의 PC 보호" 창이 뜨면 **추가 정보 → 실행**을 눌러요(서명하지 않은 개인 시제품이라서 떠요).

```powershell
& {
  $desk = [Environment]::GetFolderPath('Desktop')
  $roots = @('C:\Downbeat', $desk) | Where-Object { Test-Path -LiteralPath $_ }   # 먼저 C:\Downbeat, 없으면 바탕화면
  $exe = Get-ChildItem $roots -Recurse -Depth 3 -Filter Downbeat.exe -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
  if (-not $exe) { "C:\Downbeat 와 바탕화면에서 Downbeat.exe를 못 찾았어요. 압축을 풀었는지 확인해 주세요."; return }
  "사용할 판: $($exe.FullName) (만든 때 $($exe.LastWriteTime))"
  $root = Join-Path $exe.DirectoryName "measurements"
  $clock = [ordered]@{
    "B1-batch"      = "-batchmode"
    "F1-borderless" = "-screen-fullscreen 1"
    "F2-exclusive"  = "-screen-fullscreen 1 -window-mode exclusive"
    "W1-window"     = "-screen-fullscreen 0 -screen-width 640 -screen-height 360"
  }
  foreach ($name in $clock.Keys) {
    $dir = Join-Path $root "win-clock\$name"
    New-Item -ItemType Directory -Force $dir | Out-Null
    "실행 중: 박자 시계 $name (약 70초)"
    $argLine = "$($clock[$name]) --measure-clock --out `"$dir`" --scenario calm --duration 60 -logFile `"$dir.log`""
    Start-Process -FilePath $exe.FullName -ArgumentList $argLine -Wait
  }
  $meadow = Join-Path $root "win-meadow"
  New-Item -ItemType Directory -Force $meadow | Out-Null
  foreach ($q in "High", "Low") {
    "실행 중: 들판 화면 $q (약 40초)"
    $argLine = "-screen-fullscreen 1 --quality $q --uncapped --demo-waves --mute --log-frame-stats 5 --capture `"$meadow\meadow-$q.png`" --capture-after 8 --quit-after 35 -logFile `"$meadow\meadow-$q.log`""
    Start-Process -FilePath $exe.FullName -ArgumentList $argLine -Wait
  }
  $zip = Join-Path $desk "downbeat-win-check.zip"
  Compress-Archive -Force -Path $root -DestinationPath $zip
  "끝났어요. 바탕화면의 downbeat-win-check.zip을 보내 주세요."
}
```

5. 바탕화면에 생긴 `downbeat-win-check.zip`을 Mac으로 옮겨 주세요. 분석은 Claude가 Mac에서 해요.

이 zip에는 측정용 소리 기록, 앱 로그(그래픽 카드 이름이 들어 있어요), 앱이 그린 들판 화면 2장만 들어가요. 카메라는 켜지 않고, 바탕화면이나 다른 창은 찍지 않아요.

## 4. 오늘 밤 하지 않는 것

- 1~3절의 점검에서는 밤샘 구동을 하지 않아요. 밤샘 구동은 상태 기록 기능이 들어간 판으로 5절에서 따로 해요.
- 1~5절에서는 아무것도 설치하지 않아요. 손으로 연주해 보는 것(손 추적 앱 설치)은 따로 오는 안내서를 따르는 선택 사항이에요. 하려면 밤샘 구동(5절)을 시작하기 **전에** 해요.

## 5. (밤샘) 8시간 구동

**이 8시간은 "앱 혼자"의 안정성을 재요.** 감시 프로그램 없이 앱만 켜 두기 때문에, 앱이 한 번이라도 죽으면 아침까지 꺼져 있어요(다시 켜 주는 것이 없어서 고장이 가려지지 않아요). 서류에 "앱이 8시간 멈추지 않고 돌았다, 메모리가 늘지 않았다"고 쓰려면 이 방법으로 재요. 전시와 같은 모습(꺼지면 저절로 다시 켜짐, 손 추적과 같이)으로 하룻밤 리허설을 하고 싶다면 이 절이 아니라 `README-runbook.txt` 의 **세션 C** 를 해요. 둘 다 할 수 있으면 이 절을 먼저, 세션 C를 그다음에 해요.

잠자는 동안 8시간 켜 두고, 앱이 멈추거나 느려지거나 메모리를 계속 먹는지 봐요. 카메라도 손 추적 앱도 필요 없어요. 앱이 미리 녹화한 손 움직임을 계속 되풀이해서 연주하고, 소리는 나지 않아요(밤이니까요). 약 1분 35초마다 "손이 와서 연주하고 떠나고, 들판이 차분해지다가 조용해지는" 한 바퀴가 돌아서, 깨어남·연주·가라앉음·쉼이 밤새 300번쯤 일어나요.

- 설치하지 않고, **Windows 설정도 바꾸지 않아요.** 앱이 켜져 있는 동안만 Windows에 "잠들지 마세요, 화면도 켜 두세요"라고 부탁하고, 앱을 끄면 그 부탁도 사라져요. 전원·절전 설정은 그대로예요.
- 화면은 밤새 켜져 있어요(전체 화면). 눈부시면 모니터의 전원 버튼만 끄세요. PC는 계속 돌아요.
- 노트북이면 충전기를 꽂고 **뚜껑을 연 채로** 두세요. 뚜껑을 닫으면 앱의 부탁과 상관없이 잠들 수 있어요.
- 손 플레이(`play-with-hands`) 창과 Windows 카메라 앱은 먼저 닫아요. 밤샘 구동은 카메라를 쓰지 않아요.
- 명령에 들어 있는 `--quality Low`는 내장 그래픽 노트북용이에요(손 플레이도 Low로 해요). 그래픽 카드가 따로 있는 전시용 PC에서는 `--quality Low`를 지워서 기본값(High)으로 돌려요.
- "Windows의 PC 보호" 창이 뜨면 **추가 정보 → 실행**을 눌러요(3절과 같아요).
- 자는 동안 Windows 업데이트가 PC를 다시 켜면 앱도 꺼져요. 아침에 앱이 꺼져 있으면 그 사실을 알려 주세요. `soak-날짜` 폴더의 `heartbeat.txt`에 마지막으로 살아 있던 시각이 남아요.

1. 새로 받은 zip을 바탕화면에 풀어요(3절의 1~3번과 같아요). 옛 판이 같이 있어도 아래 명령이 가장 새 판을 골라요.
2. PowerShell에 아래 명령을 통째로 붙여 넣고 Enter를 눌러요. 앱이 전체 화면으로 열리고 PowerShell은 바로 끝나요.

```powershell
& {
  $desk = [Environment]::GetFolderPath('Desktop')
  $roots = @('C:\Downbeat', $desk) | Where-Object { Test-Path -LiteralPath $_ }   # 먼저 C:\Downbeat, 없으면 바탕화면
  $exe = Get-ChildItem $roots -Recurse -Depth 3 -Filter Downbeat.exe -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
  if (-not $exe) { "C:\Downbeat 와 바탕화면에서 Downbeat.exe를 못 찾았어요. 압축을 풀었는지 확인해 주세요."; return }
  "사용할 판: $($exe.FullName) (만든 때 $($exe.LastWriteTime))"
  $meas = Join-Path $exe.DirectoryName "measurements"
  New-Item -ItemType Directory -Force $meas | Out-Null
  Start-Process -FilePath $exe.FullName -ArgumentList "-screen-fullscreen 1 --soak --quality Low -logFile `"$meas\soak-player.log`""
  "밤샘 구동을 시작했어요: $(Get-Date)"
  "기록은 $meas 안의 soak-(날짜) 폴더에 쌓여요."
}
```

3. 잘 돌고 있는지 2분쯤 뒤에 한 번만 봐요: 앱이 있는 폴더의 `measurements\soak-날짜` 폴더에 `heartbeat.txt`와 `health-날짜.csv`가 생겼고, 이 csv에 머리글 아래로 줄이 하나 이상 있으면 돼요. (`heartbeat.txt`의 시각은 한국 시각보다 9시간 늦은 UTC로 적혀 있어요.) 그 뒤에는 만지지 말고 자요.

**아침에**

4. 앱 화면을 한 번 클릭하고 **Alt+F4**를 눌러 꺼요. 꺼질 때 마지막 기록 줄이 저절로 적혀요. (앱이 이미 꺼져 있으면 그대로 5번으로 가요.)

   앱이 밤사이 저절로 꺼졌다면 이유가 `measurements\soak-날짜\exit-reasons.txt` 에 한 줄로 남아요(이 파일이 없으면 앱이 스스로 끈 적이 없어요: Windows 업데이트 같은 다른 이유예요). `reason=graphics_device_lost`(종료 코드 71)는 앱의 로그 갈고리가 "그래픽 장치를 잃었다"는 줄을 받아서 바로 끈 거예요. `reason=main_thread_stalled`(72)는 앱이 20초 넘게 장면을 그리지 못해서 20초 감시가 끈 거예요. 파일의 줄을 알려 주세요. 그리고 `measurements\soak-player.log` 에서 `d3d11:` 로 시작하는 줄이 있으면 그 줄 끝에 `(Filename:` 로 시작하는 꼬리가 붙어 있는지도 알려 주세요.
5. PowerShell에 아래 명령을 붙여 넣고 Enter를 눌러요. 바탕화면에 `downbeat-soak.zip`이 생겨요.

```powershell
& {
  $desk = [Environment]::GetFolderPath('Desktop')
  $roots = @('C:\Downbeat', $desk) | Where-Object { Test-Path -LiteralPath $_ }   # 먼저 C:\Downbeat, 없으면 바탕화면
  $exe = Get-ChildItem $roots -Recurse -Depth 3 -Filter Downbeat.exe -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
  if (-not $exe) { "C:\Downbeat 와 바탕화면에서 Downbeat.exe를 못 찾았어요."; return }
  $items = Get-ChildItem (Join-Path $exe.DirectoryName "measurements") -Filter "soak*" -ErrorAction SilentlyContinue
  if (-not $items) { "soak 기록이 없어요. 밤샘 구동을 시작했는지 확인해 주세요."; return }
  $zip = Join-Path $desk "downbeat-soak.zip"
  Compress-Archive -Force -Path $items.FullName -DestinationPath $zip
  "끝났어요. 바탕화면의 downbeat-soak.zip을 보내 주세요."
}
```

6. 바탕화면의 `downbeat-soak.zip`을 Mac으로 옮겨 주세요. 분석은 Claude가 Mac에서 해요.

이 zip에는 1분마다 한 줄씩 쌓인 상태 기록(초당 장면 수, 가장 긴 멈춤, 메모리, 소리 예약 수, 오류 수, 들판의 상태), 0.1초 넘게 멈춘 순간의 목록, 마지막으로 살아 있던 시각, 앱 로그(그래픽 카드 이름이 들어 있어요)만 들어가요. 카메라는 켜지 않고, 바탕화면이나 다른 창은 찍지 않아요.
