# 進度總覽

> 每完成一個工作單元更新。完成度 = 已完成工作單元 / 該模組全部工作單元。

| 階段 | 模組 | 狀態 | 完成度 | 工作單元（完成 / 全部） | 測試（通過 / 全部） | Tag | 最後更新 |
|---|---|---|---|---|---|---|---|
| P0 | 基礎 | 進行中（015／016 受阻） | 94% | 34 / 36（015／016 BLOCKED；033 父項不計） | DocGen 22 / 22；Debug 建置 0 Warning／Error；真實 SQL 隔離產出 0 Error／573 Warning | - | 2026-10-27 |
| P0 | SYS | 完成 | 100% | 24 / 24（WU-SYS-010 拆為 010a–d；011 拆為 011a–b；012 拆為 012a–b；016 拆為 016a–d） | 後端 599 / 599；前端 432 / 432；E2E 65 / 65（模擬，3 略過）、真實 API 3 / 3；docgen 0 Error | v0.1.0-sys | 2026-10-11 |
| P1 | MD | 完成（P3 URL 優化 029a–f 完成） | 100%（既定模組）；P3 URL 7/7 切片 | 51 / 51（既定列表改造）；029a–f 完成（026／028a 待續） | 前端 714/714；模擬 E2E 全量 442 通過／5 略過；DocGen 15/15、隔離一致性 0 Error／574 Warning（無 SQL schema）；Debug 建置 0 警告／錯誤；真實 SQL smoke 未驗 | v0.2.0-md | 2026-10-27 |
| P1 | INV | 進行中 | 91% | 61 / 67（020d 設計完成；020a 待 SQL；011c 真實畫面與 SQL 同資料聯查待續；015b 待 PUR／SD） | 本輪 DocGen 測試 7/7、`git diff --check`；SQL 與產品未驗，產出一致性因文件佔用未完成 | - | 2026-10-24 |
| P1 | PUR | 進行中（005f3e／005e3／e4／f2a2／006a 受阻） | 58% | 40 / 69（008d 三單比對設計完成並拆 008a／b／c；SQL 仍停止） | 本輪 DocGen 7/7、`git diff --check`；最近產品離線驗證：前端 699/699、模擬 E2E 335 通過／5 略過、Shared 381/381、架構 75/75、後端 Debug 建置 0 警告／錯誤。最近 SQL 整合全量通過為 005e2 的 618/618 | - | 2026-10-24 |
| P1 | SD | 進行中（001 受阻；002a 報價 transaction store checkpoint、003a 轉單待續、004a2b SQL checkpoint） | 49% | 19 / 39（002a、003a、004a2b 未結案；001 BLOCKED；不計已拆分父項） | 報價 schema 真實 SQL 1/1、store 真實 SQL 3/3；後端全量 1296/1296；既有訂單專項 31/31 | - | 2026-10-29 |
| P1 | FI | 進行中（基礎設計） | 16 個設計切片 | 16 / 待拆分（含 BI 來源契約；EP-FI 其餘仍待拆） | 本輪 DocGen 7/7、`git diff --check`；未有 FI 產品或 SQL 驗收 | - | 2026-10-24 |
| P2 | CO | 進行中（設計基線） | 1 個設計切片 | 1 / 待拆分 | DocGen 7/7、`git diff --check`；無 CO 產品或 SQL 驗收 | - | 2026-10-24 |
| P1 | BI | 進行中（報表／儀表板／排程設計） | 6 個設計／文件切片 | 6 / 待拆分 | DocGen 8/8、隔離一致性 0 Error／576 Warning，BI 子功能警告 0；無 BI 產品或 SQL 驗收 | - | 2026-10-25 |
| P3 | PP | 進行中（設計基線） | 2 個設計切片 | 2 / 待拆分 | 本輪 DocGen 7/7、`git diff --check`；無 PP 產品或 SQL 驗收，產出一致性因文件佔用未完成 | - | 2026-10-25 |
| P3 | QM | 進行中（設計基線） | 1 個設計切片 | 1 / 待拆分 | DocGen 7/7、`git diff --check`；無 QM 產品或 SQL 驗收 | - | 2026-10-24 |
| P3 | PM | 進行中（設計基線） | 1 個設計切片 | 1 / 待拆分 | DocGen 7/7、`git diff --check`；產出文件被佔用，無 PM 產品或 SQL 驗收 | - | 2026-10-24 |
| P4 | CRM | 進行中（設計基線） | 1 個設計切片 | 1 / 待拆分 | DocGen 7/7、`git diff --check`；無 CRM 產品或 SQL 驗收 | - | 2026-10-24 |
| P4 | HR | 進行中（設計基線） | 1 個設計切片 | 1 / 待拆分 | DocGen 7/7、`git diff --check`；無 HR 產品或 SQL 驗收 | - | 2026-10-24 |
| P4 | PS | 進行中（設計基線） | 1 個設計切片 | 1 / 待拆分 | DocGen 7/7、`git diff --check`；產出文件被佔用，無 PS 產品或 SQL 驗收 | - | 2026-10-24 |
| P4 | SCM | 進行中（預測來源契約設計） | 3 個設計切片 | 3 / 待拆分 | DocGen 7/7、`git diff --check`；產出文件被佔用，無 SCM 產品或 SQL 驗收 | - | 2026-10-24 |

## 進行中

- 2026-10-29T01:09:09+08:00 WU-SD-002a transaction store checkpoint：先紅燈缺 `SalesQuotationDraftStore`，後接入交易內 Draft 建立、版本檢查整組行替換與軟刪；真實 SQL 3/3、後端全量 1296/1296、Debug 建置 0 警告／錯誤。原 Domain 狀態列舉跨層問題已修復。SYS 單號、MD 快照、action scope、SYS 審批及受權 HTTP 仍待續，002a 不結案。

- 2026-10-29T00:51:18+08:00 WU-SD-002a 報價 schema checkpoint：SQL 服務恢復後，上一輪兩個未追蹤檔案接入 EF 報價模型與可逆 `Sales_QuotationDraft` migration，已套用 `myerp`。先紅燈（缺 `sal_quotation`），再驗兩表、公司範圍外鍵、單號／行唯一索引及日期／刪除／數量檢查，真實 SQL 1/1 通過；REQ_DS43.174–175 已分配。後端全量 1293/1293；帶真實 SQL 的 DocGen 隔離產出 0 Error／573 Warning。草稿 CRUD、SYS 審批、受權 HTTP、轉單及完整業務驗收未完成，002a 保持 IN_PROGRESS，003a 仍待來源。

- 2026-10-28T21:10:26+08:00 WU-SD-003a 診斷收尾 checkpoint：背景 SQL 採樣的 `Add-Content` 加入有界 IOException 重試，`Receive-Job` 錯誤明確回傳採樣失敗 2；固定短暫／永久檔鎖、背景工作錯誤傳遞等 5/5 通過。完整真實 SQL 整合重跑 659/659，耗時 5m 41s，診斷包裝回傳 0、採樣無 `SQLCMD_ERROR`。原競爭程序未定位；報價轉單仍依賴 BLOCKED WU-SD-002a 的真實報價持久化，003a 未結案。

- 2026-10-28T21:00:30+08:00 WU-SD-003a 全量驗證 checkpoint：明確以 2.5 GB 門檻及單一測試執行緒啟動診斷，全量真實 SQL 整合 659/659、0 失敗／略過，耗時 5m 44s。SQL 曾回報低記憶體，但程序持續完成工作。診斷腳本在測試結束後收集 SQL 採樣背景工作時遇短暫檔案佔用 IOException，命令回傳 1；測試摘要完整保留於忽略提交的 logs。003a 仍缺依賴 BLOCKED 002a 的真實報價轉單，腳本收尾亦待修驗，未結案。

- 2026-10-28T20:51:35+08:00 WU-SD-003a 建置伺服器資源 checkpoint：訂單真實 SQL／HTTP 專項 31/31。發現上一輪殘留的 7 個無存活父程序 MSBuild 節點與 VBCSCompiler，執行 `dotnet build-server shutdown` 後可用記憶體由約 1.25 GB 升至 2.84 GB；全量診斷腳本於 2.83 GB 仍按 3 GB 門檻回傳 3，未啟動全量。互動式 IDE／DevHub／瀏覽器保持運行。003a 尚未結案；記憶體達門檻後需跑全量並修實際失敗，報價轉單仍依賴 BLOCKED 002a。

- 2026-10-28T20:47:28+08:00 WU-SD-003a 全量驗證資源 checkpoint：主機 32 GB 中只有約 1.42 GB 可用，主要佔用為 IDE、DevHub 與瀏覽器；SQL 程序回報低記憶體。整合診斷腳本加入預設 3 GB 前置檢查，實際呼叫立即回傳 3 並說明原因，未啟動易停滯的全量測試；可顯式覆寫門檻。訂單專項真實 SQL 31/31、Debug 建置 0 警告／錯誤。全量 SQL 結果仍缺，003a 未結案；報價轉單仍依賴 BLOCKED 002a。

- 2026-10-28T20:43:05+08:00 WU-SD-003a 完整 EF 行查詢計劃 checkpoint：直接對 store 的追蹤行查詢 `ToQueryString()` 執行真實 SQL SHOWPLAN XML，確認完整投影仍使用 `(company_id, order_id, line_no)` 索引 Seek；訂單專項 31/31、Architecture 230/230、Debug 建置 0 警告／錯誤。主機可用記憶體約 1.3 GB，與先前全量低記憶體停滯相近，未取得全量摘要；報價轉單仍依賴 BLOCKED 002a，003a 未結案。

- 2026-10-28T20:35:55+08:00 WU-SD-003a 跨單鎖回歸 checkpoint：保持 A 單 Serializable 編輯交易開啟時，B 單明細插入與提交成功，兩單持久版本／備註／行號正確；暫移 FORCESEEK 時同測試 SQL 1222，恢復後 1/1，訂單專項 30/30、Debug 建置 0 警告／錯誤。主機可用記憶體約 1.3 GB，未重跑全量整合；完整 EF 行查詢計劃和報價轉單仍待續，003a 未結案。

- 2026-10-28T20:28:17+08:00 WU-SD-003a 訂單行索引 checkpoint：真實 `myerp_test` SHOWPLAN 證實原 company/order 查詢在小表選擇 `pk_sal_order_line` Clustered Index Scan，與先前 `RangeS-S` 死鎖一致；加現有 `(company_id, order_id, line_no)` 索引的 `FORCESEEK` 後計劃為該索引 Seek 加 PK Lookup。Store 的追蹤載入只取本單行，既有並行受權 HTTP 與 SQL 專項以雙執行緒連續四次 29/29、Architecture 230/230、Debug 建置 0 警告／錯誤。尚未以專門跨單插入競態證明死鎖消失；主機僅約 1.2 GB 可用記憶體，未重跑全量整合。003a 未結案，報價轉單仍依賴 BLOCKED 002a。

- 2026-10-28T20:19:54+08:00 WU-SD-003a 訂單行鎖 checkpoint：`system_health` 死鎖圖確認 Serializable 編輯持有 `sal_order_line` 聚集鍵 `RangeS-S`，另一筆行插入等 `RangeI-N` 並持有編輯觸發器需讀的 `sal_order` X 鎖。將 store 的表頭與行分開載入，表頭鎖和版本檢查保持不變；雙執行緒 SalesOrder 專項連續兩次 29/29，Debug 建置 0 警告／錯誤。修改前本輪同組亦通過，故尚未證明間歇死鎖消失；主機可用實體記憶體約 1.35 GB，暫無全量摘要。003a 未結案，下一輪先驗實際行查詢計劃和壓力競態，記憶體恢復再跑全量；報價轉單仍依賴 BLOCKED 002a。

- 2026-10-28T20:10:52+08:00 WU-SD-003a 診斷 checkpoint：主機起始可用記憶體約 1.5 GB、SQL 低記憶體旗標為 1；全量整合約兩分鐘無摘要，可用記憶體低於 0.8 GB 後中止。診斷腳本加入 SQL 與主機記憶體 DMV 同步採樣，查詢實測通過；Debug 建置 0 警告／錯誤。另發現 SalesOrder 雙執行緒兩次 28/29：受權 HTTP 並發編輯 SQL 1205／HTTP 500，SQL 死鎖圖顯示訂單行觸發器表頭讀取與另一交易行插入的鎖循環；同一測試單跑 1/1、單執行緒整組 29/29。需先定位死鎖雙方呼叫及修復，再於記憶體恢復後重跑全量；003a 未結案，報價轉單仍依賴 BLOCKED 002a 真實報價。

- 2026-10-28T19:59:48+08:00 WU-SD-003a SQL 環境診斷 checkpoint：上一輪 migration 逾時的同一 SYS 審批單測重跑 1/1 通過，訂單真實 SQL／HTTP 29/29 通過。全量 `--max-threads 2` 期間多次見 `WAIT_SLEEP_LOWMEM_GUARD`、SQL 低記憶體旗標，主機可用實體記憶體降至約 0.5 GB；診斷採樣器 SQL 登入亦逾時。約七分鐘仍無測試摘要，為避免主機壓力擴大而中止，不能算全量通過。Debug 建置 0 警告／錯誤；003a 未結案，下一輪先恢復主機記憶體後重跑全量並保留摘要，報價轉單仍依賴 BLOCKED 的 002a 真實報價。

- 2026-10-28T19:46:58+08:00 WU-SD-003a 全量 SQL 驗證 checkpoint：`--max-threads 2` 在 SYS 審批及 MD 單位測試逾時；單跑 SYS 測試仍在 `TestDatabase.InitializeAsync` migration 的 `__EFMigrationsHistory` INSERT 逾時，證明不是訂單測試或測試方法並行的單一原因。SQL DMV 見資料庫 RCSI DDL 等待、主機可用實體記憶體約 767 MB 且 SQL 回報低記憶體。未改產品或 migration；Debug 建置 0 警告／錯誤、Architecture 230/230。003a 未結案，下一輪先量測 SQL 阻塞／資源並恢復真實 SQL 驗證；報價轉單仍依賴 BLOCKED 的 002a 真實持久化。

- 2026-10-28T16:02:23+08:00 WU-SD-003a 受權 HTTP 命令競態 checkpoint：新測試重現兩個同版本 PUT 一個 200、一個 SQL 1205／HTTP 500；Serializable 命令改在 action scope 讀取前以公司／有效表頭 UPDLOCK/HOLDLOCK 序列化，重跑得到 200／409，兩個 DELETE 得 204／404。獨立 SQL 確認永久單號、備註、本人及軟刪。訂單 SQL/HTTP 29/29、Architecture 230/230、Debug 建置 0 警告／錯誤；測試修復後連續兩次通過。報價轉單與全量整合仍待續，003a 未結案。


- 2026-10-28T15:52:20+08:00 WU-SD-003a 雙連線修改／刪除 checkpoint：四種交易先後與提交／回滾組合，另一連線在持鎖時 1222；提交後舊版本 409 或已刪 404，回滾後原版本可重試。獨立 SQL 核對永久單號、版本、軟刪旗標和行數量；訂單專項 29/29、Debug 建置 0 警告／錯誤。這是 store 層驗證；受權 HTTP service 競態、報價轉單及全量整合仍待續，003a 未結案。

- 2026-10-28T15:44:56+08:00 WU-SD-003a 受權 HTTP 真實參照 checkpoint：真實 SYS 用戶／角色、已核准 MD 客戶、啟用物料、公司幣別／銷項稅率和 SYS 規則下，POST 201 得永久號 HC001 與商業快照，GET 200，PUT 後版本 2，舊版本 DELETE 409，現版 DELETE 204，後續 GET 404。獨立 SQL 確認本人、修改後備註、單號保留與軟刪。訂單 SQL/HTTP 25/25、Debug 建置 0 警告／錯誤；雙連線修改／刪除競態、報價轉單及全量整合仍待續，003a 未結案。

- 2026-10-28T15:37:40+08:00 WU-SD-003a action-scope HTTP checkpoint：同公司 VIEW 公司範圍可讀他人訂單，但 EDIT／DELETE 本人範圍對該 ID 均回 404 與 `SAL.ORDER.NOT_FOUND`；本人 Draft DELETE 回 204，SQL 證明他人單未刪且本人單已軟刪。測試資料需顯式給 Version 1；訂單 SQL/HTTP 24/24、Debug 建置 0 警告／錯誤。真實 MD 受權建改、雙連線競態、報價轉單與全量整合仍待續，003a 未結案。

- 2026-10-28T15:30:04+08:00 WU-SD-003a 跨公司 HTTP checkpoint：同一真實 SYS VIEW 用戶獲兩公司授權；首公司已保存訂單明細 200，另一公司同 ID 404、列表總數 0。訂單 SQL/HTTP 23/23、Debug 全方案 0 警告／錯誤；受權人工建讀改刪及真實 MD 參照、action 資料範圍、雙連線競態、報價轉單與全量整合仍待續，003a 未結案。

- 2026-10-28T15:24:04+08:00 WU-SD-003a 受權 HTTP 權限 checkpoint：真實 SYS 用戶的公司 VIEW-only 角色可取得空列表，POST／PUT／DELETE 均回 403 和 `COMMON.PERMISSION.DENIED`。訂單 SQL/HTTP 22/22、Debug 全方案 0 警告／錯誤；全量整合仍未通過。已保存單據的受權 CRUD、跨公司與各 action 資料範圍 404、雙連線修改／刪除競態及報價轉單待續；003a 未結案。

- 2026-10-28T15:17:16+08:00 WU-SD-003a 列表測試 checkpoint：真實 SQL 覆蓋日期上下界、Pending、客戶、去空白關鍵字／單號篩選及外分店不計入總數；八種無效頁碼、頁長、日期、客戶、狀態及排序均拒絕。專項 21/21、Debug 建置 0 警告／錯誤。受權 HTTP、跨公司／缺權 403、PUT/DELETE 雙連線競態、報價轉單及全量整合待續；003a 未結案。

- 2026-10-28T15:11:05+08:00 WU-SD-003a 列表 checkpoint：新增 GET 訂單列表，VIEW 公司／資料範圍先於篩選、計數與投影，白名單排序以 ID 打破同日平手。測試先紅後綠；真實 SQL 驗兩筆同日穩定分頁、範圍外 0 筆及無效排序，匿名 HTTP 驗 401。SalesOrder 21/21、Debug 全方案 0 警告／錯誤；DocGen、Shared、Architecture 全量測試組通過，SQL 全量約 2.5 分鐘無摘要後中止，未計通過。受權 HTTP、雙連線寫入競態、其餘篩選測試與報價轉單待續；003a 未結案。

- 2026-10-28T15:01:14+08:00 WU-SD-003a 草稿修改／刪除 checkpoint：新增 PUT／DELETE HTTP 與 EDIT／DELETE 獨立 SYS action scope，Serializable 交易內先查範圍再做版本化全行替換或軟刪。真實 SQL 驗 Branch 範圍外 404、編輯保存、舊版本 409、軟刪；匿名 HTTP 401，OpenAPI 路徑註冊。SalesOrder 20/20、Architecture 230/230，Debug 建置 0 警告／錯誤。受權 HTTP 建讀改刪、跨公司及缺權 403、雙連線競態、列表、報價轉單與全量整合待續；003a 未結案。

- 2026-10-28T14:51:45+08:00 WU-SD-003a HTTP 讀／建 checkpoint：新增 `GET /api/v1/sal/orders/{id}` 與 `POST /api/v1/sal/orders`，分別用 VIEW／CREATE 權限；GET 在 SQL 查詢套用 SYS VIEW 資料範圍，範圍外 404。先紅後綠真實 SQL 驗 Branch、Department、Self，匿名 HTTP 401 與 OpenAPI 路徑已測。SalesOrder 專項 19/19、Architecture 230/230、Shared 381/381、Debug 建置 0 警告／錯誤。受權 HTTP 建單／跨公司、EDIT／DELETE 資料範圍與版本 409、列表及報價轉單仍待續；全量整合未驗收，003a 未結案。

- 2026-10-28T14:42:58+08:00 WU-SD-003a 資料範圍 checkpoint：上一輪未提交的 migration 與實作已核對，人工建單保存 SYS CREATE 權限的 owner／branch／department，既有單據 owner 從 `created_by` 回填；真實 SQL 驗 Branch、Department、Self 篩選，專項 6/6。migration 已套用 `myerp`；Debug 建置 0 警告／錯誤，Architecture 230/230、Shared 381/381、DocGen 22/22。全量後端整合有 SYS／MD SQL 命令逾時，兩項各自單跑 1/1 通過，故全量未驗收。受權 HTTP／Swagger、讀取／修改／刪除資料範圍與版本競態、報價轉單仍待續；003a 未結案。

- 2026-10-27T20:40:31+08:00 WU-SD-003a 雙連線 SYS 取號 checkpoint：首筆人工建單持有交易取 `SO001` 時，第二連線取同規則號碼被 SQL 鎖逾時 1222 擋下；首筆提交後重試保存 `SO002`，兩張訂單及 SYS 流水值 2 一致。首次測試因 EF 關閉探針連線而用回預設 30 秒命令逾時，固定連線後通過。訂單 SQL 16/16、Shared 381/381、Architecture 230/230、DocGen 22/22、Debug 建置 0 警告／錯誤。受權 HTTP／Swagger、跨公司／範圍及版本競態、報價轉單和全量整合待續；003a 未結案。

- 2026-10-27T20:34:30+08:00 WU-SD-003a 幣別／稅率穩定性 checkpoint：真實 MD 幣別精度、日期匯率、銷項稅率及稅碼啟用狀態在 Serializable 建單交易內均阻擋第二 SQL 連線改寫（鎖逾時 1222）；提交後匯率可更新，訂單保留原匯率與稅率。訂單 SQL 15/15、Debug 建置 0 警告／錯誤。報價轉單、受權 HTTP／Swagger、雙連線取號及全量整合待續，003a 未結案。

- 2026-10-27T20:28:57+08:00 WU-SD-003a 客戶穩定性 checkpoint：真實 MD 已核准客戶查詢在 Serializable 訂單交易內，另一 SQL 連線改名被鎖逾時拒絕；提交後可改名，訂單保留原名稱。首跑暴露測試 DbContext 漏註冊付款設定模型，補齊後專項及訂單 SQL 14/14、Debug 建置 0 警告／錯誤。幣別／稅率競態、報價轉單、受權 HTTP／Swagger、雙連線取號及全量整合待續，003a 未結案。

- 2026-10-27T16:38:46+08:00 WU-SD-003a MD 穩定性 checkpoint：新增真實 MD 物料跨連線競態測試，先紅燈證實 Create 使用 ReadCommitted，再改 Serializable。讀取物料後另一連線更新名稱被 SQL 鎖逾時拒絕，提交後可更新，訂單行保留原名稱。訂單 SQL 13/13、Architecture 230/230、Shared 381/381、DocGen 22/22、Debug 建置 0 警告／錯誤；全量整合約四分鐘無摘要後中止，未計通過。其餘 MD 參照競態、報價轉單、受權 HTTP／Swagger 和雙連線取號待續；003a 未結案。

- 2026-10-27T16:30:27+08:00 WU-SD-003a 交易 checkpoint：先紅後綠新增 `SalesOrderCreateService`，在 `SAL.ORDER.CREATE` 權限檢查後，以同一交易取 SYS 單號、組裝 MD 契約商業快照並保存 Draft。真實 SQL 測試驗保存金額、缺權拒絕與插入失敗時 SYS 流水號回滾；訂單專項 12/12、Architecture 230/230、Shared／DocGen 通過、Debug 建置 0 警告／錯誤。全方案整合約兩分鐘無摘要後中止，未計全量通過。MD 回應在此測試為固定契約資料，真實 MD 競態、報價轉單、受權 HTTP／Swagger及雙連線待續；003a 未結案。

- 2026-10-27T16:17:55+08:00 WU-SD-003a 商業快照 checkpoint：測試先因缺少組裝器編譯失敗，再驗人工訂單 MD 客戶／物料／幣別／閏日匯率／銷項稅率身份與金額。發現預設小數位會與 MD 公司設定不一致，改傳入幣別精度，補原幣表頭溢出守衛；專項 3/3、Architecture 230/230、Debug 全方案 0 警告／錯誤。尚需 SYS 單號、MD 組裝與 store 同交易保存、報價轉單、受權 HTTP、雙連線及全量整合；003a 未結案。

- 2026-10-27T16:12:22+08:00 WU-SD-003a MD 物料身份 checkpoint：為訂單行商業快照新增 `ISalesItemLookup`，真實 SQL 測試先因實作缺失編譯失敗，後驗同公司有效、停用、外公司、缺失及軟刪 1/1。Debug 全方案 0 警告／錯誤、Architecture 227/227、Shared 381/381、DocGen 22/22；`git diff --check` 通過。客戶、幣別匯率與稅率已有 MD 契約，尚需同交易組裝、SYS 單號、報價轉單、受權 HTTP 和雙連線；全量整合未重跑，003a 未結案。

- 2026-10-27T16:05:03+08:00 WU-SD-003a 人工草稿持久化 checkpoint：真實 SQL 測試先因缺少 store 編譯失敗，再驗交易內建立、整行替換（單號不變、版本遞增）、軟刪、過期版本、回滾、跨公司不可見／不可建及 Approved 不可編輯。訂單 schema 11/11、Debug 全方案建置 0 警告／錯誤，Shared／Architecture／DocGen 通過。全量整合 `--max-threads 2` 約四分鐘無摘要後中止，未計通過；MD 商業快照、SYS 單號同交易、報價轉單、受權 HTTP 與雙連線仍待續，003a 未結案。

- 2026-10-27T15:53:47+08:00 WU-SD-003a 物理刪除 checkpoint：真實 SQL 紅燈證實可直接 DELETE 訂單、釋放永久單號及報價來源並級聯刪行。新增可回滾的表頭 DELETE trigger，覆蓋有效 Draft、已軟刪 Draft、Approved；拒絕後原單號、來源、狀態、行仍保存。migration 已套用 `myerp`，訂單 schema 8/8，Debug 全方案建置 0 警告／錯誤，Shared／Architecture／DocGen 通過；`--max-threads 2` 全量整合久候後中止，未計通過。人工草稿 CRUD、MD／SYS 交易、報價轉單、受權 HTTP 與雙連線仍待續；003a 未結案。

- 2026-10-27T15:41:51+08:00 WU-SD-003a 表頭內容 checkpoint：真實 SQL 測試先證實 Approved 可改客戶名稱，再以可回滾 migration 凍結 Pending／Approved／Cancelled／已刪訂單的商業欄位，仍允許 Draft 編輯與合法狀態轉移。訂單 schema 7/7，migration 已套用 `myerp`；Debug 建置 0 警告／錯誤，全方案 Shared／Architecture／DocGen 通過，整合久候後中止，故未計全量通過。INV 10 萬筆估值低負載單跑 1/1 通過，顯示全量久候與負載有關。人工草稿 CRUD、MD／SYS 報價轉單、雙連線與受權 HTTP 仍待續；003a 未結案。

- 2026-10-27T15:30:38+08:00 WU-SD-003a 表頭狀態 checkpoint：接續上一輪未提交的紅燈 SQL 測試，證實 Approved 可回 Draft；新增可回滾 migration，拒絕非法狀態回退與已軟刪 Draft 復活，失敗後原狀仍保存。migration 已套用 `myerp`，訂單 schema 6/6、Debug 建置 0 警告／錯誤；全方案的單元／架構／DocGen 通過，整合在主機負載下久候而中止，故未宣稱全量通過。003a 的草稿 CRUD、MD／SYS 轉單、雙連線及受權 HTTP 仍待續，本輪不結案。

- 2026-10-27T11:47:54+08:00 WU-SD-003a 行狀態 checkpoint：紅燈證實 Approved 訂單行可直接 UPDATE；新增可回滾的行 INSERT／UPDATE／DELETE trigger，拒絕已核准或已軟刪父單的行變更。migration 已套用 `myerp`，真實 SQL schema 5/5、Debug 建置 0 警告／錯誤；全量中 Shared、Architecture、DocGen 通過，但 INV `HundredThousandMovementsUseValuationIndex` 在 3 分鐘後連線關閉、`NegativePoolAndOverflowReturnBusinessErrors` 在高負載逾時，遂停止全量；後者單獨低負載重跑通過 1/1。未將全量標為通過；003a 的草稿 CRUD、MD／SYS 轉單、雙連線與受權 HTTP 仍待續，本輪不結案。

- 2026-10-27T11:30:43+08:00 WU-SD-003a 來源配對 checkpoint：紅燈證實人工訂單可保存報價行、報價訂單可保存無來源行；新增可回滾的行 INSERT／UPDATE trigger，拒絕兩種混合來源並保證失敗整單回滾。新 trigger 暴露 EF `OUTPUT` 相容問題，兩張訂單表改用 trigger 相容儲存設定後真實 SQL 4/4。migration 已套用 `myerp`，down script 含 DROP TRIGGER；Debug 建置 0 警告／錯誤、Shared／Architecture／DocGen 測試通過。全量整合在高負載下久候後中止，003a 草稿 CRUD、MD／SYS 轉單、雙連線及受權 HTTP 仍待續，本輪不結案。

- 2026-10-27T11:17:09+08:00 WU-SD-003a 來源不可變 checkpoint：紅燈證實已保存來源 ID 可被 UPDATE 清除；新增可回滾 migration 的訂單及訂單行 SQL trigger，拒絕清除或改配來源，普通修改與軟刪仍可執行，migration 已套用 `myerp`。真實 SQL 3/3、架構 227/227、SALES 種子角色專項 1/1、Debug 建置 0 警告／錯誤。全量整合再遇既有 SafetyStock SQL 逾時與 OtherReceipt 422／500 差異，在高負載下中止；003a 草稿 CRUD、MD／SYS 轉單、雙連線及受權 HTTP 仍待續，本輪不結案。

- 2026-10-27T10:54:02+08:00 WU-SD-003a SQL 恢復後重試，完成訂單表結構 checkpoint：先紅後綠，`sal_order`／`sal_order_line` migration 已套用 `myerp`，真實 SQL 2/2 驗證來源唯一、同公司 FK、數量 CHECK 與無效行整單回滾，分配 REQ_DS43.172–173；Debug 全方案建置 0 警告／錯誤、`git diff --check`。全量後端中 Shared／Architecture／DocGen 通過，整合在高負載下出現既有 SafetyStock SQL 逾時與 OtherReceipt 錯誤，故未宣稱全量通過；測試已中止。訂單草稿 CRUD／轉單、受權 HTTP、雙連線、down migration 和 004a2b 來源餘量仍待續，003a 不結案。2026-10-27T11:00:27+08:00 帶真實 SQL 的隔離 DocGen 一致性產出 0 Error／573 Warning。

- 2026-10-27T10:40:35+08:00 WU-SD-004a2b 保存行 SQL 選取 checkpoint：測試先紅後綠，新增要求既有 Serializable 交易的公司篩選獨立 COUNT／完整選取，產生升序來源行鎖計劃；真實 SQL 專項 2/2、Architecture 227/227、Debug 建置 0 警告／錯誤、`git diff --check`。查明 `sal_order`／`sal_order_line` 尚未由受阻 WU-SD-003a 建立，故來源鎖、完整餘量、HTTP、SYS／INV 回滾與雙連線競態未驗，下一輪先恢復該依賴。

- 2026-10-27T10:34:46+08:00 WU-P0-035 修復 DocGen schema 查詢逾時：原查詢 45 秒仍逾時，改以 `sys` 目錄一次查 PK/FK 後約 0.7 秒返回 1209 個業務欄位。真實 SQL 回歸 1/1、DocGen 22/22、Debug 建置 0 警告／錯誤；完整帶連線隔離產出 0 Error／573 Warning，REQ_DS43.170–171 與出貨兩表一致。剩餘警告屬其他模組來源未完成；WU-SD-004a2b 的 HTTP／交易／競態仍待下一輪。

- 2026-10-27T10:26:51+08:00 WU-SD-004a2b 接續上輪未提交的出貨持久化模型及 migration，還原誤改的受保護 Runner 文件；SQL Server 恢復後將 migration 套用 `myerp`，實際核對兩張表、同公司 FK、7 個 CHECK、六位數量精度，分配 REQ_DS43.170–171。Architecture 227/227、DocGen 單元 21/21、Debug 建置 0 警告／錯誤；PUR 舊測試資料違反軟刪約束，修正後專項 1/1。全量整合在主機負載下有既有 MD SQL 逾時（專項單跑通過），帶連線 DocGen 連續兩次查 schema 逾時，故不宣稱全量驗收。草稿 CRUD、完整 SQL 鎖／餘量、SYS／INV 回滾、競態、預設埠 smoke 仍待後續；本輪為可建置的部分提交。

- 2026-10-27T05:57:46+08:00 WU-SD-004a2b9 將鎖後現單持久行身份比對與完整 Draft／Pending 餘量決策併為單一純入口，拒絕 Posted。測試先紅後綠 3/3；Architecture 226/226、Shared 381/381、DocGen 21/21，Debug 建置 0 警告／錯誤。SQL Server 仍 Stopped；真實交易鎖、SYS／INV 原子回滾、雙連線競態與預設埠 smoke 仍待服務恢復。

- 2026-10-27T05:54:31+08:00 WU-SD-004a2b8 完成出貨已保存行與鎖後餘量快照的純身份比對：現單持久行 ID、來源訂單行、數量及 Draft／Pending 狀態必須逐行一致，缺失、額外、重複或變更即拒絕。先紅後綠 4/4；Architecture 223/223、Shared 381/381、DocGen 21/21、Debug 建置 0 警告／錯誤。SQL Server 仍 Stopped；同交易真實讀取／升序鎖、SYS／INV 原子回滾、雙連線競態及預設埠 smoke 待服務恢復。

- 2026-10-27T05:49:58+08:00 WU-SD-004a2b7 完成出貨已保存行選取完整性守衛：交易讀取的獨立計數須等於選取筆數、持久行 ID 唯一且有效，並沿用現單 Draft／Pending 的升序來源鎖計劃。測試先紅後綠 3/3；Architecture 219/219、Shared 381/381、DocGen 21/21，Debug 建置 0 警告／錯誤，隔離一致性 0 Error／574 Warning。SQL Server 仍 Stopped；交易內實際計數／選取／鎖、SYS／INV 回滾、雙連線競態及預設埠 smoke 待 SQL 恢復。

- 2026-10-27T05:45:26+08:00 WU-SD-004a2b6 完成已保存出貨行的升序鎖計劃：Draft／Pending 各可取升序唯一來源訂單行，拒絕空、超 1000 行、外單、已刪、混合狀態及非法來源／數量。測試先紅後綠，專項 4/4；Architecture 216/216、Shared 381/381、DocGen 21/21、隔離一致性 0 Error／574 Warning、Debug 建置 0 警告／錯誤。SQL Server 仍 Stopped；完整 SQL 選取、實際鎖、HTTP、SYS／INV 原子回滾與雙連線競態仍待 WU-SD-004a2b。

- 2026-10-27T05:40:47+08:00 WU-SD-004a2b5 完成送審的同快照保存數量守衛：從目前 Draft 全部保存行導出數量，拒絕缺失、刪除、混合狀態及超 1000 行，並以 Posted／其他 Pending 預留驗證餘量。測試先紅後綠；送審專項 2/2、Architecture 212/212、Shared 381/381、DocGen 21/21、隔離一致性 0 Error／574 Warning、Debug 建置 0 警告／錯誤。SQL Server 仍 Stopped；完整 SQL 選取、升序鎖、HTTP、SYS／INV 回滾與雙連線競態待 WU-SD-004a2b。

- 2026-10-27T05:35:58+08:00 WU-SD-004a2b4 完成最終核准的同快照保存數量檢查：從目前 Pending 已保存行導出本單數量，拒絕缺失、超 1000 行及超訂單餘量，呼叫方不可另傳較小數量。測試先紅後綠；快照專項 7/7、Architecture 210/210、Shared 381/381、DocGen 21/21、隔離一致性 0 Error／574 Warning、Debug 建置 0 警告／錯誤。SQL Server 仍 Stopped；交易內完整選取、升序鎖、HTTP、SYS／INV 回滾與雙連線競態仍待 WU-SD-004a2b。

- 2026-10-27T05:31:32+08:00 WU-SD-004a2b3 修正最終核准出貨快照完整性：指定的目前 ID 必須有已保存有效 Pending 行，任一同 ID 行已刪或處於 Draft／Rejected／混合狀態即拒絕。測試先紅後綠；快照專項 5/5、Architecture 208/208、Shared 381/381、DocGen 21/21，Debug 建置 0 警告／錯誤。SQL Server 仍 Stopped；完整 SQL 選取、升序鎖、HTTP、SYS／INV 回滾與競態留在 WU-SD-004a2b。

- 2026-10-27T05:27:11+08:00 WU-SD-004a2b2 完成已保存出貨行的純餘量快照組裝：Posted 歷史持續消耗、其他有效 Pending 預留、目前 Pending 排除一次，Draft／Rejected／已刪 Pending 不預留；拒絕缺失／重複來源行、非法數量及超訂單總量。測試先紅後綠 4 個專項；Architecture 207/207、Shared 381/381、DocGen 21/21、隔離一致性 0 Error／574 Warning（SQL schema 未驗）、Debug 建置 0 警告／錯誤。SQL Server 仍 Stopped；004a2b 的 migration、真實交易鎖、HTTP、SYS／INV 原子回滾及雙連線競態待 SQL 恢復。

- 2026-10-27T05:21:35+08:00 WU-P0-034 接續上一輪五個未提交檔，完成 DocGen Mermaid PNG 跨次快取：圖源及 lockfile／CLI／Node／Chromium 指紋變更時重畫；PNG 必須有有效區塊、非零尺寸與 CRC，損壞重畫、渲染失敗不快取。PNG 校驗測試揭露原測試 fixture 的 IDAT CRC 錯誤，修正後 DocGen 21/21；Debug 全方案建置 0 警告／錯誤。完整隔離產出連跑兩次，均 0 Error／574 Warning，第二次 23.31 秒，快取 83 PNG。SQL Server 仍 Stopped，schema 與真實 smoke 未驗；出貨交易 WU-SD-004a2b 待 SQL 恢復。

- 2026-10-27T01:14:59+08:00 WU-SD-004a2b1 完成出貨純狀態規則：Draft 編輯／刪除／送審、Rejected 修訂回 Draft、Pending SYS 決議、Posted 不可修改；送審與最終核准須重新確認來源訂單 Approved，來源失效時仍可駁回以釋放預留。測試先紅後綠 24 個專項；Architecture 203/203、Shared 381/381、DocGen 15/15，Debug 建置 0 警告／錯誤。SQL Server 仍 Stopped；隔離 DocGen 在 SDS 階段久候後中止，未取得本輪完整一致性報告。004a2b 的 migration、交易鎖、授權／HTTP、SYS／INV 原子回滾及雙連線競態仍未驗收。

- 2026-10-27T01:03:55+08:00 WU-SD-004a2a 完成出貨草稿的離線欄位檢查：訂單／業務日期、1–1000 行、唯一正行號、來源行／倉庫／可選儲位及正數六位數量；允許同一來源行拆成多個出貨行。先紅後綠 13 個專項；Architecture 179/179、Shared 381/381、DocGen 15/15、隔離一致性 0 Error／574 Warning（無 SQL schema）、Debug 建置 0 警告／錯誤。SQL Server 仍 Stopped；004a2b 須建立表與真實交易，在鎖內從保存資料組成餘量快照並驗雙連線競態，真實 smoke/schema 亦待驗。

- 2026-10-27T00:51:34+08:00 WU-SD-004a1 完成出貨訂單行純數量檢查：重複來源行先聚合、按六位小數比較，最終核准時本單 Pending 預留只計一次。測試先紅後綠，補 1000／1001 行、單微量超額、缺失／重複快照及非法精度；Architecture 166/166、Shared 381/381、DocGen 15/15、隔離一致性 0 Error／574 Warning（無 SQL schema）、Debug 建置 0 警告／錯誤。SQL Server 2022 仍 Stopped，現有帳號啟動被拒；004a2 須在交易鎖內從保存單據形成快照，真實 SQL／HTTP、smoke、schema 一致性仍待驗。

- 2026-10-27T00:36:45+08:00 WU-MD-029f 完成物料分類／會計科目樹搜尋 URL 狀態，並關閉 WU-MD-029 父項。分享連結、返回／前進還原已提交關鍵字；搜尋草稿不改 URL，空白及不適用的分頁／排序參數清除。切公司保留關鍵字並以新公司標頭重讀完整樹；命中子節點保留上層，無伺服器分頁。樹頁 URL／QA 專項 12/12、前端 714/714、全量模擬 E2E 442 通過／5 略過；14 張重截圖逐張核對後還原無關像素變動，既有 24 張 QA 圖仍保留。DocGen 15/15、隔離一致性 0 Error／574 Warning、Debug 建置 0 警告／錯誤。首輪全量 E2E 的既有登入案例時序失敗已補等新驗證碼載入並斷言第二次請求，專項連跑 10/10、最終全量通過。SQL Server 2022 仍 Stopped，真實 smoke 與 schema 未驗。

- 2026-10-26T19:44:21+08:00 WU-MD-029e 完成付款條件／資金帳戶／收支類別／工序類別／工序五路由 URL 查詢狀態。分享連結、返回／前進還原已提交關鍵字、頁碼、筆數、排序與適用的類型／類別；搜尋草稿不改 URL，切公司頁碼回 1、工序清除舊公司類別，非法參數及超出後端位移上限的頁碼回預設。保留各頁排序白名單、付款試算及參照下拉。URL 專項先紅後綠、最終 10/10；五頁 QA 20/20、50 張三語亮暗截圖逐張審查，無新增 P1／P2。前端 714/714、全量模擬 E2E 438 通過／5 略過、Shared 381/381、Architecture 160/160、DocGen 15/15、隔離一致性 0 Error／574 Warning、Debug 建置 0 警告／錯誤。SQL Server 仍 Stopped，真實 smoke 與 schema 未驗；下一輪先查 SQL，若仍停止做 029f 樹搜尋 URL 切片。

- 2026-10-26T19:22:41+08:00 WU-MD-029d 完成倉庫／幣別／稅別列表 URL 查詢狀態：分享連結與返回／前進還原已提交關鍵字、頁碼、筆數和排序；搜尋草稿不改 URL，切公司保留關鍵字並重設頁碼，非法參數回安全預設。保留儲位、匯率、稅率／試算子介面。URL 專項 E2E 6/6、三頁 QA 12/12，8.17／8.79／8.83 共 30 張三語亮暗、正常／空列表／無結果／500 截圖逐張審查，無新增 P1／P2。前端 714/714、全量模擬 E2E 428 通過／5 略過、DocGen 15/15、隔離一致性 0 Error／574 Warning、Debug 建置 0 警告／錯誤。SQL Server 仍 Stopped，真實 smoke 與 schema 未驗；下一輪先查 SQL，若仍停止做 029e 付款條件／資金帳戶／收支類別／工序類別／工序 URL 切片。

- 2026-10-26T15:36:12+08:00 WU-MD-029c2 完成物料／計量單位列表 URL 查詢狀態：分享連結與返回／前進還原已提交關鍵字、頁碼、筆數與排序；搜尋草稿不改 URL，切公司保留關鍵字並重設頁碼，非法參數回預設。專項 E2E 4/4、兩頁 QA 8/8，8.11／8.45 共 20 張三語亮暗、正常／空列表／無結果／500 截圖逐張審查，無本次變更新增的 P1／P2。前端 714/714、全量模擬 E2E 422 通過／5 略過、DocGen 15/15、隔離一致性 0 Error／574 Warning、Debug 建置 0 警告／錯誤。SQL Server 仍 Stopped，真實 smoke 與 schema 未驗；下一輪先查 SQL，若仍停止做 029d 倉庫／幣別／稅別 URL 切片。

- 2026-10-26T14:33:25+08:00 WU-MD-029c1 完成客戶／供應商列表 URL 查詢狀態：分享連結與返回還原已提交關鍵字、頁碼、筆數及排序；搜尋草稿不改 URL，切公司保留關鍵字並重設頁碼，非法／負數／溢出頁碼回預設。專項 E2E 4/4，8.1／8.6 三語亮暗、正常／空列表／無結果／500 共 20 張截圖逐張審查，無新 P1／P2。前端 714/714、模擬 E2E 首輪 417 通過／5 略過／1 個既有物料分類案例間歇失敗，單獨重跑 6/6、第二次全量 418 通過／5 略過；後端離線單元 556/556、DocGen 15/15、隔離一致性 0 Error／574 Warning、Debug 建置 0 警告／錯誤。SQL Server 仍 Stopped，真實 smoke 與 schema 未驗；下一輪先查 SQL，若仍停止做 029c2 物料／計量單位 URL 切片。

- 2026-10-26T11:08:39+08:00 WU-MD-029b 完成供應商分類與物料品牌 URL 查詢狀態：分享連結還原已提交關鍵字、頁碼、筆數及排序，搜尋草稿不改 URL，返回還原查詢，切公司保留關鍵字且頁碼回 1，非法參數回退預設。兩路由 E2E 先 4/4 失敗、啟用設定後 4/4 通過；8.33／8.39 的正常、空列表、無結果、400／500、驗證及保存錯誤共 32 張三語亮暗截圖逐張審查，無新 P1／P2。前端 714/714、模擬 E2E 全量 414 通過／5 略過，QA 專項補強後 8/8；DocGen 15/15、隔離一致性 0 Error／574 Warning（無 SQL schema）、Debug 建置 0 警告／錯誤。SQL Server 仍 Stopped，真實 smoke 與 schema 未驗；下一輪先查 SQL，否則按 029c 拆分客戶／供應商、物料／計量單位 URL 切片。

- 2026-10-26T10:43:55+08:00 WU-MD-029a 接續上一輪未提交的客戶分類 URL 狀態切片：僅此路由同步已提交關鍵字、頁碼、每頁筆數與排序；分享連結、返回及切公司重設頁碼、非法參數回退有 E2E 2/2。畫面審查發現載入 400／500 後仍顯示「無結果」，已在共用分類頁隱藏錯誤時的空表；單元測試保護。修正 QA 截圖等待主題與載入動畫，介面 8.30 三語亮暗、空列表、無結果、400／500、驗證及保存錯誤共 16 張逐張審查，無未修正 P1／P2。前端 714/714、模擬 E2E 全量 406 通過／5 略過、MD 專項 6/6；後端 Shared 381/381、Architecture 160/160、Debug 建置 0 警告／錯誤；DocGen 15/15、隔離一致性 0 Error／574 Warning（無 SQL schema）。SQL Server 仍 Stopped，P0-015／016 真實 smoke 與 schema 待恢復；下一輪先查 SQL，否則做 029b 供應商分類與物料品牌 URL 狀態。

- 2026-10-26T06:08:06+08:00 WU-MD-027b5 完成非分頁樹頁列表共用切片：物料分類與會計科目接 `erp-list-page`，保留完整樹、前端搜尋保留上層、原 testId 與權限；`table-query` 只適用伺服器分頁，接到樹會拆散父子節點，故不接入。新增三語空列表／無結果、500 traceId 與重試，保存 500 亦保留表單並顯示 traceId；刪除執行中顯示「處理中」。先紅後綠，前端 714/714、模擬 E2E 全量 404 通過／5 略過，最後兩頁專項 8/8；8.36／8.87 共 24 張三語、亮暗、空／無結果及錯誤截圖逐張檢視，無未修正 P1／P2。DocGen 測試 15/15、Debug 建置 0 警告／錯誤；06:11:34+08:00 隔離一致性檢查 0 Error／574 Warning（無 SQL schema）。SQL Server 仍 Stopped，P0-015／016 真實 smoke 及 schema 尚未驗。027b 及 027 父項已關閉；若 SQL 繼續停止，下一輪做 WU-MD-029 URL 查詢狀態。

- 2026-10-26T05:43:54+08:00 WU-MD-027b4 完成付款條件、資金帳戶、收支類別、工序類別、工序及物料屬性六路由列表共用切片：三頁元件改用 `erp-list-page`、排序白名單及已提交篩選值；保留付款到期日試算、參照資料、權限與原 testId。空列表／無結果／500 traceId 與重試已分流。前端 708/708、模擬 E2E 396 通過／5 略過（新增專項 24/24），六路由 60 張亮暗及三語截圖逐張檢視，無 P1／P2。隔離 DocGen 初次因本輪 test-plan 使用「同上」有 6 個 `UNKNOWN_REQ_F`，修正追溯欄後重跑 0 Error／574 Warning；DocGen 測試 15/15。SQL Server 仍 Stopped，真實 smoke 與 schema 未驗；下一輪先查 SQL，否則做 027b5。

- 2026-10-26T00:45:17+08:00 WU-MD-027b3 完成倉庫、幣別、稅別列表共用切片：三頁改用 `erp-list-page` 與排序白名單，搜尋只在提交後套用，空列表／無結果／載入錯誤分流，500 顯示 traceId 並可重試；儲位、匯率、稅率／試算子對話框與原 testId 保留。先紅後綠；前端 707/707、模擬 E2E 372 通過／5 略過（專項 12/12），三頁共 30 張亮暗與三語截圖逐張檢視，無 P1／P2。DocGen 15/15、Debug 建置 0 警告／錯誤、隔離一致性 0 Error／574 Warning。SQL Server 仍 Stopped，真實 smoke 與 schema 待服務恢復；下一輪先查 SQL，否則做 027b4。

- 2026-10-25T19:53:00+08:00 WU-MD-027b2 完成物料／計量單位列表共用切片：`erp-list-page` 與排序白名單、已提交搜尋值、空／無結果／載入錯誤分流、500 traceId 與重試；單位換算及物料子對話框保留。刪除執行中按鈕顯示三語「處理中」。新增測試先紅後綠；前端 704/704、全量模擬 E2E 360 通過／5 略過、最後兩頁專項 14/14；8.11／8.45 共 20 張亮暗及三語截圖已逐張審查。DocGen 15/15、隔離一致性 0 Error／574 Warning；修復 027b1／b2 測試追溯欄的 12 個 `UNKNOWN_REQ_F`。SQL Server 2022 仍 Stopped，真實 smoke 與 schema 未驗。下一輪先查 SQL，否則做 027b3。

- 2026-10-25T19:15:07+08:00 整理上一輪 WU-MD-027b1：客戶／供應商列表共用 `erp-list-page` 與 `table-query`，保留路由與 testId；已提交搜尋條件和輸入值分離，載入錯誤可重試並顯示 traceId。上一輪前端 702/702、模擬 E2E 全量 352 通過／5 略過，兩路由專項 8/8；20 張亮暗與三語截圖已審查。SQL 服務仍 Stopped，真實 smoke 未重驗。

- 2026-10-25T14:56:59+08:00 WU-MD-027a 完成分類共用列表切片：客戶分類、供應商分類、物料品牌三路由改用 `erp-list-page` 和 `nextTableState`／`sortOrderOf`，保留原標題定位點。搜尋有三語標籤、送出後才套用關鍵字；空列表與搜尋無結果分開，載入錯誤可重試且列表／新增 500 顯示追蹤碼，刪除錯誤不顯示誤導的列表重試。先紅後綠單元測試，前端 700/700；模擬 E2E 全量 340 通過／5 略過，最新三路由與 QA 專項 14/14。8.30／8.33／8.39 共 24 張亮暗及三語截圖已目視審查；DocGen 15/15、隔離一致性 0 Error／574 Warning。SQL 仍 Stopped，真實 smoke 與 schema 未驗。父項 WU-MD-027 留待 027b 遷移其餘列表。

- 2026-10-25T14:23:55+08:00 WU-P0-033b 完成 SRS 4.5 截圖與嵌圖：按 4.4 固定次序由 134 頁 HTML 產出 134 張英文亮色 PNG，缺圖即失敗，移除介面後清理舊 PNG；SRS 內嵌原型圖並加圖說，SDS 4.2 繼續使用同一介面清單。DocGen 15/15、Debug 建置 0 Warning／Error；隔離 SRS 有 135 個圖片項目（含既有圖），一致性 0 Error／574 Warning。代表頁已目視檢查，原型是規格線框，非實際系統截圖。SQL Server 仍 Stopped，P0-015／016 真實 smoke 及 schema 未驗；下輪先查 SQL 並依 blocked.md 重試。

- 2026-10-25T08:27:12+08:00 WU-P0-033a 完成 SRS 4.5 HTML 原型與次序契約：同一份 4.4 介面來源按固定編號產 134 頁及索引，項目按 REQ_IO_A 排序，來源文字安全轉義，缺項顯示說明。先紅後綠 DocGen 13/13、Debug 建置 0 Warning／Error；隔離產出 0 Error／574 Warning，亮暗代表頁已目視檢查。SRS 4.5 截圖佔位仍待 033b，SQL Server 仍 Stopped，真實 smoke／schema 未驗。下輪先查 SQL；若仍停止，直接做 033b 截圖及嵌圖。

- 2026-10-25T08:12:12+08:00 WU-P0-032 完成 SDS 4.1 分模組 ER 圖：來源限定 data-structure.md 的 Mermaid `erDiagram`，依 SRS 模組與檔內順序連號，無 SQL 連線亦可嵌圖，欄位表仍標示 schema 未讀。測試先紅後綠，補驗非 ER 圖與無 ER 來源；DocGen 12/12、Shared 381/381、Architecture 160/160、Debug 建置 0 Warning／Error。隔離產出 0 Error／574 Warning；SDS 81 張 ER 圖、共 82 張 PNG。SQL Server 2022 仍 Stopped，P0-015／016 真實驗證及 schema 一致性待服務恢復。下輪先查 SQL；如仍停止，拆 WU-P0-033 的 SRS 4.5 原型首個切片。

- 2026-10-25T03:55:32+08:00 WU-P0-031 完成敘述 Mermaid 圖的文件內嵌：DocGen 呼叫固定版 Mermaid CLI 與 repo Chromium，對 PNG 尺寸與頁面比例作檢查；來源增 SRS 4.6.2 里程碑依賴圖及 SDS 3.1 架構圖，保留 Figure 圖說。測試先紅後綠，DocGen 10/10、Debug 解決方案建置 0 Warning／Error；隔離產出 0 Error／574 Warning，兩份 DOCX 各含一張 PNG。SQL Server 2022 仍 Stopped，未驗 schema 或真實 smoke。下輪先查 SQL，若恢復依 blocked.md 重試 P0-016／015；否則做 P0-032 SDS 4.1 ER 圖。

- 2026-10-25T03:46:35+08:00 WU-P0-030 完成公開可提交的 SRS NDA 英文草稿，保留 `<Client>`、`<Contractor>`、`<Date>` 及雙方資料／簽署表，不含客戶資料或商業金額。原創條款保留最終法律審閱與簽署空間；DocGen 8/8、隔離產出 0 Error／574 Warning，SRS `MISSING_SOURCE` 歸零，產出 DOCX 已核對指定佔位及表格文字。SQL Server 2022 仍 Stopped，無 schema 或真實 smoke 驗收。下輪先查 SQL；若恢復，依 blocked.md 重試 P0-016／015 並做 schema 檢查；若仍停止，做 P0-031 Mermaid 圖片產出。

- 2026-10-25T03:42:39+08:00 WU-P0-029 完成 SRS 2.2.8–2.2.14 七個後續模組英文概述，核對 CO／QM／PM／CRM／HR／PS／SCM 需求基線與進度；均明確標為設計，未宣稱已交付產品。審查後無需修改 DocGen 代碼或另加鏡像測試。DocGen 測試 8/8、隔離產出 0 Error／575 Warning；報告唯一缺失的 SRS 敘述來源為 NDA，另有未連線的 schema 警告。SQL Server 2022 仍 Stopped，真實 smoke 與 schema 未驗；下輪先查 SQL，若恢復按 blocked.md 重試 P0-016／015，否則做 P0-030 NDA 來源。

- 2026-10-25T03:39:48+08:00 WU-P0-028 完成 SRS 2.2.2–2.2.7 六個模組英文概述。對照 SD／PP／PUR／INV／FI／BI 需求與產品狀態，區分設計目標、部分交付與已實作功能；審查後無需改 DocGen 代碼。DocGen 測試 8/8、隔離產出 0 Error／575 Warning，SRS 缺來源 14→8。SQL Server 2022 仍 Stopped，真實 smoke 與 schema 未驗。下輪先查 SQL，若恢復按 blocked.md 重試 P0-016／015，否則做 P0-029 的 SRS 2.2 後續七模組概述。

- 2026-10-25T03:36:15+08:00 WU-P0-027 完成 SDS 3.1–3.3 系統、安全及軟件架構英文來源。核對 Program.cs 實際註冊模組與中介層、architecture.md、security.md、ADR-003／018；區分現有框架、規劃模組及待驗部署。審查後將 SQL 停止的即時狀態移出長期來源，避免往後文件過期。DocGen 測試 8/8、隔離產出 0 Error／575 Warning，SDS 缺來源 3→0；SQL schema 與真實 smoke 未驗。下輪先查 SQL，若恢復按 blocked.md 重試 P0-016／015；否則做 P0-028 SRS 2.2.2–2.2.7 模組功能概述。

- 2026-10-25T03:33:01+08:00 WU-P0-026 完成 SDS 2.1–2.3 英文來源，核對模組邊界、共用交易、開發與測試流程、安全及資料庫策略與現有架構、ADR-003／004。檢查後未發現需改生成器的問題；來源區分已實作機制、規劃設計與待驗證據。DocGen 測試 8/8、隔離產出 0 Error／576 Warning，SDS 缺來源 6→3。SQL Server 2022 仍 Stopped，真實 smoke 與 schema 未驗；下輪先查 SQL，若恢復按 blocked.md 重試 P0-016／015，否則做 P0-027 SDS 第 3 章來源。

- 2026-10-25T03:29:57+08:00 WU-P0-025 完成 SDS 1.1–1.6 六節英文來源，依 BlueWhale SDS 章節、MyERP SRS 術語及架構現況核對用途、範圍、縮寫、讀者、參考與文件導覽。將規劃設計與實際交付證據分開；DocGen 測試 8/8、隔離產出 0 Error／576 Warning，SDS 缺來源 12→6。SQL 服務仍 Stopped，schema 與真實 smoke 未驗；下輪先查服務，若恢復按 blocked.md 重試 P0-016／015，否則做 P0-026 SDS 第 2 章。

- 2026-10-25T03:26:56+08:00 WU-P0-024 完成 SRS 4.6.1–4.6.5 五節英文來源，按 BlueWhale 章節與 MyERP 章程 M0–M6 里程碑核對。Scrum、時程、人力、預算及產品設計均標明規劃性質；日期、合約金額及維護條款保留佔位，示例預算比例合計 100%。檢查來源與里程碑對照後，DocGen 測試 8/8、隔離產出 0 Error／576 Warning，SRS 缺來源 19→14；SQL 服務仍 Stopped，未驗 schema。下輪先查 SQL；恢復後按 blocked.md 重試 P0-016／015，否則做 P0-025 SDS 第 1 章來源。

- 2026-10-25T03:23:59+08:00 WU-P0-023 完成 SRS 4.1／4.3／4.4 英文引言，交代 REQ_F411、REQ_DS43、REQ_IO_A 的固定編號與模組來源、需求工作簿追溯及 4.4／4.5／SDS 4.2 介面次序。敘述區分計畫需求與已驗收產品，並明言無 SQL 連線時 4.3 不證實實際 schema。DocGen 測試 8/8、隔離產出 0 Error／576 Warning，SRS 缺來源 22→19；SQL 仍 Stopped，未跑真實 smoke 或 schema 檢查。下輪先查 SQL；恢復後按 blocked.md 重試 P0-016／015，否則做 P0-024 的 SRS 4.6 規劃來源。

- 2026-10-25T03:21:21+08:00 WU-P0-022 完成 SRS 3.1 八題擬議持份者訪談與 3.2.1–3.2.3 經濟、營運、技術可行性英文來源。訪談人名、職位、組織、地點、日期及成本均保留佔位，擬議答案明示待確認；可行性區分已記錄工程證據與仍待評估的投資、試點、產能及生產部署。DocGen 測試 8/8、隔離產出 0 Error／576 Warning，SRS 缺來源 26→22；SQL 仍 Stopped，schema 未驗。下輪先查 SQL，恢復後按 blocked.md 重試 P0-016／015，否則做 P0-023 的 SRS 4.1／4.3／4.4 引言。

- 2026-10-25T03:18:34+08:00 WU-P0-021 完成 SRS 2.1、2.3、2.4.1–2.4.3、2.5、2.6.1–2.6.2 八節英文來源，核對 Windows／.NET 10／Angular 22／Node 24.21.0／SQL Server 2022／Redis 的現有開發路徑；硬件數值明列為待驗規劃假設，用戶類別與公司授權一致。DocGen 測試 8/8、隔離產出 0 Error／576 Warning，SRS 缺來源 34→26。SQL 仍 Stopped，schema 與真實產品未驗；下輪先查服務，恢復後按 blocked.md 重試 P0-016／015，否則做 P0-022 的 SRS 第 3 章來源。

- 2026-10-25T03:15:35+08:00 WU-P0-020 完成 SRS 1.1–1.7 七節英文敘述來源，按 BlueWhale 章節及 MyERP 章程核對用途、範圍、術語、讀者、參考、角色與文件導覽；明確區分需求目標與已交付現況，未引入客戶資料。DocGen 測試 8/8、隔離產出 0 Error／576 Warning，SRS 缺來源 41→34。SQL 服務仍 Stopped，schema 與真實產品未驗。下輪 SQL 恢復時按 blocked.md 重試 P0-016／015；否則做 P0-021 的 SRS 2 章共通來源。

- 2026-10-25T03:12:24+08:00 WU-P0-019 完成 SRS 4.2 Performance／Security／Quality／Availability／Testability 五節英文敘述來源。先確認五份檔案全缺，再逐節核對 architecture.md 的 33 個 REQ_NF，引用完整且無錯段、重複或額外 ID；敘述將目標與實測區分，沒有宣稱未做的負載、備份還原或正式環境可用性驗收。DocGen 測試 8/8，`--output .tools/docgen-isolated` 產出 0 Error／576 Warning，SRS 缺敘述來源從 46 降至 41；SQL 服務仍 Stopped，schema 未驗。下一輪如 SQL 恢復按 blocked.md 重試 P0-016／015；否則做 P0-020 SRS 第 1 章。

- 2026-10-25T03:09:02+08:00 WU-BI-005d 完成 BI SRS 子功能索引：7.1–7.7 對應採購、銷售、生產、庫存、財務報表、管理層 KPI、比較／鑽取／排程。DocGen 測試 8/8、隔離產出 0 Error／576 Warning（較上輪少 32），BI `MISSING_SUBFUNCTION` 為 0；`git diff --check` 通過。只更新需求文件索引，無 BI 產品、SQL schema 或真實資料驗收。SQL 服務仍 Stopped；恢復後先重試 P0-016／015，否則下輪做 P0-019 的 SRS 4.2 敘述來源。

- 2026-10-25T03:05:56+08:00 WU-P0-018 完成 DocGen 獨立輸出目錄：`--output` 讓正式 SRS 被 WINWORD 佔用時仍可從同一 repo 來源生成四檔並跑一致性檢查，預設輸出不變。測試先紅後綠 8/8，實際隔離 CLI 兩種目錄產出成功；檢查揭露 11 個範圍式需求 ID 追溯錯誤，已改明列 ID，重跑 0 Error／608 Warning。SQL 實例仍 Stopped、目前帳號無權啟動，故 schema 與真實產品未驗；下輪服務恢復後依 blocked.md 先重試 WU-P0-016／015。

- 2026-10-25T03:00:53+08:00 WU-PP-001d 完成 Released 生產計劃需求來源純設計：REQ_F4113.2.7、BR-PP-015～021、擬定 `IReleasedDemandSnapshotReader` 與 PP-DEMAND-001～017。區分 Approved 與 Released，固定修正版本、完整空桶、已提交 cursor、90 天重放與逐頁權限重驗；SCM 只作可選比較層。SQL 服務仍 Stopped，無 PP backend、migration、端點、頁面或產品驗收；DocGen 單元 7/7、`git diff --check` 通過。直接產出仍因 MyERP_SRS.docx 被佔用而未完成一致性檢查。SQL 恢復後先依 blocked.md 重試 P0-016／015；否則先排除 DocGen 檔案佔用，再按 backlog 選下一個設計單元。

- 2026-10-24T22:54:12+08:00 WU-INV-020d 完成 Posted 異動及沖回固定來源設計：REQ_F4115.28.1～3、BR-INV-063～068、內部 `IPostedMovementSnapshotReader` 擬定契約及 INV-MOV-SNAP-001～014。要求 SQL 一致性視圖固化全量已提交行、持久化後才發 cursor、明確原異動沖回關聯、90 天重放與每頁授權重驗；無 migration、實際 reader、HTTP 或頁面。SQL 服務仍 Stopped，真實 SQL 待 WU-INV-020a；DocGen 單元 7/7、`git diff --check` 通過。直接產出仍因 MyERP_SRS.docx 被佔用而未完成一致性檢查。SQL 恢復後先依 blocked.md 重試 P0-016／015；否則做 PP-001d 設計。

- 2026-10-24T22:48:02+08:00 WU-MD-028d 完成基準 UOM／換算路徑凍結證據 reader 的需求與設計：REQ_F4118.3.8、BR-MD-034～040、Application 收據契約與 MD-CONV-EV-001～022；規定逐段版本、精確有理數、六位小數、至少 90 天重放及每次權限重驗。無 migration、端點、頁面或產品驗收；SQL 仍 Stopped。DocGen 單元 7/7、`git diff --check` 通過；直接產出因 MyERP_SRS.docx 被佔用，未完成一致性檢查。SQL 恢復後先依 blocked.md 重試 P0-016／015，再做 FI-004a；否則依 backlog 選單一設計單元。

- 2026-10-24T22:43:43+08:00 WU-SD-009d 完成 SD 已核准訂單需求唯讀來源設計：REQ_F4112.2.1 v1.1、BR-SD-051～053、SD-DEMAND-001～015；固定 committed revision cursor、90 天重放、完整空桶、逐頁權限重驗與修正另開 cursor。無 migration、端點、頁面或真實 SQL 驗收；SQL 服務仍 Stopped。DocGen 單元 7/7 及 `git diff --check` 通過；直接產出因 MyERP_SRS.docx 被佔用未完成一致性檢查。SQL 恢復後先按 blocked.md 重試 P0-016／015，再做 FI-004a；否則繼續 backlog 的單一設計單元。

- 2026-10-24T22:40:54+08:00 WU-SCM-001e 完成 owner 前置契約純設計：分拆 SD-009a 已核准需求、MD-028a 凍結換算證據、INV-020a 已過帳異動及 PP-001c 已放行需求；定義最少 90 天 cursor replay、逐頁權限重驗、修正另開 cursor 與 SCM 已發布 manifest 自存，補 BR-SCM-024～031、SCM-OWN-001～016。無 migration、端點、頁面或產品驗收。SQL 服務仍 Stopped；DocGen 單元 7/7、`git diff --check` 通過；直接產出因 MyERP_SRS.docx 被佔用未完成一致性檢查。SQL 恢復後依 blocked.md 優先重試 P0-016／015，否則按 backlog 做下一設計單元。

- 2026-10-24T22:37:18+08:00 WU-SCM-001d 完成預測來源純設計：核對 SD 無公開需求 reader、PP 無後端、INV 只有即時結存／暫估估值、MD 換算缺版本證據；ADR-072 固定 `SCM-SD-MA3-v1`、owner committed cursor、凍結來源集合與不可用理由，補 BR-SCM-016～023、SCM-SRC-001～013，拆 001e owner 前置契約。無 SCM migration、端點、頁面或產品驗收。SQL 服務仍 Stopped；DocGen 單元 7/7 與 `git diff --check` 通過；直接產出因 MyERP_SRS.docx 被佔用未完成一致性檢查。SQL 恢復後依 blocked.md 優先重試 P0-016／015，否則做 WU-SCM-001e。

- 2026-10-24T22:33:04+08:00 WU-SCM-000d 完成 SCM 純設計基線：REQ_F41116.1.1～3.3、BR-SCM-001～015、SCM-BASE-001～018；固定預測來源、運輸交付及供應商提案與 SD／PP／INV／PUR／MD／SYS 的 owner 邊界，拆 001d、001a／b 至 003a／b。無 migration、端點、頁面或 SCM 產品驗收。SQL 服務仍 Stopped；DocGen 單元 7/7、`git diff --check` 通過。直接產出因 MyERP_SRS.docx 被其他程序佔用，未宣稱一致性檢查完成。SQL 恢復後依 blocked.md 優先重試 P0-016／015；否則下一輪做 SCM-001d 來源契約設計，並重試 DocGen 一致性檢查。

- 2026-10-24T22:30:02+08:00 WU-PS-000d 完成 PS 純設計基線：REQ_F41115.1.1～6.2、BR-PS-001～015、PS-BASE-001～018；固定 PS 預算／工時／資源／里程碑／計費與 HR、SD、FI、SYS 的 owner 邊界，拆 001a／b 至 006a／b。無 migration、端點、頁面或 PS 產品驗收。SQL 服務仍 Stopped；DocGen 單元 7/7、`git diff --check` 通過。直接產出因 MyERP_SRS.docx 被其他程序佔用，未宣稱一致性檢查完成。SQL 恢復後依 blocked.md 優先重試 P0-016／015，否則先建並做 SCM 設計基線。

- 2026-10-24T22:26:21+08:00 WU-HR-000d 完成 HR 純設計基線：REQ_F41114.1.1～8.2、BR-HR-001～018、HR-BASE-001～021；固定 HR 員工／組織／考勤／薪資／假期／招聘／績效／培訓與 SYS 帳號／審批、FI 過帳的 owner 邊界，拆 001a／b 至 007a／b。無 migration、端點、頁面或 HR 產品驗收。SQL 服務仍 Stopped；DocGen 7/7、`git diff --check` 通過。恢復後按 blocked.md 優先重試 P0-016／015，否則先建並做 PS 設計基線。

- 2026-10-24T22:22:22+08:00 WU-CRM-000d 完成 CRM 純設計基線：REQ_F41113.1.1～5.3、BR-CRM-001～018、CRM-BASE-001～020；固定 MD 客戶／同意、SD 報價／訂單、SYS 審批與 CRM 潛客／商機／互動／活動／工單邊界，拆 001a／b 至 005a／b。無 migration、端點、頁面或 CRM 產品驗收。SQL 服務仍 Stopped；DocGen 7/7、`git diff --check` 通過。恢復後依 blocked.md 優先重試 P0-016／015，否則做 HR 設計基線。

- 2026-10-24T22:18:21+08:00 WU-PM-000d 完成 PM 純設計基線：REQ_F41112.1.1～4.3、BR-PM-001～016、PM-BASE-001～018；固定設備與 FI 資產、備件與 INV 出入庫的 owner 邊界、到期鍵及工單狀態，拆 001a／b、002a／b、003a／b、004a／b。無 migration、端點、頁面或 PM 產品驗收。SQL 服務仍 Stopped；DocGen 單元 7/7、`git diff --check` 通過。直接產出因 MyERP_SRS.docx 被其他程序佔用，未宣稱一致性檢查完成。SQL 恢復後依 blocked.md 優先重試 P0-016／015，否則做 CRM 設計基線。

- 2026-10-24T22:14:43+08:00 WU-QM-000d 完成 QM 純設計基線：REQ_F41111.1.1～4.2、BR-QM-001～014、QM-BASE-001～018；固定來料檢驗由 PUR 確認收貨／INV 確認庫存、製程檢驗由 PP 確認完工，以及不合格品處置與凍結品質報告邊界，拆 001a／b、002a／b、003a／b、004a／b。無 migration、端點、頁面或 QM 產品驗收。SQL 服務仍 Stopped；DocGen 7/7、`git diff --check` 通過。恢復後先依 blocked.md 重試 P0-016／015；否則做 PM 設計基線。

- 2026-10-24T22:11:21+08:00 WU-CO-000d 完成 CO 純設計基線：REQ_F41110.1.1～4.4、BR-CO-001～013、CO-BASE-001～017；固定 FI Posted 實際、INV 暫估與 PP 已確認來源的 owner 邊界，預算版本與實際截止凍結，拆 001a／b、002a／b、003a／b。無 migration、端點、頁面或 CO 產品驗收。SQL 服務仍 Stopped；DocGen 7/7 通過。恢復後先依 blocked.md 重試 P0-016／015，否則做 QM 設計基線。

- 2026-10-24T22:07:12+08:00 WU-PP-000d 完成 PP 純設計基線：BlueWhale REQ_F4113.1.1～4.6 與新增 5.1～9.1、BR-PP-001～014、PP-BASE-001～020；固定 BOM／工藝版本、MPS／MRP owner 截止、工單放行、領料／完工須由 INV Posted 確認，並拆 001a／b、002a、003a、004a、005a。SQL 服務仍 Stopped；無 migration、端點、頁面或 PP 產品驗收。DocGen 7/7 通過；恢復後依 blocked.md 優先重試 P0-016／015，否則按 backlog 拆 CO 設計基線。

- 2026-10-24T22:03:18+08:00 WU-BI-004d 完成比較／鑽取／排程純設計：REQ_F4117.7.1～5、BR-BI-026～034、BI-ADV-001～023；比較採兩個獨立 owner 截止，鑽取綁原聚合來源集合，排程每次執行及下載重驗權限，只交付站內完整產物。拆 004a／b／c／e。無 migration、背景工作、端點、畫面或 BI 產品驗收。SQL 服務仍 Stopped；DocGen 7/7、`git diff --check` 通過。恢復後先按 blocked.md 重試 P0-016／015；否則下一輪拆 PP 設計基線。

- 2026-10-24T21:58:49+08:00 WU-BI-003d 完成儀表板四項 KPI 純設計：REQ_F4117.6.1～4、BR-BI-020～025、BI-KPI-001～017，分別固定 FI 已過帳銷售收入／開放 AR、SD 首次核准訂單數、INV 低庫存數及各自 owner 截止、單位、權限、缺來源狀態；拆 003a／b。現有首頁仍為示範常數，沒有 migration、端點、畫面或產品驗收。SQL 服務仍 Stopped；恢復後先按 blocked.md 重試 P0-016／015，否則下一輪拆 BI 比較／鑽取／排程設計。DocGen 7/7、`git diff --check` 通過。

- 2026-10-24T17:50:10+08:00 WU-BI-002d 完成 BlueWhale 營運 16 張報表設計：REQ_F4117.1.1～4.3、BR-BI-012～019、BI-OPS-001～021，固定 PUR／SD／PP／INV owner 公開來源、已提交跨頁集合、權限與完整情境；PP 尚未實作時須明示來源不可用。拆 002a／b／p／c 及儀表板 003d。無 migration、端點、畫面或 BI 產品驗收。DocGen 7/7、`git diff --check` 通過；SQL 仍 Stopped。恢復後依 blocked.md 先重試 P0-016／015，否則做 WU-BI-003d。

- 2026-10-24T17:46:28+08:00 WU-FI-015d 完成月結／年結設計：REQ_F4116.20.1～6、BR-FI-133～144、FI-CLOSE-001～030，固定已提交來源清單、來源／GL 對帳、調整與結轉永久鍵、重開覆核及補償來源；拆 015a／b／c。只做設計，無 migration、端點、畫面或 FI 產品測試。DocGen 7/7、`git diff --check` 通過；SQL 仍 Stopped。恢復後依 blocked.md 先重試 P0-016／015，否則做 WU-BI-002d。

- 2026-10-24T17:41:07+08:00 WU-BI-001d 補 FI 財務報表來源唯讀契約：REQ_F4116.19.1～3、BR-FI-124～132、BR-BI-009～011、FI-RPT-001～015、BI-FIN-021～031，固定 AR/AP as-of 明細、其他收支 Posted 分類、已提交來源集合、跨頁權限與完整筆數／金額。純設計，無 migration、端點、畫面或產品測試。DocGen 7/7、`git diff --check` 通過；SQL 服務仍 Stopped。恢復後按 blocked.md 優先重試 P0-016／015，否則進行 WU-FI-015d 月結／年結設計。

- 2026-10-24T17:37:20+08:00 WU-BI-000d 完成 BlueWhale 財務 7 項報表設計基線：REQ_F4117.5.1～7、BR-BI-001～008、BI-FIN-001～020，固定 BI 只讀 FI 已過帳公開來源、穩定截止點、公司／來源權限與凍結匯出；EP-BI1 部分拆分為後續來源設計、真實 SQL、HTTP、畫面單元。無 migration、端點、畫面或 BI 產品驗收；SQL 仍 Stopped，恢復後優先依 blocked.md 重試 P0-016／015。

- 2026-10-24T17:34:15+08:00 WU-FI-014d 完成外幣 AR/AP 與收付款匯差設計：新增 REQ_F4116.18.1～6、BR-FI-113～123、FI-FX-001～032，固定歷史交易匯率與本位金額、已實現差額、期末未實現估值及結清釋放，拆 014a／b／c。無 migration、端點、畫面或 FI 產品驗收。DocGen 測試 7/7、`git diff --check` 通過；SQL 仍 Stopped，恢復後優先依 blocked.md 重試 P0-016／015。

- 2026-10-24T17:29:21+08:00 WU-FI-013d 完成稅務來源、期間申報與 GL 對帳設計：新增 REQ_F4116.17.1～6、BR-FI-103～112、FI-TAX-001～030，區分已過帳稅務來源、內部申報草稿、GL 差額及外部申報證據；拆 013a／b／c，另排 014d 匯差設計。無 migration、端點、畫面或 FI 產品驗收。DocGen 測試 7/7 通過；SQL 仍 Stopped，恢復後優先依 blocked.md 重試 P0-016／015。

- 2026-10-24T17:25:00+08:00 WU-FI-012d 完成固定資產取得／折舊／處置設計：新增 REQ_F4116.14～16、BR-FI-093～102、FI-FA-001～029，固定卡片與已過帳 GL 分離、直線法末期餘額、永久來源鍵與處置前折舊；拆 012a／b／c／e，另排 013d 稅務設計。無 migration、端點、畫面或 FI 產品驗收。DocGen 單元 7/7、`git diff --check` 通過；直接產出因 MyERP_SRS.docx 被其他程序佔用，未宣稱一致性檢查完成。SQL 仍 Stopped；恢復後先按 blocked.md 重試 P0-016／015。

- 2026-10-24T17:21:08+08:00 WU-FI-011d 完成外部銀行對帳設計：新增 REQ_F4116.13.1～6、BR-FI-083～092、FI-RECON-001～030，固定 UTF-8 CSV 逐行結果、外部證據與帳面分離、一對一匹配與獨立覆核；拆 011a／b／c，另排 012d 固定資產設計。無 migration、端點、畫面或 FI 產品驗收。DocGen 7/7、`git diff --check` 通過；SQL 仍 Stopped，恢復後先依 blocked.md 重試 P0-016／015。

- 2026-10-24T17:16:02+08:00 WU-FI-010d 完成銀行／現金帳來源設計：新增 REQ_F4116.12.1～3、BR-FI-075～082、FI-CASH-001～022，固定 MD 資金帳戶與 FI 已過帳資金分錄的分工、期初／異動／期末、跨頁已提交截點及外部銀行對帳邊界；拆 010a／b、011d。BlueWhale 現金銀行報表仍屬 BI；無 migration、端點、畫面或 FI 產品驗收。DocGen 7/7、`git diff --check` 通過；SQL 仍 Stopped，恢復後先重試 P0-016／015。

- 2026-10-24T17:11:28+08:00 WU-FI-009d 完成客戶／供應商對帳單 FI 來源設計：新增 REQ_F4116.11.1～3、BR-FI-067～074、FI-STMT-001～020，固定已過帳應收／應付與核銷的日期餘額、跨頁一致截止點及 BI 邊界；拆 009a／b。BlueWhale 報表需求與畫面仍屬 BI；無 migration、端點、畫面或 FI 產品驗收。DocGen 7/7、`git diff --check` 通過；SQL 仍 Stopped，恢復後先重試 P0-016／015。

- 2026-10-24T17:07:19+08:00 WU-FI-008d 完成其他付款設計：沿 BlueWhale REQ_F4116.4.1～6，新增 BR-FI-059～066 與 FI-AP-OTHER-001～020，固定已發生非發票費用、FI 科目映射、永久來源鍵及與 AP／PUR 隔離；拆 008a／b／c。無 migration、端點、畫面或 FI 產品驗收。DocGen 7/7、git diff --check 通過；SQL 仍 Stopped，恢復後優先重試 P0-016／015。


- 2026-10-24T17:03:09+08:00 WU-FI-007d 完成其他收款設計：沿 BlueWhale REQ_F4116.2.1～6，新增 BR-FI-051～058 與 FI-AR-OTHER-001～020，固定非發票已賺取收入、FI 科目映射、永久來源鍵及與 AR／SD 信用隔離；拆 007a／b／c。無 migration、端點、畫面或 FI 產品驗收。DocGen 7/7、`git diff --check` 通過；SQL 仍 Stopped，恢復後優先重試 P0-016／015。

- 2026-10-24T16:58:26+08:00 WU-FI-006d 接續未提交 FI 草稿並完成採購付款／應付核銷設計：沿既有 REQ_F4116.3.1～6／6.6.1～6，固定 BR-FI-041～050、FI-AP-PAY-001～028；付款過帳先形成未核銷款，正式核銷才減少開放 AP，供應商鎖序與永久來源鍵防重放及超額。拆 006a／b／c，無 migration、端點、畫面或 FI 產品驗收。DocGen 7/7、`git diff --check` 通過；SQL 仍 Stopped，恢復後優先重試 P0-016／015。

- 2026-10-24T12:41:08+08:00 WU-FI-005d 完成銷售收款／應收核銷設計：沿既有 REQ_F4116.1.1～6／6.5.1～6，補 BR-FI-031～040、FI-AR-COLL-001～028，固定收款過帳先形成未核銷款、正式核銷才減少開放 AR、永久來源鍵與信用／收款／應收鎖序；拆 005a／b／c，另排 006d 應付付款設計。無 migration、端點、畫面或 FI 產品驗收。DocGen 7/7、`git diff --check` 通過；SQL 仍 Stopped，恢復後優先重試 P0-016／015。

- 2026-10-24T12:36:32+08:00 WU-FI-004d 完成 GL 最小基礎設計：REQ_F4116.10.1～3、BR-FI-023～030、FI-GL-001～024 固定會計期間、已保存來源、MD 科目、平衡分錄、永久來源鍵與同交易回滾；拆 004a／b／c，另排 005d 收款設計。無 migration、端點、畫面或產品驗收。DocGen 7/7、`git diff --check` 通過；SQL 仍 Stopped，恢復後優先重試 P0-016／015。

- 2026-10-24T12:32:48+08:00 WU-FI-003d 完成採購應付來源設計：新增 REQ_F4116.9.1～3、BR-FI-015～022、FI-PUR-001～024，固定 PUR 已保存匹配發票驗證、FI 永久來源鍵與冪等、採購／進項稅／AP 平衡分錄及同交易回滾；拆出 003a／b／c。無 migration、端點、畫面或產品驗收。DocGen 單元 7/7、`git diff --check` 通過。SQL 仍 Stopped，恢復後先按 blocked.md 重試 P0-016／015；未恢復則做 WU-FI-004d GL 最小基礎設計。

- 2026-10-24T12:29:34+08:00 WU-FI-002d 完成銷售應收來源設計：新增 REQ_F4116.8.1～3、BR-FI-007～014、FI-SALES-001～024，固定已保存 SD 發票／銷退來源驗證、FI 來源冪等、平衡分錄、信用轉移、已收款信用票據邊界與同交易回滾；拆出 002a／b／c。無 migration、端點、畫面或產品測試。DocGen 單元 7/7、`git diff --check` 通過；直接產出因 MyERP_SRS.docx 被另一程序佔用失敗，未宣稱一致性檢查完成。SQL 仍 Stopped，恢復後先重試 P0-016／015；未恢復則做 WU-FI-003d。

- 2026-10-24T12:26:27+08:00 WU-FI-000d 完成信用公開契約設計：EP-FI 先拆信用、AR/AP 來源與 GL 基礎切片，新增 REQ_F4116.7.1～3、BR-FI-001～006、FI-CREDIT-001～020，固定 FI 開放 AR 暴露、公司／客戶信用鍵交易鎖和 SD 承諾分工；ADR-071 修正財務 Posted 同交易規則。無 migration、端點、畫面或產品測試；DocGen 7/7、`git diff --check` 通過。SQL 服務仍 Stopped；恢復後先重試 P0-016／015 真實 smoke，未恢復則按 WU-FI-002d 繼續設計。

- 2026-10-24T12:23:08+08:00 WU-PUR-008d 完成三單比對設計：將 008 拆成 008d／008a／008b／008c，新增 REQ_F4114.8.1～6、BR-PUR-036～043、PUR-MATCH-001～028，固定收貨淨可開票量、價稅差異決定、FI 應付同交易冪等契約及已開票退貨限制。沒有表、端點、畫面或產品測試；DocGen 7/7、`git diff --check` 通過。SQL 服務仍 Stopped；恢復後先依 blocked.md 重試 P0-016／015 及 PUR／SD 阻塞項，未恢復則拆 EP-FI 基礎公開契約設計單元。

- 2026-10-24T12:18:46+08:00 WU-PUR-007d 完成收貨／退貨設計：將 007 拆成 007d／007a／007b／007c／007e，固定 BlueWhale REQ_F4114.2.1～6 及新增 4.7.1～3、BR-PUR-028～035、PUR-RECV-001～028。採購入庫沿 INV `PostPurchaseInboundAsync`，退貨須新增專用 INV 出庫契約並沿原入庫成本；兩者按來源行預留、SYS 最終決定與 INV 同交易。沒有表、端點、畫面或產品測試；DocGen 7/7、`git diff --check` 通過。SQL 服務仍 Stopped，下輪先查服務，恢復後先依 blocked.md 重試 P0-016／015 與 PUR／SD 阻塞項；未恢復則拆 008 為獨立三單比對／FI 契約設計單元。

- 2026-10-24T12:14:32+08:00 WU-P0-016 第三輪標記 BLOCKED：`MSSQL$MSSQLSERVER2022` 仍 Stopped，現有帳號執行 `Start-Service` 回無法開啟服務；`tests/smoke npm test` 在 SQL 前置檢查明確失敗，真實預設埠驗證碼／登入／登出及 migration／種子連線驗收未跑，完成數不變。已將錯誤、前兩輪離線檢查與恢復後指令寫入 blocked.md；下一輪按 P1 待辦拆 WU-PUR-007 的獨立收貨／採購入庫設計單元，SQL 恢復時優先重試阻塞項。

- 2026-10-24T12:12:25+08:00 WU-P0-016 第二輪可驗證切片：`MSSQL$MSSQLSERVER2022` 仍為 Stopped，啟動被目前帳號拒絕；冒煙前置檢查補上 55 筆 migration 與資料庫歷史比較。PowerShell 解析 0 錯誤、UTF-8 BOM 保留、TypeScript 型別檢查及 Playwright 測試發現 1/1 通過，SQL 停止時前置檢查如預期拒絕。真實預設埠登入／登出未跑，本單元不計完成，累計未完成 2/3；下輪先查 SQL，恢復後跑 `tests/smoke npm test` 並核對 migration／種子，否則按第三輪規則處理。

- 2026-10-24T12:09:50+08:00 WU-P0-016 可編譯切片：新增獨立 tests/smoke，以 README 預設埠和命令啟動 API／Angular、前置檢查 SQL／種子／Redis／憑證／埠、真實驗證碼與登入／登出測試。npm ci、TypeScript 型別檢查與 Playwright 測試發現 1/1 通過；npm test 因 SQL 服務 Stopped 在前置檢查明確失敗，真實瀏覽器驗收未跑，單元不計完成，累計未完成 1/3。設計與情境見 smoke-test-plan.md。下輪 SQL 恢復後執行真實 smoke，然後再處理 SD BLOCKED。



- 2026-10-24T12:06:00+08:00 WU-SD-008d 完成銷退設計：將 008 拆 008d／008a／008b，REQ_F4112.7.1 升 1.1，固定 Posted 出貨行餘量鎖、已開票／未開票互斥分配、原出庫成本來源的 INV 新入庫契約、FI 信用票據與 SD 承諾反向影響同交易，補 BR-SD-043～049、SD-RETURN-001～026、資料／API／UI 契約及設計審查。沒有銷退表、端點、畫面或產品測試；DocGen 7/7、`git diff --check` 通過。SQL 服務仍 Stopped，001／002a／003a 真實驗收保持 BLOCKED；下輪先查 SQL，恢復後依 blocked.md 重試，否則按 P1 待辦推進 WU-P0-016 的獨立冒煙測試骨架並明確記錄真實驗收受阻。

- 2026-10-24T12:02:12+08:00 WU-SD-007d 完成發票設計：將 007 拆 007d／007a／007b，REQ_F4112.5.1 升 1.1，固定 Posted 出貨行餘量鎖、分次開票累計取整、請求鍵與 FI 來源冪等、FI AR 與 SD 信用承諾同交易轉移，補 BR-SD-037～042、SD-INVOICE-001～024、資料／API／UI 契約及設計審查。沒有發票表、端點、畫面或產品測試；DocGen 7/7、`git diff --check` 通過。SQL 服務仍 Stopped，001／002a／003a 真實驗收保持 BLOCKED。下輪先查 SQL；恢復後依 blocked.md 重試，否則將 008 拆成獨立銷退設計單元。

- 2026-10-24T11:58:05+08:00 WU-SD-006d 完成信用設計：將 006 拆 006d／006a／006b／006c，REQ_F4112.6.1 升 1.1，固定 FI/AR 公開額度與已過帳未清應收、SD 已核准未開票承諾、同交易信用鎖／重驗、逐輪受權覆核及 24 個完整情境；審查修正倒填日期與 Pending 重複計入風險。沒有 FI/AR 服務、表、端點、畫面或產品測試；DocGen 7/7、`git diff --check` 通過。SQL 服務仍 Stopped，001／002a／003a 真實驗收保持 BLOCKED。下輪先查 SQL；恢復後依 blocked.md 重試，否則拆 007 為獨立發票／AR 設計單元。

- 2026-10-24T11:53:43+08:00 WU-SD-005d 完成定價設計：將 005 拆 005d／005a／005b，REQ_F4112.4.1 升 1.1，固定同公司日期有效客戶專屬／預設優先級、區間重疊併發防線、人工價格／超額折扣獨立權限及理由、存檔重新定價與歷史取整快照；新增 BR-SD-026～030、SD-PRICE-001～020、資料／API／UI 規格與設計審查。沒有產品程式、migration、端點或畫面；DocGen 7/7、`git diff --check` 通過。SQL 服務仍 Stopped，`Start-Service` 無法開啟；001／002a／003a 真實驗收保持 BLOCKED。下輪先查 SQL，恢復後按 blocked.md 重試；未恢復則拆 WU-SD-006d 為獨立信用公開契約設計。

- 2026-10-24T07:43:18+08:00 WU-SD-004d 完成出貨設計：依 INV-015a 實際契約固定 Approved 訂單餘量、Pending 預留、順序鎖、駁回釋回、批序號快照、SYS 零步驟與最終決定同交易 INV 出庫，補 BR-SD-021～025、SD-DEL-001～020 及需求／資料／API／UI 規格。沒有出貨表、端點、畫面或產品測試；DocGen 7/7、`git diff --check` 通過。SQL 服務仍 Stopped，001／002a／003a 真實驗收保持 BLOCKED；下輪先查 SQL，恢復後按 blocked.md 重試，再做 004a；未恢復則拆 WU-SD-005 的獨立定價設計單元。

- 2026-10-24T07:39:34+08:00 WU-SD-003a 第三輪標記 BLOCKED：SQL 服務仍 Stopped，`Start-Service` 無法開啟；本輪先編譯紅燈後完成訂單 Draft／Pending／Rejected／Approved／Cancelled 純狀態轉移及有出貨不可取消守衛，專項 17/17。Debug 全方案 0 警告／錯誤、架構 160/160、Shared 381/381、DocGen 7/7；migration、交易 CRUD、來源唯一約束、真實 SQL／受權 HTTP 未完成，不計完成數。SQL 恢復後先按 blocked.md 重試 001／002a／003a；未恢復則進行可獨立設計的 WU-SD-004d，不宣稱有出貨實作。

- 2026-10-24T07:36:22+08:00 WU-SD-003a 第二段可編譯切片：先以缺少來源驗證器的編譯紅燈，後加入 Approved 報價、到期日含當日、已轉單、跨公司及無效公司檢查，重用既有報價狀態規則；專項 9/9，架構 143/143、Shared 381/381、DocGen 7/7，Debug 全方案 0 警告／錯誤。SQL 仍 Stopped；未有 migration、報價鎖／來源唯一索引、保存快照、交易 CRUD 或受權 HTTP，003a 完成數不變，累計未完成 2／3。下一輪先查 SQL；恢復後先重試 BLOCKED 001／002a，再續 003a 真實 SQL 紅燈與交易實作；未恢復時按第三輪規則處理 003a。

- 2026-10-24T07:33:21+08:00 WU-SD-003a 可編譯純邏輯切片：先以缺少訂單型別的編譯紅燈，後加入人工草稿逐欄驗證與共用報價捨入政策的訂單金額快照／專屬溢位錯誤；專項 14/14，架構 134/134、Shared 381/381、DocGen 7/7，Debug 全方案 0 警告／錯誤。SQL 服務仍 Stopped 且 `Start-Service` 無法開啟，沒有 migration、保存草稿、來源轉單、受權 HTTP 或真實 SQL 驗收，003a 完成數不變，累計未完成 1／3。下一輪先查 SQL；恢復後先重試 BLOCKED 001／002a，再續 003a 交易驗收；未恢復時只做可獨立驗證的 003a 切片。

- 2026-10-24T07:29:06+08:00 WU-SD-003d 完成設計單元：先確認 SQL 仍 Stopped 且 `Start-Service` 無權，將第三輪 002a 標記 BLOCKED；固定訂單 Draft／Pending／Rejected／Approved／Cancelled、整單轉報價來源唯一性、信用契約缺席時拒絕、SYS 同交易與取消／出貨餘量邊界。新增 BR-SD-016～020、SD-ORDER-001～020 及資料/API/UI 設計；無產品程式、migration 或端點。DocGen 7/7、`git diff --check` 通過；下一輪先查 SQL，恢復後重試 001／002a 再做 003a，未恢復時只能做獨立可驗證切片。

- 2026-10-24T07:26:10+08:00 WU-SD-002a 第二段可編譯切片：先紅燈後加入報價 Draft／Pending／Rejected／Approved／Voided 純狀態轉移、閏日後到期的 `IClock.Today` 含當日規則、已轉單的 VOID／再轉拒絕與衍生到期旗標；專項 21/21。Debug 全方案 0 警告／錯誤、架構 120/120、Shared 381/381、DocGen 7/7。SQL 服務仍 Stopped，沒有 migration、草稿交易 API、SYS 審批或真實 SQL／HTTP 驗收；002a 完成數不變，累計未完成 2／3。下一輪先查 SQL；恢復後按 blocked.md 重試 001，再續 002a 的真實 SQL 紅燈與交易實作；未恢復則按第三輪規則處理 002a。

- 2026-10-24T07:22:45+08:00 WU-SD-002a 可編譯切片：先寫報價輸入與金額測試，再加入 1～1000 行、閏日／到期、六位數量／單價、四位折扣、欄位錯誤及逐行原幣取整後轉本位幣的純邏輯；行／表頭 DECIMAL(19,4) 溢位有明確錯誤。首次測試把 TWD 誤當小數幣別，修正 fixture 後架構 99/99；Debug 全方案 0 警告／錯誤、Shared 381/381、DocGen 7/7。`MSSQL$MSSQLSERVER2022` 仍 Stopped；migration、草稿 API、SYS 審批、真實 SQL／受權 HTTP 及整合全量未驗，002a 不計完成數。下一輪先查 SQL，恢復後先重試 BLOCKED 001 再續 002a；未恢復則繼續可獨立驗證的 002a 狀態轉移／期限純邏輯，累計未完成 1／3。

- 2026-10-24T07:18:32+08:00 WU-SD-002d 完成報價規格切片：固定 Draft／Pending／Rejected／Approved／Voided 轉移、到期與單次全行轉單、金額快照和逐行取整、SYS 零步驟與最終回調，以及 20 個 feature 級情境。REQ_DS43 於 002a 建表驗證時分配，REQ_IO 於 002b 實際畫面時分配；沒有新表、端點或畫面。DocGen 7/7、`git diff --check` 通過；SQL 服務仍 Stopped，001 真實驗收保持 BLOCKED。下一輪先查 SQL，恢復後重試 001，再做 002a；未恢復時只能推進 002a 可獨立驗證的純邏輯與失敗測試。

- 2026-10-24T07:15:06+08:00 WU-SD-001 第三輪標記 BLOCKED：先紅燈後完成 MD 公開銷售選項契約與查詢，已核准客戶／啟用物料／幣別及指定業務日銷項稅率，新增真實 SQL 測試原始碼。Debug 全方案 0 警告／錯誤，架構 83/83、Shared 381/381、DocGen 7/7。`MSSQL$MSSQLSERVER2022` 仍 Stopped，`Start-Service` 無法開啟服務；真實 SQL、受權 HTTP、整合全量及帶 DB DocGen 未驗，完成數不變。下輪改做獨立的 WU-SD-002d 報價詳細設計；SQL 恢復後按 blocked.md 重試 001。

- 2026-10-24T07:11:25+08:00 WU-SD-001 第二段可編譯切片：依 SD-FOUND-005／008 先寫缺少動作範圍服務的紅燈測試，再加入 `SalesActionAccess`，按每個 SAL 動作經 SYS `ICurrentAuthorization` 取得資料範圍，拒絕匿名、缺權或公司不一致；沒有新路由或表。Debug 全方案建置 0 警告／錯誤，Sales 專項 8/8、架構全量 83/83、Shared 381/381、DocGen 7/7。SQL 服務仍 Stopped；MD 選項、真實 SQL／受權 HTTP 與整合全量待續，001 不計完成數。本單元累計未完成 2／3。

- 2026-10-24T07:07:36+08:00 WU-SD-001 第一段可編譯切片：先用 SalesModuleTests 證明缺少模組的紅燈，再新增 SD／Contracts 專案、API 組合根註冊、SAL 動作權限及公司 fail-closed 邊界，沒有新增表或 HTTP 路由。架構目錄要求 SAL 權限前綴，已修正初版 API 設計並以測試鎖定；Debug 全方案建置 0 警告／錯誤，架構 80/80、Shared 381/381、DocGen 7/7。SQL 服務仍 Stopped；MD 客戶／物料／幣別／銷項稅選項、SYS 動作資料範圍、真實 SQL／受權 HTTP 及整合全量尚未完成，001 不計完成數。下一輪先恢復 SQL，續做公開選項契約及範圍，再跑真實驗收。

- 2026-10-24T07:01:29+08:00 WU-SD-000 完成：固定銷售 2.1～2.7 的 REQ_F4112 需求與 BlueWhale 2.2／2.3 映射、BR-SD-001～010、SD-BASE-001～020 情境，建立資料／API／介面設計，將 EP-SD 拆為有依賴和驗收邊界的 17 個後續單元。SD 擁有來源單據，經 MD/SYS/INV/FI 公開契約互通；出貨最終核准與 INV 同交易，FI 信用契約缺席時明確阻擋而不虛構暴露額。未改產品程式／資料庫；`git diff --check`、DocGen 單元 7/7 通過。下一輪先查 SQL 服務，恢復後做 WU-SD-001 真實 SQL／HTTP 骨架與授權契約；PUR BLOCKED 按 blocked.md 待重試。

- 2026-10-24T06:57:23+08:00 WU-PUR-006a 第三輪標記 BLOCKED：`MSSQL$MSSQLSERVER2022` 為 Stopped，`Start-Service` 回無法開啟服務；相依 005e4 草稿表未落地，故未能驗採購單 migration、真實 SQL／HTTP 與整合全量。前兩輪已提交輸入驗證 17/17 與金額計算 4/4 的可編譯切片，本輪未重跑產品測試，完成數不變。按第 1 節列入 blocked.md；下一輪執行可獨立設計的 WU-SD-000 拆分銷售 Epic，SQL 恢復後重試 006a 與相依項。

- 2026-10-24T06:55:05+08:00 WU-PUR-006a 第二輪可編譯切片：人工採購單金額先寫紅燈測試，再加入行級原幣／本位幣取整、匯率與進項稅及行／表頭 `DECIMAL(19,4)` 溢位拒絕，專項 4/4。Debug 全方案建置 0 警告／錯誤，ArchitectureTests 75/75、Shared 381/381、DocGen 7/7。`MSSQL$MSSQLSERVER2022` 仍停止，`Start-Service` 無法開啟服務；005e4 三表、006a migration／人工 API、真實 SQL／HTTP、整合全量及帶 DB DocGen 未驗，完成數不變。下一輪先確認 SQL 並完成 005e4 相依，再續 006a；本單元累計未完成 2／3。

- 2026-10-24T06:50:28+08:00 WU-PUR-006a 可編譯切片：先寫人工採購單輸入紅燈測試，再加入逐欄驗證，閏日、六位精度、1／1000 行、重複行及錯誤欄名專項 17/17。Debug 解決方案 0 警告／錯誤，ArchitectureTests 71/71、Shared 381/381、DocGen 7/7。`MSSQL$MSSQLSERVER2022` 仍停止，`Start-Service` 回無法開啟服務；005e4 三表未落地，006a migration、人工 CRUD、受權 HTTP、SQL 整合全量及帶 DB DocGen 未驗，本單元不計完成數。下一輪先恢復 SQL 並完成 005e4 相依，再續 006a。

- 2026-10-24T02:35:05+08:00 WU-PUR-006d 完成：固定採購單 4.1.1～6 需求、人工／決標草稿共用生命週期、提交鎖請購行與整單餘量、SYS 審批及駁回／合格取消釋回一次、INV 入庫邊界；補 4.1～4.3 介面與 PUR-PO-001～024 完整情境，拆 006a／b／c／e／f。沒有改產品程式或新增資料表。DocGen 單元 7/7、`git diff --check` 通過；完整離線 DocGen 因 `MyERP_SRS.docx` 被外部程式佔用而未產報告，SQL 真實驗收仍待已 BLOCKED 的 005e4。下一輪先檢查 SQL 服務及檔鎖，再依相依關係推進 006a。

- 2026-10-24T02:28:26+08:00 WU-PUR-005f3e 第三輪標記 BLOCKED：`MSSQL$MSSQLSERVER2022` 為 Stopped，`Start-Service` 無法開啟服務，`sqlcmd` 共享記憶體連線失敗／登入逾時；真實 SQL／受權 HTTP fixture、整合全量與帶 DB DocGen 仍不可驗。先前前端 699/699、模擬 E2E 335 通過／5 略過及 Debug 建置／非 SQL 測試記錄有效，但不宣稱本輪重跑或本單元完成。完成數不變，按第 1 節列 blocked.md；下一輪做獨立設計 WU-PUR-006d。

- 2026-10-24T02:25:16+08:00 WU-PUR-005f3e 第二輪續做：補真實受權 HTTP 專項，驗 QUOTE-only、本人範圍、保存行／邀請、閏日有效進項稅率、停用幣別／銷項排除、次日空稅率與 401／403／404／400。Debug 全解決方案建置 0 警告／錯誤，Shared 381／381、架構 54／54、DocGen 7／7。專項在 SQL fixture 初始化即因 `MSSQL$MSSQLSERVER2022` Stopped、無法連線失敗；目前帳號 `Start-Service` 無法開啟服務。真實斷言、整合全量、帶 DB DocGen 與剩餘 4.14 E2E／QA 仍待驗，完成數不變；下輪續本單元，累計失敗 2／3。

- 2026-10-24T02:19:43+08:00 WU-PUR-005f3e 可編譯切片：QUOTE 專屬上下文按目前公司及動作資料範圍返回保存行、受邀供應商，經 MD 公開契約選目前公司啟用幣別和報價日有效進項稅率；4.14 改原生選單，日期切換清舊稅率，500 顯示 traceId。三語亮暗及正常、驗證、衝突、錯誤截圖已審查。前端 699/699，模擬 E2E 335 通過／5 略過，後補日期切換 1/1、建立／修訂重跑 1/1；Shared 381/381、架構 54/54、DocGen 7/7，後端 Debug 建置 0 警告／錯誤。`MSSQL$MSSQLSERVER2022` 仍停止，新增真實 SQL／受權 HTTP、後端整合全量及帶 DB DocGen 待服務恢復，完成數不變；下一輪續 005f3e 的真實邊界／授權驗收。

- 2026-10-24T01:55:46+08:00 WU-PUR-005f3c1 完成：4.15 以 COMPARE 守衛接已驗收唯讀 GET，逐行顯示原幣單價、即時本位幣含稅成本、匯率／進項稅率來源、不可比原因及空報價行；換公司、422、500／traceId、斷線清除舊成本，沒有 AWARD／CONVERT 寫入入口。接手上輪未提交切片，還原測試意外重拍的既有 INV／PUR 截圖；逐張檢視 14 張新截圖並修正英文首屏含稅成本被遮住。前端 698/698、模擬 E2E 全量 334 通過／5 略過、後端 Debug 建置 0 警告／錯誤、Shared 381/381、架構 54/54、DocGen 7/7。SQL 服務仍停止，005e3／e4／f2a2 保持 BLOCKED；下一輪依 backlog 做 005f3e QUOTE 專屬上下文／主檔選項的可獨立切片，決標操作待 SQL 全量驗收。

- 2026-10-23T22:14:56+08:00 WU-PUR-005f3b 完成：4.14 接首版／帶版本修訂／撤回 QUOTE API；以已保存詢價行與受邀供應商建表單，逐欄驗證六位十進位價格與日期，撤回確認並保留不可變歷史。409 保留草稿及確認重載，422／404 清舊詳情，400 顯示欄位原因，500 traceId 與斷線不報成功；未存離頁有確認。前端全量 697/697、模擬 E2E 全量 328 通過／5 略過，後補操作專項 5/5；後端 Debug 建置 0 警告／錯誤，Shared 381/381、架構 54/54、DocGen 7/7。4.14 新增 25 張三語亮暗及各狀態截圖逐張 QA。因現有表單依賴 RFQ.VIEW 讀取保存行、手輸稅率 ID，新增 P2 005f3e 供模組 QA 前完成；缺權時畫面明示原因。SQL 服務仍停止，005e3／e4／f2a2 保持 BLOCKED；下輪先做已驗收 COMPARE GET 的 005f3c1 唯讀比價畫面。

- 2026-10-23T21:33:17+08:00 WU-PUR-005f3a 完成：報價介面 4.14 以獨立 QUOTATION.VIEW 權限呈現分頁、URL 篩選與不可變修訂歷史，保留原幣六位單價及失效引用；4.11 加受權入口。換公司、篩選及請求失敗清過時資料，新增無效草稿篩選換公司守衛。前端單元 694/694、報價模擬 E2E 8/8；全量首輪 322 通過／1 個新測試 fixture 錯誤／5 略過，修正 fixture 後專項全過。後端 Debug 建置 0 警告／錯誤，Shared 381/381、架構 54/54、DocGen 7/7。4.14 的 18 張、4.11 入口 2 張截圖逐張審查，無剩餘 P1／P2。SQL 服務仍停止；離線 DocGen 因 SRS Word 檔被佔用未能產檔。下輪做 005f3b 報價首版／修訂／撤回前端操作；SQL 及 Word 解鎖後重試既有 BLOCKED／DocGen。

- 2026-10-23T16:57:25+08:00 WU-PUR-005f2c 完成：4.11 詢價按 VIEW + EXPORT 顯示安全 CSV，按已套用條件跨頁讀取並核對唯一 ID、總筆數、保存版本及 10000 明細行上限；取消、離頁、換篩選、403／500 或資料變動不下載部分檔。列印重新 GET 保存詳情，print media 白底黑字且不帶側欄。先紅燈後專項 11/11，前端全量 693/693、模擬 E2E 全量 317 通過／5 略過、Shared 381/381、架構 54/54、DocGen 7/7、Debug 後端建置 0 警告／錯誤；4.11 新截圖 19 張及同頁既有狀態逐張檢視，無 P1／P2。SQL 實例仍停止，真實整合沿用 BLOCKED 項；離線 DocGen 產檔被已開啟的 Word 鎖住，正式產檔待解鎖。下一輪 WU-PUR-005f3a 報價唯讀列表／保存修訂歷史。

- 2026-10-23T12:11:42+08:00 WU-PUR-005f2b 完成：RFQ 詳情接既有 SEND／CLOSE 版本 API，依各動作權限與 Draft／Sent 狀態顯示按鈕；確認框說明不寄電郵及關閉後不收新報價，取消不發請求。成功採保存回應更新列表／詳情；409 清過時詳情並重查，422 引用失效、500 traceId 有三語原因。測試先行紅燈後，前端 691/691、RFQ 動作模擬 E2E 6/6、全量 306 通過／5 略過，DocGen 7/7、Shared 381/381、架構 54/54，Debug 後端建置 0 警告／錯誤。4.11 新增 20 張亮暗／三語截圖逐張檢視無 P1／P2；全量 E2E 發現既有 RFQ 篩選測試使用舊標籤，修正定位後全量重跑通過。SQL 真實整合仍待已 BLOCKED 的 005f2a2 重試；下一單元 005f2c 詢價 CSV／列印。

- 2026-10-23T11:17:26+08:00 WU-PUR-005f2a2 依三次失敗規則標記 BLOCKED：RFQ 表單／來源選擇器與模擬 E2E 切片均已提交，本輪修正需求文件的過時手輸 ID 描述；`MSSQL$MSSQLSERVER2022` 仍為 Stopped，`Start-Service` 無法開啟服務，`sqlcmd` 連線逾時，真實 SQL／受權 HTTP 與整合全量無法驗收。Debug 全解決方案建置 0 警告／錯誤，Shared 381/381、架構 54/54、DocGen 7/7、前端 689/689；完成數不變。下一工作單元 005f2b 接既有 SEND／CLOSE API 做前端操作，SQL 恢復後按 blocked.md 重試 f2a2。

- 2026-10-23T07:00:49+08:00 WU-PUR-005f2a2 已保存來源與錯誤 QA 切片：`saved-options` 增加同公司請購行單號、行號、未轉量及可用旗標，表單顯示失效原因；共用選擇器支援 Enter／Space 並在 500 顯示 traceId。新增 SQL 測試原始碼與失效引用、來源 500／逾時模擬 E2E；前端 689/689、來源錯誤專項 3/3、架構 54/54、DocGen 7/7、後端 Debug 建置 0 警告／錯誤。新亮暗截圖逐張檢視，未見 P1／P2；截圖動畫時序已修。SQL 服務仍停止且 `Start-Service` 無權，真實 SQL／HTTP 與後端全量未跑；本單元未結案，完成數不變。

- 2026-10-23T06:51:31+08:00 WU-PUR-005f2a2 補充驗收切片：RFQ CREATE／EDIT 路由拒絕、換公司後已保存行與供應商替換、未存離開、空請購來源不寫入，新增模擬 E2E 及亮暗／提示截圖並逐張檢視。未存離開測試定位翻譯修正後整份表單 E2E 12/12。SQL 服務仍停止且目前帳號無權啟動，真實 SQL／HTTP 及後端全量未跑。已保存來源可讀名稱、停用引用、來源失敗／逾時及完整 QA 續本單元；完成數不變。

- 2026-10-23T06:43:57+08:00 WU-PUR-005f2a2 來源選擇切片：以先紅燈前端契約測試新增 RFQ CREATE／EDIT 受權請購來源分頁 API，僅選目前公司已核准且未轉量大於零的行，依物料／單號搜尋，畫面顯示單號、行號、餘量並保存實際行 ID；變更物料清除舊來源。後端 Debug 建置 0 警告／錯誤、前端正式建置及 689/689、架構 54/54、DocGen 7/7、來源模擬 E2E 1/1。新亮暗截圖逐張審查無 P1／P2。SQL 服務仍停止，真實 SQL／HTTP、路由及換公司等補充 E2E 未驗，本單元維持 IN_PROGRESS，完成數不變。

- 2026-10-23T06:35:10+08:00 WU-PUR-005f2a2 前端切片部分交付：建立／修訂表單接 RFQ 專屬 CREATE／EDIT 路由與 MD 選項，保存行及供應商、版本 409、狀態守衛、未存離開、三語錯誤及成功回列表。前端正式建置及全量 689/689、RFQ 列表與表單模擬 E2E 15/15；介面 4.12／4.13 共 18 張亮暗／三語截圖逐張檢視。QA 發現請購來源仍須手輸行 ID，留同單元補選擇器及相關授權 E2E；SQL `MSSQL$MSSQLSERVER2022` 停止且無啟動權，真實 SQL／HTTP 和後端全量未跑。完成數不變。

- 2026-10-23T06:24:26+08:00 WU-PUR-005f2a2 部分交付：RFQ 加 CREATE／EDIT 專屬 MD 物料及已核准供應商分頁選項、EDIT 範圍詳情及先驗單據再讀已保存引用；前端服務四端點先紅燈後 3/3。新增真實 SQL 測試涵蓋 EDIT 無 VIEW、供應商待審／停用／軟刪／跨公司，但 `MSSQL$MSSQLSERVER2022` 仍停止，尚未執行。Debug 後端全解決方案 0 警告／錯誤、架構 54/54、DocGen 7/7、前端 688/688 且正式建置通過。本單元未完成：表單、E2E、截圖 QA、SQL／HTTP 與後端全量留下一輪；完成數不變。

- 2026-10-23T06:15:30+08:00 WU-PUR-005f2a1 完成：接手上一輪未提交的 RFQ 草稿服務與 076～083 測試情境，先寫缺驗證函式的紅燈測試，補 POST／帶版本 PUT 契約與日期、行、供應商、來源 ID 的逐欄驗證；六位小數保留為十進位字串。發現暫加的錯誤碼白名單沒有三語翻譯，已恢復原有映射。專項 2/2、前端全量 687/687、正式建置成功、DocGen 測試 7/7；沒有新畫面或後端資料表。005f2a2 接 RFQ 獨立授權的 MD 選項、建立／修訂表單、三語亮暗與 Playwright QA；005e3／e4 仍待 SQL 服務恢復。

- 2026-10-23T01:50:30+08:00 WU-PUR-005f1 完成：既有 RFQ GET API 接三語亮暗唯讀列表／保存詳情，按 VIEW 路由權限、目前公司及服務端資料範圍查詢；篩選與頁碼可分享、重設。換公司、換頁、無結果、500／網絡中斷清舊詳情，404 詳情保留原因並重查列表；不顯示尚未驗收的發送、關閉或 CONVERT。介面 4.11 的 20 張亮暗／三語截圖逐張審查，修欄位提示可及性、發送時間格式和舊列殘留。正式前端建置成功、單元 685/685、模擬 E2E 全量 283 通過／5 略過；最後產品修改後 RFQ 專項 7/7，另補第二頁 1/1、草稿／關閉唯讀 1/1；DocGen 測試 7/7。SQL 服務仍停止，真實 SQL 後端全量未重跑，005e3／e4 保持 BLOCKED。下一輪拆 005f2 的建立／修訂表單子單元。

- 2026-10-23T01:24:12+08:00 WU-PUR-005e4 第三輪標記 BLOCKED：`MSSQL$MSSQLSERVER2022` 仍停止，`Start-Service` 再次因無服務控制權失敗，`sqlcmd` 無法連線。Debug 全解決方案建置 0 警告、0 錯誤，既有輸入驗證 9/9；真實 SQL 紅燈、migration、CONVERT 端點與整合全量仍未執行，不計完成數。詳見 blocked.md；下一輪改做 PUR 詢價前端可獨立切片，SQL 恢復後重試 005e3／e4。

- 2026-10-23T01:21:21+08:00 WU-PUR-005e4 第二輪部分交付：SQL Server 仍停止且 `Start-Service` 無權啟動。先按 PUR-RFQ-061 寫缺型別紅燈，新增轉單輸入驗證（版本、供應商、1～1000 個正數互異決標 ID），補空集合與上限後專項 9/9、架構全量 54/54 通過；`dotnet restore` 修復本機缺失的 NuGet 參照後 Debug 解決方案建置 0 警告、0 錯誤。尚無轉單端點或資料表，真實 SQL／HTTP 與 DocGen 帶 DB 驗收待服務恢復，005e4 不計入完成數。

- 2026-10-23T01:15:35+08:00 WU-PUR-005e4 設計切片：按 ADR-070 細化 REQ_F4114.6.3 v1.1、三表草稿／來源唯一約束、CONVERT API、BR-PUR-019 與 PUR-RFQ-060～068 完整情境；DocGen 測試 7/7。SQL Server 實例仍停止且目前帳號無權啟動，故未寫真實 SQL 紅燈測試、migration 或端點；離線 DocGen 輸出因 Word 開啟 SDS 檔被鎖，未取得本輪一致性報告。下一輪續做同一單元，先恢復 SQL 與釋放 SDS 檔鎖，按測試清單實作及全量驗收；本切片未計入完成數。

- 2026-10-23T01:11:27+08:00 WU-PUR-005e3 第三輪標記 BLOCKED：決標模型、端點及 PUR-RFQ-055～059 專項已提交且 2/2 通過，但全量回歸未完成。測試實例 `MSSQL$MSSQLSERVER2022` 本輪為 Stopped；`sqlcmd` 無法連線，`Start-Service` 因目前帳號無服務控制權失敗。前兩輪既有 SQL 案例於低記憶體下逾時；本輪約 1.8 GB 可用，未能採集運行中 DMV。服務恢復後依 blocked.md 重跑單執行緒全量、決標專項和帶 DB DocGen；下一輪依第 1 節跳到 005e4，不宣稱 005e3 已完成。

- 2026-10-21T21:26:21+08:00 WU-PUR-005e3 續輪部分交付：PUR-RFQ-056～059 補真實 SQL 未生效報價、草稿、跨公司及保存 INSERT 注入故障回滾；受權 HTTP 補本人資料範圍外 404。決標專項 2/2、Release 建置 0 警告、Shared 381/381、架構 45/45、DocGen 測試 7/7、帶 DB DocGen 0 Error／66 Warning。整合全量單執行緒於既有 SafetyStockTests 五例 SQL 命令逾時後停止；可用記憶體約 0.65 GB，當下 DMV 無持續阻擋，失敗類案例隔離 1/1 通過。全量仍未通過，005e3 維持 IN_PROGRESS，下一輪先排查主機資源與 SQL 等待再全量重跑；未開始 005e4。

- 2026-10-21T21:05:19+08:00 WU-PUR-005e3 部分交付：接手 18 個未提交決標檔案，完成不可變 `pur_rfq_award`、可逆 migration 並套用 `myerp`；AWARD 按公司、SYS 動作範圍、版本及 Sent 狀態逐行保存理由與即時 MD 價格／稅率／匯率快照，表頭鎖與唯一索引守衛雙連線只決標一次。新增理由長度、無效來源 ID、SQL DELETE 補充測試後，Release 建置 0 警告、決標 SQL／HTTP 專項 2/2、Shared 381/381、架構 45/45、DocGen 7/7，帶 DB DocGen 0 Error／66 Warning。整合全量兩次在可用記憶體約 0.8–1.0 GB 下，分別於既有期初庫存及安全庫存 SQL 資料準備逾時；三項失敗案例隔離重跑各 1/1 通過，尚不能宣稱全量通過。PUR-RFQ-056～059 的其餘分支及全量回歸留本工作單元續輪，005e3 不結案；未開始 005e4。

- 2026-10-21T17:14:26+08:00 WU-PUR-005e2 完成：接手前輪未提交的 5 個比價檔案，完成 `PUR.RFQ.COMPARE` 唯讀逐行含稅比價 API。依目前公司與 SYS 動作資料範圍先驗詢價；跨幣別經 MD 匯率／進項稅率快照換算，逐行 `Money.Round` 並按本位幣含稅成本穩定排序。撤回、過期、未生效、缺有效匯率／稅率及溢位保留明確不可比原因；已決標報價仍供歷史對照，同幣別／稅率每次請求只查一次。真實 SQL 驗排序、日期／狀態／缺率、空報價、資料範圍與 MD 故障；受權 HTTP 驗 200／401／403／404／422。測試草稿前置曾因 `SentAt` 未清空觸發 CHECK，修正後 Release 建置 0 警告、整合全量 618/618、Shared 381/381、架構 45/45、DocGen 7/7；帶 DB DocGen 0 Error／67 Warning。前端未改；下一單元 005e3 建決標快照、migration、AWARD 端點及雙連線唯一性。P0-015／016 真實啟動仍待預設埠釋放。

- 2026-10-21T16:41:55+08:00 WU-PUR-005e1 完成：沿用已提交的 MD 匯率／進項稅率快照契約與日期、公司、失效真實 SQL 專項 2/2。本輪先確認主機可用記憶體約 1.3 GB、SQL 測試庫無執行請求，再用既有診斷腳本單獨跑 Release 整合全量，617/617 通過（6 分 12 秒）。SQL 採樣只有短暫鎖等待，無持續阻擋；單元 381/381、架構 45/45、DocGen 7/7、帶 DB 0 Error／67 Warning 沿用上一輪已通過結果。未改產品程式或測試斷言；下一輪做 005e2 逐行跨幣別含稅比價 API。

- 2026-10-21T16:30:52+08:00 WU-PUR-005e1 回歸重試：SQL Server 起初無執行中的 MyERP 請求，單獨以 `--max-threads 1` 重跑 Release 整合全量，仍在既有供應商分類測試 SQL 查詢逾時，兩個詢價測試因 SQL 交易終止連帶回滾失敗；診斷查詢本身延遲，可用實體記憶體僅約 0.9／32 GB。已停止測試，沒有削弱斷言或修改產品程式；e1 專項 2/2 及上一輪其他回歸結果仍保留，但全量未通過，005e1 繼續 IN_PROGRESS，失敗累計 2/3。下一輪先確認主機資源及 SQL 等待狀態後重跑；詳細證據見 PUR optimization-log。

- 2026-10-21T16:23:15+08:00 WU-PUR-005e1 部分交付：005e 範圍超過 90 分鐘，拆為 e1～e4；本輪先按 ADR-070 補 MD 匯率／進項稅率公開快照契約，返回值、來源 ID 及有效期間，不新增 PUR 表或端點。真實 SQL 測試先紅燈後專項 2/2 通過，涵蓋本位幣 1、日期邊界、跨公司、缺率、軟刪及方向／到期；Release 建置 0 警告，Shared 381/381、架構 45/45、DocGen 7/7、帶 DB DocGen 0 Error／67 Warning。DocGen 初跑因新測試 ID 與既有 ID 重複 2 Error，改用 051～052 後通過。全量整合 `--max-threads 2` 在安全庫存／SYS 等舊測試 SQL timeout，`--max-threads 1` 又在十萬筆庫存估值資料準備逾時並連帶斷線；單獨重跑安全庫存 1/1 通過，與 P0-014 所記錄的主機資源競爭相符。本切片暫不標 DONE，下一輪先重跑全量驗收。7180／5180／4200 仍由既有 MyERP.Api／node 佔用，P0-015／016 待續。

- 2026-10-21T11:57:34+08:00 WU-PUR-005d4 與父 005d 完成：供應商報價撤回端點按 QUOTE 權限、公司及動作資料範圍與目前版本只改表頭，保留全部不可變修訂；查詢用 `IClock` 回 `isExpired`，已過期／已決標不能修訂或撤回。真實 SQL 驗重複／舊版、跨公司、MD 查詢故障後重試與雙連線撤回／修訂僅一次成功；受權 HTTP 驗 400／401／403／404／409／422 及真實 MD 幣別／稅率軟刪。Release 後端全量 1050/1050，帶 DB DocGen 0 Error／67 Warning；前端未改，沿用 685/685 與模擬 E2E 277 通過／5 略過。7180／5180／4200 仍被既有程序佔用，P0-015／016 待續；下一單元 005e 接逐行比價、決標與原子轉採購單草稿。

- 2026-10-21T11:38:53+08:00 WU-PUR-005d3 完成：首版報價與不可變修訂 POST 限已發送、未關閉及 QUOTE 動作範圍內的詢價；受邀供應商逐行覆蓋詢價，按目前公司 MD 公開契約重驗啟用幣別及報價日有效進項稅率。詢價表頭鎖、同供應商唯一及表頭版本 409 守衛競態，整單失敗回滾；舊修訂只讀保留。真實 SQL 驗六位價格、零價、整單拒絕、MD 停用及雙連線同版；受權 HTTP 驗 201／200／400／401／403／404／409，真實 MD 稅率 ID／日期／方向／公司隔離。架構權限清單測試因新 QUOTE 未列期望初跑 44/45，補齊後 45/45；Release 建置零警告，整合全量 613/613、Shared 381/381、DocGen 測試 7/7、前端 685/685，帶 DB DocGen 0 Error／67 Warning。預設 7180／5180／4200 仍佔用，P0-015／016 待續；005d4 接撤回、過期／決標與故障邊界。

- 2026-10-21T11:07:58+08:00 WU-PUR-005d2 完成：接手上一輪 9 個未提交的報價查詢／測試／文件變更，完成 `PUR.QUOTATION.VIEW` 受權分頁列表與全部不可變修訂詳情。公司及本人／部門／分店範圍先驗詢價；保留撤回歷史、逐行六位價格，真實 SQL 補同詢價多供應商跨頁與篩選，HTTP 驗 401／403／404／400。Release 建置 0 警告；專項 SQL 2/2、HTTP 1/1，整合專案單獨全量 607/607，單元專案均通過；帶 DB DocGen 0 Error／67 Warning。首次解決方案全量並行啟動多專案時，既有庫存安全量 SQL 逾時、組織 API 500；兩例單獨重跑皆通過，整合專案單獨全量通過。預設 7180／5180／4200 仍佔用，P0-015／016 待續；005d3 接首版與修訂寫入。

- 2026-10-21T06:41:45+08:00 WU-PUR-005d1 完成：先寫缺模型的真實 SQL 紅燈測試，建立報價表頭、不可變修訂與逐行價格三表（REQ_DS43.166～168）及可逆 migration，已套用 `myerp`。受邀供應商同公司 FK、單一詢價／供應商唯一、修訂及行唯一、日期／非負六位單價／幣別／稅率約束及歷史 UPDATE／DELETE 觸發器均以真實 SQL 驗；專項 1/1。首次雙執行緒全量 1035/1037，兩項既有庫存估值測試因 SQL 連線關閉／逾時失敗；該類單執行緒 6/6 與全量單執行緒 1037/1037 通過。Release 建置 0 警告；帶 DB DocGen 0 Error／67 Warning。前端未改，沿用 685/685 與模擬 E2E 277 通過／5 略過。預設 7180／5180 仍由 PID 36248 佔用，P0-015／016 待續；005d2 接受權唯讀報價與歷史 API。

- 2026-10-21T06:21:44+08:00 WU-PUR-005c3 與父 005c 完成：詢價 SEND 同交易重驗 MD 物料／供應商、已核准請購來源與餘量，保存供應商代碼／名稱和共同 sentAt 快照；CLOSE 只允許已發送並保留歷史。補 SEND／CLOSE 權限與受權 HTTP 401／403／404／409／422，真實 SQL 驗停用、軟刪、未核准、來源駁回、餘量變動、故障回滾同版重試及雙連線同版只一次成功。競態紅燈揭露鎖升級死鎖，表頭更新鎖修正後專項 14/14；第一輪全量權限清單測試未同步，修正後 Release 1036/1036，最後來源專項 1/1。前端未改，沿用 685/685 與模擬 E2E 277 通過／5 略過。預設 7180／5180 仍由 PID 36248 佔用，P0-015／016 真實啟動冒煙待續；下一單元 005d 建不可變報價版本與 API。

- 2026-10-21T06:06:05+08:00 WU-PUR-005c2 完成：真實 MD 物料／供應商停用、軟刪及供應商未核准引用拒絕，SYS `PUR.RFQ` 同交易取號、發號後注入故障回滾與同號重試，已發送草稿不可修訂／軟刪；受權 HTTP 驗 201／200／204、範圍外 404、版本 409，以及雙連線並行建立不重號。真實 SQL 與 HTTP 新增 3 個測試，Release 全量後端 1033/1033；前端未改，沿用 685/685 與模擬 E2E 277 通過／5 略過。測試曾因 `IgnoreQueryFilters()` 跨公司計數失敗，已修正斷言並全量重跑通過。預設 7180／5180 真實啟動冒煙仍由 P0-015／016 處理；下一工作單元 005c3 接發送快照、關閉、重驗引用及競態。

- 2026-10-21T05:53:27+08:00 WU-PUR-005c2 部分交付：接手上一輪未追蹤的服務與 SQL 測試，完成草稿 POST／PUT／DELETE、CREATE／EDIT／DELETE 權限、公司及動作資料範圍、輸入驗證、MD 公開契約檢查與同公司已核准請購來源。真實 SQL 驗建立／整組修訂／軟刪、閏日六位數量、舊版 409、整單拒絕、請購來源及庫存零異動；受權 HTTP 驗 401／403。修正只換子行時版本不遞增。Release 建置 0 警告；全量後端 1030 個中 1029 通過，剩餘架構權限清單測試更新後單專案 45/45，合計 1030/1030。真實 MD 停用／軟刪、SYS 取號、成功 HTTP、跨範圍及雙連線回滾仍待下一輪驗收，故 005c2 保持 IN_PROGRESS。預設 7180／5180 埠仍佔用。

- 2026-10-21T02:13:51+08:00 WU-PUR-005c1 完成：預設 7180／5180 埠仍由 PID 36248 佔用，4200 未監聽，P0-015／016 真實啟動待續。因 005c 涵蓋查詢、草稿交易與發送快照，拆成 005c1～c3。本切片先寫缺查詢服務／權限的紅燈真實 SQL 測試，交付 `GET /api/v1/pur/rfqs` 列表與 `GET /{id}` 保存詳情；公司與 SYS VIEW 資料範圍於 SQL 分頁前套用，白名單排序、日期／狀態／請購行篩選、軟刪隱藏及行／邀請快照均受測。真實 SQL／受權 HTTP 各 1/1，Release 後端全量 1026/1026；前端未改，沿用 685/685 與模擬 E2E 277 通過／5 略過。005c2 接草稿建立／修訂／軟刪、SYS 單號、MD 與請購來源及版本／交易；005c3 接發送與關閉。本切片沒有寫入端點或畫面。

- 2026-10-21T02:04:12+08:00 WU-PUR-005b 完成：預設 7180／5180 埠仍由既有程序佔用，4200 未見監聽，P0-015／016 真實啟動待續。先寫缺詢價模型的紅燈真實 SQL 測試，再建立 `pur_rfq`、`pur_rfq_line`、`pur_rfq_invitation` 及可逆 migration 並套用 `myerp`；分配 REQ_DS43.163～165。公司單號與邀請唯一、同公司父子／請購來源複合 FK、六位數量、日期、發送快照 CHECK 及列表／引用索引已核對。專項 2/2、Release 後端全量 1024/1024、帶 DB DocGen 0 Error／71 Warning。005c 接草稿 API、SYS 單號、MD 引用、發送／關閉與真實 SQL／HTTP；本切片沒有端點或畫面。

- 2026-10-21T01:47:06+08:00 WU-PUR-005a 完成：預設 7180／5180 埠仍佔用，4200 未見監聽，P0-015／016 真實啟動待續。將父 005 拆為 005a～f；ADR-070 固定詢價發送快照、不可變報價修訂、不同幣別有效匯率比較、逐行決標和原子轉採購單草稿界線。新增 REQ_F4114.4～6 共 13 項、介面 4.11～4.15、BR-PUR-014～020 及 PUR-RFQ-001～018 全類測試先行清單；資料表 ID 待 migration 落地，未聲稱端點已完成。修正 DocGen 發現的需求範圍縮寫追溯；帶 DB 0 Error／77 Warning（新表待建追溯警告），DocGen 測試 7/7、Release 後端全量 1022/1022、前端 685/685、模擬 E2E 277 通過／5 略過。下一單元 005b 先紅燈真實 SQL 測試，再建立詢價模型與 migration。

- 2026-10-21T01:27:11+08:00 WU-PUR-004d 與父 004 完成：預設 7180／5180 埠仍佔用，真實啟動 P0-015／016 繼續追蹤。請購介面 4.8 新增按目前已套用篩選遍歷全部分頁及保存明細的 UTF-8 CSV；核對總筆數與唯一 ID，限制 10000 明細行，防公式注入，取消、公司／查詢變動、詳情錯誤不下載部分檔。成功提示為實際 CSV 行數，零行及超限明示原因。CSV 13/13、全量模擬 E2E 268 通過／5 略過、前端 685/685、Release 後端 1022/1022，帶 DB DocGen 0 Error／64 Warning。亮暗匯出／超限／500 截圖已逐張審查，4.9／4.10 回歸通過。DocGen 首次 SQL 逾時，確認資料庫可連後重跑通過。下一單元 005 先按 30～90 分鐘拆分詢價、報價、比價與轉單。

- 2026-10-21T00:58:38+08:00 WU-PUR-004c 完成：接手上一輪未提交請購表單變更，還原 004c 以外重產的 INV／PUR 截圖。介面 4.9／4.10 加入 CREATE／EDIT 路由與資料範圍守衛、MD 受權物料選項、逐欄日期／六位數量／行數／備註驗證、未存離開確認，以及 400／409／422／500／斷線／逾時訊息和三語亮暗畫面。真實受權 HTTP 增加 EDIT-only 與物料選項測試；截圖發現的停用原因截斷、衝突重載及伺服器欄位路徑問題已修正。Release 建置 0 警告，後端 1022/1022、前端 683/683、表單 E2E 12/12、全量模擬 E2E 264 通過／5 略過。預設 7180／5180 埠仍由既有程序佔用，P0-015／016 真實啟動驗收待續；004d 接完整安全 CSV，父 004 保持 TODO。

- 2026-10-20T19:58:41+08:00 WU-PUR-004b 完成：預設 7180／5180 埠仍由既有程序佔用，P0-015／016 真實啟動冒煙仍待續。接手上一輪 20 個未提交請購前端／測試／翻譯變更，完成介面 4.8 三語亮暗列表、已保存詳情與兩輪審批歷史、狀態動作、刪除確認及列印；搜尋時清除舊詳情，MD 物料停用／已刪原因具體呈現。QA 30 張各狀態截圖逐張審查；請購 E2E 14/14，模擬 E2E 全量 250 通過／5 略過，前端 680/680，Release 後端 1019/1019。首次後端 `--no-build` 指向缺少的 Debug 執行檔，改用已有 Release 執行檔全量通過。004c 接建立／修訂表單，004d 接安全 CSV；父 004 保持 TODO。

- 2026-10-20T16:11:30+08:00 WU-PUR-004a 完成：預設 4200／7180／5180 埠仍佔用，將 004 拆成 004a～d。先寫缺服務的前端紅燈測試，新增請購列表／詳情／建立／修訂／送審／刪除契約與 1～1000 行、閏日、六位十進位數量、500 字備註守衛；不送可偽造的請購人／組織歸屬。真實受權 HTTP 建立／修改改送數量字串並精確回讀，無需後端改動。前端 674/674、Release 後端單執行緒全量 1019/1019、前端正式建置、帶 DB DocGen 0 Error／64 Warning。初次同時執行前後端全量，後端三個非 PUR 案例失敗；各自重跑及完整單執行緒重跑通過，原因尚未完全定位，見 PUR optimization-log。004b 接列表／詳情畫面與 QA；004c 表單，004d CSV。此切片沒有畫面，無須截圖審查。

- 2026-10-20T15:45:56+08:00 WU-PUR-003c 與父 003 完成：預設 4200／7180／5180 埠仍被既有程序佔用。真實 SQL／受權 HTTP 補版本零／負、駁回未修訂／待審／核准／關閉／軟刪、本人／分店／部門範圍、真實 MD 物料停用／軟刪與恢復重試；雙連線同版本送審和 SYS 最終決定只各成功一次，PUR／SYS 狀態、稽核及零 INV 異動一致。測試造軟刪資料首次漏 `deleted_at`，補齊後通過，產品程式無需修正。Release 後端全量 1019/1019，最後補的 SYS 稽核斷言專項 1/1；帶 DB DocGen 0 Error／64 Warning。前端未改；004 請購畫面待拆分，P0-015／016 真實啟動及 INV-011c 同資料畫面聯查仍待預設埠釋放。

- 2026-10-20T15:24:40+08:00 WU-PUR-003b 完成：預設 4200／7180／5180 埠仍佔用。以缺結果處理器的紅燈測試起步，註冊 PUR `IApprovalResultHandler`，SYS 最終核准／駁回與請購狀態同交易保存；PUR 詳情先驗 VIEW 公司／資料範圍，再讀 SYS 所有輪次和逐步意見。真實 SQL 驗駁回、修訂重送、最終核准、零 INV 異動及請購狀態變化時 SYS 決定回滾；受權 HTTP 驗歷史和 SYS 決定後雙方持久狀態。首輪全量 1008/1009，既有 INV 拆卸測試 SQL 逾時；單獨 1/1 及單執行緒全量重跑 1009/1009 通過，未削弱測試。前端未改；003c 接 MD 停用／軟刪、狀態、資料範圍與併發邊界。

- 2026-10-20T14:49:43+08:00 WU-PUR-003a 完成：預設 4200／7180／5180 埠仍佔用，將 003 拆成 003a～c。本輪先以缺送審服務的紅燈測試起步，新增 SUBMIT 端點及 SYS 審批契約呼叫；公司、動作權限、資料範圍、版本、草稿與 MD 引用檢查後，在同一 SQL 交易保存待審或即時核准狀態。真實 SQL 4/4 驗 PUR 狀態、零 INV 異動、回滾及拒絕分支；匿名 HTTP 3/3。Release 後端全量 1007/1007，帶 DB DocGen 0 Error／64 Warning；前端未改，沿用既有結果。SYS 真實審批實例、人工決定、歷史與原子回滾由 003b 接續；003c 補 MD／狀態／併發邊界，004 做畫面。

- 2026-10-19T11:20:28+08:00 WU-PUR-002b2c 與父 002b2／002b／002 完成：請購 PUT 依 EDIT 權限和資料範圍整組替換草稿／駁回行，駁回修訂回草稿；DELETE 依 DELETE 範圍只軟刪草稿。真實 SQL／HTTP 驗 401／403／404／409／422、MD 停用／軟刪、行回滾、無 INV 異動、PUR 併發取號及失敗後重試。本人範圍紅燈測試發現未映射 `OwnerUserId` 無法轉 SQL，改以 `RequesterId` 查詢後通過；Release 後端全量 1003/1003、請購專項 15/15，帶 DB DocGen 0 Error／64 Warning。前端／E2E 未改，沿用既有結果；預設 4200 埠仍佔用，P0-015 真實啟動待續。下一單元 PUR-003 送審、SYS 決定與稽核，開始前按 30～90 分鐘拆分。

- 2026-10-19T11:01:56+08:00 WU-PUR-002b2b 完成：接手上一輪 15 個未提交變更，確認先前缺建立服務及 MD 契約的紅燈測試；建立 POST 從 SYS 目前授權取請購人／分店／部門，於同交易驗 MD 有效物料、取 SYS 單號及保存整單。真實 HTTP 驗 201、未登入 401、缺權 403、不存在物料 404、停用物料 422 和偽造歸屬無效；真實 SQL 驗閏日、六位數量、整單保存及無 INV 異動。Release 建置 0 警告、後端全量 998/998、帶 DB DocGen 0 Error／64 Warning；前端／E2E 未改，沿用既有結果。預設 4200 埠仍佔用，P0-015 真實啟動未驗；002b2c 接修改／軟刪、版本 409 及 PUR 專屬併發取號測試。

- 2026-10-19T06:51:04+08:00 WU-PUR-002b2a 完成：預設 4200／7180／5180 埠仍佔用，將過大的 002b2 拆成資料範圍 002b2a、建立 002b2b、修改／軟刪 002b2c。先以缺歸屬欄位及服務授權的紅燈編譯測試起步；補表頭分店／部門欄位與索引 migration 並套用 myerp，列表／詳情按 SYS VIEW 本人、部門、分店、全公司範圍過濾，缺權 403、範圍外／他公司 404。真實 SQL 專項 2/2、已登入 HTTP 1/1、Release 後端全量 994/994、帶 DB DocGen 0 Error／64 Warning；前端／E2E 未改，沿用既有結果。首輪 HTTP 404 案例由 SYS 未授權公司先回 403，修正測試前置後通過，見 PUR optimization-log。下一單元 002b2b 建立草稿。

- 2026-10-19T06:38:59+08:00 WU-PUR-002b1 完成：預設 4200／7180／5180 埠仍由既有程序佔用，將 002b 拆為唯讀 002b1 與寫入 002b2。先以缺少查詢服務的紅燈編譯測試起步，加入請購列表／詳情 GET、白名單排序與分頁、日期／狀態／請購人篩選、已保存明細及版本 DTO；真實 SQL 驗跨公司 404、軟刪隱藏、無效查詢與閏日六位數量，HTTP 驗兩端點未登入 401。Release 後端全量 990/990，新增 HTTP 專項 2/2。現有表頭缺分店／部門歸屬，已登入權限與資料範圍驗收留 002b2；審批歷史留 003。前端／E2E 未改，沿用上輪結果；帶 DB DocGen 未重跑。

- 2026-10-19T06:29:31+08:00 WU-PUR-002a 完成：預設 4200／7180／5180 埠仍由既有程序佔用，按 30～90 分鐘切片將 002 拆為資料表 002a 與 CRUD 002b。先以缺少模型的紅燈編譯測試起步，建立 `pur_requisition`／`pur_requisition_line` 及 migration 並套用 myerp；分配 REQ_DS43.161～162，同公司複合 FK、單號／行號唯一與數量上限 CHECK。真實 SQL 驗閏日、六位數量、負數拒絕與他公司不可見；Release 後端全量 989/989，帶 DB DocGen 0 Error／64 Warning。沒有 CRUD 或 HTTP 端點；002b 接手 SYS 單號、MD 引用、版本／軟刪及 API 驗收。

- 2026-10-19T06:18:24+08:00 WU-PUR-001 完成：預設 4200／7180／5180 埠仍由既有程序佔用，依 nextAction 轉做 PUR 骨架。先寫缺 PUR 組件的紅燈編譯測試，再加入獨立 Contracts／實作組件、solution／API 組合根、請購六個權限與公司守衛；缺或無效公司回 403，其他公司單回 404。專項 5/5、Release 建置 0 警告、後端全量 988/988。無 migration、資料表、HTTP 端點或前端變更；002 才實作草稿。P0-015 與 INV-011c 真實瀏覽器驗收仍待預設埠釋放。

- 2026-10-19T06:07:41+08:00 WU-PUR-000 完成：因 WU-P0-015 的 4200／7180／5180 仍由既有互動程序佔用，按 nextAction 拆 EP-PUR 為 000～009 並完成第一個請購設計切片。追加 REQ_F4114.3.1～6、介面 4.8～4.10、資料模型預案、API／狀態規則及 PUR-REQ-001～018 全類情境；新表待 002 migration 落地才分配 REQ_DS43。DocGen 測試 7/7，帶 DB 0 Error／70 Warning（較 INV 前次多 6 個請購表尚未建的追溯警告）；本輪沒有產品程式與 DB 變更，未宣稱請購功能已可用。下一輪優先檢查預設埠，若仍佔用則進入 WU-PUR-001。

- 2026-10-19T06:02:59+08:00 WU-INV-012f 與父 012 完成：接手上一輪兩個未提交真實 SQL 測試，補來源序號不存在、第二組件 INSERT 故障注入及成本快照與外部入庫雙連線交錯；真實 HTTP 驗 SYS 最終核准前停用倉庫時決定／單據仍待審且零異動，恢復後同版本核准成功。新增案例未發現產品缺陷。Release 後端全量 983/983、帶 DB DocGen 0 Error／64 Warning；前端與模擬 E2E 未改，沿用 012c 的 672/672 與 232 通過／5 略過。011c 真實瀏覽器同資料聯查仍受 P0-015／016 阻擋；015b 待 PUR／SD。

- 2026-10-19T01:34:06+08:00 WU-INV-012e 完成：補拆卸真實 SQL／HTTP 邊界驗收，負來源／組件數量與權重拒絕、2027 年底至 2028 年初最小六位量保存、錯倉儲位、保存後停用倉庫及停用／軟刪組件送審拒絕、已保存停用名稱與 SYS 駁回意見。所有新增案例首次即通過；現有產品守衛符合契約，無需產品修改。拒絕／駁回均確認草稿或單據狀態及拆卸異動帳零寫入。Release 後端全量 980/980、帶 DB DocGen 0 Error／64 Warning；前端及模擬 E2E 未改，沿用 012c 的通過結果。父 012 仍待 012f；011c 真實畫面同資料聯查仍受 P0-015／016 阻擋。

- 2026-10-19T01:20:07+08:00 WU-INV-012c 完成：接手上一輪 19 個未提交變更，完成拆卸列表／建立／編輯與詳情的三語亮暗畫面、受權選項、審批歷史、逐行錯誤、列印與安全 CSV。先寫失敗測試再修正數值上限精度，並修正提交失敗訊息被刷新清除、詳情舊請求覆蓋、匯出欄名與檔名；模擬 E2E 驗證 201 筆跨頁 CSV、公式防護、持久資料列印、取消／停用引用、未存離開、400／409／422／500／斷線／逾時、唯讀與空狀態。按 UX 清單審查 5.12–14 三語、亮暗及錯誤截圖；Release 後端 977/977、前端 672/672、模擬 E2E 全量 232 通過／5 略過、拆卸專項 18/18，帶 DB DocGen 0 Error／64 Warning。父 012 因既有 SQL／HTTP 邊界缺口拆出 012e／f 保持未結案；011c 真實瀏覽器同資料聯查仍受 P0-015／016 阻擋。

- 2026-10-18T20:29:05+08:00 WU-INV-012b 完成：先以缺少送審的真實 SQL 紅燈測試起步，實作拆卸 `SUBMIT` 與 SYS 最終決定處理器；依保存權重分攤成品可靠平均成本，四位行值守恆且每行六位入庫單價可重算，成品 Out、組件 In、Posted 與 SYS 核准同交易。真實 SQL／HTTP 覆蓋零／未知成本、精度／估值溢位、來源與批號不足、序號衝突、雙連線、來源快照重放、駁回、SYS 核准失敗回滾及重試；拆卸專項 10/10。Release 後端全量 977/977，帶 DB DocGen 0 Error／64 Warning；前端未改，沿用 669/669、模擬 E2E 216 通過／5 略過。父 012 尚待 012c 三語亮暗頁、CSV、E2E 與 QA；011c 真實瀏覽器／SQL 同資料聯查仍受 WU-P0-015／016 阻擋。

- 2026-10-18T20:08:56+08:00 WU-INV-012a 完成：先以缺少拆卸型別的真實 SQL 紅燈測試起步，新增三表 migration 並套用 myerp；拆卸草稿 GET／POST／PUT／DELETE、SYS 單號、MD 引用、公司／權限隔離、版本／狀態、分攤權重及批序號保存均落地，送審端點保留 012b。真實 SQL／HTTP 專項 3/3，含 1000 行、閏日、批號替換、無效權重 400、版本 409、跨公司 404、唯讀 403 與草稿無庫存異動；Release 後端全量 970/970。帶 DB DocGen 0 Error／64 Warning；並行全量測試時曾 SQL 逾時，單獨重跑通過，診斷限制記於 optimization-log。前端與 E2E 未改，沿用 669/669、216 通過／5 略過。父 012 尚未完成，下一單元 012b 審批、成本分攤與原子過帳。

- 2026-10-18T19:50:12+08:00 WU-INV-012d 完成：接手上一輪 14 個未提交變更，確認 ADR-069、REQ_F4115.5.1–6、預留 REQ_DS43.158–160、介面 5.12–14、BR-INV-058～062、API 與 INV-DIS-001～016 已覆蓋拆卸成本守恆、SYS 原子審批、批序號、競態、權限和失敗回滾。DocGen 帶 DB 初跑因尚未部署的三表列為正式表而有 3 Error；調整為保留 ID 的設計敘述，待 012a migration 落地再列正式表，重跑 0 Error／70 Warning（其中 6 項是拆卸預留 ID 的追溯缺口）。DocGen Release 建置 0 警告、測試 7/7；產品程式與 DB 未改，後端／前端／E2E 沿用 017c 的通過結果。父 012 尚未完成，下一單元 012a 草稿後端。

- 2026-10-18T15:53:32+08:00 WU-INV-017c 與父 017 完成：接手上一輪批號紅燈測試；批號／序號歷史 CSV 在單一 SQL SNAPSHOT 交易內計數與讀取，詳情仍只預覽最近 1000 筆，實際下載 1001 筆並顯示列數；10,000 列上限先拒絕且不寫檔，UTF-8 BOM、六位數量、公式字首守衛、取消及公司隔離均驗證。高頻批號 10,000 筆等價 SQL 的追蹤表暖快取 IO 20,059→71，覆蓋索引 migration 已套用 myerp；單次耗時與配置限制記在 optimization-log。三語亮暗匯出、422／500 traceId／斷線／取消／唯讀權限畫面與 QA 截圖已檢，追溯頁改用 TRACKING.VIEW 可讀的 INV 選項端點。Release 後端全量 967/967、權限專項 1/1、前端 669/669、模擬 E2E 全量 216 通過／5 略過、追溯最新 8/8，帶 DB docgen 0 Error／64 Warning。WU-INV-011c 真實瀏覽器與 SQL 同資料聯查仍待 BLOCKED 的 WU-P0-015 預設埠與 WU-P0-016 smoke；下一單元先做 WU-INV-012 的需求設計切片。

- 2026-10-18T10:30:26+08:00 WU-INV-017b 完成進出存三語亮暗頁與受權完整 CSV：伺服器 SNAPSHOT 快照內先計數，10,000 列上限超出時零位元組拒絕；UTF-8 BOM、六位小數、公式字首守衛、實際列數標頭與取消。REPORT.VIEW 角色可讀物料／倉庫／儲位選項，缺 EXPORT 隱藏動作；畫面處理負結存、空值、驗證、422、403、500 traceId、斷線及逾時。真實 SQL／HTTP 專項 9/9、Release 後端全量 963/963、前端 668/668、模擬 E2E 213 通過／5 略過、最新頁面 13/13；帶 DB docgen 0 Error／64 Warning，亮暗／三語 QA 截圖已檢。下一單元 017c 做批序號完整歷史匯出及大量追溯效能。

- 2026-10-18T09:50:54+08:00 WU-INV-017a 完成進出存唯讀 API：接手上一輪 14 個未提交變更，完成同一 SQL Server SNAPSHOT 交易內的總數與頁面、公司隔離、日期／物料／倉庫／儲位篩選及穩定排序；負期末標記、六位數量及 IClock 查詢時間已驗。兩個 migration 在 myerp 套用，10 萬筆等價 SQL 的邏輯讀取 2052→986，但暖快取耗時未改善，詳見 optimization-log。Release 建置 0 警告、進出存專項 4/4、後端全量 959/959、帶 DB docgen 0 Error／64 Warning；前端與 E2E 未改，沿用前次 667/667、199 通過／5 略過。下一單元 017b 接三語頁及伺服器完整 CSV。

- 2026-10-18T05:09:55+08:00 WU-INV-017d 完成報表設計切片：ADR-068 固定業務日期進出存數量與估值的口徑、單次 SQL 快照伺服器匯出、CSV 公式安全／取消／超限、批序號完整歷史；新增 REQ_F4115.27.1–3、5.27／5.23.7 介面、BR-INV-055～057 與 INV-RPT-001～014。父 017 拆 017a 真實 SQL 報表、017b 三語頁及 CSV、017c 追溯匯出效能。Release DocGen 建置 0 警告，帶 DB 產出檢查 0 Error／64 Warning，DocGen 測試 7/7；產品程式、DB 未改，既有產品測試結果沿用。Debug 建置因既有產物 ACL 拒絕寫入，Release 通過；下一單元 017a。

- 2026-10-18T05:03:47+08:00 WU-INV-013b 與父 013 完成：接手上一輪 75 個未提交變更，整理安全庫存三語亮暗 5.26 頁、唯讀與編輯選項權限、低量與補貨提示、錯誤及空狀態。按 myerp-ux-review 逐張看正常、空、無結果、驗證、409／422／500、斷線、逾時、無權限與成功截圖；修正修改時錯誤文案、聚焦與成功提示截圖殘留。專項 E2E 初次 8/10 因提示定位器錯誤，修正後 10/10；新增分頁專項初次因 CSS 定位器錯誤，改可存取名稱後 1/1；無權限亮暗 2/2。Release 後端 954/954、前端 667/667、全量模擬 E2E 199 通過／5 略過，加 3 個新專項通過；帶 DB docgen 重試後 0 Error／64 Warning。真實瀏覽器與 SQL 同資料聯查仍依賴 WU-P0-015／016、WU-INV-011c；下一獨立單元 WU-INV-017。

- 2026-10-16T20:43:13+08:00 WU-INV-013a 後端結案：接手並提交上一輪 8 個未提交檔案，MD Contracts 負責受權代碼排序／關鍵字搜尋，INV 以 MD 頁面取得門檻鍵；萬筆真實 SQL 升降冪首末頁、稀疏與同物料跨倉穩定排序、500 traceId 及資料不變已驗。代碼首頁由 42 條 SQL／約 30.8 MB 配置降至樣本 4 條／約 1.66–2.23 MB。另以紅燈確認空門檻仍讀 3 頁 MD，修正後零 MD 排序呼叫。安全庫存專項 15/15，Release 後端全量 954/954；前端及 E2E 沿用 665/665、189 通過／5 略過。013b 接續三語頁與 E2E，父 013 未結案。

- 2026-10-16T16:52:32+08:00 WU-INV-013a 新增萬筆門檻真實 SQL IO 驗收並擴及 MD 物料引用查詢：代碼排序首頁 2 筆、總數 10,000，MD 單批 ≤512；暖快取單次 1,455 ms、程序配置 30.8 MB、42 條受攔截 SQL，門檻表 197／MD 物料表 20,885 邏輯讀取。量測顯示逐批讀 MD 成本，下一續輪須設計受權伺服器端排序／搜尋契約並先寫萬筆紅燈；500 traceId 分支亦未完成。Release 建置 0 警告、後端全量 953/953；前端與 E2E 未改，沿用 665/665 與 189 通過／5 略過。013a 保持進行中。

- 2026-10-16T16:40:51+08:00 WU-INV-013a 補安全庫存查詢 SQL IO 可重跑驗收：測試攔截 EF 實際參數化門檻 SQL，於同一個 520 筆資料庫重放並讀取 `STATISTICS IO`；專項 1/1，15 條門檻命令各 3–10 次門檻表邏輯讀取，0 物理讀取。這是暖快取 520 筆且未包含 MD 引用查詢；萬筆及整體成本仍待量測，013a 保持進行中。Release 後端全量 952/952；前端及 E2E 未改，沿用 665/665 與 189 通過／5 略過。

- 2026-10-16T16:33:39+08:00 WU-INV-013a 補 INV-SAF-007／008／006 後端驗收：停用倉庫仍可讀歷史門檻、更新拒絕而刪除可行；非法倉庫 ID／排序 400；唯讀 HTTP PUT／DELETE 403，刪後列表 0 筆。真實 SQL 專項 12/12，Release 後端全量 952/952；前端及 E2E 未改，沿用 665/665 與 189 通過／5 略過。`SqlConnection.InfoMessage` 對直接命令收到中文 IO 訊息，但未收到 EF 列表查詢訊息，臨時擷取程式已移除；代表性 SQL IO、排序／關鍵字成本與 500 traceId 分支未完成，013a 保持進行中。

- 2026-10-16T16:23:20+08:00 WU-INV-013a 續補真實 SQL 分支：已刪門檻修改／再次刪除 404、同鍵恢復原 ID、跨公司純讀與倉庫／低量篩選、查詢不寫異動均通過。Release 專項 1/1、後端全量 950/950。`sqlcmd` 的 `STATISTICS IO` 可正常輸出，但目前只在空正式庫驗證擷取通道，未取得萬筆查詢 IO；成本設計及 500 等剩餘分支仍待續，013a 不結案。

- 2026-10-16T16:14:20+08:00 WU-INV-013a 續做關鍵字查詢分批：520 筆真實 SQL 測試先因單次 MD 請求 520 筆失敗，改為每批 512 後通過，跨批 21 筆匹配數正確。暫時萬筆測試量得代碼首／末頁約 1.53／0.95 秒，關鍵字約 1.08 秒，程序配置約 25～29 MB；SQL `STATISTICS IO` 訊息未取得，不作 IO 結論。Release 建置零警告、全量後端 949/949。代碼排序仍須全量逐批掃描、關鍵字仍先載入所有候選 ID；SQL IO 與 INV-SAF-001～011 後端剩餘分支待續，013a 不結案。

- 2026-10-16T15:45:06+08:00 WU-INV-013a 接手 7 個未提交變更，完成門檻代碼排序 512 筆分批、只保留目標頁候選鍵及 `(company_id,id)` INCLUDE 索引；520 筆真實 SQL 首末頁與 MD 每批上限通過，SQL 計畫使用新索引 Index Seek。新增 HTTP 400／409／422 `errorCode` 斷言，修正測試所寫的 400 常數後，Release 建置 0 警告、後端全量 949/949。新 migration 已套用 myerp，帶 DB docgen 0 Error／64 Warning；臨時隔離建置檔已清除。萬筆門檻與 keyword 路徑成本、完整後端分支仍待驗，013a 不結案；詳見 nextAction。

- 2026-10-16T11:28:30+08:00 WU-INV-013a 查詢續輪：物料名稱關鍵字及 itemCode／-itemCode／shortage／-shortage 排序補齊，先以真實 SQL 測試確認 400 紅燈，再經 MD Contracts 引用契約實作。新增雙連線唯一鍵／版本衝突及真實 HTTP 401／403／404／400／409／422、停用引用後可刪不可改、已刪引用拒絕、數值上下界、同時建立競態、兩儲位合計與相等不低量驗證。代碼排序改成先排門檻鍵、只對當頁計算結存；修正 EF 投影後篩選無法翻譯。後端全量 947/947，帶 DB docgen 0 Error／64 Warning。大量門檻代碼排序仍需記憶體載入候選鍵，索引計畫與大資料量測尚待續；013a 不結案，詳見 nextAction。

- 2026-10-16T11:09:40+08:00 WU-INV-013a 可測後端切片：先以真實 SQL 重現缺 migration 紅燈，新增 `inv_safety_stock_threshold` migration、公司永久唯一索引與軟刪稽核；建立受權 CRUD、MD guard 寫入驗證、零結存低量聚合與分頁前篩選、MD 已保存名稱顯示。修正 guard 重入交易及 EF 左聯結翻譯問題；真實 SQL 專項 2/2 驗證零結存、軟刪同 ID 恢復、版本 409、公司隔離、不寫異動。migration 已套用 myerp，後端全量 941/941，最後修改後專項 2/2；前端未改，沿用 665/665 與 E2E 189 通過／5 略過。MD 關鍵字／代碼及缺口排序、完整 HTTP 權限／錯誤、雙連線競態、停用引用與索引計畫仍待 013a 續輪；故本單元不結案，詳見 nextAction。

- 2026-10-16T10:52:23+08:00 WU-INV-013d 完成安全庫存設計切片：ADR-067 固定公司／物料／倉庫門檻、軟刪恢復、全部儲位已過帳現存量、零結存與低量判定；新增 REQ_F4115.26.1–3、預留 REQ_DS43.157、介面 5.26、BR-INV-052～054、INV-SAF-001～014 及 API 契約。DocGen 契約測試 1/1，後端單獨全量 939/939、前端 665/665、模擬 E2E 189 通過／5 略過。首次後端與前端／E2E 並行時既有估值大交易有兩項 SQL 逾時；估值類別隔離 6/6，單獨全量 939/939，詳見 optimization-log。本切片不改資料庫或產品程式，013a 先寫真實 SQL／HTTP 失敗測試並建立 migration；011c 真實啟動仍受預設埠占用阻擋。

- 2026-10-16T10:36:32+08:00 WU-INV-011c 接手上一輪未提交變更：完成組裝 400／422 三語逐行欄位及常見驗證原因、SYS 審批歷史與駁回意見；發現並修正組裝 CREATE／EDIT 未列入共用選項端點權限，真實 HTTP 同時驗證 CREATE 可讀及 VIEW 403。真實 SQL／HTTP 同單核對 Posted、耗料結存 0、成品結存 1；三語亮暗截圖複核後補意見標籤。Release 建置零警告、後端全量 938/938、前端 665/665、模擬 E2E 189 通過／5 略過。INV-ASM-013 的真實瀏覽器與資料庫同資料聯查仍依賴 WU-P0-015／016 預設啟動路徑，011c 保持進行中；詳見 test-plan 與 optimization-log。

- 2026-10-16T06:26:24+08:00 WU-P0-014 完成並行整合測試 SQL 等待診斷：採樣腳本補阻擋 session 的閒置狀態／開啟交易及 SQL 採樣失敗檢查。未隔離時 2／4／8 threads 皆 510/510，8 threads 最大鎖等待 22 秒；16 threads 因期初入庫鎖未獲授予失敗，另有約 21 秒 INSERT 等待。十萬筆估值測試的大交易與 rollback 是最可能來源；隔離該測試 collection 後，同樣 16 threads 510/510、最大等待 0.524 秒，Release 後端全量 937/937。原 35 秒 SQL 命令逾時未逐字重現，MARS=True 保留。詳見 log-analysis.md。下一輪回到 WU-INV-011c；WU-P0-015 仍 BLOCKED，016 待 015。

- 2026-10-16T06:06:58+08:00 WU-P0-014 診斷切片：新增可重跑的整合測試診斷腳本及測試行程 ThreadPool 採樣，並以 `sqlcmd` 每兩秒記錄 `dm_exec_requests` 與阻擋源至不提交的 logs。預設 MARS=True、`--max-threads 2` 整合測試 510/510 通過；單獨改 MARS=False 後 2 項稽核／409 測試因未關閉 DataReader 回 500，故未採用。Release 全量後端 937/937、建置 0 警告。原偶發 35 秒逾時未重現，根因未定，014 保持進行中；下輪用診斷入口提高重現率並對齊採樣後修正。詳見 log-analysis.md。

- 2026-10-16T05:54:22+08:00 WU-P0-017 完成 Playwright 共用守衛：獨立記錄 `/api/` 5xx，只容許以 URL／狀態明確登記的預期故障；沿用 pageerror 攔截未處理 Promise rejection；測試結束掃描當前頁面可見的未翻譯 key。新增 5 個守衛自測（其中 3 個預期失敗），調整組裝預期 500 案例，E2E 型別檢查及全量 188 通過／5 略過。測試重拍的既有 QA 截圖已還原。下一輪按 backlog 做 WU-P0-014；015 仍受埠佔用阻擋，016 依賴 015。

- 2026-10-16T05:46:55+08:00 WU-P0-015 連續第三輪因同一組既有互動程序佔用預設埠而無法完成真實啟動驗收，依第 1 節標記 BLOCKED。`Start-Dev.ps1` 再次正確拒絕 4200 node PID 7504；7180／5180 仍為 Visual Studio API PID 8100。已記錄已嘗試方法與重試條件於 blocked.md；本輪未更動產品程式或測試，下一輪改做獨立的 WU-P0-017 共用 E2E 守衛。015 的腳本、代理、README 已提交，待埠空閒後再按 README 與 016 smoke 補驗。

- 2026-10-16T05:45:05+08:00 WU-P0-015 續查既有埠：4200 是 2026-10-14 啟動的 node，7180／5180 是 2026-10-15 由 Visual Studio 啟動的 API，均未停止。啟動腳本正確拒絕 4200 佔用並列 PID；現有 4200 `/health/ready` 回 Angular HTML，直連 API 健康與驗證碼請求逾時，因此無法認定代理或真實啟動已通過。未改產品程式，既有前端 663／663、後端 Release 937／937 結果仍有效；詳見 dev-start.md。待預設埠空閒後按 README 重跑啟動，並在 WU-P0-016 建立 smoke 後依 19.5 補驗，015 保持進行中。

- 2026-10-16T05:40:35+08:00 WU-P0-015 部分進度：新增固定埠本機啟動腳本、Angular `/api`／`/health`／`/openapi` HTTPS proxy，以及 README 的一鍵／手動步驟與排錯。修正佔用埠提示不應帶 PowerShell 堆疊；已驗證既有 4200 `node` 的 PID 提示、44／44 migration、HTTPS 憑證、前端 production 建置與 663／663 測試、後端 Release 937／937 測試。現有 4200、7180／5180 程序早於本輪且未停止，真實預設啟動和代理未驗收；19.5 smoke 尚待 WU-P0-016。詳見 dev-start.md；本單元維持進行中。

- 2026-10-16T05:33:23+08:00 WU-INV-011c 接手上一輪未提交的 422 逐行錯誤測試，先確認紅燈，再讓共用錯誤解析顯示 422 欄位路徑與原因；新增逐行拒絕及逾時送審 E2E，均核對提示和草稿不變。5.9 的亮暗 422 截圖已重拍並檢視。前端全量 663/663、模擬 E2E 183 通過／5 略過，production 建置隨 E2E 通過；後端未改，沿用上輪 937/937。仍待三語可讀欄位文案、駁回理由／審批歷史、真實 SQL＋畫面結存與選項端點 403；011c 保持進行中，父 011 未結案。

- 2026-10-16T01:09:54+08:00 WU-INV-011c 邊界驗收續做，尚未結案：新增 201 筆跨頁安全 CSV 與已保存明細列印 E2E、選項取消／換倉清儲位並比對 PUT、唯讀不請求選項、斷線送審顯示繁中原因且草稿不變。專項先因測試定位器及用詞不精確失敗，修正測試後通過；沒有修改產品程式。Release 後端 937/937、前端 662/662、全量模擬 E2E 181 通過／5 略過。已重看 5.9 暗色詳情及 5.11 英文亮色截圖。下一輪同一 011c 補逾時／逐行 422／三語錯誤、駁回理由／審批歷史和真實 SQL＋畫面結存；人手啟動 smoke 仍依 WU-P0-015／016 前置處理。

- 2026-10-16T00:58:10+08:00 WU-INV-011c 續做可用切片，尚未結案：組裝列表／詳情、建立／編輯表單、受權 MD 選項、已保存停用引用、權限路由、三語亮暗、錯誤欄位與 traceId、列印入口及安全 CSV 已實作；後端摘要批次補成品代碼／名稱，SQL 確認單號／備註查詢。測試先紅後綠；後端 937/937、前端 662/662、模擬 E2E 178 通過／5 略過（組裝專項重跑 9/9）、帶 DB docgen 0 Error／64 Warning。5.9～5.11 截圖逐狀態檢查，修正欄名、失敗時舊列表與英文截圖等待。INV-ASM-009～013、015 尚有邊界案例；016 所需人手啟動路徑及 smoke 測試缺失，已列 WU-P0-015／016，E2E 共用守衛列 WU-P0-017。下一輪只續 011c，補缺口後才結案 011c／父 011。

- 2026-10-16T00:28:15+08:00 WU-INV-011c 部分進度：完成組裝前端 API 客戶端（列表日期／狀態／成品篩選、詳情、已保存引用、建立／整組修訂／送審／刪除）及型別；前端不傳成本，固定成品行 1、耗料行 2 起。先寫失敗契約測試，補實作後專項 2/2、全量前端 657/657、production 建置通過。前端頁面、三語字串、E2E、QA 截圖與 docgen 尚未做；後端 AssemblyQuery 缺單號搜尋，須於續輪補。此輪只完成可編譯切片，011c 與父 011 保持未完成。

- 2026-10-15T20:19:14+08:00 WU-INV-011b2 完成組裝送審與 SYS 最終核准過帳：耗料 Out、成品 In、Posted 同交易；按物料 ID 升序鎖定整張單，耗料用核准時移動平均計四位行值，成品六位單價重算四位行值必須守恆，未知／無法表示成本拒絕。真實 SQL／HTTP 測試覆蓋立即核准、待審駁回與再核准、缺權、缺價補價重試、耗料不足、批號不足／序號衝突、雙連線爭用、同來源重放／變更 409 及整單回滾；組裝專項 9/9。Debug 隔離輸出建置 0 警告／0 錯誤；後端整合全量 510/510、Shared 381/381、Architecture 40/40、DocGen 6/6，前端 655/655、模擬 E2E 169 通過／5 略過；帶 DB docgen 0 Error／64 Warning。標準 `obj`／`bin` 既有檔 ACL 阻擋覆寫，CR-H03 已記錄；臨時建置輔助檔已移除。011c 三語頁與 E2E 為下一單元，額外邊界按 test-plan 在模組收尾補齊。

- 2026-10-15T19:47:53+08:00 WU-INV-011b1 完成組裝估值鎖切片：原 011b 規模超出單輪，拆為 011b1 交易鎖前提與 011b2 審批原子過帳。新增 `AssemblyCostSnapshot`，要求呼叫端交易並在查估值前取得公司／物料排他鎖；所有 INV 新過帳在 MD 引用及結存檢查前按物料 ID 升序取得同鎖，來源冪等重放直接返回。真實 SQL 測試先因契約不存在而失敗，實作後驗證第二連線等待提交、未知成本與明確零成本有別。審查修正過帳服務鎖順序註解，INV-ASM-017 通過。Release 後端全量 931/931、前端 655/655、模擬 E2E 169 通過／5 略過、帶 DB docgen 0 Error／64 Warning。審批、成本尾差與原子過帳留 011b2。

- 2026-10-15T19:36:00+08:00 WU-INV-011a 接手上輪六項未提交的組裝草稿程式與測試，完成 `Inventory_AssemblyDraft` 三表 migration 並套用 myerp；組裝草稿建立／整組修訂／軟刪、列表／詳情／已保存引用、動作權限、公司隔離與批序號保存可用。真實 SQL／HTTP 測試先因缺 migration 失敗，補 migration 後通過；補合法 1000 行、閏日、1001 行、舊版本、引用失效、批序號替換與 401／403／404。檢查修正編輯狀態錯誤文字。Release 後端全量 930/930、前端 655/655、模擬 E2E 169 通過／5 略過；帶 DB docgen 0 Error／64 Warning。REQ_DS43.154–156 已正式納入，審批成本與原子過帳留 011b，前端頁留 011c。

- 2026-10-15T15:40:12+08:00 WU-INV-011d 完成組裝入庫設計切片（ADR-066）：INV 擁有可手動建立的組裝單，PP BOM 僅作未來可選建議；固定一成品、多耗料、批序號與 SYS 同交易過帳、成本守恆、公司隔離及三語介面契約。新增 REQ_F4115.4.1–6、介面 5.9–11、BR-INV-047～051、INV-ASM-001～016，預留 REQ_DS43.154–156，並把父 011 拆為 011d／a／b／c。DocGen.Tests 契約測試先紅後綠，Release 建置零警告、全量後端 926/926；帶 DB docgen 0 Error／70 Warning，其中 6 個新增追溯警告待 011a migration 補表。此輪只做設計，業務案例仍待測；前端未改，沿用上輪 655/655 及模擬 E2E 169 通過／5 略過。下一輪 011a 先寫真實 SQL／HTTP 失敗測試。

- 2026-10-15T15:26:03+08:00 WU-INV-016b2b4 完成盤點範圍多選及實地新增行物料／倉庫／儲位受權分頁選擇，移除盤點表單三類 MD 全量預載；分類 API 只在有 MD 分類查看權限時載入，缺權限及空清單均有提示。換倉清儲位、追蹤方式、公司切換競態、已存停用引用與 MD 保存拒絕均驗收；共用多選可確認清空，重新快照新增行可補名稱。三語亮暗及停用錯誤六張截圖逐張審查，修正暗色側欄白底。前端 655/655、Release 後端建置零警告及 925/925、模擬 E2E 169 通過／5 略過；帶 DB docgen 0 Error／64 Warning。INV-SEL-004～006 與父 WU-INV-016 結案；下一輪依序拆並開始 WU-INV-011 組裝入庫。

- 2026-10-15T14:30:16+08:00 WU-INV-016b2b3 完成調撥／期初表單選擇框：兩張表單移除 MD 物料／倉庫／儲位全量預載，改用 INV 受權分頁選項；調撥來源與目的倉儲、儲位獨立，換倉只清對應儲位，已存停用引用仍可辨識，期初選項確認使舊預覽失效。修正調撥 422 停用主檔錯誤被顯示為通用訊息；E2E 先紅後綠。前端 651/651、Release 後端建置零警告及 925/925、模擬 E2E 167 通過／5 略過；九張三語亮暗及拒絕截圖逐張審查；帶 DB docgen 0 Error／64 Warning。下一輪只做 016b2b4 盤點表單接入。

- 2026-10-14T20:35:36+08:00 WU-INV-016b2b2 完成其他入庫／出庫選擇框接入：兩張表單移除 MD 物料／倉庫／儲位全量預載，僅使用 INV 受權分頁選項及已存引用端點；確認才改草稿，取消／Esc 不變，換倉清儲位，追蹤方式隨物料，同物料重選保留批序號，慢回應與切換公司不能覆寫目前選值。已存停用引用帶代碼、名稱及停用標記，MD guard 保存拒絕顯示具體三語原因；長值截斷與停用儲位原因已補。專項測試先紅後綠，前端全量 648/648；Release 後端建置零警告、925/925；模擬 E2E 165 通過／5 略過；Playwright 七張亮暗／三語／錯誤截圖逐張審查；帶 DB docgen 0 Error／64 Warning，Office 鎖已解除。下一輪只做 016b2b3 調撥／期初接入，盤點留 b4。

- 2026-10-14T16:28:28+08:00 WU-INV-016b2b1 已保存引用受權顯示契約完成：五種 INV 單據新增 `/{id}/saved-references`，先按單據 VIEW 權限和公司隔離讀取已保存引用，再由 MD Contracts 回傳目前公司未刪除、含停用的最小顯示資料與 `isActive`；不接受任意主檔 ID，也不放寬新增選項。真實 SQL 先紅後綠，其他入庫 HTTP 驗證 401／403／404 與停用引用，前端客戶端測試先紅後綠。Release 建置零警告、全量後端 925/925、前端建置與全量 Vitest 638/638。檢查確認五種表單仍預載 MD 主檔，故 016b2b 拆成 b1 顯示契約、b2 其他入庫／出庫、b3 調撥／期初、b4 盤點及完整 E2E。帶 DB docgen 再次因 Office 鎖定 SRS.docx 失敗；上輪產出仍為 0 Error／61 Warning。本輪無 migration，下一輪 b2 先寫失敗 Vitest／E2E 並接入兩張表單。

- 2026-10-14T16:11:21+08:00 WU-INV-016b2a 完成物料選項追蹤模式契約：MD Contracts 僅在啟用物料選項回傳 None／Lot／Serial，倉庫／儲位為 null；INV 受權及公司隔離不變。真實 SQL 測試先紅後綠，前端選項客戶端專項 2/2；Release 建置零警告、全量後端 924/924、前端建置及全量 Vitest 637/637。檢查確認表單仍預載 MD 主檔，尚未驗收 INV-SEL-004～006 或 E2E。016b2 拆成此契約切片與 016b2b：先設計已保存停用引用的受權顯示資料，再接入其他入庫／出庫、調撥、期初、盤點；移除全量預載並跑三語暗色 E2E。帶 DB docgen 的 Word 鎖待重試。

- 2026-10-14T15:56:50+08:00 WU-INV-016b1 前端選項入口完成：016b 拆成 016b1 共用三類選項客戶端／彈窗入口與 016b2 多張單據接入。`InventorySelectionOptionsService` 只呼叫 INV 受權分頁 API，不查 MD 主檔，帶去空白搜尋；共用 `erp-select-dialog` 以三語標題與代碼／名稱顯示，儲位選擇要求倉庫 ID。專項 Vitest 先紅後綠 2/2，前端建置通過、全量 Vitest 637/637、後端 Release 924/924、模擬 E2E 161 通過／5 略過。Debug `--no-build` 因本機無執行檔未啟動，Release 全量正常。帶 DB docgen 首次 SQL 逾時，獨立重跑時 SRS.docx 仍被 Office 鎖定；上輪產出 0 Error／61 Warning 未更新。本切片無資料表或來源需求變更；016b2 須完成單據接入、追蹤模式、停用歷史顯示、保存 guard、三語暗色 E2E 並在解除 Word 檔鎖後重跑 docgen。

- 2026-10-14T15:42:28+08:00 WU-INV-016a 後端選項 API 完成：MD `IInventorySelectionOptions` 提供目前公司啟用物料／倉庫／指定啟用倉庫內儲位，僅回 id/code/name，支援去空白的代碼／名稱搜尋與代碼、ID 穩定分頁；INV 三個 GET 端點以 `RequireAnyPermission` 限現行單據 CREATE／EDIT，毋須 MD VIEW。INV-SEL-001～003 先紅後綠，真實 SQL／HTTP 專項 3/3。EF Core 不接受先投影再搜尋，已改在 MD 實體上篩選。Release 建置 0 警告，後端全量 924/924（首次與前端並行出現既有審批測試 SQL 逾時，獨立重跑全通過），前端 635/635，模擬 E2E 161 通過／5 略過。帶 DB docgen 因 Office 占用 `MyERP_SRS.docx` 無法重產，既有上輪產出仍為 0 Error／61 Warning；本輪無資料表變更。下一輪 016b 接入共用三語暗色選擇框，完成 INV-SEL-004～006，並重跑 docgen。

- 2026-10-14T15:22:22+08:00 WU-INV-016d 共用選擇介面設計完成（ADR-065）：確認 BlueWhale 5.20 Select Data Interface，定義 REQ_F4115.20.1–3、REQ_IO_A5.20.1–6、BR-INV-043～045 與 INV-SEL-001～006。原 016 依單輪上限拆為 016d 契約、016a MD Contracts／INV 只讀 API、016b 三語暗色對話框與單據接入。單據 CREATE／EDIT 權限可選資料而毋須 MD 主檔 VIEW，選項只回目前公司啟用資料；保存仍由 MD guard 驗證。DocGen.Tests Release 5/5、`git diff --check` 通過；SRS 正被 Office 鎖定，未重產帶 DB 文件，本輪無程式／DB 變更，六個功能案例仍待測。下一輪 016a 先寫 SQL／HTTP 失敗測試。

- 2026-10-14T15:13:57+08:00 WU-INV-015a INV 外部來源契約完成：`IExternalInventoryPostingService` 固定 PUR 入庫、SD 出庫與銷售空成本，要求呼叫端交易；真實 SQL 測試涵蓋重放差異 409、公司隔離、回滾、批序號及雙連線爭用。Release 建置 0 警告、後端全量 921/921。新公布的 Pkcs 漏洞阻擋還原，已釘修補版 9.0.21；11:17 已產生的帶 DB 文件為 0 Error／61 Warning，本輪重產時因 Word 正開啟 `MyERP_SRS.docx` 被檔案鎖拒絕，輸出內容未需更新。Office 暫存檔加入忽略規則。真實 PUR／SD + SYS／HTTP 驗收留 015b，須待來源單據建立；下一輪 WU-INV-016。

- 2026-10-14T11:08:38+08:00 WU-INV-015d 跨模組過帳邊界設計完成（ADR-064）：PUR `PURCHASE_INBOUND` 只入庫、SD `SALES_OUTBOUND` 只出庫；來源模組在 SYS 最終核准交易中以已保存明細呼叫 INV，INV 維持唯一寫入、公司隔離與來源重放防護。新增 REQ_F4115.22.4–5、BR-INV-040～042、INV-XMOD-001～006；父 015 拆為 015d 設計、015a INV 真實 SQL 契約、015b 真實 PUR／SD + SYS HTTP 整合。首次 docgen 發現既有 5.22 子功能重複宣告，修正後帶 DB docgen 0 Error／61 Warning；DocGen.Tests Release 5/5。Debug 測試建置受既有 `MyERP.Shared/obj/Debug` 權限阻擋，改用 Release 通過。此切片未改程式／DB，不把待測案例標為通過；下一輪 015a。

- 2026-10-14T11:02:36+08:00 WU-INV-014b 受權估值 API 與三語暗色頁完成，父 WU-INV-014 結案：`GET /api/v1/inv/valuations/items/{itemId}` 由 `INV.VALUATION.VIEW` 保護，MD 唯讀契約確認目前公司物料，非法日期 400、他公司物料 404；同權限的分頁物料選項支援關鍵字與排序，僅持估值權限也可選物料。頁面顯示 UTC+8 過帳截止、六位數量／單價、四位金額及暫估聲明，`Unpriced` 金額留空並提示未定價數量，明確零價仍顯示 0.0000。檢查修正切換公司後舊請求覆寫畫面的競態。HTTP 專項 1/1、前端 Vitest 2/2、模擬 E2E 專項 4/4；Release 建置 0 警告、全量後端 917/917、前端 635/635、模擬 E2E 161 通過／5 略過、帶 DB docgen 0 Error／59 Warning。首次帶 DB docgen 與全量前端／E2E 同時執行逾時，E2E 完成後獨立重跑通過；全量前端首次發現新 i18n key 大寫，已修正並重跑通過；後端建置曾與整合測試重疊遇到 DLL 檔案鎖，測試結束後重建 0 警告。下一輪 WU-INV-015 完成 PUR／SD 過帳契約與跨模組整合測試。

- 2026-10-14T10:29:21+08:00 WU-INV-014a 庫存估值 SQL／內部契約完成：接手上一輪未提交的 9 項變更並驗收。新增唯讀 `usp_inv_value_item`、`(company_id,item_id,posted_at,id)` 覆蓋索引、`Inventory_Valuation` migration 及 `IInventoryValuationQuery`；已套用本機 myerp。INV-VAL-001～005 真實 SQL 案例 6/6，含移動加權平均、調撥中性、未定價、截止、負池、溢位及公司隔離。100,000 筆計畫有 Index Seek，4,316 ms／CPU 2,295 ms／106,274 logical reads。檢查時以 `HOLDLOCK` 修正 SP 改動呼叫端隔離級別；Release 建置 0 警告、全量後端 916/916、前端 633/633、模擬 E2E 157 通過／5 略過、帶 DB docgen 0 Error／59 Warning。初次並行 docgen 與整合測試導致 SQL 逾時，分開重跑後全量測試通過；Debug 建置受舊有 `bin/obj` 產物的檔案權限阻擋，Release 建置與所有回歸已通過。下一輪 014b 完成受權 API、三語暗色頁與模擬 E2E。

- 2026-10-14T05:39:02+08:00 WU-INV-014d 庫存估值設計完成（ADR-063）：父單元拆成設計、SP／內部契約、API／前端三段。固定公司＋物料移動加權平均、按過帳時間／ID 順序、行四位取整與清池尾差；調撥配對作價值中性事件，NULL 成本無已知基礎時標 `Unpriced` 並回空金額；FI 須另行封存後才可過帳。新增 REQ_F4115.25.1–3、介面 5.25、BR-INV-036～039、INV-VAL-001～006 與資料／API 規格；檢查修正異動帳需求 ID 與測試表頭。DocGen.Tests 5/5，帶 DB docgen 0 Error／59 Warning，`git diff --check` 通過。本設計單元不改程式或 DB；下一輪 WU-INV-014a 先寫真實 SQL 失敗測試，再完成 SP、migration、索引、內部契約及效能驗證。

- 2026-10-14T05:33:20+08:00 WU-INV-019 介面編號修正完成：期初 View／Create 由 5.8／5.9 改編為 5.21／5.22，批序號追溯由 5.10 改為 5.23；21 個介面／欄位 ID 與 Related I/O、需求說明及 BlueWhale 對照同步更新，5.18／5.24 盤點介面保留。DocGen.Tests 增加未完成 BlueWhale 保留號、5.9／5.10 互換、重號及三份文件錯序檢查（先紅後綠）；完成的介面可使用 MyERP 名稱。後端建置 0 警告、全量 910/910，前端建置與 633/633，模擬 E2E 157 通過／5 略過，帶 DB docgen 0 Error／59 Warning；首次並行 E2E 時 docgen SQL 逾時，E2E 結束後獨立重跑通過。檢查與修正見 INV optimization-log。下一輪按 P1 優先級執行 WU-INV-014 庫存估值 SP。

- 2026-10-14T05:19:05+08:00 WU-INV-010b2 盤點前端（二）完成，WU-INV-010b／010 結案：正式列表支援單號／備註、日期及狀態篩選與伺服器分頁；詳情及已保存盈虧報告顯示 MD 名稱、審批狀態、逐行與逐批序號差異、全單盈虧合計及只列差異。草稿送審／確認刪除帶版本，列印報告，按目前篩選逐頁取完的 UTF-8 安全 CSV；三語、暗色及唯讀權限。測試先行新增 variance API Vitest（先紅後綠），補批序號、重新快照、跨頁匯出及權限模擬 E2E。前端建置、Vitest 633/633、模擬 E2E 157 通過／5 略過、後端 907/907、帶 DB docgen 0 Error／59 Warning；首次 docgen 與整合測試同時執行逾時，待整合測試結束後單獨重跑通過。檢查與優化見 INV optimization-log；下一輪優先 WU-INV-019 介面編號修正。

- 2026-10-14T05:07:26+08:00 WU-INV-010b1 盤點前端（一）完成：`/inv/stocktakes/new` 範圍多選建立快照，`/:id/count` 實盤表格（零數量、六位小數差異、批序號、實地新增行及移除、未盤填帳面、保存與版本、重新快照前保存、送審、`SNAPSHOT_STALE` 異動鍵顯示），三語與暗色 token；`/inv/stocktakes` 暫行草稿入口及選單／權限。依後端實際碼補三語錯誤，ui-spec 對齊多選檢查表與備註位置。測試先行的 Vitest 3 個；全量前端 632/632、模擬 E2E 154 通過／5 略過（盤點 4 個）、全量後端 907/907、帶 DB docgen 0 Error／59 Warning。檢查與優化記於 INV optimization-log；下一輪 WU-INV-010b2 完成正式列表／詳情／差異報告與匯出，補批序號與權限 E2E，結案 010b／010。

- 2026-10-14T04:37:55+08:00 WU-INV-010a2 盤點後端（二）完成，WU-INV-010a 結案。
  - `POST /{id}/submit`（`INV.STOCKTAKE.SUBMIT`）：在盤點鎖內檢查草稿＋版本、全部行與批序號已盤（`COUNT_MISSING`），重算範圍（含新增行的鍵）現存量與批序號結存並檢查快照後異動，不符回 `SNAPSHOT_STALE` 並以 ProblemDetails `changedKeys` 列出最多 50 個鍵；盈虧異動超過過帳服務上限回 `TOO_MANY_LINES`；之後送 SYS（金額 0），無步驟時同交易過帳。
  - `StocktakeApprovalResultHandler`：核准時逐行按批序號拆正差異 In／負差異 Out（同行號、來源 STOCKTAKE、成本空），無差異不呼叫過帳仍 Posted；不足或序號衝突全部回滾；駁回轉 Rejected。
  - `GET /{id}/variance`：Gain／Loss／Match／Uncounted、按號碼的盤盈／盤虧量、合計與 `differenceOnly`。
  - 共用：`ErpException.Extensions` 經 ProblemDetails 輸出（不覆蓋 `errorCode`／`errors`）。
  - 驗證：新增 StocktakeTests 5 個、HTTP 真實 SYS 核准 1 個、Shared 單元 1 個；全量後端 907 / 907（`--max-threads 2`）；帶 DB docgen 0 Error／59 Warning。
- 2026-10-14T04:12:19+08:00 WU-INV-010a1 盤點後端（一）完成：接手上一輪 codex 額度中斷、未提交的 14 個變更並驗收，WU-INV-010a 依 nextAction 拆為 010a1／010a2。
  - 資料表：`inv_stocktake`／`inv_stocktake_line`／`inv_stocktake_line_tracking`（REQ_DS43.151–153），migration `Inventory_Stocktake` 已套用 myerp。
  - MD 契約：新增唯讀 `IItemCategoryScope`（類別含下層、與指定物料取交集、只取啟用物料）；`IInventoryReferenceGuard.RequireWarehousesAsync` 批次驗證倉庫並持共享鎖。
  - API `/api/v1/inv/stocktakes`：列表、詳情、建立（同交易取 `INV.STOCKTAKE` 單號並快照，只讀異動帳，保存快照時間與最大異動 ID，5,000 行／20,000 批序號上限）、PUT 整組保存實盤與實地新增行、重新快照（保留實盤、標記重盤）、軟刪除；`INV.STOCKTAKE.VIEW/CREATE/EDIT/DELETE`。公司級 `inv_stocktake` 鎖內檢查「物料＋倉庫」互斥。
  - 修正：測試輔助方法觸發 CA1859 使建置失敗；CHECK 測試被唯一索引先擋；舊版本斷言用到被追蹤實體就地遞增的版本。
  - 驗證：StocktakeTests 7/7、盤點 HTTP 1/1；建置 0 警告；全量後端 900/900（`--max-threads 2`）；帶 DB docgen 0 Error／59 Warning。
  - 下一輪：WU-INV-010a2，即送審、SYS 審批盈虧過帳與差異報告。

- 2026-10-14T01:02:48+08:00 WU-INV-010d 盤點設計完成（ADR-062）。父單元拆為 010d 設計、010a 後端、010b 三語前端。
  - 參考資料：從 BlueWhale SRS 取出介面 4.4.1.5.18 的七個欄位（多選倉庫、類別、物料；查詢；盤點數量；備註；產生盈虧單）。舊系統 `Pandian` 以瀏覽器送回的帳面數量，產生兩張未核准、單價 0 的其他入出庫單，而且沒有寫入盤點歷史；MyERP 不沿用。
  - 需求：REQ_F4115.7.1–7（7.1 更正為 create stocktake，7.3–7.7 依 ADR-002 追加）。
  - 資料表：`inv_stocktake` 三張預定表（REQ_DS43.151–153）與 ER 圖；另有八個 API 契約。
  - 介面：5.18 範圍查詢與實盤（11 個 REQ_IO_A），新增 5.24 列表與盈虧報表（5 個）。另有 BR-INV-031～035、INV-STK-001～011。
  - 快照後異動規則：送審時重算範圍內現存量（含批序號），並檢查快照後的異動 ID；任一不符就拒絕送審（`SNAPSHOT_STALE`），由使用者重新快照（保留實盤、標記重盤），不自動改差異。送審後的異動與核准的差異疊加；最終核准時，盤虧不足就全部回滾。盤盈、盤虧逐批序號拆成同一行號的一入一出。未結單之間，「物料＋倉庫」互斥。
  - 檢查時發現：擴充介面 5.8–5.10 與 BlueWhale 編號衝突，新增 backlog WU-INV-019，預留 5.21–5.23 重新編號。
  - 驗證：無 DB docgen 0 Error／60 Warning；保留上輪帶 DB 的產出檔。本輪未改程式，也未套用 migration。
  - 下一輪：010a 先寫 SQL／HTTP 失敗測試，再實作。

- 2026-10-14T00:54:22+08:00 WU-INV-009b 庫存調撥三語前端完成，父 WU-INV-009 結案：新增 `/inv/transfers` 列表（單號／備註、日期區間、狀態篩選，伺服器分頁）、`/new` 與 `/:id/edit`（介面 5.15–5.17），庫存選單「庫存調撥」及 `INV.TRANSFER.*` 按鈕權限。每行選物料、調出倉庫／儲位、調入倉庫／儲位（各依所選倉庫載入）與六位小數數量；來源與目的結存鍵相同時即時提示並阻擋儲存；批號多列／序號多行沿用 `erp-tracking-input`，草稿可暫缺，已輸入者須與數量一致。詳情、列印顯示兩側 MD 名稱與批序號，只為有儲位的倉庫查儲位名稱；篩選後逐頁取完的 UTF-8 CSV 以 `csvCell` 中和公式。駁回單可修訂（帶版本）後回草稿再送審；三語鍵與 `INV.TRANSFER.*` 錯誤碼、暗色。順帶修正其他入庫／出庫表單 OnPush 下詳情晚於 MD 選項回應時不重繪的競態（全量 E2E 中 INV-REC-006 偶發失敗，已以單元測試重現）。Vitest 629/629（新增 11）、模擬 E2E 150 通過／5 略過（新增 INV-TRF-009 五個，三張單據 E2E 連跑 3 次穩定）、後端 892/892、建置 0 警告、帶 DB docgen 0 Error／59 Warning。未改啟動方式／proxy，19.5 真實 API 冒煙留待 INV 模組結案前執行。下一輪 WU-INV-010 盤點。

- 2026-10-14T00:31:30+08:00 WU-INV-009a 庫存調撥後端完成（接手上一輪 codex 額度中斷未提交的 19 個變更並驗收）：新增 `inv_transfer`／`inv_transfer_line`／`inv_transfer_line_tracking`（REQ_DS43.148–150，migration `Inventory_Transfer` 已套用 myerp），`/api/v1/inv/transfers` 草稿 CRUD、送審、軟刪除（`INV.TRANSFER.*`），SYS 最終核准在同交易以相同來源產生等量 Out／In 異動並搬遷批序號，駁回無異動。檢查修正：來源／目的 NULL 儲位以 `ISNULL` 比較避免 CHECK 放行、兩側結存鍵合併去重後驗證 MD、回滾後清除 EF 追蹤。INV-TRF-001～008 通過（`TransferTests` 12 個 SQL 案例 + HTTP 權限／公司隔離 1 個）；建置 0 警告、全量後端 892/892（`--max-threads 2`）、帶 DB docgen 0 Error／59 Warning。下一輪 WU-INV-009b 三語前端。

- 2026-10-13T23:56:40+08:00 WU-INV-009d 庫存調撥設計完成（ADR-061）：父單元拆為 009d 設計、009a 後端、009b 三語前端。新增 REQ_F4115.6.1–6、`inv_transfer` 三張預定表 REQ_DS43.148–150 與 ER 圖、六個 API 契約、介面 5.15–17（15 個 REQ_IO_A）、BR-INV-026～030、INV-TRF-001～009 驗收案例。確定每行來源與目的結存鍵不同，SYS 最終核准同交易以一份追蹤清單展開等量 Out／In，成本留待估值單元。設計審查記錄於 optimization-log。DocGen.Tests 2/2；無 DB docgen 0 Error／60 Warning。帶 DB docgen 如預期回三個 `TABLE_NOT_IN_DATABASE`（009a migration 後消除），因此保留上輪已通過的帶 DB 產出檔，未提交本輪的臨時產物。本輪未改程式或套用 migration。下一輪 009a 先寫 SQL／HTTP 失敗測試，再做單據與雙向過帳。

- 2026-10-13 23:48 WU-INV-008c 批號／序號前端完成，父 WU-INV-008 結案：其他入庫／出庫與期初按 MD `trackingMode` 顯示批號多列或序號多行貼上；六位定點數量合計、批號日期、序號大小寫重複與跨行重複在送單前檢查，`tracking[]` 數量以字串傳送。三種單據詳情／列印及逐行 CSV 顯示批序號，CSV 每批最多 4 個並行詳情請求。新增 `/inv/tracking` lazy 路由及庫存選單（`INV.TRACKING.VIEW`）、批／序號分頁、物料／關鍵字／只列在庫篩選、伺服器分頁、位置與異動歷史截斷提示。介面 REQ_IO_A5.2.8、5.5.8、5.9.6、5.10.1–6 消除六個 TRACE_GAP；三語鍵與八個錯誤碼。Vitest 618/618、模擬 E2E 145 通過／5 略過（新增 INV-LOT-UI-001～003）、全量後端 880/880、後端建置 0 警告、帶 DB docgen 0 Error／59 Warning。下一輪 WU-INV-009 調撥。

- 2026-10-13 23:30 WU-INV-008b 批號／序號後端完成：新增 `inv_lot`、`inv_serial`、`inv_stock_movement_tracking`（只增不改 trigger）與單據子表 `inv_other_receipt_line_tracking`／`inv_other_issue_line_tracking`／`inv_opening_stock_line_tracking`（REQ_DS43.142–147，migration `Inventory_LotSerial` 已套用 myerp）。過帳契約 `InventoryPostingLine.Tracking`（`InventoryTrackingLine` 批號或序號擇一）；過帳服務在 MD 共享鎖內取得物料追蹤方式，None 帶批序號／種類不符 400、缺少或合計不符 422 `INV.TRACKING.REQUIRED`；批號首次入庫建立、日期不同 422 `INV.LOT.ATTRIBUTE_MISMATCH`；出庫按結存鍵核對 `INV.LOT.INSUFFICIENT`／`INV.SERIAL.NOT_AVAILABLE`；序號全公司在庫再入庫 409 `INV.SERIAL.IN_STOCK`；入庫物料取 `inv_tracking` 排他鎖序列化批號建立與序號檢查；重放快照含批序號。其他入庫／出庫草稿可暫缺、送審檢查完整，期初預覽逐行回報；API 明細帶 `tracking[]`。新增追溯 `GET /api/v1/inv/tracking/lots|serials[/{id}]`（`INV.TRACKING.VIEW`）。REQ_F4115.24.1–6、INV-LOT-001～015（含雙連線並發、HTTP 權限）。全量後端 880/880（`--max-threads 2`）、建置 0 警告、帶 DB docgen 0 Error／65 Warning（新增 6 個為 REQ_F4115.24.x 尚無介面，008c 補）。下一輪 WU-INV-008c。

- 2026-10-13 20:37 WU-INV-008a 物料批號／序號追蹤方式完成（ADR-060 將 008 拆為 008a MD 追蹤設定／008b INV 後端／008c INV 前端）：`md_item.tracking_mode`（`None`／`Lot`／`Serial`，預設 None，CHECK 約束；migration `MasterData_ItemTrackingMode` 已套用 myerp）。物料 API 建立可選、修改省略為不變，未定義值 400 `trackingMode`；任何 `IItemUsage` 回報已使用時改變回 409 `MD.ITEM.TRACKING_MODE_LOCKED`，其他欄位仍可改。改變時持有 `md_item` 排他鎖到提交，與 INV 寫入端共享鎖互斥；`InventoryReferenceCodes` 新增 `TrackingMode`，INV 經 MD 契約取得、不讀 MD 表。Domain 比照科目類型存名稱字串（架構測試要求 Domain 不依賴 Contracts）。物料頁新增三語「追蹤方式」欄與下拉及不可再改提示。REQ_F4118.3.7、REQ_IO_A8.12.7、REQ_DS43.113 v1.2；MD-ITEM-TRK-001～006。全量後端 853/853（`--max-threads 2`）、前端 613/613、物料 E2E 10/10、帶 DB docgen 0 Error／59 Warning。下一輪 WU-INV-008b。

- 2026-10-13 20:16 WU-INV-007b 物料期初庫存前端完成，父 WU-INV-007 結案：三語 lazy 路由 `/inv/opening-stocks`（介面 5.8，`INV.OPENING_STOCK.VIEW`，庫存選單「期初庫存」）與 `/new`（介面 5.9，`INV.OPENING_STOCK.CREATE`）。列表支援啟用日期範圍（先檢查起訖）、物料、倉庫、關鍵字與伺服器分頁；詳情先補齊儲位名稱再顯示，可列印；CSV 依篩選逐頁取得全部批次並防公式注入。批次輸入只列啟用的 MD 物料／倉庫／儲位，先預覽並逐行顯示三語錯誤（新增 `MD.*.INACTIVE` 與 `INV.OPENING_STOCK.*` 訊息），只有零錯誤且內容未改的預覽才可提交；每批以 `crypto.randomUUID()` 產生 requestKey，網絡失敗重試沿用同一鍵（200 replayed 顯示「未重複過帳」），409 換新鍵並要求重新預覽，422 自動重新預覽。測試 fixture 補登記 `inv` 語言範圍，三語 key 一致性改為涵蓋 INV。ui-spec 新增 REQ_IO_A5.8.1–6、5.9.1–5，消除 4 個 TRACE_GAP；reference-notes 4.4.1.8.14 改完成。前端 612/612（新增 15）、E2E 142 通過／5 略過（新增 INV-OPN-007 共 4 個）、後端 849/849、建置 0 警告、帶 DB docgen 0 Error／59 Warning。

- 2026-10-13 20:00 WU-INV-007a 物料期初庫存後端完成（ADR-059 拆分 007a／007b）：新增 `inv_opening_stock`／`inv_opening_stock_line`（REQ_DS43.140–141，`Inventory_OpeningStock` 已套用 myerp）與 `/api/v1/inv/opening-stocks`（GET 列表／詳情、POST `preview`、POST 提交；`INV.OPENING_STOCK.VIEW/CREATE`）。預覽逐行回報 MD 不存在／停用、儲位不屬倉庫、批次內重複、已有期初、啟用日期晚於既有異動，不寫資料；提交在公司級排他鎖內重新驗證，任一行有錯 422，成功則同交易以 `OPENING_STOCK` 入庫過帳。`requestKey` 冪等：相同內容重送回原批次，不同內容 409；每個物料＋倉庫＋儲位一筆期初，資料庫不帶篩選唯一索引為最後防線（含 NULL 儲位）。檢查修正：MD 驗證依結存鍵去重、金額逐行累計防溢位、請求鍵正則改 `\z`、物料＋倉庫篩選須同一明細。INV-OPN-001～006 通過（22 個 SQL 案例 + 1 個 HTTP 流程）；建置 0 警告、全量後端 848/848（`--max-threads 2`，另補 1 個資料列後專項 22/22），帶 DB docgen 0 Error／63 Warning（新增 4 個為 REQ_F4115.23.x 尚無介面，007b 補）。下一輪 WU-INV-007b。

- 2026-10-13 19:45 WU-INV-006b 庫存查詢前端完成，父 WU-INV-006 結案：接手上一輪因額度中斷未提交的 13 個變更並補完。`/inv/stock-query`（介面 5.1，REQ_IO_A5.1.1–11）三語 lazy 路由與選單，`INV.INVENTORY.VIEW`／`INV.MOVEMENT.VIEW` 任一可進入、結存／異動分頁各自授權；經 MD API 顯示物料／倉庫／儲位名稱，異動頁支援日期範圍（先檢查起訖）與方向篩選。列印與 UTF-8 CSV 依目前篩選逐頁取得全部資料；審查修正 CSV 對負數數量誤加 `'` 前綴，只對文字防公式注入。INV-QRY-004 Vitest 2、Playwright 6 通過；前端建置 0 警告、前端 597/597、模擬 E2E 全量 138 通過／5 略過、後端建置 0 警告、全量 826/826（`--max-threads 2`）、帶 DB docgen 0 Error／59 Warning。下一輪 WU-INV-007 物料期初庫存。

- 2026-10-13 19:29 WU-INV-006a 庫存查詢後端完成（ADR-058 拆分 006a／006b）：新增 `/api/v1/inv/balances` 與 `/movements`，分別以 `INV.INVENTORY.VIEW`、`INV.MOVEMENT.VIEW` 保護。SQL 端以目前公司過濾異動帳，結存按物料／倉庫／儲位彙總非零數量，明細按業務日期及 ID 穩定排序；支援物料、倉庫、儲位、日期及方向篩選與分頁，不建彙總表、不跨模組查 MD。INV-QRY-001～003 真實 SQL／HTTP 通過；檢查修正 EF 分組投影轉譯問題。Release 建置 0 警告、後端 826/826、前端 595/595、E2E 132 通過／5 略過，帶 DB docgen 0 Error／61 Warning。下一輪 WU-INV-006b 補三語頁、MD 名稱、列印及 CSV 匯出。

- 2026-10-13 19:12 WU-INV-005b 其他出庫前端完成：三語 lazy 路由、庫存選單、介面 5.5–5.7、VIEW／CREATE／EDIT／DELETE／SUBMIT 權限、MD 物料／倉庫／儲位名稱、列印及防公式注入 CSV 匯出。單價與金額明確標示為估算；庫存不足顯示本地化錯誤，草稿仍可編輯與重送。INV-ISS-004 的 Vitest 2、模擬 Playwright 5 通過；Release 建置 0 警告、全量後端 823/823、前端 595/595、E2E 全量 132 通過／5 略過（2 worker）、帶 DB docgen 0 Error／59 Warning。審查修正出庫頁誤用入庫權限碼、三語 JSON BOM／語法及全量負載下既有 E2E 的等待條件。下一輪 WU-INV-006 庫存查詢。

- 2026-10-13 18:47 WU-INV-005a 其他出庫後端完成（ADR-057 拆分 005a／005b）。新增 `inv_other_issue`、`inv_other_issue_line`（REQ_DS43.138–139），migration 已套用 `myerp`；同公司單號、草稿 CRUD、版本、軟刪除、列表／詳情及權限 API，SYS 最終核准在同一交易出庫。出庫單價只作審批金額估算，異動帳成本留空待 WU-INV-014 估值。INV-ISS-001～003、005 真實 SQL 測試涵蓋庫存不足回滾、核准一次性扣庫存、雙連線防超賣及千行估算溢位；Release 全量後端 823/823、前端 593/593、模擬 E2E 127 通過／5 略過，帶 DB docgen 0 Error／59 Warning。下一輪 WU-INV-005b 實作三語介面、列印／匯出與 E2E。

- 2026-10-13 18:25 WU-INV-004b 其他入庫前端完成：三語 lazy 路由、庫存選單與 VIEW／CREATE／EDIT 守衛；列表按單號／日期／狀態篩選、分頁、狀態標籤、詳情、送審與草稿刪除；建立及駁回修訂表單從 MD API 分頁載入物料／倉庫／儲位，數量與單價保留六位十進位文字，後端本端點允許 decimal JSON 字串避免 JavaScript 精度損失。瀏覽器列印等候 MD 名稱載入，匯出依篩選逐頁取得供 Excel 開啟的 UTF-8 CSV（防公式注入）；原生 `.xlsx` 留 WU-INV-017 評估（ADR-056）。INV-REC-006 的 Vitest 2、Playwright 4 通過。Release 建置 0 警告；全量後端 819/819，decimal 字串 HTTP 專項 1/1；前端 593/593，變更後專項 24/24；E2E 全量 125 通過／5 略過，再新增 2 專項通過；帶 DB docgen 0 Error／59 Warning。下一輪 WU-INV-005。

- 2026-10-13 18:05 WU-INV-004a 其他入庫後端完成。WU-INV-004 超過單輪上限，拆為 004a（後端）與 004b（前端、列印、匯出、E2E），決策見 ADR-055。
  - 資料表：migration `Inventory_OtherReceipt` 建立 `inv_other_receipt`、`inv_other_receipt_line`（REQ_DS43.136–137），已套用 `myerp`。CHECK 保護狀態、過帳時間與軟刪除組合；明細以同公司複合 FK 指向表頭；明細金額取 4 位小數保存。
  - 服務與 API：`/api/v1/inv/other-receipts` 提供列表（單號／備註、日期、狀態篩選，排序）、詳情（含 SYS 審批狀態）、建立、整組替換編輯、軟刪除、送審，權限為 `INV.OTHER_RECEIPT.VIEW/CREATE/EDIT/DELETE/SUBMIT`。建立時在同一交易取 `INV.OTHER_RECEIPT` 單號。草稿依不同的物料／倉庫／儲位組合經 MD guard 驗證，不寫異動帳。
  - 審批：送審呼叫 SYS 工作流，立即核准時同一交易過帳。待審單據由 `OtherReceiptApprovalResultHandler` 在 SYS 審批交易內過帳（`OTHER_RECEIPT` 入庫異動）或標為駁回。駁回單據須先修訂才可再送審。過帳失敗時 SYS 決定、單據狀態與異動一起回滾。
  - 測試：先寫的 SQL 整合與 HTTP API 測試共 24 個（INV-REC-001～005），涵蓋經真實 SYS 審批 API 核准後入庫、雙連線編輯／送審只一方成功、停用物料回滾、缺單號規則 422、公司隔離與 403。
  - 驗證：Release 建置 0 警告、全量後端 819/819；帶 DB docgen 0 Error／59 Warning。Debug 輸出被本機 Visual Studio 偵錯中的 MyERP.Api 鎖住，所以本輪改用 Release 建置與測試，沒有結束使用者的程序。
  - 下一輪：WU-INV-004b。

- 2026-10-13 17:42 WU-INV-004 設計切片：定義其他入庫六項需求與驗收、`inv_other_receipt`／`inv_other_receipt_line` 預定結構（REQ_DS43.136–137）、API／權限、三個介面及 14 個欄位 ID、業務規則、ADR-054、INV-REC-001～006 待執行案例與設計審查。SYS 最終核准與 INV 過帳必須在同一交易。Debug 建置 0 警告、後端全量 795/795；無 DB docgen 0 Error／60 Warning。程式、migration、前端與 E2E 未開始，WU-INV-004 維持 IN_PROGRESS；下一輪依 `state.json.nextAction` 測試先行續作。

- 2026-10-13 17:33 WU-INV-003 完成：接手 10 個未提交的異動帳草稿，補齊 REQ_F4115.22.1–3、REQ_DS43.135、ADR-053、API／業務規則／追溯及優化記錄。`inv_stock_movement` 只追加並以 SQL trigger 禁止 UPDATE／DELETE；過帳在來源與結存鍵鎖內防重放、檢查負庫存，同交易寫引用與異動；結存即時由異動帳推導。補 `decimal.MinValue` 驗證邊界。Debug 建置 0 警告、全量後端 795/795，migration 已套用 `myerp`，帶 DB docgen 0 Error／59 Warning。下一輪 WU-INV-004 其他入庫。

- 2026-10-13 17:15 WU-INV-002 完成：全量失敗的 SYS IP 白名單及種子資料案例共用同一 SQL 測試庫，前者留下啟用的全系統規則，後者留下改寫的 `production` 密碼雜湊。保留全部原斷言，在各案例 `finally` 停用規則、恢復原雜湊並移除新增授權。Release 建置 0 警告，全量後端 773/773 通過。INV-REF-001～003、migration 與帶 DB docgen 已於前段驗證；審查確認引用寫入經 MD guard、Usage 只查 INV 快照。下一輪 WU-INV-003 建立庫存異動帳與過帳服務。

- 2026-10-13 17:08 WU-INV-002 第二段已整理提交：新增 `inv_reference`（REQ_DS43.134，ADR-052）與 migration，保留每個來源鍵的物料／倉庫／儲位 ID 和不可變代碼快照；INV `InventoryReferenceService` 在 MD guard 鎖下驗證、保存並實作／註冊三種 Usage。INV-REF-003 用真實 SQL 雙連線重疊交易驗證寫入先提交後刪除回 409、刪除先提交後寫入回 404，專項 2/2。Release 建置 0 警告；migration 已套用 `myerp`；帶 DB docgen 0 Error／56 Warning。全量後端 773 案例中 771 通過，兩個既有 SYS 案例（IP 白名單初始狀態、Seed 密碼被另一案例改寫）在全量失敗，但各自單獨重跑通過；嘗試以組件逐案執行仍失敗，已撤回該無效設定。WU-INV-002 不標 DONE，下一輪修復測試資料隔離並重跑全量。

- 2026-10-13 WU-INV-002 引用安全第一段：新增 MD `IInventoryReferenceGuard`，以公司級 `md_item`、`md_warehouse` 共享鎖包住物料／倉庫／儲位驗證及 INV 寫入 action；儲位刪除改在 `md_warehouse` 排他鎖內檢查 Usage 並保存。真實 SQL 整合測試 INV-REF-001／002 證明交易提交前鎖未釋放、提交後可取得，並驗證缺少引用回 404。後端建置 0 警告、全量 771/771。INV 尚無持久化交易表，`IItemUsage`／`IWarehouseUsage`／`IStorageLocationUsage` 的 INV 實作與寫入／刪除並發測試 INV-REF-003 留在本工作單元下一段；本段不宣稱引用安全已完成。

- 2026-10-13 WU-INV-001 庫存基線完成：新增 INV／Contracts 兩專案並納入組合根、權限目錄 `INV.INVENTORY.VIEW`／`INV.MOVEMENT.VIEW`、同公司單據狀態守衛；ADR-051 決定不可改寫的異動帳、`decimal(19,6)` 基本單位數量、預設禁止負庫存、儲位條件、SYS 核准與 INV 過帳分段、已過帳只以反向單據沖回，估值細節留 WU-INV-014。新增 INV 需求、設計與測試文件；測試先行，完整後端 769/769、建置 0 警告、帶 DB docgen 0 Error／55 Warning。既有種子角色測試的 SALES 固定權限清單按新增 INV 查看權限同步更新；首張表的 SQL 公司隔離由 WU-INV-003 驗證。下一輪 WU-INV-002 實作引用安全與跨模組鎖。

- 2026-10-12 WU-MD-004a 客戶聯絡人與地址後端完成（WU-MD-004 第一段，已於 004b 完成，見「最近完成」）：用 subagent 查 BlueWhale 對照與舊系統規則：BlueWhale 沒有聯絡人 REQ_F，只有介面 8.4 與 `ClientContact`／`ClientAddress` 表；舊系統聯絡人設首要時清除其他首要（最多一位），地址是另一張表，也只有一筆預設；兩者都是逐筆維護、實體刪除。據此寫 ADR-043，把 WU-MD-004 拆為 004a／004b。新增 `md_customer_contact`（REQ_DS43.128）與 `md_customer_address`（REQ_DS43.129），migration `MasterData_CustomerContact` 已套用 `myerp`；`md_customer` 加 `(company_id, id)` 唯一約束，子表以複合外鍵參照。聯絡人有姓名（必填）、職稱、手機、電話、電郵（單一合法地址）、通訊軟體帳號、備註；地址有地址（必填）、郵遞區號、收件人、電話。每位客戶最多一位預設聯絡人、一筆預設地址：設為預設時在 applock 交易內先取消原預設再保存，資料庫以篩選唯一索引與 CHECK（預設必為啟用且未刪除）把關。API `/api/v1/md/customers/{customerId}/contacts|addresses`，查看用 `MD.CUSTOMER.VIEW`、維護用 `MD.CUSTOMER.EDIT`。共用 `PartnerChild`／`PartnerContact`／`PartnerChildService`，供 WU-MD-008 供應商聯絡人沿用。需求 REQ_F4118.41.1–4、42.1–4；整合測試 MD-CONT-001～004（4 個案例）通過。後端建置 0 警告；單線程全量 713 個測試中 12 個無關的 SYS／UoM 測試因 SQL 逾時失敗（已知主機記憶體問題），重跑這 5 個測試類別 27/27 通過。下一段 WU-MD-004b 做客戶對話框子表、三語、E2E 與 docgen。

- 2026-10-12 WU-MD-021a 工序分類與工序後端完成（WU-MD-021 第一段，已於 021b 完成，見「最近完成」）：用 subagent 查 BlueWhale 對照與舊系統規則：舊系統的工序類別只有名稱與排序號，是單層清單；工序有類別、計件工價、單位、排序號；刪除不檢查引用。據此寫 ADR-042，把 WU-MD-021 拆為 021a／021b。新增 `md_process_category`（REQ_DS43.126）與 `md_process`（REQ_DS43.127），migration `MasterData_Process` 已套用 `myerp`。兩表沿用分類基底並加排序號 0–9999，列表預設按排序號（分類基底新增可覆寫排序欄位）。工序的類別與計件單位可空，以同公司複合外鍵參照，設定或變更時必須啟用；標準工價 decimal(19,6) ≥ 0，只是 PP 單據預設值。有工序的類別不可刪（`HAS_PROCESSES`），被工序使用的單位不可刪，被 PP 引用的工序只可停用（`IProcessUsage`）。新增契約 `IProcessLookup`（`FindAsync`、`RequireActiveAsync`、`ListActiveAsync` 按類別及工序排序）、API `/api/v1/md/process-categories`（帶工序數）與 `/api/v1/md/processes`（類別篩選）、權限 `MD.PROCESS_CATEGORY.*`／`MD.PROCESS.*`、需求 REQ_F4118.15.1–4、7.1–5。整合測試 MD-PROC-001～004（4 個案例）通過；後端建置 0 警告，單線程全量 709 個測試中只有無關的 `CompanyImageTests` 一個案例逾時失敗（41 秒，SQL 交易逾時），單獨重跑 13/13 通過，所以 709/709。帶資料庫 docgen 連續 3 次 SQL「執行逾時」（加 `Command Timeout=600` 也一樣）。主機記憶體被 Visual Studio、Chrome 等程式佔用，SQL Server 只剩約 350 MB，sqlcmd 也連不上；產出檔未改動，留待 021b 一併重產。

- 2026-10-12 WU-MD-018a 結算方式與付款條件後端完成（WU-MD-018 第一段，已於 018b 完成，見「最近完成」）：BlueWhale 只有結算方式（4.1.1.8.14，`PayType` 只有名稱），沒有付款條件；舊系統只在報價單有寫死的「月結 N 天」文字下拉。因此寫 ADR-040。新增 `md_settlement_method`（REQ_DS43.122，對應 BlueWhale REQ_DS43.55）與 `md_payment_term`（REQ_DS43.123），migration `MasterData_PaymentSetup` 已套用 `myerp`。結算方式為代碼＋名稱清單，不綁定資金帳戶。付款條件有三種到期規則：`NetDays`（單據日 + N 天）、`ClosingDays`（月結 N 天）、`ClosingMonthDay`（結帳日後第 M 月 D 日）。結帳日 31 表示月底，日數超過當月時取月底；不適用的欄位存 null，並由 CHECK 保證。單據保存算出的到期日，所以規則被引用後仍可修改，但不可刪除。新增契約 `ISettlementMethodLookup`、`IPaymentTermLookup`（`CalculateDueDateAsync`），以及由引用方實作的 `ISettlementMethodUsage`／`IPaymentTermUsage`。新增 API `/api/v1/md/settlement-methods`、`/api/v1/md/payment-terms`、`/payment-terms/due-date`，權限 `MD.SETTLEMENT_METHOD.*`、`MD.PAYMENT_TERM.*`。需求 REQ_F4118.14.1–5、40.1–5。新增測試 MD-PAY-001～007（共 36 個案例，含 14 個到期日邊界與 16 個驗證邊界）。後端 700 / 700，建置 0 警告。第一次全量執行時，6 個無關的 SYS 測試因 SQL 35 秒逾時失敗（WU-P0-014 已知問題）；以 `--max-threads 1` 重跑全部通過。客戶／供應商預設付款條件列為 WU-MD-025。

- 2026-10-12 WU-MD-019a 會計科目後端完成（WU-MD-019 第一段，WU-MD-019 已於 019b 完成，見「最近完成」）：BlueWhale 與舊系統都沒有會計科目（BlueWhale 的 Account 是資金帳戶），按會計實務寫 ADR-039。新增 `md_account`（REQ_DS43.121），migration `MasterData_Account` 已套用 `myerp`。規則：代碼只用 1–30 位數字，子科目代碼以上層代碼開頭；上層與類別建立後固定，因此不會形成循環；類別分資產、負債、權益、成本、收入、費用，子科目沿用上層類別；借貸方向省略時按類別預設，可設相反方向表示備抵科目；有啟用子科目不可停用，上層停用時不可啟用子科目；有子科目不可刪除；可過帳 = 啟用且沒有子科目。新增 MD 契約 `IChartOfAccounts`（`FindAsync`、`RequirePostableAsync`）與 `IAccountUsage`：FI 等使用方模組實作 `IAccountUsage`，已使用的科目不可刪除、不可改方向、不可再分子科目，MD 不讀 `gl_` 表。新增 API `/api/v1/md/accounts`、權限 `MD.ACCOUNT.*`。Domain 層不可引用 Contracts（架構測試），所以類別與方向在實體內存為字串，在 API 與契約才用列舉。新增 8 個整合測試 MD-ACC-001～008，含並行刪除上層與新增子科目；後端 664 / 664、建置 0 警告。下一段 WU-MD-019b 做三語樹狀頁面、E2E 與 docgen。

- 2026-10-12 WU-MD-017 第一段（已於第三段完成，見「最近完成」）（稅別／稅率設計與共用稅額計算）：接手上一輪未提交的 ADR-038、需求、資料結構、API 與業務規則草稿，核對後保留並補上測試計畫。測試先行新增 `Tax.Calculate`，按單行金額與幣別精度計算含稅／未稅／稅額，含稅路徑以差額確保 `Net + Tax = Gross`；11 個單元案例涵蓋負數、零稅率、幣別精度與稅率邊界。後端建置 0 警告、651 / 651 測試通過；docgen 無資料庫連線檢查 0 Error / 57 Warning（稅率資料表與介面尚未實作）。稅別／稅率表、契約、API、三語頁面、選單、E2E 與資料庫一致性檢查待續；WU-MD-017 維持進行中。

- 2026-10-12 WU-MD-017 第二段（已於第三段完成，見「最近完成」）（稅別／稅率後端）：接手上一輪 codex 未提交的 12 個變更（已建置通過），保留並提交。新增 `md_tax_code`／`md_tax_rate` 與 migration `MasterData_Tax`（已套用 `myerp`）、`ITaxRateLookup` 契約（按業務日與方向查稅率、依幣別精度試算）、`TaxCodeService`／`TaxRateService`（期間首尾包含不可重疊、四位小數、停用稅別不可新增稅率、公司級 applock）、API `/api/v1/md/tax-codes`、`/rates`、`/api/v1/md/tax-rates/lookup|calculate`，權限 `MD.TAX_CODE.*`；列表一次載入該頁當日稅率避免 N+1。整合測試 MD-TAX-001～004 通過；後端 655 / 655（單元 412、整合 243）、建置 0 警告。主機實體記憶體只剩約 1 GB（SQL Server `process_physical_memory_low`），預設平行度下整合測試 98～142 個 SQL 連線逾時，改用 `--max-threads 2` 全部通過，已記入 CLAUDE.md／AGENTS.md。ui-spec 8.83–8.86、三語頁面、選單、Vitest、E2E、帶資料庫 docgen 待第三段。

- 2026-10-12 WU-MD-011 第一段（已於第二段完成，見「最近完成」）（物料品牌與分類後端）：接手上一輪 codex 未提交的 6 個變更（測試、領域模型、DbModel、`CompanyLock`），補上服務、API、權限與 migration。新增 `md_item_brand`（REQ_DS43.111，合併 BlueWhale Brand／GoodsBrand）與 `md_item_category`（REQ_DS43.112，GoodsType），migration `MasterData_ItemBrandCategory` 已套用 `myerp`。品牌沿用 `MasterCategoryService`；分類為最多 5 層的樹，上層以 `(company_id, parent_id)` 複合外鍵限同公司，服務檢查上層啟用、不可成環、搬移後子樹深度、有子分類不可刪除，新增／修改／刪除以公司級 applock 序列化（`CompanyLock.RunAsync`，單位換算改用同一實作）。API `/api/v1/md/item-brands`、`/api/v1/md/item-categories`，權限 `MD.ITEM_BRAND.*`、`MD.ITEM_CATEGORY.*`。後端 626 / 626（新增 9 個整合測試），建置 0 警告，docgen 0 Error（新增 8 個「尚無介面」Warning，前端段補上）。首次整體執行 15 個 SYS Auth API 測試 35 秒逾時，與 WU-SYS-008 記錄的偶發相同，單獨及整體重跑皆全部通過。三語頁面、選單、E2E 待第二段。

- 2026-10-11 WU-SYS-006 第一段（已於第二段完成，見「最近完成」）（IP 白名單後端）：接手上一輪 13 個未提交變更，修正 CIDR 主機位元驗證、匿名角色判斷，以及無 SYS 模組時健康檢查 500；新增 `sys_ip_whitelist`（REQ_DS43.102）與 `System_IpWhitelist` migration，已套用 `myerp`。全系統／角色／用戶層級、IPv4／IPv6 與 CIDR、有效期限、總開關（Development 關閉）、loopback 例外；登入與刷新在發 Token 前按用戶角色再次檢查；拒絕 403、Warning 與 `IP_DENIED` 稽核。管理 API `/api/v1/sys/ip-whitelist` 有權限、版本、停用及三個篩選唯一索引。後端 519 / 519（新增 28 個單元案例與 3 個整合測試）、前端 389 / 389；docgen 0 Error／41 Warning（含尚無介面的六個新需求）。（ADR-025）。

- 2026-10-11 WU-SYS-009 第一段（已於第二段完成，見「最近完成」）（審批流程引擎後端）：接手上一輪 codex 未提交的審批變更，驗證後補 ADR-022 並提交。新增 `sys_approval_flow`、`sys_approval_flow_step`、`sys_approval_delegation`、`sys_approval_instance`、`sys_approval_instance_step`（REQ_DS43.97–101），migration `System_Approval` 已套用 `myerp`。共用介面 `MyERP.Shared.Approvals`：各模組以 `IApprovalWorkflowService.SubmitAsync／CancelAsync／GetStateAsync` 送審，實作 `IApprovalResultHandler` 接收最終結果（同一交易內、儲存前呼叫，失敗整體回滾）；SYS 只存單據 Id，不跨模組讀表。流程每公司每單據類型一條、1–10 級、角色或指定用戶、金額門檻；送審時複製適用步驟快照，沒有適用步驟即自動核准。篩選唯一索引保證同一單據只有一個審批中實例；核准帶 version；不可自審（流程可允許）；代理按業務日期生效、記錄實際處理人與被代理人、不連鎖、期間不重疊。審批動作以 APPROVE／新增的 REJECT 寫稽核。API：`/api/v1/sys/approval-flows`、`approval-delegations`、`approvals`（待辦、歷史、明細、approve、reject），新增 8 個權限代碼。後端 472 / 472（新增 10 個整合測試，SYS-APR-001–011），建置 0 警告。前端頁面（介面 8.68–8.70）待第二段。

- 2026-10-11 WU-SYS-008 第一段（已於第二段完成，見「最近完成」）（單號規則後端）：新增 `sys_number_rule`（REQ_DS43.95）與 `sys_number_sequence`（REQ_DS43.96），migration `System_NumberRule` 已套用 `myerp`。共用介面 `MyERP.Shared.Numbering.INumberGenerator`，SYS 實作 `NumberGenerator` 以單一參數化 `MERGE … WITH (HOLDLOCK)` 原子遞增計數，與呼叫端同一交易：交易回滾即退回號碼，不跳號。單號 = 前綴 + 單據日期格式 + 補零流水號，按公司、規則、重設週期（不重設／每年／每月／每日，依單據日期）計數；重設週期必須包含在日期格式內；已發出單號的規則鎖定日期格式與週期、流水號不可縮短。管理 API `/api/v1/sys/number-rules`（列表、單筆、預覽、建立、修改；`SYS.NUMBER_RULE.VIEW/CREATE/EDIT`）。新增 9 個整合測試（SYS-NUM-001～009，含 16 個 DbContext 並行取號 100 次無重號無跳號、回滾退號、四種週期、公司隔離、用盡），後端 462 / 462、建置 0 警告、docgen 0 Error。首次整體執行時 7 個 Auth 類測試出現 SQL 逾時（sys_user 查詢等待 35 秒），之後整合測試與整體各重跑一次全部通過，沒有找到持鎖的工作階段，暫列為偶發。決策見 ADR-021（REQ_F4118.28.1–5）。三語規則維護頁（介面 8.67，REQ_F4118.28.6）留待第二段。

- 2026-10-11 WU-SYS-007 第一段（已於第二段完成，見「最近完成」）（稽核日誌後端）：接手上一輪 codex 未提交的稽核變更，並修正一個缺陷：樂觀鎖衝突後重試會寫出重複的稽核記錄。原因是 EF 遇到衝突時不呼叫 `SaveChangesFailed`；現在 interceptor 同時覆寫 `ThrowingConcurrencyException` 做清理。完成內容：新增 `sys_audit_log`（REQ_DS43.94，只增不改的 trigger 與 ISJSON 約束，migration `System_AuditLog`）；`AuditTrailInterceptor` 與業務資料同交易寫入欄位差異，`[SensitiveData]` 欄位遮蔽為 `***`；登入、登入失敗、登出、萬用碼登入與權限變更都寫安全事件；新增 `GET /api/v1/sys/audit-logs`（`SYS.AUDIT.VIEW`，按公司隔離）。新增 13 個整合測試（SYS-AUDIT-001～013），後端 453 / 453、建置 0 警告；docgen 0 Error。決策見 ADR-019。前端稽核頁（介面 8.66）留待第二段。

- 2026-10-11 WU-SYS-002 第二段（已於第三段完成，見「最近完成」）：測試先行開放用戶管理 API `/api/v1/sys/users`（GET 分頁＋關鍵字＋帳號排序、POST 建立、PUT 啟用／停用帶 version、POST `{id}/reset-password`、POST `{id}/unlock`），新增 `SYS.USER.RESET_PASSWORD`。範圍為目前公司有用戶公司關聯的用戶，範圍外 404；建立時同交易加入目前公司（預設、無角色）；重複帳號（含他公司）409；不可停用自己 422；DTO 只回 `isLocked`／`isPasswordExpired`（ADR-016，REQ_F4118.23.5–8）。新增 6 個整合測試（SYS-USER-010～015），後端 420 / 420、建置 0 警告；前端與 E2E 未改動。既有測試 `UnsafeSecurityConfigurationRefusesStartup` 首次執行出現 ObjectDisposedException，重跑通過，疑為偶發。三語用戶頁及 E2E 待下一段。
- 2026-10-10 WU-SYS-003 第一段（已於第二段完成，見「最近完成」）：測試先行完成後端登入。`/api/v1/sys/auth` 提供 captcha（SVG、答案只存 Redis、一次即作廢）、login、refresh、logout、me。Access Token 15 分鐘 HS256，`sub` 為用戶 Id；Refresh Token 7 天，Redis 只存雜湊，以 Lua 原子取出實現輪替，5 個並發重放只有 1 個成功；登出只撤銷本人 Token。帳密錯誤、帳號不存在及鎖定回同一 401；密碼到期回 403 不發 Token。萬用碼只在 Development 有效並寫 Warning；非 Development 設定萬用碼或缺 JWT 金鑰即拒絕啟動。`/health/ready` 加 Redis 檢查；共用核心加 401 `UnauthenticatedException`。新增 13 個整合測試，後端 381 / 381、建置 0 警告；前端與 E2E 本輪未改動。決策見 ADR-012。前端登入頁、Captcha 元件、Token 刷新與登入 E2E 待續。
- 2026-10-10 WU-SYS-002 第一段（已於第三段完成，見「最近完成」）：接手上一輪未提交變更並驗證。新增 `sys_user`（REQ_DS43.82，`System_User` migration 已套用 `myerp`）、ASP.NET Core Identity `PasswordHasher`、可由 `Security:PasswordPolicy` 設定的密碼政策（不合法設定啟動即拒絕）、連續 5 次失敗鎖 15 分鐘（以 `ExecuteUpdate` 原子累計，並發不遺失）、鎖定到期重開計數、密碼到期判斷，以及帳號大小寫及刪除後仍唯一。補啟動設定驗證測試，用戶整合測試 5 個，後端 366 / 366、建置 0 警告。需求 REQ_F4118.23.1–4 與 SYS 文件已更新。管理 API、三語用戶頁、登入整合及 E2E 要等 WU-SYS-003／004；WU-SYS-002 維持進行中。
- 2026-10-10 WU-SYS-001 第七段：測試先行建立三語公司／分店／部門列表及表單骨架；列表有本地搜尋、代碼排序與分頁，分店／部門依公司 ID 篩選，表單驗證代碼與非空白名稱並輸出大寫代碼草稿。元件不接 API、不加路由或選單；隔離檢查及三語鍵測試通過。前端正式建置、189 / 189、後端 361 / 361、既有 E2E 16 / 16 通過。授權、HTTP API、正式頁面及 E2E 驗收待 WU-SYS-002～004 後續作；WU-SYS-001 維持進行中。下一輪先做 WU-SYS-002 的用戶、密碼政策與鎖定，以解開授權前置作業。
- 2026-10-10 WU-SYS-001 第六段：測試先行補公司列表的授權 ID 篩選、啟用與軟刪除篩選、分頁、關鍵字、排序及空授權邊界；內部服務已實作，不註冊 DI 或公開 API。後端建置 0 警告、361 / 361，前端 181 / 181、既有 E2E 16 / 16 通過；docgen 0 Error / 5 Warning。完整 HTTP 授權、前端組織頁與其 E2E 驗收仍待續，工作單元維持進行中。
- 2026-10-10 WU-SYS-001 第五段：測試先行補分店／部門列表的分頁、關鍵字、排序、公司隔離、軟刪除及無公司拒絕；內部服務已實作白名單排序與無追蹤查詢。後端建置 0 警告、360 / 360 測試通過；前端 181 / 181、E2E 16 / 16；docgen 首次 SQL 逾時，重跑 0 Error / 5 Warning。公開 API、授權、前端組織頁與完整驗收尚待續，工作單元維持進行中。
- 2026-10-10 WU-SYS-001 第四段：新增三個組織整合測試，驗證三表 JSON 約束、三種代碼唯一性、部門公司隔離及軟刪除、停用公司後歷史資料、公司更新審計、部門雙 DbContext 並發。組織測試 13 / 13、後端 359 / 359 通過；既有服務與 migration 無須改動。授權、公開 API、前端與 E2E 尚未完成，工作單元維持進行中。
- 2026-10-10 WU-SYS-001 第三段：測試先行補公司建立／修改／停用、停用部門不可作上級、代碼格式、公司唯一索引與分店 JSON 約束；加入公司內部服務並補部門停用，三種組織代碼統一轉大寫與校驗。組織整合測試 10 / 10、後端 356 / 356 通過，建置 0 警告；docgen 重生 0 Error / 5 Warning。代碼規則與追溯已更新。授權、公開 API、前端、E2E 與完整驗收仍待續，工作單元維持進行中。
- 2026-10-10 WU-SYS-001 第二段：新增未註冊 DI 的 `OrganizationService`，實作分店／部門建立及修改的啟用公司、代碼、參照、上級循環和版本檢查；重複代碼檢查含軟刪除資料，並處理唯一索引衝突。補 5 個整合測試，涵蓋複合 FK 違規寫入、跨公司上級、循環、停用公司、刪除代碼和樂觀鎖版本遞增。後端建置 0 警告，353 / 353 測試通過；docgen 0 Error / 5 Warning。授權完成前服務不開放 API（ADR-011）。公司服務、權限、API、前端與完整驗收待續，工作單元維持進行中。
- 2026-10-10 WU-SYS-001 第一段：新增 SYS 組織需求、資料結構 ER、API／UI 設計、業務規則與測試計畫；`sys_company`、`sys_branch`、`sys_department` 模型與 `System_Organization` migration 已建立並套用至 `myerp`。公司為隔離根，分店／部門以公司篩選、軟刪除篩選和複合外鍵保護跨公司參照。整合測試涵蓋資料表、模型篩選與分店跨公司查詢；後端 348 / 348 通過，docgen 0 Error / 5 Warning。應用服務、權限、API、前端與其完整驗收尚未完成，工作單元維持進行中。

## 最近完成

- 2026-10-13 WU-MD-022f MD 模組收尾：沿用 028 的後端 766/766、前端 591/591、模擬 E2E 123 通過／5 略過、帶 DB docgen 0 Error／53 Warning結果；本輪重新執行真實 API E2E，登入 3/3、往來對象審批 2/2 均通過。首次選錯規格檔名；第二次將 UI 登入規格誤設 `-ApiOnly`；預設 5180 埠也曾遇啟動衝突，改用 5190／5191 後測試通過。PowerShell 重導向將 Node 色彩警告報為 `NativeCommandError`，因此外層指令回傳 1，但 Playwright 的 5 個案例結果全數通過。REQ_F4118.3.4 對照表已由 028 標為完成；沒有 BLOCKED 項目。EP-INV 拆為 18 個工作單元，期初庫存介面 4.4.1.8.14 歸 WU-INV-007，ADR-049／050 的 Usage 與共享鎖歸 WU-INV-002。MD 026／027 是獨立 P3 後續優化，MD 業務交付與模組驗收完成；建立 `v0.2.0-md` tag。

- 2026-10-13 WU-MD-028 物料刪除完成（REQ_F4118.3.4，022e 對照表發現的 BlueWhale 缺口）。新增 `MD.ITEM.DELETE` 與 `DELETE /api/v1/md/items/{id}?version=`：軟刪除物料（含停用），代碼保留不可重用；`IItemUsage` 回報使用中回 409 `MD.ITEM.IN_USE`。ADR-050 決定子資料一併處理而非擋下：專屬換算軟刪除、圖片（不讀內容）與屬性值實體刪除，同一交易內完成。刪除依名稱順序持有 `md_item`、屬性、圖片、換算的公司排他鎖（`CompanyLock.RunAsync` 多表多載）；審查時發現圖片上傳與屬性取代只在取鎖前檢查物料，改為鎖內再確認，避免孤兒資料。物料頁新增刪除鈕（確認對話框說明子資料一併刪除、刪除中停用、404／版本衝突重查、IN_USE 不重查、無權限不顯示），三語錯誤與確認文字。測試先行：整合 MD-ITEM-DEL-001～005、Vitest 5 個、Playwright 1 個新案例並加強只讀權限案例。後端 766/766（`--max-threads 2`）、前端 591/591、模擬 E2E 123 通過 5 略過、帶 DB docgen 0 Error／53 Warning；建置 0 警告。下一輪 WU-MD-022f 收尾與 tag。

- 2026-10-13 WU-MD-022e MD 完整回歸完成。後端建置 0 警告、761 / 761（`--max-threads 2`，一次通過）；前端 586 / 586；模擬 E2E 122 通過、5 略過（真實 API 規格）；真實 API E2E 分兩批（登入 3 / 3、MD 審批 2 / 2）全部通過。帶 DB docgen 第一次有 1 個 Error：022c 新增的前端案例誤用已存在的 `MD-CUS-REV-004`，改為 `MD-CUS-REV-005`（test-plan、Vitest 名稱、code-review-log）後 0 Error／53 Warning。新增 SRS 2.2.7 Setting Management 英文敘述來源 `docs/05-doc-sources/srs/ch2-2-08-settings.md`（MD + SYS 功能概述）；SDS 敘述章節不分模組，留待最終產出。核對 BlueWhale 對照表：MD 只剩 `REQ_F4118.3.4`（刪除物料）未實作，屬基線缺口，新增 WU-MD-028（P1）；期初庫存介面 4.4.1.8.14 改歸 INV。沒有 BLOCKED 項目。tag `v0.2.0-md`、MD = DONE 與拆分 EP-INV 改由 WU-MD-022f 在 028 之後完成。

- 2026-10-13 WU-MD-022d MD 真實 API E2E 完成。新增 `real-api-partner-approval.spec.ts`，以種子 admin 建立客戶／供應商及付款條件、結算方式，主管和財務分別核准 SYS 兩級流程；審批中修改與刪除回 422 `APPROVAL_PENDING` 且名稱不變，最終核准後可修改；被主檔引用的付款條件與結算方式刪除回 409。每次使用唯一代碼，測試後還原原有審批流程（新流程停用）。`Invoke-RealApiE2E.ps1` 加 `-ApiOnly`，純 API 規格不需建置 Angular；三個登入工作階段共用並依序執行，避免觸發登入限流；授權端點取得公司 ID 後帶 `X-Company-Id`。真實 SQL Server／Redis／API 驗證 MD-REAL-001～002 兩次 2/2 通過；既有 SYS 登入 3/3 為上一輪結果，完整回歸留 WU-MD-022e。

- 2026-10-13 WU-MD-022c 修正前端審查發現完成。MD-B02 前端：客戶／供應商 Pending 也顯示刪除鈕，審批中由後端回 422 `APPROVAL_PENDING` 並顯示訊息（ui-spec REQ_IO_A8.1.4／A8.6.4）。F02／F03：新增 `shared/list-page/fetch-all-pages.ts`，取代四份各自的分頁 helper；往來對象的分類、付款條件、結算方式與單位頁選項跨頁載齊，單位選項翻頁不再重載。F05：幣別、稅別、倉庫、付款條件、資金設定刪除進行中停用刪除鈕。F06：新增 `stale-row.ts`，404 或 409 版本衝突時重新查詢（同為 409 的 IN_USE、審批中不算），12 個列表頁與 4 個子表對話框套用。F07：聯絡人對話框以世代號丟棄切換往來對象前的保存回應；物料換算改以目前物料重查。F08／F09：匯入列錯誤顯示三語欄位名；選檔時先檢查副檔名與 2 MB。F10：到期日三語範本。F12：契約 `TaxCalculation` 與試算 API 加 `decimalPlaces`，前端千分位並補零。F13：物料目前使用的停用單位／品牌／分類仍可選。F04、F11 留在 WU-MD-027。新增 MD-CUS-REV-004、MD-FE-REV-001～005；後端 761/761（`--max-threads 2`）、前端 586/586（新增 20 個）、E2E 122 通過 3 略過（更新供應商待審刪除、三個匯入欄位名、稅額試算回應），建置 0 警告。

- 2026-10-13 WU-MD-022b（第 3 段，WU-MD-022b 完成）修正 MD-B07–B12。B07：`RequireApprovedAsync` 改 AsNoTracking 投影，一次查詢帶出預設付款條件／結算方式代碼。B08：物料圖片排序／主圖／刪除以不含內容的中繼資料附加實體，仍經 SaveChanges（稽核、版本、樂觀鎖不變）。B09：六個匯入 POST 加 `RequestSizeLimit`／`RequestFormLimits`（2 MB + 64 KB）。B10：資金帳戶、收支類別、工序查找的 null 代碼回 null／404。B11：簡化 Create／Update 多載移到 `SimpleCategoryService<T>`，只有客戶／供應商分類、品牌、結算方式繼承。B12：`MasterDataQueries`（RequireCompany、IsUniqueViolation、OrderByCodeOrName）取代重複代碼。新增 MD-IMG-005，MD-PAY-DEF-001、MD-FUND-001／002、MD-PROC-003、MD-IMP-004 加入斷言；後端 761 / 761（`--max-threads 2`），建置 0 警告；前端未改動。

- 2026-10-13 WU-MD-022b（第 2 段）修正 MD-B04、B05（ADR-049）。B04：`ITaxRateLookup.GetRatePercentAsync`／`CalculateAsync` 新增 `includeInactive`（預設 false），停用稅別回 422 `MD.TAX_CODE.INACTIVE`，重算歷史單據時明確允許；維護頁查詢／試算 API 仍可查停用稅別；試算幣別改讀一筆幣別主檔，不存在回 404 `MD.CURRENCY.NOT_FOUND`，不再默默用 2 位小數。ADR-038 第 5 點標記被取代。B05：新增契約 `IItemUsage`（改基本單位 → `BASE_UOM_LOCKED`）、`IWarehouseUsage`／`IStorageLocationUsage`（刪除 → `IN_USE`）、`ITaxCodeUsage(taxCode, from, to)`（刪稅別、改／刪稅率；只改期間時只檢查不再涵蓋的頭尾）；匯率不設契約。三語錯誤訊息與前端錯誤碼對應同步。需求 REQ_F4118.3.3、4.4、4.5、37.4、38.3–38.6 升 1.1；新增 MD-USAGE-001～004。後端 760/760（`--max-threads 2`）、前端 566/566，建置 0 警告。
- 2026-10-13 WU-MD-022b（第 1 段）修正 MD-B02、B03：接手上一輪未提交變更並補完。B02：客戶／供應商主檔 Pending 時刪除改查 SYS 審批實例，只有審批中回 422，從未送審或已撤回可刪（匯入錯誤可直接刪）；送審強制遞增版本，與並發刪除以 409 互斥；需求 REQ_F4118.1.6／2.6、API、MD-CUS-REV-001／MD-SUP-REV-001 與 ADR-048 補充同步。B03：`CompanyLock.RunSharedAsync`，被引用表（分類、品牌、單位、付款條件、結算方式、幣別）刪除取排他鎖，客戶／供應商、物料、單位換算、物料換算、工序、資金帳戶與往來／物料匯入在驗證到保存期間取共享鎖；新增 MD-ITEM-003 以 APPLOCK_TEST 驗證鎖模式。後端 756/756（`--max-threads 2`），建置 0 警告。前端 Pending 隱藏刪除鈕留給 022c。
- 2026-10-13 WU-MD-022a MD 模組級代碼審查完成：後端與前端各由唯讀審查代理逐檔閱讀，主代理逐項核對；後端 12 項、前端 13 項發現寫入 code-review-log.md。架構檢查：MD 只引用 Shared 與自己的 Contracts，審批經 Shared 介面，沒有禁用 API。修正 High 級 MD-B01：送審後、審批人決定前仍可修改客戶／供應商，審批人核准的快照與主檔不符；現在主檔為 Pending 時經 `IApprovalWorkflowService.GetStateAsync` 查 SYS，審批中修改回 422 `APPROVAL_PENDING`，核准後的維護不需重審（ADR-048）。修正 MD-F01：`SYS.APPROVAL.ALREADY_PENDING` 三語缺翻譯，新增 MD-PARTNER-I18N 測試檢查每個已辨識錯誤碼都有翻譯。日誌分析（10-12～10-13）：沒有 MD 端點的 500 或慢請求，SQL 逾時屬已知的主機負載問題。其餘發現排入 022b（後端）、022c（前端）、WU-MD-026（批次契約）、WU-MD-027（列表頁共用元件），真實 API E2E 改為 022d，回歸與 tag 改為 022e。後端 754/754（`--max-threads 2`）、前端 566/566、客戶／供應商 E2E 11/11，建置 0 警告。

- 2026-10-13 WU-MD-025 客戶／供應商預設付款條件與結算方式完成：寫 ADR-047。共用基底 `BusinessPartner` 加可空 `DefaultPaymentTermId`／`DefaultSettlementMethodId`，`md_customer`／`md_supplier` 以同公司複合外鍵參照 `md_payment_term`／`md_settlement_method`（migration `MasterData_PartnerPaymentDefaults` 已套用 `myerp`）。API Create／Update／Response 帶兩個 id，PUT 缺少即清除；新選取須啟用、已停用的現值可保留（400 欄位名）；被往來對象設為預設的付款條件／結算方式不可刪（409 IN_USE）。`CustomerIdentity`／`SupplierIdentity` 追加預設代碼供 SD／PUR 帶出。匯入不含預設值。`PartnerList` 加兩個下拉與列表「預設付款條件」欄，三語；E2E fixture 對兩個選項 API 預設回空清單，並修正 `payment-setup.spec` 的時序斷言。需求 REQ_F4118.1.8、2.8，測試 MD-PAY-DEF-001～002。後端 752/752（`--max-threads 2`），前端 565/565，E2E 122 通過／3 略過，帶 DB docgen 0 Error／53 Warning。

- 2026-10-13 WU-MD-024 供應商審批與刪除完成：`md_supplier.review_status`（Pending／Approved／Rejected，CHECK），migration `MasterData_SupplierReview` 將既有供應商設 Approved，已套用 `myerp`。`POST /api/v1/md/suppliers/{id}/submit`（`MD.SUPPLIER.APPROVE`）以 `MD.SUPPLIER` 交 SYS 審批，`SupplierApprovalResultHandler` 在 SYS 交易內回寫；`DELETE /api/v1/md/suppliers/{id}?version=N`（`MD.SUPPLIER.DELETE`）軟刪除，待審 422、`ISupplierUsage` 回報引用 409；新增 `ISupplierLookup.RequireApprovedAsync`。為免複製客戶實作，審核狀態移到 `BusinessPartner` 基底（`PartnerReviewStatus`，JSON 值不變），抽出 `PartnerApprovalService`／`PartnerApprovalResultHandler`，刪除與核准檢查移入 `BusinessPartnerService`；前端送審／刪除與錯誤碼移入共用服務，`PartnerList` 對供應商同樣顯示審核欄與操作；三語文字與錯誤。ADR-046 補充；REQ_F4118.2.4–6、REQ_IO_A8.6.3–4；reference-notes 同步標記客戶 1.4–6 與供應商 2.4–6 完成。後端 748/748（`--max-threads 2`）、前端 563/563、E2E 121 通過／3 略過、帶 DB docgen 0 Error／53 Warning。

- 2026-10-13 WU-MD-023 客戶審批與刪除完成（接手上一輪未提交變更）：`md_customer.review_status`（Pending／Approved／Rejected，CHECK 約束），migration `MasterData_CustomerReview` 將既有客戶設為 Approved，已套用 `myerp`；新客戶與匯入客戶為 Pending。`POST /api/v1/md/customers/{id}/submit`（`MD.CUSTOMER.APPROVE`，帶版本）以 `MD.CUSTOMER` 交 SYS 審批，無適用步驟即核准；SYS 最終核准／駁回在同一交易內經 `IApprovalResultHandler` 回寫，Rejected 可再送審。`DELETE /api/v1/md/customers/{id}?version=N`（`MD.CUSTOMER.DELETE`）軟刪除，Pending 回 422、被單據引用（使用方實作 `ICustomerUsage`）回 409，代碼保留不可重用。新增契約 `ICustomerLookup.RequireApprovedAsync` 供交易模組檢查啟用且核准。客戶頁加審核狀態欄、送審與刪除（popconfirm）按鈕與三語錯誤。ADR-046；REQ_F4118.1.4–6、REQ_IO_A8.1.3–4。本輪補 partner-list 元件測試（送審／刪除版本、錯誤、防重複），修正 E2E 未模擬客戶分類 API 造成的 404 console error，並把全套負載下會 race 的 shared-ui 換頁測試改為等待新頁出現（斷言不變）。後端 745/745（`--max-threads 2`）、前端 561/561、E2E 120 通過／3 略過、帶 DB docgen 0 Error／53 Warning。

- 2026-10-13 WU-MD-014b 物料匯入介面完成：物料頁加 `MD.ITEM.IMPORT` 權限入口，沿用客戶／供應商匯入對話框的 xlsx／CSV 範本、唯讀預覽、列號與欄位錯誤、重預覽及提交後清單刷新；新增物料三語提示並說明不匯入期初庫存。新增 Vitest 驗證參照錯誤與衝突後重預覽，Playwright 驗證範本、錯誤列、提交與無權限入口。前端 559/559、E2E 119 通過／3 略過；帶 DB docgen 0 Error／53 Warning。後端沿用 014a 的 743/743。

- 2026-10-13 WU-MD-014a 物料匯入後端完成：把 WU-MD-014 拆為後端 014a 與三語頁面 014b。新增 `MD.ITEM.IMPORT` 與 `/api/v1/md/items/import` 範本（xlsx／CSV）、唯讀預覽、整批提交 API；欄位為代碼、名稱、規格、基本單位代碼、品牌代碼、分類代碼。預覽顯示實際列號和欄位錯誤；提交在公司鎖及交易內重驗證，已存在代碼略過、檔內重複報錯，任一錯誤整批不寫；沒有庫存資料。測試先行新增 ItemImportTests 5 件，覆蓋冪等、公司隔離、並行同碼、預覽後參照停用、範本與格式邊界。後端全量首次與前端測試、docgen 並行時有一件 SYS 授權 API SQL 逾時，獨立重跑 743/743；前端 558/558，帶 DB docgen 0 Error／53 Warning。下一輪 WU-MD-014b 完成物料頁三語入口、Vitest 與 Playwright。

- 2026-10-12 WU-MD-013c 物料圖片與屬性三語頁面完成（ADR-045 第 15 點）：物料列新增圖片對話框（Blob→data URL 預覽、multipart 上傳、主圖／排序帶版本修改、刪除）與屬性對話框（整批取代、停用屬性既有值保留、後端欄位錯誤定位）；`/md/item-attributes` 屬性定義頁沿用排序主檔頁，顯示物料數與使用中不可刪除錯誤。補三語 i18n、介面 8.15／8.16、需求與測試追溯、reference-notes 狀態；`toDataUrl` 移入共用圖片工具。檢查時修正切換物料後舊請求回應覆蓋新對話框狀態，補兩個回歸測試。前端 558/558、E2E 117 通過／3 略過、帶 DB docgen 0 Error／53 Warning（比前段少 10 個 TRACE_GAP）；後端未改，沿用 013b 的 738/738。

- 2026-10-12 WU-MD-013b 物料屬性後端完成（WU-MD-013 第二段，ADR-045 第 11–13 點）：新增 `md_item_attribute`（REQ_DS43.132，沿用分類基底加排序號 0–9999，實體 `ItemAttributeDefinition`，因 CA1711 不可以 Attribute 結尾）與 `md_item_attribute_value`（REQ_DS43.133，物料 × 屬性唯一、值 nvarchar(200) 必填、兩個同公司複合外鍵），migration `MasterData_ItemAttributes` 已套用 `myerp`。API `/api/v1/md/item-attributes`（`MD.ITEM_ATTRIBUTE.VIEW/CREATE/EDIT/DELETE`，列表帶使用物料數，被使用刪除 409 `MD.ITEM_ATTRIBUTE.IN_USE`）與 `GET/PUT /api/v1/md/items/{itemId}/attributes`（`MD.ITEM.VIEW`／`EDIT`；PUT 整批取代、最多 50 個、新屬性須啟用、原有停用屬性可保留，錯誤鍵 `attributes[i].attributeId|value`）；取代與屬性刪除共用公司鎖 `md_item_attribute`，只寫入實際變更的列。需求 REQ_F4118.45.1–6，測試 MD-ATTR-001～004（4 個整合案例）。後端 738/738（`--max-threads 2`）、建置 0 警告、帶 DB docgen 0 Error／63 Warning（新需求尚無介面，013c 補上）。前端未改動。

- 2026-10-12 WU-MD-013a 物料圖片後端完成（WU-MD-013 第一段）：用 subagent 查 BlueWhale 對照與舊系統規則：BlueWhale 沒有圖片／屬性 REQ_F，只有介面 4.4.1.8.15／8.16 與 `GoodsImages`、`GoodsAttribute` 表；舊系統圖片存磁碟、只看副檔名、最多一張預設圖、刪除不重新指定預設；屬性只是名稱清單、沒有值。據此寫 ADR-045，把 WU-MD-013 拆為 013a（圖片後端）、013b（屬性後端）、013c（三語頁面）。新增 `md_item_image`（REQ_DS43.131），migration `MasterData_ItemImage` 已套用 `myerp`：內容存資料庫，以檔頭判斷 PNG／JPEG，每張 2 MB、寬高 16–4096 像素、每物料 10 張；有圖片的物料恰好一張主圖（第一張自動、指定新主圖取消原主圖、不可直接取消、刪除主圖由排序號最小者接任），篩選唯一索引把關，維護以公司級 applock 序列化，鎖內只投影中繼欄位不讀內容。API `/api/v1/md/items/{itemId}/images`（列表、內容 ETag／304、上傳、修改、刪除），權限沿用 `MD.ITEM.VIEW`／`EDIT`；需求 REQ_F4118.44.1–4。SYS 的 `ImageInspector` 移到 `MyERP.Shared.Infrastructure.Imaging`（加 `ReadLimitedAsync`、`SafeFileName`）供兩模組共用。新增 MD-IMG-001～004（4 個整合案例）；後端 734/734（`--max-threads 2`）、建置 0 警告、帶資料庫 docgen 0 Error／57 Warning（新需求尚無介面，013c 補上）。前端未改動。

- 2026-10-12 WU-MD-009 供應商匯入完成（REQ_F4118.2.7、介面 8.10、ADR-044 補充；MD-SUP-IMP-001～003）：把客戶匯入抽成往來對象共用。後端 `PartnerImportService<TPartner,TCategory>` 負責預覽只讀、公司鎖內整批交易、代碼冪等略過與欄位錯誤，客戶／供應商子類只提供建立主檔、預設聯絡人（客戶另有預設地址）與欄位清單；範本與上傳讀取移到 `ImportFiles`。供應商範本 `code,name,categoryCode,contactName,contactMobile,contactPhone,contactEmail`，沒有地址欄（供應商無地址子表，ADR-043），檔內地址欄當未知欄位忽略。新增 `MD.SUPPLIER.IMPORT` 權限與 `/api/v1/md/suppliers/import`（template／preview／commit）、錯誤碼 `MD.SUPPLIER_IMPORT.HAS_ERRORS`。前端 `PartnerImportService`／`PartnerImportDialog` 以 `kind` 決定路徑、錯誤碼、標題與 data-testid；`PartnerPage.imports` 改為 kind，供應商頁加入匯入入口；三語文案。新增整合 4、Vitest 2、Playwright 2；客戶匯入原有測試不改斷言仍通過。後端 730/730（`--max-threads 2`）、前端 542/542、E2E 113 通過 3 略過、建置 0 警告、帶 DB docgen 0 Error／53 Warning（多 1 個與客戶匯入相同的「匯入需求沒有資料表」提示）。

- 2026-10-12 WU-MD-008 供應商聯絡人完成（REQ_F4118.43.1–4、REQ_DS43.130、介面 8.9）：依 ADR-043 第 8 條，供應商只有聯絡人、沒有地址表，所以前後端在一個單元完成。後端加 `SupplierContact`、`md_supplier_contact`（複合外鍵、篩選唯一預設索引、CHECK）、`md_supplier` 的 `(company_id, id)` 唯一約束、`SupplierContactService`（沿用 `PartnerChildService`）與 `/api/v1/md/suppliers/{supplierId}/contacts`；migration `MasterData_SupplierContact` 已套用本機 `myerp`。前端把客戶對話框改為共用的 `PartnerContactDialog`（`kind` 決定 API、權限、標題與地址段），服務抽出 `PartnerContactApi`；供應商列加「聯絡人」按鈕；三語標題、提示與 `MD.SUPPLIER_CONTACT.NOT_FOUND`。測試：SupplierContactTests 2 個、Vitest 3 個、Playwright 2 個。後端 726/726（`--max-threads 2`）、前端 540/540、E2E 111 通過／3 略過（全量時 expired-password 偶發逾時，單獨重跑通過）、帶 DB docgen 0 Error／52 Warning。下一單元 WU-MD-009 供應商匯入。

- 2026-10-12 WU-MD-005c 客戶匯入三語介面完成（REQ_F4118.1.7、介面 8.5、MD-IMP-005）：客戶頁入口由 `MD.CUSTOMER.IMPORT` 獨立控管；下載 xlsx／CSV 範本，選檔後逐列預覽實際列號、代碼、名稱、Create／Skip／Error 與欄位錯誤；有錯誤不得提交。提交重新上傳同一檔案，422／409 後清除舊預覽並可重試；換檔、切公司與關閉對話框也清除預覽，遲到回應不覆蓋新檔。新增三語文案、Vitest 4 個、Playwright 2 個，更新需求追溯及 docgen。前端 537/537、E2E 109 通過／3 略過、後端建置 0 警告、帶 DB docgen 0 Error／52 Warning。全量後端測試在無關的 CompanyImage／Currency 整合案例重現 SQL 執行逾時，已停止本次全量執行，未宣稱通過；單元、架構及 docgen 測試通過，005b 客戶匯入定向整合 4/4 已通過。下一單元 WU-MD-008 供應商聯絡人與地址。

- 2026-10-12 WU-MD-005b 客戶匯入後端完成（ADR-044，REQ_F4118.1.7；MD-IMP-003～004）：新增 `CustomerImportService`，預覽只讀，按檔案實際列號回 `Create`／`Skip`／`Error` 與欄位錯誤；提交在公司級 applock 的單一交易中重新驗證，任一錯誤列整批 422，已存在本公司代碼（含停用／刪除）略過、不更新。分類以本公司啟用代碼對應，聯絡人／地址沿用維護驗證並各建一筆預設；併發匯入同碼只有一筆新增。新增範本 xlsx／CSV、預覽及提交 API，三者皆需 `MD.CUSTOMER.IMPORT`，權限目錄與 DI 已接入。SQL Server 定向整合 4/4、架構 37/37、後端建置 0 警告；無 DB docgen 0 Error／53 Warning（降級輸出未提交）。全量整合先後在無關 SYS 審批及幣別案例 SQL 執行逾時；關閉閒置 MSBuild 伺服器後定向案例重跑通過。此為既有 WU-P0-014 主機問題，未宣稱全量通過。下一單元 005c 做三語匯入介面與 E2E。

- 2026-10-12 WU-MD-005a 客戶匯入共用檔案基礎完成：接手上一輪未提交的 ADR-044、`TabularFileReader`、`TabularTemplate` 與測試；匯入服務草稿尚未接 API 且無整合測試，已移除並列入 005b。讀取器按內容辨識 xlsx／UTF-8 CSV（含 BOM），保留實際列號，檢查重複／缺少表頭、2 MB／1,000 列限制及損壞檔案；修正 CSV 已關閉引號後仍接受多餘字元的問題。範本支援兩種格式，7 個共用單元案例涵蓋範本往返、共用字串、稀疏儲存格及格式錯誤。補 REQ_F4118.1.7 驗收條件與 MD-IMP-001～005，依 ADR-044 拆為 005a–c。正常還原後端建置 0 警告，Shared 380/380、架構 37/37、docgen 單元通過；全量整合測試在無關的 `NotificationPublisherTests.PublishIsCompanyScopedAndIdempotent` 與 `PaymentSetupTests.PaymentSetupTablesHaveRuleChecks` 遇 SQL 執行逾時，已停止，未宣稱全量通過。005b 接續客戶匯入服務、API 與整合測試。

- 2026-10-12 WU-MD-004b 客戶聯絡人與地址頁面完成（WU-MD-004 = DONE）：客戶列表每列加「聯絡人」按鈕，開啟獨立的 `CustomerContactDialog`（介面 8.4），沒有放進客戶編輯對話框：子資料逐筆即時保存，放在編輯對話框內會讓人誤以為「取消」可撤銷；只有 `MD.CUSTOMER.VIEW` 的用戶也要能唯讀查看（ADR-043 補充）。對話框分聯絡人、送貨地址兩段，預設排第一並加標籤；表單驗證姓名、電郵、地址與長度，停用時不可勾預設；保存或刪除後重讀該段列表（預設切換由後端處理）；遇 404 取消編輯並重讀，遇 409 保留草稿並重讀。兩段共用泛型 `ChildSection`，服務 `CustomerContactService` 統一去空白、空白轉 null。`PartnerPage.contacts` 控制按鈕，供應商頁不顯示。三語新增 `md.customerContact.*` 與 `MD.CUSTOMER_CONTACT／CUSTOMER_ADDRESS.NOT_FOUND`。順便修正 004a 在 requirements.md 重複的子功能編號 8.1（改為 8.41／8.42），這是 docgen 的唯一 Error。前端 533 / 533（新增 7 個；全量執行時 2 個無關的首頁／路由測試在負載下逾時，單獨重跑通過）；E2E 107 通過、3 略過（新增 2 個；首次全量與 Vitest 並行時 24 個無關規格逾時，`--last-failed --workers=1` 重跑 24/24 通過）；production 建置成功。帶資料庫的 docgen 連續 3 次 SQL 執行逾時（已知主機記憶體問題）；無資料庫 docgen 0 Error／52 Warning，輸出未提交。後端沒有改動。

- 2026-10-12 WU-MD-021b（工序類別與工序三語頁面，WU-MD-021 完成）：`/md/process-categories`（介面 8.51–8.53）與 `/md/processes`（介面 8.27–8.29）共用新的 `ProcessPage`，由路由的 `PROCESS_PAGE_KIND` 決定種類；沒有沿用扁平分類列表，以免改動 5 個既有頁面。兩頁預設按排序號排列，類別列表顯示工序數；排序號限 0–9999 整數。工序可按類別篩選，新增時帶入目前篩選的類別；類別與計件單位選項只列啟用項，編輯時保留原有停用值。標準工價以文字輸入，限 ≥ 0、最多 9 位整數及 6 位小數（避免 JSON 數字失真）。缺 `MD.PROCESS_CATEGORY.VIEW`／`MD.UOM.VIEW` 時不讀對應 API。另有選單、三語文案與錯誤碼（HAS_PROCESSES、IN_USE、INVALID_CATEGORY、INVALID_UOM 等）。Vitest 9 例、Playwright 4 例（MD-PROC-UI-001～005）。補 020a／021a 需求的子功能表，並把 reference-notes 中收支類別、資金帳戶、工序的 REQ 與介面狀態改為完成。前端 526/526、建置 0 警告；完整 E2E 105 通過、3 略過；帶資料庫 docgen 0 Error／51 Warning（重產 021a 未產出的部分）。後端未改動（上次 709/709）。

- 2026-10-12 WU-MD-020b（收支類別與資金帳戶三語頁面）：接手上一輪 20 個未提交變更並完成。`/md/fund-accounts`（介面 8.20–8.22）與 `/md/income-expense-categories`（介面 8.42–8.44）共用 `FundSetupPage`，由路由提供種類；Cash 隱藏並清除銀行欄位，幣別與方向建立後唯讀，科目只列啟用、可過帳的對應類別，缺 `MD.ACCOUNT.VIEW`／`MD.CURRENCY.VIEW` 時不讀參照 API。另有選單、三語文案與錯誤碼、Vitest 3 例、Playwright 4 例（MD-FUND-UI-001～005）。本輪修正 E2E 取消按鈕選擇器與錯誤提示斷言。前端 517/517；完整 E2E 101 通過、3 略過（首次並行有 5 個無關規格逾時，單 worker 重跑通過）；帶資料庫 docgen 0 Error／60 Warning（SQL 登入慢，連線字串需 `Connect Timeout=180`）。

- 2026-10-12 WU-MD-020a（收支類別與資金帳戶後端）：接手上一輪 6 個未提交草稿，依 ADR-041 將資金帳戶主檔歸 MD，交易與餘額歸 FI；WU-MD-020 拆為 020a 後端與 020b 前端。新增 `md_income_expense_category`（REQ_DS43.124）及 `md_fund_account`（REQ_DS43.125），`MasterData_FundSetup` migration 已套用 `myerp`。類別方向建立後固定；資金帳戶幣別建立後固定，Cash 不存銀行欄位；兩者可選相符且可過帳的科目，引用保護經 `IAccountUsage`。新增各自 CRUD API、權限、Lookup／Usage 契約與 5 個整合案例 MD-FUND-001～005。後端建置 0 警告；單線程整合 293/293、單元與架構 412/412，合計 705/705；帶資料庫 docgen 0 Error／68 Warning（主要是未完成介面與既有追溯缺口）。初次全量的 1 個幣別模型相容問題已修正；再次 2 線程全量遇到既有 SQL 逾時，改單線程全部通過。三語頁面與 E2E 待 020b。

- 2026-10-12 WU-MD-018 結算方式與付款條件完成（018b：三語頁面）：`/md/settlement-methods` 沿用扁平分類列表（介面 8.48–8.50），`/md/payment-terms` 顯示到期規則說明並提供新增／編輯與已存條件的單據日試算（介面 8.89–8.91）；兩頁均接上選單與 VIEW 路由保護，寫入按鈕按 CREATE／EDIT／DELETE 權限顯示。付款規則表單依三種 `dueType` 切換欄位、驗證範圍與同月先後，送出前清除不適用欄位；日期變動或關閉試算時取消舊請求。三語文案與 `MD.SETTLEMENT_METHOD.*`、`MD.PAYMENT_TERM.*` 錯誤碼已補。更新 ui-spec、test-plan 與 BlueWhale 對照狀態。後端 700 / 700、前端 514 / 514（新增 2 個）、E2E 97 通過、3 略過（新增 2 個）；帶資料庫 docgen 0 Error／48 Warning（14.5 是無 UI 的模組查詢契約）。

- 2026-10-12 WU-MD-019 會計科目完成（019b：三語頁面）：新增 `AccountService` 與 `/md/accounts`（介面 8.87–8.88，`permissionGuard('MD.ACCOUNT.VIEW')`，選單「主檔 → 會計科目」）。樹狀列表一次載入，按代碼排序並縮排顯示代碼、名稱、類別、餘額方向、可過帳／彙總與狀態，搜尋在前端過濾並保留上層。「新增一級科目」必選類別，選擇後帶出預設方向（仍可改為備抵方向）；啟用科目可「新增子科目」，代碼預填上層代碼、類別沿用上層且唯讀、前端先檢查前綴，送出不帶類別；修改名稱／方向／啟用帶 version，代碼與類別唯讀；刪除二次確認。物料分類的組樹與搜尋函式改為泛型共用。`MD.ACCOUNT.*` 12 個錯誤碼（含 FI 用的 INACTIVE、NOT_POSTABLE）三語。前端 512 / 512（新增 11 個）、E2E 95 通過、3 略過（新增 2 個）；後端無改動，首次整體執行 3 個無關整合測試 SQL 35 秒逾時（主機記憶體不足，見 CLAUDE.md），整合測試單獨以 `--max-threads 2` 重跑 252 / 252，後端合計 664 / 664，建置 0 警告；帶資料庫 docgen 0 Error／47 Warning（新增的 39.5 為模組契約，沒有介面屬預期）。test-plan MD-ACC-UI-001～002 通過。

- 2026-10-12 WU-MD-017 稅別與稅率完成（第三段：前端）：新增 `TaxService` 與三語 `/md/tax-codes` 頁（介面 8.83–8.86，`permissionGuard('MD.TAX_CODE.VIEW')`，選單「主檔 → 稅別與稅率」）。列表由伺服器搜尋、排序、分頁，顯示適用類型與今日稅率（沒有則顯示「今日無稅率」）；新增／編輯對話框可設代碼（編輯唯讀）、名稱、類型與啟用狀態。稅率對話框沿用匯率頁的模式：按日期篩選、期間驗證、0–100 且最多 4 位小數、帶版本修改與刪除、停用稅別不可新增；變更後通知列表重新載入今日稅率。試算對話框呼叫 `POST /api/v1/md/tax-rates/calculate`：方向依稅別類型限制，日期預設今日，顯示稅率、未稅、稅額、含稅，輸入改變即清除舊結果。後端 `TaxDirection` 加上 `JsonStringEnumConverter`，API 以 `Output`／`Input` 傳遞（與 api.md 一致，仍接受數字）。前端 501 / 501（新增 13 個）、E2E 93 通過、3 略過（新增 3 個）、後端 656 / 656（新增 1 個；首次整體執行時 2 個 Auth API 測試因同時跑 docgen 導致 SQL 逾時，整合測試單獨重跑 244 / 244 通過），建置 0 警告，帶資料庫 docgen 0 Error／46 Warning（無稅別相關）。test-plan MD-TAX-005、MD-TAX-UI-001～002 通過。

- 2026-10-12 WU-MD-016 幣別與匯率完成：ADR-037 決定本位幣是 MD 幣別上的旗標（不在 `sys_company`），每間公司最多一個，設新本位幣時自動取消舊的，已有匯率後不可更換；匯率方向為 1 外幣 = rate 本位幣、`DECIMAL(19,8)`、人手維護、同幣別期間不可重疊。新增 `md_currency`（REQ_DS43.117，ISO 4217 `char(3)`、符號、金額小數位 0–4、篩選唯一本位幣索引）與 `md_exchange_rate`（REQ_DS43.118，生效／到期日、`rate > 0`、期間 CHECK、`(company_id, currency_id)` 複合外鍵），migration `MasterData_Currency` 已套用 `myerp`。幣別與匯率維護共用公司級 applock `md_currency`。API `/api/v1/md/currencies`、`/{id}/rates`、`/api/v1/md/exchange-rates/lookup`，權限 `MD.CURRENCY.VIEW／CREATE／EDIT／DELETE`（匯率維護用 EDIT）。模組契約 `ICurrencyLookup`（本位幣、找幣別、按業務日期取匯率、以主檔小數位建立 `CurrencyPrecision`）。前端 `/md/currencies`（介面 8.79–8.81）與匯率對話框（介面 8.82，按日篩選；前端匯率限 6 位整數 8 位小數）、選單、三語文字。需求 REQ_F4118.35.1–4、36.1–5（BlueWhale 沒有，按 ADR-002 追加）。後端 640 / 640（新增 5 個整合測試）、前端 488 / 488（新增 10 個）、E2E 90 通過 3 略過（新增 3 個）、docgen 0 Error（新增 1 個 Warning：36.5 查詢契約沒有介面，屬預期）。首次後端整體執行有 8 個整合測試 SQL 35 秒逾時（與 WU-SYS-008 記錄相同），隨即整體重跑 640 / 640；docgen 第一次也 SQL 逾時，E2E 結束後重跑正常。示範公司本位幣（TWD／HKD）待 MD 種子資料寫入。

- 2026-10-12 WU-MD-015 倉庫與儲位完成：ADR-036 決定倉庫與儲位分兩表、儲位只有一層（區／排／層寫在代碼中）、MD 不存任何庫存數量。新增 `md_warehouse`（REQ_DS43.115，分類基底加地址／聯絡人／電話，`ak (company_id, id)`）與 `md_storage_location`（REQ_DS43.116，倉庫內代碼永久唯一，`(company_id, warehouse_id)` 複合外鍵），migration `MasterData_Warehouse` 已套用 `myerp`。只能在啟用倉庫新增儲位（422 `WAREHOUSE_INACTIVE`），有儲位的倉庫不可刪除（409 `HAS_LOCATIONS`），兩者共用公司級 applock。API `/api/v1/md/warehouses` 與 `/{id}/locations`，權限 `MD.WAREHOUSE.VIEW／CREATE／EDIT／DELETE`（儲位維護用 EDIT）。前端 `/md/warehouses`（介面 8.17–8.19）與儲位對話框（介面 8.78）、選單、三語文字。審查時抽出 `MasterDataInput`，MD 五個服務共用代碼／名稱／選填文字驗證。後端 635 / 635（新增 4 個整合測試）、前端 478 / 478（新增 8 個）、E2E 87 通過 3 略過（新增 3 個）、docgen 0 Error。INV 引用檢查契約留待 EP-INV 決定。

- 2026-10-12 WU-MD-012c（物料專屬換算，WU-MD-012 全部完成）：新增 `md_item_uom_conversion`（REQ_DS43.114）及 `MasterData_ItemUomConversion` migration，已套用本機 `myerp`；`md_item` 加 `ak_md_item_company_id_id` 供複合外鍵。決策見 ADR-035：每列「1 換算單位 = 係數 基本單位」，以基本單位為中心，不會成環；同物料同單位只一列（409 `MD.ITEM_UOM_CONVERSION.DUPLICATE`），單位須為本公司啟用單位且不是基本單位。按物料換算時，專屬換算覆寫該單位的公司級換算，其他單位以公司級路徑補到基本單位；兩端換不到基本單位時用公司級直接路徑；全程 decimal、分子分母分開累乘，按目標單位小數位數遠離零捨入。有專屬換算時修改基本單位回 409 `MD.ITEM.BASE_UOM_LOCKED`，新增換算與修改基本單位共用公司級 applock。被專屬換算引用的單位不可刪除。契約 `IUomConverter.ConvertForItemAsync`；API `/api/v1/md/items/{itemId}/uom-conversions`（含 `convert` 試算），查看用 `MD.ITEM.VIEW`、維護用 `MD.ITEM.EDIT`。前端物料列新增「換算」對話框（介面 8.77）：規則列表、新增／修改係數／刪除、試算，三語與亮暗主題。另更正需求編號：物料原誤用 BlueWhale 收支分類的 `REQ_F4118.12.x`，改為 Product Management `REQ_F4118.3.1–3`，新增 `REQ_F4118.3.5`。後端 631/631（新增 3 個整合測試），前端 470/470（新增 4 個），E2E 84 通過 3 略過（新增 1 個），建置 0 警告，docgen 0 Error／45 Warning。

- 2026-10-12 WU-MD-012b（物料三語頁面）：`/md/items` 提供按代碼／名稱／規格搜尋、伺服器分頁、代碼／名稱排序、建立及帶版本修改；列表顯示規格、基本單位、品牌、階層分類與狀態。單位、品牌、分類只在擁有各自 VIEW 權限時載入，跨頁取齊參照；停用選項不可新選，編輯時可保留原值。`MD.ITEM.VIEW/CREATE/EDIT` 控制路由與操作；三語文字、亮暗主題與錯誤碼沿用共用機制。介面 8.11–8.13 與 11 個 REQ_IO_A 已寫入 ui-spec，測試追溯已更新。後端 628/628、建置 0 警告，前端 466/466、E2E 83 通過 3 略過、docgen 0 Error／45 Warning。首次並行後端／docgen 遇 SQL 逾時，後端單獨重跑全過；docgen 以本機 TCP 連線完成。下一段 WU-MD-012c 先寫物料專屬換算 ADR。

- 2026-10-12 WU-MD-012a（物料主檔後端）：新增 `md_item`（REQ_DS43.113）及 `MasterData_Item` migration，已套用本機 `myerp`。物料代碼公司內永久唯一、不可修改；名稱必填、規格可空；基本單位必填，品牌及分類可空。新選參照須為本公司啟用資料，既有參照停用後可保留；三條 `(company_id, id)` 複合外鍵阻止跨公司關聯。單位／品牌／分類被物料引用時不可刪除，回各自 `IN_USE` 409；三語錯誤訊息已同步。`/api/v1/md/items` 提供 GET 分頁搜尋、POST、PUT 帶版本，權限 `MD.ITEM.VIEW/CREATE/EDIT`；期初庫存留 INV。後端建置 0 警告、全量 628/628（首次並行 21 個 SQL 逾時；限制並行度後 1 個既有測試逾時，單獨通過；再重跑 628/628），補充物料邊界斷言後單獨 2/2；前端 463/463。E2E 全量首次 80 通過／1 失敗／3 略過，失敗為單位 IN_USE 新文案與舊斷言不同；更新斷言後該檔 2/2 通過。docgen 0 Error／48 Warning（新增 3 個物料介面尚待 012b）。下一段 012b 做三語頁面、選單、E2E 與介面追溯；012c 做物料專屬換算。

- 2026-10-12 WU-MD-011（物料品牌與分類）：第二段完成前端。品牌頁 `/md/item-brands` 直接用共用 `CategoryList`（`ITEM_BRAND_PAGE`），與其他分類頁行為相同。分類頁 `/md/item-categories`（`ItemCategoryPage`）一次載入公司全部分類，在前端組成樹：上層在前、同層按代碼排序，代碼按層級縮排並帶 `aria-level`，上層不存在的節點當作最上層。搜尋在前端過濾，保留命中節點的上層。啟用且未到第 5 層的列可「新增子分類」，開啟時預選上層。上層下拉只列啟用分類（編輯時保留目前的停用上層），排除自己與子孫，也排除會讓子樹超過 5 層的節點；後端的 `INVALID_PARENT`／`CYCLE`／`TOO_DEEP`／`HAS_CHILDREN`／`DUPLICATE_CODE` 都有三語訊息。新增兩個選單項目，介面 8.36–8.41（REQ_F4118.10.1–4、11.1–4）寫入 ui-spec，docgen 的 8 個「尚無介面」Warning 已消除。後端未改動，仍 626/626；前端 463/463（新增 11 個）、E2E 81 通過 3 略過（新增 4 個）；docgen 0 Error／45 Warning。

- 2026-10-12 WU-MD-010（計量單位與換算）：新增 `md_uom`（REQ_DS43.109，BlueWhale Unit REQ_DS43.85）與 `md_uom_conversion`（REQ_DS43.110），migration `MasterData_Uom` 已套用本機 `myerp`。單位沿用分類規則（代碼公司內永久唯一、軟刪除、版本），另有小數位數 0–6；被換算引用的單位不可刪除（409 `MD.UOM.IN_USE`）。換算為「1 來源 = 係數 目標」，係數 decimal(15,6)、正數、最多 9 位整數及 6 位小數；兩端須為本公司啟用單位且不同；兩單位已可直接、反向或經其他單位換算時回 409 `MD.UOM_CONVERSION.PATH_EXISTS`，所以換算結果唯一；並行新增以公司級 `sp_getapplock` 序列化，另有過濾唯一索引與兩條同公司複合外鍵。換算全部以 decimal 計算，分子分母分開累乘，按目標單位小數位數遠離零捨入；無路徑回 422 `MD.UOM_CONVERSION.NO_PATH`。其他模組經新的 `IUomConverter` 契約換算。API `/api/v1/md/uoms`、`/api/v1/md/uom-conversions`（含 `convert` 試算），權限 `MD.UOM.VIEW/CREATE/EDIT/DELETE`；三語 `/md/uoms` 頁（單位列表、換算表、試算）與選單項目（介面 8.45–8.47，REQ_F4118.13.1–4）。`MasterCategoryService` 新增受保護的 `CreateCoreAsync`／`UpdateCoreAsync` 供單位加欄位，分類行為不變。係數上限取 15 位有效數字，令前端 JSON 數字可無損往返（ADR-034）。後端 617/617（新增 5 個整合測試）、前端 452/452（新增 5 個）、E2E 77 通過 3 略過（新增 2 個），建置 0 警告；docgen 0 Error／45 Warning。物料專屬換算留給 WU-MD-012。

- 2026-10-12 WU-MD-007（供應商主檔）：新增 `md_supplier`（REQ_DS43.108）及 `MasterData_Supplier` migration，已套用本機 `myerp`。規則與客戶相同：代碼公司內永久唯一、不可修改；名稱非空白，最多 200 字；分類可不填，新選時必須是本公司啟用的供應商分類，已停用的原分類可保留。供應商以 `(company_id, category_id)` 複合 FK 參照 `ak_md_supplier_category_company_id_id`，跨公司分類在資料庫層就會被拒絕；分類被供應商引用時不可刪除（409 `MD.SUPPLIER_CATEGORY.IN_USE`）。`/api/v1/md/suppliers` 提供 GET（分頁、搜尋、代碼／名稱排序）、POST、PUT（帶版本），權限 `MD.SUPPLIER.VIEW/CREATE/EDIT`；三語 `/md/suppliers` 頁與選單項目。客戶與供應商抽出共用的 `BusinessPartner<TCategory>`、`BusinessPartnerService<TPartner,TCategory>`、EF `BusinessPartnerModel`，前端抽出 `BusinessPartnerService` 與 `PartnerList`（`PARTNER_PAGE`）；客戶 migration 無差異，客戶原有測試全部照舊通過。後端 612/612（新增 3 個整合測試）、前端 447/447（新增 2 個）、E2E 75 通過 3 略過（新增 2 個），建置 0 警告；docgen 0 Error／45 Warning。供應商核准／駁回／刪除（REQ_F4118.2.4–6）列入新增的 WU-MD-024。

- 2026-10-12 WU-MD-006（供應商分類）：新增 `md_supplier_category`（REQ_DS43.107，BlueWhale VendorType）及 `MasterData_SupplierCategory` migration，已套用本機 `myerp`。規則與客戶分類相同：代碼公司內永久唯一、軟刪除後保留、修改及刪除帶版本。另建唯一約束 `(company_id, id)`，讓 WU-MD-007 的供應商以複合 FK 參照。`/api/v1/md/supplier-categories` 提供 GET／POST／PUT／DELETE，權限 `MD.SUPPLIER_CATEGORY.VIEW/CREATE/EDIT/DELETE`；三語 `/md/supplier-categories` 頁與主檔選單項目（介面 8.33–8.35，REQ_F4118.9.1–4）。為避免照抄，後端抽出 `MasterCategory`／`MasterCategoryService<T>`／`MasterCategoryModel`，前端抽出 `MasterCategoryService` 與共用 `CategoryList`（路由以 `CATEGORY_PAGE` 設定）；客戶分類 schema 不變，原測試不改即通過。修正測試用 i18n fixture 沒有登記 `md` 語言檔，md 三語 key 現在也受一致性檢查。後端 609/609（新增 4 個整合測試）、前端 445/445（新增 8 個）、E2E 73 通過 3 略過（新增 2 個），建置 0 警告；docgen 0 Error／45 Warning。供應商引用檢查（`MD.SUPPLIER_CATEGORY.IN_USE`）在 WU-MD-007 加入。

- 2026-10-12 WU-MD-003（客戶主檔）：接手上一輪 codex 中斷時未提交的 37 個變更，驗證後補上客戶頁元件測試並完成。新增 `md_customer`（REQ_DS43.106）及 `MasterData_Customer` migration，已套用本機 `myerp`。客戶代碼在公司內永久唯一、不可修改；名稱非空白，最多 200 字。分類可以不填；新選分類時，必須是本公司啟用中的分類，已停用的原分類可以保留。分類加了備用鍵 `(company_id, id)`，客戶以複合 FK 參照，跨公司的分類在資料庫層就會被拒絕。分類已被客戶引用時不可刪除（409 `MD.CUSTOMER_CATEGORY.IN_USE`）。`/api/v1/md/customers` 提供 GET（分頁、搜尋、代碼／名稱排序）、POST、PUT（帶版本，可修改名稱、分類、啟用狀態），權限為 `MD.CUSTOMER.VIEW/CREATE/EDIT`。三語 `/md/customers` 頁有主檔選單項目，用對話框新增和編輯；沒有分類查看權限時不載入分類。後端 605/605、前端 437/437（新增 4 個）、E2E 71 通過 3 略過，建置 0 警告；docgen 0 Error／45 Warning。核准／駁回、刪除、匯入（REQ_F4118.1.4–7）留給後續工作單元。

- 2026-10-12 WU-MD-002（客戶分類）：新增 `md_customer_category`（REQ_DS43.105）及 `MasterData_CustomerCategory` migration，已套用本機 `myerp`；代碼公司內唯一且軟刪除後保留，列表按公司與軟刪除篩選，修改及刪除帶版本。`/api/v1/md/customer-categories` 提供分頁查詢、建立、修改、軟刪除，四個動作各有 MD 權限；三語 `/md/customer-categories` 頁含搜尋、排序、分頁、表單驗證、二次刪除確認與亮／暗模式。後端 603/603、前端 433/433、E2E 69 通過 3 略過，建置 0 警告；docgen 連 `myerp` 0 Error／45 Warning（未完成模組文件與既有追溯）。補雙 DbContext 並發更新測試；客戶引用刪除檢查在 WU-MD-003 建立客戶關聯時加入。

- 2026-10-12 WU-MD-001（MD 模組基線）：建立 `MyERP.Modules.MasterData` 及 Contracts，註冊於 API 與解決方案；宣告 `MD.MASTER.VIEW`，前端新增三語 `/md` lazy route 與權限守衛，無資料表或公開資料 API。補 MD 需求、資料結構、API、UI、業務規則、測試與優化記錄。新增架構測試與兩個 E2E，後端建置 0 警告、完整後端 600/600、前端 432/432、E2E 67 通過 3 略過。首輪並行回歸遇 SQL 連線逾時；單獨重跑完整後端全過。Vitest 限為 2 個 worker，以避免 VM 記憶體不足。

- 2026-10-11 WU-SYS-016d（SYS 完整回歸與模組收尾）：後端建置 0 警告、599 / 599；前端建置與 432 / 432；模擬 API E2E 65 通過、3 個真實 API 案例依設定略過；真實 API E2E 3 / 3；docgen 連 `myerp` 產生 SRS／SDS／需求表，0 Error／44 Warning（未完成模組的敘述來源與既有追溯缺口）。首次全套後端有 16 個偶發 SQL 逾時，連續三次重跑 599 / 599；排查與後續 WU-P0-014 見 log-analysis。BlueWhale 對照表的 SYS 列改為實際需求、介面與資料表編號；沒有 BLOCKED 項目；EP-MD 拆為 WU-MD-001～022。SYS 已完成，tag `v0.1.0-sys`。
- 2026-10-11 WU-SYS-016c（審查修正：重複代碼、迴圈內查詢與前端細節）：後端新增 `SystemQueries`，集中目前公司檢查（原 4 個服務各一份）、批次查用戶名稱（原 2 份）和代碼／名稱排序（原 6 個 switch）（B10）。審批流程的步驟驗證改為先檢查格式，再用兩次查詢確認角色與用戶，錯誤仍指向第一個無效步驟；部門的上層檢查改為一次載入上層關係，在記憶體檢查循環（B09）。前端新增共用 `displayTime`，時間一律顯示到分鐘，只有稽核紀錄到秒（F06）。通知頁只在公司 ID 改變時重新載入，翻頁和篩選不再重算未讀數（F04）。通知類別、流程步驟摘要和審批人改用三語參數化 key，英文介面不再出現全形標點（F08）。`PagedResult` 只在一處宣告，10 個列表使用共用的每頁筆數選項（F07）。移除 8 個檔案寫死淺色的 CSS 後備值（F09）。角色頁的模組勾選狀態和流程列表的審批人名稱改為 computed（F10）。代理頁的「今天」在每次載入時更新，過了午夜不會過期（F11）。F01 部分修正：重複的分頁和排序邏輯改用共用 helper；不全面改用列表範本，理由見 code-review-log。測試：後端 599 / 599、前端 432 / 432、E2E 65 / 65（3 個真實 API 略過），建置 0 警告。
- 2026-10-11 WU-SYS-016b（審查修正：全系統資源授權與列表競態）：修正 SYS-B03。角色與 IP 白名單是全系統資源，之前任何公司的管理員都能修改；有 `SYS.ROLE.EDIT` 的非全權用戶也能替自己的角色加權限；新增系統層級白名單會鎖住其他公司。現在按影響範圍檢查（ADR-033）：修改角色時，操作者在角色已指派的每間公司都必須全權；白名單規則按層級計算受影響公司（系統＝全部公司、角色＝已指派的公司、用戶＝所屬公司），否則 403 `SYS.AUTHORITY.FULL_ACCESS_REQUIRED`，並加入三語訊息。前端新增共用 `latestRequest()`：新查詢取消舊請求，元件銷毀時也取消，已套用到 8 個列表頁（F02）。部門頁的選項改為只在切換公司、切換頁面類型或儲存後載入（F03）。IP 白名單頁補上防重複停用、清除操作錯誤、錯誤時總數歸零，並把擠在一行的語句拆開（F05、F12）。測試：後端 595 / 595、前端 430 / 430、E2E 65 / 65（3 個真實 API 略過），建置 0 警告。B09、B10、F01、F04、F06–F11 移到 WU-SYS-016c，完整回歸與 tag 改為 016d。

- 2026-10-11 WU-SYS-016a（SYS 模組代碼審查與後端安全修正）：後端與前端各由唯讀子代理審查，主代理逐項核對後寫入 `code-review-log.md`（後端 13 項、前端 12 項）；日誌分析寫入 `log-analysis.md`，沒有非預期的重複錯誤，也沒有慢請求。新增 `AuthorityGuard`（ADR-032），修正三個問題：跨公司動作只檢查目前公司權限（B01）、可指派全權角色或改自己角色（B02）、可接管同屬他公司的用戶（B04）。另修正角色建立未包交易（B05）、停用／鎖定帳號的時間差（B07）、通知清理首次延遲（B08）、改密碼失敗沒有安全稽核（B12）、收件人無效回 403（B13，改 422）；B06、B11 有理據不修改。新增 SYS-PERM-028～030。前端加入兩個新錯誤代碼與三語訊息。測試：後端 593 / 593、前端 426 / 426、E2E 65 / 65（模擬，3 個真實 API 略過）。016 已拆為 016a–c：016b 處理其餘發現，016c 做完整回歸與 tag。

- 2026-10-11 WU-SYS-012b 站內通知收件箱完成（REQ_F4118.33.2–3、介面 8.76）：接手上一輪 codex 未提交的 34 個變更並驗證。`/api/v1/sys/notifications` 提供分頁列表（新到舊、可篩未讀）、未讀數、單筆與全部已讀（`SYS.NOTIFICATION.VIEW／READ`），只處理本人、目前公司、未到期通知；他人／別公司 404 `SYS.NOTIFICATION.NOT_FOUND`。`ExpiredNotificationCleanup` 每日跨公司刪除過期通知。前端頂列鈴鐺徽章（超過 99 顯示 99+、只採最後一次未讀數回應）與三語收件箱 `/sys/notifications`。本輪補上：示範種子各業務角色加入本人通知權限（否則非管理員看不到）、HTTP 整合測試 SYS-NOTIFY-006、前端單元 SYS-NOTIFY-007（6 個）；修正審批代理 E2E 連選同名用戶時的下拉選單 strict mode 偶發失敗。後端 590 / 590、前端 426 / 426、E2E 65 / 65（另 3 個真實 API 案例依設定略過）、docgen 0 Error／44 Warning。

- 2026-10-11 WU-SYS-012a 站內通知發布核心完成（REQ_F4118.33.1、REQ_DS43.104、ADR-031）：新增 `sys_notification` 與 `System_Notification` migration（已套用 `myerp`），跨模組使用 `INotificationPublisher`；只向目前啟用公司中的啟用用戶發布，文字與站內路徑驗證，同公司、收件人、來源鍵唯一，並發重試讀回同一 Id。初始未讀、90 天到期，審計與版本欄位由共用核心管理。SYS-NOTIFY-001～003 三個整合測試通過；後端 588 / 588（整合 188、其他 400，分批），前端 420 / 420，E2E 64 / 64（另 3 個真實 API 案例略過），docgen 0 Error／46 Warning（含收件箱 012b 尚無介面的 3 項）。下一輪完成 012b 收件箱 API、已讀與三語頁面。

- 2026-10-11 WU-SYS-011b 首頁、修改密碼及個人設定介面完成（REQ_F4111.1.2 v1.1、.4–5；ADR-030，介面 1.2–1.4）。首頁顯示登入帳號、目前公司與自助入口，現有圖表仍標明示範。登入遇 `PASSWORD_EXPIRED` 顯示匿名修改入口，帳密與一次性 Captcha 修改成功即保存新 Session；本人修改成功替換 Access／Refresh Token，失敗清空密碼、到期流程重新取 Captcha。個人設定頁保存三語與亮／暗／跟隨系統主題於此瀏覽器，重整後沿用；不提供跨裝置同步。前端 420 / 420（新增 4 個）、E2E 64 / 64（新增 3 個；另 3 個真實 API 案例依設定略過）、後端建置 0 警告。全套後端首輪 569 / 585，16 個分散整合測試遇 SQL 逾時；單線程重跑整合測試 185 / 185，全套單元 400 / 400 均通過。docgen 0 Error／43 Warning（新首頁、偏好沒有資料表，另有舊追溯缺口）；輸出已重生。下一輪 WU-SYS-012 站內通知。

- 2026-10-11 WU-SYS-011a 修改密碼後端完成（REQ_F4111.1.2，ADR-029）：本人以舊密碼修改，密碼到期時以帳密與 Captcha 修改；沿用密碼政策、五次錯誤鎖定與 IP 白名單。修改與安全稽核同次儲存，之前簽發的 Refresh Token 以 `password_changed_at` 判斷撤銷，回傳目前工作階段新 Token。新增 SYS-AUTH-018～022 五個整合測試，針對測試 5 / 5、建置 0 警告。全套後端首輪架構清單漏列新自助端點（已修正），第二輪並行測試觸發 SQL 逾時／連線池耗盡；限制模組並行後 585 / 585 通過。前端 416 / 416；模擬 API E2E 60 / 61，既有公司圖片案例單獨重跑通過；docgen 0 Error／42 Warning（修改密碼介面待 011b）。本輪沒有前端程式變更。011b 待續。

（由 AI 追加，最新在上）

- 2026-10-11 WU-SYS-010d Logo 與印章完成（ADR-028，REQ_F4118.19.1–3，介面 8.57；WU-SYS-010 整體完成）：新增 `sys_company_image`（REQ_DS43.103，migration `System_CompanyImage` 已套用 `myerp`），每間公司 Logo、印章各一張，內容存資料庫 `varbinary(max)`，有類型、尺寸、大小與 DATALENGTH 檢查約束。上傳時以檔案內容判斷類型：只接受 PNG（IHDR／IEND）與 JPEG（SOI／SOF／EOI），不看副檔名或宣告的 Content-Type，SVG 等一律拒絕；1 MB、寬高 16–2048 像素；檔名只保留檔名部分。API `/api/v1/sys/company-images`：清單（不含內容）、`{kind}/content`（inline、nosniff、ETag 版本號，`If-None-Match` 只查版本即回 304）、multipart 上傳／替換與刪除都帶 version（409），422 專用錯誤代碼；權限沿用 `SYS.PARAMETER.VIEW／EDIT`；稽核只記錄位元組數。Contracts 新增 `ICompanyImageReader`，供列印模組讀取。三語 `/sys/logo-stamp`（選單「Logo 與印章」）：兩張卡片預覽（Blob 轉 data URL，前端 CSP 不變）、檔案資料、上傳／替換、確認刪除、用戶端先檢查類型與大小、唯讀。後端 580 / 580（新增 13 個整合測試案例 SYS-IMG-001～006）、前端 416 / 416（新增 10 個 SYS-IMG-007）、E2E 61 / 61（新增 SYS-IMG-008）；docgen 0 Error／42 Warning（未提交無 DB 的輸出）。偶發：後端與前端測試同時執行時，7 個整合測試 SQL 逾時，前端稽核頁測試逾時 15 秒；分開執行全部通過。E2E 全量執行時 approvals.spec 的審批代理測試一次失敗（下拉選單關閉動畫未完成，出現兩個相同選項），單獨重跑通過，與本次變更無關。

- 2026-10-11 WU-SYS-010c 列印設定完成（ADR-027，REQ_F4118.18.1–3，介面 8.56）：列印設定沿用系統參數，不開專表。SYS 在 `print` 分組宣告 9 個參數：列印公司名稱（100）、地址（200）、郵遞區號（10）、電話／傳真（30）、採購單／銷售出貨單頁尾備註（500，可多行）、是否列印 Logo／印章（預設否）；保存、版本、批次驗證與稽核沿用 `/api/v1/sys/parameters`，權限沿用 `SYS.PARAMETER.VIEW／EDIT`，沒有 migration。`MyERP.Modules.System.Contracts` 新增 `IPrintSettingsReader`／`PrintSettings`，讓 PUR／SD 列印時讀取，公司名稱空白時用公司主檔名稱。三語 `/sys/print-settings`（選單「列印設定」）重用 `ParameterSettings`（路由資料 `view: 'print'`），只顯示列印分組並依草稿即時預覽抬頭；`/sys/parameters` 不再顯示列印分組；長度超過 100 的文字參數改用多行輸入框。後端 567 / 567（新增 3 個整合測試 SYS-PRINT-001～003）、前端 406 / 406（新增 4 個）、E2E 60 / 60（新增 SYS-PRINT-004）；docgen 0 Error／41 Warning（未提交無 DB 的輸出）。

- 2026-10-11 WU-SYS-010b 系統參數頁完成（介面 8.55，REQ_F4118.17.4）：`/sys/parameters`（`permissionGuard('SYS.PARAMETER.VIEW')`、`unsavedChangesGuard`、系統管理選單）。參數依模組、分組以卡片顯示，模組／分組／參數名稱三語（`sys.parameters.keys.<參數鍵>`）。依型別產生欄位：布林開關、整數／十進位文字輸入（數字不經 float 保存，範圍只作提示）、文字（長度上限）、選項下拉；顯示預設值、範圍與「恢復預設」。只送出有修改的項目並帶各自 `version`，用戶端先檢查型別與範圍；伺服器 400 依 `items[i]` 標示欄位，409 提示並可重新載入。沒有 EDIT 權限時唯讀；切換公司重新查詢並丟棄草稿；有未保存修改時離開需確認。檢查與優化：只差前後空白不算修改、文字長度改用去空白後計算（與伺服器一致）。前端 402 / 402（新增 10）、E2E 59 / 59（新增 SYS-PARAM-005/006）、後端 564 / 564 未變；docgen 無 DB 0 Error（未提交降級輸出）。

- 2026-10-11 WU-SYS-010a 系統參數後端完成（ADR-026，REQ_F4118.17.1–3，REQ_DS43.73）。
  - 拆分：WU-SYS-010（系統參數、列印設定、Logo 與印章）拆為 010a 參數後端、010b 參數頁、010c 列印設定、010d Logo 與印章。上幾輪 codex 異常結束是因為額度用完（runner 輸出 `out of credits`），不是工作單元本身失敗，所以不計入失敗次數。
  - 參照 BlueWhale `SystemSet`：舊表以固定欄位混放列印抬頭、審批開關、負庫存開關、自訂欄位名稱與第三方金鑰，沒有範圍與權限檢查。MyERP 改為各模組以 `IParameterDefinitionProvider` 宣告有型別的參數（Boolean／WholeNumber／Number／Text／Option，預設值與範圍），SYS 以 `ParameterCatalog` 彙整，啟動時檢查鍵格式、模組前綴與重複。
  - `sys_parameter`（migration `System_Parameter`，已套用 `myerp`）按公司保存，只存修改過的值，`(company_id, param_key)` 唯一。API：`GET /api/v1/sys/parameters`（`SYS.PARAMETER.VIEW`）、`PUT` 批次保存（`SYS.PARAMETER.EDIT`，同一交易，錯誤一次列出 `items[i].*`，版本 409，未改變的值不寫入）。各模組在進程內用 `ISystemParameters` 讀取，同一請求只查一次；舊值不合目前規則時用預設值。SYS 宣告數量小數位數（0–4，預設 2）與單價小數位數（0–6，預設 4）；負庫存、自訂欄位名稱等由 INV／MD 開發時自行宣告。
  - 測試：後端 564 / 564（新增 42：參數宣告單元 37 個案例、目錄 2、API／讀取整合 3），建置 0 警告；前端與 E2E 沒有改動。docgen 無資料庫連線 0 Error／42 Warning（新增 3 個 TRACE_GAP，因為參數頁介面要到 010b 才做），沒有提交降級的產物。

- 2026-10-11 WU-SYS-006 IP 白名單完成（第二段，ADR-025，介面 8.75，REQ_IO_A8.75.1–7）。三語 `/sys/ip-whitelist` 用 `erp-list-page` 與 `table-query` 做伺服器端列表、搜尋、排序、分頁；依權限建立／編輯（層級、對象與網段建立後唯讀）、停用前用共用確認框，錯誤代碼與登入 IP 拒絕有三語提示。整合測試以真實登入與 Refresh 驗證 admin 角色在非允許 IP 無法取得 Token，且只有設定中的可信代理能提供來源 IP。檢查快取發現載入與失效併發時可回傳舊快照，已同步發布並在世代變更時重載；測試涵蓋立即失效、30 秒到期與載入中失效。後端 522 / 522；前端 392 / 392；模擬 API E2E 57 / 57（另 3 個真實 API 測試在一般執行時略過）。docgen 無資料庫連線 0 Error／39 Warning；連 myerp 多次遇 SQL Server 逾時，myerp_test 有兩張測試專用表造成 2 Error，故未提交降級產物。資料表已於第一段套用 migration、後端整合測試通過。下一個工作單元 WU-SYS-010。

- 2026-10-11 WU-SYS-014 共用介面完成（ADR-024，REQ_F4118.31.1–6，介面 8.71–8.74）。
  - `ErpDialogService`（`shared/dialogs/`）：確認框 `confirm()`（Esc／關閉視為取消，危險動作紅色按鈕）、提示框 `alert()`、成功提示 `success()`（3 秒）、選擇視窗 `select()`（關鍵字、伺服器端分頁、單選按兩下確定、多選跨頁保留、只採用最後一次查詢）。內容元件自帶按鈕與 `data-testid`；標題放在內容內，切換語言即時更新。服務自建 NzModalService，頁面不必另外提供。
  - 範本：`erp-list-page`（標題／動作、篩選＋查詢／重設、批次操作列與已選筆數、錯誤重試）與 `table-query.ts`；`erp-document-page`（返回、單號／新增、狀態、未儲存、上一筆／下一筆、409 重新載入、422 錯誤、頁籤插槽）、`adjacentIds()`、`unsavedChangesGuard`；`erp-date-range`（今天、本週、本月、上月、本季、本年）；`erp-status-tag`（七種單據狀態，亮／暗 token）。
  - 角色頁改用列表範本、`table-query` 與共用確認框，取代 popconfirm；既有測試全數保留，E2E 改為操作確認框並加「取消不送出」。範本輸出原名 search／reset 會收到投影輸入框冒泡的原生 search 事件，已改名（有測試）。jsdom 不發 animationend，nz-modal 關閉不會完成，測試提供 NoopAnimations。
  - 測試：前端 389 / 389（新增 20）、E2E 56 passed + 3 skipped、後端 488 / 488，建置 0 警告；docgen 0 Error（新增 6 個 TRACE_GAP Warning，因為共用介面沒有資料表）。前端覆蓋率：dialogs 97%、list-page 100%、document-page 96%、date-range 98.5%。
  - 未做：其他 SYS 列表頁之後修改時再遷移；選擇視窗與單據頁範本待第一個主檔／單據頁採用時補 E2E。

- 2026-10-11 WU-SYS-013 種子資料完成（ADR-023，REQ_F4118.30.1–3）。
  - 執行方式：`dotnet run --project src/backend/MyERP.Api -- --seed`，載入後即結束。共用 `IDataSeeder`／`DataSeedRunner` 依 Order 執行各模組種子；非 Development 環境寫 Error 並拒絕。不在啟動時自動執行，整合測試不受影響。
  - SYS 種子（同一交易、冪等）：TW01（TPE／TXG／KHH）、HK01（HKG／KLN），每間公司 7 個部門。9 個角色：ADMIN 全權，其他依規則對照權限目錄展開（例如 FINANCE = FI、CO 全部 + 全部 VIEW），新模組的權限在重新執行時補上。帳號來自 `appsettings.Development.json` 的 `SeedUsers`（MASTER_PROMPT 第 12 節），密碼雜湊；示範密碼刻意不套用密碼政策。每個帳號可存取兩間公司，預設 TW01。第二次執行新增 0 筆，不覆蓋改過的密碼，也不移除額外加入的權限。本機 `myerp` 已載入 117 筆。
  - 真實 API E2E（SYS-AUTH-017）：`serve-dist.mjs` 在設定 `E2E_API_URL` 時把 `/api/` 轉送到後端（同源，CSP 不變）；`tools/scripts/Invoke-RealApiE2E.ps1` 依序 migration → 種子 → 啟動 API → 執行 `specs/real-api-*`。3 個測試：admin 登入並在兩間公司看到各自分店、登出；viewer 沒有新增按鈕；不存在的帳號顯示錯誤。
  - 真實 API E2E 發現缺陷：在列表頁登出時，先清除公司才導向登入頁，列表頁因此用空 Session 重新查詢而得到 401。已改為先導向再清除（新增前端單元測試）。
  - 測試：後端 488 / 488（新增整合 7、單元 9）、前端 369 / 369（新增 1）、E2E 56 passed + 3 skipped（真實 API 測試在一般執行時略過），真實 API E2E 3 / 3 連續兩次通過；建置 0 警告；docgen 0 Error（新增 3 個 TRACE_GAP Warning，原因是種子沒有介面）。前端整體執行時 roles.spec 的第一個測試偶發逾時（16.7 秒，單獨重跑與整體重跑都通過），屬 ADR-020 的冷啟動問題。
  - 未做：`sys_company` 沒有本位幣欄位，TWD／HKD 待 MD／FI 建立；示範審批流程與單號規則要有單據類型，留給各業務模組的種子。

- 2026-10-11 WU-SYS-009 審批流程引擎完成（第二段：前端）：新增 `ApprovalService` 與三語頁面。`/sys/approval-flows`（介面 8.68）列出各流程的步驟、審批人與金額門檻；對話框可增刪、排序 1–10 個步驟，每步選角色或用戶審批人並可設門檻（前端限 11 位整數、4 位小數，避免 JSON 數值失真），只送出所選類型的審批人。`/sys/approval-delegations`（介面 8.69）可依用戶與有效日期篩選並標示「生效中」，新增時檢查不可同一人、日期不可倒置。`/sys/approvals`（介面 8.70）分「我的待辦」（PROCESS）與「審批歷史」（VIEW）兩個頁籤：待辦顯示第幾步與「代 某人」，核准／駁回帶版本，駁回必填意見；明細列出各步驟的處理人、被代理人、時間與意見。選單項目改為可接受多個權限（任一即顯示）。後端待辦加上 `onBehalfOfUsername`。稽核頁加入 REJECT 標籤。E2E 發現明細對話框標題缺少單號（nz-modal 開啟後不更新標題），已修正。後端 472 / 472、前端 368 / 368（新增 19）、E2E 56 / 56（新增 6），建置 0 警告，docgen 0 Error。

- 2026-10-11 WU-SYS-008 單號規則完成（第二段：前端）：新增 `NumberRuleService` 與三語 `/sys/number-rules` 頁（介面 8.67，REQ_F4118.28.6），以 `permissionGuard('SYS.NUMBER_RULE.VIEW')` 保護，系統管理選單加「單號規則」。nz-table 伺服器端分頁、單據類型／名稱排序與搜尋；列出後端樣式（今天第 1 號）、日期格式、重設週期、啟用與「已發號」標籤。新增／編輯對話框：單據類型編輯時唯讀；前端即時組合樣式（`formatNumber`，六種日期格式與後端一致）；重設週期不在日期格式內時即時提示且不可保存；已發號的規則鎖定日期格式與重設週期、流水號長度下限改為原長度；長度須為整數（檢查與優化時補上，原生數字欄位可輸入小數）。預覽對話框依業務日期呼叫 preview API，較早或關閉後才回來的回應不套用。按鈕以 `*erpHasPermission` 控制（預覽只需 VIEW）；重複類型、找不到、已發號、流水號用盡、版本衝突以三語顯示；切換公司後重新查詢並關閉預覽。`isoDate` 移到 `shared/date.ts` 共用。新增前端單元 14 個（SYS-NUM-010）、E2E 5 個（SYS-NUM-011）；後端 462 / 462、前端 349 / 349、E2E 50 / 50，建置 0 警告，docgen 0 Error。WU-SYS-008 完成。

- 2026-10-11 WU-SYS-007 稽核日誌完成（第二段：前端）。
  - 三語 `/sys/audit-logs` 頁（介面 8.66，REQ_F4118.27.6）：`permissionGuard('SYS.AUDIT.VIEW')`，系統管理選單「稽核日誌」。篩選有日期範圍與快選（今天、近 7、30 天；結束日早於開始日時不送出）、類別、操作、資料表、記錄 ID；伺服器端分頁，依時間排序（預設新到舊）。
  - 展開列顯示欄位差異（修改前→修改後，`***` 照原樣，空值顯示「（空）」）或安全事件明細，並顯示 TraceId。操作與類別有三語標籤，未知操作顯示原代碼。
  - 單據修改歷史：`?tableName=&recordId=[&userId=]` 由 `withComponentInputBinding` 帶入。列上的資料表／記錄連結可切換到該記錄的歷史；「重設」會清除網址參數（ADR-020）。
  - 測試基礎設施：前端整體執行時，每次都有一個冷啟動的測試超過 Vitest 預設的 5 秒，同 worker 之後的測試檔連帶失敗。新增 `vitest-base.config.mts`，`testTimeout` 改為 15 秒（ADR-020），斷言不變。
  - 測試：前端 335 / 335（新增 13 個）、E2E 45 / 45（新增 4 個）、後端 453 / 453（未改動）。SYS-AUDIT-014 通過。

- 2026-10-11 WU-SYS-005 安全中介層完成。後端新增 `AddMyErpWebSecurity`，涵蓋以下各項：
  - ForwardedHeaders：只信任 `Security:ForwardedHeaders` 列明的 Proxy；沒有設定時不啟用，避免偽造 X-Forwarded-For。
  - 所有回應加安全標頭（nosniff、DENY、no-referrer、API CSP）；非 Development 啟用 HSTS 與 HTTP 轉 HTTPS。
  - CORS 只允許設定的來源，不允許 Credentials；設定錯誤即拒絕啟動。
  - 限流：login／refresh 每 IP 每端點每分鐘 5 次，captcha 10 次，已登入每用戶 300 次，另預留匯出政策。超過回 429 ProblemDetails（`COMMON.REQUEST.RATE_LIMITED`、Retry-After）。ArchitectureTests 檢查匿名端點都有按 IP 的限流。
  - 前端抽出共用 `apiErrorKey`，429 顯示三語「操作過於頻繁」。E2E 靜態伺服器套用建議的前端 CSP，為此關閉 critical CSS 內嵌（否則 37 個 E2E 失敗）。
  - Token 維持 Bearer + localStorage，不改 HttpOnly Cookie，理由見 ADR-018。
  - 測試：後端 440 / 440（新增 18：整合 16、架構 1、單元 1）、前端 322 / 322、E2E 41 / 41。docgen 0 Error；新增的 7 個 TRACE_GAP Warning 是因為中介層需求沒有資料表與介面。

- 2026-10-11 WU-SYS-015 角色維護頁與用戶權限頁完成。
  - 安全修正（測試先行，ADR-017）：`PUT /api/v1/sys/users/{userId}/companies/{companyId}` 的目標用戶必須屬於目前公司，否則回 404 `SYS.USER.NOT_FOUND`；目標公司必須是操作者獲授權的公司，否則回 404 `SYS.COMPANY.NOT_FOUND`。修正前，公司管理員可以接管他公司的帳號，或在未獲授權的公司授予全權角色。另外，不可停用自己的公司授權（422）。
  - 新 API：`GET /api/v1/sys/users/{userId}/companies`（`SYS.USER.VIEW`，REQ_F4118.25.8），只列出操作者可管理公司的指派，固定 3 次查詢。
  - 角色頁：三語 `/sys/roles`（介面 8.64，系統管理選單「角色」）。伺服器端分頁、代碼／名稱排序與搜尋；顯示資料範圍與全權標籤。新增／編輯對話框可依模組分組勾選權限（模組可全選，部分勾選時呈半選），可設資料範圍、全權與啟用。刪除用 popconfirm 確認並帶版本；角色使用中時顯示 IN_USE。
  - 用戶權限對話框（介面 8.65，BlueWhale 4.4.1.8.26）：從用戶列「權限」按鈕開啟（`SYS.USER.EDIT`）。公司只能選操作者可切換的公司；分店／部門以所選公司的 `X-Company-Id` 查詢，查不到（403）時給提示並可留空。可多選角色，設預設公司與啟用。
  - 測試：後端 422 / 422（新增 2 個整合測試，修正 1 個既有測試的前置資料）、前端 312 / 312（新增 17 個）、E2E 40 / 40（新增 7 個，連續兩次通過），建置 0 警告。

- 2026-10-11 WU-SYS-002 用戶、密碼政策與帳號鎖定完成（第三段：前端）：新增 `UserService` 與三語 `/sys/users` 頁（介面 8.61、8.63；`permissionGuard('SYS.USER.VIEW')`、系統管理選單「用戶」）。nz-table 伺服器端分頁、帳號排序與搜尋；狀態標籤（啟用／停用、已鎖定、密碼已過期）及密碼修改時間（伺服器 UTC+8 時間）。按鈕依權限顯示：新增（CREATE）、停用／啟用與解鎖（EDIT，解鎖只在已鎖定的列出現）、重設密碼（RESET_PASSWORD）。新增與重設密碼共用對話框，有確認密碼；帳號去除前後空白再檢查；API 失敗後清空密碼欄位；列操作進行中不重複送出。重複帳號、密碼不符政策、不可停用自己、版本衝突、找不到用戶等錯誤以三語顯示，成功時顯示提示並重新查詢；切換公司後重新查詢。補上 REQ_F4118.23.5–8 的資料結構追溯（docgen 由 23 降回 19 個 Warning）。前端 295 / 295（新增 10 個）、E2E 33 / 33（新增 5 個）、後端 420 / 420，建置 0 警告，docgen 0 Error。test-plan SYS-USER-006 通過。

- 2026-10-10 WU-SYS-001 公司／分店／部門完成（第八段）：開放組織 API `/api/v1/sys/companies|branches|departments`（GET 分頁、POST、PUT），以 `SYS.COMPANY/BRANCH/DEPARTMENT.VIEW/CREATE/EDIT` 保護，回應 DTO 不外露審計欄位。公司 API 只涵蓋用戶獲授權的公司：修改其他公司回 404；`includeInactive` 可列出停用公司以便重新啟用。建立公司時在同一交易內授權建立者，並沿用其目前公司的角色（ADR-015）。分店／部門沿用 `X-Company-Id` 公司隔離。前端新增 `OrganizationService` 與 `/sys/companies|branches|departments` 頁面（lazy 路由、`permissionGuard`、系統管理選單）：nz-table 伺服器端分頁與排序、關鍵字搜尋、狀態標籤、對話框新增／編輯（編輯時代碼唯讀、可停用），按鈕以 `*erpHasPermission` 控制；重複代碼、版本衝突等錯誤以三語顯示；切換公司後自動重新查詢。骨架列表的本地篩選測試改為伺服器端查詢測試（見 optimization-log）。後端 414 / 414（新增 4 個整合測試）、前端 285 / 285（新增 7 個）、E2E 28 / 28（新增 4 個），建置 0 警告，docgen 0 Error。

- 2026-10-10 WU-SYS-004 角色、權限與資料範圍完成：新增 `sys_role`、`sys_role_permission`、`sys_user_company`、`sys_user_role`（REQ_DS43.90–93，`System_Authorization` migration 已套用 `myerp`）。角色是全系統定義，按公司指派；`sys_user_role` 以複合外鍵指向用戶公司關聯，沒有公司存取權就不能指派角色；每位用戶最多一間預設公司。權限目錄由各模組以 `IPermissionDefinitionProvider` 宣告，啟動時檢查格式、前綴與重複，不另建權限表。後端以 `[RequirePermission("SYS.ROLE.VIEW")]` 動態政策檢查目前公司權限；已登入請求的 `X-Company-Id` 不可存取時，整個請求回 403 `COMMON.COMPANY.ACCESS_DENIED`（修正偽造標頭可繞過公司篩選的問題）；缺權限回 403 `COMMON.PERMISSION.DENIED`。資料範圍取各角色最寬者，全權角色為全公司，缺少分店／部門歸屬時退回本人；Application 層以 `DataScopeFilter` 套用。新增角色 CRUD、權限目錄、用戶公司授權、本人授權（`/api/v1/sys/auth/authorization`）API；ArchitectureTests 檢查每個 action 都有權限代碼，且代碼已宣告。前端新增 PermissionService（進入主框架前載入權限與公司清單，記住的公司已不可存取時改用預設公司，切換公司重新載入）、`*erpHasPermission`、`permissionGuard`，選單依權限過濾，登出時清除。後端 410 / 410（新增 29）、前端 278 / 278（新增 13）、E2E 24 / 24（新增 3），建置 0 警告；docgen 0 Error（順便修正 ui-spec 介面表頭與測試計畫範圍式引用造成的 13 個舊 Error）。決策見 ADR-014。角色與用戶權限頁面拆至 WU-SYS-015。

- 2026-10-10 WU-SYS-003 登入完成（第二段：前端）：測試先行新增 `AuthService`，Token 與帳號存 `localStorage`（`myerp.auth.session`），壞資料或 Refresh Token 過期即清除。攔截器只為非匿名 API 加 Bearer；收到 401 時刷新一次再重送，並發的 401 共用同一個 refresh（後端輪替後舊 Token 立即失效）。刷新失敗會清除 Session，導向 `/login?returnUrl=…`。`authGuard`／`guestGuard` 保護主框架；`safeReturnUrl` 擋外部、`//`、`/\` 路徑。共用 `erp-captcha`（ControlValueAccessor）只收 4–6 位數字、點擊換圖、載入失敗可重試。三語登入頁依 `errorCode` 顯示錯誤，失敗後清空密碼並換驗證碼，頁底可切換語言。頂列顯示帳號和登出按鈕：登出時撤銷 Refresh Token，伺服器失敗也清除本機 Session。E2E 以 fixture 寫入假 Session，讓既有測試維持已登入；另以 `page.route` 模擬 auth API，新增 5 個登入 E2E（萬用碼登入、重新整理仍保持登入、登出、密碼／驗證碼錯誤、必填、returnUrl、三語）。前端 265 / 265（新增 76 個）、E2E 21 / 21、後端 381 / 381，建置 0 警告。ADR-001 補記本機 Redis 為 Windows 移植版 3.2.100（新增 R-018）。決策見 ADR-013。萬用碼稽核移到 WU-SYS-007，登入限流移到 WU-SYS-005，連真實 API 的 E2E 移到 WU-SYS-013。

- 2026-10-10 WU-P0-013 文件產生器骨架完成：`tools/docgen` 使用 .NET 10 + OpenXML，從模組 Markdown、非功能需求、BlueWhale 對照表讀取來源，產出 SRS / SDS docx、九張工作表的需求 xlsx，以及一致性報告；可選 SQL Server 連線，以 `INFORMATION_SCHEMA` 產 SDS 4.1 欄位表並檢查資料表一致性。文件使用 `reference-notes.md` 的 BlueWhale 字型、欄寬、框線及章節順序；未完成敘述來源以 `[Pending]` 和警告標示。這次實際產出在 `docs/99-output/`，資料庫可連線，報告 0 Error / 5 Warning（後續內容待補）。新增 2 個 docgen 測試並納入 CI；後端 346 個測試通過。使用說明見 `tools/docgen/README.md`，決策見 ADR-010。

- 2026-10-10 WU-P0-012 Playwright 完成：`tests/e2e` 有獨立的 package.json，使用 @playwright/test 1.64.0，瀏覽器只裝 chromium，下載到 `.tools/ms-playwright`（由 `Invoke-Frontend.ps1` 設定 `PLAYWRIGHT_BROWSERS_PATH`）。webServer 先做前端 production 建置，再以 `serve-dist.mjs` 提供靜態檔：缺少的資源回 404，不以 index.html 掩蓋；路徑跳出根目錄時拒絕。共 16 個冒煙測試：首頁三張 G2 圖表加 KPI 迷你趨勢圖都以像素確認確實畫出；切換暗色時 theme-dark.css 回 200、CSSOM 有規則，body 色彩符合設計 token，重新整理後保留，切回亮色時移除；跟隨系統；切換三語時 `<html lang>` 與文字同步，並依瀏覽器語言決定預設語言；404 / 403 頁；靜態伺服器行為。自動 fixture 遇到任何 console error 即判定失敗。E2E 第一次執行就發現暗色模式下 body 背景被執行期插入的 ng-zorro 暗色 bundle 蓋成 `#000`，已改用 `html body` 提高權重修正。E2E 不加入 CI。後端 344、前端 181、E2E 16 個測試全過。說明見 e2e.md、ADR-009。

- 2026-10-10 WU-P0-011 CI 完成：新增 `.github/workflows/ci.yml`，有兩個 job，都跑在 `windows-latest`，權限只有 `contents: read`，不用任何機密。backend job 用 `global.json` 的 SDK 以 Release 建置，再逐一以 `dotnet test --project` 跑 `tests/backend.unit` 下的單元測試專案；不跑整合測試，因為需要 SQL Server。frontend job 用 `.nvmrc` 的 Node，執行 `npm ci`、`ng build`、`ng test --watch=false`（jsdom，不需瀏覽器）。新增 `CiWorkflowTests`（9 個測試），在本機檢查 workflow 結構：每個單元測試專案都要列入 CI、不得引用整合測試、沒有機密等。本機已用 Release 跑過 CI 的全部步驟，都通過。後端 344 個、前端 181 個測試全過。說明見 ci.md。

- 2026-10-10 WU-P0-009 應用程式日誌完成：`MyERP.Shared.Infrastructure/Logging` 以 Serilog 取代預設日誌提供者（`AddMyErpLogging` / `UseMyErpRequestLogging`）。日誌寫入滾動檔案 `logs/app-yyyyMMdd.log`，格式為 CLEF JSON，每天換檔，單檔上限 100 MB，保留 30 天。設定 `ApplicationInsights:ConnectionString` 或環境變數 `APPLICATIONINSIGHTS_CONNECTION_STRING` 後才啟用 Application Insights。實測 sink 5.0.1 相依 Microsoft.ApplicationInsights 2.23，和 AspNetCore SDK 3.2 衝突，所以只用 sink（ADR-008）。`ErpLogEnricher` 在每筆日誌加上 TraceId（和 ProblemDetails 的 traceId 對得上）、UserId、CompanyId，以及由路由前綴或命名空間判斷的 Module。請求耗時超過 `App:SlowRequestMilliseconds`（預設 1000）寫 Warning，其餘寫 Debug；請求路徑不含查詢字串。設定錯誤時啟動即失敗。appsettings 改用 `Serilog:MinimumLevel`。新增 53 個測試（單元 + WebApplicationFactory 整合，含檔案輸出），後端 335 測試全過；Infrastructure 行覆蓋率 84.8%。設計見 logging.md。

- 2026-10-10 WU-P0-010 前端骨架完成：`src/frontend` 為 Angular 22.2.2（standalone、zoneless、strict）+ ng-zorro-antd 22.1.1 + G2 5.4.8 + @ngx-translate 18。本機 Node 22 不符要求，改用 repo 內可攜版 Node 24.21.0，所有前端指令經 `tools/scripts/Invoke-Frontend.ps1` 執行（下載後驗證 SHA256，R-016 已緩解）。三語 i18n 按範圍分檔，模組語言檔以 `i18nScopeGuard` 延遲載入，`i18n-keys.spec.ts` 檢查三語 key 一致。主題有亮 / 暗 / 跟隨系統三種模式：用 less 編譯兩份 ng-zorro bundle 並換上馬卡龍主色，暗色 bundle 延遲載入；色值的 WCAG 對比由測試把關。Layout 有可收合側欄（平板寬度自動收合）與頂列（公司切換、語言、主題）；API 攔截器加上 Accept-Language 與 X-Company-Id。共用圖表元件有 erp-line-chart、erp-column-chart、erp-pie-chart（超過 7 類時合併「其他」）、erp-kpi-card（環比 + 迷你趨勢），主題或語言改變時重新渲染。另有首頁示範頁與 403 / 404 頁。前端 181 測試全過，覆蓋率 statements 97.4%、branches 96.6%；後端 282 測試全過。決策見 ADR-007。

- 2026-10-10 WU-P0-008 資料庫基線完成：`MyERP.Migrations` 第一個 migration `Core_Baseline`（`dotnet ef ... -p MyERP.Migrations -s MyERP.Api`，以 Api 主機 DI 建 DbContext）；定序改用 `Chinese_Traditional_Stroke_Count_100_CI_AS_SC`（MASTER_PROMPT 原名稱不存在，ADR-006、CR-007），既有庫定序不符時 migration 修正；SP 部署機制（`database/procedures/{前綴}/usp_*.sql` 嵌入 + `CreateOrAlterProcedure` / `DropProcedure`，範例 `usp_sys_database_info`）；EF 模型快取鍵含模組組合；`myerp_test` 每次測試執行重建（assembly fixture，拒絕連其他庫）；補 WU-P0-007 的真實資料庫測試（審計蓋章、樂觀鎖 409、公司 / 軟刪除篩選）；`/health/ready` 資料庫檢查（失敗 503）。本機 `myerp` 已建立並套用 migration。282 測試全過，Migrations 行覆蓋率 93.9%。

- 2026-10-10 WU-P0-007 共用核心完成：MyERP.Shared 加 IClock / SystemClock（UTC+8，`App:TimeZoneOffset`；唯一可讀系統時間的型別，TimeProvider 亦列禁用）、`Money.Round`（TWD/JPY 0 位、其他 2 位、AwayFromZero，可設定）、ICurrentUser / ICurrentCompany、PagedRequest / PagedResult / SortSpec 白名單、ErpException 系列 + errorCode、BusinessEntity / MasterEntity；Shared.Infrastructure 加 MyErpDbContext（IModuleDbModel、snake_case、型別慣例、具名 Company / SoftDelete 篩選、無公司時 `WHERE 0 = 1`、version concurrency token）、AuditStamper + SaveChanges interceptor、全域 ProblemDetails（三語標題、traceId、500 不洩漏細節、404 亦回 ProblemDetails）。243 測試全過，行覆蓋率 Shared 93.5% / Infrastructure 87%（`tools/scripts/Get-BackendCoverage.ps1`）；決策見 ADR-005、設計見 shared-core.md。

- 2026-10-10 WU-P0-006 後端骨架完成：`src/backend/MyERP.sln`（Api、Shared、Shared.Infrastructure、Migrations、Modules.System + Contracts 範本）、Directory.Build/Packages.props（net10.0、Nullable、TreatWarningsAsErrors、ADR-001 版本集中管理）、nuget.config（只用 nuget.org）、`/health/live`、`IErpModule` + `ErpModuleCatalog` 模組註冊；測試 MyERP.Shared.Tests、MyERP.ArchitectureTests（模組互不引用實作、Contracts 純淨、Domain 不依賴 EF/ASP.NET/其他層、禁 DateTime.Now 與 System.Data.SqlClient，以 IL 掃描且有自我測試）、MyERP.IntegrationTests（WebApplicationFactory）共 43 測試全過；xunit.v3 4.0.2 經 global.json 改走 Microsoft.Testing.Platform（ADR-004），測試指令改為 `dotnet test --solution ...`。

- 2026-10-10 WU-P0-005 架構與規範文件完成：architecture.md（全景圖、模組清單、Mermaid 依賴圖、跨模組交易內 / 交易後事件、安全架構、NFR 指標 REQ_NF421.1～425.6）、api-conventions.md（路由 `/api/v1/{prefix}/{複數資源}`、ProblemDetails + errorCode、分頁排序篩選、權限代碼）、db-conventions.md、ui-conventions.md（列表 / 單據頁範本、馬卡龍與 dark mode、i18n key）；ADR-003 決定每模組實作 + Contracts 兩專案、單一 MyErpDbContext、前綴統一於表 / 路由 / 權限。

- 2026-10-10 WU-P0-004 ADR-001 技術版本固定：本機實測 SDK 10.0.301（新增 global.json）、Node 22.16.0；固定 EF Core 10.0.12、EFCore.NamingConventions 10.0.1、SqlClient 6.1.7、Serilog 10、xunit.v3 4.0.2、FluentAssertions 7.2.2（8.x 商業授權）、Moq 4.20.72；前端 Angular 22.2 + ng-zorro 22.1.1 + G2 5.4.8 + TS 6.0，需 Node 24.21.0（新增風險 R-016、R-017）；docgen 用 .NET + OpenXML；查版本腳本 tools/versions/Get-LatestVersions.ps1。

- 2026-10-10 WU-P0-003 項目章程完成：目標 G1–G4、範圍（15 模組 + BlueWhale 基線 222 REQ_F / 150 介面按模組分佈）、不在範圍、成功標準 S1–S8、里程碑 M0–M6、角色；風險清單新增 R-008～015（BlueWhale 不一致、SRS/SDS 版面、無 Python、版本相容）；sprint-log 建立 Sprint 1 計劃。

- 2026-10-10 WU-P0-002 參考文件核對完成：用腳本逐項核對 SRS / SDS 原文（REQ_F 222、REQ_DS43 88、介面 150、REQ_IO_A 1,416），補齊章節規模、版面樣式、表格欄寬框線；對照表 460 行填上 MyERP 模組、需求 ID、介面編號、預計表名；編號規則寫入 ADR-002。
- 2026-10-10 WU-P0-001 Runner 檢查完成：符合 MASTER_PROMPT 2.1 全部要求；額度訊息解析實測 8 個案例正確；6 項改善建議寫入 change-requests.md（CR-001～006），詳見 runner-review.md。
