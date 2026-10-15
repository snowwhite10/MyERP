# 本機預設啟動路徑

- 工作單元：WU-P0-015（BLOCKED，待預設埠空閒重試）
- 記錄時間：2026-10-16T05:40:35+08:00

## 契約與驗收

`tools/dev/Start-Dev.ps1` 僅使用 `launchSettings.json` 的 HTTPS profile（7180／5180）及 Angular 的 `npm start`（4200）。啟動前檢查埠、SQL Server Windows 驗證、資料庫及 migration 歷史、Development 種子帳號、Redis、HTTPS 憑證和前端套件。缺項時輸出中文原因與修正指令。`proxy.conf.json` 代理 `/api`、`/health`、`/openapi` 到 7180。

## 本輪檢查與優化

首次以 4200 既有程序測試時，腳本正確識別 `node` 與 PID，但 `Write-Error` 額外輸出 PowerShell 呼叫堆疊。改用紅色簡短訊息後，同一重現只顯示原因與修正步驟。檢查既有執行檔時，Debug 測試檔不存在而 Release 存在；`--no-build` 應指定 `-c Release`。

SQL Server 的 44 個 migration 原始檔與 `__EFMigrationsHistory` 44 筆一致。HTTPS 憑證檢查、前端 production 建置、前端 663 個測試、後端 Release 937 個測試通過。依 `web-design-guidelines` 檢視本次前端變更：只有開發伺服器設定與代理路徑，沒有可視 UI 程式碼可檢查；後續真實啟動須驗證 `/api` 回應不是 Angular HTML。

目前 4200 的 `node` 與 7180／5180 的後端程序在本輪開始前已存在，因此未停止既有程序，也未用其他埠替代。真實預設啟動、驗證碼／登入／登出 smoke 與 proxy 轉送仍待後續以空閒預設埠驗收。

## 2026-10-16T05:45:05+08:00 接續驗收

工作目錄乾淨。4200 由 2026-10-14 啟動的 `node`（PID 7504）佔用；7180／5180 由 2026-10-15 經 `VsDebugConsole.exe` 啟動的 `MyERP.Api.exe`（PID 8100）佔用，屬既有互動開發程序，不擅自停止。執行 `Start-Dev.ps1` 正確列出 4200 的程序及 PID 並退出。當前 4200 的 `/health/ready` 回 200，但內容是 Angular HTML，不能視為 API 健康；7180 的 `/health/ready` 及 4200 的驗證碼請求均逾時。這些請求只用於辨識現有程序狀態，沒有以它們代替 README 啟動驗收。

預設埠確實空閒後，直接按 README 執行 `Start-Dev.ps1`，核對直連與代理的 `/health/ready` 回 200 且內容不是 HTML，`/api/v1/sys/auth/captcha` 回有效 challenge。隨後由 WU-P0-016 建立並執行登入／首頁／登出 smoke，再依 19.5 補驗 WU-P0-015。若互動開發程序仍在，保留程序，不另改埠。

## 2026-10-16T05:46:55+08:00 第三次嘗試

再次執行 `Start-Dev.ps1`，仍回報 4200 被 node PID 7504 佔用並以代碼 1 退出；7180／5180 仍被既有 Visual Studio API PID 8100 佔用。同一阻礙連續三輪，依總指令第 1 節標記 WU-P0-015 為 BLOCKED，詳見 blocked.md。下一輪先處理獨立的 WU-P0-017；預設埠空閒後再按上述驗收步驟重試本單元。
