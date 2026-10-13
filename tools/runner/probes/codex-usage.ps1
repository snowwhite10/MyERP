param(
    [string]$CodexCommand = 'codex',
    [ValidateRange(5,120)][int]$TimeoutSeconds = 45,
    [switch]$Json
)
$ErrorActionPreference = 'Stop'
$command = (Get-Command $CodexCommand -All -ErrorAction Stop |
    Where-Object { $_.CommandType -eq 'Application' -and $_.Path -match '\.(exe|cmd|bat)$' } |
    Select-Object -First 1).Path
if (-not $command) { throw 'Codex executable or .cmd wrapper was not found.' }
$startInfo = New-Object System.Diagnostics.ProcessStartInfo
$startInfo.FileName = $command
$startInfo.Arguments = 'app-server'
if ($command -match '\.(cmd|bat)$') {
    $startInfo.FileName = $env:ComSpec
    $startInfo.Arguments = '/d /s /c ""' + $command + '" app-server"'
}
$startInfo.WorkingDirectory = (Get-Location).Path
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.RedirectStandardInput = $true
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true
$startInfo.StandardOutputEncoding = [System.Text.Encoding]::UTF8
$startInfo.StandardErrorEncoding = [System.Text.Encoding]::UTF8
$process = New-Object System.Diagnostics.Process
$process.StartInfo = $startInfo
$started = $false
$clock = [System.Diagnostics.Stopwatch]::StartNew()
function Send-Rpc($message) {
    $process.StandardInput.WriteLine(($message | ConvertTo-Json -Depth 10 -Compress))
    $process.StandardInput.Flush()
}
function Read-RpcReply([int]$requestId) {
    while ($true) {
        $remainingMs = [int]($TimeoutSeconds * 1000 - $clock.ElapsedMilliseconds)
        if ($remainingMs -le 0) { throw 'Usage query timed out; no reset time was confirmed.' }
        $readTask = $process.StandardOutput.ReadLineAsync()
        if (-not $readTask.Wait($remainingMs)) { throw 'Usage query timed out; no reset time was confirmed.' }
        $line = $readTask.Result
        if ($null -eq $line) { throw 'App Server exited before returning usage information.' }
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        try { $message = $line | ConvertFrom-Json } catch { continue }
        if ($null -ne $message.id -and [string]$message.id -eq [string]$requestId) {
            if ($null -ne $message.error) {
                throw ('Usage query failed: ' + $message.error.message)
            }
            return $message.result
        }
    }
}
function Convert-Reset($timestamp) {
    if ($null -eq $timestamp) { return $null }
    return ([DateTimeOffset]::FromUnixTimeSeconds([long]$timestamp)).ToLocalTime()
}
try {
    [void]$process.Start()
    $started = $true
    $stderrTask = $process.StandardError.ReadToEndAsync()
    Send-Rpc @{ id=1; method='initialize'; params=@{ clientInfo=@{ name='erp_usage_monitor'; title='ERP Usage Monitor'; version='1.0.0' } } }
    $null = Read-RpcReply 1
    Send-Rpc @{ method='initialized'; params=@{} }
    Send-Rpc @{ id=2; method='account/rateLimits/read' }
    $snapshot = Read-RpcReply 2
    $buckets = @()
    if ($null -ne $snapshot.rateLimitsByLimitId) {
        foreach ($entry in $snapshot.rateLimitsByLimitId.PSObject.Properties) {
            $buckets += [pscustomobject]@{ id=$entry.Name; limits=$entry.Value }
        }
    }
    if ($buckets.Count -eq 0 -and $null -ne $snapshot.rateLimits) {
        $buckets += [pscustomobject]@{ id=$snapshot.rateLimits.limitId; limits=$snapshot.rateLimits }
    }
    $windows = @(); $credits = @(); $states = @()
    foreach ($bucket in $buckets) {
        $limits = $bucket.limits
        $states += [pscustomobject]@{ bucket=$bucket.id; reachedType=$limits.rateLimitReachedType; spendControlReached=$limits.spendControlReached }
        foreach ($name in @('primary','secondary','individualLimit')) {
            $window = $limits.$name
            if ($null -eq $window) { continue }
            $reset = Convert-Reset $window.resetsAt
            $localReset = $null; $minutes = $null
            if ($null -ne $reset) {
                $localReset = $reset.ToString('yyyy-MM-dd HH:mm:ss zzz')
                $minutes = [Math]::Max(0,[Math]::Ceiling(($reset - [DateTimeOffset]::Now).TotalMinutes))
            }
            $windows += [pscustomobject]@{
                bucket=$bucket.id; window=$name; usedPercent=$window.usedPercent
                remainingPercent=$window.remainingPercent; windowMinutes=$window.windowDurationMins
                resetLocal=$localReset; minutesUntilReset=$minutes
            }
        }
        if ($null -ne $limits.credits) {
            $credits += [pscustomobject]@{ bucket=$bucket.id; hasCredits=$limits.credits.hasCredits; unlimited=$limits.credits.unlimited; balance=$limits.credits.balance }
        }
    }
    $summary = [pscustomobject]@{
        queriedAt=[DateTimeOffset]::Now.ToString('o')
        ordinaryUsageAllowed=$snapshot.ordinaryUsageAllowed
        states=$states; windows=$windows; credits=$credits
    }
    if ($Json) { $summary | ConvertTo-Json -Depth 10 }
    else {
        Write-Output ('Queried at: ' + $summary.queriedAt)
        Write-Output ('Ordinary usage allowed: ' + $(if ($null -eq $summary.ordinaryUsageAllowed) { 'unknown (field unavailable)' } else { [string]$summary.ordinaryUsageAllowed }))
        $states | Format-Table -AutoSize | Out-String -Width 220 | Write-Output
        $windows | Format-Table -AutoSize | Out-String -Width 220 | Write-Output
        if ($credits.Count -gt 0) { $credits | Format-Table -AutoSize | Out-String -Width 220 | Write-Output }
        Write-Output 'Reset timestamps describe quota windows. They do not guarantee restored access or a workspace credit refill.'
    }
} finally {
    if ($started) {
        try { $process.StandardInput.Close() } catch {}
        if (-not $process.WaitForExit(3000)) {
            $previousPreference = $ErrorActionPreference
            $ErrorActionPreference = 'Continue'
            try { & taskkill.exe /PID $process.Id /T /F 2>$null | Out-Null }
            finally { $ErrorActionPreference = $previousPreference }
        }
    }
    $process.Dispose()
    $clock.Stop()
}
