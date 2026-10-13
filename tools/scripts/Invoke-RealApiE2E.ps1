<#
.SYNOPSIS
    以真實後端執行 E2E（SYS-AUTH-017，WU-SYS-013、ADR-023）。

.DESCRIPTION
    1. 建置後端（-NoBuild 略過）；
    2. 對 Development 資料庫（appsettings.Development.json 的 myerp）套用 migration；
    3. 以 --seed 載入種子資料（冪等，只在 Development）；
    4. 在 ApiUrl 啟動 API，等待 /health/ready（需要本機 SQL Server 與 Redis）；
    5. 設定 E2E_API_URL 後執行 Playwright（預設只跑 specs/real-api-*.spec.ts），前端靜態伺服器把 /api/ 轉送到 API；
       -ApiOnly 讓純 API 規格直接連 API，略過前端建置與靜態伺服器；
    6. 結束時停止 API。API 輸出寫入 logs/real-api-e2e-api.txt。

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File tools/scripts/Invoke-RealApiE2E.ps1
    powershell -NoProfile -ExecutionPolicy Bypass -File tools/scripts/Invoke-RealApiE2E.ps1 -NoBuild -ApiUrl http://127.0.0.1:5190
    powershell -NoProfile -ExecutionPolicy Bypass -File tools/scripts/Invoke-RealApiE2E.ps1 -NoBuild -ApiOnly -Specs specs/real-api-partner-approval.spec.ts
#>
param(
    [string]$ApiUrl = 'http://127.0.0.1:5180',
    [string]$Specs = 'specs/real-api-',
    [switch]$NoBuild,
    [switch]$ApiOnly
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$apiProject = Join-Path $repoRoot 'src/backend/MyERP.Api'
$apiDll = Join-Path $apiProject 'bin/Debug/net10.0/MyERP.Api.dll'
$logDir = Join-Path $repoRoot 'logs'
New-Item -ItemType Directory -Force $logDir | Out-Null

function Invoke-Native([string]$Name, [scriptblock]$Command) {
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "$Name 失敗（結束碼 $LASTEXITCODE）" }
}

$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:DOTNET_ENVIRONMENT = 'Development'

if (-not $NoBuild) {
    Invoke-Native '後端建置' { dotnet build (Join-Path $repoRoot 'src/backend/MyERP.sln') -c Debug --nologo -v q }
}
Invoke-Native '套用 migration' {
    dotnet ef database update --no-build -p (Join-Path $repoRoot 'src/backend/MyERP.Migrations') -s $apiProject
}
Invoke-Native '載入種子資料' { dotnet $apiDll --contentRoot $apiProject --seed }

$apiLog = Join-Path $logDir 'real-api-e2e-api.txt'
$api = Start-Process -FilePath 'dotnet' -PassThru -NoNewWindow `
    -ArgumentList @($apiDll, '--contentRoot', $apiProject, '--urls', $ApiUrl) `
    -RedirectStandardOutput $apiLog -RedirectStandardError "$apiLog.err"
try {
    $ready = $false
    for ($i = 0; $i -lt 60 -and -not $ready; $i++) {
        if ($api.HasExited) { throw "API 已結束（結束碼 $($api.ExitCode)），見 $apiLog" }
        try {
            $ready = (Invoke-WebRequest -UseBasicParsing -Uri "$ApiUrl/health/ready" -TimeoutSec 5).StatusCode -eq 200
        }
        catch {
            Start-Sleep -Seconds 1
        }
    }
    if (-not $ready) { throw "API 在 60 秒內未就緒（$ApiUrl/health/ready），見 $apiLog" }

    $env:E2E_API_URL = $ApiUrl
    if ($ApiOnly) { $env:E2E_BASE_URL = $ApiUrl }
    & (Join-Path $PSScriptRoot 'Invoke-Frontend.ps1') tests/e2e npx playwright test $Specs
    $exitCode = $LASTEXITCODE
}
finally {
    Remove-Item Env:E2E_API_URL -ErrorAction SilentlyContinue
    if ($ApiOnly) { Remove-Item Env:E2E_BASE_URL -ErrorAction SilentlyContinue }
    if (-not $api.HasExited) { Stop-Process -Id $api.Id -Force }
}
exit $exitCode
