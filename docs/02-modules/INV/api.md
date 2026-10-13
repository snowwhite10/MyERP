# INV API

WU-INV-001 只建立模組與權限目錄，無公開 HTTP 動作。`INV.INVENTORY.VIEW` 預留給 WU-INV-006 庫存查詢；`INV.MOVEMENT.VIEW` 預留給異動明細。後續 API 沿用 `/api/v1/inv/`、Swagger、ProblemDetails 與 `X-Company-Id` 授權規範。

WU-INV-002 增加內部 `IInventoryReferenceGuard.RunWithCodesAsync(itemId, warehouseId, locationId, action)` 契約及 `InventoryReferenceService.RecordAsync(sourceKey, itemId, warehouseId, locationId)`；INV 寫入服務須把來源與引用的驗證及保存放在同一個交易，交易提交後才釋放鎖。重複來源鍵回 `INV.REFERENCE.DUPLICATE`（409），已刪主檔回相應 MD 404。本單元無公開 HTTP API。

WU-INV-003 增加內部 `IInventoryPostingService.PostAsync(InventoryPostingRequest)`，來源類型、來源 ID、業務日期及全部異動行由核准單據服務傳入；呼叫方須在同一交易確認核准狀態。相同來源相同內容回 `Replayed=true` 與原異動 ID；不同內容回 `INV.POSTING.CONFLICT`（409）；不足庫存回 `INV.STOCK.INSUFFICIENT`（業務錯誤）。`IStockBalanceQuery.GetOnHandAsync` 與 `ListAsync` 從異動帳加總、由 DbContext 公司篩選。這些是進程內 Contracts，公開 HTTP 與 Swagger 端點隨 WU-INV-006 提供。

## WU-INV-004 其他入庫 API 設計（尚未實作）

所有路徑均為 `/api/v1/inv/other-receipts`，需要目前公司授權，回應採 Swagger 註解與 ProblemDetails。權限規劃：`INV.OTHER_RECEIPT.VIEW`、`CREATE`、`EDIT`、`DELETE`、`SUBMIT`；核准／駁回由 SYS 審批 API 的權限執行。

| Method | Path | 用途／主要欄位 | 權限 |
|---|---|---|---|
| GET | `/` | 依單號、日期、狀態篩選、排序與分頁 | VIEW |
| GET | `/{id}` | 表頭、明細、審批狀態 | VIEW |
| POST | `/` | 業務日期、備註、明細；回新 ID、單號與版本 | CREATE |
| PUT | `/{id}` | 完整替換草稿明細；要求版本 | EDIT |
| DELETE | `/{id}?version={version}` | 軟刪除草稿 | DELETE |
| POST | `/{id}/submit` | 版本檢查後呼叫 SYS 工作流；立即核准時同交易過帳 | SUBMIT |
| GET | `/{id}/print` | 可列印單據視圖 | VIEW |
| GET | `/export` | 使用相同列表篩選匯出 Excel | VIEW |

明細欄位：`lineNo`、`itemId`、`warehouseId`、`locationId?`、`quantity`、`unitCost`。每個項目以物料基本單位輸入；API 重驗證正數、六位精度與目前公司引用。SYS 最終核准結果由 `IApprovalResultHandler`（`DocumentType = INV.OTHER_RECEIPT`）處理；核准在 SYS 交易內呼叫 `IInventoryPostingService`，拒絕只更新單據狀態。
