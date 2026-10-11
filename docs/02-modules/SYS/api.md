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

`AuthTokens`：`accessToken`、`accessTokenExpiresAt`、`refreshToken`、`refreshTokenExpiresAt`、`tokenType = "Bearer"`（時間為 UTC+8）。錯誤代碼：400 `COMMON.REQUEST.VALIDATION_FAILED`／`SYS.AUTH.CAPTCHA_INVALID`，401 `SYS.AUTH.INVALID_CREDENTIALS`／`SYS.AUTH.REFRESH_INVALID`，403 `SYS.AUTH.PASSWORD_EXPIRED`，429 `COMMON.REQUEST.RATE_LIMITED`。

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

待辦 `ApprovalTask`：`instanceId`、`documentType`、`documentId`、`documentNo`、`summary`、`amount`、`submittedBy`、`submittedByUsername`、`submittedAt`、`stepNo`、`stepCount`、`stepName`、`onBehalfOfUserId`（代理時的被代理人）、`version`（核准／駁回時帶回）。實例明細 `ApprovalInstanceDetail` 另含 `status`、`currentStepNo`、`allowSelfApproval`、`completedAt` 與 `steps[]`（`stepNo`、`name`、`approverType`、`roleId`／`roleName`、`userId`／`username`、`minAmount`、`status`（`Waiting`／`Pending`／`Approved`／`Rejected`／`Skipped`）、`actedBy`／`actedByUsername`、`onBehalfOfUserId`／`onBehalfOfUsername`、`actedAt`、`comment`）。

錯誤：400 驗證（`documentType`、`name`、`steps`、`steps[i].roleId|userId|minAmount|approverType|name`、`endDate`、`delegateUserId`、`delegatorUserId`、`remark`、`comment`、`status`、`sort`）；401 `SYS.APPROVAL.USER_REQUIRED`（進程內呼叫沒有登入用戶）；403 `COMMON.PERMISSION.DENIED`、`SYS.APPROVAL.NOT_APPROVER`；404 `SYS.APPROVAL_FLOW.NOT_FOUND`、`SYS.APPROVAL_DELEGATION.NOT_FOUND`、`SYS.APPROVAL.NOT_FOUND`；409 `SYS.APPROVAL_FLOW.DUPLICATE_TYPE`、`SYS.APPROVAL_DELEGATION.OVERLAP`、`SYS.APPROVAL.ALREADY_PENDING`、版本衝突；422 `SYS.APPROVAL.NOT_PENDING`、`SYS.APPROVAL.SELF_APPROVAL`。

**進程內介面**（不開放 HTTP，`MyERP.Shared.Approvals`）：

- `IApprovalWorkflowService.SubmitAsync(ApprovalSubmission(documentType, documentId, documentNo, amount, summary))` → `ApprovalState(instanceId, documentType, documentId, status, currentStepNo, stepCount)`；沒有適用步驟時狀態即為 `Approved`。
- `IApprovalWorkflowService.CancelAsync(documentType, documentId)`：撤回審批中的實例。
- `IApprovalWorkflowService.GetStateAsync(documentType, documentId)`：最近一次送審的狀態（未送審為 null）。
- `IApprovalResultHandler`（單據模組實作並註冊 DI）：`DocumentType` 與 `HandleAsync(ApprovalCompleted)`，審批人作出最終決定時在同一交易內呼叫。
