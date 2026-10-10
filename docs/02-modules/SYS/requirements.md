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
| REQ_F4118.24.4 | 1.0 | System should provide a development-only captcha master code with startup protection. | Developer | `Captcha:MasterCode` 只在 Development 生效且須為 4–6 位數字；其他環境有設定即寫 Error 並拒絕啟動；每次使用寫 Warning（帳號、IP），稽核日誌於 WU-SYS-007 補上。 |
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
| REQ_F4118.25.6 | 1.0 | System should allow authorized users to grant a user access to a company with roles, organization and default company. | Developer | 需 `SYS.USER.EDIT`；公司必須啟用；分店／部門必須屬於該公司且啟用；角色必須存在且啟用；角色整組取代；每位用戶最多一間預設公司（設定新預設會取消舊預設）。用戶不存在回 404。 |
| REQ_F4118.25.7 | 1.0 | System should resolve the user's effective permissions and data scope per company and hide unauthorized UI. | Developer | 同一權限取各啟用角色最寬的資料範圍；全權角色視為全部權限、全公司範圍；分店／部門範圍但用戶無對應歸屬時退回本人。`GET /api/v1/sys/auth/authorization` 回傳可存取公司（預設在前）與目前公司權限；前端 `*erpHasPermission`、`permissionGuard` 與選單依此隱藏，切換公司重新載入；本機記住的公司不可存取時改用預設公司。 |

2026-10-10：後端模型、migration、授權管線、角色／權限目錄／用戶公司授權 API、目前授權 API 與 7 個整合測試完成；前端 PermissionService、`*erpHasPermission`、`permissionGuard`、選單過濾與 3 個 E2E 完成（ADR-014）。角色維護頁面與用戶權限頁（BlueWhale User Permission Edit Interface）待後續工作單元。
