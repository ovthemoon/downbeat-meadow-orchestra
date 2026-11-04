<#
.SYNOPSIS
  Makes the watcher start by itself when you sign in to Windows: it puts one shortcut into your own Startup folder. Asks before it does.

.DESCRIPTION
  powershell -ExecutionPolicy Bypass -File install-autostart.ps1 [-Camera 0] [-Backend auto] [-Quality Auto|High|Low] [-PlayMode conduct|strike] [-MaxFps N] [-Mouse] [-Replay] [-Soak]
  (-ExecutionPolicy Bypass applies to this one run only; the PowerShell setting itself is not changed.)

  User level only: no administrator rights, no Task Scheduler, no registry, no Windows setting. The only change is the shortcut
  "Downbeat 들판 오케스트라 (자동 실행)" in your Startup folder; uninstall-autostart.ps1 removes it again.
  This does NOT make Windows sign in by itself: the PC has to be signed in (or set to sign in automatically by you) for the shortcut to run.
  The options are written into the shortcut, so it starts the same way every time. They are the options of run-supervised.ps1 (-Quality: the app's meadow tier, Low for a laptop with built-in graphics; -Soak: the overnight run with recorded hands, no sound).
#>
param(
    [ValidateRange(0, 9)][int]$Camera = 0,
    [ValidateSet('auto', 'dshow', 'msmf')][string]$Backend = 'auto',
    [ValidateSet('Auto', 'High', 'Low')][string]$Quality,
    [ValidateSet('conduct', 'strike')][string]$PlayMode,
    [ValidateRange(10, 240)][int]$MaxFps,
    [switch]$Mouse,
    [switch]$Replay,
    [switch]$Soak
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '_common.ps1')
. (Join-Path $PSScriptRoot '_supervisor-common.ps1')

# ValidateSet ignores case, but the app matches the tier name exactly; the shortcut gets the exact spelling.
if ($Quality) { $Quality = @{ 'auto' = 'Auto'; 'high' = 'High'; 'low' = 'Low' }[$Quality.ToLower()] }

function Invoke-Install {
    $runScript = Join-Path $PSScriptRoot 'run-supervised.ps1'
    if (-not (Test-Path -LiteralPath $runScript)) { throw "run-supervised.ps1 을 같은 폴더에서 찾지 못했어요." }
    $root = Find-BundleRoot
    $shortcutPath = Get-AutostartShortcutPath

    $options = "-Camera ${Camera} -Backend ${Backend}"
    if ($Quality) { $options += " -Quality ${Quality}" }
    if ($PlayMode) { $options += " -PlayMode $($PlayMode.ToLower())" }
    if ($MaxFps) { $options += " -MaxFps ${MaxFps}" }
    if ($Mouse) { $options += ' -Mouse' }
    if ($Replay) { $options += ' -Replay' }
    if ($Soak) { $options += ' -Soak' }
    $arguments = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Minimized -File $(Quote-Arg $runScript) ${options}"

    Write-Host ""
    Write-Host "== 들판 오케스트라: 로그인하면 저절로 시작하게 하기 ==" -ForegroundColor Cyan
    Write-Step "이 PC에서 바뀌는 것 (딱 하나, 관리자 권한 필요 없음)"
    Write-Line "내 계정의 시작 프로그램 폴더에 바로가기 한 개를 만들어요:"
    Write-Line "  ${shortcutPath}"
    Write-Line "그 바로가기는 로그인할 때 감시 프로그램을 작은 창으로 켜요: 손 추적과 앱을 켜고, 꺼지면 다시 켜 줘요."
    Write-Line "옵션: ${options}"
    Write-Line "작업 스케줄러, 레지스트리, Windows 설정, 자동 로그인 설정은 건드리지 않아요. (자동 로그인이 필요하면 직접 정해 주세요.)"
    Write-Line "되돌리려면 uninstall-autostart.ps1 을 실행하면 돼요."
    if (Test-Path -LiteralPath $shortcutPath) { Write-Warn "같은 이름의 바로가기가 이미 있어요. Y 를 누르면 새 옵션으로 바꿔요." }

    if (-not (Confirm-YesNo "만들까요?")) {
        Write-Line "아무것도 바꾸지 않고 끝났어요."
        return
    }

    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut($shortcutPath)
    $link.TargetPath = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $link.Arguments = $arguments
    $link.WorkingDirectory = $root
    $link.WindowStyle = 7            # minimized
    $link.Description = 'Downbeat: 손 추적과 앱을 켜고 감시해요'
    $link.Save()

    if (-not (Test-Path -LiteralPath $shortcutPath)) { throw "바로가기를 만들지 못했어요: ${shortcutPath}" }
    Write-Ok "바로가기를 만들었어요. 다음에 로그인할 때부터 저절로 시작해요."
    Write-Line "지금 바로 시험해 보려면 로그아웃했다가 다시 로그인하거나, run-supervised.ps1 을 직접 실행해 보세요."
}

try {
    Invoke-Install
    exit 0
} catch {
    Write-Host ""
    Write-Fail $_.Exception.Message
    exit 1
}
