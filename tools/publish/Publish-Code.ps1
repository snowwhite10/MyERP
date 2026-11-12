<#
.SYNOPSIS
  把開發 repo 中「已提交」的代碼同步到公開的代碼 repo。AI 相關檔案（見 publish-exclude.txt）不會發佈。

.DESCRIPTION
  1. 匯出開發 repo 的 HEAD（只有已提交的內容，不含未提交的半成品）
  2. 刪除 publish-exclude.txt 列出的路徑
  3. 掃描常見機密（密碼、Token、私鑰），發現就停止，不會發佈
  4. 鏡像到代碼 repo 的工作目錄，提交並 push

  只可由人手執行。AI 不得執行本腳本、不得修改排除清單。

.EXAMPLE
  # 第一次：指定公開 repo 網址
  .\tools\publish\Publish-Code.ps1 -PublicRemote https://github.com/snowwhite10/MyERP-code.git

  # 之後
  .\tools\publish\Publish-Code.ps1
  .\tools\publish\Publish-Code.ps1 -DryRun     # 只列出會發佈的內容，不提交
#>
param(
    [string]$PublicRemote,
    [string]$PublicDir,
    [string]$Branch = 'main',
    [switch]$DryRun
)
$ErrorActionPreference = 'Continue'
try { [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false) } catch { }

$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if (-not $PublicDir) { $PublicDir = Join-Path (Split-Path $root -Parent) 'MyERP-public' }
$excludeFile = Join-Path $PSScriptRoot 'publish-exclude.txt'
$exclude = @(Get-Content $excludeFile -Encoding UTF8 | ForEach-Object { $_.Trim() } | Where-Object { $_ -and -not $_.StartsWith('#') })

function Fail([string]$msg) { Write-Host $msg -ForegroundColor Red; exit 1 }

# ---------------------------------------------------------------- 1. 匯出 HEAD
$dirty = @(git -C $root status --porcelain 2>$null).Count
if ($dirty -gt 0) { Write-Host "注意：開發 repo 有 $dirty 個未提交變更（例如 AI 正在工作），這些不會發佈，只發佈已提交的內容。" -ForegroundColor Yellow }
$sha = (git -C $root rev-parse --short HEAD).Trim()
$subject = (git -C $root log -1 --format=%s).Trim()
if ($LASTEXITCODE -ne 0 -or -not $sha) { Fail '無法讀取開發 repo 的 HEAD。' }

$stamp = Get-Date -Format 'yyyyMMddHHmmss'
$tmp = Join-Path $env:TEMP "myerp-publish-$stamp"
$zip = "$tmp.zip"
git -C $root archive --format=zip -o $zip HEAD
if ($LASTEXITCODE -ne 0) { Fail 'git archive 失敗。' }
Expand-Archive -Path $zip -DestinationPath $tmp -Force
Remove-Item $zip -Force

# ---------------------------------------------------------------- 2. 排除 AI 檔案
foreach ($e in $exclude) {
    $p = Join-Path $tmp ($e -replace '/', '\')
    if (Test-Path $p) { Remove-Item $p -Recurse -Force }
}

# ---------------------------------------------------------------- 3. 機密掃描
$secretPatterns = @(
    '(?i)(password|pwd)\s*=\s*[^;"''\s<>{}]{4,}',
    'ghp_[A-Za-z0-9]{30,}', 'github_pat_[A-Za-z0-9_]{30,}',
    'sk-[A-Za-z0-9_-]{30,}',
    '-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----',
    '(?i)AccountKey=[A-Za-z0-9+/=]{20,}',
    '(?i)"SigningKey"\s*:\s*"[^"]{16,}"'
)
$textExt = '\.(cs|ts|js|mjs|json|config|xml|yml|yaml|md|ps1|psm1|sql|txt|html|scss|env|props|targets|csproj|sln)$'
$hits = @()
Get-ChildItem $tmp -Recurse -File | Where-Object { $_.Name -match $textExt -and $_.Length -lt 2MB } | ForEach-Object {
    $file = $_
    foreach ($pat in $secretPatterns) {
        $m = Select-String -Path $file.FullName -Pattern $pat -Encoding UTF8 -List
        if ($m) { $hits += ('{0}:{1}  {2}' -f $file.FullName.Substring($tmp.Length + 1), $m.LineNumber, $m.Line.Trim()) }
    }
}
if ($hits.Count -gt 0) {
    Write-Host '發現疑似機密，已停止，沒有發佈：' -ForegroundColor Red
    $hits | Select-Object -First 20 | ForEach-Object { Write-Host "  $_" }
    Write-Host '如確定是假警報（例如測試用的假密碼），把該檔案路徑加入 publish-exclude.txt，或請 AI 改用不像機密的寫法。'
    Remove-Item $tmp -Recurse -Force
    exit 1
}

# ---------------------------------------------------------------- 3b. 內部參考名稱掃描（文字檔與 Word / Excel 內容）
Add-Type -AssemblyName System.IO.Compression.FileSystem
function Get-OfficeText([string]$file) {
    try {
        $z = [System.IO.Compression.ZipFile]::OpenRead($file)
        $sb = New-Object System.Text.StringBuilder
        foreach ($e in $z.Entries) {
            if ($e.FullName -match '\.xml$') {
                $r = New-Object System.IO.StreamReader($e.Open(), [System.Text.Encoding]::UTF8)
                [void]$sb.Append(($r.ReadToEnd() -replace '<[^>]+>', ' '))
                $r.Close()
            }
        }
        $z.Dispose()
        return $sb.ToString()
    } catch { return '' }
}
# 開發參考的系統名稱只可出現在私人 repo 的內部文件，不得出現在公開內容
$forbidden = '(?i)(blue\s*whale|藍微|蓝微|lanwei)'
$termHits = @()
Get-ChildItem $tmp -Recurse -File | Where-Object { $_.Name -match $textExt -and $_.Length -lt 2MB } | ForEach-Object {
    $m = Select-String -Path $_.FullName -Pattern $forbidden -Encoding UTF8
    foreach ($x in @($m)) { $termHits += ('{0}:{1}  {2}' -f $_.FullName.Substring($tmp.Length + 1), $x.LineNumber, $x.Line.Trim()) }
}
Get-ChildItem $tmp -Recurse -File -Include *.docx, *.xlsx | ForEach-Object {
    if ((Get-OfficeText $_.FullName) -match $forbidden) { $termHits += ('{0}  （Word / Excel 內容）' -f $_.FullName.Substring($tmp.Length + 1)) }
}
if ($termHits.Count -gt 0) {
    Write-Host ("發現內部參考名稱（BlueWhale 等）{0} 處，已停止，沒有發佈：" -f $termHits.Count) -ForegroundColor Red
    $termHits | Select-Object -First 20 | ForEach-Object { Write-Host "  $_" }
    Write-Host '請等 AI 完成清理（CR-H14）後再發佈。'
    Remove-Item $tmp -Recurse -Force
    exit 1
}

if ($DryRun) {
    Write-Host "（DryRun）將發佈 $sha「$subject」，以下是頂層內容：" -ForegroundColor Cyan
    Get-ChildItem $tmp | Select-Object -ExpandProperty Name
    $n = @(Get-ChildItem $tmp -Recurse -File).Count
    Write-Host "共 $n 個檔案。暫存位置：$tmp（檢查完可刪除）"
    exit 0
}

# ---------------------------------------------------------------- 4. 準備代碼 repo
if (-not (Test-Path (Join-Path $PublicDir '.git'))) {
    if (-not $PublicRemote) { Fail "代碼 repo 尚未建立。第一次請加 -PublicRemote <公開 repo 網址>。" }
    $remoteHasContent = [bool](git ls-remote $PublicRemote 2>$null)
    if ($remoteHasContent) {
        git clone $PublicRemote $PublicDir
        if ($LASTEXITCODE -ne 0) { Fail 'clone 代碼 repo 失敗。' }
    } else {
        New-Item -ItemType Directory -Path $PublicDir -Force | Out-Null
        git -C $PublicDir init -b $Branch | Out-Null
        git -C $PublicDir remote add origin $PublicRemote
    }
}

# 鏡像（保留代碼 repo 的 .git）
& robocopy $tmp $PublicDir /MIR /XD (Join-Path $PublicDir '.git') /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { Remove-Item $tmp -Recurse -Force; Fail "複製檔案失敗（robocopy 結束碼 $LASTEXITCODE）。" }
Remove-Item $tmp -Recurse -Force

# ---------------------------------------------------------------- 5. 提交並 push
git -C $PublicDir add -A
$changes = @(git -C $PublicDir status --porcelain).Count
if ($changes -eq 0) { Write-Host '代碼沒有變化，不需要發佈。' -ForegroundColor Green; exit 0 }
# 不沿用開發 repo 的提交訊息（可能含內部資訊），只記錄來源提交與日期
git -C $PublicDir commit -m ("sync: {0} ({1})" -f $sha, (Get-Date -Format 'yyyy-MM-dd')) | Out-Null
if ($LASTEXITCODE -ne 0) { Fail 'commit 失敗。' }
git -C $PublicDir push -u origin $Branch
if ($LASTEXITCODE -ne 0) { Fail 'push 失敗（憑證或網絡問題），可稍後重新執行。' }
Write-Host "已發佈 $changes 個變更（來源 $sha）到 $PublicRemote" -ForegroundColor Green
