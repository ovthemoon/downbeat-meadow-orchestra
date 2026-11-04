<#
.SYNOPSIS
  Removes what setup-hand-tracker.ps1 installed: the tracker's Python environment, its logs and the hand model. Asks before every deletion.

.DESCRIPTION
  powershell -ExecutionPolicy Bypass -File remove-hand-tracker.ps1
  (-ExecutionPolicy Bypass applies to this one run only; the PowerShell setting itself is not changed.)

  Question 1 (only when there is one, asked per folder): Python environments of the old layout, tracker\.venv inside an unzipped folder.
              This bundle's scripts do not use them; an older bundle's own scripts still use its own, which is said before asking.
              This bundle's and those of the other unzipped bundles on the Desktop and in C:\Downbeat are listed (the ones OneDrive syncs are marked);
              a folder only counts when scripts\play-with-hands.ps1 sits next to its tracker folder, so a developer's repository is never listed.
  Question 2: the tracker's environment (%LOCALAPPDATA%\Downbeat\venv, shared by every unzipped bundle), its logs and the downloaded hand model.
  Question 3: uv and the Python it downloaded (%LOCALAPPDATA%\Downbeat\uv). Only the copy this project installed is touched;
              an uv that was already on this PC before is never removed.
  The unpacked downbeat-windows folder itself is not touched: delete it by hand when you are done.
#>

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_common.ps1')

$root = Find-BundleRoot
$paths = Get-TrackerPaths $root
$trackerDir = $paths.TrackerDir
$cacheBase = $env:DOWNBEAT_CACHE_DIR
if (-not $cacheBase) { $cacheBase = Join-Path $env:USERPROFILE '.cache\downbeat' }
$uvHome = Join-Path $env:LOCALAPPDATA 'Downbeat\uv'
$uvParent = Join-Path $env:LOCALAPPDATA 'Downbeat'

# Folders that belong to the tracker only: the Python environment, the log files (play's and, in the bundle, the watcher's) and the build leftover.
$trackerFolders = @(
    $paths.VenvDir,
    $paths.LogDir,
    $paths.OldLogDir,
    (Join-Path $trackerDir 'downbeat_tracker.egg-info')
)
# Only the model files themselves are deleted from the cache folder, never the folder's other content; the folder goes only when it is left empty.
$modelFiles = @(
    (Join-Path $cacheBase 'hand_landmarker.task'),
    (Join-Path $cacheBase 'hand_landmarker.part')
)

function Remove-Listed {
    # One folder that cannot be deleted (a file OneDrive or a running program holds) is named and the others are still deleted.
    param([string[]]$Paths)
    foreach ($path in $Paths) {
        try {
            if (Remove-FolderSafely $path) { Write-Ok "지웠어요: ${path}" } else { Write-Skip "없어요: ${path}" }
        } catch {
            Write-Fail "다 지우지 못했어요: ${path} ($($_.Exception.Message))"
            Write-Line "OneDrive나 다른 프로그램이 안의 파일을 쓰고 있을 수 있어요. 잠시 뒤 이 스크립트를 다시 실행하거나 탐색기에서 지워 주세요."
        }
    }
}

function Find-OldVenvs {
    # Python environments of the old layout (tracker\.venv inside an unzipped folder): this bundle's, and those of the other unzipped bundles on the
    # Desktop and in C:\Downbeat, the places the guide's commands look. Only a tracker folder with scripts\play-with-hands.ps1 beside it counts.
    $found = @()
    $places = @('C:\Downbeat', [Environment]::GetFolderPath('Desktop')) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }
    $trackers = @(Get-Item -LiteralPath $trackerDir)
    foreach ($place in $places) {
        $trackers += @(Get-ChildItem -LiteralPath $place -Directory -Recurse -Depth 2 -Filter 'tracker' -ErrorAction SilentlyContinue)
    }
    foreach ($tracker in $trackers) {
        $venv = Join-Path $tracker.FullName '.venv'
        if (-not (Test-Path -LiteralPath (Join-Path $venv 'pyvenv.cfg'))) { continue }
        if (-not (Test-Path -LiteralPath (Join-Path (Split-Path -Parent $tracker.FullName) 'scripts\play-with-hands.ps1'))) { continue }
        if (@($found | Where-Object { Test-SamePath $_ $venv }).Count -gt 0) { continue }
        $found += $venv
    }
    return $found
}

function Test-InOneDrive {
    # True for a path inside a folder that OneDrive syncs (OneDrive sets these variables for the signed-in user).
    param([string]$Path)
    $full = [IO.Path]::GetFullPath($Path)
    foreach ($base in @($env:OneDrive, $env:OneDriveConsumer, $env:OneDriveCommercial)) {
        if (-not $base) { continue }
        if ($full.StartsWith([IO.Path]::GetFullPath($base).TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { return $true }
    }
    return $false
}

function Remove-ModelFiles {
    foreach ($file in $modelFiles) {
        if (-not (Test-Path -LiteralPath $file)) { continue }
        Remove-Item -LiteralPath $file -Force
        Write-Ok "지웠어요: ${file}"
    }
    if ((Test-Path -LiteralPath $cacheBase) -and (@(Get-ChildItem -LiteralPath $cacheBase -Force).Count -eq 0)) {
        Remove-Item -LiteralPath $cacheBase -Force
        Write-Ok "빈 폴더도 지웠어요: ${cacheBase}"
    }
}

function Invoke-Remove {
    Write-Host ""
    Write-Host "== 들판 오케스트라: 손 추적 프로그램 지우기 ==" -ForegroundColor Cyan

    $stopped = Stop-OldTrackers
    if ($stopped -gt 0) { Write-Line "켜져 있던 손 추적 ${stopped}개를 먼저 껐어요." }

    Write-Step "[1/3] 예전 판이 압축을 푼 폴더 안에 만든 Python 환경 (tracker\.venv)"
    $oldVenvs = @(Find-OldVenvs)
    if ($oldVenvs.Count -eq 0) {
        Write-Skip "예전 방식의 환경이 없어요."
    } else {
        foreach ($path in $oldVenvs) {
            $where = ''
            if (Test-InOneDrive $path) { $where = '  (OneDrive가 동기화하는 곳)' }
            Write-Line "- ${path}${where}"
        }
        Write-Line "이 판의 손 추적은 이 환경들을 쓰지 않고 $($paths.VenvDir) 를 써요. 하나에 약 270 MB, 파일 약 3,500개예요."
        Write-Line "다만 예전 판 폴더의 스크립트는 아직 자기 폴더의 환경을 써요. 지운 뒤에 그 예전 판으로 손 연주를 하려면 그 폴더에서 설치를 다시 해야 해요."
        Write-Line "OneDrive 안의 것을 지우면 OneDrive가 '파일을 많이 지웠다'는 알림을 띄울 수 있어요. 이 환경의 파일이에요."
        foreach ($path in $oldVenvs) {
            if (Confirm-YesNo "지울까요? ${path}") {
                Remove-Listed @($path)
            } else {
                Write-Line "지우지 않았어요."
            }
        }
    }

    Write-Step "[2/3] 손 추적 프로그램의 환경과 기록, 손 인식 모델"
    $folders = @($trackerFolders | Where-Object { Test-Path -LiteralPath $_ })
    $models = @($modelFiles | Where-Object { Test-Path -LiteralPath $_ })
    if ($folders.Count -eq 0 -and $models.Count -eq 0) {
        Write-Skip "지울 것이 없어요."
    } else {
        foreach ($path in ($folders + $models)) { Write-Line "- ${path}" }
        if (Test-Path -LiteralPath $paths.VenvDir) {
            Write-Line "$($paths.VenvDir) 는 압축을 푼 다른 폴더에서도 같이 쓰는 환경이에요. 지우면 손으로 연주하기 전에 설치를 다시 해야 해요."
        }
        if (Confirm-YesNo "위 폴더와 파일을 지울까요?") {
            Remove-Listed $folders
            Remove-ModelFiles
        } else {
            Write-Line "지우지 않았어요."
        }
    }

    Write-Step "[3/3] uv 와 uv가 내려받은 Python"
    if (-not (Test-Path -LiteralPath $uvHome)) {
        Write-Skip "이 프로젝트가 설치한 uv가 없어요: ${uvHome}"
        Write-Line "(이미 PC에 있던 uv는 이 스크립트가 건드리지 않아요. 필요하면 직접 지워 주세요.)"
    } else {
        Write-Line "- ${uvHome}  (uv 프로그램, uv가 내려받은 Python 3.12, 내려받은 파일 보관함)"
        Write-Line "다른 프로그램에서 uv를 같이 쓰고 있다면 지우지 마세요. 이 폴더는 설치 스크립트가 만든 것이라, 보통은 지워도 괜찮아요."
        if (Confirm-YesNo "uv 와 Python 도 지울까요?") {
            Remove-Listed @($uvHome)
            if ((Test-Path -LiteralPath $uvParent) -and (@(Get-ChildItem -LiteralPath $uvParent -Force).Count -eq 0)) {
                Remove-Item -LiteralPath $uvParent -Force
            }
        } else {
            Write-Line "지우지 않았어요. 나중에 이 스크립트를 다시 실행하면 물어봐요."
        }
    }

    Write-Host ""
    Write-Host "끝났어요. Windows 설정과 PATH는 처음부터 바꾸지 않았으니 되돌릴 것이 없어요." -ForegroundColor Green
    Write-Line "압축을 푼 폴더(${root})는 직접 지우시면 돼요."
}

$script:RemoveExitCode = 1
try {
    Invoke-Remove
    $script:RemoveExitCode = 0
} catch {
    Write-Host ""
    Write-Fail $_.Exception.Message
}
exit $script:RemoveExitCode
