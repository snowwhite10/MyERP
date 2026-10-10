<#
.SYNOPSIS
  寫一行 AI 執行日誌（給 AI 代理使用）。
.EXAMPLE
  pwsh -NoProfile -File tools/runner/Write-AiLog.ps1 -Module SD -Stage 測試 -Status OK -Message "銷售訂單 測試 48/48 通過，已提交 a1b2c3d"
#>
param(
    [string]$Module = '-',
    [Parameter(Mandatory)][string]$Stage,
    [Parameter(Mandatory)][ValidateSet('START', 'OK', 'FAIL', 'WAIT', 'SKIP', 'BLOCKED', 'DONE')][string]$Status,
    [Parameter(Mandatory)][string]$Message
)
Import-Module (Join-Path $PSScriptRoot 'lib\RunnerCommon.psm1') -Force
Write-AiRunLog -Module $Module -Stage $Stage -Status $Status -Message $Message
