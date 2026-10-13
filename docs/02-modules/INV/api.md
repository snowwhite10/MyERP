# INV API

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

列印與 Excel 匯出隨 WU-INV-004b 前端交付（ADR-055），沿用上述列表／詳情 API 與 VIEW 權限。
