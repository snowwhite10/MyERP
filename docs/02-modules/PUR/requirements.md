# PUR 採購需求

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
| REQ_F4114.4.1 | 1.0 | System should enable user to view requests for quotation. | Developer | 只顯示目前公司及 SYS VIEW 資料範圍內未軟刪詢價；單號、日期、狀態、請購來源篩選後穩定分頁；詳情含保存行、受邀供應商、版本及發送快照。範圍外／他公司回 404。 |
| REQ_F4114.4.2 | 1.0 | System should enable user to create requests for quotation. | Developer | CREATE 建立草稿，SYS 同交易發公司單號；1～1000 個 MD 有效物料行、1～50 個不同的有效 MD 供應商、正數六位數量、回覆期限不得早於單據日；可選同公司已核准請購行作來源，詢價數量不超過其未轉單量；整單失敗回滾。 |
| REQ_F4114.4.3 | 1.0 | System should enable user to edit requests for quotation. | Developer | EDIT 僅可按版本整組替換草稿，舊版本 409；發送或關閉後不得改動商業條件。 |
| REQ_F4114.4.4 | 1.0 | System should enable user to send requests for quotation. | Developer | SEND 重新驗 MD 物料及供應商與請購來源，保存發送時的行及受邀名單快照；只可發送一次，無外部 email 承諾。 |
| REQ_F4114.4.5 | 1.0 | System should enable user to close requests for quotation. | Developer | CLOSE 將已發送詢價關閉，阻止新的供應商報價；保留原詢價與報價歷史，不自動產生採購單。 |
| REQ_F4114.4.6 | 1.0 | System should enable user to delete requests for quotation. | Developer | DELETE 僅可按版本軟刪草稿；發送後或已有報價不得刪除；單號永久保留。 |
| REQ_F4114.5.1 | 1.1 | System should enable user to view supplier quotations. | Developer | VIEW 按詢價、供應商、有效期與狀態查詢目前公司報價；詳情列出逐行價格、幣別、稅別、版本與修訂歷史，按目前業務日回傳當前修訂是否過期；範圍外及他公司 404。 |
| REQ_F4114.5.2 | 1.0 | System should enable user to record supplier quotations. | Developer | QUOTE 僅為已發送、未關閉詢價的受邀供應商錄入報價；每行報價的物料與基本單位數量取發送快照，單價非負且最多六位小數，幣別與稅別為目前公司有效 MD 引用；有效期不得早於報價日。原子保存，不接受非受邀或重複供應商當前有效報價。 |
| REQ_F4114.5.3 | 1.0 | System should enable user to revise supplier quotations. | Developer | QUOTE 按版本修訂未決標、未過期報價；保留舊版不可覆寫，已決標或已轉單報價不可修訂；舊版本 409。 |
| REQ_F4114.5.4 | 1.1 | System should enable user to withdraw supplier quotations. | Developer | QUOTE 按目前版本撤回未決標、未過期報價；舊版本 409，重複撤回或已決標／過期 422。撤回後不得參與比價，歷史仍可查閱，不影響其他供應商報價。 |
| REQ_F4114.6.1 | 1.0 | System should enable user to compare supplier quotations. | Developer | COMPARE 只比較同一已發送詢價的最新未撤回、未過期報價；逐行展示原幣單價、稅及以 MD 有效匯率換算至本位幣的總成本與匯率日期，缺有效匯率時明示不可比較；不把不同幣別原價直接排序。 |
| REQ_F4114.6.2 | 1.0 | System should enable user to select supplier quotations. | Developer | AWARD 可逐行選擇一個合格供應商報價，保存選擇理由與價格／匯率／稅額快照；重複決標或詢價關閉回 422，版本衝突 409。 |
| REQ_F4114.6.3 | 1.1 | System should enable user to convert selected quotations into a purchase order. | Developer | CONVERT 只接受同一已發送詢價、同一供應商及同一原幣的 1～1000 個不同已決標未轉行；按目前詢價版本原子建立一張採購單草稿、永久單號及不可重用來源連結，保留決標商務快照。空集合／重複 ID 拒絕，任何行衝突整批回滾。請購已轉量由 WU-PUR-006 的訂單提交流程消耗；轉單不改庫存。成功提示顯示已保存單號及行數。 |

詢價三表已於 WU-PUR-005b 落地並分配 REQ_DS43.163～165；報價三表於 005d 分配 166～168，決標快照於 005e3 分配 169。採購單草稿的完整編輯、送審及取消在 WU-PUR-006；本需求群只規定最小可保存轉單草稿與來源唯一性。變更時遞增需求版本並記錄原因。

變更記錄：WU-PUR-005e4 將 REQ_F4114.6.3 從 1.0 更新至 1.1，固定明確行集合、單幣別、版本及來源永久唯一的驗收條件；資料表及端點待實作驗收。

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
