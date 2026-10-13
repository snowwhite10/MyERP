<#
.SYNOPSIS
  在背景啟動 Runner，並選擇使用哪個 AI。不指定則預設 Claude。

.EXAMPLE
  .\Start-Background.ps1            # Claude 優先（額度用完時切換到 Codex）
  .\Start-Background.ps1 codex      # Codex 優先（額度用完時切換到 Claude）
  .\Start-Background.ps1 codex -Only   # 只用 Codex；額度用完就等待，不切換

  執行中改用其他 AI：Use-Agent.ps1
  停止：Stop-Runner.ps1
#>
param(
    [Parameter(Position = 0)][ValidateSet('claude', 'codex')][string]$Agent = 'claude',
    [switch]$Only
)
Import-Module (Join-Path $PSScriptRoot 'lib\RunnerCommon.psm1') -Force
try { [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false) } catch { }
$p = Get-RunnerPaths

if (Test-RunnerAlive) {
    Write-Host 'Runner 已在執行中。要改用其他 AI，請執行：' -ForegroundColor Yellow
    Write-Host "  powershell -ExecutionPolicy Bypass -File tools\runner\Use-Agent.ps1 $Agent           （下一輪生效）"
    Write-Host "  powershell -ExecutionPolicy Bypass -File tools\runner\Use-Agent.ps1 $Agent -Restart  （立即中斷本輪並重新開始）"
    return
}

$cfg = Read-JsonFile $p.Config
if (-not @($cfg.agents | Where-Object { $_.name -eq $Agent -and $_.enabled }).Count) {
    throw "$Agent 在 runner.config.json 中不存在或未啟用（enabled: false）"
}

if (Test-Path $p.StopFlag) {
    Remove-Item $p.StopFlag -Force
    Write-Host '已刪除 STOP 檔。'
}

Start-RunnerProcess -Agent $Agent -Only:$Only
Write-Host "已在背景啟動 Runner（$Agent$(if ($Only) { '，只用此代理' } else { ' 優先' })），5 秒後顯示狀態..."
Start-Sleep -Seconds 5
& (Join-Path $PSScriptRoot 'status.ps1') -Lines 5
