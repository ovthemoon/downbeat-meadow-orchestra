<#
.SYNOPSIS
  Installs the Downbeat hand tracker (Python + MediaPipe) for the current Windows user. Run it once.

.DESCRIPTION
  User level only: no administrator rights, no Windows setting, PATH or privacy setting is changed.
  It writes only to %LOCALAPPDATA%\Downbeat\venv (the Python environment), %LOCALAPPDATA%\Downbeat\uv, %USERPROFILE%\.cache\downbeat and,
  in the unzipped folder, the small tracker\downbeat_tracker.egg-info of the editable install. Nothing big goes into the unzipped folder, which may sit
  on a Desktop that OneDrive syncs.
  Running it again skips what is already there. One environment serves every unzipped bundle: run from another bundle, it points the editable install
  at that bundle's tracker folder (seconds) and installs the packages again only when that bundle's lock or project file differs.
  An environment of the old layout (tracker\.venv in the unzipped folder) is not used and not touched; remove-hand-tracker.ps1 offers to delete it.

  powershell -ExecutionPolicy Bypass -File setup-hand-tracker.ps1
  (-ExecutionPolicy Bypass applies to this one run only; the PowerShell setting itself is not changed.)
#>

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_common.ps1')

# --- fixed choices -----------------------------------------------------------------------------------------------------------------------
# uv is installed with Astral's official installer for exactly this version. The installer script is checked against its SHA-256 first.
$UvVersion = '0.13.6'
$UvInstallerUrl = "https://astral.sh/uv/${UvVersion}/install.ps1"
$UvInstallerSha256 = 'd00580051699bee58a2e2d9b925f45788b35f7e0fff7966fd5bedfc3c547dc7d'
$MinUsableUv = [version]'0.6.8'          # an uv that is already on this PC is used when it is at least this new (0.6.8 added venv --managed-python)
$PythonVersion = '3.12'
$SelfCheckPort = 47899                    # not 47800, so a running app never sees the self-check's test hands
$MinSelfCheckFrames = 30
$ModelBytes = 7819105
$MinFreeBytes = 2GB
$VcRedistUrl = 'https://aka.ms/vs/17/release/vc_redist.x64.exe'

# --- places ------------------------------------------------------------------------------------------------------------------------------
$root = Find-BundleRoot
$paths = Get-TrackerPaths $root
$trackerDir = $paths.TrackerDir
$venvDir = $paths.VenvDir
$venvPython = $paths.VenvPython
$stampFile = $paths.StampFile
$oldVenvDir = $paths.OldVenvDir
$lockFile = Join-Path $trackerDir 'requirements.lock.txt'
$uvHome = Join-Path $env:LOCALAPPDATA 'Downbeat\uv'    # uv, the Python it downloads and its download cache: everything uv-related lives here
$uvBinDir = Join-Path $uvHome 'bin'
$uvOwnExe = Join-Path $uvBinDir 'uv.exe'
$cacheBase = $env:DOWNBEAT_CACHE_DIR
if (-not $cacheBase) { $cacheBase = Join-Path $env:USERPROFILE '.cache\downbeat' }
$modelFile = Join-Path $cacheBase 'hand_landmarker.task'
$playScript = Join-Path $PSScriptRoot 'play-with-hands.ps1'

function Get-UvVersionOf {
    # The version of an uv.exe, or $null when it does not run or is not recognised.
    param([string]$Exe)
    $result = Get-NativeText $Exe @('--version')
    if ($result.Code -ne 0) { return $null }
    if ($result.Text -match 'uv (\d+\.\d+\.\d+)') { return [version]$Matches[1] }
    return $null
}

function Find-UsableUv {
    # An uv of ours, otherwise one the user already has. Returns its path or $null.
    $candidates = @($uvOwnExe)
    $onPath = Get-Command 'uv.exe' -ErrorAction SilentlyContinue
    if ($onPath) { $candidates += $onPath.Source }
    $candidates += (Join-Path $HOME '.local\bin\uv.exe')
    foreach ($candidate in $candidates) {
        if (-not (Test-Path -LiteralPath $candidate)) { continue }
        $version = Get-UvVersionOf $candidate
        if ($version -and $version -ge $MinUsableUv) { return $candidate }
    }
    return $null
}

function Install-Uv {
    New-Item -ItemType Directory -Force -Path $uvBinDir | Out-Null
    $installer = Join-Path $env:TEMP "downbeat-uv-installer-${UvVersion}.ps1"
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    Write-Line "설치 파일을 내려받는 중: ${UvInstallerUrl}"
    Invoke-WebRequest -UseBasicParsing -Uri $UvInstallerUrl -OutFile $installer
    try {
        $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $installer).Hash.ToLower()
        if ($hash -ne $UvInstallerSha256) {
            throw "내려받은 uv 설치 파일이 예상과 달라서 실행하지 않았어요 (SHA-256 불일치). 회사 보안 프로그램이 내용을 바꿨을 수 있어요."
        }
        Write-Line "설치 파일 검사 통과 (SHA-256 일치)."
        # UV_UNMANAGED_INSTALL: install the program into that one folder, with no PATH change and no background updater.
        $env:UV_UNMANAGED_INSTALL = $uvBinDir
        $code = Invoke-Native 'powershell.exe' @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $installer)
        if ($code -ne 0) { throw "uv 설치 프로그램이 오류로 끝났어요 (종료 코드 ${code})." }
    } finally {
        Remove-Item Env:UV_UNMANAGED_INSTALL -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $installer -Force -ErrorAction SilentlyContinue
    }
    if (-not (Test-Path -LiteralPath $uvOwnExe)) { throw "uv 설치 후에도 ${uvOwnExe} 가 없어요." }
}

function Test-VenvWorks {
    if (-not (Test-Path -LiteralPath $venvPython)) { return $false }
    $result = Get-NativeText $venvPython @('--version')
    return ($result.Code -eq 0 -and $result.Text -match '^Python 3\.12\.')
}

function Test-LinkedHere {
    # True when the editable install points at this bundle's tracker folder (the stamp's second line).
    $stamp = Read-TrackerStamp $stampFile
    return ($null -ne $stamp -and (Test-SamePath $stamp.TrackerDir $trackerDir))
}

function Test-ModelPresent {
    return ((Test-Path -LiteralPath $modelFile) -and ((Get-Item -LiteralPath $modelFile).Length -eq $ModelBytes))
}

function Test-EnoughDiskSpace {
    # Best effort: a profile on a network path or an unreadable drive must not stop the setup, so any problem counts as "enough".
    try {
        $drive = New-Object System.IO.DriveInfo (Split-Path -Qualifier $env:LOCALAPPDATA)
        return ($drive.AvailableFreeSpace -ge $MinFreeBytes)
    } catch {
        return $true
    }
}

function Show-Hints {
    param([string[]]$Lines)
    foreach ($line in $Lines) { Write-Line "- $line" }
}

function Invoke-Setup {
    Write-Host ""
    Write-Host "== 들판 오케스트라: 손 추적 프로그램 설치 ==" -ForegroundColor Cyan

    if (-not [Environment]::Is64BitOperatingSystem) { throw "64비트 Windows에서만 동작해요." }
    if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { Write-Warn "ARM 방식 PC에서는 확인하지 못했어요. 설치가 실패할 수 있어요." }

    $uvExe = Find-UsableUv
    $needUv = -not $uvExe
    $needVenv = -not (Test-VenvWorks)
    $needPackages = $needVenv -or -not (Test-TrackerPackagesFit $paths)
    $needLink = $needPackages -or -not (Test-LinkedHere)
    $needModel = -not (Test-ModelPresent)
    $oldVenvLeft = Test-Path -LiteralPath $oldVenvDir

    # --- what is about to happen, then the question ----------------------------------------------------------------------------------------
    Write-Step "이 PC에 설치할 것 (관리자 권한 필요 없음, Windows 설정은 바꾸지 않아요)"
    if ($needUv) { Write-Line "1. uv ${UvVersion} (Python 설치 도구, 약 30 MB)  ->  ${uvBinDir}" } else { Write-Line "1. uv: 이미 있어요 (${uvExe})" }
    if ($needVenv) { Write-Line "2. Python ${PythonVersion} (약 60 MB)와 가상 환경  ->  ${uvHome}\python  /  ${venvDir}" } else { Write-Line "2. Python 가상 환경: 이미 있어요 (${venvDir})" }
    if ($needPackages) {
        Write-Line "3. 손 추적 패키지 mediapipe, opencv 등 (약 270 MB, 내려받은 파일 보관 약 270 MB)  ->  ${venvDir}  /  ${uvHome}\cache"
    } elseif ($needLink) {
        Write-Line "3. 손 추적 패키지: 이미 설치돼 있어요. 이 폴더의 tracker 를 쓰도록 연결만 다시 해요 (몇 초)  ->  ${trackerDir}"
    } else {
        Write-Line "3. 손 추적 패키지: 이미 설치돼 있어요"
    }
    if ($needModel) { Write-Line "4. 손 인식 모델 hand_landmarker.task (약 8 MB, 구글 공식 주소에서 받고 SHA-256 검사)  ->  ${modelFile}" } else { Write-Line "4. 손 인식 모델: 이미 있어요 (${modelFile})" }
    Write-Line "압축을 푼 폴더에는 큰 파일을 두지 않아요 (바탕화면이 OneDrive와 동기화돼도 올라가지 않아요). 압축을 푼 폴더가 여러 개여도 환경은 하나를 같이 써요."
    if ($oldVenvLeft) { Write-Line "예전 방식으로 이 폴더 안에 만든 환경(${oldVenvDir})은 이제 쓰지 않아요. 지우려면 remove-hand-tracker.ps1 (지울지 물어봐요)." }
    Write-Line "인터넷 연결이 필요해요. 카메라 영상은 저장하지 않고, 어디로도 보내지 않아요."
    Write-Line "지우고 싶을 땐 remove-hand-tracker.ps1 을 실행하면 돼요."

    if ($needUv -or $needVenv -or $needPackages -or $needLink -or $needModel) {
        if (-not (Test-EnoughDiskSpace)) { Write-Warn "C: 같은 설치 드라이브의 빈 공간이 2 GB보다 적어요. 설치가 실패할 수 있어요." }
        Write-Host ""
        if (-not (Confirm-YesNo "위 내용을 설치할까요?")) {
            Write-Line "설치하지 않았어요. 아무것도 바뀌지 않았어요."
            $script:SetupExitCode = 0
            return
        }
    } else {
        Write-Line "필요한 것이 모두 설치돼 있어요. 점검만 다시 해요."
    }

    # --- 1. uv -----------------------------------------------------------------------------------------------------------------------------
    Write-Step "[1/6] uv"
    if ($needUv) {
        Install-Uv
        $uvExe = $uvOwnExe
        Write-Ok "uv ${UvVersion} 설치: ${uvExe}"
    } else {
        Write-Skip "uv: ${uvExe}"
    }
    # Everything uv downloads stays under ${uvHome} so that remove-hand-tracker.ps1 can take it away cleanly. These variables last for this run only.
    $env:UV_PYTHON_INSTALL_DIR = Join-Path $uvHome 'python'
    $env:UV_CACHE_DIR = Join-Path $uvHome 'cache'
    $env:UV_LINK_MODE = 'copy'

    # --- 2. virtual environment ------------------------------------------------------------------------------------------------------------
    Write-Step "[2/6] Python ${PythonVersion} 가상 환경"
    if ($needVenv) {
        if (Test-Path -LiteralPath $venvDir) {
            Write-Line "있던 가상 환경이 동작하지 않아서 새로 만들어요."
            Remove-FolderSafely $venvDir | Out-Null
        }
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $venvDir) | Out-Null
        $code = Invoke-Native $uvExe @('venv', '--python', $PythonVersion, '--managed-python', $venvDir)
        if ($code -ne 0) { throw "가상 환경을 만들지 못했어요 (uv 종료 코드 ${code}). 위쪽 메시지를 확인해 주세요. 인터넷 연결이나 회사 보안 프로그램이 원인일 수 있어요." }
        if (-not (Test-VenvWorks)) { throw "가상 환경은 만들었는데 Python이 실행되지 않아요: ${venvPython}" }
        Write-Ok "가상 환경: ${venvDir}"
    } else {
        Write-Skip "가상 환경: ${venvDir}"
    }

    # --- 3. packages -----------------------------------------------------------------------------------------------------------------------
    Write-Step "[3/6] 손 추적 패키지"
    if ($needPackages) {
        Write-Line "내려받는 중이에요. 처음에는 몇 분 걸릴 수 있어요."
        Remove-Item -LiteralPath $stampFile -Force -ErrorAction SilentlyContinue
        $code = Invoke-Native $uvExe @('pip', 'install', '--python', $venvPython, '-r', $lockFile)
        if ($code -ne 0) { throw "패키지를 설치하지 못했어요 (uv 종료 코드 ${code}). 위쪽 메시지를 확인해 주세요." }
        $code = Invoke-Native $uvExe @('pip', 'install', '--python', $venvPython, '--no-deps', '--editable', $trackerDir)
        if ($code -ne 0) { throw "tracker 프로그램을 설치하지 못했어요 (uv 종료 코드 ${code})." }
        Write-TrackerStamp $stampFile $trackerDir
        Write-Ok "패키지를 잠금 파일(requirements.lock.txt)에 적힌 버전 그대로 설치했어요."
    } elseif ($needLink) {
        # The packages fit; only the editable install still points at another unzipped bundle (or the stamp is of the old layout).
        $code = Invoke-Native $uvExe @('pip', 'install', '--python', $venvPython, '--no-deps', '--editable', $trackerDir)
        if ($code -ne 0) { throw "tracker 프로그램을 이 폴더에 연결하지 못했어요 (uv 종료 코드 ${code})." }
        Write-TrackerStamp $stampFile $trackerDir
        Write-Ok "패키지는 그대로 두고, 이 폴더의 tracker 를 쓰도록 연결했어요: ${trackerDir}"
    } else {
        Write-Skip "패키지 (requirements.lock.txt 와 같은 버전)"
    }

    # --- 4. model --------------------------------------------------------------------------------------------------------------------------
    Write-Step "[4/6] 손 인식 모델"
    Push-Location $trackerDir
    try {
        if ($needModel) { Write-Line "구글 공식 주소에서 내려받는 중이에요 (약 8 MB)." }
        $modelCode = 'from downbeat_tracker.landmarker import ensure_model; p = ensure_model(); print(p); print(p.stat().st_size)'
        $model = Get-NativeText $venvPython @('-c', $modelCode)
    } finally {
        Pop-Location
    }
    if ($model.Code -ne 0) {
        Write-Fail "손 인식 모델을 받지 못했어요."
        foreach ($line in ($model.Text -split "`r?`n" | Select-Object -Last 4)) { Write-Line $line }
        Show-Hints @(
            "회사나 학교 네트워크의 프록시, 보안 프로그램(SSL 검사)이 막고 있을 수 있어요. 집 네트워크나 휴대폰 핫스팟으로 바꿔서 다시 실행해 보세요.",
            "직접 받은 hand_landmarker.task 를 ${cacheBase} 폴더에 넣어도 돼요. 프로그램이 SHA-256 검사를 해서 맞는 파일만 써요."
        )
        throw "손 인식 모델 단계에서 멈췄어요."
    }
    Write-Ok "손 인식 모델 확인 (SHA-256 검사 통과): ${modelFile}"

    # --- 5. self check ---------------------------------------------------------------------------------------------------------------------
    Write-Step "[5/6] 카메라 없이 자체 점검 (손 사진이 움직이는 짧은 영상으로 확인)"
    $video = @((Join-Path $trackerDir 'selfcheck\hand-strikes.mp4'), (Join-Path $root 'tests\fixtures\video\hand-strikes.mp4')) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $video) { throw "자체 점검용 영상(hand-strikes.mp4)을 찾지 못했어요. tracker\selfcheck 폴더가 있는지 확인해 주세요." }
    $statsFile = Join-Path $env:TEMP 'downbeat-selfcheck-stats.json'
    $checkLog = Join-Path $env:TEMP 'downbeat-selfcheck.log'
    Remove-Item -LiteralPath $statsFile, $checkLog -Force -ErrorAction SilentlyContinue
    # The video is opened by its plain file name from inside its own folder: OpenCV can fail on a path with Korean letters in it (for example a Korean user name).
    Push-Location (Split-Path -Parent $video)
    $env:PYTHONPATH = $trackerDir
    try {
        $check = Get-NativeText $venvPython @('-m', 'downbeat_tracker', '--video', (Split-Path -Leaf $video), '--port', "$SelfCheckPort", '--max-fps', '120', '--max-seconds', '60', '--log', $checkLog, '--stats-json', $statsFile)
    } finally {
        Remove-Item Env:PYTHONPATH -ErrorAction SilentlyContinue
        Pop-Location
    }
    $stats = $null
    if (Test-Path -LiteralPath $statsFile) { $stats = Get-Content -LiteralPath $statsFile -Raw -Encoding UTF8 | ConvertFrom-Json }
    if ($check.Code -ne 0 -or -not $stats -or $stats.frames -lt $MinSelfCheckFrames -or $stats.frames_with_hands -lt 1) {
        Write-Fail "자체 점검이 통과하지 못했어요."
        foreach ($line in (($check.Text -split "`r?`n") + @(Read-LogTail $checkLog 6) | Select-Object -Last 8)) { Write-Line $line }
        if ($check.Text -match 'DLL load failed|MSVCP140|VCRUNTIME') {
            Write-Warn "Microsoft Visual C++ 런타임이 없어서 생기는 오류 같아요. 아래 주소에서 받아 설치한 뒤(관리자 승인 창이 떠요) 이 스크립트를 다시 실행해 주세요: ${VcRedistUrl}"
        }
        throw "자체 점검 단계에서 멈췄어요."
    }
    $fps = [math]::Round([double]$stats.fps, 1)
    $p50 = [math]::Round([double]$stats.infer_ms_p50, 1)
    Write-Ok "영상 $($stats.frames)장 처리, 손이 보인 장 $($stats.frames_with_hands)장, 초당 ${fps}장, 한 장당 인식 약 ${p50} ms (이 PC의 속도 참고용)"
    Remove-Item -LiteralPath $statsFile, $checkLog -Force -ErrorAction SilentlyContinue

    # --- 6. cameras ------------------------------------------------------------------------------------------------------------------------
    # The DirectShow list has the tracker's own numbers and also shows virtual cameras (a phone through Windows, OBS), which the
    # Camera/Image device classes leave out. The device classes are only the fallback when that list cannot be read.
    Write-Step "[6/6] 이 PC에서 보이는 카메라"
    $numbered = Get-DirectShowCameras
    if ($null -ne $numbered) {
        if ($numbered.Count -eq 0) {
            Write-Warn "카메라가 보이지 않아요. 웹캠이 꽂혀 있는지 확인해 주세요. (카메라 없이 마우스로 해 보려면 -Mouse)"
        }
        foreach ($camera in $numbered) {
            $kind = ''
            if ($camera.Virtual) { $kind = '  (가상 카메라: 휴대폰 연결이나 OBS 같은 프로그램이 만든 카메라예요)' }
            Write-Line "- $($camera.Index)번: $($camera.Name)${kind}"
        }
        if ($numbered.Count -gt 1) {
            Write-Line "손 추적은 0번을 써요. 다른 카메라를 쓰려면 실행할 때 -Camera 1 처럼 번호를 붙여요."
        }
    } else {
        try {
            $cameras = @(Get-PnpDevice -Class Camera, Image -PresentOnly -ErrorAction SilentlyContinue)
            if ($cameras.Count -eq 0) {
                Write-Warn "카메라가 보이지 않아요. 웹캠이 꽂혀 있는지 확인해 주세요. (카메라 없이 마우스로 해 보려면 -Mouse)"
            }
            foreach ($camera in $cameras) { Write-Line "- $($camera.FriendlyName)  (상태: $($camera.Status))" }
            if ($cameras.Count -gt 1) {
                Write-Line "카메라 번호는 보통 위에서부터 0, 1, 2 예요. 가상 카메라(OBS 등)가 있으면 번호가 밀릴 수 있어요. 안 되면 -Camera 1 처럼 바꿔 보세요."
            }
        } catch {
            Write-Warn "카메라 목록을 읽지 못했어요 ($($_.Exception.Message)). 실행할 때 확인하면 돼요."
        }
    }
    $apps = Get-CameraApps
    if ($apps.Count -gt 0) {
        Write-Warn "$($apps -join ', ') 이(가) 켜져 있어요. 켜 둔 채로는 손 추적이 카메라 화면을 받지 못해요. 손으로 연주하기 전에 닫아 주세요."
    }

    Write-Host ""
    Write-Host "설치와 점검이 끝났어요. 이제 아래 명령으로 손으로 연주해 봐요." -ForegroundColor Green
    Write-Host ""
    Write-Host "  powershell -ExecutionPolicy Bypass -File `"${playScript}`""
    Write-Host ""
    Write-Line "노트북(내장 그래픽)도 그냥 켜면 돼요: 앱이 알아서 화질을 맞춰요(-Quality 를 주지 않았을 때). 전시 PC처럼 화면이 바뀌면 안 되면 -Quality Low 나 High 로 고정해요. 카메라 화면을 보면서 확인하려면 -Preview, 카메라 번호를 바꾸려면 -Camera 1 이에요."
    if ($oldVenvLeft) {
        Write-Line "예전 위치의 환경(${oldVenvDir}, 약 270 MB)은 이제 쓰지 않아요. remove-hand-tracker.ps1 을 실행하면 맨 먼저 그것만 지울지 물어봐요."
    }
    $script:SetupExitCode = 0
}

$script:SetupExitCode = 1
try {
    Invoke-Setup
} catch {
    Write-Host ""
    Write-Fail $_.Exception.Message
    Write-Line "다시 실행하면 끝난 단계는 건너뛰고 이어서 해요."
    $script:SetupExitCode = 1
}
exit $script:SetupExitCode
