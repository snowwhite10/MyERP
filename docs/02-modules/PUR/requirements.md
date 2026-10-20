# PUR 採購需求

## WU-PUR-000 請購設計基線

WU-PUR-001 交付 REQ_F4114.3.1～6 共用前置條件：PUR 組件與獨立 Contracts 載入、請購 VIEW／CREATE／EDIT／DELETE／SUBMIT（及後續 CSV 的 EXPORT）權限宣告、無公司上下文拒絕、他公司單據隱藏為 404。WU-PUR-002a 已落地資料表與資料庫約束；002b1 完成 3.1 唯讀列表／詳情的保存資料部分。002b2a 加入分店／部門歸屬及 SYS 授權資料範圍，審批歷史待 003；建立、修改與刪除待 002b2 後續切片。

BlueWhale 已占用子功能 4.1 採購單、4.2 採購入庫；MyERP 請購追加 4.3，不更動既有編號。本切片固定契約，產品功能待 WU-PUR-001～004。

| Sub-function No. | Sub-function |
|---|---|
| 4.3 | Purchase Requisition |

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4114.3.1 | 1.1 | System should enable user to view purchase requisitions. | Developer | `PUR.REQUISITION.VIEW` 僅列目前公司及 SYS 授權的本人／部門／分店／全公司範圍；依單號、業務日期、狀態、請購人穩定分頁；詳情呈現保存行、版本、SYS 審批歷史與可轉採購餘量；範圍外及他公司 ID 回 404。 |
| REQ_F4114.3.2 | 1.1 | System should enable user to create purchase requisitions. | Developer | `PUR.REQUISITION.CREATE` 建立草稿並取 SYS 公司單號；1–1000 行、物料與基本單位正數六位數量、需求日期不得早於單據日；只驗本公司有效 MD 引用，不寫 INV 異動或預留庫存。請購人、分店、部門從 SYS 目前授權取得。 |
| REQ_F4114.3.3 | 1.0 | System should enable user to edit purchase requisitions. | Developer | `PUR.REQUISITION.EDIT` 以版本整組替換草稿或已駁回單；駁回修訂回草稿並保留歷史；過期版本 409，待審、已核准、已關閉不可修改。 |
| REQ_F4114.3.4 | 1.0 | System should enable user to approve purchase requisitions. | Developer | `PUR.REQUISITION.SUBMIT` 送 SYS；核准人僅經 SYS 審批 API 決定；最終核准與請購狀態同交易完成，核准量作為後續採購單可引用上限；重複決定不重複釋放數量。 |
| REQ_F4114.3.5 | 1.0 | System should enable user to reject purchase requisitions. | Developer | SYS 核准人可帶意見駁回待審單，保存歷史，不產生採購單或庫存異動；修訂後可重送。 |
| REQ_F4114.3.6 | 1.0 | System should enable user to delete purchase requisitions. | Developer | `PUR.REQUISITION.DELETE` 僅按版本軟刪草稿；已駁回未修訂、待審、已核准及已被引用單據不可刪；保留單號、稽核與歷史。 |

變更記錄：WU-PUR-002b2a 將 REQ_F4114.3.1 由 1.0 提至 1.1，明定 SYS 授權資料範圍及範圍外 404；資料表新增歸屬欄位供查詢，不改需求 ID。WU-PUR-002b2b 將 3.2 提至 1.1，明定建立歸屬來自 SYS 授權。

WU-PUR-002b2c 依現有 3.3／3.6 驗收條件交付後端 PUT／DELETE；沒有修改需求語義或版本。追溯補充：BR-PUR-010、PUR-MUT-001～005，審批與前端交互仍由後續單元實作。

WU-PUR-003a 交付 3.4 的送審部分：僅草稿可帶目前版本送審；送審前重新驗證全部 MD 物料；SYS 審批實例與請購狀態在同一交易保存。無適用審批步驟時立即成為已核准；失敗時請購與審批實例均不得留下變更。最終人工決定、歷史及稽核由 003b 接續。對應 BR-PUR-011、PUR-SUB-001～008。

WU-PUR-003b 交付 3.1、3.4、3.5 的人工決定與歷史部分：SYS 最終核准／駁回在同一交易更新 PUR 請購；GET 詳情先經 VIEW 授權及資料範圍檢查，再讀取同公司所有輪次與逐步意見。請購不在待審狀態時決定回 409 並回滾 SYS 結果；駁回後修訂重送保留前次意見。對應 BR-PUR-012、PUR-APR-001～008；MD 邊界與資料範圍／併發補強留 003c。

WU-PUR-003c 完成 3.4 的邊界驗收：零／負／舊版本、非草稿及軟刪單不建立審批；送審以真實 MD 物料重新檢查停用與軟刪狀態，失敗後原版本可重試；本人／分店／部門／公司範圍及無權路徑保護；雙連線送審與最終決定最多一次成功，稽核、PUR／SYS 狀態及 INV 零異動一致。對應 PUR-APR-009～014，沒有變更既有需求語義與版本。前端提示仍由 004 驗收。

WU-PUR-004a 對 3.1～3.4、3.6 建立前端 API 契約：列表篩選和分頁由伺服器處理；建立／修改只送業務日、備註及重新編號的行，不送可偽造的請購人或組織歸屬；修改／刪除以查詢版本、送審以 JSON 版本呼叫；數量保留六位十進位文字，經真實 HTTP 驗證可保存。前端輸入先驗日期、1～1000 行、正數且小於 10^13 的六位數量及 500 字備註；後端仍為最終驗證者。004a 沒有可見畫面，介面 4.8～4.10 由後續切片交付。

追溯：`business-rules.md` BR-PUR-001～009、`data-structure.md` REQ_DS43.161～162、`api.md`、`ui-spec.md` 介面 4.8～4.10，以及 `test-plan.md` PUR-CREATE-001～006、PUR-SCHEMA-001～003、PUR-SCOPE-001～003、PUR-REQ-001～018。建立草稿已落地；修改／刪除、審批與前端案例仍待測。
