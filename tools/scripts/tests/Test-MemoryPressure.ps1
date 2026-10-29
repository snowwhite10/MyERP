$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\MemoryPressure.Core.ps1')

function Assert-Equal([object]$expected, [object]$actual, [string]$case) {
    if ($expected -ne $actual) { throw "$case expected '$expected', got '$actual'" }
}

$inputProcesses = @(
    [pscustomobject]@{ ProcessName = 'small'; Id = 1; WorkingSet64 = 1024; SessionId = 0; CommandLine = 'secret-one' },
    [pscustomobject]@{ ProcessName = 'interactive'; Id = 2; WorkingSet64 = 3221225472; SessionId = 2; CommandLine = 'secret-two' },
    [pscustomobject]@{ ProcessName = 'service'; Id = 3; WorkingSet64 = 2147483648; SessionId = 0; CommandLine = 'secret-three' }
)
$rows = @(Get-MemoryPressureRows -Processes $inputProcesses -Top 2)
Assert-Equal 2 $rows.Count 'bounded result'
Assert-Equal 2 $rows[0].Pid 'largest first'
Assert-Equal 3 $rows[1].Pid 'second largest'
Assert-Equal 'Interactive' $rows[0].Session 'interactive session'
Assert-Equal 'Service' $rows[1].Session 'service session'
Assert-Equal $false ($rows[0].PSObject.Properties.Name -contains 'CommandLine') 'no command line'
Assert-Equal 0 @(Get-MemoryPressureRows -Processes @() -Top 2).Count 'empty list'
try { Get-MemoryPressureRows -Processes $inputProcesses -Top 0 | Out-Null; throw 'top zero accepted' }
catch { if ($_.Exception.Message -eq 'top zero accepted') { throw } }
Write-Host 'Memory pressure: 8/8 passed'
