# 代碼審查日誌（Code Review Log）

> MASTER_PROMPT 第 10 節：每個模組完成後，按清單自我審查（可讀性、重複代碼、效能、N+1 查詢、安全、錯誤處理）。
> 狀態：`已修正`（附工作單元）／`排程`（附 backlog 項目）／`不修改`（附理由）。

## SYS 模組（WU-SYS-016a，2026-10-11）

**範圍**
- 後端：`src/backend/Modules/MyERP.Modules.System`、`MyERP.Modules.System.Contracts`
- 前端：`src/frontend/src/app/modules/sys`

**方法**：後端與前端各由一個唯讀審查代理逐檔閱讀，主代理再逐項核對原始碼後決定處理方式。

**規則掃描**：沒有 `DateTime.Now/UtcNow`；沒有用 `float/double` 處理業務數值（唯一的 `double` 是請求耗時毫秒，屬技術用途）；沒有 `System.Data.SqlClient`。所有 action 都有 `RequirePermission`、`Authorize` 或 `AllowAnonymous`（另有 ArchitectureTests 保護）。

### 後端發現

| ID | 嚴重度 | 類別 | 位置 | 發現 | 狀態 |
|---|---|---|---|---|---|
| SYS-B01 | High | 安全 | `RoleService.AssignInScopeAsync`、`CompaniesController.Update` | 只檢查目前公司的權限，就能在其他獲授權公司指派角色或修改公司 | 已修正（016a，`AuthorityGuard.EnsurePermissionAsync`，ADR-032） |
| SYS-B02 | High | 安全 | `RoleService` 指派 | 有 `SYS.USER.EDIT` 就能指派全權角色（權限提升），也能改自己的角色 | 已修正（016a，角色上限＋不可改自己角色，ADR-032） |
| SYS-B03 | High | 安全 | `RoleService.UpdateAsync`、`IpWhitelistService` | 角色與 IP 白名單是全系統資源，但任何公司的管理員都可修改；新增第一條 System 範圍規則會鎖住所有公司 | 已修正（016b，`AuthorityGuard.EnsureFullAccessAsync`：修改角色／白名單須在受影響的每間公司全權，ADR-033） |
| SYS-B04 | Medium | 安全 | `UsersController` 重設密碼／解鎖／停用 | 只要求用戶與目前公司有關聯；公司 A 管理員可接管同屬公司 B 的用戶 | 已修正（016a，`EnsureCanManageUserAsync`，ADR-032） |
| SYS-B05 | Medium | 錯誤處理 | `RoleService.CreateAsync` | 兩次 `SaveChanges` 沒有交易，失敗時留下沒有權限的角色 | 已修正（016a，同一交易） |
| SYS-B06 | Medium | 效能 | `NotificationInboxService.MarkAllReadAsync` | 逐筆載入再更新 | 不修改：通知是業務表，每筆變更須經稽核攔截器寫資料稽核並更新版本；`ExecuteUpdate` 會繞過稽核。數量受 90 天保留期限制。已在代碼註明 |
| SYS-B07 | Medium | 安全 | `SystemUserService.VerifyPasswordAsync` | 停用或鎖定的帳號提早回傳、不做雜湊，回應時間差可用來探測帳號狀態 | 已修正（016a，同樣做一次雜湊驗證） |
| SYS-B08 | Low | 可靠性 | `ExpiredNotificationCleanup` | `PeriodicTimer` 要 24 小時後才第一次執行；每日重啟的環境永遠不會清理 | 已修正（016a，啟動 5 分鐘後先清理一次） |
| SYS-B09 | Low | 效能／N+1 | `ApprovalFlowService` 步驟驗證、`OrganizationService` 上層部門檢查 | 迴圈內查詢；前者最多 10 次，後者按部門深度 | 已修正（016c，步驟先驗格式再以兩次查詢確認角色與用戶；部門一次載入上層關係後在記憶體檢查循環） |
| SYS-B10 | Low | 重複代碼 | `ActiveCompanyIdAsync`（4 處）、`UsernamesAsync`（2 處）、代碼／名稱排序 switch（4 處） | 抽出共用 helper | 已修正（016c，`SystemQueries.ActiveCompanyIdAsync`／`UsernamesAsync`／`OrderByKeyOrName`，6 個排序 switch 改用） |
| SYS-B11 | Low | 錯誤處理 | `RedisOneTimeStore` | 接受 `CancellationToken` 但沒有使用 | 不修改：Take／RemoveIf 是原子取用，中途放棄等待會令已取出的 Token 遺失；StackExchange.Redis 有自身逾時 |
| SYS-B12 | Low | 稽核 | `AuthService.ChangePasswordAsync` | 目前密碼錯誤只寫應用程式日誌，沒有寫安全稽核 | 已修正（016a，`LOGIN_FAILED`，原因 `CURRENT_PASSWORD_INVALID`） |
| SYS-B13 | Low | 錯誤處理 | `NotificationPublisher` | 收件人無效時回 403，實為業務規則錯誤 | 已修正（016a，改 422 `SYS.NOTIFICATION.RECIPIENT_UNAVAILABLE`） |

**後端做得好的地方**
- Refresh Token 只存 SHA-256 雜湊，以 Lua 原子取用；改密碼或重設密碼後，以 `PasswordChangedAt` 撤銷舊 Token。Captcha 以固定時間比較；非 Development 環境設定萬用碼時拒絕啟動。
- 登入失敗計數以條件式 `ExecuteUpdate` 更新，並發嘗試無法繞過鎖定。`version` 衝突及唯一索引衝突一致回 409。
- 單號以 `MERGE … WITH (HOLDLOCK)` 在目前交易內產生，並發不會重號。
- 讀取路徑一致使用 `AsNoTracking`、`LongCount`＋`Skip/Take`（頁大小已驗證），並批次查詢用戶名稱與權限。
- 每個請求都由資料庫計算授權，停用用戶或公司即時生效。`PasswordHash` 標記 `[SensitiveData]`，稽核時遮罩。匿名認證端點有限流。

### 前端發現

| ID | 嚴重度 | 類別 | 位置 | 發現 | 狀態 |
|---|---|---|---|---|---|
| SYS-F01 | Medium | 重複代碼 | user／organization／number-rule／approval-flow／approval-delegation／audit-log／approval-inbox 列表 | 未採用 `erp-list-page` 與 `table-query` helper，各自重寫標題、篩選、錯誤提示與排序 | 部分修正（016c）：重複的分頁／排序邏輯改用 `nextTableState`、`sortOrderOf`，10 個列表使用 `PAGE_SIZE_OPTIONS`，連同 016b 的 `latestRequest` 已無重複邏輯。不修改：其餘頁面不全面改用 `erp-list-page` 版面，因為審批待辦有頁籤、稽核有展開列與日期快選，改版面要重寫大量 E2E 而沒有功能收益；新頁面一律使用範本 |
| SYS-F02 | Medium | 競態 | 8 個列表頁的 `reload()` | 沒有過期回應保護，快速翻頁或切換公司時舊回應可能覆蓋新資料 | 已修正（016b，共用 `shared/list-page/latest-request`，8 個列表頁） |
| SYS-F03 | Medium | 效能 | `organization-list.ts` | 部門頁每次翻頁、排序或搜尋都重新載入兩個 200 筆選項清單 | 已修正（016b，選項只在切換公司／頁面類型及儲存後載入） |
| SYS-F04 | Medium | 效能 | `notification-inbox.ts` | effect 追蹤整個公司物件而非 id；每次翻頁都重算未讀數 | 已修正（016c，只追蹤公司 id；翻頁與篩選不重算，切換公司及標記已讀後才重算） |
| SYS-F05 | Medium | 錯誤處理 | `ip-whitelist-list.ts` | `actionError` 不清除；停用沒有防重複點擊；錯誤時沒有重設 `total` | 已修正（016b，送出時清除 `actionError`、`disabling` 防重複、錯誤時 `total` 歸零） |
| SYS-F06 | Low | 重複代碼 | `displayTime` 寫了 5 次，精度不一 | 統一到 `shared/date` | 已修正（016c，`displayTime(value, seconds?)`：一律到分鐘，只有稽核紀錄到秒） |
| SYS-F07 | Low | 重複代碼 | `organization.service.ts` | 重複宣告 `PagedResult`；`PAGE_SIZE_OPTIONS` 從未使用 | 已修正（016c，`PagedResult` 只在 `table-query` 宣告；列表以 `nzPageSizeOptions` 使用） |
| SYS-F08 | Low | i18n | `notification-inbox.ts`、`approval-flow-list.ts`、`approval-inbox.ts` | 通知類別顯示原始代碼；中文全形標點寫死在範本 | 已修正（016c，`sys.notifications.categories.*`，未知類別顯示代碼；步驟摘要與審批人改用參數化 key `stepSummary`、`approverValue`） |
| SYS-F09 | Low | Dark mode | 7 個檔案的 `<input>` 樣式 | CSS 變數後備值寫死淺色 | 已修正（016c，兩種主題都定義了這些變數，移除 8 個檔案的淺色後備值） |
| SYS-F10 | Low | 效能 | `role-list.ts`、`approval-flow-list.ts` | 範本內呼叫方法，每次變更偵測都重算 | 已修正（016c，角色頁 `groupStates` computed；流程列表 `flowRows` 先算好審批人名稱。對話框的 `roleChoices`／`userChoices` 依表單值，不是 signal，保留方法） |
| SYS-F11 | Low | 正確性 | `approval-delegation-list.ts` | `today` 在元件建立時固定，過了午夜就過期 | 已修正（016c，改為 signal，每次載入列表或開啟新增時更新） |
| SYS-F12 | Low | 可讀性 | `ip-whitelist-list.ts` | 多個語句擠在一行 | 已修正（016b，每個語句一行） |

**前端做得好的地方**
- 每條路由都有 `permissionGuard`，按鈕使用 `*erpHasPermission`，參數頁有 `unsavedChangesGuard`。沒有 `innerHTML` 或 `bypassSecurityTrust*`；圖片預覽限 PNG／JPEG data URL。
- 一致使用 standalone、`OnPush`、signals、`@for … track id`，長期訂閱都有 `takeUntilDestroyed`。
- HTTP 錯誤經 `apiErrorKey` 白名單轉成 i18n key；衝突時提供重新載入。
- `approval-inbox`、`user-permission-dialog`、`company-images` 已有請求序號保護。
- 密碼欄在失敗後清空；刪除經共用確認框；更新一律帶 `version`。

### 架構檢查

- 只有 SYS 一個業務模組，其他模組只可引用 `MyERP.Modules.System.Contracts`，由 ArchitectureTests 保護。
- 沒有跨模組讀寫表的情況。新增的 `AuthorityGuard` 留在 SYS Application 層。

### 日誌分析

見 `log-analysis.md`（SYS 段）。
