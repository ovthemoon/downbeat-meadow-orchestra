# Helpers of the scripts that start, stop and register the watcher (supervisor). Loaded after _common.ps1; not run by itself.
# Written for stock Windows PowerShell 5.1: no PowerShell 7 syntax (no && or ||, no ternary, no ??).
# Reminder for Korean text: in a double-quoted string write ${name} when Korean letters follow a variable.

$script:AutostartShortcutName = 'Downbeat 들판 오케스트라 (자동 실행).lnk'

function Find-AppRoot {
    # The folder that holds Downbeat.exe: the bundle folder when the player sits in it, otherwise the folder of the newest player found by Find-Player. $null when there is none.
    param([string]$Root)
    if (Test-Path -LiteralPath (Join-Path $Root 'Downbeat.exe')) { return $Root }
    $player = Find-Player $Root
    if ($player) { return $player.DirectoryName }
    return $null
}

function Get-AutostartShortcutPath {
    # The shortcut in the Startup folder of the current user (no administrator rights are needed to write there).
    $startup = [Environment]::GetFolderPath('Startup')
    if (-not $startup) { throw "이 계정의 시작 프로그램 폴더를 찾지 못했어요." }
    return (Join-Path $startup $script:AutostartShortcutName)
}

function Get-SupervisorProcesses {
    # The Python processes that run the watcher. Their command line names downbeat_supervisor.
    return @(Get-CimInstance Win32_Process -Filter "Name = 'python.exe'" -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -and ($_.CommandLine -like '*downbeat_supervisor*') })
}

function Stop-OldSupervisors {
    # Ends the watchers that are running, each with everything it started (the tracker and the app), so that two watchers never fight over the camera and the port.
    # Returns how many were found.
    $found = Get-SupervisorProcesses
    foreach ($process in $found) {
        Stop-ProcessTree -ProcessId ([int]$process.ProcessId)
    }
    return $found.Count
}

function Stop-OldPlayers {
    # Ends Downbeat.exe processes that an earlier run left behind. Closing the watcher's console window with X ends the watcher and the tracker but not the app window,
    # and a second app would write the same heartbeat file and fight over the sound. Only apps that run from THIS app folder are ended; a copy somewhere else is left alone.
    # Returns how many were found (0 when the app folder is not known).
    param([string]$AppRoot)
    if (-not $AppRoot) { return 0 }
    $folder = [IO.Path]::GetFullPath($AppRoot).TrimEnd('\') + '\'
    $found = @(Get-CimInstance Win32_Process -Filter "Name = 'Downbeat.exe'" -ErrorAction SilentlyContinue | Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($folder, [StringComparison]::OrdinalIgnoreCase) })
    foreach ($process in $found) {
        Stop-ProcessTree -ProcessId ([int]$process.ProcessId)
    }
    return $found.Count
}
