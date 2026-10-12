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

## SYS 完整回歸（WU-SYS-016d，2026-10-11）

**現象**：回歸的第一次 `dotnet test --solution`（建置後立即執行）有 16 個整合測試失敗，全部是 SQL 命令逾時（35 秒），分兩波（22:02:29、22:03:35）。被卡住的查詢涉及互不相關的表：`sys_user`、`sys_user_company`、`sys_company`、`sys_number_sequence`，連目錄檢視 `sys.tables` 也逾時。該次整合測試耗時 2 分 33 秒，平常約 50 秒。

**排查**：
- SQL Server 錯誤記錄沒有「I/O 超過 15 秒」訊息；預設追蹤的 autogrow 事件最長 6 ms，排除磁碟擴檔停頓。
- 寫了一個監看腳本，每 2 秒查 `sys.dm_exec_requests` 中 `myerp_test` 等待超過 3 秒的請求及其阻擋源。在監看下重跑整合測試 1 次、全部後端測試 2 次（連同第一次重跑共 3 次），全部 599 / 599 通過，**伺服器端完全沒有超過 3 秒的等待**。
- 測試沒有在交易中執行 DDL，`tst_*` 表在 assembly fixture 一次建立；唯一的背景服務（過期通知清理）啟動後要 5 分鐘才首次執行，與此無關。

**判斷**：同一時間點、跨不相關表（含系統目錄）一起逾時，較像用戶端（測試行程）停頓，例如 MARS 連線在大量並行非同步下的執行緒集區飢餓（SqlClient 已知問題），或整機短暫資源爭用；不像資料列鎖。與 WU-SYS-002、WU-SYS-008 記錄的偶發逾時症狀相同。目前無法穩定重現，不屬產品缺陷。

**處理**：加入 backlog `WU-P0-014`（P2）：在整合測試中保留這次的監看方法（逾時時同時傾印 `dm_exec_requests`／執行緒集區狀態），並評估測試連線關閉 MARS、或限制整合測試並行數。回歸以三次全部通過為準。

## MD 模組（WU-MD-022a，2026-10-13）

**範圍**：`app-20261012.log`、`app-20261013.log`（時間戳為 UTC，涵蓋 2026-10-12 00:00 至 10-13 04:03 本地時間）。內容涵蓋 MD 開發期間（WU-MD-001～025）的整合測試、E2E 與 docgen，共約 1,050 筆 Warning 以上。

**方法**：以一次性 Node 腳本按等級＋`@mt` 分組計數，例外按首行分組，慢請求按路徑與狀態碼分組。

| 次數 | 等級 | 訊息範本 | 來源判斷 | 處理 |
|---:|---|---|---|---|
| 222 | Error | Hosting failed to start | 啟動檢查測試故意令啟動失敗（JWT 金鑰、連線字串、萬用碼設定錯誤） | 預期 |
| 184 | Warning | HTTP … responded … in … ms（超過 1000 ms） | 174 次是 `/health/ready`（116 次 503：測試故意指向不可達的資料庫，等連線逾時；58 次 200：測試主機首次開資料庫連線）；10 次是測試用 `/api/v1/tst/orders` | 預期。**沒有任何 `/api/v1/md/*` 請求超過 1000 ms** |
| 116 | Error | Health check … completed … | 健康檢查測試 | 預期 |
| 110 | Warning | Migration operation … cannot be executed in a transaction | 每次重建 `myerp_test` 的定序 migration | 預期（同 SYS） |
| 106 | Warning | Captcha master code used for login | E2E 與整合測試使用 Development 萬用碼 | 預期，已寫稽核 |
| 66 | Error | Unhandled exception for {Method} {Path} | 57 次是 `/__test/throw/unexpected`（錯誤處理測試）；9 次是 `/api/v1/tst/orders` | 見下 |
| 22 + 5 + 5 + 4 + 3 | Error | 連線錯誤、`DbCommand` 35 秒逾時、儲存／查詢例外、`SqlTransaction 已完成` | 集中在 3 個時段（10-12 00:46、05:49–05:51、11:37–11:38 本地時間），跨不相關的表（含 `sys.tables`）一起逾時；交易因逾時被伺服器結束，隨後 Rollback 拋 `SqlTransaction 已完成` | 與 SYS 完整回歸記錄的偶發逾時症狀相同，主機記憶體不足時整合測試大量並行連線所致（WU-MD 期間已改用 `--max-threads 2`）。不屬產品缺陷；監看方法已列 `WU-P0-014` |

**MD 專屬結果**：沒有 MD 端點的 500、沒有 MD 端點的慢請求，也沒有重複出現的業務錯誤。MD 的 409／422 屬預期的業務回應，只記 Debug，不在本表。

**結論**：沒有非預期的重複錯誤或慢查詢，不需修改產品代碼。測試日誌與手動執行仍混在同一檔案（SYS 已列為觀察項目），分析時以路徑區分，影響不大。
