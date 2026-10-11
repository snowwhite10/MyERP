# SYS 組織介面規格

## User Portal（WU-SYS-011b）

對照 BlueWhale 4.4.1.1.2 Homepage Interface 與 4.4.1.1.3 Change Password Interface；原參考清單沒有個人設定頁，MyERP 追加 1.4。首頁 `/` 維持現有圖表元件，但所有指標都標示示範，並顯示登入帳號、目前公司、修改密碼及個人設定入口。修改密碼 `/change-password` 在主框架外；一般路徑須登入，到期路徑 `?expired=1&username=…` 容許匿名並顯示 Captcha。個人設定 `/personal-settings` 須登入，語言及主題即時儲存於本機瀏覽器。

| Interface No. | Interface | Related REQ_F |
|---|---|---|
| 1.2 | Homepage Interface | REQ_F4111.1.4 |
| 1.3 | Change Password Interface | REQ_F4111.1.2 |
| 1.4 | Personal Settings Interface | REQ_F4111.1.5 |

| Requirement ID | Item | Description | Purpose | Format | Valid Range | Related I/O | Author | Related REQ_F |
|---|---|---|---|---|---|---|---|---|
| REQ_IO_A1.2.1 | User and Company (Label) | 登入帳號及目前公司 | 確認工作上下文 | Text | 目前 Session 與公司授權 | None | Developer | REQ_F4111.1.4 |
| REQ_IO_A1.2.2 | Change Password (Link) | 修改密碼入口 | 開啟本人修改表單 | Link | `/change-password` | REQ_IO_A1.3.1 | Developer | REQ_F4111.1.4, REQ_F4111.1.2 |
| REQ_IO_A1.2.3 | Personal Settings (Link) | 個人設定入口 | 開啟偏好頁 | Link | `/personal-settings` | REQ_IO_A1.4.1 | Developer | REQ_F4111.1.4, REQ_F4111.1.5 |
| REQ_IO_A1.3.1 | Current Password (Input) | 目前密碼 | 驗證本人 | Password | 必填，最多 128 字元 | None | Developer | REQ_F4111.1.2 |
| REQ_IO_A1.3.2 | New Password (Input) | 新密碼與確認 | 符合密碼政策並避免輸入錯誤 | Password | 必填、兩次相同、不同於舊密碼 | None | Developer | REQ_F4111.1.2 |
| REQ_IO_A1.3.3 | Expired Password Recovery (Form) | 帳號與 Captcha | 密碼到期時匿名修改 | Text + SVG Captcha | 帳號必填；驗證碼 4–6 位數字 | None | Developer | REQ_F4111.1.2 |
| REQ_IO_A1.3.4 | Submit (Button) | 送出修改 | 取得新 Token 並替換 Session | Button | 送出中不得重複提交 | None | Developer | REQ_F4111.1.2 |
| REQ_IO_A1.4.1 | Language (Dropdown) | 介面語言 | 保存偏好 | Option | 繁體、簡體、英文 | None | Developer | REQ_F4111.1.5 |
| REQ_IO_A1.4.2 | Theme (Dropdown) | 主題 | 保存偏好 | Option | 亮、暗、跟隨系統 | None | Developer | REQ_F4111.1.5 |

> 已完成：組織（WU-SYS-001）、登入（WU-SYS-003）、用戶（WU-SYS-002）、角色與用戶權限（WU-SYS-015）、稽核查詢（WU-SYS-007）、單號規則（WU-SYS-008）、審批流程（WU-SYS-009）、共用對話框與頁面範本（WU-SYS-014）、IP 白名單（WU-SYS-006）、系統參數（WU-SYS-010b）。三語、明暗主題沿用共用設定；按鈕依權限顯示（`*erpHasPermission`），後端仍為準。

元件位於 `src/frontend/src/app/modules/sys/organization/`：`organization-list.ts`（`OrganizationList` 頁面）、`organization-form.ts`（表單）、`organization.service.ts`（API）。路由 `src/frontend/src/app/modules/sys/sys.routes.ts`（lazy，`i18nScopeGuard('sys')`）：

| 路由 | 介面 | 路由守衛 | 選單（系統管理） |
|---|---|---|---|
| `/sys/companies` | 8.58 | `permissionGuard('SYS.COMPANY.VIEW')` | `layout-menu-sys-companies` |
| `/sys/branches` | 8.59 | `permissionGuard('SYS.BRANCH.VIEW')` | `layout-menu-sys-branches` |
| `/sys/departments` | 8.60 | `permissionGuard('SYS.DEPARTMENT.VIEW')` | `layout-menu-sys-departments` |
| `/sys/users` | 8.61、8.63 | `permissionGuard('SYS.USER.VIEW')` | `layout-menu-sys-users` |
| `/sys/roles` | 8.64 | `permissionGuard('SYS.ROLE.VIEW')` | `layout-menu-sys-roles` |
| `/sys/number-rules` | 8.67 | `permissionGuard('SYS.NUMBER_RULE.VIEW')` | `layout-menu-sys-number-rules` |
| `/sys/approvals` | 8.70 | `permissionGuard('SYS.APPROVAL.PROCESS', 'SYS.APPROVAL.VIEW')`（任一） | `layout-menu-sys-approvals` |
| `/sys/approval-flows` | 8.68 | `permissionGuard('SYS.APPROVAL_FLOW.VIEW')` | `layout-menu-sys-approval-flows` |
| `/sys/approval-delegations` | 8.69 | `permissionGuard('SYS.APPROVAL_DELEGATION.VIEW')` | `layout-menu-sys-approval-delegations` |
| `/sys/audit-logs` | 8.66 | `permissionGuard('SYS.AUDIT.VIEW')` | `layout-menu-sys-audit-logs` |

頁面配置：標題＋「新增」（CREATE 權限）；篩選列（關鍵字，Enter 或「搜尋」按鈕送出；公司頁另有「顯示已停用」）；nz-table 伺服器端分頁（每頁 20，可調）、代碼／名稱排序；欄位為代碼、名稱、（部門：分店、上級部門）、狀態標籤（啟用／停用）、操作（編輯，EDIT 權限）。新增／編輯以對話框呈現，編輯時代碼唯讀，可切換啟用。API 錯誤在對話框內以三語訊息顯示（重複代碼、版本衝突、權限、驗證、網絡）。切換公司後自動重新查詢。部門頁的分店與上級部門選項取目前公司的啟用資料（各最多 200 筆），上級部門排除部門本身，循環由後端檢查。

| Interface No. | Interface | Related REQ_F |
|---|---|---|
| 8.58 | Company View Interface | REQ_F4118.20.1, REQ_F4118.20.2, REQ_F4118.20.3, REQ_F4118.20.4 |
| 8.59 | Branch View Interface | REQ_F4118.21.1, REQ_F4118.21.2, REQ_F4118.21.3 |
| 8.60 | Department View Interface | REQ_F4118.22.1, REQ_F4118.22.2, REQ_F4118.22.3 |

| Requirement ID | Item | Description | Purpose | Format | Valid Range | Related I/O | Author | Related REQ_F |
|---|---|---|---|---|---|---|---|---|
| REQ_IO_A8.58.1 | Company Code (Input) | 公司代碼 | 唯一識別公司 | VARCHAR(30) | 1–30 字元；ASCII 英文、數字、`-`、`_` | None | Developer | REQ_F4118.20.2 |
| REQ_IO_A8.58.2 | Company Name (Input) | 公司名稱 | 顯示公司 | NVARCHAR(200) | 1–200 字元 | None | Developer | REQ_F4118.20.2 |
| REQ_IO_A8.59.1 | Branch Code (Input) | 分店代碼 | 公司內唯一 | VARCHAR(30) | 1–30 字元；ASCII 英文、數字、`-`、`_` | None | Developer | REQ_F4118.21.2 |
| REQ_IO_A8.60.1 | Department Code (Input) | 部門代碼 | 公司內唯一 | VARCHAR(30) | 1–30 字元；ASCII 英文、數字、`-`、`_` | None | Developer | REQ_F4118.22.2 |
| REQ_IO_A8.60.2 | Parent Department (Dropdown) | 上級部門 | 建立層級 | bigint ID | 同公司啟用部門 | None | Developer | REQ_F4118.22.2 |

## Login Interface（WU-SYS-003）

| Interface No. | Interface | Related REQ_F |
|---|---|---|
| 8.62 | Login Interface | REQ_F4118.24.1, REQ_F4118.24.5 |

路由 `/login`（主框架外，`guestGuard` + `i18nScopeGuard('sys')`），元件 `src/frontend/src/app/pages/login/login.page.ts`；共用 Captcha 元件 `src/frontend/src/app/shared/captcha/captcha.ts`（`erp-captcha`，ControlValueAccessor，值 `{ captchaId, code }`）。卡片置中，使用設計 token，亮／暗主題一致；頁底可切換三種語言。頂列顯示帳號與登出按鈕（`layout-username`、`layout-logout`）。

| Requirement ID | Item | Description | Purpose | Format | Valid Range | Related I/O | Author | Related REQ_F |
|---|---|---|---|---|---|---|---|---|
| REQ_IO_A8.62.1 | Username (Input) | 帳號 | 登入身分 | Text，`autocomplete=username` | 必填、非空白，最多 100 字元；送出前去除前後空白 | None | Developer | REQ_F4118.24.1 |
| REQ_IO_A8.62.2 | Password (Input) | 密碼 | 驗證身分 | Password，`autocomplete=current-password` | 必填，最多 128 字元；失敗後清空 | None | Developer | REQ_F4118.24.1 |
| REQ_IO_A8.62.3 | Captcha Code (Input) | 驗證碼 | 防自動化登入 | 數字，`inputmode=numeric` | 4–6 位數字（非數字自動移除） | REQ_IO_A8.62.4 | Developer | REQ_F4118.24.1 |
| REQ_IO_A8.62.4 | Captcha Image (Button) | 驗證碼圖片 | 顯示題目，點擊換圖 | SVG data URI | 每次送出失敗後自動換新 | None | Developer | REQ_F4118.24.1 |
| REQ_IO_A8.62.5 | Sign In (Button) | 登入 | 送出 | Button | 送出中顯示載入狀態，不可重複送出 | None | Developer | REQ_F4118.24.5 |
| REQ_IO_A8.62.6 | Error Message (Alert) | 錯誤訊息 | 顯示 `errors.<errorCode>` | Text | INVALID_CREDENTIALS／CAPTCHA_INVALID／PASSWORD_EXPIRED／VALIDATION_FAILED／網路／未知 | None | Developer | REQ_F4118.24.5 |

## User Interfaces（WU-SYS-002）

| Interface No. | Interface | Related REQ_F |
|---|---|---|
| 8.61 | User Identity Create Interface | REQ_F4118.23.1, REQ_F4118.23.2, REQ_F4118.23.6 |
| 8.63 | User Maintenance Interface | REQ_F4118.23.5, REQ_F4118.23.7, REQ_F4118.23.8 |

路由 `/sys/users`（`permissionGuard('SYS.USER.VIEW')`，選單 `layout-menu-sys-users`），元件 `src/frontend/src/app/modules/sys/users/user-list.ts`（`UserList`）、`user.service.ts`（API 與錯誤代碼對應）。頁面配置：標題＋「新增」（`SYS.USER.CREATE`）；關鍵字搜尋帳號；nz-table 伺服器端分頁（每頁 20，可調）、帳號排序；欄位為帳號、狀態（啟用／停用標籤，另有「已鎖定」「密碼已過期」標籤）、密碼修改時間（顯示伺服器 UTC+8 時間 `yyyy-MM-dd HH:mm`）、操作。操作按鈕：停用／啟用與解鎖（`SYS.USER.EDIT`，解鎖只在已鎖定的列出現）、重設密碼（`SYS.USER.RESET_PASSWORD`）；同一時間只處理一個列操作。成功時頁面上方顯示三語提示並重新查詢；失敗（不可停用自己、版本衝突、找不到、權限、網絡）顯示可關閉的錯誤提示。新增（8.61）與重設密碼共用對話框：新增有帳號欄位，重設時標題顯示帳號、無帳號欄位；密碼與確認密碼須相同；API 失敗後清空兩個密碼欄位；Enter 可送出。切換公司後重新查詢（只列目前公司的用戶，ADR-016）。前端不顯示、也不會收到雜湊、失敗次數或鎖定期限。

| Requirement ID | Item | Description | Purpose | Format | Valid Range | Related I/O | Author | Related REQ_F |
|---|---|---|---|---|---|---|---|---|
| REQ_IO_A8.61.1 | Username (Input) | 帳號 | 建立登入身分 | ASCII text | 去除前後空白後 3–100 字元，字母／數字／點／底線／連字號；不分大小寫唯一 | None | Developer | REQ_F4118.23.1 |
| REQ_IO_A8.61.2 | Password (Input) | 初始密碼 | 依目前政策驗證，提交後清空 | Password，`autocomplete=new-password` | 必填，最多 128 字元；其餘依政策（預設 12 字元以上，含大小寫、數字、符號），不符時顯示政策說明 | REQ_IO_A8.61.3 | Developer | REQ_F4118.23.2 |
| REQ_IO_A8.61.3 | Confirm Password (Input) | 確認密碼 | 防止輸入錯誤 | Password | 須與密碼相同 | REQ_IO_A8.61.2 | Developer | REQ_F4118.23.2 |
| REQ_IO_A8.63.1 | Keyword (Input) | 搜尋帳號 | 篩選列表 | Text | 去除前後空白，不分大小寫 | None | Developer | REQ_F4118.23.5 |
| REQ_IO_A8.63.2 | Status (Tag) | 狀態 | 顯示啟用、鎖定、密碼過期 | Tag | 啟用／停用；已鎖定；密碼已過期 | None | Developer | REQ_F4118.23.5 |
| REQ_IO_A8.63.3 | Activate / Deactivate (Button) | 啟用／停用 | 切換帳號狀態 | Button | 帶讀取時的 `version`；不可停用自己 | None | Developer | REQ_F4118.23.7 |
| REQ_IO_A8.63.4 | Unlock (Button) | 解鎖 | 清除鎖定 | Button | 只在已鎖定時顯示 | None | Developer | REQ_F4118.23.8 |
| REQ_IO_A8.63.5 | Reset Password (Button) | 重設密碼 | 開啟重設對話框 | Button | 新密碼依政策驗證，成功後清除鎖定並重新起算到期 | REQ_IO_A8.61.2 | Developer | REQ_F4118.23.8 |

## Role Interfaces（WU-SYS-015）

| Interface No. | Interface | Related REQ_F |
|---|---|---|
| 8.64 | Role Maintenance Interface | REQ_F4118.25.1, REQ_F4118.25.2, REQ_F4118.25.3, REQ_F4118.25.4 |

路由 `/sys/roles`（`permissionGuard('SYS.ROLE.VIEW')`，選單 `layout-menu-sys-roles`）。元件是 `src/frontend/src/app/modules/sys/roles/role-list.ts`（`RoleList`），API、權限分組與錯誤代碼對應在 `role.service.ts`。

頁面配置：
- 標題和「新增」按鈕（`SYS.ROLE.CREATE`）。
- 關鍵字搜尋代碼或名稱。
- nz-table 伺服器端分頁（每頁 20，可調），可依代碼或名稱排序（預設代碼）。
- 欄位：代碼、名稱、資料範圍（本人／本部門／本分店／全公司）、權限（全權角色顯示「全權」標籤，其他顯示項數）、狀態、操作。
- 操作：編輯（`SYS.ROLE.EDIT`）；刪除（`SYS.ROLE.DELETE`，以 popconfirm 確認，帶讀取時的 `version`）。刪除使用中角色時，在頁面上方顯示 `SYS.ROLE.IN_USE`。

新增與編輯共用一個對話框（寬 720px）：
- 代碼只在新增時出現，送出時去除空白並轉大寫。
- 編輯時可切換啟用。
- 權限目錄在第一次開啟時載入一次，依模組分組顯示勾選框。模組勾選框可全選或全不選，部分勾選時呈半選。
- 勾選全權時權限勾選停用，並提示全權自動擁有全部權限。已勾選的權限仍保留，取消全權後恢復。
- API 錯誤（重複代碼、版本衝突、找不到、驗證、權限、網絡）在對話框內以三語顯示。

角色不分公司，但權限在目前公司檢查，所以切換公司後會重新查詢。

| Requirement ID | Item | Description | Purpose | Format | Valid Range | Related I/O | Author | Related REQ_F |
|---|---|---|---|---|---|---|---|---|
| REQ_IO_A8.64.1 | Role Code (Input) | 角色代碼 | 全系統唯一識別角色 | VARCHAR(50) | 1–50 字元；ASCII 英文、數字、`-`、`_`；保存為大寫；建立後不可修改 | None | Developer | REQ_F4118.25.2 |
| REQ_IO_A8.64.2 | Role Name (Input) | 角色名稱 | 顯示角色 | NVARCHAR(200) | 去除空白後 1–200 字元 | None | Developer | REQ_F4118.25.2, REQ_F4118.25.3 |
| REQ_IO_A8.64.3 | Data Scope (Dropdown) | 資料範圍 | 決定可見單據範圍 | Self／Department／Branch／Company | 必選，預設本人 | None | Developer | REQ_F4118.25.2 |
| REQ_IO_A8.64.4 | Full Access (Checkbox) | 全權 | 擁有全部權限 | Boolean | 勾選時權限清單停用 | REQ_IO_A8.64.5 | Developer | REQ_F4118.25.2 |
| REQ_IO_A8.64.5 | Permissions (Checkbox group) | 權限 | 依模組勾選權限代碼 | `模組.資源.動作` | 只可選權限目錄內的代碼；模組可全選 | REQ_IO_A8.64.4 | Developer | REQ_F4118.25.2, REQ_F4118.25.3 |
| REQ_IO_A8.64.6 | Delete (Button) | 刪除 | 軟刪除未使用的角色 | Button + popconfirm | 帶 `version`；使用中回 `SYS.ROLE.IN_USE` | None | Developer | REQ_F4118.25.4 |

| Interface No. | Interface | Related REQ_F |
|---|---|---|
| 8.65 | User Permission Edit Interface（BlueWhale 4.4.1.8.26） | REQ_F4118.25.6, REQ_F4118.25.8 |

用戶列表每列有「權限」按鈕（`SYS.USER.EDIT`），點擊後開啟對話框（寬 640px）。元件是 `src/frontend/src/app/modules/sys/users/user-permission-dialog.ts`（`UserPermissionDialog`），API 在 `user.service.ts` 的 `assignments`、`assign`、`organizationOptions`。

開啟時同時載入兩項資料：
- 用戶在操作者可管理公司的指派（`GET /users/{id}/companies`）。
- 啟用中的角色（`GET /roles`，最多 200 筆）。

兩者都載入後，預設選目前公司並帶出現有設定。

公司下拉只列操作者可切換的公司（ADR-017）。所選公司已授權時顯示啟用／停用標籤和「預設公司」標籤，尚未授權時顯示「尚未授權此公司，保存後新增」。

分店與部門下拉以所選公司的 `X-Company-Id` 查詢該公司的啟用資料（各最多 200 筆）：
- 操作者沒有該公司的 VIEW 權限（403）時顯示提示，欄位可留空。
- 切換公司後，舊的回應不套用。

其他欄位：角色多選、預設公司、啟用此公司的存取。

保存以 PUT 整組取代角色：
- 成功後關閉對話框，列表上方顯示「已更新 {username} 的權限」。
- 失敗（停用自己 422、用戶或公司 404、驗證 400、權限、網絡）在對話框內以三語顯示。
- 保存中不重複送出。

| Requirement ID | Item | Description | Purpose | Format | Valid Range | Related I/O | Author | Related REQ_F |
|---|---|---|---|---|---|---|---|---|
| REQ_IO_A8.65.1 | Company (Dropdown) | 公司 | 選擇要設定的公司 | bigint ID | 操作者可切換的公司 | REQ_IO_A8.65.2 | Developer | REQ_F4118.25.6, REQ_F4118.25.8 |
| REQ_IO_A8.65.2 | Branch / Department (Dropdown) | 分店／部門 | 組織歸屬（資料範圍用） | bigint ID，可清空 | 所選公司的啟用分店／部門 | REQ_IO_A8.65.1 | Developer | REQ_F4118.25.6 |
| REQ_IO_A8.65.3 | Roles (Multi-select) | 角色 | 整組取代該公司的角色 | bigint ID 陣列 | 啟用角色；可為空（無權限） | None | Developer | REQ_F4118.25.6 |
| REQ_IO_A8.65.4 | Default Company (Checkbox) | 預設公司 | 登入後預設的操作公司 | Boolean | 設定後取消其他預設 | None | Developer | REQ_F4118.25.6 |
| REQ_IO_A8.65.5 | Access Active (Checkbox) | 啟用存取 | 停用後該公司不授權 | Boolean | 不可停用自己 | None | Developer | REQ_F4118.25.6 |

## Audit Log Interface（WU-SYS-007）

| Interface No. | Interface | Related REQ_F |
|---|---|---|
| 8.66 | Audit Log Query Interface | REQ_F4118.27.4, REQ_F4118.27.6 |

路由 `/sys/audit-logs`（`permissionGuard('SYS.AUDIT.VIEW')`，選單 `layout-menu-sys-audit-logs`）。元件是 `src/frontend/src/app/modules/sys/audit/audit-log-list.ts`（`AuditLogList`），API、欄位差異轉換和操作／類別標籤在 `audit-log.service.ts`。頁面只能查詢，沒有修改或刪除。

頁面配置：
- 篩選列一：開始日期、結束日期（原生日期欄位，`yyyy-MM-dd`）和快選按鈕「今天」「近 7 天」「近 30 天」。快選會填入日期並立即查詢；結束日早於開始日時顯示提示，不送出查詢。
- 篩選列二：類別（資料變更／安全事件）、操作（已知操作類型，可搜尋）、資料表、記錄 ID、「查詢」與「重設」。從網址帶入用戶時，顯示可關閉的「用戶 #id」標籤。
- nz-table 伺服器端分頁（每頁 20，可調），依時間排序（預設新到舊）。欄位：展開、時間（伺服器 UTC+8 時間 `yyyy-MM-dd HH:mm:ss`）、類別標籤、操作標籤（登入失敗和萬用碼登入用警示色；未知操作顯示原代碼）、資料表／記錄、用戶（沒有帳號時顯示 `#userId`，沒有用戶時顯示「（無）」）、IP。
- 展開列：資料變更顯示「欄位／修改前／修改後」，安全事件顯示明細值。空值顯示「（空）」，`***` 照原樣顯示，另顯示 TraceId。沒有明細的列不顯示展開按鈕。
- 資料表／記錄是連結，點擊後把 `tableName`、`recordId` 寫入網址，開啟該記錄的完整修改歷史。

單據修改歷史：`/sys/audit-logs?tableName=<表名>&recordId=<Id>`（可加 `userId`）。由 `withComponentInputBinding` 帶入 query string。頁面上方顯示「<表名> #<Id> 的修改歷史」；日後單據頁的稽核頁籤用這個網址連結。按「重設」會清除網址參數，回到全部記錄。

切換公司後重新查詢（只看到目前公司的記錄，ADR-019）。錯誤（權限 403、驗證 400、網絡、未知）以三語提示顯示。

| Requirement ID | Item | Description | Purpose | Format | Valid Range | Related I/O | Author | Related REQ_F |
|---|---|---|---|---|---|---|---|---|
| REQ_IO_A8.66.1 | Date Range (Date inputs + quick buttons) | 開始／結束日期 | 限制查詢期間 | `yyyy-MM-dd` | 含頭尾；結束日不可早於開始日；快選今天、近 7、30 天 | None | Developer | REQ_F4118.27.4, REQ_F4118.27.6 |
| REQ_IO_A8.66.2 | Category (Dropdown) | 類別 | 區分資料變更與安全事件 | `DATA`／`SECURITY` | 可清空 | None | Developer | REQ_F4118.27.6 |
| REQ_IO_A8.66.3 | Action (Dropdown) | 操作 | 依操作類型篩選 | CREATE、UPDATE、DELETE、VOID、APPROVE、POST、LOGIN、LOGIN_FAILED、MASTER_CODE_LOGIN、LOGOUT、PERMISSION_CHANGE、EXPORT | 可清空 | None | Developer | REQ_F4118.27.6 |
| REQ_IO_A8.66.4 | Table / Record ID (Input) | 資料表、記錄 ID | 查看單據修改歷史 | Text | 資料表最多 128 字元、記錄 ID 最多 100 字元；去除前後空白 | REQ_IO_A8.66.6 | Developer | REQ_F4118.27.6 |
| REQ_IO_A8.66.5 | Changes (Expandable row) | 欄位差異 | 顯示修改前後的值 | 欄位／修改前／修改後 | 敏感欄位顯示 `***`；空值顯示「（空）」 | None | Developer | REQ_F4118.27.2, REQ_F4118.27.6 |
| REQ_IO_A8.66.6 | Record History (Link / query string) | 修改歷史 | 由單據或列開啟單筆記錄歷史 | `?tableName=&recordId=` | 安全事件沒有記錄 ID，不可開啟 | REQ_IO_A8.66.4 | Developer | REQ_F4118.27.6 |

| Interface No. | Interface | Related REQ_F |
|---|---|---|
| 8.67 | Document Number Rule Interface | REQ_F4118.28.1, REQ_F4118.28.2, REQ_F4118.28.3, REQ_F4118.28.4, REQ_F4118.28.6 |

路由 `/sys/number-rules`（`permissionGuard('SYS.NUMBER_RULE.VIEW')`，選單「單號規則」`layout-menu-sys-number-rules`）。元件是 `src/frontend/src/app/modules/sys/number-rules/number-rule-list.ts`（`NumberRuleList`），API、列舉、樣式組合（`formatNumber`，與後端 `NumberRule.Format` 相同）和週期檢查（`periodCovered`）在 `number-rule.service.ts`。規則不可刪除，以停用代替。

頁面配置：
- 標題＋「新增」（`SYS.NUMBER_RULE.CREATE`）；關鍵字（單據類型／名稱）搜尋。
- nz-table 伺服器端分頁（每頁 20，可調），單據類型（預設）／名稱排序。欄位：單據類型、名稱、樣式（後端回傳的今天第 1 號）、日期格式、重設週期、狀態（啟用／停用；已發出單號另顯示「已發號」標籤）、操作（「預覽」所有可檢視者可用；「編輯」需 `SYS.NUMBER_RULE.EDIT`）。
- 新增／編輯對話框：單據類型（新增時輸入，編輯時唯讀）、名稱、前綴、日期格式、流水號長度、重設週期、啟用（只在編輯時顯示）。對話框底部即時顯示「樣式（今天第 1 號）」，不呼叫 API。重設週期不在日期格式內時立即顯示提示，不可保存。已發號的規則在上方提示鎖定原因，日期格式與重設週期停用，流水號長度下限改為原長度。
- 預覽對話框：單據日期（原生日期欄位，預設今天）＋「預覽」，顯示該日期的下一個單號；提示預覽不佔號。清空日期時由後端使用今天；較早送出的回應晚到時不覆蓋結果，關閉後遲到的回應不套用。

切換公司後回到第一頁重新查詢並關閉預覽（規則屬於目前公司）。錯誤（重複類型、找不到、已發號、流水號用盡、版本衝突、權限、驗證、網絡）以三語提示顯示。

| Requirement ID | Item | Description | Purpose | Format | Valid Range | Related I/O | Author | Related REQ_F |
|---|---|---|---|---|---|---|---|---|
| REQ_IO_A8.67.1 | Document Type (Input) | 單據類型 | 識別規則所屬單據 | `{PREFIX}.{DOCUMENT}`，例如 `SD.ORDER` | 英文字母、數字、底線，最多 50 字；保存為大寫；公司內唯一；建立後不可修改 | None | Developer | REQ_F4118.28.2, REQ_F4118.28.6 |
| REQ_IO_A8.67.2 | Name (Input) | 名稱 | 規則說明 | Text | 1–100 字元，去除前後空白 | None | Developer | REQ_F4118.28.2, REQ_F4118.28.3 |
| REQ_IO_A8.67.3 | Prefix (Input) | 前綴 | 單號開頭 | Text | 0–10 個英數字、`-`、`_`；保存為大寫 | REQ_IO_A8.67.7 | Developer | REQ_F4118.28.2, REQ_F4118.28.3 |
| REQ_IO_A8.67.4 | Date Format (Dropdown) | 日期格式 | 單號的日期部分 | None／yyyy／yyyyMM／yyyyMMdd／yyMM／yyMMdd | 已發號時不可修改 | REQ_IO_A8.67.6 | Developer | REQ_F4118.28.2, REQ_F4118.28.3 |
| REQ_IO_A8.67.5 | Sequence Length (Number) | 流水號長度 | 補零位數 | 整數 | 3–10；已發號時不可小於原長度 | REQ_IO_A8.67.7 | Developer | REQ_F4118.28.2, REQ_F4118.28.3 |
| REQ_IO_A8.67.6 | Reset Period (Dropdown) | 重設週期 | 流水號歸 1 的時機 | 不重設／每年／每月／每日 | 必須包含在日期格式內；已發號時不可修改 | REQ_IO_A8.67.4 | Developer | REQ_F4118.28.2, REQ_F4118.28.3 |
| REQ_IO_A8.67.7 | Sample (Text) | 樣式 | 即時顯示今天第 1 號 | 前綴＋日期＋補零流水號 | 唯讀 | REQ_IO_A8.67.3 | Developer | REQ_F4118.28.6 |
| REQ_IO_A8.67.8 | Active (Checkbox) | 啟用 | 停用後各模組不可再取號 | Boolean | 只在編輯時顯示 | None | Developer | REQ_F4118.28.3 |
| REQ_IO_A8.67.9 | Preview Date (Date input) | 單據日期 | 預覽該日期的下一個單號 | `yyyy-MM-dd` | 預設今天；可清空（後端用今天）；不佔號 | None | Developer | REQ_F4118.28.4 |

## Approval Interfaces（WU-SYS-009）

| Interface No. | Interface | Related REQ_F |
|---|---|---|
| 8.68 | Approval Flow Maintenance Interface | REQ_F4118.29.1, REQ_F4118.29.2, REQ_F4118.29.8 |
| 8.69 | Approval Delegation Interface | REQ_F4118.29.3, REQ_F4118.29.8 |
| 8.70 | Approval Inbox Interface | REQ_F4118.29.5, REQ_F4118.29.6, REQ_F4118.29.7, REQ_F4118.29.9 |

元件位於 `src/frontend/src/app/modules/sys/approvals/`：`approval-flow-list.ts`（`ApprovalFlowList`，8.68）、`approval-delegation-list.ts`（`ApprovalDelegationList`，8.69）、`approval-inbox.ts`（`ApprovalInbox`，8.70）；API、列舉、錯誤代碼與審批人選項在 `approval.service.ts`。選單項目可設多個權限代碼（任一即顯示），審批待辦用 `['SYS.APPROVAL.PROCESS', 'SYS.APPROVAL.VIEW']`。三頁的資料都屬於目前公司，切換公司後回到第一頁重新查詢並關閉對話框。

**8.68 審批流程**：
- 標題＋「新增」（`SYS.APPROVAL_FLOW.CREATE`）；關鍵字（單據類型／名稱）搜尋；伺服器端分頁，單據類型（預設）／名稱排序。欄位：單據類型、名稱、步驟（依次序列出「名稱（角色／用戶：審批人）≥ 門檻」）、狀態（啟用／停用、「允許自審」標籤）、操作（編輯，`SYS.APPROVAL_FLOW.EDIT`）。流程不可刪除。
- 對話框：單據類型（新增時輸入、編輯時唯讀）、名稱、允許自審、啟用（只在編輯時），以及步驟清單（1–10 步）：每步名稱、審批人類型（角色／用戶）、角色或用戶下拉（可搜尋）、最低金額（選填），可上移、下移、移除（至少保留 1 步）、新增步驟。只送出所選類型的審批人 Id。編輯時提示修改只影響之後的送審。
- 審批人選項：啟用角色（`GET /sys/roles`，需 `SYS.ROLE.VIEW`）與目前公司的啟用用戶（`GET /sys/users`，需 `SYS.USER.VIEW`），各最多 200 筆。載入失敗時顯示提示，列表以 `#Id` 顯示審批人；步驟原有的審批人不在選項內（例如已停用）時補上 `#Id` 選項。

**8.69 審批代理**：
- 標題＋「新增」（`SYS.APPROVAL_DELEGATION.CREATE`）；篩選：用戶（被代理人或代理人，可清除）、有效日期（原生日期欄位）；開始日期排序（預設新到舊）。欄位：被代理人、代理人、期間、備註、狀態（啟用／停用；今天在期間內且啟用時另顯示「生效中」）、操作（編輯，`SYS.APPROVAL_DELEGATION.EDIT`）。
- 對話框：新增時選擇被代理人與代理人（不可同一人），編輯時兩者唯讀；開始與結束日期（預設今天，結束不可早於開始）、備註（≤ 200）、啟用（只在編輯時）。重疊期間由後端回 409 並以三語顯示。

**8.70 審批待辦**：
- 頁籤「我的待辦」（`SYS.APPROVAL.PROCESS`）與「審批歷史」（`SYS.APPROVAL.VIEW`）只顯示有權限者；預設待辦，只有 VIEW 時預設歷史。篩選：單據類型（轉大寫）、關鍵字（單號／摘要），歷史另有狀態；送審時間排序（待辦預設舊到新，歷史預設新到舊）。
- 待辦欄位：單據（類型＋單號）、摘要、金額（最多 4 位小數）、送審人、送審時間、步驟（「第 n / m 步」＋步驟名稱，代理時顯示「代 被代理人」標籤）、操作（核准、駁回；有 VIEW 時另有明細）。
- 核准／駁回對話框：顯示單據、金額與步驟；意見（≤ 500，駁回必填，未填時前端提示且不送出），帶待辦的 `version`。成功後提示「已核准」「已核准並送往下一步」或「已駁回」並重新查詢；錯誤（自審、非審批人、已完成、版本衝突、驗證、網絡）在對話框內三語顯示。
- 歷史欄位：單據、摘要、金額、狀態（審批中另顯示第幾步）、送審人、送審時間、完成時間、明細。明細對話框顯示狀態、金額、摘要、送審人與各步驟（審批人、門檻、狀態、處理人與被代理人、處理時間、意見）；較早送出的明細請求晚到時不覆蓋。

| Requirement ID | Item | Description | Purpose | Format | Valid Range | Related I/O | Author | Related REQ_F |
|---|---|---|---|---|---|---|---|---|
| REQ_IO_A8.68.1 | Document Type (Input) | 單據類型 | 流程所屬單據 | `{PREFIX}.{DOCUMENT}`，例如 `PUR.ORDER` | 英文字母、數字、底線，最多 50 字；保存為大寫；公司內唯一；建立後不可修改 | None | Developer | REQ_F4118.29.1, REQ_F4118.29.8 |
| REQ_IO_A8.68.2 | Flow Name (Input) | 流程名稱 | 流程說明 | Text | 1–100 字元，去除前後空白 | None | Developer | REQ_F4118.29.1, REQ_F4118.29.2 |
| REQ_IO_A8.68.3 | Allow Self-approval (Checkbox) | 允許自審 | 送審人可否審批自己的單據 | Boolean | 預設否 | None | Developer | REQ_F4118.29.1, REQ_F4118.29.2 |
| REQ_IO_A8.68.4 | Step Name (Input) | 步驟名稱 | 顯示於待辦與記錄 | Text | 1–100 字元 | None | Developer | REQ_F4118.29.1, REQ_F4118.29.8 |
| REQ_IO_A8.68.5 | Approver Type (Dropdown) | 審批人類型 | 角色或指定用戶 | Role／User | 必填 | REQ_IO_A8.68.6 | Developer | REQ_F4118.29.1, REQ_F4118.29.8 |
| REQ_IO_A8.68.6 | Approver (Dropdown) | 審批人 | 處理該步驟的角色或用戶 | bigint ID | 啟用角色或本公司啟用用戶 | REQ_IO_A8.68.5 | Developer | REQ_F4118.29.1, REQ_F4118.29.8 |
| REQ_IO_A8.68.7 | Minimum Amount (Input) | 最低金額 | 金額門檻（本位幣） | 數字 | 選填；≥ 0，最多 11 位整數、4 位小數 | None | Developer | REQ_F4118.29.1, REQ_F4118.29.8 |
| REQ_IO_A8.68.8 | Active (Checkbox) | 啟用 | 停用後送審自動核准 | Boolean | 只在編輯時顯示 | None | Developer | REQ_F4118.29.2 |
| REQ_IO_A8.69.1 | Delegator (Dropdown) | 被代理人 | 交出審批工作的用戶 | bigint ID | 本公司啟用用戶；建立後不可修改 | REQ_IO_A8.69.2 | Developer | REQ_F4118.29.3 |
| REQ_IO_A8.69.2 | Delegate (Dropdown) | 代理人 | 代為審批的用戶 | bigint ID | 本公司啟用用戶，不可與被代理人相同；建立後不可修改 | REQ_IO_A8.69.1 | Developer | REQ_F4118.29.3 |
| REQ_IO_A8.69.3 | Start / End Date (Date input) | 開始／結束日期 | 代理期間（含頭尾） | `yyyy-MM-dd` | 結束不早於開始；同一被代理人的啟用期間不可重疊 | None | Developer | REQ_F4118.29.3 |
| REQ_IO_A8.69.4 | Remark (Textarea) | 備註 | 代理原因 | Text | 0–200 字元 | None | Developer | REQ_F4118.29.3 |
| REQ_IO_A8.70.1 | Comment (Textarea) | 意見 | 核准或駁回的說明 | Text | 0–500 字元；駁回必填 | None | Developer | REQ_F4118.29.5, REQ_F4118.29.9 |
| REQ_IO_A8.70.2 | Status Filter (Dropdown) | 狀態 | 篩選審批歷史 | 審批中／已核准／已駁回／已撤回 | 可清除 | None | Developer | REQ_F4118.29.7, REQ_F4118.29.9 |

## Shared Dialogs and Page Templates（WU-SYS-014）

| Interface No. | Interface | Related REQ_F |
|---|---|---|
| 8.71 | Confirmation / Prompt Box Interface（BlueWhale 4.4.1.9） | REQ_F4118.31.1, REQ_F4118.31.2 |
| 8.72 | Select Dialog Interface（BlueWhale 4.4.1.9 Select Client / Supplier / Item） | REQ_F4118.31.3 |
| 8.73 | Standard List Page Template | REQ_F4118.31.4, REQ_F4118.31.6 |
| 8.74 | Standard Document Page Template | REQ_F4118.31.5, REQ_F4118.31.6 |
| 8.75 | IP Whitelist Interface | REQ_F4118.32.1, REQ_F4118.32.2, REQ_F4118.32.3, REQ_F4118.32.6 |

共用元件位於 `src/frontend/src/app/shared/`，用法見 ui-conventions 第 5–7 節，決策見 ADR-024：

| 元件 | 檔案 | 說明 |
|---|---|---|
| `ErpDialogService` | `dialogs/dialog.service.ts` | `confirm()`、`alert()`、`select()`、`success()`、`closeAll()`；各模組不直接用 NzModalService |
| `MessageDialog` | `dialogs/message-dialog.ts` | 8.71 確認框／提示框內容（`erp-message-*` testid） |
| `SelectDialog` | `dialogs/select-dialog.ts` | 8.72 選擇視窗內容（`erp-select-*` testid） |
| `ListPage` | `list-page/list-page.ts` | 8.73 `erp-list-page`；`table-query.ts` 排序與分頁輔助、`PagedResult`、每頁筆數 |
| `DocumentPage` | `document-page/document-page.ts` | 8.74 `erp-document-page`、`adjacentIds()`；`unsaved-changes.guard.ts` |
| `DateRangeFilter` | `date-range/date-range.ts` | `erp-date-range`、`quickRange()` |
| `StatusTag` | `status-tag/status-tag.ts` | `erp-status-tag` |

**8.71 確認框／提示框**：置中、寬 416 px；圖示（確認／警告為驚嘆號、資訊、成功、錯誤）＋標題＋內容（可多行）。確認框按鈕「取消」在左、「確定」在右（危險動作為紅色）；提示框只有「確定」。確認框點遮罩不關閉，Esc 等同取消。
**8.72 選擇視窗**：寬 720 px；標題、關鍵字輸入＋「查詢」（Enter 亦可）、表格（勾選欄＋頁面指定的欄位，每頁 10 筆，伺服器端分頁）、底部「已選 n 筆」「取消」「確定」。
**8.73 標準列表頁**：標題列（標題、動作）、篩選區（欄位、日期範圍、查詢、重設）、批次操作列（已選 n 筆、批次按鈕、清除選取）、錯誤提示（重試）、表格。角色頁（8.64）已採用。
**8.74 標準單據頁**：表頭列（返回、標題＋單號、狀態標籤、尚未儲存、上一筆／下一筆、動作）、版本衝突提示（重新載入）、業務錯誤提示、內容卡片、頁籤區。第一個業務單據（MD／INV）採用時補 E2E。

| Requirement ID | Item | Description | Purpose | Format | Valid Range | Related I/O | Author | Related REQ_F |
|---|---|---|---|---|---|---|---|---|
| REQ_IO_A8.71.1 | OK / Cancel (Button) | 確定／取消 | 確認或放棄動作 | Button | 確認框兩個按鈕，提示框只有確定；Esc 等同取消 | None | Developer | REQ_F4118.31.1, REQ_F4118.31.2 |
| REQ_IO_A8.72.1 | Keyword (Input) | 關鍵字 | 搜尋主檔 | Text | 去除前後空白；查詢回到第 1 頁 | None | Developer | REQ_F4118.31.3 |
| REQ_IO_A8.72.2 | Selection (Checkbox / Row) | 選取 | 選擇主檔資料 | Row key | 單選 1 筆、多選跨頁累積；至少 1 筆才可確定 | REQ_IO_A8.72.1 | Developer | REQ_F4118.31.3 |
| REQ_IO_A8.73.1 | Date Range (Date inputs + quick buttons) | 開始／結束日期 | 限制查詢期間 | `yyyy-MM-dd` | 含頭尾；快選今天、本週、本月、上月、本季、本年；開始晚於結束時提示 | None | Developer | REQ_F4118.31.6 |
| REQ_IO_A8.73.2 | Page Size (Dropdown) | 每頁筆數 | 分頁 | 整數 | 10／20／50／100／200 | None | Developer | REQ_F4118.31.4 |
| REQ_IO_A8.74.1 | Previous / Next (Button) | 上一筆／下一筆 | 依列表順序切換單據 | Button | 第一筆停用上一筆、最後一筆停用下一筆 | None | Developer | REQ_F4118.31.5 |

## 8.75 IP Whitelist Interface（WU-SYS-006）

`/sys/ip-whitelist` 使用共用 `erp-list-page`、伺服器端分頁與 `table-query`；`SYS.IP_WHITELIST.VIEW` 保護路由及選單。角色／用戶目標在列表中顯示 ID，因規則是全系統設定，不以目前公司查詢目標資料。新增時可選全系統／角色／用戶層級；編輯時層級、目標及 IP 網段唯讀。停用經共用確認框，帶 `version` 送出。總開關只在伺服器設定，不顯示於介面。

| Requirement ID | Item | Description | Purpose | Format | Valid Range | Related I/O | Author | Related REQ_F |
|---|---|---|---|---|---|---|---|---|
| REQ_IO_A8.75.1 | Keyword (Input) | 網段或備註關鍵字 | 篩選列表 | Text | 去除前後空白，查詢回第 1 頁 | None | Developer | REQ_F4118.32.1, REQ_F4118.32.6 |
| REQ_IO_A8.75.2 | Scope (Dropdown) | 全系統／角色／用戶 | 設定比對層級 | Enum | 建立時可選；編輯唯讀 | REQ_IO_A8.75.3 | Developer | REQ_F4118.32.2, REQ_F4118.32.6 |
| REQ_IO_A8.75.3 | Target ID (Input) | 角色或用戶 ID | 指定對象 | Positive integer | 全系統留空；角色／用戶必填 | REQ_IO_A8.75.2 | Developer | REQ_F4118.32.2, REQ_F4118.32.6 |
| REQ_IO_A8.75.4 | IP Address / Range (Input) | IP 或 CIDR | 設定允許來源 | IPv4 / IPv6 / CIDR | 完整位址；CIDR 網段從網路位址開始；伺服器作最終驗證 | None | Developer | REQ_F4118.32.3, REQ_F4118.32.6 |
| REQ_IO_A8.75.5 | Valid From / To (Date Picker) | 有效期間 | 控制規則生效日期 | yyyy-MM-dd | 可留空；結束不得早於開始 | None | Developer | REQ_F4118.32.2, REQ_F4118.32.6 |
| REQ_IO_A8.75.6 | Description (Input) | 備註 | 辨識規則用途 | Text | 最長 200 字元 | None | Developer | REQ_F4118.32.2, REQ_F4118.32.6 |
| REQ_IO_A8.75.7 | Disable (Button) | 停用規則 | 保留稽核歷史 | Button | 需 EDIT 權限；確認後帶版本送出 | REQ_IO_A8.75.4 | Developer | REQ_F4118.32.2, REQ_F4118.32.6 |

## 8.55 Parameter Edit Interface（WU-SYS-010b）

| Interface No. | Interface | Related REQ_F |
|---|---|---|
| 8.55 | Parameter Edit Interface | REQ_F4118.17.1, REQ_F4118.17.2, REQ_F4118.17.4 |

路由 `/sys/parameters`（`permissionGuard('SYS.PARAMETER.VIEW')`、`canDeactivate: [unsavedChangesGuard]`，選單 `layout-menu-sys-parameters`）。元件 `src/frontend/src/app/modules/sys/parameters/parameter-settings.ts`（`ParameterSettings`），API、用戶端檢查與 400 欄位對應在 `parameter.service.ts`。

頁面配置：標題列有「重新載入」「放棄修改」「保存」（後兩者只在有 `SYS.PARAMETER.EDIT` 時顯示；沒有修改或有欄位錯誤時停用）。參數依模組、分組以卡片顯示，卡片標題為「模組 · 分組」。每列：三語名稱與參數鍵、依型別的輸入欄位與「已修改」標籤、預設值（布林顯示是／否）、範圍、「恢復預設」（值與預設值不同且有 EDIT 權限）。用戶端錯誤顯示在該列下方；伺服器 400 依 `items[i]` 對應到列；409 顯示錯誤並提供「重新載入」。沒有 EDIT 權限時顯示唯讀提示、欄位停用。切換公司重新查詢並丟棄草稿；有未保存修改時離開頁面以共用確認框詢問。i18n：`sys.parameters.modules.<模組>`、`sys.parameters.groups.<分組>`、`sys.parameters.keys.<參數鍵>`（鍵中的 `.` 形成巢狀），新增參數的模組須同時補三語名稱。

| Requirement ID | Item | Description | Purpose | Format | Valid Range | Related I/O | Author | Related REQ_F |
|---|---|---|---|---|---|---|---|---|
| REQ_IO_A8.55.1 | Boolean Parameter (Switch) | 開關型參數 | 啟用／停用功能 | `true`／`false` | — | None | Developer | REQ_F4118.17.2, REQ_F4118.17.4 |
| REQ_IO_A8.55.2 | Whole Number Parameter (Input) | 整數參數 | 例如小數位數 | 整數字串 | 宣告的最小值至最大值 | None | Developer | REQ_F4118.17.2, REQ_F4118.17.4 |
| REQ_IO_A8.55.3 | Number Parameter (Input) | 十進位參數 | 數值設定 | 十進位字串（不使用指數） | 宣告的範圍；精度由伺服器判斷 | None | Developer | REQ_F4118.17.2, REQ_F4118.17.4 |
| REQ_IO_A8.55.4 | Text Parameter (Input) | 文字參數 | 名稱、備註等 | Text | 宣告的長度上限 | None | Developer | REQ_F4118.17.2, REQ_F4118.17.4 |
| REQ_IO_A8.55.5 | Option Parameter (Dropdown) | 選項參數 | 從固定選項選擇 | Enum | 宣告的選項 | None | Developer | REQ_F4118.17.2, REQ_F4118.17.4 |
| REQ_IO_A8.55.6 | Restore Default (Button) | 恢復預設 | 把欄位設回預設值 | Button | 需 EDIT；保存後才生效 | REQ_IO_A8.55.1, REQ_IO_A8.55.2, REQ_IO_A8.55.3, REQ_IO_A8.55.4, REQ_IO_A8.55.5 | Developer | REQ_F4118.17.4 |
| REQ_IO_A8.55.7 | Save (Button) | 保存 | 批次保存有修改的參數 | Button | 需 EDIT；只送有修改的項目並帶 `version` | REQ_IO_A8.55.1, REQ_IO_A8.55.2, REQ_IO_A8.55.3, REQ_IO_A8.55.4, REQ_IO_A8.55.5 | Developer | REQ_F4118.17.2, REQ_F4118.17.4 |

## 8.56 Print Settings Edit Interface（WU-SYS-010c）

| Interface No. | Interface | Related REQ_F |
|---|---|---|
| 8.56 | Print Settings Edit Interface | REQ_F4118.18.1, REQ_F4118.18.2 |

路由 `/sys/print-settings`（`data: { view: 'print' }`、`permissionGuard('SYS.PARAMETER.VIEW')`、`canDeactivate: [unsavedChangesGuard]`，選單 `layout-menu-sys-print-settings`）。重用 8.55 的 `ParameterSettings` 元件（ADR-027）：`view` 輸入（路由資料綁定）為 `print` 時標題為「列印設定」，只顯示 `print` 分組；8.55 則排除此分組。

頁面配置：標題列與 8.55 相同（重新載入、放棄修改、保存）。上方「抬頭預覽」卡片依草稿即時顯示：左側 Logo 標記（開啟時）、中間公司名稱（空白時為目前公司名稱）、郵遞區號與地址、電話與傳真，右側印章標記（開啟時），下方提示公司名稱留空的規則。下方為「系統管理 · 列印」卡片，欄位如下表；長度超過 100 的文字（頁尾備註）用 3 行的多行輸入框。

| Requirement ID | Item | Description | Purpose | Format | Valid Range | Related I/O | Author | Related REQ_F |
|---|---|---|---|---|---|---|---|---|
| REQ_IO_A8.56.1 | Company Name (Input) | 列印公司名稱 | 單據抬頭 | Text | 最多 100 字；空白時用公司主檔名稱 | None | Developer | REQ_F4118.18.1, REQ_F4118.18.2 |
| REQ_IO_A8.56.2 | Address (Input) | 地址 | 單據抬頭 | Text | 最多 200 字 | None | Developer | REQ_F4118.18.1, REQ_F4118.18.2 |
| REQ_IO_A8.56.3 | Postal Code (Input) | 郵遞區號 | 單據抬頭 | Text | 最多 10 字 | None | Developer | REQ_F4118.18.1, REQ_F4118.18.2 |
| REQ_IO_A8.56.4 | Phone (Input) | 電話 | 單據抬頭 | Text | 最多 30 字 | None | Developer | REQ_F4118.18.1, REQ_F4118.18.2 |
| REQ_IO_A8.56.5 | Fax (Input) | 傳真 | 單據抬頭 | Text | 最多 30 字 | None | Developer | REQ_F4118.18.1, REQ_F4118.18.2 |
| REQ_IO_A8.56.6 | Purchase Order Footer (Textarea) | 採購單頁尾備註 | 列印在採購單底部 | Text（可多行） | 最多 500 字 | None | Developer | REQ_F4118.18.1, REQ_F4118.18.2 |
| REQ_IO_A8.56.7 | Sales Delivery Footer (Textarea) | 銷售出貨單頁尾備註 | 列印在出貨單底部 | Text（可多行） | 最多 500 字 | None | Developer | REQ_F4118.18.1, REQ_F4118.18.2 |
| REQ_IO_A8.56.8 | Print Logo (Switch) | 是否列印 Logo | 控制單據顯示 Logo | `true`／`false` | 預設否 | None | Developer | REQ_F4118.18.1, REQ_F4118.18.2 |
| REQ_IO_A8.56.9 | Print Stamp (Switch) | 是否列印印章 | 控制單據顯示公司印章 | `true`／`false` | 預設否 | None | Developer | REQ_F4118.18.1, REQ_F4118.18.2 |
| REQ_IO_A8.56.10 | Header Preview (Panel) | 抬頭預覽 | 保存前確認版面 | 唯讀 | 依草稿即時更新 | REQ_IO_A8.56.1, REQ_IO_A8.56.2, REQ_IO_A8.56.3, REQ_IO_A8.56.4, REQ_IO_A8.56.5, REQ_IO_A8.56.8, REQ_IO_A8.56.9 | Developer | REQ_F4118.18.1 |
| REQ_IO_A8.56.11 | Save (Button) | 保存 | 批次保存有修改的列印設定 | Button | 需 EDIT；只送有修改的項目並帶 `version` | REQ_IO_A8.56.1, REQ_IO_A8.56.2, REQ_IO_A8.56.3, REQ_IO_A8.56.4, REQ_IO_A8.56.5, REQ_IO_A8.56.6, REQ_IO_A8.56.7, REQ_IO_A8.56.8, REQ_IO_A8.56.9 | Developer | REQ_F4118.18.2 |

## 8.57 Logo & Stamp Edit Interface（WU-SYS-010d）

| Interface No. | Interface | Related REQ_F |
|---|---|---|
| 8.57 | Logo & Stamp Edit Interface | REQ_F4118.19.1, REQ_F4118.19.2 |

路由 `/sys/logo-stamp`（`permissionGuard('SYS.PARAMETER.VIEW')`，選單 `layout-menu-sys-logo-stamp`「Logo 與印章」），元件 `src/frontend/src/app/modules/sys/company-images/company-images.ts`。

頁面配置：標題列（重新載入）、說明（PNG／JPEG、1 MB、16–2048 像素，是否列印在列印設定設定）。下方兩張卡片「公司 Logo」「公司印章」：預覽區（虛線框，圖片等比縮小；印章最大 156 像素），沒有圖片時顯示「尚未上傳」；檔案、大小與寬高、上傳時間；訊息區（成功或錯誤；版本衝突或找不到時附「重新載入」）；按鈕「上傳」（已有圖片時為「替換」）與「刪除」（確認框）。沒有 `SYS.PARAMETER.EDIT` 時顯示唯讀提示並隱藏按鈕。圖片以 HttpClient 取得後轉成 `data:` URL 顯示（前端 CSP 的 `img-src` 為 `'self' data:`，ADR-028）。

| Requirement ID | Item | Description | Purpose | Format | Valid Range | Related I/O | Author | Related REQ_F |
|---|---|---|---|---|---|---|---|---|
| REQ_IO_A8.57.1 | Logo Preview (Image) | Logo 預覽 | 確認目前的 Logo | 唯讀圖片 | PNG／JPEG | None | Developer | REQ_F4118.19.1 |
| REQ_IO_A8.57.2 | Stamp Preview (Image) | 印章預覽 | 確認目前的印章 | 唯讀圖片 | PNG／JPEG | None | Developer | REQ_F4118.19.1 |
| REQ_IO_A8.57.3 | Image Details (Text) | 檔案資料 | 顯示檔名、大小、寬高、上傳時間 | 唯讀 | - | REQ_IO_A8.57.1, REQ_IO_A8.57.2 | Developer | REQ_F4118.19.1 |
| REQ_IO_A8.57.4 | Upload / Replace (Button) | 上傳／替換 | 選擇圖片檔並上傳 | File（`accept="image/png,image/jpeg"`） | 需 EDIT；PNG／JPEG、1 MB、寬高 16–2048 像素 | REQ_IO_A8.57.1, REQ_IO_A8.57.2 | Developer | REQ_F4118.19.2 |
| REQ_IO_A8.57.5 | Delete (Button) | 刪除 | 刪除圖片 | Button | 需 EDIT；確認後帶 `version` 刪除 | REQ_IO_A8.57.1, REQ_IO_A8.57.2 | Developer | REQ_F4118.19.2 |

## Notification View Interface（WU-SYS-012b 待實作）

頂列未讀數與收件箱使用目前公司的資料；切換公司重新載入。列表按建立時間新到舊，顯示類別、標題、摘要、時間、已讀狀態與站內連結；三語與深色模式沿用共用版型。已讀操作限本人通知，過期資料不顯示。介面欄位 ID、按鈕狀態與 E2E 驗收在 012b 實作前定稿（REQ_F4118.33.2–3）。
