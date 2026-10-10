# Runner 檢查報告（WU-P0-001）

> 日期：2026-10-10　檢查者：AI（claude）　對象：`tools/runner/`（commit f489cc9）
> 依據：MASTER_PROMPT 第 2.1 節。Runner 核心檔不得由 AI 修改，所有建議見 `change-requests.md` CR-001 起。

## 1. 2.1 要求逐項核對

| # | 2.1 要求 | 實作位置 | 結果 | 備註 |
|---|---|---|---|---|
| 1 | 開機 / 登入自動啟動，註冊工作排程器 | `Install-RunnerTask.ps1`（AtLogOn，可選 `-AtStartup`；RestartCount 999 / 5 分鐘；MultipleInstances IgnoreNew） | 符合 | 預設為登入觸發，需配合 VM 自動登入；理由已寫在腳本說明 |
| 2 | 避免重複開啟 | `Start-Runner.ps1` 具名 Mutex `Local\MyERP_AI_Runner` | 符合（有保留） | `Local\` 只在同一登入工作階段有效，見 CR-004 |
| 3 | 主循環反覆呼叫 AI CLI | `Start-Runner.ps1` while 迴圈 + `Invoke-AgentRound`（stdin 傳 prompt） | 符合 | 多代理按 `agents` 次序切換 |
| 4.1 | 偵測額度用完 | `Test-QuotaExhausted`（只掃描輸出尾段 25 行） | 符合 | 實測 7 種訊息格式均判定正確，正常輸出不誤判 |
| 4.2 | 解析 reset 時間並寫入日誌 | `ConvertFrom-ResetText`；WAIT 日誌含 reset 與等待至時間 | 符合（有保留） | 支援 epoch、相對時間、時刻 + 日期 + 時區；IANA 時區名在本機找不到，見 CR-001 |
| 4.3 | 等待至 reset + 5 分鐘 | `resetBufferMinutes = 5` | 符合 | |
| 4.4 | 解析不到時每 30 分鐘重試 | `unknownResetRetryMinutes = 30` | 符合 | |
| 4.5 | 重試仍不足繼續等待，不結束 | 額度分支不計入失敗、不 break | 符合 | |
| 5 | 斷點續跑，以 state.json 為準 | Runner 只讀 `status`、`currentModule`；狀態檔只存執行狀態 | 符合 | |
| 6 | 每輪結束檢查 git status，寫 Warning，下一輪先處理 | `Get-GitDirtyCount` + `Build-Prompt` 的 `{{DIRTY_NOTE}}` | 符合（有保留） | 日誌狀態只能用 FAIL（無 WARN），說明欄以「Warning：」開頭；git 失敗時回傳 -1 被當成乾淨，見 CR-002 |
| 7 | 每日首次啟動寫 START | 啟動時寫 START；跨日於下一輪開始前補寫「每日啟動」 | 符合（有保留） | 長時間等待額度 / 長輪次跨午夜時，START 會延後到下一輪才寫，見 CR-003 |
| 8 | `status.ps1` 顯示狀態、模組、工作單元、最後 10 行日誌 | `status.ps1` | 符合 | 以 pid 存活判斷，Runner 當掉時顯示「已停止」 |

## 2. 實測記錄

在 Windows PowerShell 5.1 載入 `lib/RunnerCommon.psm1` 測試（`ConvertFrom-ResetText`、`Test-QuotaExhausted`、`Get-GitDirtyCount`）：

| 輸入訊息 | 判定額度用完 | 解析出的 reset |
|---|---|---|
| `Claude AI usage limit reached\|1791700000` | 是 | epoch 換算本機時間 |
| `You've hit your limit · resets 3pm (Asia/Taipei)` | 是 | 當日 15:00（時區找不到，當作本機時間） |
| `5-hour limit reached ∙ resets 11:30pm` | 是 | 當日 23:30 |
| `Weekly limit reached · resets Oct 13, 9am (Asia/Taipei)` | 是 | 10-13 09:00 |
| `You've hit your usage limit. Try again in 2 hours 13 minutes.` | 是 | 現在 + 2:13 |
| `... try again at 6:05 PM.` | 是 | 當日 18:05 |
| `Session limit reached` | 是 | 無 → 30 分鐘後重試 |
| `All good, done.` | 否 | - |

本機時區 `China Standard Time`（UTC+8），與 `Asia/Taipei` 同一偏移，因此目前時區回退行為結果正確。

## 3. 其他觀察（非 2.1 要求）

- 輪次逾時 120 分鐘（`roundTimeoutMinutes`），逾時以 `taskkill /T /F` 終止整個程序樹，下一輪提示先處理未提交變更：合理。
- 非額度失敗有退避（2 / 5 / 15 / 30 分鐘，連續 6 次後 60 分鐘）：合理。
- `Stop-Runner.ps1 -Now` 以狀態檔的 pid 直接 taskkill，pid 可能已被重用，見 CR-005。
- `Install-RunnerTask.ps1` 優先使用 `pwsh`，但最近提交表示改用 Windows PowerShell 5.1；兩者皆相容，但建議一致，見 CR-006。
- AI 最後輸出若在尾段提及 "usage limit reached" 等字眼可能誤判為額度用完；已以尾段掃描降低風險。AI 方面的應對：每輪最終總結避免使用這些英文片語。

## 4. 結論

Runner 符合 2.1 全部要求，可以投入使用；6 項改善建議（皆為低 / 中優先，不阻擋開發）已寫入 `change-requests.md`。
