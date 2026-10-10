# MyERP AI 自主開發 — 人手啟動指南

整個流程：準備 Windows VM → 安裝工具 → 登入 Claude Code / Codex → 建立工作目錄 → 試跑一輪 → 安裝自動啟動 → 每日看日誌。

---

## 1. 這套啟動套件包含什麼

| 檔案 | 用途 |
|---|---|
| `MASTER_PROMPT.md` | 總指令 v1.1（以你的原版為基礎，改動見 `docs/00-management/master-prompt-changelog.md`） |
| `CLAUDE.md` / `AGENTS.md` | Claude Code / Codex 自動讀取的入口檔，指向 MASTER_PROMPT |
| `state.json` | 機器可讀進度；Runner 見到 `status = DONE` 即停止 |
| `docs/00-management/` | backlog（已拆好 Phase 0 與 SYS 共 27 個工作單元）、progress、風險、BlueWhale 對照表（已預填 222 個功能、150 個介面、88 張表） |
| `tools/runner/` | 自動執行器：主循環、額度等待、Claude ⇄ Codex 自動切換、工作排程器安裝、狀態查詢 |
| `tools/bootstrap/` | 環境檢查、建立工作目錄 |
| `bluewhale-legacy-reference.zip`（另一個檔） | 藍微舊系統原始碼整理版：已轉 UTF-8、去掉 bin/obj/圖片/小程序，附資料庫結構摘要 |

---

## 2. 準備 Windows VM

建議規格：8 核、32 GB RAM、200 GB SSD，Windows 11 或 Windows Server 2022。**這部 VM 只用於本項目**，因為 AI 代理會以「略過權限確認」模式執行。開始前先做一次 VM 快照。

按次序安裝（以系統管理員 PowerShell）：

```powershell
winget install Microsoft.PowerShell
winget install Git.Git                     # 安裝時選 Git Credential Manager
winget install Microsoft.DotNet.SDK.10
winget install OpenJS.NodeJS.LTS
winget install Microsoft.Sqlcmd
dotnet tool install --global dotnet-ef
dotnet dev-certs https --trust
powercfg /change standby-timeout-ac 0
powercfg /change hibernate-timeout-ac 0
```

另外手動安裝：

- **SQL Server 2022**（Developer 版），實例名稱 `MSSQLSERVER2022`，驗證模式選 Windows，把你的 Windows 帳號加為 sysadmin。啟動 **SQL Server Browser** 服務（named instance 需要）。
- **Memurai Developer**（Redis 相容），設為自動啟動。

Git 設定並手動 push 一次，讓 Git Credential Manager 記住憑證：

```powershell
git config --global user.name  "snowwhite10"
git config --global user.email "你的 email"
```

---

## 3. 安裝並登入 AI CLI

```powershell
npm install -g @anthropic-ai/claude-code
claude          # 第一次互動式登入（Claude Pro / Max 訂閱或 API key），完成後輸入 /exit

npm install -g @openai/codex      # 可選，作為後備代理
codex login
```

只用其中一個時，在 `tools/runner/runner.config.json` 把另一個設 `"enabled": false`。兩個都啟用時，Runner 優先用排在前面的；它額度用完就自動改用另一個，兩邊都用完才等待 reset。

模型選擇：在 `runner.config.json` 的 `args` 加上 `"--model", "<模型名稱>"`（Claude）或 `"-m", "<模型名稱>"`（Codex）。

---

## 4. 檢查環境

把啟動套件解壓到暫存位置（例如 `C:\kit\MyERP`），然後：

```powershell
cd C:\kit\MyERP
pwsh -File tools\bootstrap\Check-Prerequisites.ps1
```

全部 PASS（Codex 可選）才繼續。

---

## 5. 建立工作目錄

把以下三個檔放在同一資料夾，例如 `C:\kit\ref`：

- `BlueWhaleERP_SRS.docx`
- `BlueWhaleERP_SDS.docx`
- `bluewhale-legacy-reference.zip`

```powershell
pwsh -File tools\bootstrap\Init-Workspace.ps1 -RepoPath C:\dev\MyERP -ReferenceDir C:\kit\ref
```

腳本會：建立或 clone repo → 複製套件 → 把參考文件放進 `docs\reference\`（確認被 .gitignore 排除）→ 第一次提交並 push。

---

## 6. 試跑，再交給它自己跑

**先手動試跑 2–3 輪**，確認 AI 真的在做事，而不是卡住或亂做：

```powershell
cd C:\dev\MyERP
pwsh -File tools\runner\Start-Runner.ps1 -Once                 # 用預設代理跑一輪
pwsh -File tools\runner\Start-Runner.ps1 -Once -Agent codex    # 指定代理
pwsh -File tools\runner\status.ps1
git log --oneline -5
```

每輪之後看：`state.json.nextAction` 是否具體、`git log` 有沒有新提交、`logs\ai-run\` 的日誌是否合理。

沒問題後安裝自動啟動：

```powershell
pwsh -File tools\runner\Install-RunnerTask.ps1
Start-ScheduledTask -TaskName MyERP-AI-Runner
```

預設在「使用者登入」時啟動。要做到「開機即跑」，請為 VM 設定自動登入（例如 Sysinternals Autologon），效果就是開機 → 自動登入 → Runner 啟動。不建議用「不需登入」模式，因為 Claude / Codex 的登入憑證與 Git 憑證都在使用者設定檔內，未登入時常會讀不到。

---

## 7. 日常操作

| 想做什麼 | 指令 |
|---|---|
| 看狀態與最後 10 行日誌 | `pwsh -File tools\runner\status.ps1` |
| 本輪結束後停止 | `pwsh -File tools\runner\Stop-Runner.ps1` |
| 立即停止（可能留下未提交變更） | `pwsh -File tools\runner\Stop-Runner.ps1 -Now` |
| 停止後重新啟動 | 刪除 `tools\runner\STOP`，然後 `Start-ScheduledTask MyERP-AI-Runner` |
| 移除自動啟動 | `pwsh -File tools\runner\Uninstall-RunnerTask.ps1` |
| 看 AI 的建議 | `docs\00-management\change-requests.md` |
| 看卡住的項目 | `docs\00-management\blocked.md` |

日誌樣式（每日一個檔，`logs\ai-run\YYYY-MM\YYYY-MM-DD.log`）：

```
2026-10-09 08:30 | -     | runner     | START   | Runner 啟動（PID 4120）
2026-10-09 08:31 | SYS   | 開發中     | OK      | 開始開發 登入 / JWT WU-SYS-003
2026-10-09 12:58 | SYS   | 額度       | WAIT    | 額度不足（claude），reset 10-09 18:00，等待至 10-09 18:05 執行；改用 codex
```

---

## 8. 建議的人手檢查點

AI 可以全自動，但以下時間點值得花 30 分鐘看一下，越早發現方向錯越便宜：

1. **Phase 0 完成後**（架構、骨架、conventions、ADR-001）：之後所有模組都建在這上面。確認專案結構、命名、測試能跑，再讓它繼續。
2. **第一個業務模組（MD）完成後**：確認標準列表頁 / 單據頁、權限、稽核、i18n、dark mode 的做法是你要的，因為之後每個模組都會照抄這個模式。
3. **第一次產出 SRS / SDS 後**：拿來和 BlueWhale 文件並排比較格式。
4. 之後每週一次：看 `progress.md`、`blocked.md`、`change-requests.md`。

---

## 9. 已知限制與注意事項

- **規模**：MASTER_PROMPT 定義 15 個模組加創意擴充，按每輪一個工作單元計，預計需要數百輪。訂閱方案的 5 小時與每週額度會是主要瓶頸，所以才建議同時啟用 Claude 與 Codex。
- **額度訊息格式**：Runner 用 `runner.config.json` 的 `quotaPatterns` 辨認額度用完。CLI 更新後訊息字眼可能改變；如見到日誌一直 FAIL 而實際是額度問題，把新訊息的關鍵字加進 `quotaPatterns`。解析不到 reset 時間時每 30 分鐘重試，不會停止。
- **誤判保護**：額度偵測只掃描輸出最後 25 行，避免 AI 在開發 Rate limiting 功能時輸出的文字被誤判為額度用完。
- **Codex 在 Windows**：Codex CLI 在原生 Windows 的支援仍在演進，如遇到 sandbox 或 shell 問題，先把它設為 `enabled: false`，只用 Claude Code。
- **權限**：兩個代理都以略過確認模式執行（`--dangerously-skip-permissions` / `--dangerously-bypass-approvals-and-sandbox`），這是無人值守的前提，也是 VM 必須專用並定期快照的原因。
- **舊系統原始碼的版權**：藍微原始碼只作業務邏輯參考，MASTER_PROMPT 已禁止複製；`docs/reference/` 不會提交到公開 repo。
