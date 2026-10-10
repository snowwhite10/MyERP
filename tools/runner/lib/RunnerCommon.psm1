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
        [string]$Note = ''
    )
    $p = Get-RunnerPaths
    $obj = [ordered]@{
        state     = $State
        agent     = $Agent
        pid       = $PID
        updatedAt = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
        waitUntil = $(if ($null -ne $WaitUntil) { ([datetime]$WaitUntil).ToString('yyyy-MM-dd HH:mm:ss') } else { $null })
        note      = $Note
    }
    Write-JsonFile -Path $p.StatusJson -Object $obj
}

# ---------------------------------------------------------------- 額度偵測與 reset 時間解析

function Test-QuotaExhausted {
    param([string]$Output, [int]$ExitCode, [string[]]$Patterns, [int]$TailLines = 25)
    # 只掃描輸出尾段，避免 AI 開發 Rate limiting 功能時輸出的文字被誤判
    $lines = $Output -split "`r?`n"
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
        return ([DateTimeOffset]::FromUnixTimeSeconds($epoch)).LocalDateTime
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

    # 3. 時刻（可選日期、可選時區）
    $m = [regex]::Match($Text, '(?i)(?:resets?|try again|available again)\s*(?:at|on)?\s*(?:([A-Za-z]{3,9})\s+(\d{1,2}),?\s*(?:at\s*)?)?(\d{1,2})(?::(\d{2}))?\s*([ap])\.?\s*m\.?(?:\s*\(([^)]+)\))?')
    if (-not $m.Success) {
        $m = [regex]::Match($Text, '(?i)(?:resets?|try again|available again)\s*(?:at|on)?\s*(?:([A-Za-z]{3,9})\s+(\d{1,2}),?\s*(?:at\s*)?)?(\d{1,2}):(\d{2})()(?:\s*\(([^)]+)\))?')
    }
    if ($m.Success) {
        $hour = [int]$m.Groups[3].Value
        $min = 0
        if ($m.Groups[4].Success -and $m.Groups[4].Value) { $min = [int]$m.Groups[4].Value }
        $ampm = $m.Groups[5].Value.ToLowerInvariant()
        if ($ampm -eq 'p' -and $hour -lt 12) { $hour += 12 }
        if ($ampm -eq 'a' -and $hour -eq 12) { $hour = 0 }
        if ($hour -gt 23 -or $min -gt 59) { return $null }

        $baseDate = $now.Date
        if ($m.Groups[1].Success -and $m.Groups[1].Value) {
            try {
                $baseDate = [datetime]::ParseExact(('{0} {1} {2}' -f $m.Groups[1].Value.Substring(0, 3), $m.Groups[2].Value, $now.Year), 'MMM d yyyy', [Globalization.CultureInfo]::InvariantCulture)
            } catch { $baseDate = $now.Date }
        }
        $candidate = $baseDate.AddHours($hour).AddMinutes($min)

        # 時區：盡量轉為本機時間；失敗則當作本機時間
        $tzName = $m.Groups[6].Value
        if ($tzName) {
            try {
                $tz = [TimeZoneInfo]::FindSystemTimeZoneById($tzName)
                $unspecified = [datetime]::SpecifyKind($candidate, [DateTimeKind]::Unspecified)
                $candidate = [TimeZoneInfo]::ConvertTime($unspecified, $tz, [TimeZoneInfo]::Local)
            } catch { }
        }
        if ($candidate -le $now) { $candidate = $candidate.AddDays(1) }
        # 合理性檢查：reset 不會超過 8 日後
        if (($candidate - $now).TotalDays -gt 8) { return $null }
        return $candidate
    }
    return $null
}

function Get-FirstErrorLine {
    param([string]$Output)
    $lines = $Output -split "`r?`n" | Where-Object { $_.Trim() -ne '' }
    $err = $lines | Where-Object { $_ -match '(?i)(error|exception|failed|fatal|denied|not recognized|找不到|錯誤|失敗)' } | Select-Object -First 1
    if ($err) { return $err.Trim() }
    $last = $lines | Select-Object -Last 1
    if ($last) { return $last.Trim() }
    return '(沒有輸出)'
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
