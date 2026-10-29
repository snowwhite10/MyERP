function Get-MemoryPressureRows {
    param(
        [object[]]$Processes,
        [int]$Top = 10
    )
    if ($Top -lt 1) { throw 'Top must be positive.' }
    if ($null -eq $Processes -or $Processes.Count -eq 0) { return }

    $Processes |
        Where-Object { $null -ne $_ -and $null -ne $_.WorkingSet64 -and $_.WorkingSet64 -ge 0 -and
            $null -ne $_.PrivateMemorySize64 -and $_.PrivateMemorySize64 -ge 0 } |
        Sort-Object -Property WorkingSet64 -Descending |
        Select-Object -First $Top |
        ForEach-Object {
            [pscustomobject]@{
                Name = [string]$_.ProcessName
                Pid = [int]$_.Id
                WorkingSetMiB = [math]::Round([double]$_.WorkingSet64 / 1MB, 1)
                PrivateCommitMiB = [math]::Round([double]$_.PrivateMemorySize64 / 1MB, 1)
                Session = if ($_.SessionId -eq 0) { 'Service' } else { 'Interactive' }
            }
        }
}

function Get-CommitPressure {
    param(
        [long]$CommittedBytes,
        [long]$CommitLimitBytes,
        [long]$PagesPerSec
    )
    if ($CommittedBytes -le 0 -or $CommitLimitBytes -le 0 -or
        $CommittedBytes -gt $CommitLimitBytes -or $PagesPerSec -lt 0) {
        throw 'Invalid system commit sample.'
    }
    [pscustomobject]@{
        CommittedGiB = [math]::Round($CommittedBytes / 1GB, 3)
        LimitGiB = [math]::Round($CommitLimitBytes / 1GB, 3)
        HeadroomGiB = [math]::Round(($CommitLimitBytes - $CommittedBytes) / 1GB, 3)
        PagesPerSec = $PagesPerSec
    }
}

function Get-SqlServerMemoryRows {
    param([object[]]$Processes)
    if ($null -eq $Processes) { return }
    $sqlProcesses = @($Processes | Where-Object { $null -ne $_ -and $_.ProcessName -ieq 'sqlservr' })
    if ($sqlProcesses.Count -eq 0) { return }
    Get-MemoryPressureRows -Processes $sqlProcesses -Top $sqlProcesses.Count
}
