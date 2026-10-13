# INV API

## WU-INV-009a 庫存調撥 API（設計，尚未實作）

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
