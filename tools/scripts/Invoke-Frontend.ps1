<#
.SYNOPSIS
    在 src/frontend 以正確的 Node 版本執行指令（npm / npx / ng）。

.DESCRIPTION
    Angular 22 需要 Node ^22.22.3 || ^24.15.0 || >=26（ADR-001、ADR-007）。
    本機系統 Node 版本過低且 nvm use 無法在無人值守下切換時，使用 repo 內
    可攜版 Node：.tools/node-v<版本>-win-x64（.gitignore 已排除）。
    不存在時自動從 nodejs.org 下載並以 SHASUMS256 驗證。
    只在本腳本的程序內修改 PATH，不影響系統設定。
    Playwright 瀏覽器預設下載到 .tools/ms-playwright（PLAYWRIGHT_BROWSERS_PATH）。

    刻意不用 param()：所有參數原樣傳給指令（避免 -v 等被 PowerShell 當成共用參數）。
    第一個參數如是含 "/" 的既有目錄（相對 repo 根目錄），作為工作目錄；預設 src/frontend。

.EXAMPLE
    & ./tools/scripts/Invoke-Frontend.ps1 npm ci
    & ./tools/scripts/Invoke-Frontend.ps1 npx ng build
    & ./tools/scripts/Invoke-Frontend.ps1 npx ng test --watch=false
    & ./tools/scripts/Invoke-Frontend.ps1 tests/e2e npx playwright test
#>

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$nodeVersion = (Get-Content (Join-Path $repoRoot '.nvmrc') -Raw).Trim().TrimStart('v')
$nodeDirName = "node-v$nodeVersion-win-x64"
$toolsDir = Join-Path $repoRoot '.tools'
$nodeDir = Join-Path $toolsDir $nodeDirName

function Test-SystemNode {
    $node = Get-Command node -ErrorAction SilentlyContinue
    if (-not $node) { return $false }
    $current = (& node -v).Trim().TrimStart('v')
    return $current -eq $nodeVersion
}

if (-not (Test-SystemNode)) {
    if (-not (Test-Path (Join-Path $nodeDir 'node.exe'))) {
        New-Item -ItemType Directory -Force $toolsDir | Out-Null
        $zip = Join-Path $toolsDir "$nodeDirName.zip"
        $sums = Join-Path $toolsDir 'SHASUMS256.txt'
        $baseUrl = "https://nodejs.org/dist/v$nodeVersion"
        Write-Host "下載可攜版 Node $nodeVersion ..."
        Invoke-WebRequest -UseBasicParsing -Uri "$baseUrl/$nodeDirName.zip" -OutFile $zip
        Invoke-WebRequest -UseBasicParsing -Uri "$baseUrl/SHASUMS256.txt" -OutFile $sums
        $expected = (Select-String -Path $sums -Pattern " $nodeDirName.zip$").Line.Split(' ')[0]
        $actual = (Get-FileHash $zip -Algorithm SHA256).Hash
        if (-not $expected -or $expected -ne $actual.ToLowerInvariant()) {
            Remove-Item $zip, $sums -Force
            throw "Node 壓縮檔 SHA256 不符（預期 $expected，實際 $actual）"
        }
        Expand-Archive -Path $zip -DestinationPath $toolsDir -Force
        Remove-Item $zip, $sums -Force
    }
    $env:Path = "$nodeDir;$env:Path"
}

# Playwright 瀏覽器放在 repo 內（.tools/ms-playwright，.gitignore 已排除），不寫入用戶目錄（ADR-009）
if (-not $env:PLAYWRIGHT_BROWSERS_PATH) {
    $env:PLAYWRIGHT_BROWSERS_PATH = Join-Path $toolsDir 'ms-playwright'
}

$rest = @($args)
$workingDirectory = 'src/frontend'
if ($rest.Count -gt 0 -and "$($rest[0])".Contains('/') -and (Test-Path -PathType Container (Join-Path $repoRoot $rest[0]))) {
    $workingDirectory = $rest[0]
    $rest = @($rest | Select-Object -Skip 1)
}
if ($rest.Count -eq 0) { throw '缺少要執行的指令，例如：Invoke-Frontend.ps1 npx ng build' }
$command = $rest[0]
$arguments = @($rest | Select-Object -Skip 1)

Push-Location (Join-Path $repoRoot $workingDirectory)
# 原生指令寫 stderr（警告）時，若呼叫端重導 stderr，Stop 會把它當成終止錯誤；以結束碼為準
$ErrorActionPreference = 'Continue'
try {
    & $command @arguments
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
