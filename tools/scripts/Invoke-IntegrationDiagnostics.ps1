<#
.SYNOPSIS
  Runs the SQL integration suite while sampling SQL waits and the test process thread pool.
.DESCRIPTION
  Writes diagnostic artifacts below logs/sql-timeouts. Requires sqlcmd and local Windows SQL authentication.
  The DMV sample contains request metadata only; it does not record SQL text or connection strings.
#>
param(
    [ValidateRange(1, 64)][int]$MaxThreads = 2,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$outDir = Join-Path $root 'logs\sql-timeouts'
New-Item -ItemType Directory -Path $outDir -Force | Out-Null
$runId = Get-Date -Format 'yyyyMMdd-HHmmss'
$waitLog = Join-Path $outDir "$runId-sql-waits.txt"
$poolLog = Join-Path $outDir "$runId-threadpool.csv"
$testLog = Join-Path $outDir "$runId-test.txt"
$marker = Join-Path $outDir "$runId-running"
Set-Content -LiteralPath $marker -Value running -Encoding UTF8

$query = @'
SET NOCOUNT ON;
SELECT CONVERT(varchar(33), SYSDATETIMEOFFSET(), 127) AS sampled_at,
       r.session_id, r.status, r.command, r.wait_type, r.wait_time,
       r.blocking_session_id, DB_NAME(r.database_id) AS database_name,
       r.cpu_time, r.total_elapsed_time,
       b.status AS blocker_status, b.wait_type AS blocker_wait_type
FROM sys.dm_exec_requests AS r
LEFT JOIN sys.dm_exec_requests AS b ON b.session_id = r.blocking_session_id
WHERE r.database_id = DB_ID('myerp_test') OR r.blocking_session_id > 0
ORDER BY r.wait_time DESC;
'@

$monitor = Start-Job -ArgumentList $marker, $waitLog, $query -ScriptBlock {
    param($runningFile, $logFile, $sql)
    while (Test-Path -LiteralPath $runningFile) {
        $sample = & sqlcmd -S 'localhost\MSSQLSERVER2022' -E -d master -l 2 -h -1 -W -s '|' -Q $sql 2>&1
        Add-Content -LiteralPath $logFile -Value ((Get-Date -Format o) + "`r`n" + ($sample -join "`r`n")) -Encoding UTF8
        Start-Sleep -Seconds 2
    }
}

$previousLog = $env:MYERP_TEST_THREADPOOL_LOG
$env:MYERP_TEST_THREADPOOL_LOG = $poolLog
try {
    & dotnet test --project (Join-Path $root 'tests\backend.integration\MyERP.IntegrationTests\MyERP.IntegrationTests.csproj') -c $Configuration --no-build --max-threads $MaxThreads 2>&1 | Out-File -LiteralPath $testLog -Encoding UTF8
    $testExit = $LASTEXITCODE
}
finally {
    $env:MYERP_TEST_THREADPOOL_LOG = $previousLog
    Remove-Item -LiteralPath $marker -Force
    Wait-Job $monitor | Out-Null
    Receive-Job $monitor | Out-Null
    Remove-Job $monitor
}

Write-Host "Integration test exit: $testExit"
Write-Host "Test output: $testLog"
Write-Host "SQL requests/blockers: $waitLog"
Write-Host "Thread pool: $poolLog"
if ($testExit -ne 0) { exit $testExit }
