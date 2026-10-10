<#
.SYNOPSIS
  要求 Runner 在本輪結束後停止（建立 STOP 檔）。-Now 會立即終止 Runner 及 AI 程序（可能留下未提交變更）。
#>
param([switch]$Now)
Import-Module (Join-Path $PSScriptRoot 'lib\RunnerCommon.psm1') -Force
$p = Get-RunnerPaths
New-Item -ItemType File -Path $p.StopFlag -Force | Out-Null
Write-Host '已建立 STOP 檔：Runner 會在本輪結束後停止。重新啟動前請刪除 tools\runner\STOP。'
if ($Now) {
    $st = Read-JsonFile $p.StatusJson
    if ($st -and $st.pid) {
        & taskkill.exe /T /F /PID $st.pid
        Write-AiRunLog -Stage 'runner' -Status 'SKIP' -Message '人手立即停止 Runner'
    }
}
