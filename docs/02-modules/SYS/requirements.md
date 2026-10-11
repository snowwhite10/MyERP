# SYS 組織主檔需求（WU-SYS-001）

> 版本 1.2。公司、分店與部門為後續權限及單據的組織基礎；BlueWhale 公司與部門資料表對應 REQ_DS43.20 / .22。資料模型、API（權限保護）與三語維護頁已完成（WU-SYS-001 DONE）。

| Sub-function No. | Sub-function |
|---|---|
| 8.20 | Company Management |
| 8.21 | Branch Management |
| 8.22 | Department Management |

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4118.20.1 | 1.2 | System should allow authorized users to view company details. | Developer | 僅有組織檢視權的用戶可見其獲授權且未刪除的公司；預設只列啟用公司，勾選「顯示已停用」時一併列出停用公司；列表支援分頁、關鍵字及代碼／名稱排序；無權限回 403。 |
| REQ_F4118.20.2 | 1.2 | System should allow authorized users to create a company. | Developer | 代碼全系統唯一；代碼與名稱必填；代碼限 1–30 字元的 ASCII 英文、數字、`-`、`_`，保存為大寫；重複代碼回 409；建立者在同一交易內獲授權新公司，角色沿用其在目前公司的角色（ADR-015）。 |
| REQ_F4118.20.3 | 1.1 | System should allow authorized users to edit a company. | Developer | 版本不符回 409；審計欄位記錄修改者；目標公司不是用戶獲授權的公司時回 404。 |
| REQ_F4118.20.4 | 1.0 | System should allow authorized users to deactivate a company. | Developer | 停用後不能成為新操作的公司上下文；歷史資料保留。 |
| REQ_F4118.21.1 | 1.0 | System should allow authorized users to view branches in the selected company. | Developer | 公司 A 的查詢不返回公司 B 分店；未選公司結果為空。 |
| REQ_F4118.21.2 | 1.0 | System should allow authorized users to create a branch. | Developer | 公司內代碼唯一；所屬公司必須為授權公司。 |
| REQ_F4118.21.3 | 1.0 | System should allow authorized users to edit or deactivate a branch. | Developer | 版本不符回 409；被引用時只可停用，不得刪除。 |
| REQ_F4118.22.1 | 1.0 | System should allow authorized users to view departments in the selected company. | Developer | 公司隔離、軟刪除篩選生效。 |
| REQ_F4118.22.2 | 1.0 | System should allow authorized users to create a department under a branch or parent department. | Developer | 分店與上級部門必須屬於同一公司；不得循環參照。 |
| REQ_F4118.22.3 | 1.0 | System should allow authorized users to edit or deactivate a department. | Developer | 版本不符回 409；被引用時只可停用。 |

## 追溯與階段

變更記錄：2026-10-10，REQ_F4118.20.2 由 1.0 增至 1.1，明確規定代碼格式與大寫儲存，使服務檢查與資料庫唯一性一致。REQ_F4118.20.1 由 1.0 增至 1.1，明確規定公司列表只顯示授權且啟用的公司與查詢參數。

2026-10-10（WU-SYS-001 完成）：REQ_F4118.20.1 增至 1.2（可列出停用公司以重新啟用）、REQ_F4118.20.2 增至 1.2（建立者授權）、REQ_F4118.20.3 增至 1.1（未授權公司回 404），理由見 ADR-015。

REQ_F4118.20–22 已全部通過驗收：API `/api/v1/sys/companies|branches|departments`（api.md）以 `SYS.COMPANY/BRANCH/DEPARTMENT.VIEW/CREATE/EDIT` 保護，分店／部門以 `X-Company-Id` 公司隔離；介面 8.58–8.60 為 `/sys/companies`、`/sys/branches`、`/sys/departments`。測試見 test-plan.md SYS-ORG-004、005、012、013、020～026。

## 用戶身分與密碼安全（WU-SYS-002）

| Sub-function No. | Sub-function |
|---|---|
| 8.23 | User Identity and Password Security |

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4118.23.1 | 1.0 | System should allow authorized users to create a unique user identity. | Developer | 帳號不分大小寫唯一，停用及軟刪除後仍不可重用；僅保存雜湊，不保存明文密碼。 |
| REQ_F4118.23.2 | 1.0 | System should enforce a configurable password policy. | Developer | 預設 12–128 字元且含大小寫、數字及符號；長度、複雜度與到期天數可由設定調整，不合法設定拒絕啟動。 |
| REQ_F4118.23.3 | 1.0 | System should lock an account after five consecutive failed password attempts. | Developer | 第 5 次連續失敗鎖定 15 分鐘；鎖定期間正確密碼亦不得通過；鎖定到期後開啟新失敗計數窗口，成功後清除計數。 |
| REQ_F4118.23.4 | 1.0 | System should determine whether a user's password has expired. | Developer | 依設定天數及 `password_changed_at` 判斷；0 代表不設到期。登入簽發 Token 前必須檢查。 |
| REQ_F4118.23.5 | 1.0 | System should list the users of the current company with paging, keyword and sorting. | Developer | 只列在目前公司有用戶公司關聯的用戶（含停用）；關鍵字不分大小寫比對帳號；排序 `username`／`-username`，其他欄位 400；回應不含雜湊、失敗次數及鎖定期限，只回 `isLocked`、`isPasswordExpired`（ADR-016）。 |
| REQ_F4118.23.6 | 1.0 | System should allow authorized users to create a user in the current company. | Developer | 需 `SYS.USER.CREATE`；同一交易建立身分及目前公司的預設關聯（無角色）；重複帳號（不分大小寫、含他公司）409 `SYS.USER.DUPLICATE`；弱密碼或帳號格式錯誤 400；建立後可登入。 |
| REQ_F4118.23.7 | 1.0 | System should allow authorized users to activate or deactivate a user. | Developer | 需 `SYS.USER.EDIT` 並帶 `version`（過期 409）；停用後不可登入與刷新，重新啟用後可登入；不可停用自己 422 `SYS.USER.SELF_DEACTIVATION`；他公司用戶 404。 |
| REQ_F4118.23.8 | 1.0 | System should allow authorized users to reset a password and unlock a locked user. | Developer | 重設密碼需 `SYS.USER.RESET_PASSWORD`，依密碼政策驗證，重新起算到期並清除鎖定，舊密碼即失效；解鎖需 `SYS.USER.EDIT`，清除失敗計數與鎖定，未鎖定時亦回 204；他公司用戶 404。 |

2026-10-10：已完成內部模型、雜湊、政策及鎖定的資料庫測試；並發失敗以原子更新累計。帳號管理 API／UI、登入整合及授權驗收待續；WU-SYS-002 維持進行中。

## 登入與 Token（WU-SYS-003）

| Sub-function No. | Sub-function |
|---|---|
| 8.24 | Login, Token and Captcha |

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4118.24.1 | 1.0 | System should allow a user to log in with username, password and a numeric captcha. | Developer | 帳號不分大小寫；Captcha 4–6 位數字，答案只存伺服器端，驗證一次即作廢（不論對錯）；帳密錯誤、帳號不存在、停用或鎖定一律回 401 `SYS.AUTH.INVALID_CREDENTIALS`；密碼到期回 403 `SYS.AUTH.PASSWORD_EXPIRED` 且不發 Token。 |
| REQ_F4118.24.2 | 1.0 | System should issue a 15-minute access token and a 7-day rotating refresh token. | Developer | Access Token 為 HS256 JWT，`sub` 為用戶 Id；Refresh Token 只以 SHA-256 雜湊存 Redis，使用一次即輪替，並發重放只有一個成功。 |
| REQ_F4118.24.3 | 1.0 | System should allow a logged-in user to log out and invalidate the refresh token. | Developer | 登出須帶有效 Access Token；只撤銷屬於本人的 Refresh Token，撤銷後刷新回 401。 |
| REQ_F4118.24.4 | 1.0 | System should provide a development-only captcha master code with startup protection. | Developer | `Captcha:MasterCode` 只在 Development 生效且須為 4–6 位數字；其他環境有設定即寫 Error 並拒絕啟動；每次使用寫 Warning（帳號、IP），並寫 `sys_audit_log` 安全事件 MASTER_CODE_LOGIN（REQ_F4118.27.3）。 |
| REQ_F4118.24.5 | 1.0 | System should provide a trilingual login page that keeps the session and refreshes tokens automatically. | Developer | 未登入進入主框架導向 `/login?returnUrl=…`，登入後只回到站內路徑；Captcha 元件只收 4–6 位數字，失敗後換新圖並清空密碼；錯誤依 `errorCode` 三語顯示；API 請求帶 Bearer，401 時只刷新一次並重送，並發 401 共用同一 refresh；刷新失敗清除 Session 回登入頁；登出撤銷 Refresh Token，伺服器失敗也清除本機 Session。 |

2026-10-10：後端服務、API 與 13 個整合測試完成（myerp_test + 本機 Redis）。同日第二段完成前端登入頁、`erp-captcha`、AuthService、攔截器、路由守衛與 5 個登入 E2E（ADR-013）。萬用碼稽核日誌待 WU-SYS-007；Rate limiting 待 WU-SYS-005。

## 角色、權限與資料範圍（WU-SYS-004）

| Sub-function No. | Sub-function |
|---|---|
| 8.25 | Role, Permission and Data Scope |

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4118.25.1 | 1.0 | System should allow authorized users to view roles and the permission catalog. | Developer | 需 `SYS.ROLE.VIEW`；角色列表支援分頁、關鍵字（代碼／名稱）、`code`／`name` 排序；權限目錄列出各模組宣告的全部代碼；已軟刪除角色不顯示，查詢回 404。 |
| REQ_F4118.25.2 | 1.0 | System should allow authorized users to create a role with permissions and a data scope. | Developer | 需 `SYS.ROLE.CREATE`；代碼 1–50 字元 ASCII 英文、數字、`-`、`_`，保存為大寫，全系統唯一（含已刪除），重複回 409 `SYS.ROLE.DUPLICATE_CODE`；權限代碼不分大小寫輸入，必須在權限目錄內，否則 400；資料範圍為 Self／Department／Branch／Company。 |
| REQ_F4118.25.3 | 1.0 | System should allow authorized users to edit or deactivate a role. | Developer | 需 `SYS.ROLE.EDIT`；帶 `version`，不符回 409，且權限集合不變（同一交易）；停用角色立即不再授予權限。 |
| REQ_F4118.25.4 | 1.0 | System should allow authorized users to delete an unused role. | Developer | 需 `SYS.ROLE.DELETE`；仍有用戶使用回 422 `SYS.ROLE.IN_USE`；軟刪除，代碼不可重用。 |
| REQ_F4118.25.5 | 1.0 | System should enforce permission codes on every API with the backend as the authority. | Developer | 每個 API 動作標註 `模組.資源.動作` 權限（ArchitectureTests 檢查）；未登入 401；缺權限或沒有公司上下文 403 `COMMON.PERMISSION.DENIED`；已登入請求的 `X-Company-Id` 不可存取（未授權、用戶公司關聯停用、公司停用）一律 403 `COMMON.COMPANY.ACCESS_DENIED`。 |
| REQ_F4118.25.6 | 1.0 | System should allow authorized users to grant a user access to a company with roles, organization and default company. | Developer | 需 `SYS.USER.EDIT`；公司必須啟用；分店／部門必須屬於該公司且啟用；角色必須存在且啟用；角色整組取代；每位用戶最多一間預設公司（設定新預設會取消舊預設）。用戶不存在或不屬於目前公司回 404 `SYS.USER.NOT_FOUND`；目標公司不是操作者獲授權的公司回 404 `SYS.COMPANY.NOT_FOUND`；不可停用自己的公司授權（422 `SYS.USER.SELF_DEACTIVATION`）（ADR-017）。 |
| REQ_F4118.25.7 | 1.0 | System should resolve the user's effective permissions and data scope per company and hide unauthorized UI. | Developer | 同一權限取各啟用角色最寬的資料範圍；全權角色視為全部權限、全公司範圍；分店／部門範圍但用戶無對應歸屬時退回本人。`GET /api/v1/sys/auth/authorization` 回傳可存取公司（預設在前）與目前公司權限；前端 `*erpHasPermission`、`permissionGuard` 與選單依此隱藏，切換公司重新載入；本機記住的公司不可存取時改用預設公司。 |
| REQ_F4118.25.8 | 1.0 | System should allow authorized users to view a user's company access, organization and roles. | Developer | 需 `SYS.USER.VIEW`；用戶須屬於目前公司，否則 404；只列出操作者獲授權公司的指派（公司代碼／名稱、分店、部門、預設、啟用、角色 ID），依公司代碼排序（ADR-017）。 |

2026-10-10：後端模型、migration、授權管線、角色／權限目錄／用戶公司授權 API、目前授權 API 與 7 個整合測試完成；前端 PermissionService、`*erpHasPermission`、`permissionGuard`、選單過濾與 3 個 E2E 完成（ADR-014）。角色維護頁面與用戶權限頁（BlueWhale User Permission Edit Interface）待後續工作單元。

2026-10-11（WU-SYS-015）：授權指派加上範圍檢查，並新增指派查詢 API REQ_F4118.25.8（ADR-017）。完成角色維護頁（介面 8.64）與用戶權限對話框（介面 8.65），均支援三語，並有前端單元測試與 E2E。

## 安全中介層（WU-SYS-005）

| Sub-function No. | Sub-function |
|---|---|
| 8.26 | Web Security Middleware |

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4118.26.1 | 1.0 | System should enforce HTTPS with HSTS outside the development environment. | Developer | 非 Development：HTTP 請求 307 轉 HTTPS；HTTPS 回應帶 `Strict-Transport-Security: max-age=31536000; includeSubDomains`（天數可設定）。Development 不轉址、不送 HSTS。 |
| REQ_F4118.26.2 | 1.0 | System should allow cross-origin requests only from configured frontend origins. | Developer | 來源清單在 `Security:Cors:AllowedOrigins`，預設空（不允許跨來源）；只接受 `scheme://host[:port]`，萬用字元、含路徑或非 http(s) 拒絕啟動；不允許 Credentials；允許標頭 Authorization、Content-Type、Accept、Accept-Language、X-Company-Id；公開 Retry-After、Content-Disposition；未列明的來源預檢回應不含 `Access-Control-Allow-Origin`。 |
| REQ_F4118.26.3 | 1.0 | System should rate-limit login, token refresh and captcha requests per client IP. | Developer | 登入、Token 刷新每 IP 每端點每分鐘 5 次；Captcha 每 IP 每分鐘 10 次（ADR-018）；超過回 429 ProblemDetails，`errorCode` 為 `COMMON.REQUEST.RATE_LIMITED`，標題依 Accept-Language 三語，帶 `Retry-After`；寫 Warning 日誌（方法、路徑、IP）。CORS 預檢不計入。每個匿名端點必須套用按 IP 的限流政策（ArchitectureTests 檢查）。 |
| REQ_F4118.26.4 | 1.0 | System should rate-limit authenticated API requests per user. | Developer | 已登入請求每用戶每分鐘 300 次（可設定），不同用戶分開計數；匯出報表政策每用戶每分鐘 10 次，供日後匯出端點套用。白名單內的 IP 仍受限流。 |
| REQ_F4118.26.5 | 1.0 | System should add security headers to every API response. | Developer | 所有回應（含 404、429、500）帶 `X-Content-Type-Options: nosniff`、`X-Frame-Options: DENY`、`Referrer-Policy: no-referrer`、`Content-Security-Policy: default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'`。前端站台的 CSP 見 architecture/security.md，E2E 在該 CSP 下執行。 |
| REQ_F4118.26.6 | 1.0 | System should honour X-Forwarded-For only from configured proxies. | Developer | `Security:ForwardedHeaders:KnownProxies`（IP）與 `KnownNetworks`（CIDR）皆空時不處理 X-Forwarded-*；有設定時只信任列明來源，其他來源偽造的 X-Forwarded-For 被忽略，限流與日誌使用連線 IP；格式錯誤拒絕啟動。 |
| REQ_F4118.26.7 | 1.0 | System should show a trilingual "too many requests" message when an API call is rate-limited. | Developer | 前端收到 429 一律顯示 `errors.COMMON.REQUEST.RATE_LIMITED`（三語），登入頁、組織、用戶、角色頁共用 `apiErrorKey`。 |

2026-10-11（WU-SYS-005）：REQ_F4118.26.1–7 已通過驗收；決策見 ADR-018（Captcha 限流較寬鬆、Token 維持 Authorization 標頭不改 HttpOnly Cookie、前端 CSP 與關閉 critical CSS 內嵌）。

## 稽核日誌（WU-SYS-007）

| Sub-function No. | Sub-function |
|---|---|
| 8.27 | Audit Log |

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4118.27.1 | 1.0 | System should record every data change in sys_audit_log within the same transaction. | Developer | 透過 EF 新增、修改、刪除（含軟刪除）都寫一筆記錄，內容包括公司、用戶（背景程序為系統用戶）、時間（IClock）、表名、記錄 Id、操作類型（CREATE／UPDATE／DELETE，可由 Application 指定 VOID／APPROVE／POST）、欄位差異 JSON（修改只列有變更的欄位）、來源 IP、TraceId。沒有欄位變更不寫記錄。交易回滾或 SaveChanges 失敗（含樂觀鎖衝突）不留記錄，重試時也不會重複（ADR-019）。 |
| REQ_F4118.27.2 | 1.0 | System should protect audit records and sensitive values. | Developer | `[SensitiveData]` 欄位（例如 `password_hash`）的值以 `***` 遮蔽；主鍵、審計欄位、version、company_id 不記錄差異。`sys_audit_log` 只能新增，UPDATE 或 DELETE 由 trigger 拒絕（`append-only`）；`changes` 必須是 JSON。 |
| REQ_F4118.27.3 | 1.0 | System should record login, failed login, logout and captcha master-code login as security events. | Developer | 安全事件記錄帳號、IP、時間與 TraceId。LOGIN_FAILED 的原因只寫 `INVALID_CREDENTIALS` 或 `PASSWORD_EXPIRED`，不寫密碼。萬用碼登入不論結果都寫 MASTER_CODE_LOGIN（REQ_F4118.24.4）。 |
| REQ_F4118.27.4 | 1.0 | System should allow authorized users to query audit logs of the current company. | Developer | `GET /api/v1/sys/audit-logs` 需 `SYS.AUDIT.VIEW`，支援分頁、日期範圍（含頭尾）、類別、操作類型、表名＋記錄 Id（單據修改歷史）、用戶篩選，以時間排序。只列出目前公司的記錄，以及屬於目前公司用戶、沒有公司的安全事件；其他公司的記錄看不到。資料變更記錄會補上帳號。`to` 早於 `from`、類別未知或長度超限時回 400。 |
| REQ_F4118.27.5 | 1.0 | System should record role permission and user company access changes as security events. | Developer | 修改角色的權限、資料範圍、全權或啟用狀態，以及指派用戶公司授權，都寫 PERMISSION_CHANGE，與變更同一交易。記錄內容包括操作者、公司、新增與移除的權限，或指派後的角色、組織、預設公司與啟用狀態。 |
| REQ_F4118.27.6 | 1.0 | System should provide a trilingual audit log page with per-record change history. | Developer | 三語稽核查詢頁 `/sys/audit-logs`（介面 8.66），需 `SYS.AUDIT.VIEW`，無權限時選單不顯示、直接進入顯示 403。可依日期範圍（含快選今天、近 7、30 天；結束日早於開始日時不送出）、類別、操作、資料表＋記錄 ID 篩選，伺服器端分頁並依時間排序。可展開欄位差異（修改前→修改後，`***` 照原樣顯示）或安全事件明細。`?tableName=&recordId=` 開啟單筆記錄的修改歷史，供單據頁的稽核頁籤連結。操作與類別有三語標籤，未知操作顯示原代碼。切換公司後重新查詢。 |

2026-10-11（WU-SYS-007 第一段）：REQ_F4118.27.1–5 的後端部分已通過驗收（ADR-019）。
2026-10-11（WU-SYS-007 第二段）：REQ_F4118.27.6 稽核查詢頁通過驗收（SYS-AUDIT-014）。

## 審批流程引擎（WU-SYS-009）

| Sub-function No. | Sub-function |
|---|---|
| 8.29 | Approval Workflow |

| Requirement ID | Version | Description | Author | 驗收條件 |
|---|---|---|---|---|
| REQ_F4118.29.1 | 1.0 | System should allow user to view and create multi-level approval flows with amount thresholds. | Developer | `GET/POST /api/v1/sys/approval-flows` 需 `SYS.APPROVAL_FLOW.VIEW`／`CREATE`。每間公司每個單據類型 `{PREFIX}.{DOCUMENT}` 一條流程（重複 409 `SYS.APPROVAL_FLOW.DUPLICATE_TYPE`）；1–10 個步驟依清單次序編號；角色步驟須為啟用角色、用戶步驟須為本公司啟用用戶（否則 400 `steps[i].roleId`／`userId`）；金額門檻 ≥ 0、最多 4 位小數；可設允許自審。列表含步驟，支援分頁、關鍵字、排序；他公司的流程 404。 |
| REQ_F4118.29.2 | 1.0 | System should allow user to edit or deactivate an approval flow without affecting pending approvals. | Developer | `PUT /api/v1/sys/approval-flows/{id}` 需 `SYS.APPROVAL_FLOW.EDIT`，帶 `version`（不符 409，只改步驟也遞增版本）；整組取代步驟；停用後送審即自動核准；審批中的實例沿用送審時的步驟快照。沒有刪除。 |
| REQ_F4118.29.3 | 1.0 | System should allow user to assign an approval delegate for a date range. | Developer | `/api/v1/sys/approval-delegations` 需 `SYS.APPROVAL_DELEGATION.VIEW/CREATE/EDIT`。被代理人與代理人為不同的本公司啟用用戶；結束日不早於開始日；同一被代理人的啟用期間不可重疊（409 `SYS.APPROVAL_DELEGATION.OVERLAP`）；可依用戶與有效日期篩選；修改期間、備註、啟用帶 `version`。 |
| REQ_F4118.29.4 | 1.0 | System should enable every module to submit documents for approval through the shared workflow engine. | Developer | 各模組經 `IApprovalWorkflowService.SubmitAsync(單據類型, 單據 Id, 單號, 本位幣金額, 摘要)` 送審：依流程與金額門檻複製適用步驟；沒有啟用流程或沒有適用步驟即自動核准；同一單據同時只可有一個審批中的實例（409 `SYS.APPROVAL.ALREADY_PENDING`，併發亦然）；駁回或撤回後可再送審；可撤回（`CancelAsync`）及查詢最新狀態（`GetStateAsync`）。 |
| REQ_F4118.29.5 | 1.0 | System should allow approvers or their delegates to approve or reject the current approval step. | Developer | `POST /api/v1/sys/approvals/{id}/approve|reject` 需 `SYS.APPROVAL.PROCESS`，帶 `version`（不符 409）。只有目前步驟的審批人或其有效代理人可處理（否則 403 `SYS.APPROVAL.NOT_APPROVER`）；送審人不可自審（422 `SYS.APPROVAL.SELF_APPROVAL`，流程允許除外）；已完成 422 `SYS.APPROVAL.NOT_PENDING`；駁回必填意見。最後一步核准或任一步駁回時，在同一交易內通知單據模組的 `IApprovalResultHandler`，處理失敗則整體回滾。審批動作以 APPROVE／REJECT 寫稽核記錄；代理處理記錄被代理人。 |
| REQ_F4118.29.6 | 1.0 | System should list the pending approval tasks of the current user. | Developer | `GET /api/v1/sys/approvals/tasks` 需 `SYS.APPROVAL.PROCESS`：列出本人或經有效代理可處理的目前步驟（不含自己送審、不允許自審的單據；停用角色或用戶不算），含單號、摘要、金額、送審人、第幾步／共幾步、被代理人與版本；可依單據類型與關鍵字篩選，依送審時間排序。 |
| REQ_F4118.29.7 | 1.0 | System should allow user to view the approval history of a document. | Developer | `GET /api/v1/sys/approvals`（單據類型、單據 Id、狀態、關鍵字篩選，預設新到舊）與 `GET /api/v1/sys/approvals/{id}`（各步驟的審批人、狀態、處理人、被代理人、時間、意見）需 `SYS.APPROVAL.VIEW`；只看目前公司。 |
| REQ_F4118.29.8 | 1.0 | System should provide trilingual approval flow and delegation maintenance pages. | Developer | （第二段）三語 `/sys/approval-flows`（介面 8.68）與 `/sys/approval-delegations`（介面 8.69）：列表、新增／編輯對話框（步驟可增刪排序、角色／用戶選擇、金額門檻）、權限按鈕、切換公司重新查詢。 |
| REQ_F4118.29.9 | 1.0 | System should provide a trilingual approval inbox with approval history. | Developer | （第二段）三語 `/sys/approvals`（介面 8.70）：我的待辦（代理標示）、明細與步驟記錄、核准／駁回對話框（駁回必填意見、帶版本、錯誤三語顯示），以及審批歷史查詢。 |

2026-10-11（WU-SYS-009 第一段）：REQ_F4118.29.1–7 後端已通過驗收（ADR-022）。
2026-10-11（WU-SYS-009 第二段）：REQ_F4118.29.8–9 三語流程、代理與待辦頁（介面 8.68–8.70）通過驗收（SYS-APR-012、SYS-APR-013）。

## 單號規則（WU-SYS-008）

| Sub-function No. | Sub-function |
|---|---|
| 8.28 | Document Number Rule |

| Requirement ID | Version | Description | Author | 驗收條件 |
|---|---|---|---|---|
| REQ_F4118.28.1 | 1.0 | System should allow user to view document number rules of the current company. | Developer | `GET /api/v1/sys/number-rules` 需 `SYS.NUMBER_RULE.VIEW`，分頁、關鍵字（單據類型／名稱）、排序 `documentType`（預設）／`name`；每條規則附今天第 1 號的樣式（`sample`）與是否已發出單號（`isInUse`）。只列出目前公司的規則，他公司的規則回 404。 |
| REQ_F4118.28.2 | 1.0 | System should allow user to create a document number rule with prefix, date format, sequence length and reset period. | Developer | 需 `SYS.NUMBER_RULE.CREATE`。單據類型 `{PREFIX}.{DOCUMENT}`（保存為大寫，最多 50 字）在公司內唯一（重複 409 `SYS.NUMBER_RULE.DUPLICATE_TYPE`，不同公司可相同）；前綴 0–10 個英數字、`-`、`_`（保存為大寫）；日期格式 None／Year／YearMonth／YearMonthDay／ShortYearMonth／ShortYearMonthDay；流水號長度 3–10；重設週期 Never／Yearly／Monthly／Daily，且必須包含在日期格式內（否則 400 `resetPeriod`）。 |
| REQ_F4118.28.3 | 1.0 | System should allow user to edit or deactivate a document number rule without breaking issued numbers. | Developer | 需 `SYS.NUMBER_RULE.EDIT`，帶 `version`（不符 409）。已發出單號的規則：日期格式與重設週期不可改、流水號長度不可縮短（422 `SYS.NUMBER_RULE.IN_USE`）；名稱、前綴、加長流水號、啟用狀態可改。沒有刪除，以停用代替。規則修改寫稽核記錄。 |
| REQ_F4118.28.4 | 1.0 | System should allow user to preview the next document number for a business date. | Developer | `GET /api/v1/sys/number-rules/{id}/preview?date=yyyy-MM-dd`（預設今天）需 `SYS.NUMBER_RULE.VIEW`，回傳該日期的下一個單號，不佔用流水號。 |
| REQ_F4118.28.5 | 1.0 | System should generate unique, gap-free document numbers for every module under concurrency. | Developer | 各模組經 `INumberGenerator.NextAsync(單據類型, 單據日期)` 取號：單號 = 前綴 + 單據日期格式化 + 補零流水號；流水號按公司、規則、重設週期（依單據日期）分開計數。100 次並行取號沒有重號、沒有跳號；在單據交易內取號而交易回滾時號碼退回。規則不存在回 422 `SYS.NUMBER_RULE.NOT_CONFIGURED`、停用回 422 `SYS.NUMBER_RULE.INACTIVE`、流水號用盡回 422 `SYS.NUMBER_RULE.SEQUENCE_EXHAUSTED`、沒有公司上下文回 403（ADR-021）。 |
| REQ_F4118.28.6 | 1.0 | System should provide a trilingual number rule maintenance page. | Developer | 三語 `/sys/number-rules` 頁（介面 8.67）：列表、新增／編輯對話框、即時樣式預覽、已發出單號時鎖定日期格式與重設週期、流水號長度不可縮短；重設週期不在日期格式內時即時提示且不可保存；依業務日期預覽下一號；權限按鈕（新增 CREATE、編輯 EDIT，預覽只需 VIEW）；無 VIEW 時選單不顯示、直接進入顯示 403；切換公司後重新查詢。 |

2026-10-11（WU-SYS-008 第一段）：REQ_F4118.28.1–5 後端已通過驗收（ADR-021）；REQ_F4118.28.6 待第二段。

2026-10-11（WU-SYS-008 第二段）：REQ_F4118.28.6 完成。三語 `/sys/number-rules` 頁（介面 8.67），含預覽下一號（REQ_F4118.28.4）、已發號時鎖定欄位（REQ_F4118.28.3）。前端單元測試 SYS-NUM-010、E2E SYS-NUM-011。

## 種子資料（WU-SYS-013）

| Sub-function No. | Sub-function |
|---|---|
| 8.30 | Development Seed Data |

| Requirement ID | Version | Description | Author | 驗收條件 |
|---|---|---|---|---|
| REQ_F4118.30.1 | 1.0 | System should load demo companies, branches, departments and roles for the Development environment. | Developer | `dotnet run --project src/backend/MyERP.Api -- --seed` 建立 TW01（分店 TPE／TXG／KHH）、HK01（分店 HKG／KLN），每間公司 7 個部門，以及 9 個角色（ADMIN 全權；FINANCE、SALES、PURCHASE、WAREHOUSE、PRODUCTION、HR、MANAGER、VIEWER 依規則對照權限目錄展開，資料範圍全公司）。非 Development 環境拒絕執行，不寫入任何資料（ADR-023）。 |
| REQ_F4118.30.2 | 1.0 | System should create the test accounts of the SeedUsers setting with hashed passwords and company assignments. | Developer | 讀取 `appsettings.Development.json` 的 `SeedUsers`（帳號、密碼、角色代碼）；密碼以 `PasswordHasher` 雜湊後保存。每個帳號可存取兩間公司（預設 TW01），歸屬總公司分店與角色對應部門，並在兩間公司擁有該角色。角色代碼不存在、欄位空白或帳號重複時，在寫入前拒絕。 |
| REQ_F4118.30.3 | 1.0 | System should allow the seed to run repeatedly without duplicating or overwriting data. | Developer | 第二次執行新增 0 筆；不覆蓋已修改的密碼、名稱或資料範圍，也不移除額外加入的角色權限；新模組宣告的權限在重新執行時補上。種子帳號可以用真實 API 登入（SYS-AUTH-017）。 |

2026-10-11（WU-SYS-013）：REQ_F4118.30.1–3 通過驗收（SYS-SEED-001～006、SYS-AUTH-017，ADR-023）。

## 共用介面（WU-SYS-014）

| Sub-function No. | Sub-function |
|---|---|
| 8.31 | Shared Dialogs and Page Templates |

| Requirement ID | Version | Description | Author | 驗收條件 |
|---|---|---|---|---|
| REQ_F4118.31.1 | 1.0 | System should ask for confirmation in a shared trilingual confirmation box before dangerous actions. | Developer | `ErpDialogService.confirm()`（BlueWhale 4.4.1.9 Confirmation Box）：標題／內容為 i18n key 與參數；確定發出 `true`，取消、Esc、關閉發出 `false`；`danger` 時確定按鈕為紅色；確定文字可改（例如「刪除」）。刪除、作廢一律使用（角色刪除已改用，取代 popconfirm）。 |
| REQ_F4118.31.2 | 1.0 | System should show results that need the user's attention in a shared prompt box, and short success messages as toasts. | Developer | `alert(info／success／warning／error)` 只有確定按鈕，關閉後發出一次；圖示依類型；`success(key, params)` 以 `nz-message` 顯示翻譯後文字 3 秒。標題放在內容內，切換語言即時更新。 |
| REQ_F4118.31.3 | 1.0 | System should provide a shared selection window for choosing master data with search and server-side paging. | Developer | `ErpDialogService.select()`（BlueWhale Select Client／Supplier／Item Interface）：欄位、查詢函式、rowKey 由頁面傳入；關鍵字去除前後空白後從第 1 頁查詢；單選時按列選取、按兩下直接確定；多選時勾選跨頁保留，可帶入已選資料；未選取時確定停用；確定回傳資料陣列，取消／關閉回傳 `null`；載入失敗顯示三語錯誤；快速連續查詢只採用最後一次結果。 |
| REQ_F4118.31.4 | 1.0 | System should provide a standard list page template reused by every module. | Developer | `erp-list-page`：標題列＋動作插槽、篩選插槽＋「查詢」「重設」、勾選後顯示批次操作列（已選筆數、批次按鈕、清除選取）、載入錯誤＋「重試」、表格內容；`data-testid` 以 `{prefix}-{resource}` 為前綴。`table-query.ts` 提供排序白名單轉換、`nzSortOrder` 與分頁狀態比較（相同時不重查）。投影內容冒泡的原生 search 事件不觸發查詢。 |
| REQ_F4118.31.5 | 1.0 | System should provide a standard document page template with status, previous / next navigation and unsaved-change protection. | Developer | `erp-document-page`：返回列表、標題＋單號（新單據顯示「（新增）」）、狀態標籤、未儲存標示、上一筆／下一筆（依列表順序 `adjacentIds`，頭尾停用）、動作插槽、409 版本衝突提示＋「重新載入」、422 錯誤提示、內容與頁籤插槽。路由 `canDeactivate: [unsavedChangesGuard]`：無修改直接離開，有修改時確認，選「繼續編輯」留在頁面。 |
| REQ_F4118.31.6 | 1.0 | System should provide a date range filter with quick ranges and a document status tag following the macaron status colours. | Developer | `erp-date-range`：開始／結束日期雙向綁定，快選今天、本週（星期一起）、本月、上月、本季、本年（含頭尾、跨年與閏年正確），快選時發出範圍供立即查詢，開始晚於結束時提示。`erp-status-tag`：草稿、待審、已核准、已過帳、已退回、已作廢、已結案，顏色取 `--erp-status-*`（亮／暗），作廢與結案加刪除線，可覆寫文字。 |

2026-10-11（WU-SYS-014）：REQ_F4118.31.1–6 通過驗收（SYS-UI-001～003，ADR-024）。
