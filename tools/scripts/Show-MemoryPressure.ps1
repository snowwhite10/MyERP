$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'MemoryPressure.Core.ps1')

try {
    $os = Get-CimInstance Win32_OperatingSystem -ErrorAction Stop
    if ($null -eq $os -or $null -eq $os.FreePhysicalMemory) { throw 'Free physical memory is unavailable.' }
    $freeGiB = [math]::Round([double]$os.FreePhysicalMemory / 1048576, 3)
    Write-Output ('Free physical memory: {0:N3} GiB' -f $freeGiB)
    Write-Output 'Largest process working sets (shared pages may appear in multiple processes):'
    Get-MemoryPressureRows -Processes @(Get-Process -ErrorAction Stop) -Top 10 |
        Format-Table Name, Pid, WorkingSetMiB, Session -AutoSize | Out-String | Write-Output
    exit 0
}
catch {
    Write-Error ('Memory pressure report failed: {0}' -f $_.Exception.Message)
    exit 1
}
