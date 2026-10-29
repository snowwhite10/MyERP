$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'MemoryGate.Core.ps1')

try {
    $samples = @()
    for ($i = 0; $i -lt 3; $i++) {
        $os = Get-CimInstance Win32_OperatingSystem -ErrorAction Stop
        if ($null -eq $os -or $null -eq $os.FreePhysicalMemory) {
            throw '無法讀取可用實體記憶體。'
        }
        $freeKiB = [long]$os.FreePhysicalMemory
        $samples += $freeKiB
        $gib = [math]::Round($freeKiB / 1048576, 3)
        Write-Output ('{0} 樣本 {1}/3：可用實體記憶體 {2:N3} GiB' -f (Get-Date -Format 'HH:mm:ss'), ($i + 1), $gib)
        if ($i -lt 2) { Start-Sleep -Seconds 5 }
    }

    if (Test-MemoryGate -SamplesKiB $samples) {
        Write-Output 'PASS：三次樣本均至少 2.5 GiB，可開始 SQL／全量驗證；過程中仍須監看資源。'
        exit 0
    }
    Write-Output 'DEFER：至少一次樣本低於 2.5 GiB，延後 SQL／全量驗證。'
    exit 2
}
catch {
    Write-Error ('記憶體量測失敗：{0}' -f $_.Exception.Message)
    exit 1
}
