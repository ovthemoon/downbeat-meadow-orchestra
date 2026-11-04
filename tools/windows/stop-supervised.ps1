<#
.SYNOPSIS
  Ends the watcher and everything it started (the app and the hand tracker).

.DESCRIPTION
  powershell -ExecutionPolicy Bypass -File stop-supervised.ps1
  It only ends processes of the Downbeat watcher (and the Downbeat.exe of this app folder) that run under your own account. No setting is changed.
  Use this, or Ctrl+C in the watcher window, to stop. Closing the watcher window with X leaves the app open; this script ends it too.
#>
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_common.ps1')
. (Join-Path $PSScriptRoot '_supervisor-common.ps1')

try {
    $count = Stop-OldSupervisors
    if ($count -eq 0) {
        Write-Line "켜져 있는 감시 프로그램이 없어요."
    } else {
        Write-Ok "감시 프로그램 ${count}개와 그것이 켠 앱·손 추적을 껐어요."
    }
    $leftTrackers = Stop-OldTrackers
    if ($leftTrackers -gt 0) { Write-Line "남아 있던 손 추적 ${leftTrackers}개도 껐어요." }

    # The app of this folder may be left over when the watcher's window was closed with X. If the folder is not found, the app is not looked for.
    $appRoot = $null
    try {
        $appRoot = Find-AppRoot (Find-BundleRoot)
    } catch {
        Write-Warn "앱 폴더를 찾지 못해서 남아 있는 앱은 확인하지 못했어요: $($_.Exception.Message)"
    }
    $leftPlayers = Stop-OldPlayers $appRoot
    if ($leftPlayers -gt 0) { Write-Line "남아 있던 앱 ${leftPlayers}개도 껐어요." }
    exit 0
} catch {
    Write-Fail $_.Exception.Message
    exit 1
}
