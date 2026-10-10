# SYS 組織 API 設計

> 待實作；沒有 WU-SYS-004 授權機制前不公開寫入端點。路由遵守 `/api/v1/sys/` 慣例。

| Method | Path | Purpose | Permission |
|---|---|---|---|
| GET | `/api/v1/sys/companies` | 已授權公司列表 | SYS.Company.View |
| POST | `/api/v1/sys/companies` | 建立公司 | SYS.Company.Create |
| PUT | `/api/v1/sys/companies/{id}` | 更新／停用公司，帶 version | SYS.Company.Edit |
| GET | `/api/v1/sys/branches` | 目前公司分店列表 | SYS.Branch.View |
| POST | `/api/v1/sys/branches` | 建立分店 | SYS.Branch.Create |
| PUT | `/api/v1/sys/branches/{id}` | 更新／停用分店 | SYS.Branch.Edit |
| GET | `/api/v1/sys/departments` | 目前公司部門列表 | SYS.Department.View |
| POST | `/api/v1/sys/departments` | 建立部門 | SYS.Department.Create |
| PUT | `/api/v1/sys/departments/{id}` | 更新／停用部門 | SYS.Department.Edit |

錯誤沿用 ProblemDetails：400 驗證、403 權限、404 不存在、409 重複代碼／版本衝突；列表按標準分頁。分店／部門必須由已授權的公司上下文篩選。

內部服務已提供公司、分店、部門列表查詢：`page`、`pageSize`、`keyword` 與單一 `sort`（`code`、`name`，可加 `-` 降序）；以代碼排序為預設，同值以 ID 穩定排序。公司列表只取授權層明確傳入的公司 ID，並排除停用及軟刪除公司；空授權集合返回空列表。分店／部門列表先確認公司上下文有效，再套用公司與軟刪除篩選。HTTP 端點仍待授權機制完成。

## 用戶身分（WU-SYS-002）

內部 `SystemUserService` 已提供建立身分、驗證密碼與密碼到期檢查；管理端點須等 WU-SYS-004 的 `SYS.User.*` 權限生效後才公開。WU-SYS-003 登入端點應查詢正規化帳號、呼叫驗證服務，並在發 Token 前檢查到期狀態。API DTO 絕不能包含 `password_hash`、失敗次數或鎖定時間。
