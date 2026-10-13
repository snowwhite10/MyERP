<#
.SYNOPSIS
  選擇 Runner 使用哪個 AI 代理。下一輪開始生效；加 -Restart 則立即中斷本輪並重新開始。

.EXAMPLE
  .\Use-Agent.ps1 codex            # Codex 優先；Codex 額度用完時仍會切換到 Claude
  .\Use-Agent.ps1 codex -Only      # 只用 Codex；額度用完就等待，不切換
  .\Use-Agent.ps1 claude           # 改回 Claude 優先（預設）
  .\Use-Agent.ps1 codex -Restart   # 立即中斷目前這一輪，改用 Codex 重新開始
#>
param(
    [Parameter(Mandatory, Position = 0)][ValidateSet('claude', 'codex')][string]$Agent,
    [switch]$Only,
    [switch]$Restart
)
Import-Module (Join-Path $PSScriptRoot 'lib\RunnerCommon.psm1') -Force
try { [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false) } catch { }
$p = Get-RunnerPaths

$cfg = Read-JsonFile $p.Config
$found = @($cfg.agents | Where-Object { $_.name -eq $Agent -and $_.enabled })
if ($found.Count -eq 0) { throw "$Agent 在 runner.config.json 中不存在或未啟用（enabled: false）" }

$obj = [ordered]@{ preferred = $Agent; only = [bool]$Only; setAt = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss'); by = 'Use-Agent' }
Write-JsonFile -Path $p.AgentChoice -Object $obj
$text = Get-AgentChoiceText
Write-AiRunLog -Stage '人手' -Status 'OK' -Message "代理選擇改為：$text"
Write-Host "代理選擇：$text"

if (-not $Restart) {
    if (Test-RunnerAlive) {
        Write-Host '下一輪開始生效（目前這一輪會由原本的代理做完）。如該代理正在等待額度 / 退避，會立即重新嘗試。'
    } else {
        Write-Host ('Runner 目前沒有運行。啟動：powershell -ExecutionPolicy Bypass -File tools\runner\Start-Background.ps1 ' + $Agent)
    }
    return
}

# 立即中斷本輪並重新啟動
$st = Read-JsonFile $p.StatusJson
if ($st -and $st.pid -and (Get-Process -Id $st.pid -ErrorAction SilentlyContinue)) {
    & taskkill.exe /T /F /PID $st.pid 2>&1 | Out-Null
    Write-AiRunLog -Stage '人手' -Status 'SKIP' -Message '為切換代理而中斷本輪（未提交變更會由下一輪處理）'
    Start-Sleep -Seconds 3
}
if (Test-Path $p.StopFlag) { Remove-Item $p.StopFlag -Force }
Start-RunnerProcess -Agent $Agent -Only:$Only
Start-Sleep -Seconds 3
Write-Host "已重新啟動 Runner（$Agent）。"
