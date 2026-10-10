<#
.SYNOPSIS
  MyERP AI Runner 主循環：反覆呼叫 Claude Code / Codex，每輪完成一個工作單元。

.DESCRIPTION
  - 單一實例（重複啟動會直接退出）
  - 額度用完：解析 reset 時間，等待至 reset + 5 分鐘；解析不到則每 30 分鐘重試；不會結束 Runner
  - 多代理：某代理額度用完時自動改用下一個可用代理（runner.config.json 的 agents 次序）
  - 斷點續跑：業務進度全部在 state.json，由 AI 維護；Runner 不保存業務狀態
  - 停止方式：執行 Stop-Runner.ps1（建立 tools/runner/STOP 檔），本輪結束後停止
  - state.json 的 status = "DONE" 時自動停止

.PARAMETER Once
  只執行一輪（測試用）。

.PARAMETER Agent
  只使用指定代理（claude / codex），忽略其他代理。
#>
[CmdletBinding()]
param(
    [switch]$Once,
    [string]$Agent
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'lib\RunnerCommon.psm1') -Force

try { [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false) } catch { }
$OutputEncoding = New-Object System.Text.UTF8Encoding($false)

$paths = Get-RunnerPaths
$root = $paths.Root

# ---------------------------------------------------------------- 單一實例
$createdNew = $false
$mutex = New-Object System.Threading.Mutex($true, 'Local\MyERP_AI_Runner', [ref]$createdNew)
if (-not $createdNew) {
    Write-Host 'Runner 已在執行中，本次啟動結束。'
    exit 0
}

# ---------------------------------------------------------------- 防止系統閒置睡眠
# Runner 執行期間告訴 Windows「系統需要保持運作」（螢幕仍可關閉）。
# 不需要管理員權限，不改電源設定；Runner 結束時自動失效。
try {
    Add-Type -Namespace MyERP -Name Power -ErrorAction Stop -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("kernel32.dll")]
public static extern uint SetThreadExecutionState(uint esFlags);
'@
    # ES_CONTINUOUS (0x80000000) | ES_SYSTEM_REQUIRED (0x00000001)
    [void][MyERP.Power]::SetThreadExecutionState([Convert]::ToUInt32('80000001', 16))
} catch {
    Write-Host "無法設定防睡眠：$($_.Exception.Message)"
}

function Get-Config {
    $cfg = Read-JsonFile $paths.Config
    if (-not $cfg) { throw "無法讀取 $($paths.Config)" }
    return $cfg
}

function Get-ProjectState {
    return Read-JsonFile $paths.StateJson
}

function Get-StateModule {
    $s = Get-ProjectState
    if ($s -and $s.PSObject.Properties['currentModule'] -and $s.currentModule) { return [string]$s.currentModule }
    return '-'
}

function Wait-Until([datetime]$Until, [string]$StatusState, [string]$AgentName, [string]$Note) {
    while ((Get-Date) -lt $Until) {
        if (Test-Path $paths.StopFlag) { return }
        Set-RunnerStatus -State $StatusState -Agent $AgentName -WaitUntil $Until -Note $Note
        $remaining = ($Until - (Get-Date)).TotalSeconds
        Start-Sleep -Seconds ([Math]::Max(1, [Math]::Min(60, [int]$remaining)))
    }
}

function Build-Prompt([string]$AgentName, [int]$DirtyCount, [string]$LastResult) {
    $tpl = [System.IO.File]::ReadAllText($paths.PromptTpl, [System.Text.Encoding]::UTF8)
    $dirtyNote = '工作目錄乾淨。'
    if ($DirtyCount -gt 0) {
        $dirtyNote = "警告：git 有 $DirtyCount 個未提交變更（上一輪未完成）。本輪必須先處理：檢查變更，能完成的完成並提交，不能完成的還原，然後才開始新工作。"
    }
    $text = $tpl.Replace('{{AGENT}}', $AgentName)
    $text = $text.Replace('{{NOW}}', (Get-Date).ToString('yyyy-MM-dd HH:mm'))
    $text = $text.Replace('{{DIRTY_NOTE}}', $dirtyNote)
    $text = $text.Replace('{{LAST_RESULT}}', $LastResult)
    return $text
}

# ---------------------------------------------------------------- 主循環
$agentWaitUntil = @{}      # 代理名稱 -> 可再使用的時間
$consecutiveFailures = 0
$lastResult = '（本次 Runner 啟動後第一輪）'
$lastStartDate = $null
$doneLogged = $false

try {
    Write-AiRunLog -Stage 'runner' -Status 'START' -Message ('Runner 啟動（PID {0}）' -f $PID)
    $lastStartDate = (Get-Date).Date

    while ($true) {
        $cfg = Get-Config

        # 每日首次啟動記錄（Runner 跨日持續運行時）
        if ((Get-Date).Date -ne $lastStartDate) {
            if (-not (Test-TodayHasStart)) {
                Write-AiRunLog -Stage 'runner' -Status 'START' -Message '每日啟動'
            }
            $lastStartDate = (Get-Date).Date
        }

        # 人手停止
        if (Test-Path $paths.StopFlag) {
            Write-AiRunLog -Stage 'runner' -Status 'SKIP' -Message '偵測到 STOP 檔，Runner 停止（刪除 tools/runner/STOP 後可重新啟動）'
            break
        }

        # 項目完成
        $state = Get-ProjectState
        if ($state -and $state.PSObject.Properties['status'] -and $state.status -eq 'DONE') {
            if (-not $doneLogged) {
                Write-AiRunLog -Stage 'runner' -Status 'DONE' -Message 'state.json 狀態為 DONE，Runner 停止'
                $doneLogged = $true
            }
            break
        }

        # 選擇代理
        $agents = @($cfg.agents | Where-Object { $_.enabled })
        if ($Agent) { $agents = @($agents | Where-Object { $_.name -eq $Agent }) }
        if ($agents.Count -eq 0) { throw '沒有可用代理，請檢查 runner.config.json' }

        $now = Get-Date
        $chosen = $agents | Where-Object { -not $agentWaitUntil.ContainsKey($_.name) -or $agentWaitUntil[$_.name] -le $now } | Select-Object -First 1
        if (-not $chosen) {
            $earliest = ($agents | ForEach-Object { $agentWaitUntil[$_.name] } | Sort-Object | Select-Object -First 1)
            Wait-Until -Until $earliest -StatusState 'waiting_quota' -AgentName '' -Note '所有代理額度不足'
            continue
        }

        # 執行一輪
        $dirty = Get-GitDirtyCount $root
        $prompt = Build-Prompt -AgentName $chosen.name -DirtyCount $dirty -LastResult $lastResult
        Set-RunnerStatus -State 'running' -Agent $chosen.name -Note '執行中'
        $roundStart = Get-Date
        $r = Invoke-AgentRound -Agent $chosen -Prompt $prompt -TimeoutMinutes $cfg.roundTimeoutMinutes -WorkingDirectory $root
        $minutes = [int]((Get-Date) - $roundStart).TotalMinutes
        $module = Get-StateModule

        if ($cfg.keepLastOutput -and $r.Output) {
            [System.IO.File]::WriteAllText($paths.LastOutput, $r.Output, (New-Object System.Text.UTF8Encoding($false)))
        }

        if ($r.Missing) {
            Write-AiRunLog -Module $module -Stage 'runner' -Status 'FAIL' -Message ("{0} 未安裝或不在 PATH，暫停此代理 60 分鐘" -f $chosen.name)
            $agentWaitUntil[$chosen.name] = (Get-Date).AddMinutes(60)
            $lastResult = "$($chosen.name) 指令不存在"
        }
        elseif (Test-QuotaExhausted -Output $r.Output -ExitCode $r.ExitCode -Patterns $chosen.quotaPatterns -TailLines $cfg.quotaScanTailLines) {
            $tail = (($r.Output -split "`r?`n") | Select-Object -Last $cfg.quotaScanTailLines) -join "`n"
            $reset = ConvertFrom-ResetText $tail
            if ($reset -and $reset -le (Get-Date)) { $reset = $null }   # 已過去的時間視為解析失敗
            if ($reset) {
                $until = $reset.AddMinutes($cfg.resetBufferMinutes)
                $msg = '額度不足（{0}），reset {1}，等待至 {2} 執行' -f $chosen.name, $reset.ToString('MM-dd HH:mm'), $until.ToString('MM-dd HH:mm')
            } else {
                $until = (Get-Date).AddMinutes($cfg.unknownResetRetryMinutes)
                $msg = '額度不足（{0}），解析不到 reset 時間，{1} 再試' -f $chosen.name, $until.ToString('HH:mm')
            }
            $agentWaitUntil[$chosen.name] = $until
            $others = @($agents | Where-Object { $_.name -ne $chosen.name -and (-not $agentWaitUntil.ContainsKey($_.name) -or $agentWaitUntil[$_.name] -le (Get-Date)) })
            if ($others.Count -gt 0) { $msg += "；改用 $($others[0].name)" }
            Write-AiRunLog -Module $module -Stage '額度' -Status 'WAIT' -Message $msg
            $lastResult = "上一輪 $($chosen.name) 額度用完而中斷，工作可能未完成，請檢查 git 狀態與 state.json.nextAction。"
        }
        elseif ($r.TimedOut) {
            $consecutiveFailures++
            Write-AiRunLog -Module $module -Stage 'runner' -Status 'FAIL' -Message ('本輪超時（{0} 分鐘，{1}），已終止' -f $cfg.roundTimeoutMinutes, $chosen.name)
            $lastResult = "上一輪超時被終止（超過 $($cfg.roundTimeoutMinutes) 分鐘）。請把工作單元拆小，並先處理未提交變更。"
        }
        elseif ($r.ExitCode -ne 0) {
            $consecutiveFailures++
            $first = Get-FirstErrorLine $r.Output
            Write-AiRunLog -Module $module -Stage 'runner' -Status 'FAIL' -Message ('{0} 結束碼 {1}：{2}' -f $chosen.name, $r.ExitCode, $first)
            $lastResult = "上一輪 $($chosen.name) 異常結束（結束碼 $($r.ExitCode)）：$first"
        }
        else {
            $consecutiveFailures = 0
            $lastResult = "上一輪 $($chosen.name) 正常結束（約 $minutes 分鐘）。"
        }

        # 檢查未提交變更
        $dirtyAfter = Get-GitDirtyCount $root
        if ($dirtyAfter -gt 0) {
            Write-AiRunLog -Module $module -Stage 'git檢查' -Status 'FAIL' -Message ("Warning：有 {0} 個未提交變更，下一輪先處理" -f $dirtyAfter)
        }

        if ($Once) { break }

        # 連續失敗退避（非額度問題）
        if ($consecutiveFailures -gt 0) {
            $backoffList = @($cfg.transientBackoffMinutes)
            if ($consecutiveFailures -ge $cfg.maxConsecutiveFailuresBeforeLongWait) {
                $wait = [int]$cfg.longWaitMinutes
            } else {
                $wait = [int]$backoffList[[Math]::Min($consecutiveFailures - 1, $backoffList.Count - 1)]
            }
            $until = (Get-Date).AddMinutes($wait)
            Write-AiRunLog -Module $module -Stage 'runner' -Status 'WAIT' -Message ("連續失敗 {0} 次，{1} 分鐘後重試" -f $consecutiveFailures, $wait)
            Wait-Until -Until $until -StatusState 'backoff' -AgentName $chosen.name -Note "連續失敗 $consecutiveFailures 次"
        } else {
            Set-RunnerStatus -State 'idle' -Agent $chosen.name -Note '輪與輪之間'
            Start-Sleep -Seconds $cfg.pauseBetweenRoundsSeconds
        }
    }
}
catch {
    Write-AiRunLog -Stage 'runner' -Status 'FAIL' -Message ('Runner 例外：' + $_.Exception.Message)
    throw
}
finally {
    Set-RunnerStatus -State 'stopped' -Note '已停止'
    $mutex.ReleaseMutex()
    $mutex.Dispose()
}
