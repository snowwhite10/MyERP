<#
.SYNOPSIS
  建立 MyERP 工作目錄：放入啟動套件、參考文件、舊系統原始碼，初始化 Git 並作第一次提交。

.EXAMPLE
  .\tools\bootstrap\Init-Workspace.ps1 -RepoPath C:\dev\MyERP -ReferenceDir C:\Users\me\Downloads\bluewhale

  ReferenceDir 內需有：
    BlueWhaleERP_SRS.docx
    BlueWhaleERP_SDS.docx
    bluewhale-legacy-reference.zip   （可選：已整理的藍微舊系統原始碼與資料庫腳本）
#>
param(
    [Parameter(Mandatory)][string]$RepoPath,
    [Parameter(Mandatory)][string]$ReferenceDir,
    [string]$Remote = 'https://github.com/snowwhite10/MyERP.git'
)
# 注意：Windows PowerShell 5.1 在 'Stop' 模式下，會把 git 寫到 stderr 的普通訊息（例如 push 進度）當成錯誤而中止。
# 所以這裏用 'Continue'，git 是否成功一律以 $LASTEXITCODE 判斷。
$ErrorActionPreference = 'Continue'
$kit = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path

# 1. 取得 repo（遠端已有內容就 clone，否則新建）
if (-not (Test-Path (Join-Path $RepoPath '.git'))) {
    $remoteHasContent = $false
    $refs = git ls-remote $Remote 2>$null
    if ($LASTEXITCODE -eq 0 -and $refs) { $remoteHasContent = $true }
    if ($remoteHasContent) {
        git clone $Remote $RepoPath
        if ($LASTEXITCODE -ne 0) { throw 'git clone 失敗' }
    } else {
        New-Item -ItemType Directory -Path $RepoPath -Force | Out-Null
        Push-Location $RepoPath
        git init -b main | Out-Null
        git remote add origin $Remote
        Pop-Location
    }
}

# 2. 複製啟動套件（不覆蓋已存在的檔案，避免覆寫 AI 已更新的狀態）
if ((Resolve-Path $kit).Path -ne (Resolve-Path $RepoPath).Path) {
    Get-ChildItem $kit -Recurse -File -Force | Where-Object { $_.FullName -notmatch '\\\.git\\' } | ForEach-Object {
        $rel = $_.FullName.Substring($kit.Length).TrimStart('\')
        $dst = Join-Path $RepoPath $rel
        if (-not (Test-Path $dst)) {
            New-Item -ItemType Directory -Path (Split-Path $dst) -Force | Out-Null
            Copy-Item $_.FullName $dst -ErrorAction Stop
        }
    }
}

# 3. 參考文件（不提交）
$refDst = Join-Path $RepoPath 'docs\reference'
New-Item -ItemType Directory -Path $refDst -Force | Out-Null
foreach ($f in 'BlueWhaleERP_SRS.docx', 'BlueWhaleERP_SDS.docx') {
    $src = Join-Path $ReferenceDir $f
    if (Test-Path $src) { Copy-Item $src $refDst -Force -ErrorAction Stop } else { Write-Warning "找不到 $src" }
}
$legacyZip = Join-Path $ReferenceDir 'bluewhale-legacy-reference.zip'
if (Test-Path $legacyZip) {
    Expand-Archive -Path $legacyZip -DestinationPath $refDst -Force -ErrorAction Stop
    Write-Host '已解壓舊系統參考原始碼到 docs\reference\legacy-source'
}

# 4. 確認 docs/reference 與 logs 不會被提交
Push-Location $RepoPath
try {
    foreach ($p in 'docs/reference/BlueWhaleERP_SRS.docx', 'logs/ai-run/x.log') {
        git check-ignore -q $p
        if ($LASTEXITCODE -ne 0) { throw ".gitignore 沒有排除 $p，停止。" }
    }

    # 5. 第一次提交
    git add -A
    if ($LASTEXITCODE -ne 0) { throw 'git add 失敗' }
    $staged = git diff --cached --name-only
    if ($staged -match '^docs/reference/') { throw '參考文件被加入暫存區，停止。' }
    if ($staged) {
        git commit -m 'chore: bootstrap AI autonomous development workspace' | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'git commit 失敗' }
        Write-Host '已建立提交。'
    }
    git branch -M main
    git push -u origin main
    if ($LASTEXITCODE -ne 0) { Write-Warning 'push 失敗（可稍後手動 push，Runner 也會在每輪由 AI 重試）。' }
} finally { Pop-Location }

Write-Host ''
Write-Host '下一步：' -ForegroundColor Cyan
Write-Host "  1. cd $RepoPath"
Write-Host '  2. 試跑一輪：pwsh -File tools\runner\Start-Runner.ps1 -Once'
Write-Host '  3. 檢查：pwsh -File tools\runner\status.ps1 ; git log --oneline -5'
Write-Host '  4. 沒問題後安裝自動啟動：pwsh -File tools\runner\Install-RunnerTask.ps1 ; Start-ScheduledTask MyERP-AI-Runner'
