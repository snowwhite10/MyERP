# INV API

WU-INV-001 只建立模組與權限目錄，無公開 HTTP 動作。`INV.INVENTORY.VIEW` 預留給 WU-INV-006 庫存查詢；`INV.MOVEMENT.VIEW` 預留給異動明細。後續 API 沿用 `/api/v1/inv/`、Swagger、ProblemDetails 與 `X-Company-Id` 授權規範。
