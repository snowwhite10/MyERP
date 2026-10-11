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

已實作：`src/backend/Modules/MyERP.Modules.System/Api/UsersController.cs`，Swagger 標籤 `SYS - User`。需登入並帶 `X-Company-Id`；範圍為目前公司有用戶公司關聯的用戶，範圍外一律 404（ADR-016）。

| Method | Path | Request → Response | Permission | REQ_F |
|---|---|---|---|---|
| GET | `/api/v1/sys/users` | `?page&pageSize&keyword&sort=username\|-username` → `PagedResult<UserSummary>`（含停用） | SYS.USER.VIEW | REQ_F4118.23.5 |
| POST | `/api/v1/sys/users` | `{ username, password }` → 201 `UserSummary`；同交易加入目前公司（預設、無角色） | SYS.USER.CREATE | REQ_F4118.23.1, REQ_F4118.23.2, REQ_F4118.23.6 |
| PUT | `/api/v1/sys/users/{id}` | `{ isActive, version }` → 200 `UserSummary` | SYS.USER.EDIT | REQ_F4118.23.7 |
| POST | `/api/v1/sys/users/{id}/reset-password` | `{ password }` → 204 | SYS.USER.RESET_PASSWORD | REQ_F4118.23.8 |
| POST | `/api/v1/sys/users/{id}/unlock` | 無 body → 204 | SYS.USER.EDIT | REQ_F4118.23.8 |

`UserSummary`：`{ id, username, isActive, isLocked, passwordChangedAt, isPasswordExpired, version }`。DTO 絕不包含 `password_hash`、正規化帳號、失敗次數或鎖定期限。錯誤：400 `COMMON.REQUEST.VALIDATION_FAILED`（帳號格式、密碼政策、排序欄位）、403 `COMMON.PERMISSION.DENIED`／`COMMON.COMPANY.ACCESS_DENIED`、404 `SYS.USER.NOT_FOUND`、409 `SYS.USER.DUPLICATE`／`COMMON.CONCURRENCY.CONFLICT`、422 `SYS.USER.SELF_DEACTIVATION`。

登入端點（WU-SYS-003）以正規化帳號查詢、呼叫 `VerifyPasswordAsync`，並在發 Token 前檢查到期狀態。

## 登入與 Token（WU-SYS-003）

| Method | Path | Purpose | Permission |
|---|---|---|---|
| GET | `/api/v1/sys/auth/captcha` | 取得 Captcha：`captchaId`、`image`（SVG data URI）、`length`、`expiresInSeconds`；`Cache-Control: no-store` | AllowAnonymous |
| POST | `/api/v1/sys/auth/login` | `{ username, password, captchaId, captchaCode }` → `AuthTokens` | AllowAnonymous |
| POST | `/api/v1/sys/auth/refresh` | `{ refreshToken }` → 新 `AuthTokens`；舊 Token 失效 | AllowAnonymous |
| POST | `/api/v1/sys/auth/logout` | `{ refreshToken }` → 204 | 已登入 |
| GET | `/api/v1/sys/auth/me` | `{ userId, username }` | 已登入 |
| POST | `/api/v1/sys/auth/change-password` | `{ currentPassword, newPassword, refreshToken? }` → 新 `AuthTokens`；舊工作階段的 Refresh Token 失效 | 已登入本人（REQ_F4111.1.2） |
| POST | `/api/v1/sys/auth/change-expired-password` | `{ username, currentPassword, newPassword, captchaId, captchaCode }` → 新 `AuthTokens`；密碼到期後可使用 | AllowAnonymous（REQ_F4111.1.2） |

`AuthTokens`：`accessToken`、`accessTokenExpiresAt`、`refreshToken`、`refreshTokenExpiresAt`、`tokenType = "Bearer"`（時間為 UTC+8）。錯誤代碼：400 `COMMON.REQUEST.VALIDATION_FAILED`／`SYS.AUTH.CAPTCHA_INVALID`，401 `SYS.AUTH.INVALID_CREDENTIALS`／`SYS.AUTH.REFRESH_INVALID`，403 `SYS.AUTH.PASSWORD_EXPIRED`，429 `COMMON.REQUEST.RATE_LIMITED`。

修改密碼另有 400 `SYS.AUTH.CURRENT_PASSWORD_INVALID`（目前密碼錯誤）、`SYS.AUTH.PASSWORD_REUSED`（新舊相同），併發版本衝突回 409。到期密碼修改的帳密錯誤回 `SYS.AUTH.INVALID_CREDENTIALS`。兩端點分別是本人操作與匿名找回到期密碼入口，無管理權限代碼；後者依 IP 套用 auth 限流。

個人設定語言／主題於 WU-SYS-011b 使用前端 `LanguageService`／`ThemeService` 和 `PreferenceStorage` 保存於此瀏覽器，沒有新增 HTTP API。跨裝置偏好同步另需有權限與資料模型的後端設計。

限流（WU-SYS-005、ADR-018）：login、refresh 每 IP 每端點每分鐘 5 次；captcha 每 IP 每分鐘 10 次；logout、me 及其他已登入 API 每用戶每分鐘 300 次。超過回 429 ProblemDetails（`type` `…/errors/rate-limited`、三語 `title`、`errorCode` `COMMON.REQUEST.RATE_LIMITED`）與 `Retry-After`（秒）。

## 跨來源與安全標頭（WU-SYS-005）

- CORS：只允許 `Security:Cors:AllowedOrigins` 列明的來源（Development 預設 `http://localhost:4200`、`http://localhost:4300`、`http://127.0.0.1:4300`）；方法 GET／POST／PUT／PATCH／DELETE；請求標頭 Authorization、Content-Type、Accept、Accept-Language、X-Company-Id；公開 Retry-After、Content-Disposition；不允許 Credentials（Token 走 Authorization 標頭）；預檢快取 10 分鐘。
- 每個回應帶 `X-Content-Type-Options: nosniff`、`X-Frame-Options: DENY`、`Referrer-Policy: no-referrer`、`Content-Security-Policy: default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'`；非 Development 另有 HSTS 與 HTTP→HTTPS 307。
- 詳細設計見 `docs/01-architecture/security.md`。

## 角色與權限（WU-SYS-004）

| Method | Path | Purpose | Permission | REQ_F |
|---|---|---|---|---|
| GET | `/api/v1/sys/roles` | 角色列表（`page`、`pageSize`、`keyword`、`sort=code|name|-code|-name`） | SYS.ROLE.VIEW | REQ_F4118.25.1 |
| GET | `/api/v1/sys/roles/{id}` | 角色與權限代碼 | SYS.ROLE.VIEW | REQ_F4118.25.1 |
| POST | `/api/v1/sys/roles` | `{ code, name, description?, dataScope, isFullAccess, permissions[] }` → 201 | SYS.ROLE.CREATE | REQ_F4118.25.2 |
| PUT | `/api/v1/sys/roles/{id}` | `{ name, description?, dataScope, isFullAccess, isActive, permissions[], version }` | SYS.ROLE.EDIT | REQ_F4118.25.3 |
| DELETE | `/api/v1/sys/roles/{id}?version=` | 軟刪除；仍被指派回 422 `SYS.ROLE.IN_USE` | SYS.ROLE.DELETE | REQ_F4118.25.4 |
| GET | `/api/v1/sys/permissions` | 權限目錄 `[{ code, module }]` | SYS.ROLE.VIEW | REQ_F4118.25.1 |
| GET | `/api/v1/sys/users/{userId}/companies` | → `[{ companyId, companyCode, companyName, branchId, departmentId, isDefault, isActive, roleIds[] }]`；只列操作者獲授權的公司（ADR-017） | SYS.USER.VIEW | REQ_F4118.25.8 |
| PUT | `/api/v1/sys/users/{userId}/companies/{companyId}` | `{ branchId?, departmentId?, isDefault, isActive, roleIds[] }` → 204；角色整組取代；用戶須屬於目前公司、目標公司須為操作者獲授權公司（ADR-017） | SYS.USER.EDIT | REQ_F4118.25.6 |
| GET | `/api/v1/sys/auth/authorization` | 本人授權：`{ companies[{id,code,name,isDefault}], companyId, isFullAccess, permissions[{code,dataScope}] }`；不帶 `X-Company-Id` 時取預設公司 | 已登入（自助） | REQ_F4118.25.7 |

`dataScope` 以字串表示：`Self`、`Department`、`Branch`、`Company`。錯誤：400 驗證（未知權限代碼、跨公司分店／部門、角色不存在或停用）、403 `COMMON.PERMISSION.DENIED`／`COMMON.COMPANY.ACCESS_DENIED`、404 `SYS.ROLE.NOT_FOUND`／`SYS.USER.NOT_FOUND`（含不屬於目前公司的用戶）／`SYS.COMPANY.NOT_FOUND`（操作者未獲授權的目標公司）、409 `SYS.ROLE.DUPLICATE_CODE`／`COMMON.CONCURRENCY.CONFLICT`、422 `SYS.ROLE.IN_USE`／`SYS.USER.SELF_DEACTIVATION`（停用自己的公司授權）。

## 稽核日誌（WU-SYS-007）

| 方法 | 路徑 | 權限 | 說明 |
|---|---|---|---|
| GET | `/api/v1/sys/audit-logs` | `SYS.AUDIT.VIEW` | 目前公司的資料變更與安全事件（REQ_F4118.27.4）。只能查詢，沒有修改或刪除端點。 |

查詢參數：`page`、`pageSize`；`from`、`to`（`yyyy-MM-dd`，含頭尾）；`category`（`DATA`／`SECURITY`）；`action`（例如 `UPDATE`、`LOGIN_FAILED`）；`tableName`＋`recordId`（單據修改歷史）；`userId`；`sort`（`occurredAt`／`-occurredAt`，預設 `-occurredAt`）。

回應為 `PagedResult<AuditLogItem>`，欄位：`id`、`occurredAt`、`category`、`action`、`tableName`、`recordId`、`userId`、`username`、`companyId`、`changes`、`remoteIp`、`traceId`。`changes` 是 JSON 物件：資料變更為 `{"欄位":{"old":…,"new":…}}`，安全事件為明細（例如 `{"reason":"INVALID_CREDENTIALS"}`）。敏感欄位的值為 `***`。

錯誤：400 驗證（`to` 早於 `from`、未知類別、長度超限）；403 `COMMON.PERMISSION.DENIED`。

## 權限

SYS 模組宣告的權限代碼（`SystemPermissions`，ADR-014）：

| 資源 | 代碼 |
|---|---|
| 公司 | `SYS.COMPANY.VIEW`、`SYS.COMPANY.CREATE`、`SYS.COMPANY.EDIT` |
| 分店 | `SYS.BRANCH.VIEW`、`SYS.BRANCH.CREATE`、`SYS.BRANCH.EDIT` |
| 部門 | `SYS.DEPARTMENT.VIEW`、`SYS.DEPARTMENT.CREATE`、`SYS.DEPARTMENT.EDIT` |
| 用戶 | `SYS.USER.VIEW`、`SYS.USER.CREATE`、`SYS.USER.EDIT`、`SYS.USER.RESET_PASSWORD` |
| 角色 | `SYS.ROLE.VIEW`、`SYS.ROLE.CREATE`、`SYS.ROLE.EDIT`、`SYS.ROLE.DELETE` |
| 稽核 | `SYS.AUDIT.VIEW` |
| 單號規則 | `SYS.NUMBER_RULE.VIEW`、`SYS.NUMBER_RULE.CREATE`、`SYS.NUMBER_RULE.EDIT` |
| 審批流程 | `SYS.APPROVAL_FLOW.VIEW`、`SYS.APPROVAL_FLOW.CREATE`、`SYS.APPROVAL_FLOW.EDIT` |
| 審批代理 | `SYS.APPROVAL_DELEGATION.VIEW`、`SYS.APPROVAL_DELEGATION.CREATE`、`SYS.APPROVAL_DELEGATION.EDIT` |
| 審批 | `SYS.APPROVAL.VIEW`（歷史與明細）、`SYS.APPROVAL.PROCESS`（待辦、核准、駁回） |
| IP 白名單 | `SYS.IP_WHITELIST.VIEW`、`SYS.IP_WHITELIST.CREATE`、`SYS.IP_WHITELIST.EDIT` |
| 系統參數 | `SYS.PARAMETER.VIEW`、`SYS.PARAMETER.EDIT` |

不需權限代碼的端點：匿名的 `auth/captcha`、`auth/login`、`auth/refresh`；只需登入的自助端點 `auth/logout`、`auth/me`、`auth/authorization`。

## 單號規則（WU-SYS-008，ADR-021）

| 方法 | 路徑 | 權限 | 說明 |
|---|---|---|---|
| GET | `/api/v1/sys/number-rules` | `SYS.NUMBER_RULE.VIEW` | 目前公司的規則列表：`page`、`pageSize`、`keyword`（單據類型／名稱）、`sort`（`documentType` 預設／`name`，可加 `-`）（REQ_F4118.28.1） |
| GET | `/api/v1/sys/number-rules/{id}` | `SYS.NUMBER_RULE.VIEW` | 單一規則；他公司或不存在回 404 `SYS.NUMBER_RULE.NOT_FOUND` |
| GET | `/api/v1/sys/number-rules/{id}/preview?date=yyyy-MM-dd` | `SYS.NUMBER_RULE.VIEW` | 指定業務日期（預設今天）的下一個單號，不佔用流水號（REQ_F4118.28.4） |
| POST | `/api/v1/sys/number-rules` | `SYS.NUMBER_RULE.CREATE` | 建立規則，回 201（REQ_F4118.28.2） |
| PUT | `/api/v1/sys/number-rules/{id}` | `SYS.NUMBER_RULE.EDIT` | 修改名稱、前綴、格式、長度、週期、啟用狀態，帶 `version`（REQ_F4118.28.3） |

建立請求：`documentType`、`name`、`prefix`、`dateFormat`（`None`／`Year`／`YearMonth`／`YearMonthDay`／`ShortYearMonth`／`ShortYearMonthDay`）、`sequenceLength`（3–10）、`resetPeriod`（`Never`／`Yearly`／`Monthly`／`Daily`）。修改請求不含 `documentType`，另加 `isActive`、`version`。

回應 `NumberRuleDetail`：`id`、`documentType`、`name`、`prefix`、`dateFormat`、`sequenceLength`、`resetPeriod`、`isActive`、`isInUse`、`sample`（今天第 1 號）、`version`。預覽回應 `NumberPreview`：`documentType`、`documentDate`、`nextNumber`。

錯誤：400 驗證（欄位 `documentType`、`name`、`prefix`、`dateFormat`、`sequenceLength`、`resetPeriod`）；403 `COMMON.PERMISSION.DENIED`；404 `SYS.NUMBER_RULE.NOT_FOUND`；409 `SYS.NUMBER_RULE.DUPLICATE_TYPE` 或版本衝突；422 `SYS.NUMBER_RULE.IN_USE`、`SYS.NUMBER_RULE.SEQUENCE_EXHAUSTED`（預覽）。

**進程內取號介面**（不開放 HTTP）：`MyERP.Shared.Numbering.INumberGenerator.NextAsync(documentType, documentDate)`，由 SYS 的 `NumberGenerator` 實作。錯誤：422 `SYS.NUMBER_RULE.NOT_CONFIGURED`、`SYS.NUMBER_RULE.INACTIVE`、`SYS.NUMBER_RULE.SEQUENCE_EXHAUSTED`；沒有公司上下文 403 `SYS.COMPANY.UNAVAILABLE`。

## 審批流程（WU-SYS-009，ADR-022）

| 方法 | 路徑 | 權限 | 說明 |
|---|---|---|---|
| GET | `/api/v1/sys/approval-flows` | `SYS.APPROVAL_FLOW.VIEW` | 目前公司的流程（含步驟）：`page`、`pageSize`、`keyword`（單據類型／名稱）、`sort`（`documentType` 預設／`name`）（REQ_F4118.29.1） |
| GET | `/api/v1/sys/approval-flows/{id}` | `SYS.APPROVAL_FLOW.VIEW` | 單一流程；他公司或不存在回 404 `SYS.APPROVAL_FLOW.NOT_FOUND` |
| POST | `/api/v1/sys/approval-flows` | `SYS.APPROVAL_FLOW.CREATE` | 建立流程，回 201（REQ_F4118.29.1） |
| PUT | `/api/v1/sys/approval-flows/{id}` | `SYS.APPROVAL_FLOW.EDIT` | 修改名稱、自審、啟用並整組取代步驟，帶 `version`（REQ_F4118.29.2） |
| GET | `/api/v1/sys/approval-delegations` | `SYS.APPROVAL_DELEGATION.VIEW` | 代理列表：`userId`（被代理人或代理人）、`activeOn`（yyyy-MM-dd，該日有效）、`sort`（`startDate`，預設 `-startDate`）（REQ_F4118.29.3） |
| POST | `/api/v1/sys/approval-delegations` | `SYS.APPROVAL_DELEGATION.CREATE` | 建立代理，回 201 |
| PUT | `/api/v1/sys/approval-delegations/{id}` | `SYS.APPROVAL_DELEGATION.EDIT` | 修改期間、備註、啟用，帶 `version` |
| GET | `/api/v1/sys/approvals/tasks` | `SYS.APPROVAL.PROCESS` | 我的待辦：`documentType`、`keyword`（單號／摘要）、`sort`（`submittedAt` 預設舊到新）（REQ_F4118.29.6） |
| GET | `/api/v1/sys/approvals` | `SYS.APPROVAL.VIEW` | 審批歷史：`documentType`、`documentId`、`status`（`Pending`／`Approved`／`Rejected`／`Cancelled`）、`keyword`、`sort`（預設 `-submittedAt`）（REQ_F4118.29.7） |
| GET | `/api/v1/sys/approvals/{id}` | `SYS.APPROVAL.VIEW` | 審批實例與各步驟處理記錄 |
| POST | `/api/v1/sys/approvals/{id}/approve` | `SYS.APPROVAL.PROCESS` | 核准目前步驟：`comment`（選填，≤ 500）、`version`（REQ_F4118.29.5） |
| POST | `/api/v1/sys/approvals/{id}/reject` | `SYS.APPROVAL.PROCESS` | 駁回：`comment`（必填）、`version`（REQ_F4118.29.5） |

流程請求：`documentType`（建立時）、`name`、`allowSelfApproval`、`steps[]`（1–10 個，依次序執行：`name`、`approverType`（`Role`／`User`）、`roleId` 或 `userId`、`minAmount`（選填，≥ 0，最多 4 位小數））；修改另加 `isActive`、`version`。回應 `ApprovalFlowDetail`：`id`、`documentType`、`name`、`allowSelfApproval`、`isActive`、`steps[]`（`stepNo`、`name`、`approverType`、`roleId`、`userId`、`minAmount`）、`version`。

代理請求：`delegatorUserId`、`delegateUserId`、`startDate`、`endDate`、`remark`（≤ 200）；修改只有 `startDate`、`endDate`、`remark`、`isActive`、`version`。回應另含 `delegatorUsername`、`delegateUsername`。

待辦 `ApprovalTask`：`instanceId`、`documentType`、`documentId`、`documentNo`、`summary`、`amount`、`submittedBy`、`submittedByUsername`、`submittedAt`、`stepNo`、`stepCount`、`stepName`、`onBehalfOfUserId`／`onBehalfOfUsername`（代理時的被代理人）、`version`（核准／駁回時帶回）。實例明細 `ApprovalInstanceDetail` 另含 `status`、`currentStepNo`、`allowSelfApproval`、`completedAt` 與 `steps[]`（`stepNo`、`name`、`approverType`、`roleId`／`roleName`、`userId`／`username`、`minAmount`、`status`（`Waiting`／`Pending`／`Approved`／`Rejected`／`Skipped`）、`actedBy`／`actedByUsername`、`onBehalfOfUserId`／`onBehalfOfUsername`、`actedAt`、`comment`）。

錯誤：400 驗證（`documentType`、`name`、`steps`、`steps[i].roleId|userId|minAmount|approverType|name`、`endDate`、`delegateUserId`、`delegatorUserId`、`remark`、`comment`、`status`、`sort`）；401 `SYS.APPROVAL.USER_REQUIRED`（進程內呼叫沒有登入用戶）；403 `COMMON.PERMISSION.DENIED`、`SYS.APPROVAL.NOT_APPROVER`；404 `SYS.APPROVAL_FLOW.NOT_FOUND`、`SYS.APPROVAL_DELEGATION.NOT_FOUND`、`SYS.APPROVAL.NOT_FOUND`；409 `SYS.APPROVAL_FLOW.DUPLICATE_TYPE`、`SYS.APPROVAL_DELEGATION.OVERLAP`、`SYS.APPROVAL.ALREADY_PENDING`、版本衝突；422 `SYS.APPROVAL.NOT_PENDING`、`SYS.APPROVAL.SELF_APPROVAL`。

**進程內介面**（不開放 HTTP，`MyERP.Shared.Approvals`）：

- `IApprovalWorkflowService.SubmitAsync(ApprovalSubmission(documentType, documentId, documentNo, amount, summary))` → `ApprovalState(instanceId, documentType, documentId, status, currentStepNo, stepCount)`；沒有適用步驟時狀態即為 `Approved`。
- `IApprovalWorkflowService.CancelAsync(documentType, documentId)`：撤回審批中的實例。
- `IApprovalWorkflowService.GetStateAsync(documentType, documentId)`：最近一次送審的狀態（未送審為 null）。
- `IApprovalResultHandler`（單據模組實作並註冊 DI）：`DocumentType` 與 `HandleAsync(ApprovalCompleted)`，審批人作出最終決定時在同一交易內呼叫。

## IP 白名單（WU-SYS-006）

| Method | Route | Permission | Description |
|---|---|---|---|
| GET | `/api/v1/sys/ip-whitelist` | `SYS.IP_WHITELIST.VIEW` | 分頁，keyword 搜網段／備註，sort 為 `id` 或 `ipRange` |
| GET | `/api/v1/sys/ip-whitelist/{id}` | `SYS.IP_WHITELIST.VIEW` | 單筆；不存在 404 |
| POST | `/api/v1/sys/ip-whitelist` | `SYS.IP_WHITELIST.CREATE` | `scope`（System／Role／User）、`targetId`（後兩者必填）、`ipRange`、`validFrom`、`validTo`、`description`；201，重複 409 |
| PUT | `/api/v1/sys/ip-whitelist/{id}` | `SYS.IP_WHITELIST.EDIT` | `validFrom`、`validTo`、`description`、`isActive`、`version`；版本衝突 409 |

回應包含 `id`、上述欄位、`isActive`、`version`。拒絕的請求回 403 `COMMON.REQUEST.IP_NOT_ALLOWED`（REQ_F4118.32.4）。

## 系統參數（WU-SYS-010a，ADR-026）

| 方法 | 路徑 | 權限 | 說明 |
|---|---|---|---|
| GET | `/api/v1/sys/parameters?module=SYS` | `SYS.PARAMETER.VIEW` | 目前公司的全部參數，依模組、分組、鍵排序；`module` 可省略（REQ_F4118.17.1） |
| PUT | `/api/v1/sys/parameters` | `SYS.PARAMETER.EDIT` | 批次保存 1–200 項，同一交易，回傳保存後的完整清單（REQ_F4118.17.2） |

回應 `ParameterDetail`：`key`、`module`、`group`、`type`（`Boolean`／`WholeNumber`／`Number`／`Text`／`Option`）、`value`、`defaultValue`、`isDefault`、`minimum`、`maximum`、`maxLength`、`options`、`version`（未修改過為 0）。

保存請求：`{ "items": [ { "key": "SYS.QUANTITY_DECIMALS", "value": "3", "version": 0 } ] }`。值一律以字串傳送，布林為 `true`／`false`，數字用 `.` 作小數點。

錯誤：400 驗證（`items`、`items[i].key`、`items[i].value`，一次列出全部）；403 `COMMON.PERMISSION.DENIED` 或 `SYS.COMPANY.UNAVAILABLE`；409 版本衝突。

**進程內讀取介面**（不開放 HTTP）：`MyERP.Shared.Settings.ISystemParameters`，提供 `GetAsync`／`GetBooleanAsync`／`GetIntegerAsync`／`GetDecimalAsync`，由 SYS 的 `SystemParameterReader` 實作。

## 列印設定（WU-SYS-010c，ADR-027）

沒有新增端點：列印設定是 `print` 分組的參數，經 `GET／PUT /api/v1/sys/parameters` 讀取與保存（`SYS.PARAMETER.VIEW／EDIT`）。

| 鍵 | 型別 | 上限／預設 | 說明 |
|---|---|---|---|
| `SYS.PRINT_COMPANY_NAME` | Text | 100，空白 | 空白時列印公司主檔名稱 |
| `SYS.PRINT_ADDRESS` | Text | 200，空白 | 地址 |
| `SYS.PRINT_POSTAL_CODE` | Text | 10，空白 | 郵遞區號 |
| `SYS.PRINT_PHONE` | Text | 30，空白 | 電話 |
| `SYS.PRINT_FAX` | Text | 30，空白 | 傳真 |
| `SYS.PRINT_PURCHASE_ORDER_FOOTER` | Text | 500，空白 | 採購單頁尾備註（可多行） |
| `SYS.PRINT_SALES_DELIVERY_FOOTER` | Text | 500，空白 | 銷售出貨單頁尾備註（可多行） |
| `SYS.PRINT_SHOW_LOGO` | Boolean | `false` | 是否列印 Logo |
| `SYS.PRINT_SHOW_STAMP` | Boolean | `false` | 是否列印公司印章 |

**進程內讀取介面**（不開放 HTTP）：`MyERP.Modules.System.Contracts.IPrintSettingsReader.GetAsync()` → `PrintSettings(CompanyName, Address, PostalCode, Phone, Fax, PurchaseOrderFooter, SalesDeliveryFooter, ShowLogo, ShowStamp)`（REQ_F4118.18.3）。

## Logo 與印章（WU-SYS-010d，ADR-028）

| 方法 | 路徑 | 權限 | 說明 |
|---|---|---|---|
| GET | `/api/v1/sys/company-images` | `SYS.PARAMETER.VIEW` | 目前公司已上傳的圖片（不含內容），依種類排序（REQ_F4118.19.1） |
| GET | `/api/v1/sys/company-images/{kind}/content` | `SYS.PARAMETER.VIEW` | 圖片內容（`image/png`／`image/jpeg`，`Content-Disposition: inline`）；`ETag` 為版本號，`If-None-Match` 相符回 304；`Cache-Control: private, no-cache`（REQ_F4118.19.1） |
| PUT | `/api/v1/sys/company-images/{kind}` | `SYS.PARAMETER.EDIT` | multipart：`file`、`version`（新上傳為 0）；上傳或替換，回傳 `CompanyImageDetail`（REQ_F4118.19.2） |
| DELETE | `/api/v1/sys/company-images/{kind}?version=` | `SYS.PARAMETER.EDIT` | 刪除，204（REQ_F4118.19.2） |

`{kind}` 為 `logo` 或 `stamp`（不分大小寫），其他值回 404。回應 `CompanyImageDetail`：`kind`（`Logo`／`Stamp`）、`contentType`、`fileName`、`sizeBytes`、`width`、`height`、`version`、`updatedAt`（最後上傳時間，UTC+8）。

檢查（以內容判斷，不看副檔名或宣告的 Content-Type）：只接受 PNG（簽章、首個 chunk 為 IHDR、以 IEND 結尾）與 JPEG（`FF D8 FF` 開頭、`FF D9` 結尾、可解析到 SOF 段）；檔案 1 B–1 MB；寬高 16–2048 像素。請求本體上限 1 MB + 64 KB。

錯誤：400 `file`（沒有檔案或空檔）；403 `COMMON.PERMISSION.DENIED` 或 `SYS.COMPANY.UNAVAILABLE`；404 `SYS.COMPANY_IMAGE.NOT_FOUND`；409 版本衝突（含同時新增）；422 `SYS.COMPANY_IMAGE.UNSUPPORTED_TYPE`／`TOO_LARGE`／`INVALID_DIMENSIONS`。

**進程內讀取介面**（不開放 HTTP）：`MyERP.Modules.System.Contracts.ICompanyImageReader.GetAsync(CompanyImageKind kind)` → `CompanyImage(Kind, ContentType, Width, Height, Content)`，沒有上傳時為 null（REQ_F4118.19.3）。

## 站內通知（WU-SYS-012）

012a 提供進程內 `MyERP.Modules.System.Contracts.INotificationPublisher.PublishAsync(NotificationMessage)`；參數為收件人用戶 Id、類別、標題、內文、站內目標路徑及穩定來源鍵，回傳通知 Id。SYS 依目前公司驗證收件人、寫入 `sys_notification`；業務模組不得直接操作表（REQ_F4118.33.1）。

012b HTTP 收件箱（全部只對目前登入用戶及 `X-Company-Id` 公司生效）：

| Method | Path | 權限 | 回應／錯誤 |
|---|---|---|---|
| GET | `/api/v1/sys/notifications?page=1&pageSize=20&unreadOnly=false` | `SYS.NOTIFICATION.VIEW` | `PagedResult<NotificationItem>`；新到舊，過期排除；無效分頁 400 |
| GET | `/api/v1/sys/notifications/unread-count` | `SYS.NOTIFICATION.VIEW` | `{count}`（過期排除） |
| POST | `/api/v1/sys/notifications/{id}/read` | `SYS.NOTIFICATION.READ` | 204；本人重複操作冪等；他人、他公司或過期 404 `SYS.NOTIFICATION.NOT_FOUND` |
| POST | `/api/v1/sys/notifications/read-all` | `SYS.NOTIFICATION.READ` | `{updatedCount}`，只改本人未到期未讀記錄，重複操作回 0 |

通知內容是純文字，`targetPath` 只允許站內路徑；各端點在 Swagger 列出摘要及回應類型（REQ_F4118.33.2–3）。
