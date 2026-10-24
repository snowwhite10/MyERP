# WU-P0-016 真實啟動冒煙測試

## 需求與設計

依 MASTER_PROMPT 19.4、19.5 與 README：使用固定 4200／7180／5180 埠，以 `dotnet run --project src/backend/MyERP.Api --launch-profile https` 與 `Invoke-Frontend.ps1 npm start` 啟動 Development 服務，經 Angular proxy 取得真實驗證碼，使用 Development 種子帳號登入首頁，再登出。`tests/smoke` 獨立於 production 靜態伺服器 E2E；若 SQL、Redis、種子、憑證或埠不可用，前置檢查顯示原因並拒絕執行。測試憑證僅在執行時讀取既有 Development 設定，不輸出到日誌或成果檔。

| ID | 類別 | 情境與斷言 | 層級 | 狀態 |
|---|---|---|---|---|
| P0-SMOKE-001 | 正常 | 首頁導向登入、真實驗證碼圖片載入且刷新有 200 回應、登入顯示 admin 與首頁、登出後受保護首頁再導向登入 | E | 已寫測試，待 SQL 恢復實跑 |
| P0-SMOKE-002 | 邊界 | 固定埠已有監聽者時列出 PID 並停止，不重用或改埠 | I | 已寫前置檢查，待實跑 |
| P0-SMOKE-003 | 錯誤 | SQL 停止、migration 未套用、種子不可查、Redis 不通、憑證未受信、前端依賴缺失時拒絕測試；瀏覽器意外 5xx／console error／pageerror 導致失敗 | I/E | SQL 停止拒絕已驗；其餘待 SQL 恢復實跑 |
| P0-SMOKE-004 | 權限／狀態 | 登出後不應仍可進入受保護首頁 | E | 已寫測試，待實跑 |
| P0-SMOKE-005 | 重複與衝突／部分結果／關聯／檔案 | 不適用：本單元不建立或批次修改業務資料、不處理上傳檔案 | - | 不適用 |

執行：`& ./tools/scripts/Invoke-Frontend.ps1 tests/smoke npm ci`；`& ./tools/scripts/Invoke-Frontend.ps1 tests/smoke npm test`。Chromium 使用 `.tools/ms-playwright`。前置檢查失敗不算冒煙通過。

## 2026-10-24 設計審查與驗證

以 Playwright `webServer` 直接啟動 README 所列的兩條命令；固定埠、禁止重用既有伺服器。審查發現初版 SQL 前置檢查只驗退出碼，即使 admin 計數為 0 也會放行；已補計數判斷。PowerShell 5.1 讀無 BOM 的中文腳本會解析錯誤，已將 `preflight.ps1` 保存為 UTF-8 BOM。

`npm ci`、`npm run typecheck`、`playwright test --list` 通過；`npm test` 前置檢查因 `MSSQL$MSSQLSERVER2022` 停止而明確拒絕，真實瀏覽器流程與資料庫驗收未通過。SQL 恢復後先跑 `npm test`，再核對前置檢查覆蓋的 migration／種子與 README 啟動路徑，通過後才標記單元完成。

## 2026-10-24T12:12:25+08:00 第二輪檢查

再次嘗試啟動 SQL Server，現有帳號被拒絕開啟服務。前置檢查補上 `__EFMigrationsHistory` 與本機 migration 清單比較，防止資料庫落後時進入登入測試；並複核 SQL 實例字串只有一個反斜線。PowerShell 解析 0 錯誤、UTF-8 BOM 保留、migration 檔案識別 55 筆、TypeScript 型別檢查及 Playwright 測試發現 1/1 通過。實際前置檢查在 SQL 停止時如預期拒絕；migration、種子和真實登入／登出仍需服務恢復才能驗證。本單元累計未完成 2/3。
