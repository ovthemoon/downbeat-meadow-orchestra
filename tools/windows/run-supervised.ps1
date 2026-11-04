<#
.SYNOPSIS
  Starts the watcher: it starts the hand tracker and the Downbeat app, starts them again when they stop, and restarts the app when it freezes or loses its sound.

.DESCRIPTION
  powershell -ExecutionPolicy Bypass -File run-supervised.ps1 [-Camera 0] [-Backend auto] [-Quality Auto|High|Low] [-PlayMode conduct|strike] [-MaxFps N] [-Mouse] [-Replay] [-Soak] [-LatencyMode] [-LogPose]
  (-ExecutionPolicy Bypass applies to this one run only; the PowerShell setting itself is not changed.)

  -Camera N    camera number (default 0).
  -Backend     camera API: auto (DirectShow), dshow or msmf. msmf can be faster: on the Windows laptop DirectShow held the Integrated Webcam at 15 fps and Media Foundation gave 30.
  -Quality     the picture's quality (--quality): Auto, High or Low, exactly so. Without it nothing is passed and the app chooses by itself (Auto: from the graphics chip, then a step down when the
               frames are slow or the tracker is starved, a step up after a long calm; [quality] lines in Player.log). High or Low locks a tier (Low: about a sixth of the grass, 0.7 of the window's size per
               side, a cheaper glow, no clouds, 30 frames a second) and turns Auto off. Lock it on the exhibition PC once Auto has shown what the PC needs.
  -Mouse       no camera, no tracker: the mouse plays (--input mouse).
  -Replay      no camera, no tracker: the recorded hands play (--input replay).
  -Soak        the overnight run: recorded hands on a loop with pauses, no sound (--soak). Needs no camera.
  -PlayMode    how a hand plays (--play-mode): conduct (the app's default) keeps the music going while a hand is raised (height = loudness, side = instrument, movement = density; it works at 5 to
               10 tracker pictures a second); strike plays only when the palm strikes downward. Without it nothing is passed and the app uses the mode of its content.
  -MaxFps N    the frames a second the app is held to (--max-fps, 10..240); Low holds itself to 30 anyway. On its own it fixes only the cap: with Auto (the default) the app still picks the tier itself; with -Quality High or Low everything is fixed. The tracker is started with a higher priority class by the watcher (no administrator rights).
  -LatencyMode the latency measurement (--latency-mode): a white square flashes at the top right of the meadow for every played note, so that a slow-motion phone video can show the moment of
               the sound and of the picture (docs/latency-method.md). The sound is not changed. It measures the time from a strike to its sound, so it also passes --play-mode strike.
  -LogPose     writes the hand pose reading to Player.log about once a second (--log-pose): one line per hand with the four finger ratios (index, middle, ring, little finger: the fingertip's distance from the wrist in wrist-to-middle-knuckle
               lengths), the pose it gives (raw = this picture, pose = the one the game takes over), whether the hand plays (canPlay) and the cell it is in. It is how pose.openRatio and pose.fistRatio are tuned on this PC: hold a relaxed
               raised hand, a fist and a half open hand up for a few seconds each (docs/conducting-play.md). Off without it; it changes nothing the app plays.

  The watcher writes logs\supervisor-yyyyMMdd.csv (every start, exit and restart with the time) next to Downbeat.exe. Ctrl+C in this window ends it and everything it started. Do NOT close the window with X: that ends the watcher but leaves the app window open
  (the next start of this script, or stop-supervised.ps1, ends such a leftover app).
  User level only: no administrator rights and no Windows setting is changed. Run setup-hand-tracker.ps1 once before the first start.
#>
param(
    [ValidateRange(0, 9)][int]$Camera = 0,
    [ValidateSet('auto', 'dshow', 'msmf')][string]$Backend = 'auto',
    [ValidateSet('Auto', 'High', 'Low')][string]$Quality,
    [ValidateSet('conduct', 'strike')][string]$PlayMode,
    [ValidateRange(10, 240)][int]$MaxFps,
    [switch]$Mouse,
    [switch]$Replay,
    [switch]$Soak,
    [switch]$LatencyMode,
    [switch]$LogPose
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_common.ps1')
. (Join-Path $PSScriptRoot '_supervisor-common.ps1')

# ValidateSet ignores case, but the app matches the tier name exactly ("low" would quietly give High).
if ($Quality) { $Quality = @{ 'auto' = 'Auto'; 'high' = 'High'; 'low' = 'Low' }[$Quality.ToLower()] }
# The latency measurement is strike to sound: it has to play by striking whatever -PlayMode says.
if ($PlayMode) { $PlayMode = $PlayMode.ToLower() }
if ($LatencyMode) { $PlayMode = 'strike' }

$script:ExitCode = 1

function Invoke-Run {
    $root = Find-BundleRoot
    $paths = Get-TrackerPaths $root
    $trackerDir = $paths.TrackerDir
    $venvPython = $paths.VenvPython    # %LOCALAPPDATA%\Downbeat\venv: the watcher runs with it and starts the tracker with it
    $supervisorDir = Join-Path $root 'supervisor'
    $config = Join-Path $supervisorDir 'configs\windows.json'

    if (-not (Test-Path -LiteralPath (Join-Path $supervisorDir 'downbeat_supervisor\__main__.py'))) {
        Write-Fail "감시 프로그램(supervisor 폴더)이 없어요. 전체 묶음 downbeat-windows 의 scripts 폴더에서 실행해 주세요."
        return
    }
    if (-not (Test-TrackerReady $paths (Join-Path $PSScriptRoot 'setup-hand-tracker.ps1'))) {
        return
    }
    $appRoot = Find-AppRoot $root
    if (-not $appRoot) {
        Write-Fail "Downbeat.exe 를 찾지 못했어요. 압축을 푼 downbeat-windows 폴더가 바탕화면(또는 이 scripts 폴더 옆)에 있는지 확인해 주세요."
        return
    }

    $inputKind = 'hand'
    if ($Mouse) { $inputKind = 'mouse' }
    if ($Replay -or $Soak) { $inputKind = 'replay' }

    # The tracker's log goes to tracker\logs\tracker-supervised.log, and a freshly unzipped bundle has no such folder (the tracker makes it too; this is the second line of defence).
    if ($inputKind -eq 'hand') {
        New-Item -ItemType Directory -Force -Path (Join-Path $trackerDir 'logs') | Out-Null
    }

    $left = Stop-OldSupervisors
    if ($left -gt 0) { Write-Line "이전에 켜 둔 감시 프로그램 ${left}개를 껐어요 (앱과 손 추적도 같이 꺼져요)." }
    $leftTrackers = Stop-OldTrackers
    if ($leftTrackers -gt 0) { Write-Line "이전에 켜 둔 손 추적 ${leftTrackers}개를 껐어요." }
    $leftPlayers = Stop-OldPlayers $appRoot
    if ($leftPlayers -gt 0) { Write-Line "이전에 켜 둔 앱 ${leftPlayers}개를 껐어요 (감시 창을 X 로 닫으면 감시 프로그램만 꺼지고 앱은 남아요)." }

    $arguments = @(
        '-m', 'downbeat_supervisor',
        '--config', $config,
        '--root', $appRoot,
        '--var', "trackerDir=${trackerDir}",
        '--var', "trackerPython=${venvPython}",
        '--var', "camera=${Camera}",
        '--var', "backend=${Backend}",
        '--var', "input=${inputKind}"
    )
    if ($Soak) { $arguments += @('--var', 'soak=1') }
    if ($LatencyMode) { $arguments += @('--var', 'latency=1') }
    if ($LogPose) { $arguments += @('--var', 'logPose=1') }
    if ($Quality) { $arguments += @('--var', "quality=${Quality}") }
    if ($PlayMode) { $arguments += @('--var', "playMode=${PlayMode}") }
    if ($MaxFps) { $arguments += @('--var', "maxFps=${MaxFps}") }

    Write-Host ""
    Write-Line "사용할 앱 폴더: ${appRoot}"
    Write-Line "입력: ${inputKind}$(if ($Soak) { ' (밤샘 모드: 소리 없음)' })"
    if ($Quality) { Write-Line "앱 화질: ${Quality}" }
    if ($MaxFps) { Write-Line "앱 프레임 상한: ${MaxFps}장/초" }
    if ($PlayMode) { Write-Line "손 연주 방식: ${PlayMode}$(if ($PlayMode -eq 'strike') { ' (손바닥을 아래로 내려칠 때만 소리가 나요)' } else { ' (손을 들고 있으면 연주해요)' })" }
    if ($LatencyMode) { Write-Line "지연 측정 모드: 음마다 화면 오른쪽 위에 하얀 네모가 반짝여요 (README-latency.txt)." }
    if ($LogPose) { Write-Line "손 모양 기록: 앱이 Player.log 에 1초마다 손마다 한 줄씩 [pose] 로 적어요 (손가락 비율, 손 모양, 연주 여부, 칸)." }
    Write-Line "기록은 $(Join-Path $appRoot 'logs') 폴더의 supervisor-날짜.csv 에 쌓여요."
    Write-Line "'Windows의 PC 보호' 창이 뜨면 추가 정보 > 실행 을 눌러요. 끝내려면 이 창에서 Ctrl+C (창을 X 로 닫으면 앱이 남아요)."

    $env:PYTHONPATH = $supervisorDir
    $env:PYTHONUTF8 = '1'
    try {
        $code = Invoke-Native $venvPython $arguments
    } finally {
        Remove-Item Env:PYTHONPATH -ErrorAction SilentlyContinue
        Remove-Item Env:PYTHONUTF8 -ErrorAction SilentlyContinue
    }
    Write-Line "감시 프로그램이 끝났어요 (종료 코드 ${code})."
    $script:ExitCode = 0
}

try {
    Invoke-Run
} catch {
    Write-Host ""
    Write-Fail $_.Exception.Message
    $script:ExitCode = 1
}
exit $script:ExitCode
