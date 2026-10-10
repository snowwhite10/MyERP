# SYS 組織介面規格

> 已完成（WU-SYS-001）。三語、明暗主題沿用共用設定；按鈕依權限顯示（`*erpHasPermission`），後端仍為準。

元件位於 `src/frontend/src/app/modules/sys/organization/`：`organization-list.ts`（`OrganizationList` 頁面）、`organization-form.ts`（表單）、`organization.service.ts`（API）。路由 `src/frontend/src/app/modules/sys/sys.routes.ts`（lazy，`i18nScopeGuard('sys')`）：

| 路由 | 介面 | 路由守衛 | 選單（系統管理） |
|---|---|---|---|
| `/sys/companies` | 8.58 | `permissionGuard('SYS.COMPANY.VIEW')` | `layout-menu-sys-companies` |
| `/sys/branches` | 8.59 | `permissionGuard('SYS.BRANCH.VIEW')` | `layout-menu-sys-branches` |
| `/sys/departments` | 8.60 | `permissionGuard('SYS.DEPARTMENT.VIEW')` | `layout-menu-sys-departments` |

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

## User Identity Interface（WU-SYS-002，待授權後實作）

| Interface No. | Interface | Related REQ_F |
|---|---|---|
| 8.61 | User Identity Create Interface | REQ_F4118.23.1, REQ_F4118.23.2 |

| Requirement ID | Item | Description | Purpose | Format | Valid Range | Related I/O | Author | Related REQ_F |
|---|---|---|---|---|---|---|---|---|
| REQ_IO_A8.61.1 | Username (Input) | 帳號 | 建立登入身分 | ASCII text | 3–100 字元，字母／數字／點／底線／連字號 | None | Developer | REQ_F4118.23.1 |
| REQ_IO_A8.61.2 | Password (Input) | 初始密碼 | 依目前政策驗證，提交後清空 | Password text | 預設 12–128 字元 | None | Developer | REQ_F4118.23.2 |

前端表單不得顯示 `password_hash`。正式路由及操作要等 WU-SYS-004 權限檢查後接入，並提供 zh-Hant、zh-Hans、en 與 dark mode。
