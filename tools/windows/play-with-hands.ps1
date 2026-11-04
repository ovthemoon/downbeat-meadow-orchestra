<#
.SYNOPSIS
  Starts the hand tracker (hidden) and the Downbeat app together, waits for the app to close, then stops the tracker.

.DESCRIPTION
  powershell -ExecutionPolicy Bypass -File play-with-hands.ps1 [-Camera 0] [-Preview] [-Mouse] [-Backend auto] [-Quality Auto|High|Low] [-PlayMode conduct|strike] [-MaxFps N] [-LatencyMode] [-LogPose] [-SaveVideo PATH [-SaveVideoPoints]] [-RecordApp DIR [-RecordSeconds 180] [-RecordSize 1280x720] [-RecordFps 30]]
  (-ExecutionPolicy Bypass applies to this one run only; the PowerShell setting itself is not changed.)

  -Camera N    camera number (default 0). Try 1 when a virtual camera such as OBS takes number 0.
  -Preview     also open a window that shows the camera picture with the hand points (nothing is saved).
  -Mouse       no camera and no tracker: play with the mouse (--input mouse).
  -Backend     camera API: auto (DirectShow), dshow or msmf. msmf can be faster: on the Windows laptop DirectShow held the Integrated
               Webcam at 15 fps (YUY2 640x480, an MJPG request was ignored) and Media Foundation gave 30, opening in 0.3 s.
  -Quality     the picture's quality (--quality). Without it nothing is passed and the app chooses by itself (Auto: it picks the tier (High or Low) at the start from the graphics chip and the first
               seconds, goes down to a cheaper tier at most once and never climbs back in that run; the picture's size goes down when the frames are slow or the tracker is starved, and up again only
               after a long calm in which somebody played; every change is a [quality] line of Player.log). High or Low locks a tier (Low: about a sixth of the grass, a picture drawn at 0.7 of the
               window's size per side, a cheaper glow, no clouds, 30 frames a second) and turns Auto off: Auto is for laptops and demos, the exhibition PC is measured once and then locked. Exactly Auto, High or Low.
               On a laptop with built-in graphics High keeps the graphics chip full, the processor slows down with it and the tracker falls to a few pictures a second.

  -PlayMode    how a hand plays (--play-mode): conduct (the app's default) keeps the music going while a hand is raised: its height is the loudness, its side picks the instrument and how much it
               moves sets how dense the notes are, so it works when the tracker only manages 5 to 10 pictures a second; strike plays only when the palm strikes downward and needs 15 or more.
  -MaxFps N    the frames a second the app is held to (--max-fps, 10..240). Low holds itself to 30 anyway; the vertical sync is switched off while a cap is set. A chip that is not
               kept busy leaves its power to the processor, and so to the tracker, on a laptop where both share one power budget. On its own it fixes only the cap: with Auto (the
               default) the app still picks the tier (High or Low) itself, judging its frames against this cap. With -Quality High or Low everything is fixed.
  -LatencyMode the latency measurement (--latency-mode): a white square flashes at the top right of the meadow for every played note, so that a slow-motion phone video can show the moment of
               the sound and of the picture (docs/latency-method.md). The sound is not changed. It measures the time from a strike to its sound, so it also passes --play-mode strike.
  -LogPose     writes the hand pose reading to Player.log about once a second (--log-pose): one line per hand with the four finger ratios (index, middle, ring, little finger: the fingertip's distance from the wrist in wrist-to-middle-knuckle
               lengths), the pose it gives (raw = this picture, pose = the one the game takes over), whether the hand plays (canPlay) and the cell it is in. It is how pose.openRatio and pose.fistRatio are tuned on this PC: hold a relaxed
               raised hand, a fist and a half open hand up for a few seconds each (docs/conducting-play.md). Off without it; it changes nothing the app plays.
  -SaveVideo PATH  for demo videos: the tracker saves what the webcam sees to this file on this PC (--save-video; .mp4, or .avi), mirrored like the app, at 30 frames a second in real time.
               When the app closes, the tracker is asked to stop (a stop file) so that it finishes the file; it is killed only if it has not ended in 8 s. Nothing is saved without it.
  -SaveVideoPoints with -SaveVideo: the 21 hand points and their bones are drawn into the video (--save-video-points).
  -RecordApp DIR  for demo videos: the app records its own picture and sound into this new folder (--record-dir): JPEG frames and the final mix (audio.wav) on one clock,
               to be joined into an MP4 afterwards (tools/assemble_run_video.py). The app ends by itself after -RecordSeconds. Closed before that with the window's close button or
               Alt+F4, the sound and the frame times up to that moment are still saved (recording.json says endedEarly). Killed (Task Manager, End task) or hung, the app saves
               nothing and only the JPEG frames remain. recording.json's videoZeroUtc is the UTC time at which second 0 of the video is on the screen: use it to line the video up with
               the -SaveVideo file's "first picture at" (to within a few tens of milliseconds). -RecordSeconds N (default 180), -RecordSize WxH (default 1280x720), -RecordFps F (default 30).
               On the Windows laptop 1280x720 at 30 held the app at about 21 frames a second and 960x540 at about 25 (docs/windows-test-2026-11-01-1833.md).
               A folder that already holds a recording is refused.

  Before the app opens, the tracker's camera check must see real pictures. A camera that another program holds (the Windows Camera
  app, OBS, a video call) still opens but sends an almost black picture once a second; the script names that and offers to check again.

  Run setup-hand-tracker.ps1 once before the first start (not needed for -Mouse). The tracker's Python environment is %LOCALAPPDATA%\Downbeat\venv,
  shared by every unzipped bundle; a bundle whose lock and project files differ from the ones it was installed for asks for setup again.
  The tracker's logs go to %LOCALAPPDATA%\Downbeat\logs (tracker-<date>-<time>.log, .out.txt, .err.txt), outside a Desktop that OneDrive syncs.
#>
param(
    [ValidateRange(0, 9)][int]$Camera = 0,
    [switch]$Preview,
    [switch]$Mouse,
    [ValidateSet('auto', 'dshow', 'msmf')][string]$Backend = 'auto',
    [ValidateSet('Auto', 'High', 'Low')][string]$Quality,
    [ValidateSet('conduct', 'strike')][string]$PlayMode,
    [ValidateRange(10, 240)][int]$MaxFps,
    [switch]$LatencyMode,
    [switch]$LogPose,
    [string]$SaveVideo,
    [switch]$SaveVideoPoints,
    [string]$RecordApp,
    [ValidateRange(10, 1800)][int]$RecordSeconds = 180,
    [ValidatePattern('^\d{3,4}x\d{3,4}$')][string]$RecordSize = '1280x720',
    [ValidateRange(5, 60)][int]$RecordFps = 30
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_common.ps1')

# ValidateSet ignores case, but the app matches the tier name exactly ("low" would quietly give High).
if ($Quality) { $Quality = @{ 'auto' = 'Auto'; 'high' = 'High'; 'low' = 'Low' }[$Quality.ToLower()] }
# The latency measurement is strike to sound: it has to play by striking whatever -PlayMode says.
if ($PlayMode) { $PlayMode = $PlayMode.ToLower() }
if ($LatencyMode) { $PlayMode = 'strike' }
# The saved video's path from where the script was started; a name without an extension becomes an .mp4.
if ($SaveVideoPoints -and -not $SaveVideo) {
    Write-Fail '-SaveVideoPoints 는 -SaveVideo 와 함께 써요 (예: -SaveVideo C:\Downbeat\demo.mp4 -SaveVideoPoints).'
    exit 1
}
if ($SaveVideo) {
    $SaveVideo = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($SaveVideo)
    if (-not [IO.Path]::GetExtension($SaveVideo)) { $SaveVideo += '.mp4' }
}
# The app's recording goes into a folder of its own; one that already holds a recording is not written over.
if ($RecordApp) {
    $RecordApp = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($RecordApp)
    if ((Test-Path -LiteralPath (Join-Path $RecordApp 'recording.json')) -or (Test-Path -LiteralPath (Join-Path $RecordApp 'frames'))) {
        Write-Fail "앱 녹화 폴더에 이미 녹화가 있어요: ${RecordApp}. 다른 폴더 이름을 주세요 (예: -RecordApp C:\Downbeat\take2)."
        exit 1
    }
}

$UdpPort = 47800                  # the port the app listens on (loopback only)
$StartCheckSeconds = 5            # a tracker that ends within this time failed to start
$CameraCheckWaitSeconds = 15      # the tracker's camera check needs about 3 s of pictures once the hand model has loaded
$LogTailLines = 15
$PollMilliseconds = 500
$script:PlayExitCode = 1

function Show-CameraHints {
    Write-Line "확인해 볼 것:"
    Write-Line "- 카메라 앱, 화상 회의, OBS 같은 다른 프로그램이 카메라를 쓰고 있지 않은지 확인해 주세요."
    Write-Line "- Windows 설정 > 개인 정보 및 보안 > 카메라 에서 '데스크톱 앱에서 카메라에 액세스하도록 허용' 이 꺼져 있을 수 있어요. (이 스크립트는 설정을 바꾸지 않아요. 직접 켜 주세요.)"
    Write-Line "- 가상 카메라(OBS 등)가 0번 자리를 차지했을 수 있어요. 맨 끝에 -Camera 1 을 붙여서 다시 해 보세요."
    Write-Line "- 그래도 안 되면 -Backend msmf 를 붙여 보세요. 카메라 없이 해 보려면 -Mouse 예요."
}

function Show-TrackerFiles {
    param([string]$LogFile, [string]$OutFile, [string]$ErrFile)
    foreach ($file in @($LogFile, $ErrFile, $OutFile)) {
        $lines = @(Read-LogTail $file $LogTailLines)
        if ($lines.Count -eq 0) { continue }
        Write-Line "--- $(Split-Path -Leaf $file) 마지막 줄 ---"
        foreach ($line in $lines) { Write-Line $line }
    }
}

function Test-LogHas {
    param([string]$LogFile, [string]$Pattern)
    if (-not (Test-Path -LiteralPath $LogFile)) { return $false }
    return [bool](Select-String -LiteralPath $LogFile -Pattern $Pattern -SimpleMatch -Quiet -ErrorAction SilentlyContinue)
}

function Get-LastStatsLine {
    param([string]$LogFile)
    $lines = @(Read-LogTail $LogFile 60)
    $stats = @($lines | Where-Object { $_ -match 'fps=' })
    if ($stats.Count -eq 0) { return $null }
    return $stats[$stats.Count - 1]
}

function Get-CameraCheck {
    # The tracker's "camera check:" line (tracker\downbeat_tracker\camera_check.py) as Status (ok, slow, dark, none), Frames, ReadMs and Brightness; $null while there is none.
    param([string]$LogFile)
    if (-not (Test-Path -LiteralPath $LogFile)) { return $null }
    $found = Select-String -LiteralPath $LogFile -Pattern 'camera check: (\w+) frames=(\d+) read_ms=(\d+) brightness=(\d+)' -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $found) { return $null }
    $groups = $found.Matches[0].Groups
    return [pscustomobject]@{ Status = $groups[1].Value; Frames = [int]$groups[2].Value; ReadMs = [int]$groups[3].Value; Brightness = [int]$groups[4].Value }
}

function Wait-CameraCheck {
    # Waits for the camera check line. Returns it, or $null when the tracker ended or the line did not come in time.
    param($Process, [string]$LogFile)
    $deadline = (Get-Date).AddSeconds($CameraCheckWaitSeconds)
    while ((Get-Date) -lt $deadline) {
        $check = Get-CameraCheck $LogFile
        if ($check) { return $check }
        if ($Process.HasExited) { return $null }
        Start-Sleep -Milliseconds 250
    }
    return $null
}

function Show-CameraCheckProblem {
    param($Check)
    if ($Check.Status -eq 'slow') {
        Write-Warn "카메라가 화면을 $([math]::Round($Check.ReadMs / 1000.0, 1))초에 한 장씩만 보내요 (밝기 $($Check.Brightness) / 255). 다른 프로그램이 카메라를 잡고 있을 때 이렇게 돼요."
    } elseif ($Check.Status -eq 'dark') {
        Write-Warn "카메라 화면이 거의 까매요 (밝기 $($Check.Brightness) / 255). 카메라 덮개가 닫혀 있거나 방이 어두울 수 있어요."
    } else {
        Write-Warn "카메라는 열렸는데 화면이 한 장도 오지 않아요. 다른 프로그램이 카메라를 쓰고 있을 수 있어요."
    }
    $apps = Get-CameraApps
    if ($apps.Count -gt 0) {
        Write-Line "지금 켜져 있는 프로그램: $($apps -join ', '). 이 창을 닫아 주세요. 다른 창 뒤에 가려져 있어도 카메라를 쓰고 있을 수 있어요."
    }
    Show-CameraHints
}

function Show-CameraChoice {
    # Names the camera the tracker is about to open (DirectShow numbers, the same as the tracker's with -Backend auto or dshow).
    if ($Backend -eq 'msmf') { return }
    $cameras = Get-DirectShowCameras
    if ($null -eq $cameras) { return }
    if ($cameras.Count -eq 0) {
        Write-Warn "이 PC에서 카메라가 보이지 않아요. 웹캠이 꽂혀 있는지 확인해 주세요. (카메라 없이 하려면 -Mouse)"
        return
    }
    $chosen = @($cameras | Where-Object { $_.Index -eq $Camera })
    if ($chosen.Count -eq 0) {
        Write-Warn "카메라 ${Camera}번이 없어요. 이 PC의 카메라: $(($cameras | ForEach-Object { "$($_.Index)번 $($_.Name)" }) -join ', ')"
        return
    }
    $kind = ''
    if ($chosen[0].Virtual) { $kind = ' (가상 카메라예요. 노트북이나 USB 웹캠을 쓰려면 -Camera 로 번호를 바꿔요)' }
    Write-Line "카메라 ${Camera}번: $($chosen[0].Name)${kind}"
}

function Test-LogHasHands {
    param([string]$LogFile)
    if (-not (Test-Path -LiteralPath $LogFile)) { return $false }
    return [bool](Select-String -LiteralPath $LogFile -Pattern 'hands=[1-9]' -Quiet -ErrorAction SilentlyContinue)
}

function Start-Tracker {
    # Starts the tracker without a window of its own and returns the process. Its log goes to %LOCALAPPDATA%\Downbeat\logs.
    # The working folder is the bundle's tracker folder, so the tracker's code is this bundle's whatever bundle the shared environment was installed from.
    # -NoNewWindow (not -WindowStyle Hidden): a hidden start would also hide the --preview picture, and the tracker now ends together with this console window.
    param([string]$PythonExe, [string]$TrackerDir, [string]$LogFile, [string]$OutFile, [string]$ErrFile, [string]$StopFile)
    $arguments = "-m downbeat_tracker --camera ${Camera} --backend ${Backend} --port ${UdpPort} --log $(Quote-Arg $LogFile)"
    if ($Preview) { $arguments += ' --preview' }
    if ($SaveVideo) {
        $arguments += " --save-video $(Quote-Arg $SaveVideo) --stop-file $(Quote-Arg $StopFile)"
        if ($SaveVideoPoints) { $arguments += ' --save-video-points' }
    }
    $env:PYTHONUTF8 = '1'
    if ($Backend -eq 'msmf') { $env:OPENCV_VIDEOIO_MSMF_ENABLE_HW_TRANSFORMS = '0' }   # the known workaround for slow camera start with Media Foundation
    try {
        $process = Start-Process -FilePath $PythonExe -ArgumentList $arguments -WorkingDirectory $TrackerDir -NoNewWindow -RedirectStandardOutput $OutFile -RedirectStandardError $ErrFile -PassThru
    } finally {
        Remove-Item Env:PYTHONUTF8 -ErrorAction SilentlyContinue
        Remove-Item Env:OPENCV_VIDEOIO_MSMF_ENABLE_HW_TRANSFORMS -ErrorAction SilentlyContinue
    }
    $null = $process.Handle    # keeps the exit code readable on Windows PowerShell 5.1
    return $process
}

function Set-TrackerPriority {
    # The tracker gets a higher priority class than the app (no administrator rights needed): on a laptop whose graphics chip and processor share one power budget a busy app starves it.
    # It can fail (a process that is already gone, a policy), and then the tracker simply runs with the normal priority.
    param($Process)
    try {
        $Process.PriorityClass = [System.Diagnostics.ProcessPriorityClass]::AboveNormal
    } catch {
        Write-Warn "손 추적의 우선순위를 올리지 못했어요. 그대로 써도 돼요: $($_.Exception.Message)"
    }
}

function Wait-TrackerStart {
    # Returns $true when the tracker is still running after the start check, $false when it ended early.
    param($Process)
    $deadline = (Get-Date).AddSeconds($StartCheckSeconds)
    while ((Get-Date) -lt $deadline) {
        if ($Process.HasExited) { return $false }
        Start-Sleep -Milliseconds 250
    }
    return (-not $Process.HasExited)
}

function Invoke-Play {
    $root = Find-BundleRoot
    $paths = Get-TrackerPaths $root
    $trackerDir = $paths.TrackerDir
    $venvPython = $paths.VenvPython

    $player = Find-Player $root
    if (-not $player) {
        Write-Fail "Downbeat.exe 를 찾지 못했어요. 압축을 푼 downbeat-windows 폴더가 바탕화면(또는 이 scripts 폴더 옆)에 있는지 확인해 주세요."
        return
    }
    Write-Host ""
    Write-Line "사용할 판: $($player.FullName) (만든 때 $($player.LastWriteTime))"

    $tracker = $null
    $logFile = $null
    try {
        if ($Mouse) {
            Write-Line "마우스로 해요. 손 추적은 켜지 않아요."
            if ($SaveVideo) { Write-Warn "-Mouse 에서는 손 추적(웹캠)을 켜지 않아서 웹캠 영상도 저장하지 않아요." }
            $appArguments = '--input mouse'
        } else {
            if (-not (Test-TrackerReady $paths (Join-Path $PSScriptRoot 'setup-hand-tracker.ps1'))) {
                Write-Line "(카메라 없이 마우스로만 해 보려면 -Mouse)"
                return
            }
            $left = Stop-OldTrackers
            if ($left -gt 0) { Write-Line "이전에 켜 둔 손 추적 ${left}개를 껐어요." }

            $logDir = $paths.LogDir
            New-Item -ItemType Directory -Force -Path $logDir | Out-Null
            $stopFile = Join-Path $logDir 'tracker.stop'
            Show-CameraChoice
            if ($SaveVideo) {
                Write-Line "웹캠 화면을 영상으로 저장해요$(if ($SaveVideoPoints) { ' (손 점과 뼈대를 그려서)' }): ${SaveVideo}"
                Write-Line "영상에는 얼굴이 나와요. 이 PC에만 저장되고 어디로도 보내지 않아요. 앱을 끄면 파일이 마무리돼요."
            }

            # A camera that another program holds still opens, so the tracker's camera check decides; the user can close that program and check again.
            $attempt = 0
            while ($true) {
                $attempt++
                $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
                if ($attempt -gt 1) { $stamp = "${stamp}-${attempt}" }
                $logFile = Join-Path $logDir "tracker-${stamp}.log"
                $outFile = Join-Path $logDir "tracker-${stamp}.out.txt"
                $errFile = Join-Path $logDir "tracker-${stamp}.err.txt"

                Write-Line "손 추적을 켜요 (카메라 ${Camera}번, 창 없이 뒤에서 실행). 기록: ${logFile}"
                $tracker = Start-Tracker $venvPython $trackerDir $logFile $outFile $errFile $stopFile
                Set-TrackerPriority $tracker
                if (-not (Wait-TrackerStart $tracker)) {
                    Write-Fail "손 추적이 시작하자마자 꺼졌어요 (${StartCheckSeconds}초 안에)."
                    Show-TrackerFiles $logFile $outFile $errFile
                    Show-CameraHints
                    return
                }
                if (Test-LogHas $logFile 'could not be opened') {
                    Write-Warn "카메라를 열지 못한 것 같아요. 손 추적은 1초마다 다시 시도하고 있어요."
                    Show-CameraHints
                    break
                }
                Write-Line "카메라 화면이 잘 들어오는지 확인하는 중이에요 (몇 초 걸려요)."
                $check = Wait-CameraCheck $tracker $logFile
                if ($check -and $check.Status -eq 'ok') {
                    Write-Ok "손 추적이 켜졌어요. 카메라 화면이 잘 들어와요 (밝기 $($check.Brightness) / 255)."
                    break
                }
                if ($tracker.HasExited) {
                    Write-Fail "손 추적이 카메라를 확인하다가 꺼졌어요."
                    Show-TrackerFiles $logFile $outFile $errFile
                    Show-CameraHints
                    return
                }
                if (-not $check) {
                    Write-Warn "카메라 확인 결과가 ${CameraCheckWaitSeconds}초 안에 나오지 않았어요. 그대로 앱을 켜요."
                    break
                }
                Show-CameraCheckProblem $check
                Write-Host ""
                if (-not (Confirm-YesNo "그 프로그램을 닫았으면 Y (다시 확인해요). N 이면 이대로 앱을 켜요")) { break }
                Stop-ProcessTree -ProcessId $tracker.Id
                $tracker = $null
            }
            $appArguments = '--input hand'
        }

        if ($Quality) {
            $appArguments += " --quality ${Quality}"
            Write-Line "앱 화질: ${Quality}"
        }
        if ($MaxFps) {
            $appArguments += " --max-fps ${MaxFps}"
            Write-Line "앱 프레임 상한: ${MaxFps}장/초"
        }
        if ($PlayMode) {
            $appArguments += " --play-mode ${PlayMode}"
            Write-Line "손 연주 방식: ${PlayMode}$(if ($PlayMode -eq 'strike') { ' (손바닥을 아래로 내려칠 때만 소리가 나요)' } else { ' (손을 들고 있으면 연주해요)' })"
        }
        if ($LatencyMode) {
            $appArguments += ' --latency-mode'
            Write-Line "지연 측정 모드: 음마다 화면 오른쪽 위에 하얀 네모가 반짝여요 (README-latency.txt)."
        }
        if ($LogPose) {
            $appArguments += ' --log-pose'
            Write-Line "손 모양 기록: 앱이 Player.log 에 1초마다 손마다 한 줄씩 [pose] 로 적어요 (손가락 비율, 손 모양, 연주 여부, 칸)."
        }
        if ($RecordApp) {
            $appArguments += " --record-dir $(Quote-Arg $RecordApp) --record-seconds ${RecordSeconds} --record-size ${RecordSize} --record-fps ${RecordFps}"
            Write-Line "앱 화면과 소리를 ${RecordSeconds}초 동안 녹화해요 (${RecordSize}, 1초에 ${RecordFps}장): ${RecordApp}"
            Write-Line "녹화 시간이 지나면 앱이 스스로 꺼져요. 그 전에 창의 닫기 버튼이나 Alt+F4 로 꺼도 그때까지의 소리와 그림은 저장돼요."
            Write-Warn "작업 관리자로 강제 종료하거나 앱이 멈춰서 끄면 소리가 저장되지 않아요."
        }
        Write-Line "앱을 켜요. 'Windows의 PC 보호' 창이 뜨면 추가 정보 > 실행 을 눌러요. 앱을 끄면 손 추적도 같이 꺼져요."
        $app = Start-Process -FilePath $player.FullName -ArgumentList $appArguments -WorkingDirectory $player.DirectoryName -PassThru
        $null = $app.Handle

        $trackerDiedEarly = $false
        while (-not $app.HasExited) {
            Start-Sleep -Milliseconds $PollMilliseconds
            if ($tracker -and -not $trackerDiedEarly -and $tracker.HasExited) {
                $trackerDiedEarly = $true
                Write-Warn "앱이 도는 중에 손 추적이 멈췄어요. 앱을 끄고 아래 기록을 확인해 주세요."
            }
        }
        Write-Line "앱이 끝났어요 (종료 코드 $($app.ExitCode))."
        if ($RecordApp) {
            $recordingFile = Join-Path $RecordApp 'recording.json'
            if (Test-Path -LiteralPath $recordingFile) {
                $endedEarly = $false
                $recordedSeconds = 0
                try {
                    $recording = Get-Content -LiteralPath $recordingFile -Raw -Encoding UTF8 | ConvertFrom-Json
                    $endedEarly = [bool]$recording.endedEarly
                    $recordedSeconds = [math]::Round([double]$recording.recordedSeconds, 1)
                } catch {
                    Write-Warn "recording.json 을 읽지 못했어요: $($_.Exception.Message)"
                }
                if ($endedEarly) {
                    Write-Warn "앱을 녹화 시간(${RecordSeconds}초)보다 일찍 꺼서 녹화가 ${recordedSeconds}초로 짧아요. 그때까지의 소리와 그림은 저장했어요: ${RecordApp}"
                } else {
                    Write-Ok "앱 화면과 소리를 저장했어요: ${RecordApp}"
                }
            } else {
                Write-Warn "앱 녹화가 마무리되지 않았어요(앱이 저장하기 전에 멈췄어요. 예: 강제 종료). 그림만 남고 소리는 없어요: ${RecordApp}"
            }
        }
        $script:PlayExitCode = 0

        if ($tracker) {
            if ($trackerDiedEarly) { Show-TrackerFiles $logFile $outFile $errFile }
            $last = Get-LastStatsLine $logFile
            if ($last) {
                Write-Line "손 추적 마지막 상태: $last"
                if (-not (Test-LogHasHands $logFile)) {
                    Write-Warn "앱이 켜져 있는 동안 손이 한 번도 잡히지 않았어요."
                    Write-Line "- -Preview 를 붙여서 카메라 화면에 손이 보이는지 확인해 보세요. 손 전체가 화면 안에 들어오고 방이 밝을 때 잘 잡혀요."
                }
            } else {
                Write-Warn "손 추적이 카메라 화면을 한 장도 받지 못했어요."
                Show-CameraHints
            }
        }
    } finally {
        if ($tracker) {
            if ($SaveVideo -and -not $tracker.HasExited) {
                # killed, the tracker would leave a video file that does not play: ask it to stop, so that it finishes the file, and kill it only if it does not
                New-Item -ItemType File -Force -Path $stopFile | Out-Null
                if (-not $tracker.WaitForExit(8000)) { Write-Warn "손 추적이 8초 안에 스스로 끝나지 않아서 강제로 껐어요. 영상 파일이 재생되지 않을 수 있어요." }
            }
            Stop-ProcessTree -ProcessId $tracker.Id
            if ($SaveVideo) {
                Remove-Item -LiteralPath $stopFile -Force -ErrorAction SilentlyContinue
                if (Test-Path -LiteralPath $SaveVideo) {
                    Write-Ok ("웹캠 영상을 저장했어요: ${SaveVideo} ({0:N1} MB)" -f ((Get-Item -LiteralPath $SaveVideo).Length / 1MB))
                } else {
                    Write-Warn "웹캠 영상이 만들어지지 않았어요. 손 추적 기록을 확인해 주세요: ${logFile}"
                }
            }
            Write-Line "손 추적을 껐어요. 기록 폴더: $(Split-Path -Parent $logFile)"
        }
    }
}

try {
    Invoke-Play
} catch {
    Write-Host ""
    Write-Fail $_.Exception.Message
    $script:PlayExitCode = 1
}
exit $script:PlayExitCode
