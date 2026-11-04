<#
.SYNOPSIS
  Removes the shortcut that install-autostart.ps1 made, so the watcher no longer starts at sign-in. Asks before it does.

.DESCRIPTION
  powershell -ExecutionPolicy Bypass -File uninstall-autostart.ps1 [-StopNow]
  -StopNow   also ends a watcher that is running right now (with the app and the hand tracker it started).
  Only the one shortcut in your own Startup folder is deleted. Nothing else is changed.
#>
param(
    [switch]$StopNow
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_common.ps1')
. (Join-Path $PSScriptRoot '_supervisor-common.ps1')

try {
    $shortcutPath = Get-AutostartShortcutPath
    Write-Host ""
    Write-Host "== 들판 오케스트라: 자동 시작 지우기 ==" -ForegroundColor Cyan
    if (-not (Test-Path -LiteralPath $shortcutPath)) {
        Write-Skip "자동 시작 바로가기가 없어요 (${shortcutPath})."
    } else {
        Write-Line "지울 것: ${shortcutPath}"
        if (Confirm-YesNo "지울까요?") {
            Remove-Item -LiteralPath $shortcutPath -Force
            Write-Ok "지웠어요. 다음 로그인부터는 저절로 시작하지 않아요."
        } else {
            Write-Line "아무것도 지우지 않았어요."
        }
    }
    if ($StopNow) {
        $count = Stop-OldSupervisors
        if ($count -gt 0) { Write-Ok "켜져 있던 감시 프로그램 ${count}개와 앱·손 추적을 껐어요." } else { Write-Line "켜져 있는 감시 프로그램이 없어요." }
    }
    exit 0
} catch {
    Write-Fail $_.Exception.Message
    exit 1
}
