# 本機預設啟動路徑

- 工作單元：WU-P0-015（續做中）
- 記錄時間：2026-10-16T05:40:35+08:00

## 契約與驗收

`tools/dev/Start-Dev.ps1` 僅使用 `launchSettings.json` 的 HTTPS profile（7180／5180）及 Angular 的 `npm start`（4200）。啟動前檢查埠、SQL Server Windows 驗證、資料庫及 migration 歷史、Development 種子帳號、Redis、HTTPS 憑證和前端套件。缺項時輸出中文原因與修正指令。`proxy.conf.json` 代理 `/api`、`/health`、`/openapi` 到 7180。

## 本輪檢查與優化

首次以 4200 既有程序測試時，腳本正確識別 `node` 與 PID，但 `Write-Error` 額外輸出 PowerShell 呼叫堆疊。改用紅色簡短訊息後，同一重現只顯示原因與修正步驟。檢查既有執行檔時，Debug 測試檔不存在而 Release 存在；`--no-build` 應指定 `-c Release`。

SQL Server 的 44 個 migration 原始檔與 `__EFMigrationsHistory` 44 筆一致。HTTPS 憑證檢查、前端 production 建置、前端 663 個測試、後端 Release 937 個測試通過。依 `web-design-guidelines` 檢視本次前端變更：只有開發伺服器設定與代理路徑，沒有可視 UI 程式碼可檢查；後續真實啟動須驗證 `/api` 回應不是 Angular HTML。

目前 4200 的 `node` 與 7180／5180 的後端程序在本輪開始前已存在，因此未停止既有程序，也未用其他埠替代。真實預設啟動、驗證碼／登入／登出 smoke 與 proxy 轉送仍待後續以空閒預設埠驗收。
