<#
.SYNOPSIS
  以易讀方式顯示 MyERP 後端日誌（Serilog JSON 格式）。預設：按問題統計，附中文說明，略過自動測試產生的記錄。

.EXAMPLE
  .\tools\dev\Show-Log.ps1                    # 錯誤統計（最常用）
  .\tools\dev\Show-Log.ps1 -Level Warning     # 錯誤 + 警告統計
  .\tools\dev\Show-Log.ps1 -Detail            # 逐筆列出最近 20 筆錯誤
  .\tools\dev\Show-Log.ps1 -TraceId <ID>      # 某次請求的全部記錄與完整錯誤
  .\tools\dev\Show-Log.ps1 -IncludeTests      # 連同自動測試產生的記錄
  .\tools\dev\Show-Log.ps1 -Date 2026-10-20   # 指定日期的日誌
#>
param(
    [string]$Path,
    [string]$Date,
    [ValidateSet('Error', 'Warning', 'Information')][string]$Level = 'Error',
    [switch]$Detail,
    [int]$Last = 20,
    [string]$TraceId,
    [switch]$IncludeTests
)
try { [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false) } catch { }

# ---------------------------------------------------------------- 已知訊息的中文說明
# 依次比對 Pattern（正則，比對訊息 + 錯誤內容），第一個符合的生效
$known = @(
    @{ Pattern = 'Database is unreachable|SqlException.*(network|server was not found|connection)'; Title = '連不上資料庫'; Hint = '確認 SQL Server（MSSQLSERVER2022）服務已啟動；如你當時沒有遇到問題，多數是自動測試模擬斷線' }
    @{ Pattern = 'RedisConnectionException|Redis.*(unreachable|timeout|refused)'; Title = '連不上 Redis'; Hint = '確認 Redis 視窗仍在運行（Test-NetConnection 127.0.0.1 -Port 6379）' }
    @{ Pattern = 'DbUpdateConcurrencyException'; Title = '資料被同時修改（版本衝突）'; Hint = '兩個人同時修改同一筆資料；屬正常保護，畫面應提示重新載入' }
    @{ Pattern = 'SqlException'; Title = '資料庫執行錯誤'; Hint = '用 -TraceId 看完整內容，記錄為 CR 交給 AI' }
    @{ Pattern = 'Unhandled exception'; Title = '程式未處理的錯誤'; Hint = '這是程式缺陷：用 -TraceId 看完整內容，記錄為 CR 交給 AI' }
    @{ Pattern = 'Hosting failed to start'; Title = '後端啟動失敗'; Hint = '看「原因」一行；如發生在你啟動後端時，按原因修正設定' }
    @{ Pattern = 'responded 5\d\d'; Title = 'API 回應伺服器錯誤（5xx）'; Hint = '用 -TraceId 查看同一請求的其他記錄' }
    @{ Pattern = 'Login failed'; Title = '登入失敗'; Hint = '帳號或密碼錯誤；連續 5 次會鎖定 15 分鐘' }
    @{ Pattern = 'master code used'; Title = '使用萬用碼登入'; Hint = '開發環境的正常記錄' }
    @{ Pattern = 'responded \d+ in \d+'; Title = 'API 回應緩慢（超過 1 秒）'; Hint = '偶爾出現屬正常；同一 API 經常出現，記錄為 CR 請 AI 優化' }
    @{ Pattern = 'cannot be executed in a transaction'; Title = '資料庫 migration 提示'; Hint = '建立資料庫時的正常提示，可忽略' }
)

# 自動測試故意製造的錯誤（預設不顯示）
$testSignatures = @(
    '/__test', '/api/v1/tst/', 'IntegrationTests', 'Mvc\.Testing',
    'startup refused', 'MasterCode is only allowed', 'MasterCode must be',
    'SigningKey must be configured', 'Database is not configured'
)
$testRegex = '(' + ($testSignatures -join '|') + ')'

# ---------------------------------------------------------------- 找日誌檔
if (-not $Path) {
    $root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
    $dir = Join-Path $root 'src\backend\MyERP.Api\logs'
    if ($Date) {
        $Path = Join-Path $dir ('app-{0}.log' -f ($Date -replace '-', ''))
    } else {
        $f = Get-ChildItem $dir -Filter '*.log' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if ($f) { $Path = $f.FullName }
    }
}
if (-not $Path -or -not (Test-Path $Path)) { Write-Host "找不到日誌檔：$Path" -ForegroundColor Red; return }

# 後端運行中會一直開着檔案寫入，所以用共用模式讀取
try {
    $fs = New-Object System.IO.FileStream($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, ([System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete))
    $reader = New-Object System.IO.StreamReader($fs, [System.Text.Encoding]::UTF8)
    $lines = New-Object System.Collections.Generic.List[string]
    while ($null -ne ($l = $reader.ReadLine())) { $lines.Add($l) }
    $reader.Close(); $fs.Close()
} catch {
    Write-Host "無法讀取日誌檔：$($_.Exception.Message)" -ForegroundColor Red
    return
}

# ---------------------------------------------------------------- 解析
$rank = @{ 'Verbose' = 0; 'Debug' = 1; 'Information' = 2; 'Warning' = 3; 'Error' = 4; 'Fatal' = 5 }
$minRank = $rank[$Level]
$levelZh = @{ 'Error' = '錯誤'; 'Fatal' = '嚴重'; 'Warning' = '警告'; 'Information' = '資訊'; 'Debug' = '除錯'; 'Verbose' = '詳細' }

function Format-Message($r) {
    [regex]::Replace([string]$r.'@mt', '\{@?(\w+)(?::[^}]*)?\}', {
            param($m)
            $p = $r.PSObject.Properties[$m.Groups[1].Value]
            if ($p) { [string]$p.Value } else { $m.Value }
        })
}
function Get-Reason([string]$ex) {
    if (-not $ex) { return '' }
    $first = ($ex -split "`r?`n")[0]
    # 「Namespace.XxxException: 訊息」→ 只取訊息
    if ($first -match '^[\w\.`]+(Exception|Error)\w*:\s*(.+)$') { return $Matches[2].Trim() }
    return $first.Trim()
}
function Limit([string]$t, [int]$n = 200) { if ($t.Length -gt $n) { return $t.Substring(0, $n) + '…' } return $t }
function Get-Known([string]$text) {
    foreach ($k in $known) { if ($text -match $k.Pattern) { return $k } }
    return $null
}

$all = New-Object System.Collections.Generic.List[object]
$hiddenTests = 0
foreach ($line in $lines) {
    if (-not $line.Trim()) { continue }
    try { $r = $line | ConvertFrom-Json } catch { continue }
    $lv = 'Information'; if ($r.PSObject.Properties['@l']) { $lv = [string]$r.'@l' }
    $msg = Format-Message $r
    $ex = ''; if ($r.PSObject.Properties['@x']) { $ex = [string]$r.'@x' }
    $reqPath = ''; if ($r.PSObject.Properties['RequestPath']) { $reqPath = [string]$r.RequestPath }
    $trace = ''; if ($r.PSObject.Properties['@tr']) { $trace = [string]$r.'@tr' } elseif ($r.PSObject.Properties['TraceId']) { $trace = [string]$r.TraceId }
    $isTest = ("$msg $reqPath $ex" -match $testRegex)
    if ($isTest -and -not $IncludeTests -and -not $TraceId) { if ($rank[$lv] -ge $minRank) { $hiddenTests++ }; continue }
    $src = ''; if ($r.PSObject.Properties['SourceContext']) { $src = ([string]$r.SourceContext).Split('.')[-1] }
    $all.Add([pscustomobject]@{
            Time = [DateTimeOffset]::Parse([string]$r.'@t', [Globalization.CultureInfo]::InvariantCulture).LocalDateTime
            Level = $lv; Rank = [int]$rank[$lv]; Source = $src; Message = $msg; Reason = (Get-Reason $ex)
            Exception = $ex; TraceId = $trace; Known = (Get-Known "$msg $ex")
        })
}

Write-Host ''
Write-Host "日誌檔：$Path" -ForegroundColor Cyan

# ---------------------------------------------------------------- 按 TraceId
if ($TraceId) {
    $hit = @($all | Where-Object { $_.TraceId -eq $TraceId -or $_.Message -match [regex]::Escape($TraceId) })
    if ($hit.Count -eq 0) { Write-Host "找不到 TraceId：$TraceId"; return }
    foreach ($h in $hit) {
        $c = 'Gray'; if ($h.Rank -ge 4) { $c = 'Red' } elseif ($h.Rank -eq 3) { $c = 'Yellow' }
        Write-Host ''
        Write-Host ('{0:HH:mm:ss}  {1}  {2}' -f $h.Time, $levelZh[$h.Level], $h.Source) -ForegroundColor $c
        Write-Host ('  ' + $h.Message)
        if ($h.Exception) { Write-Host ((($h.Exception -replace "`r", '') -split "`n" | Select-Object -First 25 | ForEach-Object { '    ' + $_ }) -join "`n") -ForegroundColor DarkGray }
    }
    return
}

$sel = @($all | Where-Object { $_.Rank -ge $minRank })
$errCount = @($sel | Where-Object { $_.Rank -ge 4 }).Count
$warnCount = @($sel | Where-Object { $_.Rank -eq 3 }).Count
$summaryLine = '錯誤 {0} 筆' -f $errCount
if ($minRank -le 3) { $summaryLine += '、警告 {0} 筆' -f $warnCount }
if (-not $IncludeTests -and $hiddenTests -gt 0) { $summaryLine += "（另有 $hiddenTests 筆屬自動測試故意製造，已略過；加 -IncludeTests 顯示）" }
Write-Host $summaryLine
if ($sel.Count -eq 0) { Write-Host ''; Write-Host '沒有需要處理的記錄。' -ForegroundColor Green; return }

# ---------------------------------------------------------------- 逐筆
if ($Detail) {
    foreach ($e in ($sel | Select-Object -Last $Last)) {
        $c = 'Red'; if ($e.Rank -eq 3) { $c = 'Yellow' }
        $title = Limit $e.Message 120; if ($e.Known) { $title = $e.Known.Title }
        Write-Host ''
        Write-Host ('[{0:MM-dd HH:mm:ss}] {1}：{2}' -f $e.Time, $levelZh[$e.Level], $title) -ForegroundColor $c
        if ($e.Known) { Write-Host ('  訊息：' + (Limit $e.Message)) }
        if ($e.Reason) { Write-Host ('  原因：' + $e.Reason) }
        if ($e.Known) { Write-Host ('  建議：' + $e.Known.Hint) -ForegroundColor DarkCyan }
        if ($e.TraceId) { Write-Host ('  TraceId：' + $e.TraceId) -ForegroundColor DarkGray }
    }
    return
}

# ---------------------------------------------------------------- 統計（預設）
# 把數字換成 # 再分組，避免「1008ms」「1010ms」被當成不同訊息
$groups = $sel | Group-Object { ('{0}|{1}|{2}' -f $_.Level, ($_.Message -replace '\d+(\.\d+)?', '#'), ($_.Reason -replace '\d+(\.\d+)?', '#')) } |
    Sort-Object @{ e = { $_.Group[0].Rank }; Descending = $true }, @{ e = { $_.Count }; Descending = $true }
$i = 0
foreach ($g in $groups) {
    $i++
    $f = $g.Group | Sort-Object Time | Select-Object -Last 1
    $first = $g.Group | Sort-Object Time | Select-Object -First 1
    $c = 'Red'; if ($f.Rank -eq 3) { $c = 'Yellow' } elseif ($f.Rank -lt 3) { $c = 'Gray' }
    $title = Limit $f.Message 120; if ($f.Known) { $title = $f.Known.Title }
    Write-Host ''
    Write-Host ('{0}. {1}：{2}　× {3} 次' -f $i, $levelZh[$f.Level], $title, $g.Count) -ForegroundColor $c
    Write-Host ('   時間：{0:MM-dd HH:mm} 至 {1:MM-dd HH:mm}' -f $first.Time, $f.Time)
    if ($f.Known) { Write-Host ('   訊息：' + (Limit $f.Message)) }
    if ($f.Reason) { Write-Host ('   原因：' + $f.Reason) }
    if ($f.Known) { Write-Host ('   建議：' + $f.Known.Hint) -ForegroundColor DarkCyan }
    if ($f.TraceId) { Write-Host ('   最近一次 TraceId：' + $f.TraceId) -ForegroundColor DarkGray }
}
Write-Host ''
Write-Host '逐筆查看：加 -Detail；看某次的完整錯誤：-TraceId <ID>' -ForegroundColor DarkGray
