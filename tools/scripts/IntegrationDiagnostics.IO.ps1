function Write-IntegrationDiagnosticSample {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Value,
        [ValidateRange(1, 100)][int]$Attempts = 10,
        [ValidateRange(0, 10000)][int]$DelayMilliseconds = 100
    )

    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        try {
            Add-Content -LiteralPath $Path -Value $Value -Encoding UTF8 -ErrorAction Stop
            return
        }
        catch [IO.IOException] {
            if ($attempt -eq $Attempts) { throw }
            Start-Sleep -Milliseconds $DelayMilliseconds
        }
    }
}
