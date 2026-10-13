# INV API

WU-INV-001 只建立模組與權限目錄，無公開 HTTP 動作。`INV.INVENTORY.VIEW` 預留給 WU-INV-006 庫存查詢；`INV.MOVEMENT.VIEW` 預留給異動明細。後續 API 沿用 `/api/v1/inv/`、Swagger、ProblemDetails 與 `X-Company-Id` 授權規範。

WU-INV-002 增加內部 `IInventoryReferenceGuard.RunAsync(itemId, warehouseId, locationId, action)` 契約；INV 寫入服務須把驗證與保存放在同一個 action，若使用外部交易，須在交易提交後才釋放鎖。本段無公開 HTTP API。
