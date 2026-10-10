# 重新產生 src/backend/MyERP.sln 並加入全部後端與測試專案（WU-P0-006）。
# 用法：powershell -NoProfile -ExecutionPolicy Bypass -File tools/scripts/New-BackendSolution.ps1
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$backend = Join-Path $root 'src\backend'
$sln = Join-Path $backend 'MyERP.sln'

if (-not (Test-Path $sln)) {
    dotnet new sln --name MyERP --format sln --output $backend
    if ($LASTEXITCODE -ne 0) { throw 'dotnet new sln failed' }
}

$projects = @(
    Get-ChildItem -Path $backend -Recurse -Filter *.csproj
    Get-ChildItem -Path (Join-Path $root 'tests') -Recurse -Filter *.csproj
) | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }

foreach ($p in $projects) {
    dotnet sln $sln add $p.FullName
    if ($LASTEXITCODE -ne 0) { throw "dotnet sln add failed: $($p.FullName)" }
}
