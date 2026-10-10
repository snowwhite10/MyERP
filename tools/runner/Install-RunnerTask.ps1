<#
.SYNOPSIS
  註冊 Windows 工作排程器任務：使用者登入時自動啟動 Runner；當機時自動重啟。

.DESCRIPTION
  預設觸發：目前使用者登入（建議配合 VM 自動登入，即「開機 → 自動登入 → Runner 啟動」）。
  為何不用「開機時（不需登入）」：Claude Code / Codex 的登入憑證、Git Credential Manager
  都存在使用者設定檔，以 S4U 或系統帳號在未登入下執行常會讀不到。如確定要用，加 -AtStartup，
  系統會要求輸入 Windows 密碼。

.PARAMETER AtStartup
  另加「開機時」觸發（需要密碼，任務以「不論使用者是否登入」執行）。
#>
param(
    [string]$TaskName = 'MyERP-AI-Runner',
    [switch]$AtStartup
)
$ErrorActionPreference = 'Stop'
$runner = Join-Path $PSScriptRoot 'Start-Runner.ps1'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path

$shell = (Get-Command pwsh -ErrorAction SilentlyContinue)
if ($shell) { $exe = $shell.Source } else { $exe = (Get-Command powershell).Source }

$action = New-ScheduledTaskAction -Execute $exe `
    -Argument ('-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "{0}"' -f $runner) `
    -WorkingDirectory $repo

$user = "$env:USERDOMAIN\$env:USERNAME"
$triggers = @(New-ScheduledTaskTrigger -AtLogOn -User $user)
if ($AtStartup) { $triggers += New-ScheduledTaskTrigger -AtStartup }

$settings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -StartWhenAvailable `
    -ExecutionTimeLimit ([TimeSpan]::Zero) `
    -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 5) `
    -MultipleInstances IgnoreNew

if ($AtStartup) {
    $cred = Get-Credential -UserName $user -Message '輸入 Windows 密碼（任務需在未登入時執行）'
    Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $triggers -Settings $settings `
        -User $user -Password $cred.GetNetworkCredential().Password -RunLevel Limited -Force | Out-Null
} else {
    $principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited
    Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $triggers -Settings $settings `
        -Principal $principal -Force | Out-Null
}

Write-Host "已註冊工作排程器任務：$TaskName"
Write-Host "立即啟動：Start-ScheduledTask -TaskName $TaskName"
Write-Host "查看狀態：pwsh -File tools\runner\status.ps1"
