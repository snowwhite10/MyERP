<#
.SYNOPSIS
  檢查 Windows VM 是否已具備 MyERP 自動開發所需的全部工具。只檢查，不安裝。
#>
param(
    [string]$SqlInstance = 'localhost\MSSQLSERVER2022',
    [int]$RedisPort = 6379
)
try { [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false) } catch { }
$results = New-Object System.Collections.Generic.List[object]

function Add-Result($Item, $Ok, $Detail, $Fix) {
    $results.Add([pscustomobject]@{ 項目 = $Item; 結果 = $(if ($Ok) { 'PASS' } else { 'FAIL' }); 詳情 = $Detail; 修正 = $(if ($Ok) { '' } else { $Fix }) })
}
function Try-Run([scriptblock]$Block) { try { return (& $Block 2>&1 | Out-String).Trim() } catch { return $null } }

# PowerShell
Add-Result 'PowerShell 7' ([bool](Get-Command pwsh -ErrorAction SilentlyContinue)) (Try-Run { pwsh -v }) 'winget install Microsoft.PowerShell'

# Git
$git = Try-Run { git --version }
Add-Result 'Git for Windows' ([bool]$git) $git 'winget install Git.Git（Claude Code 在 Windows 亦需要 Git Bash）'
$gname = Try-Run { git config --global user.name }
Add-Result 'git user.name / email' ([bool]$gname) $gname 'git config --global user.name "..." ; git config --global user.email "..."'
# Git for Windows 通常把 credential.helper 設在 system 層級，所以不限 --global
$gcm = Try-Run { git config --get credential.helper }
Add-Result 'Git 憑證（GCM）' ([bool]$gcm) $gcm '安裝 Git 時選 Git Credential Manager，並手動 push 一次完成登入'

# .NET
$sdks = Try-Run { dotnet --list-sdks }
Add-Result '.NET SDK 10（LTS）' ($sdks -match '(^|\n)10\.') $sdks 'winget install Microsoft.DotNet.SDK.10'
$ef = Try-Run { dotnet ef --version }
if (-not ($ef -match '(?m)^\s*\d+\.\d+\.\d+')) { $ef = '未安裝' }
Add-Result 'dotnet-ef' ([bool]($ef -match '\d+\.\d+')) $ef 'dotnet tool install --global dotnet-ef'
$cert = Try-Run { dotnet dev-certs https --check --trust }
Add-Result 'HTTPS 開發憑證' ($LASTEXITCODE -eq 0) $cert 'dotnet dev-certs https --trust'

# Node / Angular
$node = Try-Run { node -v }
Add-Result 'Node.js LTS' ([bool]($node -match '^v(2[2-9]|[3-9]\d)')) $node 'winget install OpenJS.NodeJS.LTS（建議 v22 以上）'
$npm = Try-Run { npm -v }
Add-Result 'npm' ([bool]$npm) $npm '隨 Node.js 安裝'

# SQL Server
$sqlcmd = Get-Command sqlcmd -ErrorAction SilentlyContinue
if ($sqlcmd) {
    $q = Try-Run { sqlcmd -S $SqlInstance -E -C -b -h -1 -Q "SET NOCOUNT ON; SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(50))" }
    Add-Result "SQL Server（$SqlInstance，Windows 驗證）" ($LASTEXITCODE -eq 0 -and $q -match '^16\.') $q '確認實例名稱、SQL Server Browser 服務已啟動、目前 Windows 帳號有 sysadmin 或 dbcreator 權限'
} else {
    Add-Result 'sqlcmd' $false '' 'winget install Microsoft.Sqlcmd'
}

# Redis / Memurai
$redisOk = $false
try {
    $c = New-Object System.Net.Sockets.TcpClient
    $redisOk = $c.ConnectAsync('127.0.0.1', $RedisPort).Wait(2000)
    $c.Close()
} catch { }
Add-Result "Redis 相容服務（port $RedisPort）" $redisOk '' '安裝 Memurai Developer 並設為自動啟動'

# AI CLI
$claude = Try-Run { claude --version }
Add-Result 'Claude Code CLI' ([bool]($claude -match '\d+\.\d+')) $claude 'npm install -g @anthropic-ai/claude-code，然後執行 claude 完成登入一次'
$codex = Try-Run { codex --version }
Add-Result 'Codex CLI（可選）' ([bool]($codex -match '\d+\.\d+')) $codex 'npm install -g @openai/codex，然後 codex login；不使用可在 runner.config.json 設 enabled=false'

# 電源
$sleep = Try-Run { powercfg /query SCHEME_CURRENT SUB_SLEEP STANDBYIDLE }
# powercfg 輸出會按系統語言翻譯，所以不比對文字：最後兩個十六進位值依次為 AC、DC 設定
$hex = @(($sleep -split "`r?`n") | ForEach-Object { if ($_ -match '[:：]\s*(0x[0-9a-fA-F]{8})\s*$') { $Matches[1] } })
$neverSleep = ($hex.Count -ge 2 -and $hex[$hex.Count - 2] -eq '0x00000000')
Add-Result 'VM 不休眠' $neverSleep '' 'powercfg /change standby-timeout-ac 0 ; powercfg /change hibernate-timeout-ac 0'

$results | Format-Table -AutoSize -Wrap
$fail = @($results | Where-Object { $_.結果 -eq 'FAIL' }).Count
if ($fail -eq 0) { Write-Host '全部通過，可以執行 Init-Workspace.ps1。' -ForegroundColor Green }
else { Write-Host "$fail 項未通過，請按「修正」欄處理後再執行（Codex 為可選）。" -ForegroundColor Yellow }
