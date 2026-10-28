$ErrorActionPreference = 'Stop'
$helper = Join-Path $PSScriptRoot '..\IntegrationDiagnostics.IO.ps1'
$tempFile = Join-Path ([IO.Path]::GetTempPath()) ("myerp-sample-{0}.txt" -f [guid]::NewGuid())
$readyFile = "$tempFile.ready"

function Assert-Equal([object]$expected, [object]$actual, [string]$case) {
    if ($expected -ne $actual) { throw "$case expected '$expected', got '$actual'" }
}

try {
    . $helper
    Write-IntegrationDiagnosticSample -Path $tempFile -Value 'normal' -Attempts 10 -DelayMilliseconds 100
    Assert-Equal 'normal' ((Get-Content -LiteralPath $tempFile -Encoding UTF8) -join '') 'normal append'

    $locker = Start-Job -ArgumentList $tempFile, $readyFile -ScriptBlock {
        param($file, $ready)
        $handle = [IO.File]::Open($file, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
        try {
            [IO.File]::WriteAllText($ready, 'locked')
            Start-Sleep -Milliseconds 350
        }
        finally { $handle.Dispose() }
    }
    try {
        for ($i = 0; $i -lt 50 -and -not (Test-Path -LiteralPath $readyFile); $i++) { Start-Sleep -Milliseconds 20 }
        Assert-Equal $true (Test-Path -LiteralPath $readyFile) 'lock acquired'
        Write-IntegrationDiagnosticSample -Path $tempFile -Value 'after lock' -Attempts 10 -DelayMilliseconds 100
        Wait-Job $locker | Out-Null
        Receive-Job $locker -ErrorAction Stop | Out-Null
    }
    finally { Remove-Job $locker -Force }
    Assert-Equal 'normal,after lock' ((Get-Content -LiteralPath $tempFile -Encoding UTF8) -join ',') 'transient lock retry'

    $lock = [IO.File]::Open($tempFile, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        $failed = $false
        try { Write-IntegrationDiagnosticSample -Path $tempFile -Value 'must fail' -Attempts 2 -DelayMilliseconds 25 }
        catch [IO.IOException] { $failed = $true }
        Assert-Equal $true $failed 'permanent lock propagation'
    }
    finally { $lock.Dispose() }
    Assert-Equal 'normal,after lock' ((Get-Content -LiteralPath $tempFile -Encoding UTF8) -join ',') 'no partial append'

    $lock = [IO.File]::Open($tempFile, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        $sampler = Start-Job -ArgumentList $tempFile, $helper -ScriptBlock {
            param($file, $script)
            . $script
            Write-IntegrationDiagnosticSample -Path $file -Value 'job failure' -Attempts 2 -DelayMilliseconds 25
        }
        try {
            Wait-Job $sampler | Out-Null
            $caught = $false
            try { Receive-Job $sampler -ErrorAction Stop | Out-Null }
            catch { $caught = $true }
            Assert-Equal $true $caught 'background job error propagation'
        }
        finally { Remove-Job $sampler -Force }
    }
    finally { $lock.Dispose() }
    Write-Host 'Integration diagnostics IO: 5/5 passed'
}
finally {
    if (Test-Path -LiteralPath $readyFile) { Remove-Item -LiteralPath $readyFile -Force }
    if (Test-Path -LiteralPath $tempFile) { Remove-Item -LiteralPath $tempFile -Force }
}
