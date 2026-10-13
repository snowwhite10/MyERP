<#
.SYNOPSIS
  顯示 Runner 目前狀態、目前模組與工作單元、最後 10 行日誌。
#>
param([int]$Lines = 10)
Import-Module (Join-Path $PSScriptRoot 'lib\RunnerCommon.psm1') -Force
try { [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false) } catch { }
$p = Get-RunnerPaths

$st = Read-JsonFile $p.StatusJson
$alive = $false
if ($st -and $st.pid) { $alive = [bool](Get-Process -Id $st.pid -ErrorAction SilentlyContinue) }

$label = '已停止'
if ($alive -and $st) {
    switch ($st.state) {
        'running'       { $label = "運行中（$($st.agent)）" }
        'waiting_quota' { $label = "等待額度（至 $($st.waitUntil)）" }
        'backoff'       { $label = "失敗退避（至 $($st.waitUntil)）" }
        'idle'          { $label = "運行中（$($st.note)）" }
        default         { $label = $st.state }
    }
}
if (Test-Path $p.StopFlag) { $label += '　[已要求停止：STOP 檔存在]' }

$state = Read-JsonFile $p.StateJson
Write-Host ''
Write-Host "Runner 狀態 : $label" -ForegroundColor Cyan
if ($st) { Write-Host "最後更新    : $($st.updatedAt)　$($st.note)" }
Write-Host "代理選擇    : $(Get-AgentChoiceText)"
if ($alive -and $st -and $st.PSObject.Properties['agents'] -and $st.agents) {
    foreach ($prop in $st.agents.PSObject.Properties) {
        $a = $prop.Value
        $txt = '可用'
        if ($a.availableAt) {
            $why = '失敗退避'
            if ($a.reason -eq 'quota') { $why = '額度不足' } elseif ($a.reason -eq 'missing') { $why = '指令不存在' }
            $txt = "$why，至 $($a.availableAt)"
        }
        if ($a.failures -gt 0) { $txt += "（連續失敗 $($a.failures) 次）" }
        if ($a.PSObject.Properties['hold'] -and $a.hold) { $txt += '（短期上限：等待此代理，不切換）' }
        if ($a.PSObject.Properties['probe'] -and $a.probe) { $txt += "　[用量：$($a.probe)]" }
        $color = 'Green'; if ($a.availableAt) { $color = 'Yellow' }
        Write-Host ('  {0,-8}: {1}' -f $prop.Name, $txt) -ForegroundColor $color
    }
}
if ($state) {
    Write-Host "項目狀態    : $($state.status)　Phase $($state.phase)"
    Write-Host "目前模組    : $($state.currentModule)"
    Write-Host "目前工作單元: $($state.currentWorkUnit)"
    Write-Host "下一步      : $($state.nextAction)"
}
Write-Host ''
$log = Get-TodayLogPath
if (-not (Test-Path $log)) {
    $latest = Get-ChildItem (Join-Path $p.LogRoot '*\*.log') -ErrorAction SilentlyContinue | Sort-Object Name | Select-Object -Last 1
    if ($latest) { $log = $latest.FullName }
}
if (Test-Path $log) {
    Write-Host "最後 $Lines 行日誌（$([IO.Path]::GetFileName($log))）：" -ForegroundColor Cyan
    Get-Content $log -Encoding UTF8 -Tail $Lines | ForEach-Object {
        $color = 'Gray'
        if ($_ -match '\| (FAIL|BLOCKED)\s') { $color = 'Red' }
        elseif ($_ -match '\| WAIT\s') { $color = 'Yellow' }
        elseif ($_ -match '\| (OK|DONE)\s') { $color = 'Green' }
        Write-Host $_ -ForegroundColor $color
    }
} else {
    Write-Host '（尚未有日誌）'
}
