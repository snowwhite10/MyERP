$ErrorActionPreference = 'Stop'
$helper = Join-Path $PSScriptRoot '..\MemoryGate.Core.ps1'

function Assert-Equal([object]$expected, [object]$actual, [string]$case) {
    if ($expected -ne $actual) { throw "$case expected '$expected', got '$actual'" }
}

. $helper
$thresholdKiB = 2621440 # 2.5 GiB
Assert-Equal $true (Test-MemoryGate -SamplesKiB @($thresholdKiB, $thresholdKiB, $thresholdKiB)) 'exact threshold'
Assert-Equal $true (Test-MemoryGate -SamplesKiB @(3145728, 2883584, 2621440)) 'all above threshold'
Assert-Equal $false (Test-MemoryGate -SamplesKiB @(3145728, 2621439, 3145728)) 'one low sample'
Assert-Equal $false (Test-MemoryGate -SamplesKiB @($thresholdKiB, $thresholdKiB)) 'incomplete sample set'
Assert-Equal $false (Test-MemoryGate -SamplesKiB @()) 'empty sample set'
Assert-Equal $false (Test-MemoryGate -SamplesKiB @(0, $thresholdKiB, $thresholdKiB)) 'invalid sample'
Write-Host 'Memory gate: 6/6 passed'
