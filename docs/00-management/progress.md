# 進度總覽

> 每完成一個工作單元更新。完成度 = 已完成工作單元 / 該模組全部工作單元。

| 階段 | 模組 | 狀態 | 完成度 | 工作單元（完成 / 全部） | 測試（通過 / 全部） | Tag | 最後更新 |
|---|---|---|---|---|---|---|---|
| P0 | 基礎 | 完成 | 100% | 13 / 13 | 後端 346 / 346；前端 181 / 181；E2E 16 / 16 | - | 2026-10-10 |
| P0 | SYS | 完成 | 100% | 24 / 24（WU-SYS-010 拆為 010a–d；011 拆為 011a–b；012 拆為 012a–b；016 拆為 016a–d） | 後端 599 / 599；前端 432 / 432；E2E 65 / 65（模擬，3 略過）、真實 API 3 / 3；docgen 0 Error | v0.1.0-sys | 2026-10-11 |
| P1 | MD | 未開始 | 0% | 0 / 22 | - | - | 2026-10-11 |
| P1 | INV | 未開始 | 0% | 待拆分 | - | - | - |
| P1 | PUR | 未開始 | 0% | 待拆分 | - | - | - |
| P1 | SD | 未開始 | 0% | 待拆分 | - | - | - |
| P2 | FI | 未開始 | 0% | 待拆分 | - | - | - |
| P2 | CO | 未開始 | 0% | 待拆分 | - | - | - |
| P2 | BI | 未開始 | 0% | 待拆分 | - | - | - |
| P3 | PP | 未開始 | 0% | 待拆分 | - | - | - |
| P3 | QM | 未開始 | 0% | 待拆分 | - | - | - |
| P3 | PM | 未開始 | 0% | 待拆分 | - | - | - |
| P4 | CRM | 未開始 | 0% | 待拆分 | - | - | - |
| P4 | HR | 未開始 | 0% | 待拆分 | - | - | - |
| P4 | PS | 未開始 | 0% | 待拆分 | - | - | - |
| P4 | SCM | 未開始 | 0% | 待拆分 | - | - | - |

## 進行中

- 2026-10-11 WU-SYS-006 第一段（已於第二段完成，見「最近完成」）（IP 白名單後端）：接手上一輪 13 個未提交變更，修正 CIDR 主機位元驗證、匿名角色判斷，以及無 SYS 模組時健康檢查 500；新增 `sys_ip_whitelist`（REQ_DS43.102）與 `System_IpWhitelist` migration，已套用 `myerp`。全系統／角色／用戶層級、IPv4／IPv6 與 CIDR、有效期限、總開關（Development 關閉）、loopback 例外；登入與刷新在發 Token 前按用戶角色再次檢查；拒絕 403、Warning 與 `IP_DENIED` 稽核。管理 API `/api/v1/sys/ip-whitelist` 有權限、版本、停用及三個篩選唯一索引。後端 519 / 519（新增 28 個單元案例與 3 個整合測試）、前端 389 / 389；docgen 0 Error／41 Warning（含尚無介面的六個新需求）。（ADR-025）。

- 2026-10-11 WU-SYS-009 第一段（已於第二段完成，見「最近完成」）（審批流程引擎後端）：接手上一輪 codex 未提交的審批變更，驗證後補 ADR-022 並提交。新增 `sys_approval_flow`、`sys_approval_flow_step`、`sys_approval_delegation`、`sys_approval_instance`、`sys_approval_instance_step`（REQ_DS43.97–101），migration `System_Approval` 已套用 `myerp`。共用介面 `MyERP.Shared.Approvals`：各模組以 `IApprovalWorkflowService.SubmitAsync／CancelAsync／GetStateAsync` 送審，實作 `IApprovalResultHandler` 接收最終結果（同一交易內、儲存前呼叫，失敗整體回滾）；SYS 只存單據 Id，不跨模組讀表。流程每公司每單據類型一條、1–10 級、角色或指定用戶、金額門檻；送審時複製適用步驟快照，沒有適用步驟即自動核准。篩選唯一索引保證同一單據只有一個審批中實例；核准帶 version；不可自審（流程可允許）；代理按業務日期生效、記錄實際處理人與被代理人、不連鎖、期間不重疊。審批動作以 APPROVE／新增的 REJECT 寫稽核。API：`/api/v1/sys/approval-flows`、`approval-delegations`、`approvals`（待辦、歷史、明細、approve、reject），新增 8 個權限代碼。後端 472 / 472（新增 10 個整合測試，SYS-APR-001–011），建置 0 警告。前端頁面（介面 8.68–8.70）待第二段。

- 2026-10-11 WU-SYS-008 第一段（已於第二段完成，見「最近完成」）（單號規則後端）：新增 `sys_number_rule`（REQ_DS43.95）與 `sys_number_sequence`（REQ_DS43.96），migration `System_NumberRule` 已套用 `myerp`。共用介面 `MyERP.Shared.Numbering.INumberGenerator`，SYS 實作 `NumberGenerator` 以單一參數化 `MERGE … WITH (HOLDLOCK)` 原子遞增計數，與呼叫端同一交易：交易回滾即退回號碼，不跳號。單號 = 前綴 + 單據日期格式 + 補零流水號，按公司、規則、重設週期（不重設／每年／每月／每日，依單據日期）計數；重設週期必須包含在日期格式內；已發出單號的規則鎖定日期格式與週期、流水號不可縮短。管理 API `/api/v1/sys/number-rules`（列表、單筆、預覽、建立、修改；`SYS.NUMBER_RULE.VIEW/CREATE/EDIT`）。新增 9 個整合測試（SYS-NUM-001～009，含 16 個 DbContext 並行取號 100 次無重號無跳號、回滾退號、四種週期、公司隔離、用盡），後端 462 / 462、建置 0 警告、docgen 0 Error。首次整體執行時 7 個 Auth 類測試出現 SQL 逾時（sys_user 查詢等待 35 秒），之後整合測試與整體各重跑一次全部通過，沒有找到持鎖的工作階段，暫列為偶發。決策見 ADR-021（REQ_F4118.28.1–5）。三語規則維護頁（介面 8.67，REQ_F4118.28.6）留待第二段。

- 2026-10-11 WU-SYS-007 第一段（已於第二段完成，見「最近完成」）（稽核日誌後端）：接手上一輪 codex 未提交的稽核變更，並修正一個缺陷：樂觀鎖衝突後重試會寫出重複的稽核記錄。原因是 EF 遇到衝突時不呼叫 `SaveChangesFailed`；現在 interceptor 同時覆寫 `ThrowingConcurrencyException` 做清理。完成內容：新增 `sys_audit_log`（REQ_DS43.94，只增不改的 trigger 與 ISJSON 約束，migration `System_AuditLog`）；`AuditTrailInterceptor` 與業務資料同交易寫入欄位差異，`[SensitiveData]` 欄位遮蔽為 `***`；登入、登入失敗、登出、萬用碼登入與權限變更都寫安全事件；新增 `GET /api/v1/sys/audit-logs`（`SYS.AUDIT.VIEW`，按公司隔離）。新增 13 個整合測試（SYS-AUDIT-001～013），後端 453 / 453、建置 0 警告；docgen 0 Error。決策見 ADR-019。前端稽核頁（介面 8.66）留待第二段。

- 2026-10-11 WU-SYS-002 第二段（已於第三段完成，見「最近完成」）：測試先行開放用戶管理 API `/api/v1/sys/users`（GET 分頁＋關鍵字＋帳號排序、POST 建立、PUT 啟用／停用帶 version、POST `{id}/reset-password`、POST `{id}/unlock`），新增 `SYS.USER.RESET_PASSWORD`。範圍為目前公司有用戶公司關聯的用戶，範圍外 404；建立時同交易加入目前公司（預設、無角色）；重複帳號（含他公司）409；不可停用自己 422；DTO 只回 `isLocked`／`isPasswordExpired`（ADR-016，REQ_F4118.23.5–8）。新增 6 個整合測試（SYS-USER-010～015），後端 420 / 420、建置 0 警告；前端與 E2E 未改動。既有測試 `UnsafeSecurityConfigurationRefusesStartup` 首次執行出現 ObjectDisposedException，重跑通過，疑為偶發。三語用戶頁及 E2E 待下一段。
- 2026-10-10 WU-SYS-003 第一段（已於第二段完成，見「最近完成」）：測試先行完成後端登入。`/api/v1/sys/auth` 提供 captcha（SVG、答案只存 Redis、一次即作廢）、login、refresh、logout、me。Access Token 15 分鐘 HS256，`sub` 為用戶 Id；Refresh Token 7 天，Redis 只存雜湊，以 Lua 原子取出實現輪替，5 個並發重放只有 1 個成功；登出只撤銷本人 Token。帳密錯誤、帳號不存在及鎖定回同一 401；密碼到期回 403 不發 Token。萬用碼只在 Development 有效並寫 Warning；非 Development 設定萬用碼或缺 JWT 金鑰即拒絕啟動。`/health/ready` 加 Redis 檢查；共用核心加 401 `UnauthenticatedException`。新增 13 個整合測試，後端 381 / 381、建置 0 警告；前端與 E2E 本輪未改動。決策見 ADR-012。前端登入頁、Captcha 元件、Token 刷新與登入 E2E 待續。
- 2026-10-10 WU-SYS-002 第一段（已於第三段完成，見「最近完成」）：接手上一輪未提交變更並驗證。新增 `sys_user`（REQ_DS43.82，`System_User` migration 已套用 `myerp`）、ASP.NET Core Identity `PasswordHasher`、可由 `Security:PasswordPolicy` 設定的密碼政策（不合法設定啟動即拒絕）、連續 5 次失敗鎖 15 分鐘（以 `ExecuteUpdate` 原子累計，並發不遺失）、鎖定到期重開計數、密碼到期判斷，以及帳號大小寫及刪除後仍唯一。補啟動設定驗證測試，用戶整合測試 5 個，後端 366 / 366、建置 0 警告。需求 REQ_F4118.23.1–4 與 SYS 文件已更新。管理 API、三語用戶頁、登入整合及 E2E 要等 WU-SYS-003／004；WU-SYS-002 維持進行中。
- 2026-10-10 WU-SYS-001 第七段：測試先行建立三語公司／分店／部門列表及表單骨架；列表有本地搜尋、代碼排序與分頁，分店／部門依公司 ID 篩選，表單驗證代碼與非空白名稱並輸出大寫代碼草稿。元件不接 API、不加路由或選單；隔離檢查及三語鍵測試通過。前端正式建置、189 / 189、後端 361 / 361、既有 E2E 16 / 16 通過。授權、HTTP API、正式頁面及 E2E 驗收待 WU-SYS-002～004 後續作；WU-SYS-001 維持進行中。下一輪先做 WU-SYS-002 的用戶、密碼政策與鎖定，以解開授權前置作業。
- 2026-10-10 WU-SYS-001 第六段：測試先行補公司列表的授權 ID 篩選、啟用與軟刪除篩選、分頁、關鍵字、排序及空授權邊界；內部服務已實作，不註冊 DI 或公開 API。後端建置 0 警告、361 / 361，前端 181 / 181、既有 E2E 16 / 16 通過；docgen 0 Error / 5 Warning。完整 HTTP 授權、前端組織頁與其 E2E 驗收仍待續，工作單元維持進行中。
- 2026-10-10 WU-SYS-001 第五段：測試先行補分店／部門列表的分頁、關鍵字、排序、公司隔離、軟刪除及無公司拒絕；內部服務已實作白名單排序與無追蹤查詢。後端建置 0 警告、360 / 360 測試通過；前端 181 / 181、E2E 16 / 16；docgen 首次 SQL 逾時，重跑 0 Error / 5 Warning。公開 API、授權、前端組織頁與完整驗收尚待續，工作單元維持進行中。
- 2026-10-10 WU-SYS-001 第四段：新增三個組織整合測試，驗證三表 JSON 約束、三種代碼唯一性、部門公司隔離及軟刪除、停用公司後歷史資料、公司更新審計、部門雙 DbContext 並發。組織測試 13 / 13、後端 359 / 359 通過；既有服務與 migration 無須改動。授權、公開 API、前端與 E2E 尚未完成，工作單元維持進行中。
- 2026-10-10 WU-SYS-001 第三段：測試先行補公司建立／修改／停用、停用部門不可作上級、代碼格式、公司唯一索引與分店 JSON 約束；加入公司內部服務並補部門停用，三種組織代碼統一轉大寫與校驗。組織整合測試 10 / 10、後端 356 / 356 通過，建置 0 警告；docgen 重生 0 Error / 5 Warning。代碼規則與追溯已更新。授權、公開 API、前端、E2E 與完整驗收仍待續，工作單元維持進行中。
- 2026-10-10 WU-SYS-001 第二段：新增未註冊 DI 的 `OrganizationService`，實作分店／部門建立及修改的啟用公司、代碼、參照、上級循環和版本檢查；重複代碼檢查含軟刪除資料，並處理唯一索引衝突。補 5 個整合測試，涵蓋複合 FK 違規寫入、跨公司上級、循環、停用公司、刪除代碼和樂觀鎖版本遞增。後端建置 0 警告，353 / 353 測試通過；docgen 0 Error / 5 Warning。授權完成前服務不開放 API（ADR-011）。公司服務、權限、API、前端與完整驗收待續，工作單元維持進行中。
- 2026-10-10 WU-SYS-001 第一段：新增 SYS 組織需求、資料結構 ER、API／UI 設計、業務規則與測試計畫；`sys_company`、`sys_branch`、`sys_department` 模型與 `System_Organization` migration 已建立並套用至 `myerp`。公司為隔離根，分店／部門以公司篩選、軟刪除篩選和複合外鍵保護跨公司參照。整合測試涵蓋資料表、模型篩選與分店跨公司查詢；後端 348 / 348 通過，docgen 0 Error / 5 Warning。應用服務、權限、API、前端與其完整驗收尚未完成，工作單元維持進行中。

## 最近完成

- 2026-10-11 WU-SYS-016d（SYS 完整回歸與模組收尾）：後端建置 0 警告、599 / 599；前端建置與 432 / 432；模擬 API E2E 65 通過、3 個真實 API 案例依設定略過；真實 API E2E 3 / 3；docgen 連 `myerp` 產生 SRS／SDS／需求表，0 Error／44 Warning（未完成模組的敘述來源與既有追溯缺口）。首次全套後端有 16 個偶發 SQL 逾時，連續三次重跑 599 / 599；排查與後續 WU-P0-014 見 log-analysis。BlueWhale 對照表的 SYS 列改為實際需求、介面與資料表編號；沒有 BLOCKED 項目；EP-MD 拆為 WU-MD-001～022。SYS 已完成，tag `v0.1.0-sys`。
- 2026-10-11 WU-SYS-016c（審查修正：重複代碼、迴圈內查詢與前端細節）：後端新增 `SystemQueries`，集中目前公司檢查（原 4 個服務各一份）、批次查用戶名稱（原 2 份）和代碼／名稱排序（原 6 個 switch）（B10）。審批流程的步驟驗證改為先檢查格式，再用兩次查詢確認角色與用戶，錯誤仍指向第一個無效步驟；部門的上層檢查改為一次載入上層關係，在記憶體檢查循環（B09）。前端新增共用 `displayTime`，時間一律顯示到分鐘，只有稽核紀錄到秒（F06）。通知頁只在公司 ID 改變時重新載入，翻頁和篩選不再重算未讀數（F04）。通知類別、流程步驟摘要和審批人改用三語參數化 key，英文介面不再出現全形標點（F08）。`PagedResult` 只在一處宣告，10 個列表使用共用的每頁筆數選項（F07）。移除 8 個檔案寫死淺色的 CSS 後備值（F09）。角色頁的模組勾選狀態和流程列表的審批人名稱改為 computed（F10）。代理頁的「今天」在每次載入時更新，過了午夜不會過期（F11）。F01 部分修正：重複的分頁和排序邏輯改用共用 helper；不全面改用列表範本，理由見 code-review-log。測試：後端 599 / 599、前端 432 / 432、E2E 65 / 65（3 個真實 API 略過），建置 0 警告。
- 2026-10-11 WU-SYS-016b（審查修正：全系統資源授權與列表競態）：修正 SYS-B03。角色與 IP 白名單是全系統資源，之前任何公司的管理員都能修改；有 `SYS.ROLE.EDIT` 的非全權用戶也能替自己的角色加權限；新增系統層級白名單會鎖住其他公司。現在按影響範圍檢查（ADR-033）：修改角色時，操作者在角色已指派的每間公司都必須全權；白名單規則按層級計算受影響公司（系統＝全部公司、角色＝已指派的公司、用戶＝所屬公司），否則 403 `SYS.AUTHORITY.FULL_ACCESS_REQUIRED`，並加入三語訊息。前端新增共用 `latestRequest()`：新查詢取消舊請求，元件銷毀時也取消，已套用到 8 個列表頁（F02）。部門頁的選項改為只在切換公司、切換頁面類型或儲存後載入（F03）。IP 白名單頁補上防重複停用、清除操作錯誤、錯誤時總數歸零，並把擠在一行的語句拆開（F05、F12）。測試：後端 595 / 595、前端 430 / 430、E2E 65 / 65（3 個真實 API 略過），建置 0 警告。B09、B10、F01、F04、F06–F11 移到 WU-SYS-016c，完整回歸與 tag 改為 016d。

- 2026-10-11 WU-SYS-016a（SYS 模組代碼審查與後端安全修正）：後端與前端各由唯讀子代理審查，主代理逐項核對後寫入 `code-review-log.md`（後端 13 項、前端 12 項）；日誌分析寫入 `log-analysis.md`，沒有非預期的重複錯誤，也沒有慢請求。新增 `AuthorityGuard`（ADR-032），修正三個問題：跨公司動作只檢查目前公司權限（B01）、可指派全權角色或改自己角色（B02）、可接管同屬他公司的用戶（B04）。另修正角色建立未包交易（B05）、停用／鎖定帳號的時間差（B07）、通知清理首次延遲（B08）、改密碼失敗沒有安全稽核（B12）、收件人無效回 403（B13，改 422）；B06、B11 有理據不修改。新增 SYS-PERM-028～030。前端加入兩個新錯誤代碼與三語訊息。測試：後端 593 / 593、前端 426 / 426、E2E 65 / 65（模擬，3 個真實 API 略過）。016 已拆為 016a–c：016b 處理其餘發現，016c 做完整回歸與 tag。

- 2026-10-11 WU-SYS-012b 站內通知收件箱完成（REQ_F4118.33.2–3、介面 8.76）：接手上一輪 codex 未提交的 34 個變更並驗證。`/api/v1/sys/notifications` 提供分頁列表（新到舊、可篩未讀）、未讀數、單筆與全部已讀（`SYS.NOTIFICATION.VIEW／READ`），只處理本人、目前公司、未到期通知；他人／別公司 404 `SYS.NOTIFICATION.NOT_FOUND`。`ExpiredNotificationCleanup` 每日跨公司刪除過期通知。前端頂列鈴鐺徽章（超過 99 顯示 99+、只採最後一次未讀數回應）與三語收件箱 `/sys/notifications`。本輪補上：示範種子各業務角色加入本人通知權限（否則非管理員看不到）、HTTP 整合測試 SYS-NOTIFY-006、前端單元 SYS-NOTIFY-007（6 個）；修正審批代理 E2E 連選同名用戶時的下拉選單 strict mode 偶發失敗。後端 590 / 590、前端 426 / 426、E2E 65 / 65（另 3 個真實 API 案例依設定略過）、docgen 0 Error／44 Warning。

- 2026-10-11 WU-SYS-012a 站內通知發布核心完成（REQ_F4118.33.1、REQ_DS43.104、ADR-031）：新增 `sys_notification` 與 `System_Notification` migration（已套用 `myerp`），跨模組使用 `INotificationPublisher`；只向目前啟用公司中的啟用用戶發布，文字與站內路徑驗證，同公司、收件人、來源鍵唯一，並發重試讀回同一 Id。初始未讀、90 天到期，審計與版本欄位由共用核心管理。SYS-NOTIFY-001～003 三個整合測試通過；後端 588 / 588（整合 188、其他 400，分批），前端 420 / 420，E2E 64 / 64（另 3 個真實 API 案例略過），docgen 0 Error／46 Warning（含收件箱 012b 尚無介面的 3 項）。下一輪完成 012b 收件箱 API、已讀與三語頁面。

- 2026-10-11 WU-SYS-011b 首頁、修改密碼及個人設定介面完成（REQ_F4111.1.2 v1.1、.4–5；ADR-030，介面 1.2–1.4）。首頁顯示登入帳號、目前公司與自助入口，現有圖表仍標明示範。登入遇 `PASSWORD_EXPIRED` 顯示匿名修改入口，帳密與一次性 Captcha 修改成功即保存新 Session；本人修改成功替換 Access／Refresh Token，失敗清空密碼、到期流程重新取 Captcha。個人設定頁保存三語與亮／暗／跟隨系統主題於此瀏覽器，重整後沿用；不提供跨裝置同步。前端 420 / 420（新增 4 個）、E2E 64 / 64（新增 3 個；另 3 個真實 API 案例依設定略過）、後端建置 0 警告。全套後端首輪 569 / 585，16 個分散整合測試遇 SQL 逾時；單線程重跑整合測試 185 / 185，全套單元 400 / 400 均通過。docgen 0 Error／43 Warning（新首頁、偏好沒有資料表，另有舊追溯缺口）；輸出已重生。下一輪 WU-SYS-012 站內通知。

- 2026-10-11 WU-SYS-011a 修改密碼後端完成（REQ_F4111.1.2，ADR-029）：本人以舊密碼修改，密碼到期時以帳密與 Captcha 修改；沿用密碼政策、五次錯誤鎖定與 IP 白名單。修改與安全稽核同次儲存，之前簽發的 Refresh Token 以 `password_changed_at` 判斷撤銷，回傳目前工作階段新 Token。新增 SYS-AUTH-018～022 五個整合測試，針對測試 5 / 5、建置 0 警告。全套後端首輪架構清單漏列新自助端點（已修正），第二輪並行測試觸發 SQL 逾時／連線池耗盡；限制模組並行後 585 / 585 通過。前端 416 / 416；模擬 API E2E 60 / 61，既有公司圖片案例單獨重跑通過；docgen 0 Error／42 Warning（修改密碼介面待 011b）。本輪沒有前端程式變更。011b 待續。

（由 AI 追加，最新在上）

- 2026-10-11 WU-SYS-010d Logo 與印章完成（ADR-028，REQ_F4118.19.1–3，介面 8.57；WU-SYS-010 整體完成）：新增 `sys_company_image`（REQ_DS43.103，migration `System_CompanyImage` 已套用 `myerp`），每間公司 Logo、印章各一張，內容存資料庫 `varbinary(max)`，有類型、尺寸、大小與 DATALENGTH 檢查約束。上傳時以檔案內容判斷類型：只接受 PNG（IHDR／IEND）與 JPEG（SOI／SOF／EOI），不看副檔名或宣告的 Content-Type，SVG 等一律拒絕；1 MB、寬高 16–2048 像素；檔名只保留檔名部分。API `/api/v1/sys/company-images`：清單（不含內容）、`{kind}/content`（inline、nosniff、ETag 版本號，`If-None-Match` 只查版本即回 304）、multipart 上傳／替換與刪除都帶 version（409），422 專用錯誤代碼；權限沿用 `SYS.PARAMETER.VIEW／EDIT`；稽核只記錄位元組數。Contracts 新增 `ICompanyImageReader`，供列印模組讀取。三語 `/sys/logo-stamp`（選單「Logo 與印章」）：兩張卡片預覽（Blob 轉 data URL，前端 CSP 不變）、檔案資料、上傳／替換、確認刪除、用戶端先檢查類型與大小、唯讀。後端 580 / 580（新增 13 個整合測試案例 SYS-IMG-001～006）、前端 416 / 416（新增 10 個 SYS-IMG-007）、E2E 61 / 61（新增 SYS-IMG-008）；docgen 0 Error／42 Warning（未提交無 DB 的輸出）。偶發：後端與前端測試同時執行時，7 個整合測試 SQL 逾時，前端稽核頁測試逾時 15 秒；分開執行全部通過。E2E 全量執行時 approvals.spec 的審批代理測試一次失敗（下拉選單關閉動畫未完成，出現兩個相同選項），單獨重跑通過，與本次變更無關。

- 2026-10-11 WU-SYS-010c 列印設定完成（ADR-027，REQ_F4118.18.1–3，介面 8.56）：列印設定沿用系統參數，不開專表。SYS 在 `print` 分組宣告 9 個參數：列印公司名稱（100）、地址（200）、郵遞區號（10）、電話／傳真（30）、採購單／銷售出貨單頁尾備註（500，可多行）、是否列印 Logo／印章（預設否）；保存、版本、批次驗證與稽核沿用 `/api/v1/sys/parameters`，權限沿用 `SYS.PARAMETER.VIEW／EDIT`，沒有 migration。`MyERP.Modules.System.Contracts` 新增 `IPrintSettingsReader`／`PrintSettings`，讓 PUR／SD 列印時讀取，公司名稱空白時用公司主檔名稱。三語 `/sys/print-settings`（選單「列印設定」）重用 `ParameterSettings`（路由資料 `view: 'print'`），只顯示列印分組並依草稿即時預覽抬頭；`/sys/parameters` 不再顯示列印分組；長度超過 100 的文字參數改用多行輸入框。後端 567 / 567（新增 3 個整合測試 SYS-PRINT-001～003）、前端 406 / 406（新增 4 個）、E2E 60 / 60（新增 SYS-PRINT-004）；docgen 0 Error／41 Warning（未提交無 DB 的輸出）。

- 2026-10-11 WU-SYS-010b 系統參數頁完成（介面 8.55，REQ_F4118.17.4）：`/sys/parameters`（`permissionGuard('SYS.PARAMETER.VIEW')`、`unsavedChangesGuard`、系統管理選單）。參數依模組、分組以卡片顯示，模組／分組／參數名稱三語（`sys.parameters.keys.<參數鍵>`）。依型別產生欄位：布林開關、整數／十進位文字輸入（數字不經 float 保存，範圍只作提示）、文字（長度上限）、選項下拉；顯示預設值、範圍與「恢復預設」。只送出有修改的項目並帶各自 `version`，用戶端先檢查型別與範圍；伺服器 400 依 `items[i]` 標示欄位，409 提示並可重新載入。沒有 EDIT 權限時唯讀；切換公司重新查詢並丟棄草稿；有未保存修改時離開需確認。檢查與優化：只差前後空白不算修改、文字長度改用去空白後計算（與伺服器一致）。前端 402 / 402（新增 10）、E2E 59 / 59（新增 SYS-PARAM-005/006）、後端 564 / 564 未變；docgen 無 DB 0 Error（未提交降級輸出）。

- 2026-10-11 WU-SYS-010a 系統參數後端完成（ADR-026，REQ_F4118.17.1–3，REQ_DS43.73）。
  - 拆分：WU-SYS-010（系統參數、列印設定、Logo 與印章）拆為 010a 參數後端、010b 參數頁、010c 列印設定、010d Logo 與印章。上幾輪 codex 異常結束是因為額度用完（runner 輸出 `out of credits`），不是工作單元本身失敗，所以不計入失敗次數。
  - 參照 BlueWhale `SystemSet`：舊表以固定欄位混放列印抬頭、審批開關、負庫存開關、自訂欄位名稱與第三方金鑰，沒有範圍與權限檢查。MyERP 改為各模組以 `IParameterDefinitionProvider` 宣告有型別的參數（Boolean／WholeNumber／Number／Text／Option，預設值與範圍），SYS 以 `ParameterCatalog` 彙整，啟動時檢查鍵格式、模組前綴與重複。
  - `sys_parameter`（migration `System_Parameter`，已套用 `myerp`）按公司保存，只存修改過的值，`(company_id, param_key)` 唯一。API：`GET /api/v1/sys/parameters`（`SYS.PARAMETER.VIEW`）、`PUT` 批次保存（`SYS.PARAMETER.EDIT`，同一交易，錯誤一次列出 `items[i].*`，版本 409，未改變的值不寫入）。各模組在進程內用 `ISystemParameters` 讀取，同一請求只查一次；舊值不合目前規則時用預設值。SYS 宣告數量小數位數（0–4，預設 2）與單價小數位數（0–6，預設 4）；負庫存、自訂欄位名稱等由 INV／MD 開發時自行宣告。
  - 測試：後端 564 / 564（新增 42：參數宣告單元 37 個案例、目錄 2、API／讀取整合 3），建置 0 警告；前端與 E2E 沒有改動。docgen 無資料庫連線 0 Error／42 Warning（新增 3 個 TRACE_GAP，因為參數頁介面要到 010b 才做），沒有提交降級的產物。

- 2026-10-11 WU-SYS-006 IP 白名單完成（第二段，ADR-025，介面 8.75，REQ_IO_A8.75.1–7）。三語 `/sys/ip-whitelist` 用 `erp-list-page` 與 `table-query` 做伺服器端列表、搜尋、排序、分頁；依權限建立／編輯（層級、對象與網段建立後唯讀）、停用前用共用確認框，錯誤代碼與登入 IP 拒絕有三語提示。整合測試以真實登入與 Refresh 驗證 admin 角色在非允許 IP 無法取得 Token，且只有設定中的可信代理能提供來源 IP。檢查快取發現載入與失效併發時可回傳舊快照，已同步發布並在世代變更時重載；測試涵蓋立即失效、30 秒到期與載入中失效。後端 522 / 522；前端 392 / 392；模擬 API E2E 57 / 57（另 3 個真實 API 測試在一般執行時略過）。docgen 無資料庫連線 0 Error／39 Warning；連 myerp 多次遇 SQL Server 逾時，myerp_test 有兩張測試專用表造成 2 Error，故未提交降級產物。資料表已於第一段套用 migration、後端整合測試通過。下一個工作單元 WU-SYS-010。

- 2026-10-11 WU-SYS-014 共用介面完成（ADR-024，REQ_F4118.31.1–6，介面 8.71–8.74）。
  - `ErpDialogService`（`shared/dialogs/`）：確認框 `confirm()`（Esc／關閉視為取消，危險動作紅色按鈕）、提示框 `alert()`、成功提示 `success()`（3 秒）、選擇視窗 `select()`（關鍵字、伺服器端分頁、單選按兩下確定、多選跨頁保留、只採用最後一次查詢）。內容元件自帶按鈕與 `data-testid`；標題放在內容內，切換語言即時更新。服務自建 NzModalService，頁面不必另外提供。
  - 範本：`erp-list-page`（標題／動作、篩選＋查詢／重設、批次操作列與已選筆數、錯誤重試）與 `table-query.ts`；`erp-document-page`（返回、單號／新增、狀態、未儲存、上一筆／下一筆、409 重新載入、422 錯誤、頁籤插槽）、`adjacentIds()`、`unsavedChangesGuard`；`erp-date-range`（今天、本週、本月、上月、本季、本年）；`erp-status-tag`（七種單據狀態，亮／暗 token）。
  - 角色頁改用列表範本、`table-query` 與共用確認框，取代 popconfirm；既有測試全數保留，E2E 改為操作確認框並加「取消不送出」。範本輸出原名 search／reset 會收到投影輸入框冒泡的原生 search 事件，已改名（有測試）。jsdom 不發 animationend，nz-modal 關閉不會完成，測試提供 NoopAnimations。
  - 測試：前端 389 / 389（新增 20）、E2E 56 passed + 3 skipped、後端 488 / 488，建置 0 警告；docgen 0 Error（新增 6 個 TRACE_GAP Warning，因為共用介面沒有資料表）。前端覆蓋率：dialogs 97%、list-page 100%、document-page 96%、date-range 98.5%。
  - 未做：其他 SYS 列表頁之後修改時再遷移；選擇視窗與單據頁範本待第一個主檔／單據頁採用時補 E2E。

- 2026-10-11 WU-SYS-013 種子資料完成（ADR-023，REQ_F4118.30.1–3）。
  - 執行方式：`dotnet run --project src/backend/MyERP.Api -- --seed`，載入後即結束。共用 `IDataSeeder`／`DataSeedRunner` 依 Order 執行各模組種子；非 Development 環境寫 Error 並拒絕。不在啟動時自動執行，整合測試不受影響。
  - SYS 種子（同一交易、冪等）：TW01（TPE／TXG／KHH）、HK01（HKG／KLN），每間公司 7 個部門。9 個角色：ADMIN 全權，其他依規則對照權限目錄展開（例如 FINANCE = FI、CO 全部 + 全部 VIEW），新模組的權限在重新執行時補上。帳號來自 `appsettings.Development.json` 的 `SeedUsers`（MASTER_PROMPT 第 12 節），密碼雜湊；示範密碼刻意不套用密碼政策。每個帳號可存取兩間公司，預設 TW01。第二次執行新增 0 筆，不覆蓋改過的密碼，也不移除額外加入的權限。本機 `myerp` 已載入 117 筆。
  - 真實 API E2E（SYS-AUTH-017）：`serve-dist.mjs` 在設定 `E2E_API_URL` 時把 `/api/` 轉送到後端（同源，CSP 不變）；`tools/scripts/Invoke-RealApiE2E.ps1` 依序 migration → 種子 → 啟動 API → 執行 `specs/real-api-*`。3 個測試：admin 登入並在兩間公司看到各自分店、登出；viewer 沒有新增按鈕；不存在的帳號顯示錯誤。
  - 真實 API E2E 發現缺陷：在列表頁登出時，先清除公司才導向登入頁，列表頁因此用空 Session 重新查詢而得到 401。已改為先導向再清除（新增前端單元測試）。
  - 測試：後端 488 / 488（新增整合 7、單元 9）、前端 369 / 369（新增 1）、E2E 56 passed + 3 skipped（真實 API 測試在一般執行時略過），真實 API E2E 3 / 3 連續兩次通過；建置 0 警告；docgen 0 Error（新增 3 個 TRACE_GAP Warning，原因是種子沒有介面）。前端整體執行時 roles.spec 的第一個測試偶發逾時（16.7 秒，單獨重跑與整體重跑都通過），屬 ADR-020 的冷啟動問題。
  - 未做：`sys_company` 沒有本位幣欄位，TWD／HKD 待 MD／FI 建立；示範審批流程與單號規則要有單據類型，留給各業務模組的種子。

- 2026-10-11 WU-SYS-009 審批流程引擎完成（第二段：前端）：新增 `ApprovalService` 與三語頁面。`/sys/approval-flows`（介面 8.68）列出各流程的步驟、審批人與金額門檻；對話框可增刪、排序 1–10 個步驟，每步選角色或用戶審批人並可設門檻（前端限 11 位整數、4 位小數，避免 JSON 數值失真），只送出所選類型的審批人。`/sys/approval-delegations`（介面 8.69）可依用戶與有效日期篩選並標示「生效中」，新增時檢查不可同一人、日期不可倒置。`/sys/approvals`（介面 8.70）分「我的待辦」（PROCESS）與「審批歷史」（VIEW）兩個頁籤：待辦顯示第幾步與「代 某人」，核准／駁回帶版本，駁回必填意見；明細列出各步驟的處理人、被代理人、時間與意見。選單項目改為可接受多個權限（任一即顯示）。後端待辦加上 `onBehalfOfUsername`。稽核頁加入 REJECT 標籤。E2E 發現明細對話框標題缺少單號（nz-modal 開啟後不更新標題），已修正。後端 472 / 472、前端 368 / 368（新增 19）、E2E 56 / 56（新增 6），建置 0 警告，docgen 0 Error。

- 2026-10-11 WU-SYS-008 單號規則完成（第二段：前端）：新增 `NumberRuleService` 與三語 `/sys/number-rules` 頁（介面 8.67，REQ_F4118.28.6），以 `permissionGuard('SYS.NUMBER_RULE.VIEW')` 保護，系統管理選單加「單號規則」。nz-table 伺服器端分頁、單據類型／名稱排序與搜尋；列出後端樣式（今天第 1 號）、日期格式、重設週期、啟用與「已發號」標籤。新增／編輯對話框：單據類型編輯時唯讀；前端即時組合樣式（`formatNumber`，六種日期格式與後端一致）；重設週期不在日期格式內時即時提示且不可保存；已發號的規則鎖定日期格式與重設週期、流水號長度下限改為原長度；長度須為整數（檢查與優化時補上，原生數字欄位可輸入小數）。預覽對話框依業務日期呼叫 preview API，較早或關閉後才回來的回應不套用。按鈕以 `*erpHasPermission` 控制（預覽只需 VIEW）；重複類型、找不到、已發號、流水號用盡、版本衝突以三語顯示；切換公司後重新查詢並關閉預覽。`isoDate` 移到 `shared/date.ts` 共用。新增前端單元 14 個（SYS-NUM-010）、E2E 5 個（SYS-NUM-011）；後端 462 / 462、前端 349 / 349、E2E 50 / 50，建置 0 警告，docgen 0 Error。WU-SYS-008 完成。

- 2026-10-11 WU-SYS-007 稽核日誌完成（第二段：前端）。
  - 三語 `/sys/audit-logs` 頁（介面 8.66，REQ_F4118.27.6）：`permissionGuard('SYS.AUDIT.VIEW')`，系統管理選單「稽核日誌」。篩選有日期範圍與快選（今天、近 7、30 天；結束日早於開始日時不送出）、類別、操作、資料表、記錄 ID；伺服器端分頁，依時間排序（預設新到舊）。
  - 展開列顯示欄位差異（修改前→修改後，`***` 照原樣，空值顯示「（空）」）或安全事件明細，並顯示 TraceId。操作與類別有三語標籤，未知操作顯示原代碼。
  - 單據修改歷史：`?tableName=&recordId=[&userId=]` 由 `withComponentInputBinding` 帶入。列上的資料表／記錄連結可切換到該記錄的歷史；「重設」會清除網址參數（ADR-020）。
  - 測試基礎設施：前端整體執行時，每次都有一個冷啟動的測試超過 Vitest 預設的 5 秒，同 worker 之後的測試檔連帶失敗。新增 `vitest-base.config.mts`，`testTimeout` 改為 15 秒（ADR-020），斷言不變。
  - 測試：前端 335 / 335（新增 13 個）、E2E 45 / 45（新增 4 個）、後端 453 / 453（未改動）。SYS-AUDIT-014 通過。

- 2026-10-11 WU-SYS-005 安全中介層完成。後端新增 `AddMyErpWebSecurity`，涵蓋以下各項：
  - ForwardedHeaders：只信任 `Security:ForwardedHeaders` 列明的 Proxy；沒有設定時不啟用，避免偽造 X-Forwarded-For。
  - 所有回應加安全標頭（nosniff、DENY、no-referrer、API CSP）；非 Development 啟用 HSTS 與 HTTP 轉 HTTPS。
  - CORS 只允許設定的來源，不允許 Credentials；設定錯誤即拒絕啟動。
  - 限流：login／refresh 每 IP 每端點每分鐘 5 次，captcha 10 次，已登入每用戶 300 次，另預留匯出政策。超過回 429 ProblemDetails（`COMMON.REQUEST.RATE_LIMITED`、Retry-After）。ArchitectureTests 檢查匿名端點都有按 IP 的限流。
  - 前端抽出共用 `apiErrorKey`，429 顯示三語「操作過於頻繁」。E2E 靜態伺服器套用建議的前端 CSP，為此關閉 critical CSS 內嵌（否則 37 個 E2E 失敗）。
  - Token 維持 Bearer + localStorage，不改 HttpOnly Cookie，理由見 ADR-018。
  - 測試：後端 440 / 440（新增 18：整合 16、架構 1、單元 1）、前端 322 / 322、E2E 41 / 41。docgen 0 Error；新增的 7 個 TRACE_GAP Warning 是因為中介層需求沒有資料表與介面。

- 2026-10-11 WU-SYS-015 角色維護頁與用戶權限頁完成。
  - 安全修正（測試先行，ADR-017）：`PUT /api/v1/sys/users/{userId}/companies/{companyId}` 的目標用戶必須屬於目前公司，否則回 404 `SYS.USER.NOT_FOUND`；目標公司必須是操作者獲授權的公司，否則回 404 `SYS.COMPANY.NOT_FOUND`。修正前，公司管理員可以接管他公司的帳號，或在未獲授權的公司授予全權角色。另外，不可停用自己的公司授權（422）。
  - 新 API：`GET /api/v1/sys/users/{userId}/companies`（`SYS.USER.VIEW`，REQ_F4118.25.8），只列出操作者可管理公司的指派，固定 3 次查詢。
  - 角色頁：三語 `/sys/roles`（介面 8.64，系統管理選單「角色」）。伺服器端分頁、代碼／名稱排序與搜尋；顯示資料範圍與全權標籤。新增／編輯對話框可依模組分組勾選權限（模組可全選，部分勾選時呈半選），可設資料範圍、全權與啟用。刪除用 popconfirm 確認並帶版本；角色使用中時顯示 IN_USE。
  - 用戶權限對話框（介面 8.65，BlueWhale 4.4.1.8.26）：從用戶列「權限」按鈕開啟（`SYS.USER.EDIT`）。公司只能選操作者可切換的公司；分店／部門以所選公司的 `X-Company-Id` 查詢，查不到（403）時給提示並可留空。可多選角色，設預設公司與啟用。
  - 測試：後端 422 / 422（新增 2 個整合測試，修正 1 個既有測試的前置資料）、前端 312 / 312（新增 17 個）、E2E 40 / 40（新增 7 個，連續兩次通過），建置 0 警告。

- 2026-10-11 WU-SYS-002 用戶、密碼政策與帳號鎖定完成（第三段：前端）：新增 `UserService` 與三語 `/sys/users` 頁（介面 8.61、8.63；`permissionGuard('SYS.USER.VIEW')`、系統管理選單「用戶」）。nz-table 伺服器端分頁、帳號排序與搜尋；狀態標籤（啟用／停用、已鎖定、密碼已過期）及密碼修改時間（伺服器 UTC+8 時間）。按鈕依權限顯示：新增（CREATE）、停用／啟用與解鎖（EDIT，解鎖只在已鎖定的列出現）、重設密碼（RESET_PASSWORD）。新增與重設密碼共用對話框，有確認密碼；帳號去除前後空白再檢查；API 失敗後清空密碼欄位；列操作進行中不重複送出。重複帳號、密碼不符政策、不可停用自己、版本衝突、找不到用戶等錯誤以三語顯示，成功時顯示提示並重新查詢；切換公司後重新查詢。補上 REQ_F4118.23.5–8 的資料結構追溯（docgen 由 23 降回 19 個 Warning）。前端 295 / 295（新增 10 個）、E2E 33 / 33（新增 5 個）、後端 420 / 420，建置 0 警告，docgen 0 Error。test-plan SYS-USER-006 通過。

- 2026-10-10 WU-SYS-001 公司／分店／部門完成（第八段）：開放組織 API `/api/v1/sys/companies|branches|departments`（GET 分頁、POST、PUT），以 `SYS.COMPANY/BRANCH/DEPARTMENT.VIEW/CREATE/EDIT` 保護，回應 DTO 不外露審計欄位。公司 API 只涵蓋用戶獲授權的公司：修改其他公司回 404；`includeInactive` 可列出停用公司以便重新啟用。建立公司時在同一交易內授權建立者，並沿用其目前公司的角色（ADR-015）。分店／部門沿用 `X-Company-Id` 公司隔離。前端新增 `OrganizationService` 與 `/sys/companies|branches|departments` 頁面（lazy 路由、`permissionGuard`、系統管理選單）：nz-table 伺服器端分頁與排序、關鍵字搜尋、狀態標籤、對話框新增／編輯（編輯時代碼唯讀、可停用），按鈕以 `*erpHasPermission` 控制；重複代碼、版本衝突等錯誤以三語顯示；切換公司後自動重新查詢。骨架列表的本地篩選測試改為伺服器端查詢測試（見 optimization-log）。後端 414 / 414（新增 4 個整合測試）、前端 285 / 285（新增 7 個）、E2E 28 / 28（新增 4 個），建置 0 警告，docgen 0 Error。

- 2026-10-10 WU-SYS-004 角色、權限與資料範圍完成：新增 `sys_role`、`sys_role_permission`、`sys_user_company`、`sys_user_role`（REQ_DS43.90–93，`System_Authorization` migration 已套用 `myerp`）。角色是全系統定義，按公司指派；`sys_user_role` 以複合外鍵指向用戶公司關聯，沒有公司存取權就不能指派角色；每位用戶最多一間預設公司。權限目錄由各模組以 `IPermissionDefinitionProvider` 宣告，啟動時檢查格式、前綴與重複，不另建權限表。後端以 `[RequirePermission("SYS.ROLE.VIEW")]` 動態政策檢查目前公司權限；已登入請求的 `X-Company-Id` 不可存取時，整個請求回 403 `COMMON.COMPANY.ACCESS_DENIED`（修正偽造標頭可繞過公司篩選的問題）；缺權限回 403 `COMMON.PERMISSION.DENIED`。資料範圍取各角色最寬者，全權角色為全公司，缺少分店／部門歸屬時退回本人；Application 層以 `DataScopeFilter` 套用。新增角色 CRUD、權限目錄、用戶公司授權、本人授權（`/api/v1/sys/auth/authorization`）API；ArchitectureTests 檢查每個 action 都有權限代碼，且代碼已宣告。前端新增 PermissionService（進入主框架前載入權限與公司清單，記住的公司已不可存取時改用預設公司，切換公司重新載入）、`*erpHasPermission`、`permissionGuard`，選單依權限過濾，登出時清除。後端 410 / 410（新增 29）、前端 278 / 278（新增 13）、E2E 24 / 24（新增 3），建置 0 警告；docgen 0 Error（順便修正 ui-spec 介面表頭與測試計畫範圍式引用造成的 13 個舊 Error）。決策見 ADR-014。角色與用戶權限頁面拆至 WU-SYS-015。

- 2026-10-10 WU-SYS-003 登入完成（第二段：前端）：測試先行新增 `AuthService`，Token 與帳號存 `localStorage`（`myerp.auth.session`），壞資料或 Refresh Token 過期即清除。攔截器只為非匿名 API 加 Bearer；收到 401 時刷新一次再重送，並發的 401 共用同一個 refresh（後端輪替後舊 Token 立即失效）。刷新失敗會清除 Session，導向 `/login?returnUrl=…`。`authGuard`／`guestGuard` 保護主框架；`safeReturnUrl` 擋外部、`//`、`/\` 路徑。共用 `erp-captcha`（ControlValueAccessor）只收 4–6 位數字、點擊換圖、載入失敗可重試。三語登入頁依 `errorCode` 顯示錯誤，失敗後清空密碼並換驗證碼，頁底可切換語言。頂列顯示帳號和登出按鈕：登出時撤銷 Refresh Token，伺服器失敗也清除本機 Session。E2E 以 fixture 寫入假 Session，讓既有測試維持已登入；另以 `page.route` 模擬 auth API，新增 5 個登入 E2E（萬用碼登入、重新整理仍保持登入、登出、密碼／驗證碼錯誤、必填、returnUrl、三語）。前端 265 / 265（新增 76 個）、E2E 21 / 21、後端 381 / 381，建置 0 警告。ADR-001 補記本機 Redis 為 Windows 移植版 3.2.100（新增 R-018）。決策見 ADR-013。萬用碼稽核移到 WU-SYS-007，登入限流移到 WU-SYS-005，連真實 API 的 E2E 移到 WU-SYS-013。

- 2026-10-10 WU-P0-013 文件產生器骨架完成：`tools/docgen` 使用 .NET 10 + OpenXML，從模組 Markdown、非功能需求、BlueWhale 對照表讀取來源，產出 SRS / SDS docx、九張工作表的需求 xlsx，以及一致性報告；可選 SQL Server 連線，以 `INFORMATION_SCHEMA` 產 SDS 4.1 欄位表並檢查資料表一致性。文件使用 `reference-notes.md` 的 BlueWhale 字型、欄寬、框線及章節順序；未完成敘述來源以 `[Pending]` 和警告標示。這次實際產出在 `docs/99-output/`，資料庫可連線，報告 0 Error / 5 Warning（後續內容待補）。新增 2 個 docgen 測試並納入 CI；後端 346 個測試通過。使用說明見 `tools/docgen/README.md`，決策見 ADR-010。

- 2026-10-10 WU-P0-012 Playwright 完成：`tests/e2e` 有獨立的 package.json，使用 @playwright/test 1.64.0，瀏覽器只裝 chromium，下載到 `.tools/ms-playwright`（由 `Invoke-Frontend.ps1` 設定 `PLAYWRIGHT_BROWSERS_PATH`）。webServer 先做前端 production 建置，再以 `serve-dist.mjs` 提供靜態檔：缺少的資源回 404，不以 index.html 掩蓋；路徑跳出根目錄時拒絕。共 16 個冒煙測試：首頁三張 G2 圖表加 KPI 迷你趨勢圖都以像素確認確實畫出；切換暗色時 theme-dark.css 回 200、CSSOM 有規則，body 色彩符合設計 token，重新整理後保留，切回亮色時移除；跟隨系統；切換三語時 `<html lang>` 與文字同步，並依瀏覽器語言決定預設語言；404 / 403 頁；靜態伺服器行為。自動 fixture 遇到任何 console error 即判定失敗。E2E 第一次執行就發現暗色模式下 body 背景被執行期插入的 ng-zorro 暗色 bundle 蓋成 `#000`，已改用 `html body` 提高權重修正。E2E 不加入 CI。後端 344、前端 181、E2E 16 個測試全過。說明見 e2e.md、ADR-009。

- 2026-10-10 WU-P0-011 CI 完成：新增 `.github/workflows/ci.yml`，有兩個 job，都跑在 `windows-latest`，權限只有 `contents: read`，不用任何機密。backend job 用 `global.json` 的 SDK 以 Release 建置，再逐一以 `dotnet test --project` 跑 `tests/backend.unit` 下的單元測試專案；不跑整合測試，因為需要 SQL Server。frontend job 用 `.nvmrc` 的 Node，執行 `npm ci`、`ng build`、`ng test --watch=false`（jsdom，不需瀏覽器）。新增 `CiWorkflowTests`（9 個測試），在本機檢查 workflow 結構：每個單元測試專案都要列入 CI、不得引用整合測試、沒有機密等。本機已用 Release 跑過 CI 的全部步驟，都通過。後端 344 個、前端 181 個測試全過。說明見 ci.md。

- 2026-10-10 WU-P0-009 應用程式日誌完成：`MyERP.Shared.Infrastructure/Logging` 以 Serilog 取代預設日誌提供者（`AddMyErpLogging` / `UseMyErpRequestLogging`）。日誌寫入滾動檔案 `logs/app-yyyyMMdd.log`，格式為 CLEF JSON，每天換檔，單檔上限 100 MB，保留 30 天。設定 `ApplicationInsights:ConnectionString` 或環境變數 `APPLICATIONINSIGHTS_CONNECTION_STRING` 後才啟用 Application Insights。實測 sink 5.0.1 相依 Microsoft.ApplicationInsights 2.23，和 AspNetCore SDK 3.2 衝突，所以只用 sink（ADR-008）。`ErpLogEnricher` 在每筆日誌加上 TraceId（和 ProblemDetails 的 traceId 對得上）、UserId、CompanyId，以及由路由前綴或命名空間判斷的 Module。請求耗時超過 `App:SlowRequestMilliseconds`（預設 1000）寫 Warning，其餘寫 Debug；請求路徑不含查詢字串。設定錯誤時啟動即失敗。appsettings 改用 `Serilog:MinimumLevel`。新增 53 個測試（單元 + WebApplicationFactory 整合，含檔案輸出），後端 335 測試全過；Infrastructure 行覆蓋率 84.8%。設計見 logging.md。

- 2026-10-10 WU-P0-010 前端骨架完成：`src/frontend` 為 Angular 22.2.2（standalone、zoneless、strict）+ ng-zorro-antd 22.1.1 + G2 5.4.8 + @ngx-translate 18。本機 Node 22 不符要求，改用 repo 內可攜版 Node 24.21.0，所有前端指令經 `tools/scripts/Invoke-Frontend.ps1` 執行（下載後驗證 SHA256，R-016 已緩解）。三語 i18n 按範圍分檔，模組語言檔以 `i18nScopeGuard` 延遲載入，`i18n-keys.spec.ts` 檢查三語 key 一致。主題有亮 / 暗 / 跟隨系統三種模式：用 less 編譯兩份 ng-zorro bundle 並換上馬卡龍主色，暗色 bundle 延遲載入；色值的 WCAG 對比由測試把關。Layout 有可收合側欄（平板寬度自動收合）與頂列（公司切換、語言、主題）；API 攔截器加上 Accept-Language 與 X-Company-Id。共用圖表元件有 erp-line-chart、erp-column-chart、erp-pie-chart（超過 7 類時合併「其他」）、erp-kpi-card（環比 + 迷你趨勢），主題或語言改變時重新渲染。另有首頁示範頁與 403 / 404 頁。前端 181 測試全過，覆蓋率 statements 97.4%、branches 96.6%；後端 282 測試全過。決策見 ADR-007。

- 2026-10-10 WU-P0-008 資料庫基線完成：`MyERP.Migrations` 第一個 migration `Core_Baseline`（`dotnet ef ... -p MyERP.Migrations -s MyERP.Api`，以 Api 主機 DI 建 DbContext）；定序改用 `Chinese_Traditional_Stroke_Count_100_CI_AS_SC`（MASTER_PROMPT 原名稱不存在，ADR-006、CR-007），既有庫定序不符時 migration 修正；SP 部署機制（`database/procedures/{前綴}/usp_*.sql` 嵌入 + `CreateOrAlterProcedure` / `DropProcedure`，範例 `usp_sys_database_info`）；EF 模型快取鍵含模組組合；`myerp_test` 每次測試執行重建（assembly fixture，拒絕連其他庫）；補 WU-P0-007 的真實資料庫測試（審計蓋章、樂觀鎖 409、公司 / 軟刪除篩選）；`/health/ready` 資料庫檢查（失敗 503）。本機 `myerp` 已建立並套用 migration。282 測試全過，Migrations 行覆蓋率 93.9%。

- 2026-10-10 WU-P0-007 共用核心完成：MyERP.Shared 加 IClock / SystemClock（UTC+8，`App:TimeZoneOffset`；唯一可讀系統時間的型別，TimeProvider 亦列禁用）、`Money.Round`（TWD/JPY 0 位、其他 2 位、AwayFromZero，可設定）、ICurrentUser / ICurrentCompany、PagedRequest / PagedResult / SortSpec 白名單、ErpException 系列 + errorCode、BusinessEntity / MasterEntity；Shared.Infrastructure 加 MyErpDbContext（IModuleDbModel、snake_case、型別慣例、具名 Company / SoftDelete 篩選、無公司時 `WHERE 0 = 1`、version concurrency token）、AuditStamper + SaveChanges interceptor、全域 ProblemDetails（三語標題、traceId、500 不洩漏細節、404 亦回 ProblemDetails）。243 測試全過，行覆蓋率 Shared 93.5% / Infrastructure 87%（`tools/scripts/Get-BackendCoverage.ps1`）；決策見 ADR-005、設計見 shared-core.md。

- 2026-10-10 WU-P0-006 後端骨架完成：`src/backend/MyERP.sln`（Api、Shared、Shared.Infrastructure、Migrations、Modules.System + Contracts 範本）、Directory.Build/Packages.props（net10.0、Nullable、TreatWarningsAsErrors、ADR-001 版本集中管理）、nuget.config（只用 nuget.org）、`/health/live`、`IErpModule` + `ErpModuleCatalog` 模組註冊；測試 MyERP.Shared.Tests、MyERP.ArchitectureTests（模組互不引用實作、Contracts 純淨、Domain 不依賴 EF/ASP.NET/其他層、禁 DateTime.Now 與 System.Data.SqlClient，以 IL 掃描且有自我測試）、MyERP.IntegrationTests（WebApplicationFactory）共 43 測試全過；xunit.v3 4.0.2 經 global.json 改走 Microsoft.Testing.Platform（ADR-004），測試指令改為 `dotnet test --solution ...`。

- 2026-10-10 WU-P0-005 架構與規範文件完成：architecture.md（全景圖、模組清單、Mermaid 依賴圖、跨模組交易內 / 交易後事件、安全架構、NFR 指標 REQ_NF421.1～425.6）、api-conventions.md（路由 `/api/v1/{prefix}/{複數資源}`、ProblemDetails + errorCode、分頁排序篩選、權限代碼）、db-conventions.md、ui-conventions.md（列表 / 單據頁範本、馬卡龍與 dark mode、i18n key）；ADR-003 決定每模組實作 + Contracts 兩專案、單一 MyErpDbContext、前綴統一於表 / 路由 / 權限。

- 2026-10-10 WU-P0-004 ADR-001 技術版本固定：本機實測 SDK 10.0.301（新增 global.json）、Node 22.16.0；固定 EF Core 10.0.12、EFCore.NamingConventions 10.0.1、SqlClient 6.1.7、Serilog 10、xunit.v3 4.0.2、FluentAssertions 7.2.2（8.x 商業授權）、Moq 4.20.72；前端 Angular 22.2 + ng-zorro 22.1.1 + G2 5.4.8 + TS 6.0，需 Node 24.21.0（新增風險 R-016、R-017）；docgen 用 .NET + OpenXML；查版本腳本 tools/versions/Get-LatestVersions.ps1。

- 2026-10-10 WU-P0-003 項目章程完成：目標 G1–G4、範圍（15 模組 + BlueWhale 基線 222 REQ_F / 150 介面按模組分佈）、不在範圍、成功標準 S1–S8、里程碑 M0–M6、角色；風險清單新增 R-008～015（BlueWhale 不一致、SRS/SDS 版面、無 Python、版本相容）；sprint-log 建立 Sprint 1 計劃。

- 2026-10-10 WU-P0-002 參考文件核對完成：用腳本逐項核對 SRS / SDS 原文（REQ_F 222、REQ_DS43 88、介面 150、REQ_IO_A 1,416），補齊章節規模、版面樣式、表格欄寬框線；對照表 460 行填上 MyERP 模組、需求 ID、介面編號、預計表名；編號規則寫入 ADR-002。
- 2026-10-10 WU-P0-001 Runner 檢查完成：符合 MASTER_PROMPT 2.1 全部要求；額度訊息解析實測 8 個案例正確；6 項改善建議寫入 change-requests.md（CR-001～006），詳見 runner-review.md。
