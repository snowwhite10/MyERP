<#
.SYNOPSIS
  在背景啟動 Runner。

.EXAMPLE
  .\Start-Background.ps1                    # 沿用目前的代理模式（Set-AgentMode.ps1 設定的）
  .\Start-Background.ps1 codex              # 改為 Codex 優先（額度用完改用 Claude）再啟動
  .\Start-Background.ps1 codex -Only        # 改為只用 Codex 再啟動

  Runner 已在運行時，有指定代理就只更新模式（下一輪生效），不會重複啟動。
  建議改用 Set-AgentMode.ps1 設定模式，本腳本只負責啟動。
#>
param(
    [Parameter(Position = 0)][ValidateSet('', 'claude', 'codex')][string]$Agent = '',
    [switch]$Only
)
Import-Module (Join-Path $PSScriptRoot 'lib\RunnerCommon.psm1') -Force
try { [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false) } catch { }
$p = Get-RunnerPaths

$cfg = Read-JsonFile $p.Config
if ($Agent) {
    if (-not @($cfg.agents | Where-Object { $_.name -eq $Agent -and $_.enabled }).Count) {
        throw "$Agent 在 runner.config.json 中不存在或未啟用（enabled: false）"
    }
    [void](Set-AgentChoice -Preferred $Agent -Only ([bool]$Only) -By 'Start-Background')
    Write-AiRunLog -Stage '人手' -Status 'OK' -Message ('代理模式改為：' + (Get-AgentChoiceText))
}

if (Test-RunnerAlive) {
    Write-Host 'Runner 已在執行中，不會重複啟動。' -ForegroundColor Yellow
    if ($Agent) { Write-Host "代理模式已更新為：$(Get-AgentChoiceText)（下一輪生效）" -ForegroundColor Green }
    else { Write-Host "目前代理模式：$(Get-AgentChoiceText)" }
    return
}

if (Test-Path $p.StopFlag) {
    Remove-Item $p.StopFlag -Force
    Write-Host '已刪除 STOP 檔。'
}

Start-RunnerProcess
Write-Host "已在背景啟動 Runner，代理模式：$(Get-AgentChoiceText)。5 秒後顯示狀態..."
Start-Sleep -Seconds 5
& (Join-Path $PSScriptRoot 'status.ps1') -Lines 5
