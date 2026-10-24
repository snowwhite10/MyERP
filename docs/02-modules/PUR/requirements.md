# PUR 採購需求

## WU-PUR-007d 收貨、採購入庫與退貨設計基線（尚未實作）

沿用 BlueWhale 4.2 Purchase Inbound 六項 ID；退貨是 MyERP 新增的 4.7。收貨以已核准採購單的逐行未收量為上限，只有入庫單的 SYS 最終核准才經 INV 公開契約增加庫存。退貨只引用已過帳入庫行，須有新的 INV 專用退貨出庫契約；現有 `PostSalesOutboundAsync` 不適用。本設計對應 BR-PUR-028～035、PUR-RECV-001～028。007d 沒有建表、端點、畫面或產品測試。

| Sub-function No. | Sub-function |
|---|---|
| 4.2 | Purchase Inbound |
| 4.7 | Purchase Return |

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4114.2.1 | 1.0 | System should enable user to view purchase inbound documents. | Developer | VIEW 只列目前公司及 SYS 資料範圍內的單據，依單號、日期、採購單與狀態穩定分頁；詳情顯示已保存行、批序號、審批歷史、已收／未收量及 INV 過帳結果，跨公司或範圍外回 404。列印重新讀保存資料；CSV 最多 10000 行、無部分檔。 |
| REQ_F4114.2.2 | 1.0 | System should enable user to create purchase inbound documents. | Developer | CREATE 只從同公司已核准採購單建立 1～1000 行草稿，按訂單行選擇倉庫／儲位及批序號；基本單位正數六位量不得超過逐行可收量，成本使用採購單已保存原幣價格、稅率與匯率換算後的本位幣未稅成本快照。草稿不寫 INV。 |
| REQ_F4114.2.3 | 1.0 | System should enable user to edit purchase inbound documents. | Developer | EDIT 帶版本整組修訂 Draft／Rejected 的數量、倉儲與追蹤；來源採購單、供應商、物料及成本來源不可改。待審／已過帳不得修改，舊版 409；修改後重新驗可收量。 |
| REQ_F4114.2.4 | 1.0 | System should enable user to approve purchase inbound documents. | Developer | SUBMIT 原子鎖定採購單行並預留全部可收量、建立 SYS 審批；只有 SYS 最終核准同交易把入庫單標為 Posted 並以已保存明細呼叫 INV `PostPurchaseInboundAsync`。零步驟流程也遵守同一交易。失敗全部回滾；重放相同快照不得重複入庫。 |
| REQ_F4114.2.5 | 1.0 | System should enable user to reject purchase inbound documents. | Developer | SYS 核准人可駁回 PendingApproval 入庫單；同交易釋回預留量，保留歷史與意見，不呼叫 INV。重複／過時決定不得二次釋回。 |
| REQ_F4114.2.6 | 1.0 | System should enable user to delete purchase inbound documents. | Developer | DELETE 僅可帶版本軟刪 Draft；Rejected 須先修訂回 Draft。PendingApproval 與 Posted 不可刪，Posted 只能經退貨沖回且 INV 原異動不修改。 |
| REQ_F4114.7.1 | 1.0 | System should enable user to view purchase returns. | Developer | VIEW 可追溯原 Posted 入庫行、可退量、保存的退貨狀態、批序號、審批和出庫異動；只見目前公司與授權範圍。 |
| REQ_F4114.7.2 | 1.0 | System should enable user to create and edit purchase returns. | Developer | 從同公司 Posted 入庫行建立 Draft；1～1000 行、每行正數六位量，不超原入庫行扣除已預留／已過帳退貨量；保留原入庫成本與批序號來源。僅 Draft／Rejected 可帶版本修訂或草稿軟刪，無庫存副作用。 |
| REQ_F4114.7.3 | 1.0 | System should enable user to approve or reject purchase returns. | Developer | SUBMIT 原子預留可退量並送 SYS；最終核准同交易呼叫新的 INV 採購退貨出庫契約，以原入庫 movement 的本位幣單位成本沖回。庫存或追蹤不足整單失敗；駁回釋回預留，不寫 INV。不得借用銷售出庫來源或直接改 INV 表。 |

變更記錄：2026-10-24 WU-PUR-007d 首次固定 4.2.1～6 與新增 4.7.1～3；前者沿用 BlueWhale ID，後者追加且不重排既有子功能。REQ_DS43 於 migration 真實建表時分配，REQ_IO_A 於實際前端畫面時分配。

## WU-PUR-006d 採購單設計基線（尚未實作）

BlueWhale 的 4.1 採購單六項需求沿用原編號。人工建立與 ADR-070 的決標轉單草稿共用同一採購單生命週期；後者保留不可變決標來源與商業快照。提交才鎖定同公司已核准請購行並消耗剩餘量；詢價與轉單草稿都不預留。需求對應 BR-PUR-022～027、介面 4.1～4.3、PUR-PO-001～024。006d 只固定規格，沒有宣稱下表功能已完成。

| Sub-function No. | Sub-function |
|---|---|
| 4.1 | Purchase Order |

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4114.1.1 | 1.0 | System should enable user to view purchase orders. | Developer | VIEW 只列目前公司及 SYS 動作資料範圍內的訂單；依單號、日期、供應商、狀態及請購來源穩定分頁。詳情顯示保存行、決標來源、請購原量／已轉量／餘量、版本與 SYS 審批歷史；他公司或範圍外 404。另持 EXPORT 權限才可匯出完整安全 CSV；列印重新讀取已保存詳情。 |
| REQ_F4114.1.2 | 1.0 | System should enable user to create purchase orders. | Developer | CREATE 可人工建立 1～1000 行草稿；CONVERT 建立的草稿沿用 REQ_F4114.6.3。SYS 同交易發永久唯一單號；供應商、物料、幣別、進項稅率與可選請購來源須在目前公司有效，正數六位基本單位數量、非負六位單價、逐行四位金額；來源請購必須已核准且物料相同。草稿不消耗請購量、不寫 INV。 |
| REQ_F4114.1.3 | 1.0 | System should enable user to edit purchase orders. | Developer | EDIT 只可帶目前版本修改草稿或駁回單的可編輯表頭與人工行；決標轉單行的來源、供應商、數量與價格快照不可改。駁回的純轉單訂單可只修訂備註後回草稿。修訂後重新驗人工行 MD 與請購引用並重算金額；待審、已核准、已取消不可修改；舊版本 409。 |
| REQ_F4114.1.4 | 1.0 | System should enable user to approve purchase orders. | Developer | SUBMIT 只接受草稿及目前版本，按請購行鎖定並檢查全部來源的累計已轉量不超核准量，在同一交易增加已轉量與建立 SYS 審批實例；零步驟立即核准。人工最終核准只經 SYS 審批 API，同交易更新訂單狀態；重複提交／決定不重複消耗。核准不寫 INV，採購入庫另行過帳。 |
| REQ_F4114.1.5 | 1.0 | System should enable user to reject purchase orders. | Developer | SYS 核准人可帶意見駁回待審訂單；審批決定、訂單狀態與全部請購預占量的釋回同交易完成。保留歷史，修訂後可重送；重複駁回或已核准不得再釋回。 |
| REQ_F4114.1.6 | 1.0 | System should enable user to delete purchase orders. | Developer | DELETE 只可按版本軟刪未提交草稿；已提交、已核准、已有入庫引用均不可刪。核准且未入庫訂單以獨立 CANCEL 動作取消並原子釋回請購量；已入庫不得取消，須走後續退貨流程。刪除或取消後永久保留單號與決標來源連結，不允許重轉同一決標行。 |

變更記錄：2026-10-24 WU-PUR-006d 首次固定 4.1.1～6 的驗收條件。取消作為 4.1.6 的明確受權延伸動作，不新增 BlueWhale 功能編號；正式操作須在後續子單元驗收。

WU-PUR-005f3e 將 REQ_F4114.5.2 由 1.0 升至 1.1：表單讀取改用 QUOTE 專屬最小保存上下文及目前公司／報價日主檔選項，消除對 RFQ.VIEW／MD.VIEW 的額外角色依賴；保存時仍重新驗引用。其餘需求 ID 不變。

WU-PUR-005f2b 依既有 REQ_F4114.4.4／5：4.11 詳情只對草稿顯示 SEND、已發送顯示 CLOSE，分別檢查動作權限；確認框說明發送不寄電郵、關閉後不收新報價。帶保存版本提交，確認取消不發請求，處理中不能重複提交；成功後狀態與版本採伺服器回應，409／404 清除過時詳情並保留錯誤原因，其他失敗不顯示成功。需求 ID 與版本不變。

## WU-PUR-005a 詢價、供應商報價與比價設計基線

本工作單元固定契約，實體、API 與畫面依 WU-PUR-005b～f 交付。詢價是向受邀供應商索取價格的商業文件，不會預留請購量或改動庫存。對應 ADR-070、BR-PUR-014～020、介面 4.11～4.15、PUR-RFQ-001～018。

WU-PUR-005f1 依 REQ_F4114.4.1 交付唯讀列表／保存詳情。進頁須有 `PUR.RFQ.VIEW`，篩選錯誤不發請求；篩選與頁碼可從 URL 還原／分享。查詢、換公司或列表失敗關閉舊詳情，詳情 404 重查列表並保留具體原因，5xx 顯示 traceId。原需求 ID／版本不變；發送、關閉與轉單不在此切片。

WU-PUR-005f2a2 依既有 REQ_F4114.4.2／3：建立與修訂使用 RFQ CREATE／EDIT 授權的 MD 選項；EDIT 詳情及已保存引用只按 EDIT 動作資料範圍讀，只有 EDIT 而沒有 VIEW 仍可修訂。已保存的停用／未核准引用顯示原代碼及不可用原因，提交時仍由後端拒絕。前端建立／修訂表單、三語亮暗及模擬 E2E 已交付；請購來源選擇器顯示同公司已核准且有餘量的行，保存實際行 ID，已保存的失效來源仍顯示原單號及原因。SQL 服務停止，真實 SQL／受權 HTTP 與後端全量未驗；本單元未結案，需求 ID 與版本不變。

WU-PUR-005f2a2 來源選擇器按目前公司、物料及單號分頁，變更物料清除舊來源，後端保存仍重驗狀態與餘量。此為既有 4.4.2 驗收條件的介面實作，需求 ID／版本不變；SQL 服務未恢復前不宣稱真實資料庫驗收通過。

| Sub-function No. | Sub-function |
|---|---|
| 4.4 | Request for Quotation |
| 4.5 | Supplier Quotation |
| 4.6 | Quotation Comparison |

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4114.4.1 | 1.1 | System should enable user to view requests for quotation. | Developer | 只顯示目前公司及 SYS VIEW 資料範圍內未軟刪詢價；單號、日期、狀態、請購來源篩選後穩定分頁；詳情含保存行、受邀供應商、版本及發送快照。範圍外／他公司回 404。另有 EXPORT 權限者可按已套用篩選下載最多 10000 行已保存明細的 UTF-8 安全 CSV；0 行、資料變動、取消或任一請求失敗不下載部分檔。列印須重新讀取已保存詳情。 |
| REQ_F4114.4.2 | 1.0 | System should enable user to create requests for quotation. | Developer | CREATE 建立草稿，SYS 同交易發公司單號；1～1000 個 MD 有效物料行、1～50 個不同的有效 MD 供應商、正數六位數量、回覆期限不得早於單據日；可選同公司已核准請購行作來源，詢價數量不超過其未轉單量；整單失敗回滾。 |
| REQ_F4114.4.3 | 1.0 | System should enable user to edit requests for quotation. | Developer | EDIT 僅可按版本整組替換草稿，舊版本 409；發送或關閉後不得改動商業條件。 |
| REQ_F4114.4.4 | 1.0 | System should enable user to send requests for quotation. | Developer | SEND 重新驗 MD 物料及供應商與請購來源，保存發送時的行及受邀名單快照；只可發送一次，無外部 email 承諾。 |
| REQ_F4114.4.5 | 1.0 | System should enable user to close requests for quotation. | Developer | CLOSE 將已發送詢價關閉，阻止新的供應商報價；保留原詢價與報價歷史，不自動產生採購單。 |
| REQ_F4114.4.6 | 1.0 | System should enable user to delete requests for quotation. | Developer | DELETE 僅可按版本軟刪草稿；發送後或已有報價不得刪除；單號永久保留。 |
| REQ_F4114.5.1 | 1.1 | System should enable user to view supplier quotations. | Developer | VIEW 按詢價、供應商、有效期與狀態查詢目前公司報價；詳情列出逐行價格、幣別、稅別、版本與修訂歷史，按目前業務日回傳當前修訂是否過期；範圍外及他公司 404。 |
| REQ_F4114.5.2 | 1.1 | System should enable user to record supplier quotations. | Developer | QUOTE 僅為已發送、未關閉詢價的受邀供應商錄入報價；有 QUOTE 但沒有 RFQ.VIEW／MD.VIEW 的用戶仍可在其資料範圍取得最小已保存詢價行、受邀供應商及目前公司有效幣別與報價日進項稅率選項。每行報價的物料與基本單位數量取發送快照，單價非負且最多六位小數；有效期不得早於報價日。寫入時重驗 MD 引用，原子保存，不接受非受邀或重複供應商當前有效報價。 |
| REQ_F4114.5.3 | 1.0 | System should enable user to revise supplier quotations. | Developer | QUOTE 按版本修訂未決標、未過期報價；保留舊版不可覆寫，已決標或已轉單報價不可修訂；舊版本 409。 |
| REQ_F4114.5.4 | 1.1 | System should enable user to withdraw supplier quotations. | Developer | QUOTE 按目前版本撤回未決標、未過期報價；舊版本 409，重複撤回或已決標／過期 422。撤回後不得參與比價，歷史仍可查閱，不影響其他供應商報價。 |
| REQ_F4114.6.1 | 1.1 | System should enable user to compare supplier quotations. | Developer | COMPARE 對已發送或已關閉詢價逐行顯示所有目前報價，包含撤回、過期及未生效項；逐行展示原幣單價、進項稅率、有效匯率及來源期間、本位幣未稅／稅額／含稅成本；不可比項明示原因且不顯示比較金額，不把不同幣別原價直接排序。畫面只以已驗收 GET 取得即時資料，錯誤或換公司清除舊成本。WU-PUR-005f3c1 只交付唯讀畫面。 |
| REQ_F4114.6.2 | 1.0 | System should enable user to select supplier quotations. | Developer | AWARD 可逐行選擇一個合格供應商報價，保存選擇理由與價格／匯率／稅額快照；重複決標或詢價關閉回 422，版本衝突 409。 |
| REQ_F4114.6.3 | 1.1 | System should enable user to convert selected quotations into a purchase order. | Developer | CONVERT 只接受同一已發送詢價、同一供應商及同一原幣的 1～1000 個不同已決標未轉行；按目前詢價版本原子建立一張採購單草稿、永久單號及不可重用來源連結，保留決標商務快照。空集合／重複 ID 拒絕，任何行衝突整批回滾。請購已轉量由 WU-PUR-006 的訂單提交流程消耗；轉單不改庫存。成功提示顯示已保存單號及行數。 |

詢價三表已於 WU-PUR-005b 落地並分配 REQ_DS43.163～165；報價三表於 005d 分配 166～168，決標快照於 005e3 分配 169。採購單草稿的完整編輯、送審及取消在 WU-PUR-006；本需求群只規定最小可保存轉單草稿與來源唯一性。變更時遞增需求版本並記錄原因。

變更記錄：WU-PUR-005e4 將 REQ_F4114.6.3 從 1.0 更新至 1.1，固定明確行集合、單幣別、版本及來源永久唯一的驗收條件；資料表及端點待實作驗收。WU-PUR-005f2c 將 REQ_F4114.4.1 從 1.0 更新至 1.1，加入已套用篩選的安全 CSV 與保存詳情列印；對應 BR-PUR-021、REQ_IO_A4.11.4～5、PUR-RFQ-092～098。WU-PUR-005f3c1 將 REQ_F4114.6.1 從 1.0 更新至 1.1，對齊已驗收 GET 的歷史不可比項、稅率／匯率來源及唯讀畫面錯誤清除條件。

## WU-PUR-000 請購設計基線

WU-PUR-001 交付 REQ_F4114.3.1～6 共用前置條件：PUR 組件與獨立 Contracts 載入、請購 VIEW／CREATE／EDIT／DELETE／SUBMIT（及後續 CSV 的 EXPORT）權限宣告、無公司上下文拒絕、他公司單據隱藏為 404。WU-PUR-002a 已落地資料表與資料庫約束；002b1 完成 3.1 唯讀列表／詳情的保存資料部分。002b2a 加入分店／部門歸屬及 SYS 授權資料範圍，審批歷史待 003；建立、修改與刪除待 002b2 後續切片。

BlueWhale 已占用子功能 4.1 採購單、4.2 採購入庫；MyERP 請購追加 4.3，不更動既有編號。本切片固定契約，產品功能待 WU-PUR-001～004。

| Sub-function No. | Sub-function |
|---|---|
| 4.3 | Purchase Requisition |

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4114.3.1 | 1.2 | System should enable user to view purchase requisitions. | Developer | `PUR.REQUISITION.VIEW` 僅列目前公司及 SYS 授權的本人／部門／分店／全公司範圍；依單號、業務日期、狀態、請購人穩定分頁；詳情呈現保存行、版本、SYS 審批歷史與可轉採購餘量；範圍外及他公司 ID 回 404。另持 EXPORT 權限可按目前篩選匯出所有保存明細，UTF-8 CSV 公式安全，最多 10000 行；取消、資料變動或任一請求失敗不得下載部分檔。 |
| REQ_F4114.3.2 | 1.1 | System should enable user to create purchase requisitions. | Developer | `PUR.REQUISITION.CREATE` 建立草稿並取 SYS 公司單號；1–1000 行、物料與基本單位正數六位數量、需求日期不得早於單據日；只驗本公司有效 MD 引用，不寫 INV 異動或預留庫存。請購人、分店、部門從 SYS 目前授權取得。 |
| REQ_F4114.3.3 | 1.0 | System should enable user to edit purchase requisitions. | Developer | `PUR.REQUISITION.EDIT` 以版本整組替換草稿或已駁回單；駁回修訂回草稿並保留歷史；過期版本 409，待審、已核准、已關閉不可修改。 |
| REQ_F4114.3.4 | 1.0 | System should enable user to approve purchase requisitions. | Developer | `PUR.REQUISITION.SUBMIT` 送 SYS；核准人僅經 SYS 審批 API 決定；最終核准與請購狀態同交易完成，核准量作為後續採購單可引用上限；重複決定不重複釋放數量。 |
| REQ_F4114.3.5 | 1.0 | System should enable user to reject purchase requisitions. | Developer | SYS 核准人可帶意見駁回待審單，保存歷史，不產生採購單或庫存異動；修訂後可重送。 |
| REQ_F4114.3.6 | 1.0 | System should enable user to delete purchase requisitions. | Developer | `PUR.REQUISITION.DELETE` 僅按版本軟刪草稿；已駁回未修訂、待審、已核准及已被引用單據不可刪；保留單號、稽核與歷史。 |

變更記錄：WU-PUR-002b2a 將 REQ_F4114.3.1 由 1.0 提至 1.1，明定 SYS 授權資料範圍及範圍外 404；資料表新增歸屬欄位供查詢，不改需求 ID。WU-PUR-002b2b 將 3.2 提至 1.1，明定建立歸屬來自 SYS 授權。WU-PUR-005d4 將 5.1、5.4 提至 1.1，明定即時計算過期旗標與撤回版本／狀態守衛；過期不寫回歷史狀態。

WU-PUR-004d 將 3.1 由 1.1 提至 1.2，加入完整安全 CSV、10000 明細行上限、取消及整檔失敗契約；對應 BR-PUR-013、介面 REQ_IO_A4.8.5 與 PUR-CSV-001～008。

WU-PUR-002b2c 依現有 3.3／3.6 驗收條件交付後端 PUT／DELETE；沒有修改需求語義或版本。追溯補充：BR-PUR-010、PUR-MUT-001～005，審批與前端交互仍由後續單元實作。

WU-PUR-003a 交付 3.4 的送審部分：僅草稿可帶目前版本送審；送審前重新驗證全部 MD 物料；SYS 審批實例與請購狀態在同一交易保存。無適用審批步驟時立即成為已核准；失敗時請購與審批實例均不得留下變更。最終人工決定、歷史及稽核由 003b 接續。對應 BR-PUR-011、PUR-SUB-001～008。

WU-PUR-003b 交付 3.1、3.4、3.5 的人工決定與歷史部分：SYS 最終核准／駁回在同一交易更新 PUR 請購；GET 詳情先經 VIEW 授權及資料範圍檢查，再讀取同公司所有輪次與逐步意見。請購不在待審狀態時決定回 409 並回滾 SYS 結果；駁回後修訂重送保留前次意見。對應 BR-PUR-012、PUR-APR-001～008；MD 邊界與資料範圍／併發補強留 003c。

WU-PUR-003c 完成 3.4 的邊界驗收：零／負／舊版本、非草稿及軟刪單不建立審批；送審以真實 MD 物料重新檢查停用與軟刪狀態，失敗後原版本可重試；本人／分店／部門／公司範圍及無權路徑保護；雙連線送審與最終決定最多一次成功，稽核、PUR／SYS 狀態及 INV 零異動一致。對應 PUR-APR-009～014，沒有變更既有需求語義與版本。前端提示仍由 004 驗收。

WU-PUR-004a 對 3.1～3.4、3.6 建立前端 API 契約：列表篩選和分頁由伺服器處理；建立／修改只送業務日、備註及重新編號的行，不送可偽造的請購人或組織歸屬；修改／刪除以查詢版本、送審以 JSON 版本呼叫；數量保留六位十進位文字，經真實 HTTP 驗證可保存。前端輸入先驗日期、1～1000 行、正數且小於 10^13 的六位數量及 500 字備註；後端仍為最終驗證者。004a 沒有可見畫面，介面 4.8～4.10 由後續切片交付。

WU-PUR-004b 交付 3.1、3.4～3.6 的介面 4.8 列表／已保存詳情：依 VIEW 權限進頁，伺服器分頁與篩選，顯示所有 SYS 審批輪次；只在草稿顯示送審／刪除，刪除需確認，列印前重取保存詳情。載入新列表時關閉舊詳情，避免空結果仍顯示前次單據；MD 停用／刪除回應顯示具體翻譯原因。建立與修訂畫面仍待 004c，安全 CSV 待 004d，沒有變更需求 ID 或版本。

WU-PUR-004c 交付 3.2～3.3 的介面 4.9／4.10：CREATE／EDIT 路由守衛、公司啟用 MD 物料分頁選擇、已保存物料名稱與停用標記、業務日／需求日／六位數量／備註逐欄提示及 1～1000 行整單保存。EDIT 專用詳情依 EDIT 資料範圍，沒有 VIEW 權限亦可載入可修訂單據。已駁回與草稿可修訂，待審／核准／關閉顯示不可修訂原因；舊版本與 API 拒絕不宣稱成功、原輸入保留；未存離開要確認。後端選項端點走 MD 公開契約，不直接讀 MD 表。沒有變更需求 ID 或版本；安全 CSV 仍待 004d。

追溯：`business-rules.md` BR-PUR-001～009、`data-structure.md` REQ_DS43.161～162、`api.md`、`ui-spec.md` 介面 4.8～4.10，以及 `test-plan.md` PUR-CREATE-001～006、PUR-SCHEMA-001～003、PUR-SCOPE-001～003、PUR-REQ-001～018。建立草稿已落地；修改／刪除、審批與前端案例仍待測。
