# INV API

## WU-INV-012d／012a／012b 拆卸入庫 API 契約

`/api/v1/inv/disassemblies`，Swagger 標籤 `INV - Disassemblies`；只處理目前公司。權限 `INV.DISASSEMBLY.VIEW/CREATE/EDIT/DELETE/SUBMIT`，核准與駁回只經 SYS 審批 API，無直接 approve 端點。所有錯誤使用含 `errorCode`、`traceId` 的 ProblemDetails；他公司或不存在 ID 回 404。

| Method | Path | 用途 | 權限 | 主要錯誤 |
|---|---|---|---|---|
| GET | `/` | `keyword`、`from`、`to`、`status`、`finishedItemId`、`sort`、`page`、`pageSize`；回單號、日期、狀態、成品及穩定分頁總數 | VIEW | 400 篩選／排序，403 缺權 |
| GET | `/{id}` | 已保存表頭、成品、組件、權重、追蹤、過帳單價、版本及 SYS `approvalHistory[]` | VIEW | 404 `INV.DISASSEMBLY.NOT_FOUND` |
| GET | `/{id}/saved-references` | 只對已授權單據回保存後已停用的 MD 引用名稱及停用狀態 | VIEW | 404 |
| POST | `/` | 取 SYS 單號並存不改庫存的草稿，回 201 | CREATE | 400 欄位／行，404／422 MD 引用 |
| PUT | `/{id}` | `{version,businessDate,remark,finished,components[]}` 整組替換草稿／駁回單 | EDIT | 409 版本，422 `NOT_DRAFT` |
| DELETE | `/{id}?version=...` | 僅草稿軟刪，回 204 | DELETE | 409 版本，422 `NOT_DRAFT` |
| POST | `/{id}/submit` | `{version}`；驗追蹤後送 SYS；無審批步驟時於同交易立即核准過帳；有步驟時由 SYS 最終核准處理器過帳 | SUBMIT | 409 版本／來源衝突，422 追蹤／庫存／成本原因 |

`finished` 帶 `lineNo,itemId,warehouseId,locationId?,quantity,tracking[]`；`components[]` 另帶 `allocationWeight`。成品一行，組件 1–999 行，總數 ≤1000；數量及權重為六位十進位字串，行號全單唯一且為正數。請求不收方向、成本或審批人；`postedUnitCost` 由核准處理器計算，過帳來源固定 `DISASSEMBLY`。列表與選項經 MD Contracts，審批歷史經 SYS Application 契約，不直接讀其資料表。選項端點須將拆卸 CREATE／EDIT 列為可讀權限；只有 VIEW 不可獲得建立選項。

核准成本錯誤碼：`INV.DISASSEMBLY.COST_UNKNOWN`（成品無可靠平均）、`INV.DISASSEMBLY.COST_PRECISION`（四位行值無法用六位單價重算）、`INV.DISASSEMBLY.COST_OVERFLOW`（估值或分攤超出十進位精度）；三者均為 422，單據及 SYS 決定回滾。來源位置不足為 `INV.STOCK.INSUFFICIENT`，批序號不足／衝突沿用 INV 追蹤錯誤碼。

## WU-INV-013 安全庫存 API 契約（013d／013a／013b 完成）

`/api/v1/inv/safety-stock-thresholds`，Swagger 標籤 `INV - Safety Stock`。所有錯誤為含 `errorCode`、`traceId` 的 ProblemDetails；公司隔離由 DbContext filter 與服務端驗證共同保護。數量使用基本單位十進位字串，六位精度。

| Method | Path | 用途 | 權限 | 主要錯誤 |
|---|---|---|---|---|
| GET | `/` | `keyword`（物料代碼／名稱）、`warehouseId`、`lowOnly`、`sort`（`itemCode`／`-itemCode`／`shortage`／`-shortage`）、`page`、`pageSize`；回 `items[]`、`totalCount`、`queriedAt`，每行含門檻、現存、低量、建議補足、停用標記及版本；低量篩選在分頁前 | VIEW | 400 篩選／排序，403 缺權 |
| POST | `/` | `{itemId, warehouseId, minimumQuantity}` 建立門檻；已軟刪同鍵則恢復原 ID，回 201／200 | EDIT | 400 數量，404／422 MD 引用，409 重複有效鍵 |
| PUT | `/{id}` | `{version, minimumQuantity}` 修改目前公司有效門檻 | EDIT | 404 他公司／不存在，409 舊版本，422 引用已停用 |
| DELETE | `/{id}?version=...` | 軟刪除門檻，回 204；不影響庫存 | EDIT | 404 他公司／不存在，409 舊版本 |

無設定門檻的物料不出現在列表。現存量以 `In - Out` 加總已過帳異動，所有儲位合併；不讀取草稿、待審或採購在途。負數結存若因舊資料存在仍如實顯示，建議量為門檻減現存；不在讀取路徑修正資料。查詢開始時取得 `IClock.Now` 作為 `queriedAt`，提示資料會隨交易變動。MD 引用與名稱都走 Contracts，INV 不直接讀 MD 表；低量報表不授予 MD 主檔 VIEW 權限。

013a 已接通門檻 CRUD、MD 保存引用名稱、關鍵字、四種排序與恢復時 200 狀態。無關鍵字的代碼排序經 `IInventorySelectionOptions.SavedItemsByCodeAsync` 在 MD 端按目前公司、代碼與 ID 排序，逐頁回傳包含停用但不含已刪物料；INV 只以這些 ID 查自己的門檻，取得目標頁即停止，低量狀態仍由 INV 先篩選並計算總數；總數為零時直接返回，不掃描 MD。關鍵字改經 `SearchSavedItemIdsAsync` 在 MD 端先搜尋代碼／名稱，再由 INV 對本公司門檻做分頁前過濾；不向 MD 暴露 INV 門檻表。萬筆升降冪首末頁、同物料跨倉穩定排序與 500 traceId 後端測試已驗；前端三語與模擬瀏覽器畫面由 013b 驗收。

013b 的門檻頁使用 `INV.SAFETY_STOCK.VIEW` 列表，`EDIT` 寫入。INV 選擇端點的 `items` 另授 `SAFETY_STOCK.EDIT`，`warehouses` 授 `SAFETY_STOCK.VIEW／EDIT` 供篩選及新增；唯讀角色不能讀取全部物料或儲位選項。頁面讀取或寫入 15 秒後顯示逾時與重試，不把載入失敗當成零門檻。

## WU-INV-011 組裝入庫 API 契約（011a 草稿、011b2 審批過帳已實作）

`/api/v1/inv/assemblies`，Swagger 標籤 `INV - Assemblies`；各動作以 `INV.ASSEMBLY.VIEW/CREATE/EDIT/DELETE/SUBMIT` 保護，僅當前公司。核准／駁回走 SYS 審批 API，單據端沒有直接 approve 端點。錯誤為含 errorCode／traceId 的 ProblemDetails。

| Method | Path | 用途 | 權限 | 主要錯誤 |
|---|---|---|---|---|
| GET | `/` | `keyword`（單號／備註）、`from`、`to`、`status`、`finishedItemId`、`sort`、`page`、`pageSize`；穩定分頁，日期或單號排序；每頁批次經 MD Contracts 補成品代碼／名稱與成品數量 | VIEW | 400 |
| GET | `/{id}` | 已保存表頭、成品與耗料、批序號、版本、最新 SYS 審批狀態及 `approvalHistory[]`（由新到舊，含步驟狀態、處理時間與意見） | VIEW | 404 `INV.ASSEMBLY.NOT_FOUND` |
| GET | `/{id}/saved-references` | 僅依已授權單據回已保存而現已停用的 MD 引用顯示資料 | VIEW | 404 |
| POST | `/` | 取 `INV.ASSEMBLY` 單號、保存草稿，回 201 | CREATE | 400；404／422 MD；422 無單號規則 |
| PUT | `/{id}` | `{ version, businessDate, remark, finished, components[] }` 整組替換草稿／駁回單 | EDIT | 409 版本；422 `NOT_DRAFT` |
| DELETE | `/{id}?version=...` | 僅草稿軟刪，回 204 | DELETE | 409；422 `NOT_DRAFT` |
| POST | `/{id}/submit` | `{ version }`；檢查 tracking，送 SYS，無審批步驟時同交易核准過帳 | SUBMIT | 409；422 `TRACKING.REQUIRED`／`STOCK.INSUFFICIENT`／`INV.ASSEMBLY.COST_UNKNOWN`／`COST_PRECISION`／`COST_OVERFLOW` |

`finished` 與每個 `components[]` 帶 `lineNo, itemId, warehouseId, locationId?, quantity, tracking[]`；數量為六位十進位字串。每行追蹤資料沿用 ADR-060。成品與耗料 `postedUnitCost` 只由伺服器在最終核准時計算，請求沒有成本欄；異動帳出庫單價仍為 null，耗料的鎖定單價保存於組裝行。成品的 lineNo 與耗料行號須唯一且正數，總行數上限 1000；組件不可與成品同物料。`source_type=ASSEMBLY`、`source_id=assembly.id`，過帳方向由已保存行 role 決定，不能由請求指定。列表、詳情與選項只經 MD Contracts，不直接讀 MD 表。所有組裝物料按 ID 升序取得交易鎖後計算成本；成品六位單價乘數量按四位捨入必須等於耗料四位行值總和，否則拒絕核准並回滾。

審批歷史由 `IApprovalHistoryReader` 經 SYS Application 契約查詢，INV 不直接讀 `sys_` 表；INV 端先驗證單據 VIEW 權限與公司歸屬，再回傳該單據所有送審輪次。VIEW 角色可看本單據的駁回原因，毋須 SYS 審批管理權限。選項端點另需 INV 單據 CREATE／EDIT 權限，只有組裝 VIEW 的角色取得 403。

## WU-INV-015a 跨模組 Application 契約（ADR-064；015b 待 PUR／SD）

PUR／SD 應從已保存單據明細呼叫 `IExternalInventoryPostingService.PostPurchaseInboundAsync`／`PostSalesOutboundAsync`，使用 `PurchaseInboundLine`／`SalesOutboundLine`；INV 固定來源 `PURCHASE_INBOUND`／`SALES_OUTBOUND`、方向 In／Out，銷售成本固定 null。呼叫前必須建立包含 SYS 核准決定與來源狀態的交易，否則回 `INV.POSTING.TRANSACTION_REQUIRED`。`SourceId` 是來源模組單據 ID；`BusinessDate`、行號、基本單位數量、倉儲位置與批序號一律取已保存快照。既有通用 `IInventoryPostingService` 仍供 INV 自有單據使用；PUR／SD 不應引用它。相同快照重放回原異動 ID，差異回 409 `INV.POSTING.CONFLICT`。015b 由 PUR／SD 回呼處理器驗證目前公司、待審狀態及版本，與 SYS 決定、來源狀態及 INV 異動共用交易；駁回不呼叫過帳。INV 不查 PUR／SD 表，也不接受用戶端直接宣稱核准。

BlueWhale 的 Inventory 5.8 Sales Outbound (Approval)／5.19 Purchase Inbound (Approval) 只作介面對照；MyERP 操作介面由 SD／PUR 頁或 SYS 待審任務承載，不另開 INV 寫入 API。INV 側真實 SQL 驗收見 INV-XMOD-001～004／006；實際單據與 HTTP 驗收留 015b。

## WU-INV-014d／014a 估值查詢契約（014b 公開 API 待實作）

內部 `IInventoryValuationQuery.GetItemAsync(itemId, cutoffPostedAt?)` 只讀目前公司，呼叫 `usp_inv_value_item`；回 `itemId, cutoffPostedAt, onHand, value?, averageUnitCost?, unpricedQuantity, status, movementCount, provisional=true`。截止時間空值代表查詢開始時的目前已提交資料；歷史查詢依過帳時間截止，不依業務日期重新排序。`status` 為 `Valued`／`Unpriced`；SQL 負池量、調撥不配對、溢位回業務錯誤，不給部分估值。

014b 公開 `GET /api/v1/inv/valuations/items/{itemId}?cutoffPostedAt=...`，Swagger 標籤 `INV - Valuation`，權限 `INV.VALUATION.VIEW`；經 MD 唯讀契約確認物料屬目前公司（停用物料仍可查歷史），不存在或屬他公司回 404 `MD.ITEM.NOT_FOUND`。非法日期回 400 ProblemDetails，無物料異動回零數量／零金額，未定價回 200 且 `value=null`。前端 `datetime-local` 以 UTC+8 本地時間傳遞，不轉 UTC。FI 將來只可依 INV 內部契約讀取，正式過帳需另行期間封存與 `IGlPostingService`。

`GET /api/v1/inv/valuations/items?page=1&pageSize=100[&keyword=...][&sort=code|-code|name|-name]` 也只需 `INV.VALUATION.VIEW`：經 MD 唯讀契約列出目前公司可選物料（含停用但可查歷史者），預設按代碼／ID 穩定分頁，代碼或名稱關鍵字篩選，回 `{ items: [{ id, code, name }], page, pageSize, totalCount }`；前端不依賴 `MD.ITEM.VIEW`。

## WU-INV-010a 盤點 API（已實作：010a1 列表、詳情、建立、保存實盤、重新快照、刪除；010a2 `variance`、`submit` 與 SYS 審批處理器）

`/api/v1/inv/stocktakes`，Swagger 標籤 `INV - Stocktakes`，依動作授權 `INV.STOCKTAKE.VIEW/CREATE/EDIT/DELETE/SUBMIT`，只接受目前公司。本資源沒有直接核准端點，核准與駁回使用 SYS 審批 API。錯誤一律回 ProblemDetails。列表排序可用 `businessDate`（預設降冪）或 `documentNo`，同值以 ID 穩定排序。

| Method | Path | 用途 | 權限 | 主要錯誤 |
|---|---|---|---|---|
| GET | `/` | 參數 `keyword`、`from`、`to`、`status`、`sort`、`page`、`pageSize`；回單號、日期、狀態、倉庫數、行數、已盤行數、版本 | VIEW | 400 |
| GET | `/{id}` | 回表頭、範圍、快照時間、依行號排列的明細（帳面、實盤、新增／重盤標記、批序號），以及最新 SYS 審批狀態 | VIEW | 404 `INV.STOCKTAKE.NOT_FOUND` |
| GET | `/{id}/variance` | 差異報告：參數 `differenceOnly`；每行帳面、實盤、差異與類型，另回盤盈／盤虧總數量及行數 | VIEW | 404 |
| POST | `/` | 參數 `businessDate`、`remark`、`scope { warehouseIds, categoryIds, itemIds }`；同交易取 `INV.STOCKTAKE` 單號並寫快照，回 201 草稿 | CREATE | 400；404／422 MD；422 `TOO_MANY_LINES`／`SCOPE_IN_USE`／無單號規則 |
| PUT | `/{id}` | 帶 `version`，整組保存日期、備註、各行及批序號的實盤，可附新增行；用於草稿或已駁回單（駁回單保存後回草稿） | EDIT | 400；409 版本；422 `INV.STOCKTAKE.NOT_DRAFT`／`SCOPE_IN_USE`／`LINE_OUT_OF_SCOPE` |
| POST | `/{id}/refresh-snapshot` | 參數 `{ version }`；依原範圍重新快照，保留實盤並標記重盤行 | EDIT | 409；422 `NOT_DRAFT`／`TOO_MANY_LINES`／`SCOPE_IN_USE` |
| DELETE | `/{id}?version={version}` | 軟刪除草稿，回 204 | DELETE | 409；422 `NOT_DRAFT` |
| POST | `/{id}/submit` | 參數 `{ version }`；先檢查已全盤與快照未過期，再送 SYS 工作流；沒有審批步驟時同交易過帳 | SUBMIT | 404；409 版本／`INV.SERIAL.IN_STOCK`；422 `NOT_DRAFT`／`COUNT_MISSING`／`SNAPSHOT_STALE`（`changedKeys` 最多 50 個）／`TOO_MANY_LINES`（盈虧異動超過 1,000 筆或批序號差異超過 10,000 個）／庫存或批序號不足 |

`SNAPSHOT_STALE` 的 ProblemDetails 另帶 `changedKeys: [{ itemId, warehouseId, locationId }]`，依物料、倉庫、儲位排序。範圍包括原範圍的物料，以及新增行的「物料＋倉庫＋儲位」鍵（新增行的物料可在類別／物料範圍外）。

差異報告回 `id`、`documentNo`、`businessDate`、`status`、`lines[]`、`gainQuantity`、`lossQuantity`、`gainLineCount`、`lossLineCount`、`matchLineCount`、`uncountedLineCount`；合計一律按全部行計算，不受 `differenceOnly` 影響。每行：
- `difference`（實盤 − 帳面，未盤為 null）與 `type`（Gain／Loss／Match／Uncounted）按行淨額判定。
- `gainQuantity`／`lossQuantity` 按過帳粒度（批序號物料逐號碼），所以批號 A 盤盈、批號 B 盤虧的行，`type` 可為 Match，但仍兩者皆大於 0，並計入盤盈及盤虧行數。
- `differenceOnly=true` 只列 `gainQuantity` 或 `lossQuantity` 大於 0 的行（即會產生異動的行）。
- `tracking[]`：`lotNo`／`serialNo`、帳面、實盤、差異。

明細欄位：
- 保存實盤時以 `lineId` 指向快照行；新增行省略 `lineId`，改帶 `itemId`、`warehouseId`、`locationId?`。
- `countedQuantity`：null 或大於等於 0，最多六位小數，可用十進位字串。
- `tracking[]`：`lotNo` 或 `serialNo`，加上 `countedQuantity`；序號只能是 0 或 1。新增的號碼帳面為 0。
- 新增行的倉庫必須在範圍內；物料、倉庫、儲位須通過 MD guard 驗證。

列表與詳情不跨模組 JOIN MD，名稱由前端經 MD API 取得。物料類別範圍由 MD 唯讀契約 `IItemCategoryScope` 展開（含下層類別、只取啟用物料）。

SYS 文件類型為 `INV.STOCKTAKE`，審批金額為 0。最終核准在審批交易內，以來源 `STOCKTAKE` + 單據 ID 呼叫一次過帳服務：
- 每行的正差異合計為一筆 `In`，負差異合計為一筆 `Out`，兩筆同 `lineNo`，批序號按差異拆分，成本空。
- 差異為 0 不產生異動；全部行都無差異時不呼叫過帳服務，仍轉 Posted。
- 盤虧不足回 422 `INV.STOCK.INSUFFICIENT`／`INV.LOT.INSUFFICIENT`／`INV.SERIAL.NOT_AVAILABLE`，盤盈序號已在他處現存回 409 `INV.SERIAL.IN_STOCK`；SYS 決定與全部異動一起回滾，單據仍待審。
- 重複審批回 409 `INV.STOCKTAKE.APPROVAL_STATE_CHANGED`；同來源不同內容重放回 409 `INV.POSTING.CONFLICT`。

## WU-INV-009a 庫存調撥 API（後端已實作）

`/api/v1/inv/transfers`，Swagger 標籤 `INV - Transfers`；依動作授權 `INV.TRANSFER.VIEW/CREATE/EDIT/DELETE/SUBMIT`。只接受目前公司授權；核准與駁回使用 SYS 審批 API，本資源沒有直接核准端點。錯誤一律 ProblemDetails。列表排序 `businessDate`（預設降冪）或 `documentNo`，同值以 ID 穩定排序。

| Method | Path | 用途 | 權限 | 主要錯誤 |
|---|---|---|---|---|
| GET | `/` | `keyword`、`from`、`to`、`status`、`sort`、`page`、`pageSize`；回單號、日期、狀態、行數、總數量、版本 | VIEW | 400 |
| GET | `/{id}` | 表頭、依行號的來源與目的、批序號、最新 SYS 審批狀態 | VIEW | 404 `INV.TRANSFER.NOT_FOUND` |
| POST | `/` | `businessDate`、`remark`、`lines`；同交易取 `INV.TRANSFER` 單號，回 201 草稿 | CREATE | 400、404／422 MD、422 無單號規則 |
| PUT | `/{id}` | 附 `version` 整組替換草稿或已駁回內容 | EDIT | 409 版本、422 `INV.TRANSFER.NOT_DRAFT` |
| DELETE | `/{id}?version={version}` | 軟刪除草稿，回 204 | DELETE | 409 版本、422 `NOT_DRAFT` |
| POST | `/{id}/submit` | `{ version }`；送 SYS 工作流，無步驟即同交易過帳 | SUBMIT | 409 版本、422 `NOT_DRAFT`／庫存不足／批序號不足 |

明細：`lineNo`（正數且唯一）、`itemId`、`sourceWarehouseId`、`sourceLocationId?`、`destinationWarehouseId`、`destinationLocationId?`、`quantity`（正數、最多六位小數，可用十進位字串）、`tracking[]`（批號／序號與數量，格式沿用 ADR-060；調撥不修改批號日期）。來源與目的結存鍵必須不同，同倉不同儲位允許；草稿可暫缺 tracking，送審前必須完整。列表與詳情不跨模組 JOIN MD，名稱由前端 MD API 取得。

SYS 文件類型 `INV.TRANSFER`；最終核准在審批交易內以來源 `TRANSFER`、單據 ID 呼叫一次過帳服務，每行拆為同 `lineNo` 的 `Out`／`In`，物料、數量和 tracking 完全相同，成本皆空。重複審批回 409 `INV.TRANSFER.APPROVAL_STATE_CHANGED`；重放異動依來源快照回原結果，不增加異動。

## WU-INV-008b 批號與序號

### 單據明細的 `tracking`

其他入庫、其他出庫、期初的明細請求加可選 `tracking[]`：`{ lotNo?, serialNo?, quantity, manufactureDate?, expiryDate? }`，批號或序號擇一；`quantity` 可為字串；序號數量固定 1；日期只用於入庫／期初批號。回應明細同樣帶 `tracking[]`（依輸入順序）。草稿（其他入庫／出庫）可暫缺批序號；送審檢查與物料追蹤方式相符且合計等於數量。

| 情況 | 狀態 | errorCode／欄位 |
|---|---|---|
| 格式錯誤（擇一、長度、數量、序號數量非 1、日期用於出庫或序號、有效期限早於生產日期、行內重複、混用、同來源序號重複） | 400 | `lines[i].tracking[j]...` |
| 不追蹤物料帶批序號、批號／序號種類不符、序號物料數量非整數 | 400 | `lines[i].tracking`／`lines[i].quantity` |
| 批序號缺少或合計不等於數量（送審、核准、期初提交） | 422 | `INV.TRACKING.REQUIRED` |
| 既有批號日期不同 | 422 | `INV.LOT.ATTRIBUTE_MISMATCH` |
| 出庫批號在該倉庫／儲位不足 | 422 | `INV.LOT.INSUFFICIENT` |
| 出庫序號不在該倉庫／儲位 | 422 | `INV.SERIAL.NOT_AVAILABLE` |
| 入庫序號仍在庫 | 409 | `INV.SERIAL.IN_STOCK` |

期初預覽改為逐行錯誤：`{ field: "tracking", code: "INV.TRACKING.REQUIRED" | "INV.TRACKING.MODE_MISMATCH" }`。

### 追溯查詢

`/api/v1/inv/tracking`，Swagger 標籤 `INV - Lot and Serial Tracking`；權限 `INV.TRACKING.VIEW`。

| Method | Path | 用途 | 主要錯誤 |
|---|---|---|---|
| GET | `/lots` | `itemId`、`keyword`（批號）、`inStockOnly`、`page`、`pageSize`；回 `id, itemId, lotNo, manufactureDate, expiryDate, onHand`，按物料、批號排序 | 400 |
| GET | `/lots/{id}` | `header`（同列表列）、`locations[{warehouseId, locationId, quantity}]`、`history[{movementId, businessDate, postedAt, sourceType, sourceId, sourceLineNo, direction, warehouseId, locationId, quantity}]`（新到舊最多 1000）、`historyTruncated` | 404 `INV.LOT.NOT_FOUND` |
| GET | `/serials` | `itemId`、`keyword`（序號）、`inStockOnly`；回 `id, itemId, serialNo, inStock` | 400 |
| GET | `/serials/{id}` | 同批號追溯，`locations` 為空表示已出庫 | 404 `INV.SERIAL.NOT_FOUND` |

## WU-INV-007a 物料期初庫存

`/api/v1/inv/opening-stocks`，Swagger 標籤 `INV - Opening Stocks`；權限 `INV.OPENING_STOCK.VIEW`、`INV.OPENING_STOCK.CREATE`。

| Method | Path | 用途 | 權限 | 主要錯誤 |
|---|---|---|---|---|
| GET | `/` | `from`／`to`（啟用日期）、`itemId`、`warehouseId`、`keyword`（備註、請求鍵）、`page`、`pageSize`；啟用日期與 ID 降冪；每列行數、總數量、總金額 | VIEW | 400 |
| GET | `/{id}` | 表頭與依行號排序的明細 | VIEW | 404 `INV.OPENING_STOCK.NOT_FOUND` |
| POST | `/preview` | 與提交相同的請求；回 `lines[{lineNo, amount, errors[{field, code}]}]`、`errorCount`、`totalQuantity`、`totalAmount`，不寫資料 | CREATE | 400 |
| POST | `/` | `requestKey`、`openingDate`、`remark`、`lines`；201 新批次，200 為相同請求重送（`replayed = true`） | CREATE | 400、409 `INV.OPENING_STOCK.REQUEST_CONFLICT`、422 `INV.OPENING_STOCK.HAS_ERRORS` |

明細欄位：`lineNo`（正數、批次內唯一）、`itemId`、`warehouseId`、`locationId?`、`quantity`（基本單位 >0，最多 6 位小數）、`unitCost`（≥0，最多 6 位小數），最多 1000 行，金額合計小於 10^15。`requestKey` 為 1–64 個英數、`-`、`_`，建議用戶端每批產生 GUID。逐行錯誤碼：`MD.ITEM.NOT_FOUND|INACTIVE`、`MD.WAREHOUSE.NOT_FOUND|INACTIVE`、`MD.STORAGE_LOCATION.NOT_FOUND|INACTIVE`、`INV.OPENING_STOCK.DUPLICATE_IN_BATCH`、`INV.OPENING_STOCK.DUPLICATE`、`INV.OPENING_STOCK.AFTER_MOVEMENT`。前端（007b）以 `errors.*` 三語顯示。

## WU-INV-006a 結存與異動查詢

| Method | Path | 篩選／排序 | 權限 | 回應 |
|---|---|---|---|---|
| GET | `/api/v1/inv/balances` | `itemId`、`warehouseId`、`locationId`（指定儲位；未指定包含所有）、`page`、`pageSize`；物料／倉庫／儲位 ID 升冪 | `INV.INVENTORY.VIEW` | `PagedResult`，每列物料、倉庫、儲位 ID 與六位精度結存；零結存不列 |
| GET | `/api/v1/inv/movements` | `itemId`、`warehouseId`、`locationId`、`from`／`to` 業務日期（含頭尾）、`direction`、`page`、`pageSize`；業務日期／ID 降冪 | `INV.MOVEMENT.VIEW` | `PagedResult`，含來源、方向、數量、成本（可空）、過帳時間與代碼快照 |

兩端點僅查目前公司，非法 ID、日期範圍或分頁回 400 ProblemDetails；不跨模組 JOIN MD 表。Swagger 由 XML 註解與回應型別描述。006b 以此 API 製作三語頁、列印及篩選 CSV 匯出。

WU-INV-001 只建立模組與權限目錄，無公開 HTTP 動作。`INV.INVENTORY.VIEW` 預留給 WU-INV-006 庫存查詢；`INV.MOVEMENT.VIEW` 預留給異動明細。後續 API 沿用 `/api/v1/inv/`、Swagger、ProblemDetails 與 `X-Company-Id` 授權規範。

WU-INV-002 增加內部 `IInventoryReferenceGuard.RunWithCodesAsync(itemId, warehouseId, locationId, action)` 契約及 `InventoryReferenceService.RecordAsync(sourceKey, itemId, warehouseId, locationId)`；INV 寫入服務須把來源與引用的驗證及保存放在同一個交易，交易提交後才釋放鎖。重複來源鍵回 `INV.REFERENCE.DUPLICATE`（409），已刪主檔回相應 MD 404。本單元無公開 HTTP API。

WU-INV-003 增加內部 `IInventoryPostingService.PostAsync(InventoryPostingRequest)`，來源類型、來源 ID、業務日期及全部異動行由核准單據服務傳入；呼叫方須在同一交易確認核准狀態。相同來源相同內容回 `Replayed=true` 與原異動 ID；不同內容回 `INV.POSTING.CONFLICT`（409）；不足庫存回 `INV.STOCK.INSUFFICIENT`（業務錯誤）。`IStockBalanceQuery.GetOnHandAsync` 與 `ListAsync` 從異動帳加總、由 DbContext 公司篩選。這些是進程內 Contracts，公開 HTTP 與 Swagger 端點隨 WU-INV-006 提供。

## WU-INV-004 其他入庫 API（004a 已實作）

所有路徑均為 `/api/v1/inv/other-receipts`（Swagger 標籤 `INV - Other Receipts`），需要目前公司授權，錯誤採 ProblemDetails。權限：`INV.OTHER_RECEIPT.VIEW`、`CREATE`、`EDIT`、`DELETE`、`SUBMIT`；核准／駁回由 SYS 審批 API（`/api/v1/sys/approvals/{id}/approve|reject`）執行，本資源沒有直接核准端點。

| Method | Path | 用途／主要欄位 | 權限 | 主要錯誤 |
|---|---|---|---|---|
| GET | `/` | `keyword`（單號、備註）、`from`、`to`（業務日期）、`status`、`sort`（`businessDate` 預設降冪或 `documentNo`，一個欄位）、`page`、`pageSize`；每列含行數、總數量、總金額與版本 | VIEW | 400 |
| GET | `/{id}` | 表頭、依行號排序的明細（含 `amount`）、最近一次 SYS 審批狀態 `approval` | VIEW | 404 `INV.OTHER_RECEIPT.NOT_FOUND` |
| POST | `/` | `businessDate`、`remark`、`lines`；同交易取 `INV.OTHER_RECEIPT` 單號；回 201 | CREATE | 400、404（MD 引用）、422（MD 停用、未設單號規則） |
| PUT | `/{id}` | 完整替換草稿或已駁回單據的日期、備註與明細，附 `version`；駁回單據回到草稿 | EDIT | 409 版本、422 `INV.OTHER_RECEIPT.NOT_DRAFT` |
| DELETE | `/{id}?version={version}` | 軟刪除草稿；回 204 | DELETE | 409、422 `NOT_DRAFT` |
| POST | `/{id}/submit` | `{ version }`；呼叫 SYS 工作流，立即核准時同交易過帳並回 `Posted` | SUBMIT | 409、422 `NOT_DRAFT`、422 `INV.STOCK.*`／MD 停用 |

明細欄位：`lineNo`（正數、單據內唯一）、`itemId`、`warehouseId`、`locationId?`、`quantity`（基本單位，>0，最多 6 位小數）、`unitCost`（≥0，最多 6 位小數），最多 1000 行，金額合計須小於 10^15。回應只含 MD ID，名稱由前端查 MD。SYS 最終結果由 `OtherReceiptApprovalResultHandler`（`DocumentType = INV.OTHER_RECEIPT`）在 SYS 審批交易內處理：核准即呼叫 `IInventoryPostingService`（來源類型 `OTHER_RECEIPT`、每行一筆入庫），駁回只改狀態；非待審單據回 409 `INV.OTHER_RECEIPT.APPROVAL_STATE_CHANGED`，整個審批動作回滾。

列印與供 Excel 開啟的 CSV 匯出由 WU-INV-004b 前端提供（ADR-056），沿用上述列表／詳情 API 與 VIEW 權限；CSV 逐頁載入目前篩選結果並防止試算表公式注入。原生 `.xlsx` 與大量資料匯出在 WU-INV-017 評估。

## WU-INV-005a 其他出庫 API

`/api/v1/inv/other-issues`，Swagger 標籤 `INV - Other Issues`；權限 `INV.OTHER_ISSUE.VIEW/CREATE/EDIT/DELETE/SUBMIT`。列表與詳情、草稿 CRUD、SYS 工作流、版本與錯誤格式沿用其他入庫契約，但文件類型為 `INV.OTHER_ISSUE`，過帳來源 `OTHER_ISSUE`，方向為 `Out`。

| Method | Path | 用途 | 權限 | 主要錯誤 |
|---|---|---|---|---|
| GET | `/` | `keyword`、`from`、`to`、`status`、`sort`、`page`、`pageSize`；只列目前公司 | VIEW | 400 |
| GET | `/{id}` | 表頭、明細、最近審批狀態 | VIEW | 404 `INV.OTHER_ISSUE.NOT_FOUND` |
| POST | `/` | 交易內取號並建立草稿；明細含正數 `quantity` 與非負 `unitCost` 估算 | CREATE | 400、404、422 |
| PUT | `/{id}` | 附 `version` 整組替換草稿或駁回單據 | EDIT | 409、422 `NOT_DRAFT` |
| DELETE | `/{id}?version=` | 草稿軟刪除 | DELETE | 409、422 `NOT_DRAFT` |
| POST | `/{id}/submit` | `{ version }`；SYS 最終核准與出庫同交易 | SUBMIT | 409、422 `INV.STOCK.INSUFFICIENT` |

`unitCost` 與明細 `amount` 只供審批金額估算，不寫入出庫異動帳成本。SYS 審批 API 負責核准／駁回，本資源不提供直接核准端點。列印及 CSV 匯出由 005b 前端實作。
# WU-INV-016d／016a 選擇資料 API（017b 擴充報表讀取權限）

Swagger 標籤 `INV - Selection Options`，只讀、無新資料表。MD 模組以 Contracts 應用介面提供目前公司資料；INV 不得直接查 `md_` 表。三端點接受 `keyword`（去前後空白，代碼／名稱）、`page`、`pageSize`，按代碼、ID 升冪穩定分頁，回 `PagedResult<{ id, code, name }>`。權限為任一現行 INV 單據的 CREATE／EDIT（其他入庫、其他出庫、調撥、期初、盤點或組裝），或 `INV.REPORT.VIEW`、`INV.TRACKING.VIEW`；倉庫端點另接受 `INV.SAFETY_STOCK.VIEW`，物料端點另接受 `INV.SAFETY_STOCK.EDIT`。估值及一般庫存 VIEW 不授予此選項查詢權。未登入 401、缺授權 403，非法分頁 400 ProblemDetails，無效、停用、他公司或不存在的父倉庫 404。

| Method | Path | 範圍 | 主要驗收 |
|---|---|---|---|
| GET | `/api/v1/inv/selection-options/items` | 目前公司啟用、未刪除物料 | 不要求 `MD.ITEM.VIEW`；回 id/code/name/trackingMode（None／Lot／Serial）；搜尋與穩定分頁 |
| GET | `/api/v1/inv/selection-options/warehouses` | 目前公司啟用、未刪除倉庫 | 不要求 `MD.WAREHOUSE.VIEW`；搜尋與穩定分頁 |
| GET | `/api/v1/inv/selection-options/warehouses/{warehouseId}/locations` | 指定目前公司啟用倉庫內啟用、未刪除儲位 | 倉庫不存在／跨公司 404；不可列其他倉庫儲位 |

選擇結果不是保存授權；INV 建立／編輯／送審依既有 MD 引用 guard 再檢查啟用狀態。016a 已新增 HTTP 權限、公司隔離及 SQL 搜尋測試；016b1 前端服務固定呼叫上述三端點，表單接入留 016b2。

### WU-INV-016b2b1 已保存引用顯示

五個單據資源 `other-receipts`、`other-issues`、`transfers`、`opening-stocks`、`stocktakes` 各提供 `GET /api/v1/inv/{resource}/{id}/saved-references`。權限與該單據詳情 GET 相同，須有對應 `INV.*.VIEW`；先以單據服務確認目前公司內的已保存單據，再把其明細（盤點另含已保存範圍）的物料、倉庫與儲位 ID 傳給 MD Contracts。回應 `items`、`warehouses`、`locations` 三組 `{id, code, name, trackingMode, isActive}`，未刪除的停用資料保留，外公司及已刪除資料不回傳。查無單據 404，未登入 401，缺 VIEW 403。端點不接受客戶端傳入主檔 ID，避免藉停用記錄探查主檔；前端只用於既有引用顯示，不能把結果放進新增選項。保存時仍由 MD guard 重新檢查。
## WU-INV-017d 進出存報表與追溯匯出契約（ADR-068；017a～017c 已實作）

| Method | Path | 篩選與回應 | 權限 | 主要錯誤 |
|---|---|---|---|---|
| GET | `/api/v1/inv/reports/stock-flow` | 必填 `from`／`to` 業務日期（含頭尾）；可選 `itemId`、`warehouseId`、`locationId`、`page`、`pageSize`；回 `queriedAt`、`totalCount` 及 `items[{itemId, warehouseId, locationId, openingQuantity, inboundQuantity, outboundQuantity, closingQuantity, negativeClosing}]` | `INV.REPORT.VIEW` | 400 日期／ID／分頁、403 |
| GET | `/api/v1/inv/reports/stock-flow/export` | 同篩選，無分頁；完整 UTF-8 BOM CSV，包含查詢時間及六位數量；檔名 `.csv` | `INV.REPORT.VIEW` + `INV.REPORT.EXPORT` | 400、403、422 超列數上限 |
| GET | `/api/v1/inv/tracking/lots/{id}/export` | 指定目前公司批號的完整異動歷史 CSV，無最近 1000 筆截斷 | `INV.TRACKING.VIEW` + `INV.TRACKING.EXPORT` | 400、403、404、422 |
| GET | `/api/v1/inv/tracking/serials/{id}/export` | 指定目前公司序號的完整異動歷史 CSV，無最近 1000 筆截斷 | `INV.TRACKING.VIEW` + `INV.TRACKING.EXPORT` | 400、403、404、422 |

進出存按目前公司、物料／倉庫／儲位分組，以 `business_date` 分割期初與期間入出，按 `(itemId, warehouseId, locationId)` 排序。查詢在 SQL Server SNAPSHOT 交易內完成總數與資料頁，並用 `IClock` 回傳 `queriedAt`；取消令牌傳至 SQL 讀取。進出存匯出同一快照內先檢查總數，上限 10,000 列，超限在輸出 HTTP body 前回 422 `INV.REPORT.EXPORT_LIMIT`。成功回應為 `text/csv; charset=utf-8`、`Content-Disposition: attachment; filename=stock-flow.csv`、`X-Export-Row-Count` 真實列數；檔案有 UTF-8 BOM、查詢時間行、欄名與六位數量，空結果僅有查詢時間與欄名。前端空結果不啟動下載。CSV 文字欄位防公式注入。不跨模組 JOIN MD，名稱由前端既有受權選項顯示。

批序號匯出另以指定 `{id}` 確認目前公司可讀，再在同一 SQL Server SNAPSHOT 交易內先計數、檢查最多 10,000 列，超限回 422 `INV.TRACKING.EXPORT_LIMIT` 且未寫 HTTP body。成功為 `text/csv; charset=utf-8`、`lot-history.csv` 或 `serial-history.csv`，`X-Export-Row-Count` 為實際歷史列數；UTF-8 BOM 後依序輸出追蹤類型／號碼、業務日期、過帳時間、異動 ID、來源類型／ID／行號、方向、倉庫／儲位 ID、六位數量。按 `(posted_at,movement_id,tracking_id)` 穩定排序；文字防 CSV 公式注入。取消令牌傳入計數、讀取及串流；0 列只有欄名，前端不啟動空檔下載。詳情預覽仍只顯示最近 1000 筆。
