<#
.SYNOPSIS
    檢查本機依賴，使用 README 的固定埠啟動 MyERP 開發環境。
#>
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$apiProject = Join-Path $repoRoot 'src/backend/MyERP.Api'
$frontend = Join-Path $repoRoot 'src/frontend'
$server = 'localhost\MSSQLSERVER2022'

function Stop-WithHelp([string]$message, [string]$repair) {
    Write-Host "$message`n修正方式：$repair" -ForegroundColor Red
    exit 1
}

function Test-Port([int]$port) {
    $listeners = @(Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue)
    if ($listeners.Count -eq 0) { return }
    $ownerId = $listeners[0].OwningProcess
    $owner = Get-Process -Id $ownerId -ErrorAction SilentlyContinue
    $name = if ($owner) { $owner.ProcessName } else { '未知程序' }
    Stop-WithHelp "預設埠 $port 已被 $name (PID $ownerId) 佔用。" "先停止該程序，再執行 .\tools\dev\Start-Dev.ps1；不會自動改埠。"
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Stop-WithHelp '找不到 .NET SDK。' '安裝 global.json 指定的 .NET 10 SDK。'
}
if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) {
    Stop-WithHelp '找不到 sqlcmd，無法檢查 SQL Server。' '安裝 SQL Server 命令列工具並把 sqlcmd 加入 PATH。'
}
if (-not (Test-Path (Join-Path $frontend 'node_modules'))) {
    Stop-WithHelp '前端套件尚未安裝。' '& .\tools\scripts\Invoke-Frontend.ps1 npm ci'
}

foreach ($port in @(4200, 7180, 5180)) { Test-Port $port }

& sqlcmd -S $server -E -d master -l 5 -Q 'SET NOCOUNT ON; SELECT 1' -h -1 -W -b *> $null
if ($LASTEXITCODE -ne 0) {
    Stop-WithHelp "無法以 Windows 驗證連線 SQL Server $server。" '啟動 MSSQL$MSSQLSERVER2022，並確認目前 Windows 帳號有 SQL Server 存取權。'
}
& sqlcmd -S $server -E -d myerp -l 5 -Q 'SET NOCOUNT ON; SELECT COUNT(*) FROM dbo.sys_user' -h -1 -W -b *> $null
if ($LASTEXITCODE -ne 0) {
    Stop-WithHelp 'myerp 資料庫或基線資料表不存在。' 'dotnet ef database update -p src/backend/MyERP.Migrations -s src/backend/MyERP.Api'
}

$migrationFiles = @(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src/backend/MyERP.Migrations/Migrations') -Filter '*.cs' |
    Where-Object { $_.BaseName -match '^\d{14}_.+' -and $_.BaseName -notmatch '\.Designer$' } |
    ForEach-Object { $_.BaseName })
$migrationOutput = @(& sqlcmd -S $server -E -d myerp -l 5 -Q 'SET NOCOUNT ON; SELECT migration_id FROM dbo.__EFMigrationsHistory' -h -1 -W -b 2>&1)
if ($LASTEXITCODE -ne 0) {
    Stop-WithHelp '無法讀取 migration 歷史。' 'dotnet ef database update -p src/backend/MyERP.Migrations -s src/backend/MyERP.Api'
}
$appliedMigrations = @($migrationOutput | ForEach-Object { "$_".Trim() })
if (@($migrationFiles | Where-Object { $_ -notin $appliedMigrations }).Count -gt 0) {
    Stop-WithHelp '資料庫有尚未套用的 migration。' 'dotnet ef database update -p src/backend/MyERP.Migrations -s src/backend/MyERP.Api'
}

$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:DOTNET_ENVIRONMENT = 'Development'

$seedCount = & sqlcmd -S $server -E -d myerp -l 5 -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM dbo.sys_user WHERE username = N'admin' AND is_deleted = 0" -h -1 -W -b
if ($LASTEXITCODE -ne 0 -or [int](($seedCount | Out-String).Trim()) -lt 1) {
    Stop-WithHelp 'Development 種子帳號尚未建立。' 'dotnet run --project src/backend/MyERP.Api -- --seed'
}

$redis = New-Object System.Net.Sockets.TcpClient
try {
    $connect = $redis.BeginConnect('localhost', 6379, $null, $null)
    if (-not $connect.AsyncWaitHandle.WaitOne(3000)) { throw 'timeout' }
    $redis.EndConnect($connect)
}
catch {
    Stop-WithHelp 'Redis localhost:6379 無法連線。' '啟動 Memurai 或相容的 Redis 服務，確認監聽 6379。'
}
finally { $redis.Dispose() }

& dotnet dev-certs https --check --trust *> $null
if ($LASTEXITCODE -ne 0) {
    Stop-WithHelp 'HTTPS 開發憑證不存在或未受信任。' 'dotnet dev-certs https --trust'
}

$api = Start-Process -FilePath 'dotnet' -ArgumentList @('run', '--project', ('"' + $apiProject + '"'), '--launch-profile', 'https') -WorkingDirectory $repoRoot -WindowStyle Hidden -PassThru
$web = $null
try {
    $ready = $false
    for ($i = 0; $i -lt 45; $i++) {
        if ($api.HasExited) { throw "API 啟動後結束（代碼 $($api.ExitCode)）。" }
        try {
            $response = Invoke-WebRequest -UseBasicParsing -Uri 'https://localhost:7180/health/ready' -TimeoutSec 3
            if ($response.StatusCode -eq 200) { $ready = $true; break }
        }
        catch { Start-Sleep -Seconds 1 }
    }
    if (-not $ready) { throw 'API 在 45 秒內未就緒；請檢查 SQL Server、Redis 及 API 日誌。' }

    $frontendLauncher = Join-Path $repoRoot 'tools/scripts/Invoke-Frontend.ps1'
    $web = Start-Process -FilePath 'powershell.exe' -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"' + $frontendLauncher + '"'), 'npm', 'start') -WorkingDirectory $repoRoot -WindowStyle Hidden -PassThru
    $webReady = $false
    for ($i = 0; $i -lt 45; $i++) {
        if ($web.HasExited) { throw "前端啟動後結束（代碼 $($web.ExitCode)）。" }
        try {
            $response = Invoke-WebRequest -UseBasicParsing -Uri 'http://localhost:4200' -TimeoutSec 3
            if ($response.StatusCode -eq 200) { $webReady = $true; break }
        }
        catch { Start-Sleep -Seconds 1 }
    }
    if (-not $webReady) { throw '前端在 45 秒內未就緒；請確認 Node 24 及前端套件。' }
    $development = Get-Content -LiteralPath (Join-Path $apiProject 'appsettings.Development.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $demoUser = @($development.SeedUsers)[0]
    Write-Host "MyERP 已啟動：前端 http://localhost:4200（PID $($web.Id)）；後端 https://localhost:7180（PID $($api.Id)）。"
    Write-Host "Development 測試帳號：$($demoUser.Username)；密碼：$($demoUser.Password)；驗證碼萬用碼：$($development.Captcha.MasterCode)。"
}
catch {
    if ($web -and -not $web.HasExited) { Stop-Process -Id $web.Id -Force }
    if (-not $api.HasExited) { Stop-Process -Id $api.Id -Force }
    Stop-WithHelp $_.Exception.Message '檢查 logs/app-*.log，修正後重新執行 .\tools\dev\Start-Dev.ps1。'
}
