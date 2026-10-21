<#
.SYNOPSIS
  以易讀的表格顯示 MyERP 後端日誌（Serilog 的 JSON 格式），預設只列出錯誤。

.EXAMPLE
  .\tools\dev\Show-Log.ps1                     # 今天最新日誌的錯誤（略過自動測試產生的記錄）
  .\tools\dev\Show-Log.ps1 -Level Warning      # 錯誤加警告
  .\tools\dev\Show-Log.ps1 -Last 50            # 最後 50 筆
  .\tools\dev\Show-Log.ps1 -TraceId 0HN7...    # 某次請求的全部記錄與完整錯誤內容
  .\tools\dev\Show-Log.ps1 -Summary            # 按訊息統計次數
  .\tools\dev\Show-Log.ps1 -IncludeTests       # 連同自動測試產生的記錄
#>
param(
    [string]$Path,
    [ValidateSet('Error', 'Warning', 'Information', 'All')][string]$Level = 'Error',
    [int]$Last = 30,
    [string]$TraceId,
    [switch]$Summary,
    [switch]$IncludeTests
)
try { [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false) } catch { }

if (-not $Path) {
    $root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
    $dir = Join-Path $root 'src\backend\MyERP.Api\logs'
    $latest = Get-ChildItem $dir -Filter '*.log' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $latest) { Write-Host "找不到日誌檔：$dir"; return }
    $Path = $latest.FullName
}
Write-Host "日誌檔：$Path" -ForegroundColor Cyan

$rank = @{ 'Verbose' = 0; 'Debug' = 1; 'Information' = 2; 'Warning' = 3; 'Error' = 4; 'Fatal' = 5 }
$minRank = @{ 'Error' = 4; 'Warning' = 3; 'Information' = 2; 'All' = 0 }[$Level]

function Format-Message($r) {
    $mt = [string]$r.'@mt'
    return [regex]::Replace($mt, '\{@?(\w+)(?::[^}]*)?\}', {
            param($m)
            $name = $m.Groups[1].Value
            $p = $r.PSObject.Properties[$name]
            if ($p) { if ($p.Value -is [string]) { $p.Value } else { ($p.Value | ConvertTo-Json -Compress -Depth 3) } } else { $m.Value }
        })
}

$rows = New-Object System.Collections.Generic.List[object]
$bad = 0
foreach ($line in [System.IO.File]::ReadLines($Path, [System.Text.Encoding]::UTF8)) {
    if (-not $line.Trim()) { continue }
    try { $r = $line | ConvertFrom-Json } catch { $bad++; continue }
    $lv = 'Information'
    if ($r.PSObject.Properties['@l']) { $lv = [string]$r.'@l' }
    $path = ''
    if ($r.PSObject.Properties['RequestPath']) { $path = [string]$r.RequestPath }
    $msg = Format-Message $r
    $isTest = ($path -like '/__test*') -or ($msg -match '/__test/')
    if ($isTest -and -not $IncludeTests) { continue }
    $trace = ''
    if ($r.PSObject.Properties['@tr']) { $trace = [string]$r.'@tr' } elseif ($r.PSObject.Properties['TraceId']) { $trace = [string]$r.TraceId }
    $ex = ''
    if ($r.PSObject.Properties['@x']) { $ex = [string]$r.'@x' }
    $src = ''
    if ($r.PSObject.Properties['SourceContext']) { $src = ([string]$r.SourceContext).Split('.')[-1] }
    $time = [DateTimeOffset]::Parse([string]$r.'@t', [Globalization.CultureInfo]::InvariantCulture).LocalDateTime
    $rows.Add([pscustomobject]@{ Time = $time; Level = $lv; Rank = [int]$rank[$lv]; Source = $src; Message = $msg; TraceId = $trace; Exception = $ex })
}
if ($bad -gt 0) { Write-Host "（有 $bad 行不是 JSON，已略過）" -ForegroundColor DarkGray }

$total = $rows.Count
$errs = @($rows | Where-Object { $_.Rank -ge 4 }).Count
$warns = @($rows | Where-Object { $_.Level -eq 'Warning' }).Count
Write-Host ("共 {0} 筆：錯誤 {1}、警告 {2}{3}" -f $total, $errs, $warns, $(if ($IncludeTests) { '' } else { '（已略過自動測試記錄，加 -IncludeTests 可顯示）' }))
Write-Host ''

if ($TraceId) {
    $hit = @($rows | Where-Object { $_.TraceId -eq $TraceId -or $_.Message -match [regex]::Escape($TraceId) })
    if ($hit.Count -eq 0) { Write-Host "找不到 TraceId $TraceId（如屬自動測試記錄，加 -IncludeTests）"; return }
    foreach ($h in $hit) {
        $color = 'Gray'; if ($h.Rank -ge 4) { $color = 'Red' } elseif ($h.Level -eq 'Warning') { $color = 'Yellow' }
        Write-Host ("{0:yyyy-MM-dd HH:mm:ss}  {1,-11} {2}" -f $h.Time, $h.Level, $h.Source) -ForegroundColor $color
        Write-Host ("  " + $h.Message)
        if ($h.Exception) { Write-Host (($h.Exception -replace "`r", '') -replace '(?m)^', '    ') -ForegroundColor DarkRed }
    }
    return
}

$sel = @($rows | Where-Object { $_.Rank -ge $minRank })
if ($sel.Count -eq 0) { Write-Host '沒有符合條件的記錄。' -ForegroundColor Green; return }

if ($Summary) {
    $sel | Group-Object Level, Source, { if ($_.Message.Length -gt 90) { $_.Message.Substring(0, 90) + '…' } else { $_.Message } } |
        Sort-Object Count -Descending | Select-Object -First $Last |
        ForEach-Object {
            $f = $_.Group[0]
            [pscustomobject]@{ 次數 = $_.Count; 等級 = $f.Level; 最後時間 = ($_.Group | Sort-Object Time | Select-Object -Last 1).Time.ToString('HH:mm:ss'); 來源 = $f.Source; 訊息 = $_.Name.Split(',', 3)[2].Trim() }
        } | Format-Table -AutoSize -Wrap | Out-Host
    return
}

$sel | Select-Object -Last $Last | ForEach-Object {
    $exLine = ''
    if ($_.Exception) { $exLine = ($_.Exception -split "`r?`n")[0] }
    [pscustomobject]@{
        時間    = $_.Time.ToString('HH:mm:ss')
        等級    = $_.Level
        來源    = $_.Source
        訊息    = $(if ($_.Message.Length -gt 140) { $_.Message.Substring(0, 140) + '…' } else { $_.Message })
        錯誤    = $exLine
        TraceId = $_.TraceId
    }
} | Format-Table -AutoSize -Wrap | Out-Host
Write-Host '看某一筆的完整錯誤：.\tools\dev\Show-Log.ps1 -TraceId <TraceId>' -ForegroundColor DarkGray
