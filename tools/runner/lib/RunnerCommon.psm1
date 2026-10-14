# MyERP AI Runner - 共用函式
# 相容 Windows PowerShell 5.1 與 PowerShell 7+。檔案以 UTF-8 with BOM 儲存（5.1 才能正確讀中文）。

Set-StrictMode -Version 2.0

$script:Utf8NoBom = New-Object System.Text.UTF8Encoding($false)

function Get-RepoRoot {
    # tools/runner/lib -> repo 根目錄
    return (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
}

function Get-RunnerPaths {
    $root = Get-RepoRoot
    $paths = @{
        Root        = $root
        StateJson   = Join-Path $root 'state.json'
        LogRoot     = Join-Path $root 'logs\ai-run'
        RunnerDir   = Join-Path $root 'logs\runner'
        StatusJson  = Join-Path $root 'logs\runner\runner-status.json'
        AgentChoice = Join-Path $root 'logs\runner\agent-choice.json'
        LastOutput  = Join-Path $root 'logs\runner\last-output.txt'
        StopFlag    = Join-Path $root 'tools\runner\STOP'
        Config      = Join-Path $root 'tools\runner\runner.config.json'
        PromptTpl   = Join-Path $root 'tools\runner\prompts\round-prompt.md'
    }
    if (-not (Test-Path $paths.RunnerDir)) { New-Item -ItemType Directory -Path $paths.RunnerDir -Force | Out-Null }
    return $paths
}

function Read-JsonFile([string]$Path) {
    if (-not (Test-Path $Path)) { return $null }
    try {
        $text = [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
        return $text | ConvertFrom-Json
    } catch {
        return $null
    }
}

function Write-JsonFile([string]$Path, $Object) {
    $json = $Object | ConvertTo-Json -Depth 10
    $tmp = "$Path.tmp"
    [System.IO.File]::WriteAllText($tmp, $json, $script:Utf8NoBom)
    Move-Item -Path $tmp -Destination $Path -Force
}

# ---------------------------------------------------------------- AI 執行日誌
# 格式：時間 | 模組 | 階段 | 狀態 | 說明
# 檔案：logs/ai-run/YYYY-MM/YYYY-MM-DD.log

$script:AllowedStatus = @('START', 'OK', 'FAIL', 'WAIT', 'SKIP', 'BLOCKED', 'DONE')

function Get-TodayLogPath {
    $p = Get-RunnerPaths
    $now = Get-Date
    $dir = Join-Path $p.LogRoot ($now.ToString('yyyy-MM'))
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    return Join-Path $dir ($now.ToString('yyyy-MM-dd') + '.log')
}

function Write-AiRunLog {
    param(
        [string]$Module = '-',
        [Parameter(Mandatory)][string]$Stage,
        [Parameter(Mandatory)][string]$Status,
        [Parameter(Mandatory)][string]$Message
    )
    $Status = $Status.ToUpperInvariant()
    if ($script:AllowedStatus -notcontains $Status) {
        throw "Status 只可為：$($script:AllowedStatus -join ', ')"
    }
    # 說明欄保持一行
    $msg = ($Message -replace '[\r\n]+', ' ').Trim()
    if ($msg.Length -gt 300) { $msg = $msg.Substring(0, 297) + '...' }
    $line = '{0} | {1,-5} | {2,-10} | {3,-7} | {4}' -f (Get-Date).ToString('yyyy-MM-dd HH:mm'), $Module, $Stage, $Status, $msg
    $path = Get-TodayLogPath
    # 簡單重試，避免 AI 與 Runner 同時寫入時衝突
    for ($i = 0; $i -lt 5; $i++) {
        try {
            [System.IO.File]::AppendAllText($path, $line + [Environment]::NewLine, $script:Utf8NoBom)
            return
        } catch {
            Start-Sleep -Milliseconds (200 * ($i + 1))
        }
    }
}

function Test-TodayHasStart {
    $path = Get-TodayLogPath
    if (-not (Test-Path $path)) { return $false }
    return [bool](Select-String -Path $path -Pattern '\| runner\s+\| START' -Quiet)
}

# ---------------------------------------------------------------- Runner 狀態（給 status.ps1）

function Set-RunnerStatus {
    param(
        [Parameter(Mandatory)][ValidateSet('running', 'waiting_quota', 'backoff', 'stopped', 'idle')][string]$State,
        [string]$Agent = '',
        [Nullable[datetime]]$WaitUntil = $null,
        [string]$Note = '',
        $Agents = $null
    )
    $p = Get-RunnerPaths
    $obj = [ordered]@{
        state     = $State
        agent     = $Agent
        pid       = $PID
        updatedAt = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
        waitUntil = $(if ($null -ne $WaitUntil) { ([datetime]$WaitUntil).ToString('yyyy-MM-dd HH:mm:ss') } else { $null })
        note      = $Note
        agents    = $Agents
    }
    Write-JsonFile -Path $p.StatusJson -Object $obj
}

# ---------------------------------------------------------------- 代理選擇（Use-Agent.ps1 寫入，不提交）

function Get-OrderedAgents {
    param([Parameter(Mandatory)]$Config)
    $agents = @($Config.agents | Where-Object { $_.enabled })
    $choice = Read-JsonFile (Get-RunnerPaths).AgentChoice
    if ($choice -and $choice.preferred -and $choice.preferred -ne 'auto') {
        $pref = @($agents | Where-Object { $_.name -eq $choice.preferred })
        if ($pref.Count -gt 0) {
            if ($choice.only) { $agents = $pref }
            else { $agents = @($pref) + @($agents | Where-Object { $_.name -ne $choice.preferred }) }
        }
    }
    return $agents
}

function Get-AgentChoiceText {
    $choice = Read-JsonFile (Get-RunnerPaths).AgentChoice
    if (-not $choice -or -not $choice.preferred -or $choice.preferred -eq 'auto') { return '自動（按 runner.config.json 次序）' }
    if ($choice.only) { return "只用 $($choice.preferred)（額度用完時等待，不切換）" }
    return "$($choice.preferred) 優先（額度用完時切換到其他代理）"
}

# ---------------------------------------------------------------- 額度偵測與 reset 時間解析

function Test-QuotaExhausted {
    param([string]$Output, [int]$ExitCode, [string[]]$Patterns, [int]$TailLines = 25)
    # 只掃描輸出尾段，避免 AI 開發 Rate limiting 功能時輸出的文字被誤判
    $lines = @($Output -split "`r?`n" | Where-Object { $_.Trim() -ne '' })
    # 正常結束而且輸出很長 = AI 完成了工作（總結中可能提到 rate limit 等字眼），不視為額度問題
    if ($ExitCode -eq 0 -and $lines.Count -gt 8) { return $false }
    $tail = ($lines | Select-Object -Last $TailLines) -join "`n"
    foreach ($pat in $Patterns) {
        if ($tail -match "(?i)$pat") { return $true }
    }
    return $false
}

function ConvertFrom-ResetText {
    <#
      嘗試從額度訊息解析 reset 時間。回傳 [datetime] 或 $null。
      支援：
        1. Unix epoch（例如 "...limit reached|1760000000"）
        2. "try again in 2 hours 13 minutes" / "in 45m" / "in 1h 5m"
        3. "resets 6pm" / "resets at 18:30" / "try again at 6:05 PM" / "resets 3am (Asia/Tokyo)"
    #>
    param([string]$Text)
    $now = Get-Date

    # 1. epoch
    $m = [regex]::Match($Text, '\|(\d{10})\b')
    if ($m.Success) {
        $epoch = [int64]$m.Groups[1].Value
        $t = ([DateTimeOffset]::FromUnixTimeSeconds($epoch)).LocalDateTime
        if ($t -gt $now -and ($t - $now).TotalDays -le 8) { return $t }
        return $null
    }

    # 2. 相對時間
    $m = [regex]::Match($Text, '(?i)(?:try again|available|resets?)\s+in\s+(?:(\d+)\s*(?:days?|d)\b)?[\s,]*(?:(\d+)\s*(?:hours?|hrs?|h)\b)?[\s,]*(?:(\d+)\s*(?:minutes?|mins?|m)\b)?[\s,]*(?:(\d+)\s*(?:seconds?|secs?|s)\b)?')
    if ($m.Success -and ($m.Groups[1].Success -or $m.Groups[2].Success -or $m.Groups[3].Success -or $m.Groups[4].Success)) {
        $d = 0; $h = 0; $mi = 0; $s = 0
        if ($m.Groups[1].Success) { $d = [int]$m.Groups[1].Value }
        if ($m.Groups[2].Success) { $h = [int]$m.Groups[2].Value }
        if ($m.Groups[3].Success) { $mi = [int]$m.Groups[3].Value }
        if ($m.Groups[4].Success) { $s = [int]$m.Groups[4].Value }
        return $now.AddDays($d).AddHours($h).AddMinutes($mi).AddSeconds($s)
    }

    # 3. 時刻（可選日期、年份、序數詞、時區），例如：
    #    resets 6pm / resets at 18:30 / try again at 9:58 AM / resets 3am (Asia/Tokyo)
    #    try again at Oct 14th, 2026 9:58 AM / resets Oct 10, 9am / try again on Oct 14 at 9:58 AM
    $datePart = '(?:(?<mon>Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-z]*\.?\s+(?<day>\d{1,2})(?:st|nd|rd|th)?,?\s*(?:(?<year>\d{4}),?\s*)?(?:at\s*)?)?'
    $lead = '(?i)(?:resets?|try again|available again)\s*(?:at|on)?\s*'
    $tzPart = '(?:\s*\((?<tz>[^)]+)\))?'
    $m = [regex]::Match($Text, $lead + $datePart + '(?<hour>\d{1,2})(?::(?<min>\d{2}))?\s*(?<ampm>[ap])\.?\s*m\.?' + $tzPart)
    if (-not $m.Success) {
        $m = [regex]::Match($Text, $lead + $datePart + '(?<hour>\d{1,2}):(?<min>\d{2})' + $tzPart)
    }
    if ($m.Success) {
        $hour = [int]$m.Groups['hour'].Value
        $min = 0
        if ($m.Groups['min'].Success -and $m.Groups['min'].Value) { $min = [int]$m.Groups['min'].Value }
        $ampm = ''
        if ($m.Groups['ampm'].Success) { $ampm = $m.Groups['ampm'].Value.ToLowerInvariant() }
        if ($ampm -eq 'p' -and $hour -lt 12) { $hour += 12 }
        if ($ampm -eq 'a' -and $hour -eq 12) { $hour = 0 }
        if ($hour -gt 23 -or $min -gt 59) { return $null }

        $baseDate = $now.Date
        $explicitDate = $false
        if ($m.Groups['mon'].Success -and $m.Groups['mon'].Value) {
            $year = $now.Year
            if ($m.Groups['year'].Success -and $m.Groups['year'].Value) { $year = [int]$m.Groups['year'].Value }
            try {
                $baseDate = [datetime]::ParseExact(('{0} {1} {2}' -f $m.Groups['mon'].Value.Substring(0, 3), $m.Groups['day'].Value, $year), 'MMM d yyyy', [Globalization.CultureInfo]::InvariantCulture)
                $explicitDate = $true
            } catch { $baseDate = $now.Date }
        }
        $candidate = $baseDate.AddHours($hour).AddMinutes($min)

        # 時區：盡量轉為本機時間；失敗則當作本機時間
        $tzName = ''
        if ($m.Groups['tz'].Success) { $tzName = $m.Groups['tz'].Value }
        if ($tzName) {
            # Windows PowerShell 5.1（.NET Framework）不認 IANA 名稱，先轉為 Windows 時區 ID
            $ianaMap = @{
                'Asia/Taipei' = 'Taipei Standard Time'; 'Asia/Hong_Kong' = 'China Standard Time'
                'Asia/Shanghai' = 'China Standard Time'; 'Asia/Macau' = 'China Standard Time'
                'Asia/Singapore' = 'Singapore Standard Time'; 'Asia/Tokyo' = 'Tokyo Standard Time'
                'Asia/Seoul' = 'Korea Standard Time'; 'UTC' = 'UTC'; 'Etc/UTC' = 'UTC'; 'GMT' = 'UTC'
                'Europe/London' = 'GMT Standard Time'; 'Europe/Paris' = 'Romance Standard Time'
                'America/New_York' = 'Eastern Standard Time'; 'America/Chicago' = 'Central Standard Time'
                'America/Denver' = 'Mountain Standard Time'; 'America/Los_Angeles' = 'Pacific Standard Time'
            }
            if ($ianaMap.ContainsKey($tzName)) { $tzName = $ianaMap[$tzName] }
            try {
                $tz = [TimeZoneInfo]::FindSystemTimeZoneById($tzName)
                $unspecified = [datetime]::SpecifyKind($candidate, [DateTimeKind]::Unspecified)
                $candidate = [TimeZoneInfo]::ConvertTime($unspecified, $tz, [TimeZoneInfo]::Local)
            } catch { }
        }
        if ($candidate -le $now -and -not $explicitDate) { $candidate = $candidate.AddDays(1) }
        if ($candidate -le $now) { return $null }
        # 合理性檢查：reset 不會超過 8 日後
        if (($candidate - $now).TotalDays -gt 8) { return $null }
        return $candidate
    }
    return $null
}

function Remove-PromptEcho {
    <#
      Codex 等 CLI 會把收到的指示原文印在輸出開頭。分析錯誤 / 額度前先移除這些行，
      否則指示中的「失敗」「limit」等字眼會被誤當成錯誤訊息。
    #>
    param([string]$Output, [string]$Prompt)
    if (-not $Output) { return '' }
    $promptLines = New-Object 'System.Collections.Generic.HashSet[string]'
    foreach ($l in ($Prompt -split "`r?`n")) {
        $t = $l.Trim()
        if ($t.Length -ge 6) { [void]$promptLines.Add($t) }
    }
    $kept = foreach ($l in ($Output -split "`r?`n")) {
        $t = $l.Trim()
        if ($t -eq '') { continue }
        if ($promptLines.Contains($t)) { continue }
        $l
    }
    return (@($kept) -join "`n")
}

function Get-ErrorSummary {
    <# 從（已移除指示原文的）輸出中，由後往前找最後一行錯誤訊息；找不到就用最後一行。 #>
    param([string]$Output)
    $lines = @($Output -split "`r?`n" | Where-Object { $_.Trim() -ne '' })
    if ($lines.Count -eq 0) { return '(沒有輸出)' }
    $pick = $null
    for ($i = $lines.Count - 1; $i -ge 0; $i--) {
        if ($lines[$i] -match '(?i)(error|exception|failed|fatal|denied|limit|quota|429|unauthori[sz]ed|not recognized|找不到|錯誤|失敗)') { $pick = $lines[$i]; break }
    }
    if (-not $pick) { $pick = $lines[$lines.Count - 1] }
    $pick = $pick.Trim()
    if ($pick.Length -gt 200) { $pick = $pick.Substring(0, 197) + '...' }
    return $pick
}

function Start-RunnerProcess {
    <# 在背景（無視窗）啟動 Start-Runner.ps1。#>
    param([string]$Agent = '', [switch]$Only)
    $root = Get-RepoRoot
    $runner = Join-Path $root 'tools\runner\Start-Runner.ps1'
    $shell = Get-Command pwsh -ErrorAction SilentlyContinue
    if ($shell) { $exe = $shell.Source } else { $exe = (Get-Command powershell).Source }
    $argList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-WindowStyle', 'Hidden', '-File', ('"{0}"' -f $runner))
    if ($Agent) { $argList += @('-Agent', $Agent) }
    if ($Only) { $argList += '-Only' }
    Start-Process -FilePath $exe -ArgumentList $argList -WorkingDirectory $root -WindowStyle Hidden | Out-Null
}

function Test-RunnerAlive {
    $st = Read-JsonFile (Get-RunnerPaths).StatusJson
    if (-not $st -or -not $st.pid -or $st.state -eq 'stopped') { return $false }
    $proc = Get-Process -Id $st.pid -ErrorAction SilentlyContinue
    return [bool]($proc -and $proc.ProcessName -match '^(powershell|pwsh)$')
}

# ---------------------------------------------------------------- 執行 AI CLI

function Resolve-AgentCommand([string]$Command) {
    # 只接受可執行檔（.exe / .cmd / .bat），避免 npm 產生的 .ps1 shim 無法由 cmd.exe 執行
    $cmd = Get-Command $Command -CommandType Application -ErrorAction SilentlyContinue |
        Where-Object { $_.Source -match '\.(exe|cmd|bat)$' } | Select-Object -First 1
    if (-not $cmd) { return $null }
    return $cmd.Source
}

function Invoke-AgentRound {
    <#
      以 cmd.exe 執行 AI CLI：prompt 由 stdin 輸入，stdout/stderr 寫入暫存檔。
      回傳 @{ ExitCode; Output; TimedOut; Missing }
    #>
    param(
        [Parameter(Mandatory)]$Agent,
        [Parameter(Mandatory)][string]$Prompt,
        [Parameter(Mandatory)][int]$TimeoutMinutes,
        [Parameter(Mandatory)][string]$WorkingDirectory
    )
    $exe = Resolve-AgentCommand $Agent.command
    if (-not $exe) {
        return @{ ExitCode = -1; Output = "找不到指令 $($Agent.command)"; TimedOut = $false; Missing = $true }
    }

    $tmpDir = Join-Path $env:TEMP 'myerp-runner'
    if (-not (Test-Path $tmpDir)) { New-Item -ItemType Directory -Path $tmpDir -Force | Out-Null }
    $promptFile = Join-Path $tmpDir 'prompt.txt'
    $outFile = Join-Path $tmpDir 'output.txt'
    [System.IO.File]::WriteAllText($promptFile, $Prompt, $script:Utf8NoBom)
    if (Test-Path $outFile) { Remove-Item $outFile -Force }

    $argText = ($Agent.args | ForEach-Object {
            if ($_ -match '\s') { '"' + $_ + '"' } else { $_ }
        }) -join ' '
    # chcp 65001：確保中文輸出為 UTF-8
    $cmdLine = '/d /s /c "chcp 65001 >nul & "{0}" {1} < "{2}" > "{3}" 2>&1"' -f $exe, $argText, $promptFile, $outFile

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $env:ComSpec
    $psi.Arguments = $cmdLine
    $psi.WorkingDirectory = $WorkingDirectory
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $proc = [System.Diagnostics.Process]::Start($psi)

    $timedOut = -not $proc.WaitForExit($TimeoutMinutes * 60 * 1000)
    if ($timedOut) {
        $ErrorActionPreference = 'Continue'
        # 終止整個程序樹
        & taskkill.exe /T /F /PID $proc.Id 2>&1 | Out-Null
        Start-Sleep -Seconds 3
    }
    $exitCode = if ($timedOut) { -2 } else { $proc.ExitCode }

    $output = ''
    if (Test-Path $outFile) {
        try { $output = [System.IO.File]::ReadAllText($outFile, [System.Text.Encoding]::UTF8) } catch { $output = '' }
    }
    Remove-Item $promptFile -Force -ErrorAction SilentlyContinue
    return @{ ExitCode = $exitCode; Output = $output; TimedOut = $timedOut; Missing = $false; OutFile = $outFile }
}

# ---------------------------------------------------------------- Git

function Get-GitDirtyCount([string]$Root) {
    $ErrorActionPreference = 'Continue'   # git 的 stderr 警告不可令 Runner 中止
    Push-Location $Root
    try {
        $lines = & git status --porcelain 2>$null
        if ($LASTEXITCODE -ne 0) { return -1 }
        return @($lines | Where-Object { $_ }).Count
    } finally { Pop-Location }
}

Export-ModuleMember -Function *
