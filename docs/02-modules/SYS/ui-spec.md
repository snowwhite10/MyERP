# SYS 組織介面規格

> 已完成：組織（WU-SYS-001）、登入（WU-SYS-003）、用戶（WU-SYS-002）、角色（WU-SYS-015 第一段；用戶權限對話框待下一段）。三語、明暗主題沿用共用設定；按鈕依權限顯示（`*erpHasPermission`），後端仍為準。

元件位於 `src/frontend/src/app/modules/sys/organization/`：`organization-list.ts`（`OrganizationList` 頁面）、`organization-form.ts`（表單）、`organization.service.ts`（API）。路由 `src/frontend/src/app/modules/sys/sys.routes.ts`（lazy，`i18nScopeGuard('sys')`）：

| 路由 | 介面 | 路由守衛 | 選單（系統管理） |
|---|---|---|---|
| `/sys/companies` | 8.58 | `permissionGuard('SYS.COMPANY.VIEW')` | `layout-menu-sys-companies` |
| `/sys/branches` | 8.59 | `permissionGuard('SYS.BRANCH.VIEW')` | `layout-menu-sys-branches` |
| `/sys/departments` | 8.60 | `permissionGuard('SYS.DEPARTMENT.VIEW')` | `layout-menu-sys-departments` |
| `/sys/users` | 8.61、8.63 | `permissionGuard('SYS.USER.VIEW')` | `layout-menu-sys-users` |
| `/sys/roles` | 8.64 | `permissionGuard('SYS.ROLE.VIEW')` | `layout-menu-sys-roles` |

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
