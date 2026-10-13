# MASTER_PROMPT 變更記錄

## v1.1（2026-10-09，人手）

| # | 章節 | 變更 | 原因 |
|---|---|---|---|
| 1 | 檔頭 | 加版本號；說明代理可能是 Claude Code 或 Codex | Runner 會在一方額度用完時自動切換 |
| 2 | 0 | 加入舊系統原始碼 `docs/reference/legacy-source/` 的用途與限制；說明 reference-notes 已預填 | 原版未提及原始碼；預填可省去 AI 首幾輪閱讀 700KB 文件的時間 |
| 3 | 1 | 新增「每輪邊界」「代理中立」 | 一輪做太多會撞上額度 / 超時，留下半成品；兩個代理交替時不能依賴對話記憶 |
| 4 | 2 | Runner 改為人手預先提供，AI 只檢查不修改 | Runner 是啟動 AI 的前提（雞與蛋問題）；AI 修改執行中的 Runner 有令自己停止的風險 |
| 5 | 3 | 新增 `state.json` 欄位定義，加入 `idCounters` | 避免不同輪 / 不同代理分配重複 ID |
| 6 | 4 | 版本：.NET 10 LTS；Angular 跟隨 ng-zorro 支援版本；以 ADR-001 固定 | 「最新 LTS」在 Angular 沒有明確定義，且會隨時間漂移 |
| 7 | 11 | 新增 CI 範圍說明 | GitHub 託管 runner 沒有 SQL Server 2022 named instance 與 Redis |
| 8 | 15.2 | 註明 BlueWhale ID 不一致，不要照抄 | User 與部分 Settings 的 ID 漏了模組編號 |
| 9 | 15.8 | 一致性檢查加入「次序」 | BlueWhale SRS 4.4 與 SDS 4.2 介面次序不一致 |
| 10 | 18 | 禁止修改 Runner 核心檔、禁止複製舊系統代碼 | 對應第 2、4 項 |

## v1.2（2026-10-13，人手）

| # | 章節 | 變更 | 原因 |
|---|---|---|---|
| 1 | 19（新增） | 本機可運行性：一鍵啟動、前端 proxy、README、走人手路徑的冒煙測試、列入提交條件、不得繞過環境問題 | 人手驗收發現 npm start 無法載入驗證碼（CR-H02）；AI 測試用自己的埠與設定，從未驗證人手的啟動路徑 |
