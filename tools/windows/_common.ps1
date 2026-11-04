# Shared helpers of the Downbeat Windows scripts. The other scripts load this file; it is not run by itself.
# Written for stock Windows PowerShell 5.1: no PowerShell 7 syntax (no && or ||, no ternary, no ??).
# Reminder for Korean text: in a double-quoted string write ${name} when Korean letters follow a variable, because Korean letters are valid in variable names.

function Write-Step {
    param([string]$Text)
    Write-Host ""
    Write-Host $Text -ForegroundColor Cyan
}

function Write-Line {
    param([string]$Text)
    Write-Host "  $Text"
}

function Write-Ok {
    param([string]$Text)
    Write-Host "  [완료] $Text" -ForegroundColor Green
}

function Write-Skip {
    param([string]$Text)
    Write-Host "  [이미 있음, 건너뜀] $Text" -ForegroundColor DarkGray
}

function Write-Warn {
    param([string]$Text)
    Write-Host "  [주의] $Text" -ForegroundColor Yellow
}

function Write-Fail {
    param([string]$Text)
    Write-Host "  [실패] $Text" -ForegroundColor Red
}

function Quote-Arg {
    # One command-line argument for Start-Process -ArgumentList, which does not add quotes by itself (paths may contain spaces or Korean).
    param([string]$Text)
    return '"' + $Text + '"'
}

function Find-BundleRoot {
    # The folder that holds tracker\pyproject.toml, found one to three levels above this scripts folder:
    # the bundle is <root>\scripts\, the repository is <root>\tools\windows\.
    $dir = $PSScriptRoot
    for ($level = 0; $level -lt 3; $level++) {
        $dir = Split-Path -Parent $dir
        if (-not $dir) { break }
        if (Test-Path -LiteralPath (Join-Path $dir 'tracker\pyproject.toml')) { return $dir }
    }
    throw "tracker 폴더를 찾지 못했어요. scripts 폴더가 tracker 폴더 옆에 그대로 있는지 확인해 주세요."
}

function Get-TrackerPaths {
    # Where the tracker's pieces are for the bundle folder $Root.
    # The Python environment and the logs live in %LOCALAPPDATA%\Downbeat, not in the unzipped folder: a Desktop that OneDrive syncs would upload the
    # environment's 3,500 files (272 MB), could make them online-only and could lock them while uv writes them. %LOCALAPPDATA% is never synced.
    # One environment serves every unzipped bundle (a dated release beside the older one, downbeat-hand-tracker beside downbeat-windows): its stamp
    # names the lock and project files it was installed for, and a bundle with the same two files uses it as it is (Test-TrackerPackagesFit).
    # OldVenvDir and OldLogDir are where the scripts put them before; nothing uses them any more, remove-hand-tracker.ps1 offers to delete them.
    param([string]$Root)
    $downbeatDir = Join-Path $env:LOCALAPPDATA 'Downbeat'
    $trackerDir = Join-Path $Root 'tracker'
    $venvDir = Join-Path $downbeatDir 'venv'
    return [pscustomobject]@{
        TrackerDir = $trackerDir
        VenvDir    = $venvDir
        VenvPython = (Join-Path $venvDir 'Scripts\python.exe')
        StampFile  = (Join-Path $venvDir 'downbeat-setup.stamp')
        LogDir     = (Join-Path $downbeatDir 'logs')
        OldVenvDir = (Join-Path $trackerDir '.venv')
        OldLogDir  = (Join-Path $trackerDir 'logs')
    }
}

function Get-PackagesStamp {
    # The first line of the stamp for the packages of this bundle: the hashes of its lock file and project file.
    param([string]$TrackerDir)
    $lockHash = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $TrackerDir 'requirements.lock.txt')).Hash
    $projectHash = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $TrackerDir 'pyproject.toml')).Hash
    return "lock=${lockHash} pyproject=${projectHash}"
}

function Read-TrackerStamp {
    # The stamp setup-hand-tracker.ps1 writes when the packages are in: Packages (the hash line) and TrackerDir (the bundle's tracker folder the
    # editable install points at; empty in a stamp of the old layout). $null when there is no stamp: nothing installed yet, or an install that did not finish.
    param([string]$StampFile)
    if (-not (Test-Path -LiteralPath $StampFile)) { return $null }
    $lines = @([IO.File]::ReadAllLines($StampFile, [Text.Encoding]::UTF8) | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    if ($lines.Count -eq 0) { return $null }
    $linked = ''
    foreach ($line in $lines) {
        if ($line.StartsWith('tracker=')) { $linked = $line.Substring(8) }
    }
    return [pscustomobject]@{ Packages = $lines[0]; TrackerDir = $linked }
}

function Write-TrackerStamp {
    # UTF-8 without BOM: the tracker folder's path can have Korean letters in it (바탕 화면).
    param([string]$StampFile, [string]$TrackerDir)
    $text = (Get-PackagesStamp $TrackerDir) + "`r`n" + "tracker=$([IO.Path]::GetFullPath($TrackerDir).TrimEnd('\'))" + "`r`n"
    [IO.File]::WriteAllText($StampFile, $text, (New-Object System.Text.UTF8Encoding($false)))
}

function Test-SamePath {
    param([string]$First, [string]$Second)
    if (-not $First -or -not $Second) { return $false }
    return ([IO.Path]::GetFullPath($First).TrimEnd('\') -eq [IO.Path]::GetFullPath($Second).TrimEnd('\'))
}

function Test-TrackerPackagesFit {
    # True when the installed packages are the ones this bundle's lock and project files ask for. The tracker's own code always comes from the
    # bundle that starts it (it runs with the bundle's tracker folder as its working folder), so a bundle with the same two files needs no setup of its own.
    param($Paths)
    $stamp = Read-TrackerStamp $Paths.StampFile
    return ($null -ne $stamp -and $stamp.Packages -eq (Get-PackagesStamp $Paths.TrackerDir))
}

function Test-TrackerReady {
    # Before a start: $true when the environment is there and fits this bundle; otherwise says why and the command that fixes it, and gives $false.
    param($Paths, [string]$SetupScript)
    if (-not (Test-Path -LiteralPath $Paths.VenvPython)) {
        if (Test-Path -LiteralPath (Join-Path $Paths.OldVenvDir 'Scripts\python.exe')) {
            Write-Fail "손 추적 프로그램을 두는 곳이 바뀌었어요 (압축을 푼 폴더 안 -> $($Paths.VenvDir), OneDrive가 동기화하지 않는 곳). 설치를 한 번 더 해 주세요:"
        } else {
            Write-Fail "손 추적 프로그램이 아직 설치되지 않았어요. 먼저 한 번 설치해 주세요:"
        }
    } elseif (-not (Test-TrackerPackagesFit $Paths)) {
        Write-Fail "설치된 손 추적 패키지가 이 폴더의 손 추적과 맞지 않아요 (다른 판으로 설치했거나 설치가 끝나지 않았어요). 이 폴더에서 설치를 한 번 더 해 주세요:"
    } else {
        return $true
    }
    Write-Line "powershell -ExecutionPolicy Bypass -File `"${SetupScript}`""
    return $false
}

function Find-Player {
    # The newest Downbeat.exe: first near the scripts (the bundle folder), otherwise on the Desktop. Returns a FileInfo or nothing.
    param([string]$Root)
    $near = @(Get-ChildItem -LiteralPath $Root -Recurse -Depth 2 -Filter 'Downbeat.exe' -ErrorAction SilentlyContinue)
    if ($near.Count -eq 0) {
        $desktop = [Environment]::GetFolderPath('Desktop')
        if ($desktop) {
            $near = @(Get-ChildItem -LiteralPath $desktop -Recurse -Depth 3 -Filter 'Downbeat.exe' -ErrorAction SilentlyContinue)
        }
    }
    if ($near.Count -eq 0) { return $null }
    return ($near | Sort-Object LastWriteTime -Descending | Select-Object -First 1)
}

function Invoke-Native {
    # Runs a program, shows its output as it comes, and returns its exit code.
    # Windows PowerShell 5.1 turns text on a native program's error stream into errors when ErrorActionPreference is Stop, so it is relaxed here.
    param([string]$Exe, [string[]]$Arguments)
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & $Exe @Arguments | Out-Host
        return $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $previous
    }
}

function Get-NativeText {
    # Runs a program quietly. Returns an object with Code (exit code, -1 when it could not start) and Text (output and error text together).
    param([string]$Exe, [string[]]$Arguments)
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $lines = & $Exe @Arguments 2>&1 | ForEach-Object { "$_" }
        $code = $LASTEXITCODE
        return [pscustomobject]@{ Code = $code; Text = (($lines | Out-String).Trim()) }
    } catch {
        return [pscustomobject]@{ Code = -1; Text = $_.Exception.Message }
    } finally {
        $ErrorActionPreference = $previous
    }
}

function Stop-ProcessTree {
    # Ends a process and the processes it started. The Python of a virtual environment is started by a small launcher, so killing the launcher alone is not enough.
    param([int]$ProcessId)
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & taskkill.exe /PID $ProcessId /T /F 2>&1 | Out-Null
    } catch {
        # best effort: the process may already be gone, and the caller checks nothing more
        $null = $_
    } finally {
        $ErrorActionPreference = $previous
    }
}

function Stop-OldTrackers {
    # Ends tracker processes that an earlier run left behind (for example when its PowerShell window was closed). They would keep the camera busy.
    # Returns how many were found.
    $found = @(Get-CimInstance Win32_Process -Filter "Name = 'python.exe'" -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -and ($_.CommandLine -like '*downbeat_tracker*') })
    foreach ($process in $found) {
        Stop-ProcessTree -ProcessId ([int]$process.ProcessId)
    }
    return $found.Count
}

$DirectShowListSource = @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace Downbeat
{
    [ComImport, Guid("29840822-5B84-11D0-BD3B-00A0C911CE86"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ICreateDevEnum
    {
        [PreserveSig] int CreateClassEnumerator([In] ref Guid category, out IEnumMoniker monikers, int flags);
    }

    [ComImport, Guid("55272A00-42CB-11CE-8135-00AA004BB851"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyBag
    {
        [PreserveSig] int Read([In, MarshalAs(UnmanagedType.LPWStr)] string name, [Out, MarshalAs(UnmanagedType.Struct)] out object value, [In] IntPtr errorLog);
        [PreserveSig] int Write([In, MarshalAs(UnmanagedType.LPWStr)] string name, [In, MarshalAs(UnmanagedType.Struct)] ref object value);
    }

    public static class DirectShowCameras
    {
        // "name<TAB>device path" per video input, in DirectShow order. Only reads the device list; no camera is opened.
        public static List<string> List()
        {
            var result = new List<string>();
            var devEnum = (ICreateDevEnum)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("62BE5D10-60EB-11d0-BD3B-00A0C911CE86")));
            var category = new Guid("860BB310-5D01-11d0-BD3B-00A0C911CE86");
            IEnumMoniker monikers;
            if (devEnum.CreateClassEnumerator(ref category, out monikers, 0) != 0 || monikers == null) return result;
            var one = new IMoniker[1];
            while (monikers.Next(1, one, IntPtr.Zero) == 0)
            {
                object bagObject;
                var bagId = typeof(IPropertyBag).GUID;
                one[0].BindToStorage(null, null, ref bagId, out bagObject);
                var bag = (IPropertyBag)bagObject;
                object name, path;
                if (bag.Read("FriendlyName", out name, IntPtr.Zero) != 0) name = "?";
                if (bag.Read("DevicePath", out path, IntPtr.Zero) != 0) path = "";
                result.Add(name + "\t" + path);
                Marshal.ReleaseComObject(bagObject);
                Marshal.ReleaseComObject(one[0]);
            }
            return result;
        }
    }
}
'@

function Get-DirectShowCameras {
    # The cameras in DirectShow order, which is the order of the tracker's camera numbers (--backend auto and dshow on Windows).
    # Each has Index, Name and Virtual (a camera made by a program: a phone through Windows, OBS, ...). Returns $null when the list cannot be read.
    try {
        if (-not ('Downbeat.DirectShowCameras' -as [type])) { Add-Type -TypeDefinition $DirectShowListSource -ErrorAction Stop }
        $index = 0
        $cameras = @()
        foreach ($entry in [Downbeat.DirectShowCameras]::List()) {
            $name, $path = $entry -split "`t", 2
            $cameras += [pscustomobject]@{ Index = $index; Name = $name; Virtual = ((-not $path) -or ($path -like '\\?\swd#vcamdevapi#*')) }
            $index++
        }
        return , $cameras
    } catch {
        return $null
    }
}

function Get-CameraApps {
    # Programs that keep the camera to themselves while they are open. Returns their names for the user (empty when none runs).
    $known = [ordered]@{ 'WindowsCamera' = 'Windows 카메라 앱'; 'obs64' = 'OBS'; 'obs32' = 'OBS' }
    $names = @()
    foreach ($processName in $known.Keys) {
        if (Get-Process -Name $processName -ErrorAction SilentlyContinue) { $names += $known[$processName] }
    }
    return , @($names | Select-Object -Unique)
}

function Read-LogTail {
    # The last lines of a text file, or nothing when it is missing or empty.
    param([string]$Path, [int]$Count = 15)
    if (-not (Test-Path -LiteralPath $Path)) { return @() }
    return @(Get-Content -LiteralPath $Path -Tail $Count -Encoding UTF8 -ErrorAction SilentlyContinue | Where-Object { $_ -and $_.Trim() })
}

function Confirm-YesNo {
    # With no keyboard to answer (the input is redirected and has run out) Read-Host gives $null: that is a no, the safe answer for every question asked here.
    param([string]$Question)
    while ($true) {
        $reply = Read-Host "$Question (Y/N)"
        if ($null -eq $reply) {
            Write-Line "답할 키보드가 없어서 N 으로 봤어요."
            return $false
        }
        $answer = ([string]$reply).Trim().ToLower()
        if ($answer -eq 'y' -or $answer -eq 'yes') { return $true }
        if ($answer -eq 'n' -or $answer -eq 'no') { return $false }
        Write-Line "Y 또는 N 으로 답해 주세요."
    }
}

function Remove-FolderSafely {
    # Deletes one folder that this project created. Refuses anything that looks like a drive root or a very short path.
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return $false }
    $full = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    if ($full.Length -le 10) { throw "너무 짧은 경로라서 지우지 않았어요: ${full}" }
    Remove-Item -LiteralPath $full -Recurse -Force
    return $true
}
