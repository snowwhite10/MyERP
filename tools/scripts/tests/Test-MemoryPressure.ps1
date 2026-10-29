$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\MemoryPressure.Core.ps1')

function Assert-Equal([object]$expected, [object]$actual, [string]$case) {
    if ($expected -ne $actual) { throw "$case expected '$expected', got '$actual'" }
}

$inputProcesses = @(
    [pscustomobject]@{ ProcessName = 'small'; Id = 1; WorkingSet64 = 1024; PrivateMemorySize64 = 512; SessionId = 0; CommandLine = 'secret-one' },
    [pscustomobject]@{ ProcessName = 'interactive'; Id = 2; WorkingSet64 = 3221225472; PrivateMemorySize64 = 4294967296; SessionId = 2; CommandLine = 'secret-two' },
    [pscustomobject]@{ ProcessName = 'service'; Id = 3; WorkingSet64 = 2147483648; PrivateMemorySize64 = 1073741824; SessionId = 0; CommandLine = 'secret-three' }
)
$rows = @(Get-MemoryPressureRows -Processes $inputProcesses -Top 2)
Assert-Equal 2 $rows.Count 'bounded result'
Assert-Equal 2 $rows[0].Pid 'largest first'
Assert-Equal 3 $rows[1].Pid 'second largest'
Assert-Equal 'Interactive' $rows[0].Session 'interactive session'
Assert-Equal 'Service' $rows[1].Session 'service session'
Assert-Equal 4096 $rows[0].PrivateCommitMiB 'private commit conversion'
Assert-Equal 1024 $rows[1].PrivateCommitMiB 'service private commit conversion'
Assert-Equal $false ($rows[0].PSObject.Properties.Name -contains 'CommandLine') 'no command line'
Assert-Equal 0 @(Get-MemoryPressureRows -Processes @([pscustomobject]@{ ProcessName = 'bad'; Id = 4; WorkingSet64 = 1; PrivateMemorySize64 = -1; SessionId = 0 }) -Top 2).Count 'negative private commit rejected'
Assert-Equal 0 @(Get-MemoryPressureRows -Processes @() -Top 2).Count 'empty list'
$sqlProcesses = @(
    [pscustomobject]@{ ProcessName = 'sqlservr'; Id = 11; WorkingSet64 = 1048576; PrivateMemorySize64 = 2097152; SessionId = 0 },
    [pscustomobject]@{ ProcessName = 'SQLSERVR'; Id = 12; WorkingSet64 = 2097152; PrivateMemorySize64 = 3145728; SessionId = 0 },
    [pscustomobject]@{ ProcessName = 'sqlservr-helper'; Id = 13; WorkingSet64 = 4194304; PrivateMemorySize64 = 4194304; SessionId = 0 }
)
$sqlRows = @(Get-SqlServerMemoryRows -Processes $sqlProcesses)
Assert-Equal 2 $sqlRows.Count 'both SQL instances'
Assert-Equal 12 $sqlRows[0].Pid 'SQL rows sorted'
Assert-Equal 2 $sqlRows[0].WorkingSetMiB 'SQL working set'
Assert-Equal 0 @(Get-SqlServerMemoryRows -Processes @()).Count 'no SQL instance'
try { Get-MemoryPressureRows -Processes $inputProcesses -Top 0 | Out-Null; throw 'top zero accepted' }
catch { if ($_.Exception.Message -eq 'top zero accepted') { throw } }

$commit = Get-CommitPressure -CommittedBytes 64424509440 -CommitLimitBytes 77309411328 -PagesPerSec 63
Assert-Equal 60 $commit.CommittedGiB 'committed GiB'
Assert-Equal 72 $commit.LimitGiB 'commit limit GiB'
Assert-Equal 12 $commit.HeadroomGiB 'commit headroom GiB'
Assert-Equal 63 $commit.PagesPerSec 'pages per second'
foreach ($case in @(
    @{ Committed = -1; Limit = 10; Pages = 0 },
    @{ Committed = 0; Limit = 10; Pages = 0 },
    @{ Committed = 11; Limit = 10; Pages = 0 },
    @{ Committed = 0; Limit = 0; Pages = 0 },
    @{ Committed = 0; Limit = 10; Pages = -1 }
)) {
    try {
        Get-CommitPressure -CommittedBytes $case.Committed -CommitLimitBytes $case.Limit -PagesPerSec $case.Pages | Out-Null
        throw 'invalid commit sample accepted'
    }
    catch { if ($_.Exception.Message -eq 'invalid commit sample accepted') { throw } }
}
Write-Host 'Memory pressure: 24/24 passed'
