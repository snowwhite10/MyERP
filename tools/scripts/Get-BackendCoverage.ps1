<#
.SYNOPSIS
  量度後端單元測試覆蓋率（coverlet.collector，經 dotnet vstest 執行）。
.DESCRIPTION
  dotnet test 已改走 Microsoft.Testing.Platform（ADR-004），MTP 的 --coverage 需要另一個套件（不在 ADR-001 清單），
  因此以 dotnet vstest + coverlet.collector（xunit.runner.visualstudio 仍提供 VSTest 介面）量度。
  先執行 dotnet build。結果寫到 logs/coverage（不提交），並列出各組件行覆蓋率。
.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File tools/scripts/Get-BackendCoverage.ps1
  powershell -NoProfile -ExecutionPolicy Bypass -File tools/scripts/Get-BackendCoverage.ps1 -TestProject MyERP.Shared.Tests -Minimum 80
#>
param(
    [string[]]$TestProject = @('MyERP.Shared.Tests'),
    [string]$Configuration = 'Debug',
    [double]$Minimum = 80
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$resultsDir = Join-Path $root 'logs\coverage'

[xml]$packagesProps = Get-Content (Join-Path $root 'src\backend\Directory.Packages.props') -Encoding UTF8
$coverletVersion = ($packagesProps.Project.ItemGroup.PackageVersion | Where-Object Include -eq 'coverlet.collector').Version
$globalPackages = ((dotnet nuget locals global-packages --list) -replace '^global-packages:\s*', '').Trim()
$adapterPath = Join-Path $globalPackages "coverlet.collector\$coverletVersion\build\netstandard2.0"
if (-not (Test-Path $adapterPath)) { throw "coverlet.collector $coverletVersion not found at $adapterPath. Run dotnet restore first." }

if (Test-Path $resultsDir) { Remove-Item $resultsDir -Recurse -Force }
$failed = $false
foreach ($project in $TestProject) {
    $dll = Get-ChildItem (Join-Path $root 'tests') -Recurse -Filter "$project.dll" |
        Where-Object { $_.FullName -match "\\bin\\$Configuration\\net[\d.]+\\$project\.dll$" } | Select-Object -First 1
    if (-not $dll) { throw "$project.dll not found. Run dotnet build first." }

    $projectResults = Join-Path $resultsDir $project
    dotnet vstest $dll.FullName --collect:'XPlat Code Coverage' "--TestAdapterPath:$adapterPath" "--ResultsDirectory:$projectResults" | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Tests failed in $project." }

    $report = Get-ChildItem $projectResults -Recurse -Filter 'coverage.cobertura.xml' | Select-Object -First 1
    [xml]$xml = Get-Content $report.FullName -Encoding UTF8
    foreach ($package in $xml.coverage.packages.package) {
        $percent = [math]::Round([double]$package.'line-rate' * 100, 1)
        $status = if ($percent -ge $Minimum) { 'OK' } else { $failed = $true; 'BELOW' }
        Write-Host ("{0,-40} line {1,6}%  branch {2,6}%  {3}" -f $package.name, $percent, [math]::Round([double]$package.'branch-rate' * 100, 1), $status)
    }
}

if ($failed) { exit 1 }
