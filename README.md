# MyERP

企業級 ERP 系統，使用 .NET 10、Angular 22、SQL Server 2022 與 Redis。

## 前置需求

- Windows、.NET 10 SDK（`global.json` 指定版本）。
- SQL Server 2022 實例 `localhost\MSSQLSERVER2022`，目前 Windows 帳號可用整合驗證連線。
- Memurai 或相容 Redis 服務，監聽 `localhost:6379`。
- Node 24.21.0；`tools/scripts/Invoke-Frontend.ps1` 會使用 repo 內可攜版 Node。
- 首次準備：`& ./tools/scripts/Invoke-Frontend.ps1 npm ci`，`dotnet build src/backend/MyERP.sln -c Debug`，`dotnet ef database update -p src/backend/MyERP.Migrations -s src/backend/MyERP.Api`，`dotnet run --project src/backend/MyERP.Api -- --seed`，`dotnet dev-certs https --trust`。

## 啟動

在 repo 根目錄執行 `& ./tools/dev/Start-Dev.ps1`。腳本先檢查固定埠、SQL Server、migration、種子、Redis、HTTPS 憑證和前端套件，再啟動兩個服務。失敗時會顯示修正指令。

手動啟動時，開兩個 PowerShell 視窗，在 repo 根目錄分別執行：

```powershell
dotnet run --project src/backend/MyERP.Api --launch-profile https
& ./tools/scripts/Invoke-Frontend.ps1 npm start
```

前端為 `http://localhost:4200`，後端為 `https://localhost:7180`（另監聽 `http://localhost:5180`）。前端 `/api`、`/health`、`/openapi` 由 Angular 開發伺服器代理到 HTTPS 後端。

Development 測試帳號為 `admin`；其他角色帳號及密碼見 `src/backend/MyERP.Api/appsettings.Development.json` 的 `SeedUsers`，驗證碼萬用碼見同檔的 `Captcha:MasterCode`。種子只在 Development 模式執行。

## 常見問題

| 現象 | 處理 |
|---|---|
| 埠 4200、7180 或 5180 被佔用 | `Start-Dev.ps1` 會列出程序名稱及 PID；停止該程序後重試。 |
| HTTPS 憑證錯誤 | 執行 `dotnet dev-certs https --trust`；確認瀏覽器信任本機開發憑證。 |
| Redis 無法連線 | 啟動 Memurai 或相容服務，確認 `localhost:6379` 可連線。 |
| SQL 連線失敗 | 啟動 `MSSQL$MSSQLSERVER2022`，確認 Windows 帳號權限及資料庫名稱。 |
| 找不到資料表或種子帳號 | 依序執行上方 migration 與 `--seed` 指令。 |
| 驗證碼圖片載入失敗 | 確認 API 已啟動且 `https://localhost:7180/health/ready` 回 200，再查瀏覽器 Network 的 `/api` 請求。 |

開發規範見 [MASTER_PROMPT.md](MASTER_PROMPT.md)，進度見 [progress.md](docs/00-management/progress.md)。
