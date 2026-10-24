$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path

foreach ($port in @(4200, 7180, 5180)) {
    $listener = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($listener) {
        $owner = Get-Process -Id $listener.OwningProcess -ErrorAction SilentlyContinue
        throw "預設埠 $port 已由 $($owner.ProcessName) (PID $($listener.OwningProcess)) 佔用；請停止程序後重試。"
    }
}

$sql = Get-Service -Name 'MSSQL$MSSQLSERVER2022' -ErrorAction Stop
if ($sql.Status -ne 'Running') { throw 'SQL Server MSSQL$MSSQLSERVER2022 未執行；請啟動服務後重試。' }
if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) { throw '找不到 sqlcmd；請先安裝 SQL Server 命令列工具。' }
$server = 'localhost\MSSQLSERVER2022'
$migrationFiles = @(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src/backend/MyERP.Migrations/Migrations') -Filter '*.cs' |
    Where-Object { $_.BaseName -match '^\d{14}_.+' -and $_.BaseName -notmatch '\.Designer$' } |
    ForEach-Object { $_.BaseName })
$migrationOutput = @(& sqlcmd -S $server -E -d myerp -l 5 -Q 'SET NOCOUNT ON; SELECT migration_id FROM dbo.__EFMigrationsHistory' -h -1 -W -b 2>&1)
if ($LASTEXITCODE -ne 0) { throw '無法讀取 myerp migration 歷史；請依 README 套用 migration。' }
$appliedMigrations = @($migrationOutput | ForEach-Object { "$_".Trim() })
if (@($migrationFiles | Where-Object { $_ -notin $appliedMigrations }).Count -gt 0) { throw 'myerp 資料庫有尚未套用的 migration；請依 README 套用 migration。' }
$seedCount = & sqlcmd -S $server -E -d myerp -l 5 -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM dbo.sys_user WHERE username = N'admin' AND is_deleted = 0" -h -1 -W -b
if ($LASTEXITCODE -ne 0 -or [int](($seedCount | Out-String).Trim()) -lt 1) { throw 'myerp 資料庫或 admin 種子帳號不可用；請依 README 套用 migration 並執行種子。' }

$redis = New-Object System.Net.Sockets.TcpClient
try {
    $connect = $redis.BeginConnect('localhost', 6379, $null, $null)
    if (-not $connect.AsyncWaitHandle.WaitOne(3000)) { throw 'Redis 連線逾時' }
    $redis.EndConnect($connect)
}
finally { $redis.Dispose() }

& dotnet dev-certs https --check --trust *> $null
if ($LASTEXITCODE -ne 0) { throw 'HTTPS 開發憑證未受信任；請執行 dotnet dev-certs https --trust。' }
if (-not (Test-Path (Join-Path $repoRoot 'src/frontend/node_modules'))) { throw '前端套件未安裝；請執行 Invoke-Frontend.ps1 npm ci。' }
