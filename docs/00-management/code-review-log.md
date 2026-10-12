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

## MD 模組（WU-MD-022a，2026-10-13）

**範圍**
- 後端：`src/backend/Modules/MyERP.Modules.MasterData`、`MyERP.Modules.MasterData.Contracts`
- 前端：`src/frontend/src/app/modules/md`、`public/i18n/*/errors.json`、`md.json`

**方法**：後端與前端各由一個唯讀審查代理逐檔閱讀，主代理逐項核對原始碼後決定處理方式。

**規則掃描與架構檢查**：MD 只引用 `MyERP.Shared`、`MyERP.Shared.Infrastructure` 與自己的 Contracts，不引用 SYS 實作或 SYS Contracts；審批經 Shared 的 `IApprovalWorkflowService`／`IApprovalResultHandler`。沒有直接讀寫 `sys_*` 表，沒有原始 SQL。沒有 `DateTime.Now/UtcNow`、`float/double`、`System.Data.SqlClient`、`ExecuteUpdate/ExecuteDelete`（後者會繞過稽核）。ArchitectureTests（ModuleBoundaryTests、BannedApiTests、PermissionRulesTests、MasterDataModuleTests）持續保護以上規則。

### 後端發現

| ID | 嚴重度 | 類別 | 位置 | 發現 | 狀態 |
|---|---|---|---|---|---|
| MD-B01 | High | 業務規則 | `BusinessPartnerService.UpdateAsync`、`PartnerApprovalService.SubmitAsync` | 送審後、審批人決定前仍可修改名稱、分類與預設值，審批人核准的是送審時的代碼／名稱快照 | 已修正（022a，審批中修改回 422 `APPROVAL_PENDING`，ADR-048）。「核准後修改不重審」為有理據的決定，見 ADR-048 第 3 點 |
| MD-B02 | Medium | 業務規則 | `BusinessPartnerService.DeleteAsync` | `Pending` 同時代表「從未送審」與「審批中」；匯入錯的往來對象在送審並被駁回前無法刪除 | 排程（022b）：刪除改以 SYS 實例判斷，需同步修改需求與 MD-CUS-REV-001／MD-SUP-REV-001 |
| MD-B03 | Medium | 資料完整性／並發 | `MasterCategoryService` 刪除、`ItemService`、`FundAccountService` 等 | 刪除是「先檢查引用再寫入」，刪除端與引用端沒有取同一把 `CompanyLock`；並發時可能留下指向已軟刪除分類／單位／付款條件的資料（複合外鍵擋不住，因軟刪除列仍存在） | 排程（022b） |
| MD-B04 | Medium | 業務規則 | `TaxServices.GetRatePercentAsync`、`CalculateAsync` | 不檢查稅碼是否停用（其他 Lookup 契約都會拒絕停用資料）；未定義的幣別代碼默默用 2 位小數 | 排程（022b） |
| MD-B05 | Medium | 資料完整性（潛在） | `ItemService`、`WarehouseServices`、`TaxServices`、`CurrencyServices` | 物料、倉庫、儲位、稅碼／稅率、匯率沒有 `I*Usage` 契約；INV／SD／PUR 上線後無法阻止修改有庫存物料的基本單位、刪除有庫存的儲位或回溯修改稅率 | 排程（022b，先定義契約與呼叫點） |
| MD-B06 | Medium | 效能 | `IUomConverter.ConvertAsync`、`ITaxCalculator.CalculateAsync`、幣別精度 | 單據逐行呼叫時每行 3–5 次查詢，200 行單據約 1,000 次往返 | 排程（WU-MD-026，等第一個使用方出現時設計批次介面） |
| MD-B07 | Low | 效能 | `BusinessPartnerService.RequireApprovedEntityAsync` | 追蹤式載入再查兩次預設代碼；在使用方的 scoped DbContext 累積追蹤實體 | 排程（022b） |
| MD-B08 | Low | 效能 | `ItemImageService` 排序／主圖／刪除 | 整筆載入含最大 2 MB 的圖片內容 | 排程（022b，不得用 ExecuteUpdate 繞過稽核） |
| MD-B09 | Low | 安全強化 | `PartnerImportsControllers` 六個匯入 POST | 沒有 `RequestSizeLimit`／`RequestFormLimits`，Kestrel 先緩衝整個請求（預設約 30 MB）才檢查 2 MB 上限 | 排程（022b） |
| MD-B10 | Low | 錯誤處理 | `FundAccountLookup`、`IncomeExpenseCategoryLookup`、`ProcessLookup` | 代碼為 null 時拋 NullReference／ArgumentNull，成為 500；其他 Lookup 回 null／404 | 排程（022b） |
| MD-B11 | Low | 設計 | `MasterCategoryService` 公開的簡化 Create／Update 多載 | 子類（科目、幣別、稅碼、資金帳戶）繼承後可繞過各自的不變條件與鎖 | 排程（022b，改 protected 或拆出簡單分類基底） |
| MD-B12 | Low | 重複代碼 | 約 16 處 `RequireCompany()`、4 處代碼／名稱排序 switch、約 10 處 2601／2627 判斷、`UpdateAsync` 重複查詢 | 抽出共用 helper | 排程（022b） |

**後端做得好的地方**
- 所有跨實體參照都用 `(company_id, id)` 複合外鍵指向備用鍵；即使驗證漏掉，資料庫也拒絕跨公司引用。
- 並發處理一致：`Version` 以 `OriginalValue` 檢查，衝突回 409；唯一索引衝突（2601／2627）轉成各自的 409 代碼；代碼唯一檢查用 `IgnoreQueryFilters()`，已軟刪除的代碼不可重用。
- 圖形／樹狀修改（單位換算、分類樹、科目樹、預設聯絡人、圖片位置）以 `sp_getapplock` 在交易內序列化；匯入在鎖內重新驗證；循環與深度檢查。
- 圖片上傳以內容判斷類型、雙重大小限制、尺寸上限、每物料數量上限、檔名清理；稽核只記錄位元組數。
- 金額與數量全用 `decimal` 並檢查精度與範圍；單位換算以分子／分母比率傳遞，只在目標單位四捨五入一次。
- 匯入以一次 `IN` 查詢批次解析既有代碼與參照，沒有逐列查詢。

### 前端發現

| ID | 嚴重度 | 類別 | 位置 | 發現 | 狀態 |
|---|---|---|---|---|---|
| MD-F01 | Medium | i18n | `business-partner.service.ts`、`errors.json` | `SYS.APPROVAL.ALREADY_PENDING` 已列為可辨識錯誤碼，但三語都沒有翻譯；重複送審時畫面顯示原始 key。既有測試只比對 key 字串，沒有發現 | 已修正（022a，補三語翻譯；新增 MD-PARTNER-I18N 測試，檢查每個已辨識錯誤碼在三語都有翻譯。英文 `APPROVAL_PENDING` 改為不限刪除的訊息） |
| MD-F02 | Medium | 正確性 | `partner-list.ts`、`uom-page.ts` | 下拉選項只載一頁（100／200 筆）；超過後列表欄位空白且無法選取；`uom-page` 每次換頁都重載 200 筆單位 | 排程（022c） |
| MD-F03 | Medium | 重複代碼 | `item-page.ts`、`fund-setup-page.ts`、`item-attribute-dialog.ts` | 三份各自寫的「載入全部分頁」helper，停止條件不一致 | 排程（022c，抽到 `shared/list-page`） |
| MD-F04 | Medium | 重複代碼 | 12 個 MD 列表頁 | 沒有用 SYS 的 `erp-list-page` 與 `table-query` helper，排序／分頁代碼複製 12 份 | 排程（WU-MD-027） |
| MD-F05 | Medium | 防重複送出 | 幣別、稅碼、倉庫、付款條件、資金設定頁的刪除 | 刪除沒有進行中旗標，快速確認兩次第二次帶舊版本，顯示 409 | 排程（022c） |
| MD-F06 | Medium | 錯誤處理 | 往來對象、分類、物料、幣別、匯率等的儲存／刪除 | 409／404 後沒有重新查詢，列保留舊版本，重試一直 409 | 排程（022c） |
| MD-F07 | Medium | 過期回應 | `partner-contact-dialog.ts` | 聯絡人修改成功後以開始時的往來對象 id 重新載入；期間切換到另一往來對象會顯示錯誤的聯絡人 | 排程（022c） |
| MD-F08 | Low | i18n | `partner-import-dialog.ts` | 匯入列錯誤顯示原始欄位名（`categoryCode`） | 排程（022c） |
| MD-F09 | Low | 上傳驗證 | `partner-import-dialog.ts` | 沒有在前端檢查副檔名與 2 MB 上限 | 排程（022c） |
| MD-F10 | Low | i18n | `payment-term-page.ts` | 範本寫死全形冒號「：」，英文顯示不自然 | 排程（022c） |
| MD-F11 | Low | 無障礙 | 9 個搜尋框、2 個篩選下拉 | 沒有 `aria-label` | 排程（WU-MD-027 一併處理） |
| MD-F12 | Low | 數字顯示 | `tax-calculator-dialog.ts` | 金額以 JS 數字原樣顯示，沒有依幣別小數位與千分位 | 排程（022c） |
| MD-F13 | Low | 表單規則 | `item-page.ts` | 停用的單位／品牌／分類選項一律不可選，即使是物料目前的值（後端允許保留未改變的停用值，`partner-list` 已正確處理） | 排程（022c） |

**前端做得好的地方**
- 每個 MD 列表頁與對話框都用 `latestRequest()` 丟棄過期回應；切換公司時關閉對話框、回第 1 頁並重載下拉選項。
- 儲存都有防重複送出；所有按鈕以 `*erpHasPermission` 控制，代碼與後端一致；每個路由都有 `permissionGuard`。
- 比率、係數、金額以正規表示式限制在 15 位有效數字內，JSON 數字可準確往返；單位換算在伺服器計算。
- 圖片對話框檢查類型、大小與數量；預覽以 MIME 檢查過的 `data:` URL 顯示；沒有 `innerHTML` 或 `bypassSecurityTrust`。
- `i18n-keys.spec.ts` 保證三語 key 一致且非空；樣式只用 `--erp-*` 變數，暗色模式安全。
- 以設定重用頁面：`PartnerList` 服務客戶與供應商，`CategoryList` 服務四種簡單分類。
