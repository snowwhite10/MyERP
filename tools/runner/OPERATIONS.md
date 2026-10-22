# MyERP 人手操作手冊

> 給人手使用。AI 不得修改本檔，本檔也不會發佈到公開的代碼 repo。
> 所有指令都在 `D:\ERP\MyERP` 執行（先 `cd D:\ERP\MyERP`）。

---

## 1. 每日查看

```powershell
powershell -ExecutionPolicy Bypass -File tools\runner\status.ps1     # Runner 狀態、目前工作、最後 10 行日誌
git log --oneline -10                                                # 已提交的工作
git status --short                                                   # AI 正在改哪些檔案
git branch --show-current                                            # 檢查目前所在分支
```

今天日誌最後 20 行：

```powershell
Get-Content (Get-ChildItem logs\ai-run -Recurse -File | Sort-Object LastWriteTime | Select-Object -Last 1).FullName -Encoding UTF8 -Tail 20
```

最近的失敗記錄：

```powershell
Get-ChildItem logs\ai-run -Recurse -File | Select-String '\| (FAIL|BLOCKED)\s' | Select-Object -Last 20 | ForEach-Object { $_.Line }
```

`status.ps1` 第一行的意思：

| 顯示 | 意思 |
|---|---|
| 運行中（claude / codex） | AI 正在工作 |
| 運行中（輪與輪之間） | 一輪剛結束，約 20 秒後開始下一輪 |
| 等待額度（至 …） | 額度用完，正在等待 reset |
| 失敗退避（至 …） | 連續出錯，等一會再試 |
| 已停止 | Runner 沒有運行 |

---

## 2. 啟動與停止

### 啟動（選擇 AI）

```powershell
powershell -ExecutionPolicy Bypass -File tools\runner\Start-Background.ps1               # Claude 優先
powershell -ExecutionPolicy Bypass -File tools\runner\Start-Background.ps1 codex         # Codex 優先
powershell -ExecutionPolicy Bypass -File tools\runner\Start-Background.ps1 codex -Only   # 只用 Codex，額度用完就等，不切換
```

會自動刪除 STOP 檔，5 秒後顯示狀態。

### 運行中途切換 AI

```powershell
powershell -ExecutionPolicy Bypass -File tools\runner\Use-Agent.ps1 codex            # 下一輪起改用 Codex（建議）
powershell -ExecutionPolicy Bypass -File tools\runner\Use-Agent.ps1 claude           # 下一輪起改用 Claude
powershell -ExecutionPolicy Bypass -File tools\runner\Use-Agent.ps1 codex -Restart   # 立即中斷本輪，改用 Codex
```

- 不加 `-Restart`：目前這一輪做完才切換，不浪費額度。
- 切換到正在等待額度的 AI，會立即重新嘗試（適用於你知道它已恢復，例如剛充值）。

### 停止

```powershell
powershell -ExecutionPolicy Bypass -File tools\runner\Stop-Runner.ps1        # 安全停止（建議）：做完本輪才停
powershell -ExecutionPolicy Bypass -File tools\runner\Stop-Runner.ps1 -Now   # 立即停止：可能留下未提交變更，下一輪 AI 會處理
```

停止後用 `status.ps1` 確認第一行是「已停止」。STOP 檔存在時，即使重新開機，Runner 也會立即自行結束。

### 首次設定：登入時自動啟動（只需做一次）

```powershell
powershell -ExecutionPolicy Bypass -File tools\runner\Install-RunnerTask.ps1
```

- 之後每次登入會自動以 **Claude 優先**啟動。想改用 Codex，登入後執行 `Use-Agent.ps1 codex`。
- 取消自動啟動：`Disable-ScheduledTask -TaskName MyERP-AI-Runner`；恢復：`Enable-ScheduledTask -TaskName MyERP-AI-Runner`。
- 遠端桌面離開時**直接關閉視窗**，不要按「登出」，否則 Runner 會停止。

---

## 3. 確認 AI 是否在運作

```powershell
Get-Process claude, codex, node -ErrorAction SilentlyContinue | Format-Table Name, Id, CPU, StartTime
```

有程序而且 `CPU` 數值會增加，就代表在工作。

**10 分鐘以上沒有檔案變化、額度也沒有減少時**，多數是以下兩種情況：

| 情況 | 處理 |
|---|---|
| 正在跑測試（`dotnet test`、`testhost` 的 CPU 會增加） | 正常，繼續等 |
| 卡在不會結束的指令（`dotnet run`、`ng serve`、`npx` 等 CPU 不變） | 只結束那一個程序：`Stop-Process -Id <PID>`，不要停 Runner |

找出可疑程序：

```powershell
Get-CimInstance Win32_Process | Where-Object { $_.Name -match '^(codex|claude|node|dotnet|testhost|npx)' } |
    Select-Object ProcessId, Name, CreationDate, @{n='指令';e={([string]$_.CommandLine).Substring(0, [Math]::Min(100, ([string]$_.CommandLine).Length))}} | Format-Table -AutoSize -Wrap
```

**出現彈出視窗**（例如 nvm）：先按掉讓 AI 繼續，再截圖記錄。

---

## 4. 查看 AI 的輸出

```powershell
Get-Content logs\runner\last-output-codex.txt -Encoding UTF8 -Tail 30    # Codex 最後一輪
Get-Content logs\runner\last-output-claude.txt -Encoding UTF8 -Tail 30   # Claude 最後一輪
```

- 一定要加 `-Encoding UTF8`，否則中文會變亂碼。
- 用記事本開啟時，「編碼」選 UTF-8；或用 VS Code（`code logs\runner\last-output-codex.txt`）。
- 這個檔案記錄整輪過程，平時只看最後 20 至 30 行（AI 的總結）。

查看 Codex 用量與 reset 時間：

```powershell
powershell -ExecutionPolicy Bypass -File tools\runner\probes\codex-usage.ps1
```

---

## 5. 查看系統（ERP）的錯誤

```powershell
powershell -ExecutionPolicy Bypass -File tools\dev\Show-Log.ps1                    # 錯誤統計（平時用這個）
powershell -ExecutionPolicy Bypass -File tools\dev\Show-Log.ps1 -Level Warning     # 連同警告
powershell -ExecutionPolicy Bypass -File tools\dev\Show-Log.ps1 -Detail            # 逐筆列出最近 20 筆
powershell -ExecutionPolicy Bypass -File tools\dev\Show-Log.ps1 -TraceId <ID>      # 某次的完整錯誤
powershell -ExecutionPolicy Bypass -File tools\dev\Show-Log.ps1 -Date 2026-10-20   # 看其他日子
```

畫面出錯時，在瀏覽器按 F12 → Network → 點出錯的請求 → 在回應中找 `traceId`，再用 `-TraceId` 查完整原因。

---

## 6. 回報問題給 AI

發現問題時**不要自己改代碼**（AI 不知道你改了甚麼，可能覆蓋或衝突），記錄為 CR：

```powershell
$line = "| CR-Hxx | $(Get-Date -Format 'yyyy-MM-dd') | <模組或範圍> | 人手測試：<問題、重現步驟、錯誤訊息、traceId> | 人手驗收發現 | 待 AI 修正（P1/P2） |`r`n"
[IO.File]::AppendAllText("$PWD\docs\00-management\change-requests.md", $line, (New-Object System.Text.UTF8Encoding($false)))
git add docs/00-management/change-requests.md
git commit -m "docs(mgmt): CR-Hxx <簡述>"
git push
```

- `CR-Hxx` 換成下一個編號（看 `change-requests.md` 最後一個 CR-H 編號）。
- 附上 traceId，AI 可以自己去日誌查原因。
- 屬於長期規則的，另外加入 `MASTER_PROMPT.md`，並在 `docs\00-management\master-prompt-changelog.md` 記錄。

---

## 7. 修改 Runner、設定或 MASTER_PROMPT

按這個次序，避免 AI 處理到你的未提交變更：

1. 停止：`Stop-Runner.ps1`，用 `status.ps1` 確認已停止
2. 修改檔案
3. **立即提交**：只 `git add` 你改的檔案，不要用 `git add -A`
4. 啟動：`Start-Background.ps1 codex`

只改 `MASTER_PROMPT.md`、`change-requests.md` 或 `runner.config.json` 時，可以不停止 Runner，但要立即提交。

**Runner 運行期間不要做**：`git add -A`、`git commit -a`、`git checkout .`、`git restore`，以及修改或刪除 AI 正在改的檔案。

---

## 8. 發佈代碼到公開 repo

```powershell
powershell -ExecutionPolicy Bypass -File tools\publish\Publish-Code.ps1 -DryRun   # 先預覽
powershell -ExecutionPolicy Bypass -File tools\publish\Publish-Code.ps1           # 正式發佈
```

- 只發佈已提交的內容，AI 相關檔案按 `tools\publish\publish-exclude.txt` 排除。
- 發現疑似機密會自動停止。
- 不需要停止 Runner。建議在每個模組打 tag 後發佈。

---

## 9. 人手檢查點

| 時間 | 做甚麼（約 10 至 30 分鐘） |
|---|---|
| 每日 | `status.ps1`；有連續 FAIL 或 BLOCKED 時查看原因 |
| 每週 | 看 `progress.md`、`blocked.md`、`change-requests.md`（AI 的建議要填「人手決定」欄） |
| 每個模組完成（日誌出現打 tag） | 按 README 從頭啟動系統試用；看 `docs\02-modules\<模組>\qa-report.md` 與截圖；`Show-Log.ps1 -Summary`；發佈代碼 |

---

## 10. 環境檢查

```powershell
powershell -ExecutionPolicy Bypass -File tools\bootstrap\Check-Prerequisites.ps1   # 工具是否齊全（PowerShell 7 一項可忽略）
Test-NetConnection 127.0.0.1 -Port 6379 | Select-Object TcpTestSucceeded           # Redis 是否運行
Get-PSDrive C, D | Select-Object Name, @{n='剩餘GB';e={[math]::Round($_.Free/1GB,1)}}  # 硬碟剩餘空間
```

- Redis 在登入時由「啟動」資料夾自動開啟，**不要關閉那個最小化視窗**。
- 硬碟剩餘少於 10 GB 時，先停止 Runner 再清理（C 槽的 Temp、`.codex`、`.claude` 資料夾，以及 SQL Server 中測試留下的資料庫）。
