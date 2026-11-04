<#
.SYNOPSIS
  Puts the records of a run into one zip on the Desktop: the app's logs, the watcher's record, the tracker's logs, the measurements and a short report of this PC.

.DESCRIPTION
  powershell -ExecutionPolicy Bypass -File collect-results.ps1
  Nothing is changed or deleted. The zip holds text and numbers: health and stall CSVs, the watcher's CSV, the tracker's log (frames per second, errors), the app's own log, env-report.json
  (Windows version, processor, memory, graphics card and its driver, screens, camera and sound device NAMES, Python, the app folder's own name, free disk) and what is in the measurements folder next to Downbeat.exe.
  No camera picture and no video exist on this PC, so none are in the zip.
  The logs are copied as they are: the app's and the tracker's logs can contain file paths, and a path under C:\Users\ shows the Windows account name.
#>
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_common.ps1')
. (Join-Path $PSScriptRoot '_supervisor-common.ps1')

function Get-Names {
    param([scriptblock]$Query)
    try { return @(& $Query | Where-Object { $_ }) } catch { return @() }
}

function New-EnvReport {
    param([string]$Root, [string]$AppRoot)
    $os = $null
    try { $os = Get-CimInstance Win32_OperatingSystem } catch { $os = $null }
    $osText = 'unknown'
    $ramGb = $null
    if ($os) {
        $osText = "$($os.Caption) $($os.Version) $($os.OSArchitecture)"
        $ramGb = [math]::Round($os.TotalVisibleMemorySize / 1MB, 1)
    }
    $report = [ordered]@{
        madeAtUtc = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
        os        = $osText
        cpu       = (Get-NamesJoined { Get-CimInstance Win32_Processor | Select-Object -ExpandProperty Name })
        ramGb     = $ramGb
        gpu       = @(Get-CimInstance Win32_VideoController -ErrorAction SilentlyContinue | ForEach-Object {
                [ordered]@{ name = $_.Name; driver = $_.DriverVersion; width = $_.CurrentHorizontalResolution; height = $_.CurrentVerticalResolution; refreshHz = $_.CurrentRefreshRate }
            })
        screens   = @(Get-Names { Add-Type -AssemblyName System.Windows.Forms; [System.Windows.Forms.Screen]::AllScreens | ForEach-Object { "$($_.DeviceName) primary=$($_.Primary) bounds=$($_.Bounds)" } })
        cameras   = @(Get-Names { Get-PnpDevice -Class Camera, Image -PresentOnly -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FriendlyName })
        audioOut  = @(Get-Names { Get-CimInstance Win32_SoundDevice | Select-Object -ExpandProperty Name })
        python    = (Get-PythonText $Root)
        appFolder = (Split-Path -Leaf $AppRoot)   # the folder's own name only: the full path would carry the Windows account name (C:\Users\<name>\...)
        diskFreeGb = (Get-FreeGb $AppRoot)
    }
    return $report
}

function Get-FreeGb {
    # Free space of the drive of a folder in GB, or $null when it cannot be told (a network path, for example).
    param([string]$Folder)
    try {
        $letter = (Split-Path -Qualifier $Folder).TrimEnd(':')
        return [math]::Round((Get-PSDrive -Name $letter -ErrorAction Stop).Free / 1GB, 1)
    } catch {
        return $null
    }
}

function Get-NamesJoined {
    param([scriptblock]$Query)
    return ((Get-Names $Query) -join '; ')
}

function Get-PythonText {
    param([string]$Root)
    $venvPython = (Get-TrackerPaths $Root).VenvPython
    if (-not (Test-Path -LiteralPath $venvPython)) { return 'tracker Python is not installed' }
    $result = Get-NativeText $venvPython @('--version')
    return $result.Text
}

function Invoke-Collect {
    $root = Find-BundleRoot
    $appRoot = Find-AppRoot $root
    if (-not $appRoot) { $appRoot = $root }

    $stage = Join-Path $env:TEMP ("downbeat-results-" + (Get-Date -Format 'yyyyMMdd-HHmmss'))
    New-Item -ItemType Directory -Force -Path $stage | Out-Null
    try {
        $report = New-EnvReport $root $appRoot
        $json = $report | ConvertTo-Json -Depth 5
        [System.IO.File]::WriteAllText((Join-Path $stage 'env-report.json'), $json, (New-Object System.Text.UTF8Encoding($false)))

        # The tracker's logs: play-with-hands.ps1 writes to %LOCALAPPDATA%\Downbeat\logs; the watcher's tracker and the old layout write to tracker\logs in the bundle.
        $trackerPaths = Get-TrackerPaths $root
        $sources = @(
            @{ From = (Join-Path $appRoot 'logs'); To = 'logs' },
            @{ From = (Join-Path $appRoot 'measurements'); To = 'measurements' },
            @{ From = $trackerPaths.OldLogDir; To = 'tracker-logs' },
            @{ From = $trackerPaths.LogDir; To = 'tracker-logs-play' }
        )
        $copied = 0
        foreach ($source in $sources) {
            if (-not (Test-Path -LiteralPath $source.From)) { continue }
            Copy-Item -LiteralPath $source.From -Destination (Join-Path $stage $source.To) -Recurse -Force
            $copied++
            Write-Line "담았어요: $($source.From)"
        }
        if ($copied -eq 0) { Write-Warn "기록 폴더(logs, measurements, tracker\logs, %LOCALAPPDATA%\Downbeat\logs)가 하나도 없었어요. env-report.json 만 담겨요." }

        $desktop = [Environment]::GetFolderPath('Desktop')
        $zip = Join-Path $desktop ("downbeat-results-" + (Get-Date -Format 'yyyyMMdd-HHmm') + '.zip')
        Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -Force
        Write-Ok "바탕화면에 만들었어요: ${zip}"
        Write-Line "이 zip을 Mac으로 옮겨 주세요. 안에는 글과 숫자만 있고 카메라 영상은 없어요. (그래픽 카드 이름, 카메라·소리 장치 이름이 들어 있어요.)"
        Write-Line "다만 앱과 손 추적의 기록에는 파일 경로가 들어 있을 수 있어서, C:\Users\(내 이름)\... 처럼 Windows 계정 이름이 보일 수 있어요. 보내기 전에 마음에 걸리면 알려 주세요."
    } finally {
        Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue
    }
}

try {
    Invoke-Collect
    exit 0
} catch {
    Write-Host ""
    Write-Fail $_.Exception.Message
    exit 1
}
