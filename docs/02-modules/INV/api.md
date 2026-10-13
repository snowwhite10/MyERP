# INV API

WU-INV-001 只建立模組與權限目錄，無公開 HTTP 動作。`INV.INVENTORY.VIEW` 預留給 WU-INV-006 庫存查詢；`INV.MOVEMENT.VIEW` 預留給異動明細。後續 API 沿用 `/api/v1/inv/`、Swagger、ProblemDetails 與 `X-Company-Id` 授權規範。

WU-INV-002 增加內部 `IInventoryReferenceGuard.RunWithCodesAsync(itemId, warehouseId, locationId, action)` 契約及 `InventoryReferenceService.RecordAsync(sourceKey, itemId, warehouseId, locationId)`；INV 寫入服務須把來源與引用的驗證及保存放在同一個交易，交易提交後才釋放鎖。重複來源鍵回 `INV.REFERENCE.DUPLICATE`（409），已刪主檔回相應 MD 404。本單元無公開 HTTP API。

WU-INV-003 增加內部 `IInventoryPostingService.PostAsync(InventoryPostingRequest)`，來源類型、來源 ID、業務日期及全部異動行由核准單據服務傳入；呼叫方須在同一交易確認核准狀態。相同來源相同內容回 `Replayed=true` 與原異動 ID；不同內容回 `INV.POSTING.CONFLICT`（409）；不足庫存回 `INV.STOCK.INSUFFICIENT`（業務錯誤）。`IStockBalanceQuery.GetOnHandAsync` 與 `ListAsync` 從異動帳加總、由 DbContext 公司篩選。這些是進程內 Contracts，公開 HTTP 與 Swagger 端點隨 WU-INV-006 提供。
