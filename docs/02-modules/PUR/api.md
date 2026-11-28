# PUR API

WU-PUR-007a9（2026-11-28T15:43:06+08:00，ADR-219）：新增 `POST /api/v1/pur/purchase-receipts/{id}/submit`（PUR.RECEIPT.SUBMIT，body `{version}`），回應 `{id,documentNo,status,version,approval}`。範圍外／他公司 404 `PUR.RECEIPT.NOT_FOUND`；舊版 409；422 代碼：NOT_SUBMITTABLE、QUANTITY_EXCEEDED、INVALID_SOURCE、APPROVAL_FLOW_REQUIRED（無有效審批流程，007b 前）。SYS 決策處理器 `PUR.RECEIPT`：駁回釋回、最終核准於 007b 前回 422 POSTING_NOT_AVAILABLE、重複決定 409 APPROVAL_STATE_CHANGED。
WU-PUR-007a8（2026-11-28T15:19:42+08:00，ADR-218）：新增 `PurchaseReceiptsController`（Swagger `/api/v1/pur/purchase-receipts`）。`POST`（PUR.RECEIPT.CREATE，201）以 `{purchaseOrderId,businessDate,lines:[{lineNumber,purchaseOrderLineId,quantity,warehouseId}]}` 建 Draft；`GET /{id}`（PUR.RECEIPT.VIEW）與 `GET /{id}/edit`（PUR.RECEIPT.EDIT）各按該動作 SYS 資料範圍讀取，範圍外／他公司／舊發票專用來源列 404 `PUR.RECEIPT.NOT_FOUND`；`PUT /{id}?version=`（PUR.RECEIPT.EDIT）整組替換 Draft／Rejected 行，舊版 409。回應 `{id,documentNo,status,version,purchaseOrderId,supplierId,currency,businessDate,lines:[{id,lineNumber,purchaseOrderLineId,materialId,quantity,warehouseId,unitCost}]}`。422 代碼：INVALID_SOURCE、QUANTITY_EXCEEDED、COST_UNAVAILABLE、SOURCE_LOCKED、STATUS_NOT_EDITABLE、INVALID_LINES／INVALID_HEADER。LIST、CSV、DELETE、SUBMIT 未交付。

WU-PUR-007a7（2026-11-28T13:01:04+08:00，ADR-218）只交付收貨草稿資料庫基礎，沒有新增路由或 Swagger；`/api/v1/pur/purchase-receipts` 的 CREATE／EDIT／READ 留 007a8，SUBMIT 留 007a9。前端 4.4～4.6 留 007e。

WU-PUR-008b6（2026-11-27T05:30:01+08:00）：`GET /api/v1/pur/invoices/{id}` 新增 `postedAt`、`fiPurchaseSourceId`、`fiPayableId`、`fiJournalId`（只在 Posted 有值，其餘 null），供 4.19 明細顯示財務參照。

WU-PUR-008b4（2026-11-27T04:36:46+08:00，ADR-208）：`POST /api/v1/pur/invoices/{id}/post?version=N`（`PUR.INVOICE.POST`，僅全公司資料範圍；無 body）。200 回 `{id, version, status:"Posted", replayed, fiPurchaseSourceId, fiPayableId, fiJournalId, postedAt}`；首次過帳 `replayed=false`、版本＝N+1；已 Posted 時以 N（過帳版本）或 N+1 重送回同一結果且 `replayed=true`。錯誤：400 版本／ID 非正數；401；403 無權限或範圍不足；404 `PUR.INVOICE.NOT_FOUND`（他公司、不存在、已刪）或 FI `SOURCE_NOT_FOUND`；409 舊版本、`PUR.INVOICE.DRAFT_LOCK_UNAVAILABLE`、FI `SOURCE_MISMATCH`／`SOURCE_CONFLICT`／`LOCK_TIMEOUT`；422 `PUR.INVOICE.INVALID_POST_STATUS`、`RATE_SNAPSHOT_MISSING`、`BASE_CURRENCY_NOT_SET`／`BASE_CURRENCY_CHANGED`、`INVALID_POSTING_SNAPSHOT`、`INVALID_POSTING_DIFFERENCE`、`INVALID_SOURCE_IDENTITY`、FI `MAPPING_MISSING`／`ACCOUNT_INVALID`（`journalLineNo`）／`INVALID_AMOUNTS`（`lineNo`）；500 `PUR.INVOICE.FI_RECEIPT_MISMATCH`、`PUR.INVOICE.FI_POSTING_FAILED` 或 FI 故障碼。任何錯誤都不保存。Swagger 摘要與狀態碼見 `PurchaseInvoicesController.Post`。

WU-PUR-008c3c1（2026-11-26T23:22:11+08:00，ADR-202）：`GET /api/v1/pur/invoices/export`（`PUR.INVOICE.EXPORT`，僅全公司資料範圍）。參數同列表（`keyword`、`from`／`to`、`status`、`supplierId`、`purchaseOrderId`、`sort`；`page`／`pageSize` 只驗證、不分頁）。200 `text/csv; charset=utf-8`、`Content-Disposition: attachment; filename="purchase-invoices.csv"`、`Content-Language`（zh-Hant／zh-Hans／en，取自 `Accept-Language`，未知回 zh-Hant）、`X-Export-Row-Count`。內容為 UTF-8 BOM＋CRLF，欄位：供應商發票號、發票日期、狀態（三語）、供應商 ID、採購單 ID、幣別、行號、收貨行 ID、數量、單價、稅率(%)、未稅、稅額、含稅。400 參數非法；403 缺 EXPORT 或範圍較窄；422 `PUR.INVOICE.EXPORT_LIMIT`（超過 10,000 行，未輸出任何內容）。

WU-PUR-008c3a（2026-11-26T22:48:06+08:00）：`GET /api/v1/pur/invoices/{id}` 的 `lines[]` 新增 `differences[]`（`{kind,baseline,invoice,difference}`，kind 為 UNIT_PRICE／TAX_RATE／NET／TAX／GROSS，四／六位 decimal）。只在 `status=Disputed` 時以保存的訂單單價／稅率即時計算（不鎖、不寫）；其他狀態為 `[]`。來源已失效或金額無效時亦回 `[]`，RESOLVE 仍在交易鎖內重算並回 422。前端 RESOLVE 表單（008c3b）只為 differences 非空的行要求決定，與 `PUR.INVOICE.INVALID_RESOLUTION` 規則一致。

WU-PUR-008a55 已公開 `GET /api/v1/pur/invoices` 與 `GET /api/v1/pur/invoices/{id}`（`PUR.INVOICE.VIEW`，僅全公司資料範圍，較窄授權 403）。列表參數：`page`、`pageSize`（1～200）、`keyword`（供應商發票號包含）、`from`／`to`（業務日）、`status`（Draft／Matched／Disputed／Rejected／Posted）、`supplierId`、`purchaseOrderId`、`sort`（單一欄：`businessDate` 預設降冪、`supplierInvoiceNumber`、`totalGross`，同值以 ID 同向排序）。回 `PagedResult<{id,supplierId,purchaseOrderId,supplierInvoiceNumber,businessDate,currency,status,totalNet,totalTax,totalGross,lineCount,version}>`。明細回表頭（含 `createdAt`／`createdBy`／`updatedAt`／`version`）、`lines[{id,lineNo,receiptLineId,quantity,unitPrice,taxRatePercent,net,tax,gross}]` 與 `resolutions[{id,invoiceVersion,lineNo,action,reason,netDifference,taxDifference,grossDifference,decidedBy,decidedAt}]`（按決定時版本、行號排序）。`status` 以字串輸出。參數非法 400；不存在、已軟刪或他公司 404。來源名稱／可開票量與 FI 應付狀態待 008c／008b 補。

WU-PUR-008a54 已公開 `POST /api/v1/pur/invoices/{id}/resolve?version=N`（`PUR.INVOICE.RESOLVE`，僅全公司資料範圍；ADR-201）。body `{lines:[{lineNo,action,reason}]}`：每個有價稅差異的行恰一筆 `Accept`／`Reject`，原因去空白後 1～500 字。服務在與 MATCH 相同的 Serializable 交易及供應商應用鎖內重驗來源身份與訂單價稅、重算差異；全部接受回 `Matched` 並在排除本單預留後重驗餘量（不足 422 `PUR.INVOICE.INSUFFICIENT_RECEIPT_QUANTITY`），任一拒絕回 `Draft` 並精確釋回本單預留；同交易寫入 `pur_purchase_invoice_resolution`。200 回 `{id,version,status,decisions}`。版本 0 或空 lines 回 400，缺權 403，不存在 404，舊版本 409，非 Disputed 回 `PUR.INVOICE.INVALID_RESOLVE_STATUS` 422，決定不完整／多餘行／空原因回 `PUR.INVOICE.INVALID_RESOLUTION` 422。

WU-PUR-008a53 已公開 `POST /api/v1/pur/invoices/{id}/match?version=N`（`PUR.INVOICE.MATCH`，僅全公司資料範圍；空 body）。服務在 Serializable 交易及同一公司／供應商應用鎖內重讀已保存發票、Approved 訂單、Posted 收貨及保存價稅，執行 BR-PUR-037e，並把請求量加到收貨行 `matched_reserved`。200 回 `{id,version,status,amount}`，`status` 為 `Matched`（零差異）或 `Disputed`（有價稅差異，同樣預留）；版本 0 回 400，不存在或他公司 ID 回 404，舊版本回 409，鎖逾時回 `PUR.INVOICE.DRAFT_LOCK_UNAVAILABLE` 409，餘量不足回 `PUR.INVOICE.INSUFFICIENT_RECEIPT_QUANTITY` 422（不預留），來源失效回 422。POST／GET 仍未實作。

WU-PUR-008a51 已公開 `POST /api/v1/pur/invoices`（`PUR.INVOICE.CREATE`，201 + Location + `{id,version}`）與 `PUT /api/v1/pur/invoices/{id}?version=N`（`PUR.INVOICE.EDIT`，200 + `{id,version}`），Swagger 標籤 `PUR - Invoices`。body 為 `PurchaseInvoiceDraftInput`，不收公司／狀態／FI ID；公司取自授權上下文。兩者僅接受全公司資料範圍，較窄授權回 403；其他公司或不存在的 EDIT ID 回 404，舊版本與重複供應商號碼回 409，失效來源回 422。完整來源在 Serializable 交易及供應商應用鎖內組裝，並同交易寫入表頭、明細、永久號碼持有列。沒有新增 GET；來源讀取頁面及三語 UI 留 008c。此路由尚欠 008a51 的完整真實 HTTP 邊界與後端全量驗收；已知定向真實 HTTP 1/1，最近 SQL 記憶體守衛 DEFER。

WU-PUR-008a48 收緊內部 Draft 保存服務的建立／修訂參數與快照身份檢查；無效請求不建立 SQL session。此切片沒有新增 HTTP 端點或 Swagger；HTTP 驗收仍留 008a／008c。

WU-PUR-008a49 將 Draft session 的交易應用鎖超時 SQL 51001 轉成 `PUR.INVOICE.DRAFT_LOCK_UNAVAILABLE` 409，先回滾交易。尚無公開 CREATE／EDIT 路由；Swagger、權限與真實 HTTP 錯誤驗收留 008a51。
WU-PUR-008a49b 提供 `PurchaseInvoiceDraftHttpIntent.Create/Edit` 離線請求邊界，將服務端公司、路由發票 ID／期望版本與已驗證 Draft 內容組成單一命令。尚無公開路由；008a51 接入控制器時須由目前公司授權上下文供給公司 ID，不接受 body 指定公司。

WU-PUR-008a46 註冊內部 `IPurchaseInvoiceDraftSourceReader`，在現行 Draft SQL 交易讀同模組來源；不新增公開端點或 Swagger。採購單、收貨及發票 HTTP 仍由後續產品單元處理。

WU-PUR-008a44 提供 Draft SQL 交易 session 殼層，尚無 CREATE／EDIT HTTP 端點、Swagger 或權限接線；讀取器與端點留 008a。

WU-PUR-008a39 的離線 EF 計畫套用結果明確區分待新增、完整既有追蹤及實際變更持有列；008a 寫入者須在鎖內同交易使用。沒有新增 HTTP 端點或 Swagger 行為。

WU-PUR-008a37 的離線共同 EF 計畫新增保存前漂移檢查；008a 寫入者須於 SQL 交易鎖內組裝、在持有列 Apply 前呼叫。沒有新增 HTTP 端點或 Swagger 行為。

WU-PUR-008a36 提供同一保存意圖的 Draft 表頭／行與號碼持有列共同 EF 組裝入口；沒有新增 HTTP 端點或 Swagger 行為，真實保存仍待 008a。

WU-PUR-008a35 已將已驗 Draft 保存意圖的持有列步驟映射為待追蹤 EF 實體變更，沒有新增 HTTP 端點。008a 寫入者仍須於 SQL 鎖內讀完整持有列，按序套用並與發票表頭同交易保存及轉譯唯一索引衝突。

WU-PUR-008a34 已把 Draft 行寫入計畫轉成 EF 表頭與行實體的離線組裝；008a 仍須在 SQL 鎖內重驗並同交易保存。沒有新增 HTTP 端點或 Swagger 行為。

WU-PUR-008a33 已提供 Draft 表頭及行的離線寫入值計畫；CREATE／EDIT 仍須於 008a 在 SQL 鎖內重驗、執行同交易保存與持有列計畫。沒有新增 HTTP 端點或 Swagger 行為。

### WU-PUR-008d 三單比對 API 與 FI 契約（尚未實作）

本節原為後續完整發票流程設計；Draft CREATE／EDIT 實際基址現為 `/api/v1/pur/invoices`，Swagger 標籤 `PUR - Invoices`。下表的 GET、POST、DELETE 及較豐富回應仍是待實作契約（MATCH 由 008a53、RESOLVE 由 008a54 實作，見文首），不能視為現有端點。寫入只接受來源 ID、供應商發票號、發票業務日、行數量與原幣價稅值，不接受公司、狀態、FI ID、訂單基準或差額。服務按保存的採購單／收貨與公開 FI 契約重新驗算。

CREATE／EDIT 的供應商發票號先套 BR-PUR-036a；號碼欄位非法回 400，同公司／供應商正規化鍵重複（包括已軟刪）回 409。現有端點已接入持有列唯一約束，完整 HTTP 邊界及競態仍待 008a51／52 驗收。
008a13 的 BR-PUR-036b 可供交易內重複判定，但不可代替 SQL 唯一約束；現有 Draft HTTP 已接入兩者。
008a30 的改號離線計畫要求先停用舊活動鍵，再啟用本張歷史鍵或插入新活動鍵；交易內更新表頭及持有列仍待 008a。只把永久號碼索引的 2601／2627 衝突轉為 `PUR.INVOICE.NUMBER_EXISTS` 409，不將其他 SQL 唯一衝突誤報成重號。
008a31 已將新建及修訂的已驗 Draft 保存意圖接到持有列離線寫入計畫；008a51 的 HTTP 保存服務在同一 SQL 交易內讀完整持有列、執行計畫及更新發票表頭，競態驗收留 008a52。

| Method | Path | Body／回應 | 權限 |
|---|---|---|---|
| GET | `/`, `/{id}` | 按單號／供應商／日期／狀態穩定分頁；保存行、來源、差異決定、FI 狀態（008a55 已實作列表／明細／決定歷史，見文首） | `PUR.INVOICE.VIEW` |
| GET | `/source-options`, `/{id}/edit` | CREATE／EDIT 動作範圍內 Approved 訂單及 Posted 收貨可開票量、保存草稿 | `PUR.INVOICE.CREATE` 或 `PUR.INVOICE.EDIT` |
| POST | `/` | 建 Draft；201 回 ID、單號、版本及行數 | `PUR.INVOICE.CREATE` |
| PUT | `/{id}?version=` | 整組修訂 Draft／Rejected，200 回保存內容 | `PUR.INVOICE.EDIT` |
| POST | `/{id}/match` | `{version}`；200 回 Matched／Disputed、各行比較和版本；整單預留 | `PUR.INVOICE.MATCH` |
| POST | `/{id}/resolve?version=` | `{lines:[{lineNo,action,reason}]}` 整單一次決定（ADR-201 取代原 `/resolutions` 逐行草案）；200 回狀態／版本／決定 | `PUR.INVOICE.RESOLVE` |
| POST | `/{id}/post` | `{version}`；200 回 Posted、FI 應付 ID 與版本 | `PUR.INVOICE.POST` |
| DELETE | `/{id}?version=` | 軟刪 Draft／Rejected；204 | `PUR.INVOICE.DELETE` |

MATCH 無差異回 Matched，仍須呼叫 POST；Disputed 各行接受後轉 Matched。拒絕轉 Rejected 並釋回預留；再次修訂才可重匹配。POST 與 FI `PostPurchasePayableAsync` 在同一 SQL 交易，FI 未就緒回具體 422，禁止回假成功。格式 400、匿名 401、缺權 403、跨公司／資料範圍外 404、舊版與快照重放 409、來源／數量／差異 422、未預期 500；ProblemDetails 含 `errorCode`、`traceId`、行號與可用量。逾時／斷線應重讀來源狀態，不推測已成功。

### WU-PUR-007d 收貨／退貨 API 設計（端點尚未實作）

基址 `/api/v1/pur/purchase-receipts`、`/api/v1/pur/purchase-returns`，Swagger 標籤 `PUR - Purchase Receipts`／`PUR - Purchase Returns`。兩者各有 VIEW／CREATE／EDIT／DELETE／SUBMIT 權限；LIST、詳情與編輯專用上下文分別套 SYS 動作資料範圍。只讀查詢支援 `keyword,from,to,purchaseOrderId,status,page,pageSize,sort` 白名單穩定分頁。另有 VIEW + EXPORT 的安全 CSV；已保存單據重新讀取後列印。無 VIEW 的 CREATE／EDIT 用戶由該動作權限的專用來源選項及 EDIT 詳情取得必要資料。

WU-PUR-006e3（2026-11-28T08:33:59+08:00）：`GET /api/v1/pur/purchase-orders/export`，要求 `PUR.ORDER.EXPORT` 且再驗 `PUR.ORDER.VIEW`，結果取兩個動作資料範圍交集。篩選／排序與列表一致，`page`／`pageSize` 驗證但匯出全部；每個保存行一列，同單按行號。`200 text/csv; charset=utf-8`、`Content-Disposition: attachment; filename="purchase-orders.csv"`、`Content-Language`、`X-Export-Row-Count`。內容為 BOM＋CRLF、三語表頭與狀態、ISO 日期、invariant 數字、RFC 4180 引號與公式前導轉義；0 行只有表頭。超過 10000 行在輸出前回 422 `PUR.ORDER.EXPORT_LIMIT`；非法參數 400、缺任一權限 403。列印由前端每次重新請求 `GET /{id}`，沿原 VIEW／公司／資料範圍及 SYS 歷史契約，無列印寫入端點。

| Method | Path（相對各基址） | Body／回應 | 權限 |
|---|---|---|---|
| GET | `/`, `/{id}` | 分頁摘要／保存行、來源餘量、追蹤、版本與審批歷史 | VIEW |
| GET | `/source-options`, `/{id}/edit` | 授權範圍內 Approved 訂單行或 Posted 入庫行可用量；編輯保存快照 | CREATE 或 EDIT |
| POST | `/` | 來源單 ID、業務日、1～1000 行的來源行 ID、數量、倉儲／追蹤；201 回保存 ID、單號、行數與版本 | CREATE |
| PUT | `/{id}?version=` | 整組修訂 Draft／Rejected；200 回已保存內容 | EDIT |
| DELETE | `/{id}?version=` | 軟刪 Draft；204 | DELETE |
| POST | `/{id}/submit` | `{version}`；同交易預留來源餘量、建 SYS 審批，200 回狀態／版本 | SUBMIT |

來源採購單、原入庫行及成本不能由請求重寫；服務以保存來源重算並鎖定。最終核准／駁回只透過 SYS approval API，回呼在相同交易更新 PUR 與 INV。INV 採購入庫現有 `PostPurchaseInboundAsync`；退貨須先增專用 `PostPurchaseReturnAsync`，不得轉用 `PostSalesOutboundAsync`。成功回應只在交易提交後發出。ProblemDetails：格式 400、匿名 401、缺權 403、不可見 404、版本或不同快照重放 409、非法狀態／不足餘量／追蹤不符 422、未預期 500（含 traceId）；逾時及網絡失敗不得顯示成功。批次只有整單成功或整單失敗，無略過行。

WU-PUR-007a3 的 `PurchaseReceiptReservationPlan` 是離線轉移計畫；SUBMIT／SYS 駁回端點實作時須在 SQL 鎖內使用已保存行與權威來源重算，按來源 ID 升冪比較待審量原值後原子套用。此切片無 HTTP 端點驗收。

WU-PUR-007a4 的 `PurchaseReceiptApprovalPlan` 是最終核准離線計畫；SYS 決定端點須在 SQL 鎖內重讀保存行、逐來源預留與過帳證據，比對原值後與 INV 過帳、PUR 狀態同交易提交。Posted 相同保存快照重放須有 INV movement 證據。此切片無 HTTP 端點驗收。

WU-PUR-007a6 的 `PurchaseReceiptReservationAssembly.ForSubmit/ForReject` 只以請求公司／收貨單 ID 及呼叫端提供的保存快照組裝轉移計畫；SUBMIT／SYS 駁回端點必須在固定鎖序內取得權威行、本單預留及版本，重新組裝並同交易比對原值後提交。此離線切片沒有 HTTP 端點或權限驗收。

### WU-PUR-006d 採購單 API 設計（006e1 列表、006e2 詳情；匯出與畫面待 006e3～e4）

基址 `/api/v1/pur/purchase-orders`，Swagger 標籤 `PUR - Purchase Orders`。每個端點先驗目前公司、獨立 `PUR.ORDER.*` 權限及 SYS 動作資料範圍。人工 POST 不接受公司、單號、擁有人、金額合計或審計欄位；行使用 `itemId, requisitionLineId?, quantity, unitPrice, currencyCode, taxRateId`，數量／單價以十進位值或保真十進位字串傳遞。來源決標草稿仍由 005e4 的 `POST /rfqs/{id}/orders` 建立，訂單 POST 不接受 `awardId`。取得編輯詳情、MD 選項及請購來源需各自以 CREATE／EDIT 動作守衛，不能要求額外 MD.VIEW 或 ORDER.VIEW；先查訂單動作範圍再查保存引用，避免洩漏。

| Method | Path | 用途／回應 | 權限 | 主要錯誤 |
|---|---|---|---|---|
| GET | `/` | `keyword,from,to,supplierId,status,requisitionLineId,page,pageSize,sort`；穩定分頁；同排序鍵以 ID 破同序，行數來自已保存行。006e1 已實作 | `PUR.ORDER.VIEW` | 400、401、403 |
| GET | `/{id}` | 先查 VIEW 範圍訂單；保存行含 `awardId`、`requisitionLineId`、請購單 ID／單號／行號、`requisitionQuantity`／`requisitionOrderedQuantity`／`requisitionRemainingQuantity`；表頭含 `rfqId`／`rfqDocumentNo`、版本、`approvalHistory`（SYS 公開契約，最新輪次在前）。無來源欄位為 null，歷史軟刪來源仍可讀。006e2 已實作 | `PUR.ORDER.VIEW` | 400、401、403、404 |
| GET | `/supplier-options`、`/item-options` | 目前公司已核准啟用供應商、啟用物料分頁選項（`page,pageSize,keyword,sort`）；沿用 MD 公開契約。已實作（006f2a，ADR-216） | `PUR.ORDER.CREATE` 或 `PUR.ORDER.EDIT` | 400、401、403 |
| GET | `/create-context?businessDate=` | 目前公司啟用幣別及該業務日有效的非銷項稅率（`ratePercent` 精確十進位）；日期必填，缺漏／無效／0001-01-01 回 400。已實作（006f2a） | `PUR.ORDER.CREATE` 或 `PUR.ORDER.EDIT` | 400、401、403 |
| GET | `/{id}/edit` | EDIT 動作範圍內可修訂詳情和保存引用狀態 | `PUR.ORDER.EDIT` | 403、404、422 |
| GET | `/requisition-options` | `itemId?,keyword,page,pageSize`：同公司未刪除、已核准且原量大於已轉量的請購行，回 `id,requisitionId,documentNo,lineNo,itemId,requiredDate,quantity,orderedQuantity,remainingQuantity`，按單號／行號／ID 穩定分頁；非正 itemId 400。不需 REQUISITION.VIEW；送審時仍鎖內重算。已實作（006f2a） | `PUR.ORDER.CREATE` 或 `PUR.ORDER.EDIT` | 400、403 |
| POST | `/` | 原子建立人工 Draft，201 回保存詳情（ID、單號、行、原幣／本位幣價稅、版本） | `PUR.ORDER.CREATE` | 400、409、422 |
| PUT | `/{id}?version=` | 整組修訂 Draft／Rejected（保存後回 Draft），單號不變；行按行號比對，決標行保留 ID 與商業快照，200 回保存詳情（決標行原幣金額可為 null）。已實作（006a3） | `PUR.ORDER.EDIT` | 400、404、409（舊版本）、422（`PUR.ORDER.NOT_EDITABLE`、`PUR.ORDER.AWARD_LINE_LOCKED`、`PUR.ORDER.INVALID_REQUISITION_SOURCE`、`PUR.ORDER.AMOUNT_OVERFLOW`：決標行保存金額加人工行重算後表頭達 DECIMAL(19,4) 上限，006a4） |
| DELETE | `/{id}?version=` | 軟刪未消耗的 Draft／Rejected（駁回已釋回），204；單號、保存行、決標來源與 SYS 歷史保留，請購已轉量不變，單號不重用。已實作（006c1，ADR-215） | `PUR.ORDER.DELETE`（EDIT 不含） | 400、401、403、404（他公司／範圍外／已刪）、409（舊版本）、422 `PUR.ORDER.NOT_DELETABLE`（待審／已核准／已取消；核准單改用 CANCEL） |
| POST | `/{id}/submit` | `{version}`；只認已保存行，同交易聚合並消耗請購量、設消耗旗標、建立 SYS 流程（金額＝保存 `baseGross`），200 回 `{id,documentNo,status,version,approval}`；零步驟即 `Approved`。已實作（006b1，ADR-214） | `PUR.ORDER.SUBMIT` | 400、401、403、404、409（舊版本）、422（`PUR.ORDER.NOT_SUBMITTABLE`、`PUR.ORDER.EMPTY`、`PUR.ORDER.INVALID_REQUISITION_SOURCE`＋`lineNos`、`PUR.ORDER.REQUISITION_INSUFFICIENT`＋`shortages[{requisitionLineId,orderLineNos,requested,available}]`、MD 供應商未核准／停用 `MD.SUPPLIER.NOT_APPROVED`、物料停用 `MD.ITEM.INACTIVE`）；受權 HTTP 已驗收（006b3） |
| — | SYS `POST /api/v1/sys/approvals/{id}/approve`／`reject`（`PUR.ORDER`） | PUR 不另開決定端點；最終核准→`Approved` 保留請購消耗；駁回→`Rejected`，同交易釋回本單請購量並清消耗旗標；中間核准不影響 PUR。已實作（006b2） | SYS 審批人 | 409（`PUR.ORDER.APPROVAL_STATE_CHANGED` 訂單已非待審或旗標不符；`PUR.ORDER.REQUISITION_RELEASE_INVALID` 已轉量不足扣回）、422 `SYS.APPROVAL.NOT_PENDING`，皆整筆回滾（SYS 實例仍 Pending）；真實 HTTP 已驗收（006b3） |
| POST | `/{id}/cancel` | `{version,reason}`；原因去空白後 1～500 字，Approved 且無未軟刪收貨引用才按保存行原子釋回並清旗標，200 回 `{id,documentNo,status:"Cancelled",version}`；SYS 歷史與單號保留。已實作（006c2） | `PUR.ORDER.CANCEL`（DELETE／EDIT 不含） | 400 ID／原因、401、403、404 他公司／範圍外／已刪、409 舊版本／`PUR.ORDER.REQUISITION_RELEASE_INVALID`、422 `PUR.ORDER.NOT_CANCELLABLE`／`PUR.ORDER.HAS_RECEIPTS` |

核准／駁回只走 SYS `/api/v1/sys/approvals/{id}/approve|reject`（`SYS.APPROVAL.PROCESS`）；PUR 結果處理器在 SYS 交易內更新訂單，駁回釋回已轉量。提交時以請購行更新鎖和訂單版本處理並發，聚合同一請購來源量後整單檢查；不足回 422 並列行號、請購單號與可用量。匿名 401、缺權 403、跨公司／範圍外 404、格式 400、舊版 409、狀態及失效引用 422、例外 500，均用含 `errorCode` 與 `traceId` 的 ProblemDetails。供應商／物料／稅率等只透過 MD 公開契約取得。INV 過帳不由本資源直接觸發。

### WU-PUR-005f2c 詢價 CSV／列印

`PUR.RFQ.EXPORT` 控制 4.11 的 CSV 按鈕；匯出及列印組合既有 `GET /api/v1/pur/rfqs`、`GET /api/v1/pur/rfqs/{id}`，沒有新寫入 API 或資料表。每個請求仍需 `PUR.RFQ.VIEW` 並套用公司與 SYS VIEW 資料範圍。匯出先核對所有分頁的總筆數、唯一 ID，再逐張核對保存版本與明細，任何 400／403／404／409／422／500、逾時或資料變動整檔取消。列印重新 GET 單張詳情後才呼叫瀏覽器列印；讀取失敗不列印。

### WU-PUR-005e4／005f3d 轉單草稿契約

`POST /api/v1/pur/rfqs/{id}/orders` 使用 `PUR.RFQ.CONVERT`。Body：`version`（目前詢價版本）、`supplierId`、`awardIds`（1～1000 個不同的正數 ID）。不接受公司、單號、價格、數量或審計欄位。成功 201 回 `orderId`、`documentNo`、`status=Draft`、`lineCount`、`awardIds` 與新詢價版本；另以受權的訂單詳情讀取已保存行及決標快照。此端點只接受同一已發送詢價、同一供應商、同一原幣且尚未轉單的決標行。明確選擇行集合，不自行略過已轉行；空集合 400，混入無效 ID／重複 ID 400，來源錯配 404，已轉或詢價非 Sent 422，舊版本 409。

服務以目前公司及 SYS CONVERT 動作資料範圍定位詢價，鎖表頭後驗版本、行集合與來源唯一性；在同一 SQL 交易內用 SYS 公開單號契約 `PUR.ORDER` 發號、建立表頭／行／來源連結並遞增詢價版本。持久化快照取決標保存值，不重新以今日 MD 匯率改價；按 ADR-210，同批各行若保存本位幣或匯率不同回 422 `PUR.RFQ.RATE_MISMATCH`／`PUR.RFQ.CURRENCY_MISMATCH`。轉單不改請購已轉量或 INV 庫存。任何一行衝突或保存故障整批回滾，成功回應只在提交後發出。重送同一版本回 409，使用新版本重送已轉行回 422，兩者都返回可辨識的衝突原因，不再建單。匿名 401、缺權 403、跨公司／資料範圍外 404；所有錯誤用含 `errorCode` 與 `traceId` 的 ProblemDetails。005e4 真實 SQL／HTTP 交易驗收已完成；005f3d 前端接入此端點。

`GET /api/v1/pur/rfqs/{id}/award-context` 的每個決標快照包含可空 `purchaseOrderId`、`purchaseOrderNo`。已轉來源即使訂單軟刪仍須顯示原單號；查詢只解除訂單軟刪篩選，不解除公司篩選。前端 POST 201 後以這個 GET 逐行核對回傳單號、訂單 ID 與行集合；核對完成才顯示成功。任何核對失敗應提示用戶重新整理並查看採購單，不宣稱轉單成功。

WU-PUR-005d1 建立報價持久化模型，005d2 提供受權列表與歷史詳情 GET，005d3 提供首版與不可變修訂 POST，005d4 提供撤回 POST 及即時過期旗標。005e2 提供唯讀逐行比價，005e3 提供逐行決標；轉單由 005e4 完成。報價版本、行及決標快照不可修改的 SQL 守衛已落地。

`GET /api/v1/pur/rfqs/{id}/quotation-context?quoteDate=yyyy-MM-dd` 使用 `PUR.QUOTATION.QUOTE`，先按目前公司及該動作資料範圍查詢已保存詢價，再返回最小的狀態、行與受邀快照，以及 MD 公開契約提供的目前公司啟用幣別和指定報價日有效進項稅率。省略日期使用 IClock 當日；無權 401／403，範圍外 404，無效日期 400。無需 RFQ.VIEW 或 MD.VIEW；寫入仍在同公司及日期下重驗選項。不新增資料表或 migration。

`POST /api/v1/pur/rfqs/{id}/awards` 使用 `PUR.RFQ.AWARD`。Body：`version`（目前詢價版本）、`rfqLineId`、`quotationId`、`reason`（1–500 字）。成功 201 回決標 ID、詢價新版本、供應商與報價來源、價格／稅率／匯率／期間及本位幣金額快照。服務先鎖目前公司詢價表頭並按 SYS 動作資料範圍檢查，只接受已發送未關閉詢價；在同一交易以當日 MD 公開快照重新比價。缺率、撤回、過期或未生效的報價 422，行／報價錯配或範圍外 404，舊版 409，已決標行 422；空理由／無效 ID 400。整交易失敗不留決標或報價狀態變更；報價變為 Awarded 後修訂及撤回禁止。詢價行唯一索引及不可變觸發器防覆寫，轉單另於 005e4 實作。

### WU-PUR-005e2 逐行比價契約

WU-PUR-005f3c1 的前端 4.15 只呼叫此 GET，直接呈現服務端已計算及排序的金額、匯率／稅率來源和不可比原因；重新整理或切換公司時先清舊資料，不呼叫 AWARD／CONVERT。

`GET /api/v1/pur/rfqs/{id}/comparison` 使用 `PUR.RFQ.COMPARE`，對目前公司與本人／部門／分店／全公司範圍內的已發送或已關閉詢價返回每個詢價行的全部目前報價。`comparisonDate` 取 `IClock` 的目前業務日；每行按本位幣含稅成本升冪、供應商及報價 ID 穩定排序。原幣單價、匯率及稅率快照來源、有效期間、本位幣淨額／稅額／含稅額分欄返回，金額按公司本位幣精度逐行 `Money.Round`。已決標報價保留可比金額供歷史對照，重複決標由寫入端點拒絕。撤回、過期、未到生效日、缺有效匯率／進項稅率及金額溢位的報價保留在尾部，`isComparable=false` 並返回 `unavailableReason`，不提供可決標的比較金額。無任何報價時各詢價行回空陣列。同一幣別與稅率在一次請求中只查 MD 一次。匿名 401、缺權 403、詢價不存在／範圍外 404、草稿或公司未設定本位幣 422；端點不寫入任何資料，決標仍須在 005e3 重驗。

### WU-PUR-005d4 撤回與過期契約

`POST /api/v1/pur/rfqs/{id}/quotations/{quotationId}/withdraw?version={目前版本}` 使用 `PUR.QUOTATION.QUOTE`，無 Body；成功 200 回已保存的全部修訂、`Withdrawn` 狀態及新版本。匿名 401、缺權 403、跨公司／範圍外／報價錯配 404、舊版 409、已撤回／已決標／過期／詢價關閉 422。報價列表與詳情另回 `isExpired`，由 `IClock` 對目前修訂有效日計算，不改資料庫狀態；版本與歷史仍可追溯。寫入引用有效性仍經 MD 公開契約驗，不直接查 MD 表。

### WU-PUR-005d3 報價寫入契約

`POST /api/v1/pur/rfqs/{id}/quotations` 使用 `PUR.QUOTATION.QUOTE`，Body 為 `supplierId`、`quoteDate`、`validUntil`、可選 `remark` 與 `lines`（每行 `rfqLineId`、`unitPrice`、`currencyCode`、`taxRateId`）；成功 201，返回全部保存版本。`POST /api/v1/pur/rfqs/{id}/quotations/{quotationId}/revisions?version={目前版本}` 使用同一 Body；成功 200，舊版仍可查。兩者先用目前公司及動作資料範圍鎖定詢價，僅接受已發送未關閉及受邀供應商，報價行必須恰好覆蓋詢價全部行。MD 幣別須啟用，進項稅率 ID 須於報價日有效；跨模組引用只經 MD 公開契約。負價、超六位小數、空行、重複行及日期錯誤為 400；重複供應商及舊版本為 409；關閉／過期／撤回／已決標為 422。所有行與版本在同一交易原子保存，錯誤不留下部分行。

### WU-PUR-005d2 報價唯讀契約

`GET /api/v1/pur/rfqs/{id}/quotations` 以 `PUR.QUOTATION.VIEW` 守衛。先按目前公司及本人／部門／分店／全公司範圍確認詢價可見，再對該詢價的報價套 `supplierId`、`status`、`validOn`（當前修訂報價日 ≤ 指定日期 ≤ 有效期）篩選；`page`／`pageSize`（1～200）、`sort`（`supplierId`／`quoteDate`，可加 `-` 降冪）穩定分頁。包含已撤回報價，摘要顯示目前修訂號、日期、有效期及版本。`GET /api/v1/pur/rfqs/{id}/quotations/{quotationId}` 依相同範圍返回全部不可變修訂、逐行原幣六位單價、幣別及稅率 ID；按修訂號及詢價行排序。匿名 401、缺權 403、詢價不可見／報價不屬該詢價 404、無效查詢 400。此兩端點不讀其他模組資料表，也不修改資料；前端介面由 005f 接入。

## WU-PUR-005a～c 詢價／報價／比價契約

WU-PUR-005b 已落地詢價三表及同公司約束。005c1 已提供 `GET /api/v1/pur/rfqs` 與 `GET /api/v1/pur/rfqs/{id}`：前者支援 `keyword`（單號）、`from`、`to`、`status`、`requisitionLineId`、`page`、`pageSize`、`sort`（`documentNo`／`businessDate`，`-` 降冪），SQL 先套公司及 SYS `PUR.RFQ.VIEW` 資料範圍才穩定分頁；後者返回已保存行、邀請、發送快照、版本及歸屬。兩者有 Swagger DTO／回應說明。005c2 已加入草稿 POST／PUT／DELETE 與對應權限，驗證、真實 MD 引用、SYS 同交易發號與整組替換由 `RfqDraftService` 執行；受權 HTTP 驗 201／200／204、範圍外 404 與並行建立不重號。005c3 已加入發送／關閉；發送同交易重驗 MD 物料、已核准請購來源與供應商，保存供應商代碼／名稱及發送時間快照。關閉僅接受已發送狀態，保留歷史；雙連線同版本僅一次成功。

基址 `/api/v1/pur/rfqs`，Swagger 分組 `PUR - RFQs`。所有端點先驗目前公司與 SYS 動作權限／資料範圍；範圍外及他公司資源回 404，輸入 400、過期版本 409、商業狀態／停用引用 422，ProblemDetails 帶 `errorCode` 和 `traceId`。列表由 SQL 先套公司及資料範圍，再白名單排序和分頁；前端不得用其他模組資料表。以下契約於 005c～e 落地，屆時補 Swagger DTO 和具體錯誤碼。

| Method | Path | 用途 | 權限 |
|---|---|---|---|
| GET | `/` | 單號、日期、狀態、請購來源篩選及穩定分頁 | `PUR.RFQ.VIEW` |
| GET | `/{id}` | 保存行、邀請名單、狀態及版本 | `PUR.RFQ.VIEW` |
| POST | `/` | 建立詢價草稿、SYS 單號及 MD 物料／供應商驗證 | `PUR.RFQ.CREATE` |
| PUT | `/{id}?version=` | 整組替換草稿行及邀請名單 | `PUR.RFQ.EDIT` |
| DELETE | `/{id}?version=` | 軟刪未發送草稿 | `PUR.RFQ.DELETE` |
| POST | `/{id}/send?version=` | 原子保存發送與供應商快照，返回保存詳情；重發 422、舊版 409 | `PUR.RFQ.SEND` |
| POST | `/{id}/close?version=` | 關閉已發送詢價並保留歷史，返回保存詳情；草稿／重關 422、舊版 409 | `PUR.RFQ.CLOSE` |
| GET | `/{id}/quotation-context?quoteDate=` | 保存行與邀請，加上目前公司有效幣別及報價日進項稅率選項 | `PUR.QUOTATION.QUOTE` |
| GET | `/{id}/quotations` | 報價分頁與目前修訂摘要（已實作） | `PUR.QUOTATION.VIEW` |
| GET | `/{id}/quotations/{quotationId}` | 全部修訂及保存行（已實作） | `PUR.QUOTATION.VIEW` |
| POST | `/{id}/quotations` | 為受邀供應商記錄第一版報價（已實作） | `PUR.QUOTATION.QUOTE` |
| POST | `/{id}/quotations/{quotationId}/revisions?version=` | 以目前版本建立不可變修訂版（已實作） | `PUR.QUOTATION.QUOTE` |
| POST | `/{id}/quotations/{quotationId}/withdraw` | 撤回未決標報價 | `PUR.QUOTATION.QUOTE` |
| GET | `/{id}/comparison` | 逐行原幣及本位幣含稅比較，標示全部不可比原因（已實作） | `PUR.RFQ.COMPARE` |
| POST | `/{id}/awards` | 逐行決標並保存理由與價格快照 | `PUR.RFQ.AWARD` |
| GET | `/{id}/award-context` | 依 COMPARE 資料範圍讀目前詢價單號、狀態、版本及不可變逐行決標快照；他公司或範圍外 404 | `PUR.RFQ.COMPARE` |
| POST | `/{id}/orders` | 將同一供應商已決標且未轉行建立採購單草稿 | `PUR.RFQ.CONVERT` |

建立／修訂 body 只接受業務欄位與行號，不接受公司、單號、擁有人或審計欄位；歸屬從 SYS 取得。送審、決標、轉單均帶版本；重試相同轉單請求須回已建立單的可辨識結果或明確衝突，不能重複建立。MD 物料、供應商、幣別、稅率及匯率經公開 Contracts 驗證。詢價發送目前不發外部 email；介面只回報系統內的發送狀態。

005e1 已擴充 MD `ICurrencyLookup.GetRateSnapshotAsync` 與 `ITaxRateLookup.GetRateSnapshotAsync`：回傳有效期間、來源 ID、匯率／稅率數值，按目前公司與業務日期查找。005e2 的 PUR 比價端點只透過這些公開契約取值；沒有直接查 MD 表。

## WU-PUR-000 請購契約（待 WU-PUR-001～003 實作）

基址 `/api/v1/pur/requisitions`，Swagger 標籤 `PUR - Requisitions`。所有錯誤用含 `errorCode`、`traceId` 的 ProblemDetails；不存在或他公司 ID 回 404。數量使用 decimal 定點型別的 JSON 數值，不使用浮點型別。

WU-PUR-001 註冊權限目錄和 Application 公司守衛；WU-PUR-002a 已建立兩張資料表。002b1 實作 GET 列表／詳情；002b2a 將 GET 限定於 SYS 授權資料範圍，範圍外詳情回 404，缺 VIEW 回 403，DTO 加上分店／部門歸屬。002b2b／002b2c 完成 POST、PUT、DELETE，003a～c 完成送審及審批歷史。`PUR.REQUISITION.EXPORT` 控制介面 4.8 CSV 按鈕；CSV 使用既有受 VIEW 授權的 GET 列表與詳情組合，沒有新增伺服器匯出端點。端點服務先檢查正數公司，查詢必須套用公司篩選，單筆讀取再檢查表頭公司。列表支援 `keyword`（單號）、`from`、`to`、`status`、`requesterId`、`page`、`pageSize`、`sort`（`documentNo`／`businessDate`；負號降冪）；詳情含已保存行、版本和歷次審批。

| Method | Path | 用途 | 權限 | 主要錯誤 |
|---|---|---|---|---|
| GET | `/` | 單號／日期／狀態／請購人篩選、白名單排序、穩定分頁 | VIEW | 400, 403 |
| GET | `/{id}` | 表頭、行、版本、審批歷史及已轉採購量 | VIEW | 404 |
| GET | `/item-options` | 搜尋目前公司啟用的 MD 物料，按代碼穩定分頁 | CREATE 或 EDIT | 400, 403 |
| GET | `/{id}/edit` | 修訂頁專用，按 EDIT 資料範圍讀已保存表頭、行與版本 | EDIT | 403, 404 |
| GET | `/{id}/saved-item-options` | 先檢 EDIT 資料範圍，再返回該單已保存物料名稱與啟用狀態 | EDIT | 403, 404 |
| POST | `/` | `{businessDate,remark,lines[]}` 建立草稿；回 201、Location 及保存詳情；請購人／分店／部門由 SYS 授權取得 | CREATE | 400, 403, 404, 422 |
| PUT | `/{id}?version=...` | `{businessDate,remark,lines[]}` 整組替換草稿或駁回單；駁回修訂轉回草稿 | EDIT | 400, 403, 404, 409, 422 |
| DELETE | `/{id}?version=...` | 軟刪草稿；回 204 | DELETE | 404, 409, 422 |
| POST | `/{id}/submit` | `{version}` 送 SYS 審批；回保存後詳情與 SYS 審批狀態；無適用步驟可立即核准 | SUBMIT | 403, 404, 409, 422 |

WU-PUR-002b2c 已提供 PUT／DELETE：兩端點都先按動作權限和 SYS 資料範圍定位單據，再檢查版本；PUT 在交易內驗 MD 引用並整組替換行，DELETE 只標記草稿軟刪。

`lines[]` 每行含 `lineNo,itemId,requiredDate,quantity,remark`，行號唯一；1～1000 行。核准與駁回只走 SYS 審批 API，沒有 PUR 直接 approve 端點。MD 物料與 SYS 審批只能透過各自 Application／Contracts 查詢，不跨模組讀表。前端選項須按 CREATE／EDIT 權限受權，詳情只讀已保存資料。

WU-PUR-003a 送審回應為 `{requisition,approval}`；重複送審、非草稿或舊版本拒絕，並維持原狀。歷次審批記錄與 SYS 最終決定由 003b 加入詳情；003a 不提供直接核准／駁回端點。

WU-PUR-003b：`GET /{id}` 增加 `approvalHistory[]`，按最新輪次在前，每輪含 `instanceId,status,submittedAt,steps[]`；逐步提供 `stepNo,name,status,actedBy,actedAt,comment`。必須先通過 PUR VIEW 公司及資料範圍守衛；最終決定使用既有 `POST /api/v1/sys/approvals/{id}/approve|reject`，PUR 不另開決定端點。

WU-PUR-004a：前端 `RequisitionService` 對齊上述六個端點；數量由表單十進位字串送入，真實 HTTP 已驗 ASP.NET Core 可解析並以 decimal 原值保存，避免在瀏覽器轉成二進位浮點。列表只送有效篩選值。建立／修改按畫面行序重新編號，不傳請購人／分店／部門。後續 004b～d 接入畫面、錯誤翻譯及完整 CSV。

WU-PUR-004c：新增兩個唯讀選項端點，均委派 MD `IInventorySelectionOptions`；已保存選項先經請購 EDIT 公司及資料範圍定位單據，再只把該單行上的物料 ID 交給 MD，含停用物料供顯示。另有編輯專用 `GET /{id}/edit`，使只有 EDIT、沒有 VIEW 權限的用戶也可載入其 EDIT 範圍內的單據；一般詳情仍使用 VIEW。新增選項僅啟用物料，後端 POST／PUT 仍重新驗證引用。匿名 401、缺權 403、資料範圍外 404；沒有跨模組資料表存取。

WU-PUR-004d：介面以目前已套用篩選重取 `GET /` 所有分頁（每頁 200），核對原總筆數與唯一 ID，再逐張讀 `GET /{id}` 的保存行。任一端點 403／404／5xx 或匯出期間資料變動，取消整個 CSV；最多 10000 明細行，BOM UTF-8 與公式字元跳脫由前端處理。EXPORT 是前端顯示權限；持 VIEW 者本來可透過唯讀 API 讀取相同資料，後端每次仍強制 VIEW 公司與資料範圍。

WU-PUR-005f2a1：前端草稿服務已對齊既有 `POST /api/v1/pur/rfqs` 與 `PUT /api/v1/pur/rfqs/{id}?version=`。Body 只含業務日期、回覆期限、備註、供應商 ID 及按畫面順序重新編號的行；六位小數數量作十進位字串傳送。表單與 RFQ 專屬 MD 選項端點留 005f2a2。

WU-PUR-005f2a2 後端切片：`GET /api/v1/pur/rfqs/item-options` 與 `/supplier-options` 只接受 `PUR.RFQ.CREATE` 或 `PUR.RFQ.EDIT`，以 `page`、`pageSize`、`keyword` 和代碼穩定分頁；前者只列目前公司啟用物料，後者只列啟用且已核准供應商。`GET /{id}/edit` 以 EDIT 動作資料範圍返回已保存詳情與版本，無需 VIEW。`GET /{id}/saved-options` 先驗同一 EDIT 範圍，才透過 MD 公開契約讀該詢價實際引用的物料與供應商代碼、名稱、啟用／核准旗標；軟刪及他公司資料不返回。無權 403，範圍外 404；無效分頁或排序 400。前端服務已接此四端點；表單、真實 SQL／HTTP 與畫面驗收在本單元續輪。

WU-PUR-005f2a2 續輪：`GET /api/v1/pur/rfqs/source-options?page=&pageSize=&keyword=&itemId=` 僅向目前公司 RFQ CREATE／EDIT 用戶提供已核准、未轉數量大於零的請購行；可按物料 ID 與請購單號篩選，依單號、行號、行 ID 穩定分頁。回傳行 ID、單號、行號、物料 ID、未轉數量；保存時仍由 RFQ 草稿服務重新驗來源與數量。真實 SQL 專項待服務恢復。

WU-PUR-005f2a2 已保存來源續輪：`GET /{id}/saved-options` 在 EDIT 詳情與資料範圍檢查後，另回 `sources`：該 RFQ 引用的請購行 ID、單號、行號、物料 ID、未轉量與 `isAvailable`。歷史引用即使請購後來遭駁回、軟刪或餘量歸零，仍可顯示原單號與不可用提示；查詢明確限定目前公司，其他公司引用不返回。新的來源選擇清單仍只列可用行，草稿保存仍由後端重驗。真實 SQL 測試已新增，待 SQL 服務恢復執行。
# WU-PUR-008a5：差異解決離線預檢

`PurchaseInvoiceResolution.Check` 只回傳差異行逐行決定與目標 Draft／Matched 狀態，不新增 API、DTO、資料表或畫面。後續 008a 的 RESOLVE 端點須先守衛 `PUR.INVOICE.RESOLVE`、目前公司／資料範圍及版本，在 SQL 鎖內重算並保存原因、決定人／時間與差額；拒絕須於同交易釋回預留。此切片沒有 HTTP 驗收。
CREATE／EDIT 在查詢來源前，以 BR-PUR-036d 驗表頭、供應商發票號及全部行；缺少整個 JSON body 回 400 `COMMON.REQUEST.VALIDATION_FAILED`，已提供 body 但表頭非法回 422 `PUR.INVOICE.INVALID_HEADER`，號碼與行沿用各自錯誤代碼。三字母原幣只作格式預檢，實際貨幣與訂單原幣一致性在 SQL 權威來源下驗證。CREATE／EDIT 已接入 HTTP；新增的空 body 分類與其餘邊界斷言待 WU-PUR-008a51 真實 SQL／HTTP 複驗。
WU-PUR-008a20 提供離線 Draft 快照組裝，將服務端公司身份、重新驗證的請求行與總額對照權威訂單／收貨身份；CREATE／EDIT 端點須在 008a 的交易鎖內讀取來源後接入。此切片未新增端點。
WU-PUR-008a21 要求 CREATE／EDIT 在同一鎖內為每個收貨來源載入已保存訂單行 ID、物料、原幣單價及稅率，交 Draft 組裝器一對一核對並保存逐行差異證據；合法差異仍是 Draft，不得在保存時改價或宣稱已 MATCH。此切片未新增端點。
WU-PUR-008a22 要求 CREATE／EDIT 在寫入前以 `PurchaseInvoiceDraftRecheck.Check` 和鎖內重讀來源驗證待保存快照；來源或價稅證據變動回 `PUR.INVOICE.DRAFT_SNAPSHOT_CHANGED`，不寫部分行。此切片未新增端點、資料表或權限實作。
WU-PUR-008a32 建立 Draft 表頭與行的 EF 模型及 migration；當時未提供 HTTP 端點，後續由 008a51 接入 CREATE／EDIT 路由、DTO、Swagger、授權及同交易保存。
WU-PUR-008a40 將 Draft EF 計畫的持有列交接接入 DbContext 狀態準備；沒有新增 HTTP 端點或 Swagger 行為。008a 寫入者仍須以鎖內完整快照同交易保存並處理衝突。
WU-PUR-008a47 註冊內部 `PurchaseInvoiceDraftSaveService` 與 session 工廠；服務由已驗 Draft 快照建立交易內讀取器和 SQL session，再呼叫保存協調器。當時未提供公開端點；後續 008a51 已接入 HTTP 權限與回應映射。
