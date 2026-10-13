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
  優先使用的代理（claude / codex）。不指定則預設 claude。額度用完時仍會切換到其他代理。
  執行中要改用其他代理：Use-Agent.ps1。

.PARAMETER Only
  只用 -Agent 指定的代理；額度用完就等待，不切換。
#>
[CmdletBinding()]
param(
    [switch]$Once,
    [ValidateSet('', 'claude', 'codex')][string]$Agent = '',
    [switch]$Only
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

function Update-Status {
    param([string]$State, [string]$AgentName = '', $WaitUntil = $null, [string]$Note = '')
    $info = [ordered]@{}
    foreach ($a in @((Get-Config).agents | Where-Object { $_.enabled })) {
        $n = $a.name
        $until = $null; $reason = $null
        if ($agentWaitUntil.ContainsKey($n) -and $agentWaitUntil[$n] -gt (Get-Date)) {
            $until = $agentWaitUntil[$n].ToString('yyyy-MM-dd HH:mm:ss'); $reason = $agentReason[$n]
        }
        $info[$n] = [ordered]@{ availableAt = $until; reason = $reason; failures = [int]$agentFailures[$n] }
    }
    if ($null -ne $WaitUntil) {
        Set-RunnerStatus -State $State -Agent $AgentName -WaitUntil ([datetime]$WaitUntil) -Note $Note -Agents $info
    } else {
        Set-RunnerStatus -State $State -Agent $AgentName -Note $Note -Agents $info
    }
}

function Get-ProjectState {
    return Read-JsonFile $paths.StateJson
}

function Get-StateModule {
    $s = Get-ProjectState
    if ($s -and $s.PSObject.Properties['currentModule'] -and $s.currentModule) { return [string]$s.currentModule }
    return '-'
}

function Write-DailyStartIfNeeded {
    if ((Get-Date).Date -ne $script:lastStartDate) {
        if (-not (Test-TodayHasStart)) {
            Write-AiRunLog -Stage 'runner' -Status 'START' -Message '每日啟動'
        }
        $script:lastStartDate = (Get-Date).Date
    }
}

function Wait-Until([datetime]$Until, [string]$StatusState, [string]$AgentName, [string]$Note) {
    while ((Get-Date) -lt $Until) {
        if (Test-Path $paths.StopFlag) { return }
        Write-DailyStartIfNeeded   # 長時間等待額度時跨日，仍要寫每日 START
        Update-Status -State $StatusState -AgentName $AgentName -WaitUntil $Until -Note $Note
        $remaining = ($Until - (Get-Date)).TotalSeconds
        Start-Sleep -Seconds ([Math]::Max(1, [Math]::Min(60, [int]$remaining)))
    }
}

function Build-Prompt([string]$AgentName, [int]$DirtyCount, [string]$LastResult) {
    $tpl = [System.IO.File]::ReadAllText($paths.PromptTpl, [System.Text.Encoding]::UTF8)
    $dirtyNote = '工作目錄乾淨。'
    if ($DirtyCount -lt 0) {
        $dirtyNote = '警告：Runner 執行 git status 失敗（repo 可能損壞，或有殘留的 .git/index.lock）。本輪必須先診斷並修復 git 狀態，修復前不得開始新工作；無法修復則寫 BLOCKED 日誌並結束本輪。'
    }
    elseif ($DirtyCount -gt 0) {
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
$agentFailures = @{}       # 代理名稱 -> 連續失敗次數（非額度問題）
$agentReason = @{}         # 代理名稱 -> 等待原因：quota / fail / missing
$lastAgent = $null         # 上一輪使用的代理
$announcedAgent = $null    # 已在日誌宣佈「改用」的代理
$allWaitLogged = $false
$lastChoiceSetAt = $null
$lastResult = '（本次 Runner 啟動後第一輪）'
$script:lastStartDate = $null
$doneLogged = $false

try {
    # 啟動時決定優先代理：-Agent 指定的，否則預設 claude
    $startCfg = Get-Config
    $pref = $Agent
    if (-not $pref) { $pref = 'claude' }
    if (-not @($startCfg.agents | Where-Object { $_.name -eq $pref -and $_.enabled }).Count) {
        $pref = @($startCfg.agents | Where-Object { $_.enabled })[0].name
    }
    Write-JsonFile -Path $paths.AgentChoice -Object ([ordered]@{ preferred = $pref; only = [bool]$Only; setAt = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss'); by = 'start' })
    $lastChoiceSetAt = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
    Write-AiRunLog -Stage 'runner' -Status 'START' -Message ('Runner 啟動（PID {0}），代理：{1}' -f $PID, (Get-AgentChoiceText))
    $script:lastStartDate = (Get-Date).Date

    while ($true) {
        $cfg = Get-Config

        # 每日首次啟動記錄（Runner 跨日持續運行時）
        Write-DailyStartIfNeeded

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

        # 人手用 Use-Agent.ps1 改了選擇：清除該代理的等待狀態，下一輪立即嘗試
        $choice = Read-JsonFile $paths.AgentChoice
        if ($choice -and $choice.setAt -and $choice.setAt -ne $lastChoiceSetAt) {
            $lastChoiceSetAt = $choice.setAt
            if ($choice.preferred -and $agentWaitUntil.ContainsKey($choice.preferred)) {
                $agentWaitUntil.Remove($choice.preferred)
                $agentReason.Remove($choice.preferred)
                $agentFailures[$choice.preferred] = 0
            }
            $announcedAgent = $null
        }

        # 選擇代理
        $agents = Get-OrderedAgents -Config $cfg   # 已套用啟動參數 / Use-Agent.ps1 的選擇
        if ($agents.Count -eq 0) { throw '沒有可用代理，請檢查 runner.config.json' }

        $now = Get-Date
        $chosen = $agents | Where-Object { -not $agentWaitUntil.ContainsKey($_.name) -or $agentWaitUntil[$_.name] -le $now } | Select-Object -First 1
        if (-not $chosen) {
            $earliest = ($agents | ForEach-Object { $agentWaitUntil[$_.name] } | Sort-Object | Select-Object -First 1)
            if (-not $allWaitLogged) {
                $parts = foreach ($a in $agents) {
                    $why = '失敗退避'
                    if ($agentReason[$a.name] -eq 'quota') { $why = '額度不足' } elseif ($agentReason[$a.name] -eq 'missing') { $why = '指令不存在' }
                    '{0} {1}至 {2}' -f $a.name, $why, $agentWaitUntil[$a.name].ToString('MM-dd HH:mm')
                }
                Write-AiRunLog -Stage 'runner' -Status 'WAIT' -Message ('所有代理暫不可用（{0}），等待至 {1}' -f ($parts -join '；'), $earliest.ToString('MM-dd HH:mm'))
                $allWaitLogged = $true
            }
            Wait-Until -Until $earliest -StatusState 'waiting_quota' -AgentName '' -Note '所有代理暫不可用（額度不足或失敗退避）'
            continue
        }
        $allWaitLogged = $false

        # 記錄恢復 / 切換
        if ($agentReason[$chosen.name] -eq 'quota') {
            Write-AiRunLog -Stage 'runner' -Status 'OK' -Message ('額度已恢復（{0}），恢復使用 {0}' -f $chosen.name)
            $announcedAgent = $chosen.name
        }
        elseif ($lastAgent -and $chosen.name -ne $lastAgent -and $chosen.name -ne $announcedAgent) {
            Write-AiRunLog -Stage 'runner' -Status 'OK' -Message ('改用 {0}（上一輪 {1}）' -f $chosen.name, $lastAgent)
            $announcedAgent = $chosen.name
        }
        $agentReason.Remove($chosen.name)
        $agentWaitUntil.Remove($chosen.name)
        $lastAgent = $chosen.name

        # 執行一輪
        $dirty = Get-GitDirtyCount $root
        $prompt = Build-Prompt -AgentName $chosen.name -DirtyCount $dirty -LastResult $lastResult
        Update-Status -State 'running' -AgentName $chosen.name -Note '執行中'
        $roundStart = Get-Date
        $r = Invoke-AgentRound -Agent $chosen -Prompt $prompt -TimeoutMinutes $cfg.roundTimeoutMinutes -WorkingDirectory $root
        $minutes = [int]((Get-Date) - $roundStart).TotalMinutes
        $module = Get-StateModule
        $roundFailed = $false

        $clean = Remove-PromptEcho -Output $r.Output -Prompt $prompt
        if ($cfg.keepLastOutput -and $r.Output) {
            $enc = New-Object System.Text.UTF8Encoding($false)
            [System.IO.File]::WriteAllText($paths.LastOutput, $r.Output, $enc)
            [System.IO.File]::WriteAllText((Join-Path $paths.RunnerDir ('last-output-{0}.txt' -f $chosen.name)), $r.Output, $enc)
        }

        if ($r.Missing) {
            Write-AiRunLog -Module $module -Stage 'runner' -Status 'FAIL' -Message ("{0} 未安裝或不在 PATH，暫停此代理 60 分鐘" -f $chosen.name)
            $agentWaitUntil[$chosen.name] = (Get-Date).AddMinutes(60)
            $agentReason[$chosen.name] = 'missing'
            $lastResult = "$($chosen.name) 指令不存在"
        }
        elseif (Test-QuotaExhausted -Output $clean -ExitCode $r.ExitCode -Patterns $chosen.quotaPatterns -TailLines $cfg.quotaScanTailLines) {
            $tail = (($clean -split "`r?`n") | Select-Object -Last $cfg.quotaScanTailLines) -join "`n"
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
            $agentReason[$chosen.name] = 'quota'
            $others = @($agents | Where-Object { $_.name -ne $chosen.name -and (-not $agentWaitUntil.ContainsKey($_.name) -or $agentWaitUntil[$_.name] -le (Get-Date)) })
            if ($others.Count -gt 0) { $msg += "；改用 $($others[0].name)"; $announcedAgent = $others[0].name }
            Write-AiRunLog -Module $module -Stage '額度' -Status 'WAIT' -Message $msg
            $lastResult = "上一輪 $($chosen.name) 額度用完而中斷，工作可能未完成，請檢查 git 狀態與 state.json.nextAction。"
        }
        elseif ($r.TimedOut) {
            $roundFailed = $true
            Write-AiRunLog -Module $module -Stage 'runner' -Status 'FAIL' -Message ('本輪超時（{0} 分鐘，{1}），已終止' -f $cfg.roundTimeoutMinutes, $chosen.name)
            $lastResult = "上一輪超時被終止（超過 $($cfg.roundTimeoutMinutes) 分鐘）。請把工作單元拆小，並先處理未提交變更。"
        }
        elseif ($r.ExitCode -ne 0) {
            $roundFailed = $true
            $first = Get-ErrorSummary $clean
            Write-AiRunLog -Module $module -Stage 'runner' -Status 'FAIL' -Message ('{0} 結束碼 {1}：{2}' -f $chosen.name, $r.ExitCode, $first)
            $lastResult = "上一輪 $($chosen.name) 異常結束（結束碼 $($r.ExitCode)）：$first"
        }
        else {
            $agentFailures[$chosen.name] = 0
            $lastResult = "上一輪 $($chosen.name) 正常結束（約 $minutes 分鐘）。"
        }

        # 檢查未提交變更
        $dirtyAfter = Get-GitDirtyCount $root
        if ($dirtyAfter -lt 0) {
            Write-AiRunLog -Module $module -Stage 'git檢查' -Status 'FAIL' -Message 'git status 執行失敗（repo 損壞或 index.lock 殘留），下一輪先修復'
        }
        elseif ($dirtyAfter -gt 0) {
            Write-AiRunLog -Module $module -Stage 'git檢查' -Status 'FAIL' -Message ("Warning：有 {0} 個未提交變更，下一輪先處理" -f $dirtyAfter)
        }

        if ($Once) { break }

        # 連續失敗退避（非額度問題）：暫停這個代理一段時間；有其他代理可用就立即改用，否則在選代理時等待
        if ($roundFailed) {
            $fails = 1 + [int]$agentFailures[$chosen.name]
            $agentFailures[$chosen.name] = $fails
            $backoffList = @($cfg.transientBackoffMinutes)
            if ($fails -ge $cfg.maxConsecutiveFailuresBeforeLongWait) {
                $wait = [int]$cfg.longWaitMinutes
            } else {
                $wait = [int]$backoffList[[Math]::Min($fails - 1, $backoffList.Count - 1)]
            }
            $agentWaitUntil[$chosen.name] = (Get-Date).AddMinutes($wait)
            $agentReason[$chosen.name] = 'fail'
            $others = @($agents | Where-Object { $_.name -ne $chosen.name -and (-not $agentWaitUntil.ContainsKey($_.name) -or $agentWaitUntil[$_.name] -le (Get-Date)) })
            $msg = '{0} 連續失敗 {1} 次，{2} 分鐘內暫停使用' -f $chosen.name, $fails, $wait
            if ($others.Count -gt 0) { $msg += "；改用 $($others[0].name)"; $announcedAgent = $others[0].name }
            Write-AiRunLog -Module $module -Stage 'runner' -Status 'WAIT' -Message $msg
        }
        Update-Status -State 'idle' -AgentName $chosen.name -Note ('輪與輪之間（上一輪 ' + $chosen.name + '）')
        Start-Sleep -Seconds $cfg.pauseBetweenRoundsSeconds
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
