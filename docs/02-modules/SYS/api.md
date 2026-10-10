# SYS 組織 API 設計

> 待實作；WU-SYS-004 授權機制已完成（ADR-014），組織端點可依下列權限代碼開放。路由遵守 `/api/v1/sys/` 慣例。

| Method | Path | Purpose | Permission |
|---|---|---|---|
| GET | `/api/v1/sys/companies` | 已授權公司列表 | SYS.COMPANY.VIEW |
| POST | `/api/v1/sys/companies` | 建立公司 | SYS.COMPANY.CREATE |
| PUT | `/api/v1/sys/companies/{id}` | 更新／停用公司，帶 version | SYS.COMPANY.EDIT |
| GET | `/api/v1/sys/branches` | 目前公司分店列表 | SYS.BRANCH.VIEW |
| POST | `/api/v1/sys/branches` | 建立分店 | SYS.BRANCH.CREATE |
| PUT | `/api/v1/sys/branches/{id}` | 更新／停用分店 | SYS.BRANCH.EDIT |
| GET | `/api/v1/sys/departments` | 目前公司部門列表 | SYS.DEPARTMENT.VIEW |
| POST | `/api/v1/sys/departments` | 建立部門 | SYS.DEPARTMENT.CREATE |
| PUT | `/api/v1/sys/departments/{id}` | 更新／停用部門 | SYS.DEPARTMENT.EDIT |

錯誤沿用 ProblemDetails：400 驗證、403 權限、404 不存在、409 重複代碼／版本衝突；列表按標準分頁。分店／部門必須由已授權的公司上下文篩選。

內部服務已提供公司、分店、部門列表查詢：`page`、`pageSize`、`keyword` 與單一 `sort`（`code`、`name`，可加 `-` 降序）；以代碼排序為預設，同值以 ID 穩定排序。公司列表只取授權層明確傳入的公司 ID，並排除停用及軟刪除公司；空授權集合返回空列表。分店／部門列表先確認公司上下文有效，再套用公司與軟刪除篩選。HTTP 端點仍待授權機制完成。

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
