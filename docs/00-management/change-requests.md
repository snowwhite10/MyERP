# 對 MASTER_PROMPT / Runner 的修改建議

> AI 不得修改 MASTER_PROMPT.md 與 Runner 核心檔。建議寫在這裡，由人手決定是否採納。

| CR | 日期 | 對象（章節 / 檔案） | 建議 | 理由 | 人手決定 |
|---|---|---|---|---|---|
| CR-000 | 2026-10-09 | MASTER_PROMPT 第 4 節 | 本機 Redis 為 Microsoft Open Tech Redis on Windows 3.2.100（2016），只可使用 Redis 3.2 支援的指令；所有 key 加前綴 myerp:；正式環境須用 Redis 6+ 或 Memurai | 本機無管理員權限，Memurai 安裝失敗 | 已採納 |
| CR-001 | 2026-10-10 | `tools/runner/lib/RunnerCommon.psm1` `ConvertFrom-ResetText` | 時區名稱解析前先把 IANA 名稱（如 `Asia/Taipei`、`America/Los_Angeles`）對應到 Windows ID（`Taipei Standard Time` 等），可用小對照表 | 本機（Windows Server 2019 + PowerShell 5.1）`FindSystemTimeZoneById('Asia/Taipei')` 找不到，回退為本機時間；本機剛好 UTC+8 故目前結果正確，但訊息若用其他時區會差幾小時。優先級：低 | 已採納（Runner v1.1 已實作） |
| CR-002 | 2026-10-10 | `Start-Runner.ps1`、`Get-GitDirtyCount` | `Get-GitDirtyCount` 回傳 -1（git 執行失敗）時，寫 FAIL 日誌並在 prompt 提示「無法取得 git 狀態，請先檢查」，而不是當成乾淨 | 目前 -1 不大於 0，被視為「工作目錄乾淨」，可能掩蓋 repo 損壞或鎖檔（index.lock）問題。優先級：中 | 已採納（Runner v1.1 已實作） |
| CR-003 | 2026-10-10 | `Start-Runner.ps1` `Wait-Until` | 在 `Wait-Until` 迴圈內亦檢查跨日並寫「每日啟動」START | 等待額度（可長達數小時）或長輪次跨午夜時，START 要到下一輪開始才寫，早上查看日誌可能誤以為 Runner 未運作。優先級：低 | 已採納（Runner v1.1 已實作） |
| CR-004 | 2026-10-10 | `Start-Runner.ps1` 單一實例 | Mutex 名稱改用 `Global\MyERP_AI_Runner` | `Local\` 只在同一登入工作階段有效；若同時使用 `-AtStartup`（session 0）與手動在互動桌面啟動，可能出現兩個 Runner 同時修改 repo。優先級：低 | 不採納 |
| CR-005 | 2026-10-10 | `tools/runner/Stop-Runner.ps1 -Now` | taskkill 前先確認該 pid 的程序是 powershell / pwsh 且命令列包含 `Start-Runner.ps1` | 狀態檔的 pid 可能已過期並被其他程序重用，會誤殺無關程序。優先級：低 | 不採納 |
| CR-006 | 2026-10-10 | `tools/runner/Install-RunnerTask.ps1` | 與 commit f489cc9「use Windows PowerShell 5.1」一致：預設使用 `powershell.exe`，加 `-UsePwsh` 參數才用 pwsh | 目前腳本優先選 pwsh，與最近決定不一致；兩者皆相容，只為一致性。優先級：低 | 不採納 |
| CR-007 | 2026-10-10 | MASTER_PROMPT 6.1 資料庫定序 | 把 `Chinese_Taiwan_Stroke_100_CI_AS_SC` 改為 `Chinese_Traditional_Stroke_Count_100_CI_AS_SC` | 原名稱在 SQL Server 2022 不存在（建庫回「定序無效」）；後者是 `Chinese_Taiwan_Stroke` 的 100 版正式名稱，保留不分大小寫與補充字元，已依 ADR-006 實作。優先級：中 | |
| CR-H01 | 2026-10-10 | 環境 | Node.js 已由人手升級至 v22.16.0（nvm-windows）。R-016 可關閉；AI 不得自行執行 nvm | ADR-001 需要 Node 24 | 已完成 |
| CR-H02 | 2026-10-13 | 前端開發環境 | 前端缺少開發 proxy：ng serve / npm start 時 /api 請求送到 4200，驗證碼載入失敗、無法登入。請新增 src/frontend/proxy.conf.json（/api → https://localhost:7180，secure:false），在 angular.json serve 設定 proxyConfig，並在 README 寫清楚本機啟動步驟（後端、前端、測試帳號）；加一個 E2E 或冒煙測試確保 npm start 後可載入驗證碼 | 人手驗收發現 | 待 AI 修正（P1） |
