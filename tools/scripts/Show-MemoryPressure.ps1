$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'MemoryPressure.Core.ps1')

try {
    $os = Get-CimInstance Win32_OperatingSystem -ErrorAction Stop
    if ($null -eq $os -or $null -eq $os.FreePhysicalMemory) { throw 'Free physical memory is unavailable.' }
    $freeGiB = [math]::Round([double]$os.FreePhysicalMemory / 1048576, 3)
    Write-Output ('Free physical memory: {0:N3} GiB' -f $freeGiB)
    $memory = Get-CimInstance Win32_PerfFormattedData_PerfOS_Memory -ErrorAction Stop
    if ($null -eq $memory) { throw 'System commit counters are unavailable.' }
    $commit = Get-CommitPressure -CommittedBytes $memory.CommittedBytes -CommitLimitBytes $memory.CommitLimit -PagesPerSec $memory.PagesPersec
    Write-Output ('System commit: {0:N3} / {1:N3} GiB; headroom {2:N3} GiB; paging {3:N0} pages/sec' -f $commit.CommittedGiB, $commit.LimitGiB, $commit.HeadroomGiB, $commit.PagesPerSec)
    Write-Output 'Largest process working sets (shared pages may appear in multiple processes; private commit is allocated memory, not resident RAM):'
    $processes = @(Get-Process -ErrorAction Stop)
    Get-MemoryPressureRows -Processes $processes -Top 10 |
        Format-Table Name, Pid, WorkingSetMiB, PrivateCommitMiB, Session -AutoSize | Out-String | Write-Output
    Write-Output 'SQL Server processes (shown separately even when absent from the largest ten; shared pages may overlap):'
    $sqlRows = @(Get-SqlServerMemoryRows -Processes $processes)
    if ($sqlRows.Count -eq 0) { Write-Output 'None found.' }
    else { $sqlRows | Format-Table Name, Pid, WorkingSetMiB, PrivateCommitMiB, Session -AutoSize | Out-String | Write-Output }
    exit 0
}
catch {
    Write-Error ('Memory pressure report failed: {0}' -f $_.Exception.Message)
    exit 1
}
