function Test-MemoryGate {
    param([long[]]$SamplesKiB)

    $thresholdKiB = 2621440 # 2.5 GiB; Win32_OperatingSystem reports KiB.
    if ($null -eq $SamplesKiB -or $SamplesKiB.Count -ne 3) { return $false }
    foreach ($sample in $SamplesKiB) {
        if ($sample -lt $thresholdKiB) { return $false }
    }
    return $true
}
