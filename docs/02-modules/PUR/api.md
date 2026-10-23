# PUR API

### WU-PUR-006d 採購單 API 設計（以下端點尚未實作）

基址 `/api/v1/pur/purchase-orders`，Swagger 標籤 `PUR - Purchase Orders`。每個端點先驗目前公司、獨立 `PUR.ORDER.*` 權限及 SYS 動作資料範圍。人工 POST 不接受公司、單號、擁有人、金額合計或審計欄位；行使用 `itemId, requisitionLineId?, quantity, unitPrice, currencyCode, taxRateId`，數量／單價以十進位值或保真十進位字串傳遞。來源決標草稿仍由 005e4 的 `POST /rfqs/{id}/orders` 建立，訂單 POST 不接受 `awardId`。取得編輯詳情、MD 選項及請購來源需各自以 CREATE／EDIT 動作守衛，不能要求額外 MD.VIEW 或 ORDER.VIEW；先查訂單動作範圍再查保存引用，避免洩漏。

| Method | Path | 用途／回應 | 權限 | 主要錯誤 |
|---|---|---|---|---|
| GET | `/` | `keyword,from,to,supplierId,status,requisitionLineId,page,pageSize,sort`；穩定分頁 | `PUR.ORDER.VIEW` | 400、403 |
| GET | `/{id}` | 保存行、決標／請購來源、餘量、版本及 SYS 全輪次歷史 | `PUR.ORDER.VIEW` | 403、404 |
| GET | `/create-options` | 目前公司有效供應商、物料、幣別及日期有效進項稅率的分頁選項 | `PUR.ORDER.CREATE` | 400、403 |
| GET | `/{id}/edit` | EDIT 動作範圍內可修訂詳情和保存引用狀態 | `PUR.ORDER.EDIT` | 403、404、422 |
| GET | `/requisition-options` | 依物料／單號查同公司已核准且餘量大於零的請購行，並回原量／已轉量／餘量 | `PUR.ORDER.CREATE` 或 `PUR.ORDER.EDIT` | 400、403 |
| POST | `/` | 原子建立人工 Draft，201 回 ID、單號、行數、版本 | `PUR.ORDER.CREATE` | 400、409、422 |
| PUT | `/{id}?version=` | 整組修訂可編輯草稿，200 回保存詳情 | `PUR.ORDER.EDIT` | 400、404、409、422 |
| DELETE | `/{id}?version=` | 軟刪未提交草稿，204 | `PUR.ORDER.DELETE` | 404、409、422 |
| POST | `/{id}/submit` | `{version}`；同交易消耗請購量與建立 SYS 流程，200 回狀態／版本 | `PUR.ORDER.SUBMIT` | 400、404、409、422 |
| POST | `/{id}/cancel` | `{version,reason}`；核准且無入庫引用才原子釋回，200 回狀態／版本 | `PUR.ORDER.CANCEL` | 400、404、409、422 |

核准／駁回只走 SYS `/api/v1/sys/approvals/{id}/approve|reject`（`SYS.APPROVAL.PROCESS`）；PUR 結果處理器在 SYS 交易內更新訂單，駁回釋回已轉量。提交時以請購行更新鎖和訂單版本處理並發，聚合同一請購來源量後整單檢查；不足回 422 並列行號、請購單號與可用量。匿名 401、缺權 403、跨公司／範圍外 404、格式 400、舊版 409、狀態及失效引用 422、例外 500，均用含 `errorCode` 與 `traceId` 的 ProblemDetails。供應商／物料／稅率等只透過 MD 公開契約取得。INV 過帳不由本資源直接觸發。

### WU-PUR-005f2c 詢價 CSV／列印

`PUR.RFQ.EXPORT` 控制 4.11 的 CSV 按鈕；匯出及列印組合既有 `GET /api/v1/pur/rfqs`、`GET /api/v1/pur/rfqs/{id}`，沒有新寫入 API 或資料表。每個請求仍需 `PUR.RFQ.VIEW` 並套用公司與 SYS VIEW 資料範圍。匯出先核對所有分頁的總筆數、唯一 ID，再逐張核對保存版本與明細，任何 400／403／404／409／422／500、逾時或資料變動整檔取消。列印重新 GET 單張詳情後才呼叫瀏覽器列印；讀取失敗不列印。

### WU-PUR-005e4 轉單草稿契約（待實作）

`POST /api/v1/pur/rfqs/{id}/orders` 使用 `PUR.RFQ.CONVERT`。Body：`version`（目前詢價版本）、`supplierId`、`awardIds`（1～1000 個不同的正數 ID）。不接受公司、單號、價格、數量或審計欄位。成功 201 回 `orderId`、`documentNo`、`status=Draft`、`lineCount`、`awardIds` 與新詢價版本；另以受權的訂單詳情讀取已保存行及決標快照。此端點只接受同一已發送詢價、同一供應商、同一原幣且尚未轉單的決標行。明確選擇行集合，不自行略過已轉行；空集合 400，混入無效 ID／重複 ID 400，來源錯配 404，已轉或詢價非 Sent 422，舊版本 409。

服務以目前公司及 SYS CONVERT 動作資料範圍定位詢價，鎖表頭後驗版本、行集合與來源唯一性；在同一 SQL 交易內用 SYS 公開單號契約發號、建立表頭／行／來源連結並遞增詢價版本。持久化快照取決標保存值，不重新以今日 MD 匯率改價；轉單不改請購已轉量或 INV 庫存。任何一行衝突或保存故障整批回滾，成功回應只在提交後發出。重送同一版本回 409，使用新版本重送已轉行回 422，兩者都返回可辨識的衝突原因，不再建單。匿名 401、缺權 403、跨公司／資料範圍外 404；所有錯誤用含 `errorCode` 與 `traceId` 的 ProblemDetails。前端訊息與 E2E 由 005f 完成。

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
