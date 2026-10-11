# 日誌分析（Log Analysis）

> MASTER_PROMPT 第 10 節：每完成一個模組，分析測試與執行期間的應用程式日誌，找出重複錯誤或慢查詢。
> 來源：`src/backend/MyERP.Api/logs/app-*.log`（CLEF JSON，見 docs/01-architecture/logging.md）。

## SYS 模組（WU-SYS-016a，2026-10-11）

**範圍**：`app-20261010.log`、`app-20261011.log`。內容涵蓋 SYS 開發期間的整合測試、真實 API E2E 與手動執行，共 982 筆 Warning 以上。

**方法**：按 `@mt`（訊息範本）及等級分組計數，再抽查例外類型與路徑。

| 次數 | 等級 | 訊息範本 | 來源判斷 | 處理 |
|---:|---|---|---|---|
| 212 | Error | Hosting failed to start | 啟動檢查測試故意令啟動失敗（萬用碼、JWT、連線字串設定錯誤） | 預期，不處理 |
| 145 | Error | Health check `database` …（72 次 "Database is not configured"、73 次 "Database is unreachable"） | 健康檢查測試故意不設定或指向不可達的資料庫 | 預期 |
| 109 | Warning | Migration operation … cannot be executed in a transaction | 初始 migration 設定資料庫定序（`ALTER DATABASE … COLLATE`），不能在交易內執行；每次重建測試資料庫都會出現 | 預期。該 migration 已只含此操作，符合 EF 建議 |
| 94 | Warning | Security:Jwt:SigningKey is not configured; using an ephemeral development key | Development／測試未設定簽章金鑰 | 預期。正式環境未設定時拒絕啟動（ADR-012） |
| 78 | Error | Unhandled exception for {Method} {Path} | 73 次是 `/__test/throw/unexpected`（錯誤處理測試）；2 次是連線字串未設定；3 次是 SQL 逾時 | 見下 |
| 58 | Warning | Captcha master code used for login | E2E 與整合測試使用 Development 萬用碼 | 預期，並已寫稽核 |
| 53 | Error | Captcha:MasterCode is configured in environment …; startup refused | 啟動檢查測試 | 預期 |
| 8 + 5 + 3 + 3 | Error | SQL 連線／命令逾時、查詢或儲存例外 | 後端整合測試與前端 `ng test` 同時執行，CPU 爭用（WU-SYS-010d 已記錄）；SQL 逾時 3 次落在測試用 `/api/v1/tst/orders` | 不屬產品缺陷。維持「後端與前端測試不並行」的做法 |
| 4 | Warning | Login failed for {Username} | 錯誤密碼測試 | 預期 |

**慢請求**：沒有 `HTTP … responded … in … ms` 的 Warning，即沒有請求超過 `App:SlowRequestMilliseconds`（1000 ms）。

**結論**：沒有重複出現的非預期錯誤，也沒有慢查詢。測試刻意製造的錯誤佔大多數。

**改善建議**（不阻擋）：日後可讓整合測試的 WebApplicationFactory 改寫入獨立日誌目錄，減少與手動執行混在一起，方便下一個模組分析。已列為觀察項目，暫不排程。
