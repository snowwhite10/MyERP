# SYS 組織介面規格

> 已完成：組織（WU-SYS-001）、登入（WU-SYS-003）、用戶（WU-SYS-002）、角色與用戶權限（WU-SYS-015）、稽核查詢（WU-SYS-007）、單號規則（WU-SYS-008）。三語、明暗主題沿用共用設定；按鈕依權限顯示（`*erpHasPermission`），後端仍為準。

元件位於 `src/frontend/src/app/modules/sys/organization/`：`organization-list.ts`（`OrganizationList` 頁面）、`organization-form.ts`（表單）、`organization.service.ts`（API）。路由 `src/frontend/src/app/modules/sys/sys.routes.ts`（lazy，`i18nScopeGuard('sys')`）：

| 路由 | 介面 | 路由守衛 | 選單（系統管理） |
|---|---|---|---|
| `/sys/companies` | 8.58 | `permissionGuard('SYS.COMPANY.VIEW')` | `layout-menu-sys-companies` |
| `/sys/branches` | 8.59 | `permissionGuard('SYS.BRANCH.VIEW')` | `layout-menu-sys-branches` |
| `/sys/departments` | 8.60 | `permissionGuard('SYS.DEPARTMENT.VIEW')` | `layout-menu-sys-departments` |
| `/sys/users` | 8.61、8.63 | `permissionGuard('SYS.USER.VIEW')` | `layout-menu-sys-users` |
| `/sys/roles` | 8.64 | `permissionGuard('SYS.ROLE.VIEW')` | `layout-menu-sys-roles` |
| `/sys/number-rules` | 8.67 | `permissionGuard('SYS.NUMBER_RULE.VIEW')` | `layout-menu-sys-number-rules` |
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
