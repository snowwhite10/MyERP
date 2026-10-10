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
