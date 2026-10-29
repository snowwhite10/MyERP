function Get-MemoryPressureRows {
    param(
        [object[]]$Processes,
        [int]$Top = 10
    )
    if ($Top -lt 1) { throw 'Top must be positive.' }
    if ($null -eq $Processes -or $Processes.Count -eq 0) { return }

    $Processes |
        Where-Object { $null -ne $_ -and $null -ne $_.WorkingSet64 -and $_.WorkingSet64 -ge 0 } |
        Sort-Object -Property WorkingSet64 -Descending |
        Select-Object -First $Top |
        ForEach-Object {
            [pscustomobject]@{
                Name = [string]$_.ProcessName
                Pid = [int]$_.Id
                WorkingSetMiB = [math]::Round([double]$_.WorkingSet64 / 1MB, 1)
                Session = if ($_.SessionId -eq 0) { 'Service' } else { 'Interactive' }
            }
        }
}
