# Blocked 項目

> 同一工作單元連續失敗 3 次即記錄於此。每完成一個模組後回頭重試一次。

| 工作單元 | 標記時間 | 錯誤摘要 | 已嘗試的方法 | 重試記錄 | 狀態 |
|---|---|---|---|---|---|
| WU-P0-015 | 2026-10-16T05:46:55+08:00 | `Start-Dev.ps1` 退出：預設埠 4200 已被 node (PID 7504) 佔用；7180／5180 同由既有 Visual Studio API (PID 8100) 佔用。完整預設啟動與代理驗收無法執行。 | 05:40 建立腳本、代理與 README，確認 44 筆 migration、前端建置／663 測試及後端 937 測試；05:45 檢查既有程序，4200 `/health/ready` 回 Angular HTML、API 健康與驗證碼逾時；05:46 再執行腳本，仍以同一埠佔用錯誤退出。未停止既有互動程序，也未改用其他埠。 | 埠空閒後按 README 啟動，驗證直連及代理健康端點和驗證碼；WU-P0-016 建立真實啟動 smoke 後依 19.5 補驗；模組完成後按第 1 節回頭重試。 | BLOCKED |
| WU-PUR-005e3 | 2026-10-23T01:11:27+08:00 | 決標專項 2/2 通過，但 Release 整合全量連續三輪未能通過；前兩輪在低可用記憶體下於既有 SQL 案例命令逾時，本輪測試 SQL Server 實例服務已停止，無法連線或啟動。 | 前兩輪以單執行緒重跑全量，`SafetyStockTests` 等既有案例 SQL 命令 35～43 秒逾時；隔離失敗類案例 1/1 通過，DMV 當時無持續阻擋。本輪可用記憶體約 1.8 GB；`sqlcmd` 連 `localhost\MSSQLSERVER2022` 回共享記憶體連線失敗／登入逾時；`Get-Service` 顯示 `MSSQL$MSSQLSERVER2022` 為 Stopped，`Start-Service` 回無法開啟服務；`whoami /groups` 顯示 Administrators 為 deny only。未削弱測試或宣稱全量通過。 | 服務恢復後先確認 DMV 無長時間等待，再執行 `powershell -NoProfile -ExecutionPolicy Bypass -File tools/scripts/Invoke-IntegrationDiagnostics.ps1 -Configuration Release -MaxThreads 1`；全量通過後跑決標專項與帶 DB DocGen，更新 005e3 為 DONE。模組完成前依第 1 節回頭重試。 | BLOCKED |
