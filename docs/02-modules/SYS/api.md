# SYS 組織 API 設計

> 已實作（WU-SYS-001）：`src/backend/Modules/MyERP.Modules.System/Api/OrganizationControllers.cs`，Swagger 標籤 `SYS - Organization`。所有端點需登入並帶 `X-Company-Id`（公司存取中介層驗證，ADR-014）；授權範圍見 ADR-015。

| Method | Path | Request → Response | Permission | REQ_F |
|---|---|---|---|---|
| GET | `/api/v1/sys/companies` | `?page&pageSize&keyword&sort&includeInactive` → `PagedResult<{ id, code, name, isActive, version }>`；只列用戶獲授權的公司，預設不含停用 | SYS.COMPANY.VIEW | REQ_F4118.20.1 |
| POST | `/api/v1/sys/companies` | `{ code, name }` → 201 公司；建立者同交易獲授權（ADR-015） | SYS.COMPANY.CREATE | REQ_F4118.20.2 |
| PUT | `/api/v1/sys/companies/{id}` | `{ name, isActive, version }` → 200 公司；未授權公司 404 | SYS.COMPANY.EDIT | REQ_F4118.20.3, REQ_F4118.20.4 |
| GET | `/api/v1/sys/branches` | `?page&pageSize&keyword&sort` → `PagedResult<{ id, code, name, isActive, version }>`（含停用） | SYS.BRANCH.VIEW | REQ_F4118.21.1 |
| POST | `/api/v1/sys/branches` | `{ code, name }` → 201 分店 | SYS.BRANCH.CREATE | REQ_F4118.21.2 |
| PUT | `/api/v1/sys/branches/{id}` | `{ name, isActive, version }` → 200 分店 | SYS.BRANCH.EDIT | REQ_F4118.21.3 |
| GET | `/api/v1/sys/departments` | 同分店 → `PagedResult<{ id, code, name, branchId, parentDepartmentId, isActive, version }>` | SYS.DEPARTMENT.VIEW | REQ_F4118.22.1 |
| POST | `/api/v1/sys/departments` | `{ code, name, branchId?, parentDepartmentId? }` → 201 部門 | SYS.DEPARTMENT.CREATE | REQ_F4118.22.2 |
| PUT | `/api/v1/sys/departments/{id}` | `{ name, branchId?, parentDepartmentId?, isActive, version }` → 200 部門 | SYS.DEPARTMENT.EDIT | REQ_F4118.22.3 |

錯誤沿用 ProblemDetails：400 驗證（代碼格式、名稱長度、跨公司或停用的分店／上級部門、部門循環、排序欄位錯誤）、401 未登入、403 `COMMON.PERMISSION.DENIED`／`COMMON.COMPANY.ACCESS_DENIED`、404 `SYS.COMPANY|BRANCH|DEPARTMENT.NOT_FOUND`（含其他公司的資料）、409 `SYS.COMPANY|BRANCH|DEPARTMENT.DUPLICATE_CODE`／`COMMON.CONCURRENCY.CONFLICT`。

列表參數：`page`、`pageSize`（1–200）、`keyword`（代碼或名稱包含）與單一 `sort`（`code`、`name`，可加 `-` 降序）；預設代碼排序，同值以 ID 穩定排序。分店／部門先確認公司上下文有效，再套用公司與軟刪除篩選。

## 用戶身分（WU-SYS-002）

內部 `SystemUserService` 已提供建立身分、驗證密碼與密碼到期檢查；管理端點須等 WU-SYS-004 的 `SYS.User.*` 權限生效後才公開。WU-SYS-003 登入端點應查詢正規化帳號、呼叫驗證服務，並在發 Token 前檢查到期狀態。API DTO 絕不能包含 `password_hash`、失敗次數或鎖定時間。

## 登入與 Token（WU-SYS-003）

| Method | Path | Purpose | Permission |
|---|---|---|---|
| GET | `/api/v1/sys/auth/captcha` | 取得 Captcha：`captchaId`、`image`（SVG data URI）、`length`、`expiresInSeconds`；`Cache-Control: no-store` | AllowAnonymous |
| POST | `/api/v1/sys/auth/login` | `{ username, password, captchaId, captchaCode }` → `AuthTokens` | AllowAnonymous |
| POST | `/api/v1/sys/auth/refresh` | `{ refreshToken }` → 新 `AuthTokens`；舊 Token 失效 | AllowAnonymous |
| POST | `/api/v1/sys/auth/logout` | `{ refreshToken }` → 204 | 已登入 |
| GET | `/api/v1/sys/auth/me` | `{ userId, username }` | 已登入 |

`AuthTokens`：`accessToken`、`accessTokenExpiresAt`、`refreshToken`、`refreshTokenExpiresAt`、`tokenType = "Bearer"`（時間為 UTC+8）。錯誤代碼：400 `COMMON.REQUEST.VALIDATION_FAILED`／`SYS.AUTH.CAPTCHA_INVALID`，401 `SYS.AUTH.INVALID_CREDENTIALS`／`SYS.AUTH.REFRESH_INVALID`，403 `SYS.AUTH.PASSWORD_EXPIRED`。登入類端點每 IP 每分鐘 5 次限流於 WU-SYS-005 加入。

## 角色與權限（WU-SYS-004）

| Method | Path | Purpose | Permission | REQ_F |
|---|---|---|---|---|
| GET | `/api/v1/sys/roles` | 角色列表（`page`、`pageSize`、`keyword`、`sort=code|name|-code|-name`） | SYS.ROLE.VIEW | REQ_F4118.25.1 |
| GET | `/api/v1/sys/roles/{id}` | 角色與權限代碼 | SYS.ROLE.VIEW | REQ_F4118.25.1 |
| POST | `/api/v1/sys/roles` | `{ code, name, description?, dataScope, isFullAccess, permissions[] }` → 201 | SYS.ROLE.CREATE | REQ_F4118.25.2 |
| PUT | `/api/v1/sys/roles/{id}` | `{ name, description?, dataScope, isFullAccess, isActive, permissions[], version }` | SYS.ROLE.EDIT | REQ_F4118.25.3 |
| DELETE | `/api/v1/sys/roles/{id}?version=` | 軟刪除；仍被指派回 422 `SYS.ROLE.IN_USE` | SYS.ROLE.DELETE | REQ_F4118.25.4 |
| GET | `/api/v1/sys/permissions` | 權限目錄 `[{ code, module }]` | SYS.ROLE.VIEW | REQ_F4118.25.1 |
| PUT | `/api/v1/sys/users/{userId}/companies/{companyId}` | `{ branchId?, departmentId?, isDefault, isActive, roleIds[] }` → 204；角色整組取代 | SYS.USER.EDIT | REQ_F4118.25.6 |
| GET | `/api/v1/sys/auth/authorization` | 本人授權：`{ companies[{id,code,name,isDefault}], companyId, isFullAccess, permissions[{code,dataScope}] }`；不帶 `X-Company-Id` 時取預設公司 | 已登入（自助） | REQ_F4118.25.7 |

`dataScope` 以字串表示：`Self`、`Department`、`Branch`、`Company`。錯誤：400 驗證（未知權限代碼、跨公司分店／部門、角色不存在或停用）、403 `COMMON.PERMISSION.DENIED`／`COMMON.COMPANY.ACCESS_DENIED`、404 `SYS.ROLE.NOT_FOUND`／`SYS.USER.NOT_FOUND`、409 `SYS.ROLE.DUPLICATE_CODE`／`COMMON.CONCURRENCY.CONFLICT`、422 `SYS.ROLE.IN_USE`。

## 權限

SYS 模組宣告的權限代碼（`SystemPermissions`，ADR-014）：

| 資源 | 代碼 |
|---|---|
| 公司 | `SYS.COMPANY.VIEW`、`SYS.COMPANY.CREATE`、`SYS.COMPANY.EDIT` |
| 分店 | `SYS.BRANCH.VIEW`、`SYS.BRANCH.CREATE`、`SYS.BRANCH.EDIT` |
| 部門 | `SYS.DEPARTMENT.VIEW`、`SYS.DEPARTMENT.CREATE`、`SYS.DEPARTMENT.EDIT` |
| 用戶 | `SYS.USER.VIEW`、`SYS.USER.CREATE`、`SYS.USER.EDIT` |
| 角色 | `SYS.ROLE.VIEW`、`SYS.ROLE.CREATE`、`SYS.ROLE.EDIT`、`SYS.ROLE.DELETE` |

不需權限代碼的端點：匿名的 `auth/captcha`、`auth/login`、`auth/refresh`；只需登入的自助端點 `auth/logout`、`auth/me`、`auth/authorization`。
