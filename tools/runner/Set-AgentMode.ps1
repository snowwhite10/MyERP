<#
.SYNOPSIS
  設定 Runner 使用哪個 AI。設定會一直保留，重新啟動或重新開機都不會被重設。

.EXAMPLE
  .\Set-AgentMode.ps1                  # 顯示目前模式
  .\Set-AgentMode.ps1 codex-claude     # Codex 優先，Codex 額度用完改用 Claude
  .\Set-AgentMode.ps1 claude-codex     # Claude 優先，Claude 額度用完改用 Codex
  .\Set-AgentMode.ps1 codex-only       # 只用 Codex，額度用完就等待
  .\Set-AgentMode.ps1 claude-only      # 只用 Claude，額度用完就等待

  Runner 運行中也可以設定，下一輪生效（目前這一輪會由原本的 AI 做完）。
#>
param(
    [Parameter(Position = 0)][ValidateSet('', 'codex-claude', 'claude-codex', 'codex-only', 'claude-only')][string]$Mode = ''
)
Import-Module (Join-Path $PSScriptRoot 'lib\RunnerCommon.psm1') -Force
try { [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false) } catch { }
$p = Get-RunnerPaths

if (-not $Mode) {
    Write-Host "目前模式：$(Get-AgentChoiceText)" -ForegroundColor Cyan
    Write-Host ''
    Write-Host '可設定的模式：'
    Write-Host '  codex-claude   Codex 優先，額度用完改用 Claude'
    Write-Host '  claude-codex   Claude 優先，額度用完改用 Codex'
    Write-Host '  codex-only     只用 Codex，額度用完就等待'
    Write-Host '  claude-only    只用 Claude，額度用完就等待'
    return
}

$m = ConvertTo-AgentMode $Mode
$cfg = Read-JsonFile $p.Config
$enabled = @($cfg.agents | Where-Object { $_.enabled } | ForEach-Object { $_.name })
if ($enabled -notcontains $m.Preferred) {
    throw "$($m.Preferred) 在 runner.config.json 中未啟用（enabled: false），請先啟用"
}
if (-not $m.Only) {
    $other = @($enabled | Where-Object { $_ -ne $m.Preferred })
    if ($other.Count -eq 0) { Write-Host '注意：只有一個 AI 啟用，實際效果等同 only 模式。' -ForegroundColor Yellow }
}

[void](Set-AgentChoice -Preferred $m.Preferred -Only ([bool]$m.Only) -By 'Set-AgentMode')
Write-AiRunLog -Stage '人手' -Status 'OK' -Message ('代理模式改為：' + (Get-AgentChoiceText))
Write-Host "代理模式已設定為：$(Get-AgentChoiceText)" -ForegroundColor Green

if (Test-RunnerAlive) {
    Write-Host '下一輪生效。如果該 AI 正在等待額度，Runner 會立即重新嘗試一次。'
} else {
    Write-Host 'Runner 目前沒有運行。啟動：powershell -ExecutionPolicy Bypass -File tools\runner\Start-Background.ps1'
}
