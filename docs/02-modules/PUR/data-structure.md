# PUR 資料結構

## WU-PUR-006d 採購單與請購餘量設計（尚未建表）

沿用 005e4 預定的 `pur_purchase_order`、`pur_purchase_order_line`、`pur_order_source` 三表及永久唯一決標來源連結。006d 不分配 REQ_DS43 ID；005e4 建表時連續分配。採購單表頭狀態預定 Draft／PendingApproval／Approved／Rejected／Cancelled，`deleted_at` 僅供未提交草稿軟刪；行保存 `requisition_line_id?`、基本單位數量與商業快照。轉單行的 `award_id` 可經不可變來源連結追溯，禁止在編輯時替換或刪除。人工行沒有決標來源，可選同公司已核准請購行。

請購的已轉量以同一交易維護的 `pur_requisition_line.converted_quantity` 表示，最初值為零；`remaining = requested_quantity - converted_quantity`，資料庫 CHECK 限 `0 <= converted_quantity <= quantity`。005e4 只建草稿時不增加它。提交時按請購行 ID 升冪取得更新鎖；先聚合同一請購行在本訂單的全部行，再核對餘量，逐行增加已轉量及寫 SYS 審批實例。駁回或允許的取消同交易按保存的訂單行精確扣回一次。訂單表頭保存 `requisition_consumption_active` 或等價持久旗標以防重複釋回；其狀態與請購已轉量在同一 SQL 交易中轉換。未引用請購的人工行不影響已轉量。

既有請購行欄位、索引與 CHECK 由後續 migration 補齊，`REQ_DS43.162` 版本屆時遞增；不新增跨模組 FK。訂單對請購行可用同公司複合 FK，對 MD／SYS 只存 ID 並走公開契約。查詢索引至少涵蓋 `(company_id,requisition_line_id)` 的訂單行、`(company_id,status,business_date,id)` 的訂單表頭；所有 FK 欄位另建索引。SQL 約束與應用服務共同守住精度、非負餘量及來源不可重用；不可變來源連結不得因取消或軟刪被移除。採購入庫關聯由 WU-PUR-007 建立，取消前須經其公開契約檢查沒有已保存入庫，不直接讀 INV 表。

```mermaid
erDiagram
    PUR_REQUISITION_LINE ||--o{ PUR_PURCHASE_ORDER_LINE : requested_from
    PUR_PURCHASE_ORDER ||--|{ PUR_PURCHASE_ORDER_LINE : contains
    PUR_PURCHASE_ORDER_LINE ||--o| PUR_ORDER_SOURCE : converted_from_award
    PUR_RFQ_AWARD ||--o| PUR_ORDER_SOURCE : linked_once
```

WU-PUR-005f3e 只增加按 QUOTE 權限投影既有 `pur_rfq` 保存行／邀請及 MD 公開選項的唯讀契約；不新增欄位、索引、關聯或 migration，以下 ER 圖保持不變。

WU-PUR-005f2c 的 CSV／列印只讀現有 `pur_rfq`、行及邀請快照；沒有新增欄位、索引、migration 或跨模組資料表存取。

## WU-PUR-005e4 轉單草稿設計（待 migration）

預計新增 `pur_purchase_order`、`pur_purchase_order_line`、`pur_order_source` 三表；正式 `REQ_DS43` ID 在 migration 同輪分配，此處不把尚未存在的表宣稱為已落地。表頭只保存公司、SYS 永久唯一單號、詢價 ID、供應商 ID、業務日、擁有人／分店／部門、Draft 狀態、原幣與本位幣總額、版本及審計。單據只能來自同一詢價的一個供應商；不同決標行若原幣不同則拒絕同單轉換，不暗中合併貨幣。行保存行號、物料 ID、可空請購行 ID、六位數量與原幣單價、原幣及本位幣逐行金額，以及決標時的稅率／匯率來源與數值快照；表頭金額只加總已取整的行金額。來源連結保存訂單行 ID 與決標 ID，`(company_id,award_id)` 永久唯一，不因日後取消訂單而刪除；取消與重新轉單規則由 WU-PUR-006 明訂。

三表均用 `bigint identity`、`company_id`、審計時間／用戶及 `version`；同公司複合 FK 連結訂單表頭、行、詢價及決標，不對 MD／SYS 建 FK。`(company_id,document_no)` 永久唯一，`(company_id,order_id,line_no)` 唯一。每個 FK 欄位手動建立索引，另外建立公司／供應商／業務日及來源詢價查詢索引。SQL CHECK 限正數 ID／數量、非負價格與金額、006d 所列合法狀態及金額守恆；005e4 首次建立時狀態只能為 Draft，006 後續擴充狀態轉換。Application 在送 SQL 前拒絕超六位小數，以免 SQL 自動取整。來源連結不允許 UPDATE／DELETE。migration 的 Down 依來源、行、表頭順序還原；必須以真實 SQL 驗證約束與索引。

```mermaid
erDiagram
    PUR_RFQ ||--o{ PUR_PURCHASE_ORDER : converted_from
    PUR_PURCHASE_ORDER ||--|{ PUR_PURCHASE_ORDER_LINE : contains
    PUR_PURCHASE_ORDER_LINE ||--|| PUR_ORDER_SOURCE : traces
    PUR_RFQ_AWARD ||--o| PUR_ORDER_SOURCE : converted_once
```

## WU-PUR-005e3 決標快照

`Purchasing_RfqAward` migration 新增 `pur_rfq_award`（REQ_DS43.169），每個詢價行只容一筆決標；`(company_id,rfq_line_id)` 唯一且有同公司詢價行、報價行複合外鍵。表保存詢價／報價／供應商來源 ID、理由、數量、原幣六位單價、原幣及本位幣代碼、稅率 ID／百分比／生效期間、匯率 ID／數值／生效期間、比較業務日與本位幣淨額／稅額／含稅額。MD ID 僅供追溯，不建跨模組外鍵；索引涵蓋來源、供應商及稅率／匯率查詢。SQL CHECK 限正數數量／匯率、非負價格／金額及金額加總；觸發器禁止 UPDATE／DELETE，Down 可逆刪表。應用服務在同交易驗報價行確屬目前報價與詢價，鎖詢價表頭並遞增版本，避免同版兩個決標同時通過。

```mermaid
erDiagram
    PUR_RFQ_LINE ||--o| PUR_RFQ_AWARD : awarded_once
    PUR_QUOTATION_LINE ||--o| PUR_RFQ_AWARD : selected_price
```

| Requirement ID | Version | Table Name | Description | Author | Related REQ_F |
|---|---|---|---|---|---|
| REQ_DS43.169 | 1.0 | pur_rfq_award | This table stores immutable per-line quotation awards and dated commercial snapshots. | Developer | REQ_F4114.6.2 |

## WU-PUR-005e1 比價依賴的主檔快照契約

本切片不新增 PUR 資料表或 REQ_DS43 ID。後續比價／決標只經 MD 公開契約取得匯率與稅率值、生效期間及來源 ID；不得建立指向 MD 表的 PUR 外鍵或直接讀其表。決標快照表於 005e3 落地；採購單草稿表留 005e4。

## WU-PUR-005d1 供應商報價版本資料表

`Purchasing_SupplierQuotation` migration 建立 `pur_supplier_quotation`、`pur_quotation_revision`、`pur_quotation_line`（REQ_DS43.166～168）。報價表頭以同公司 `(rfq_id,supplier_id)` 唯一，複合 FK 指向詢價邀請；狀態為 Active／Withdrawn／Awarded，`current_revision_no` 是應用服務在同交易維護的目前修訂號。修訂版存報價日、有效期與備註；行存詢價行 ID、六位小數原幣單價、三字元幣別和 MD 稅率 ID。MD 引用只存 ID，不建跨模組 FK；005d3 寫入時須驗證其有效性與幣別。版本及行以 SQL 觸發器拒絕 UPDATE／DELETE，修訂必須新增版本；撤回只改表頭狀態。報價與修訂／行均有審計和版本欄位。

同公司複合 FK 鎖定報價所屬邀請、修訂所屬表頭及行所屬修訂／詢價行；同一修訂的詢價行唯一。行的 `rfq_line_id` 是否屬於報價表頭的同一詢價，仍須由 005d3 Application 同交易驗證。`decimal(19,6)` 可能自行取整，API 另需先拒絕超六位。索引支援公司／詢價／供應商、狀態、修訂號、詢價行及稅率查詢。可逆 migration 的 Down 移除三表及新複合鍵。

```mermaid
erDiagram
    PUR_RFQ_INVITATION ||--o| PUR_SUPPLIER_QUOTATION : receives
    PUR_SUPPLIER_QUOTATION ||--|{ PUR_QUOTATION_REVISION : revises
    PUR_QUOTATION_REVISION ||--|{ PUR_QUOTATION_LINE : prices
    PUR_RFQ_LINE ||--o{ PUR_QUOTATION_LINE : quoted_for
```

| Requirement ID | Version | Table Name | Description | Author | Related REQ_F |
|---|---|---|---|---|---|
| REQ_DS43.166 | 1.0 | pur_supplier_quotation | This table stores one company-scoped supplier quotation header per RFQ invitation. | Developer | REQ_F4114.5.1, REQ_F4114.5.2, REQ_F4114.5.3, REQ_F4114.5.4 |
| REQ_DS43.167 | 1.0 | pur_quotation_revision | This table stores immutable dated quotation revisions. | Developer | REQ_F4114.5.1, REQ_F4114.5.2, REQ_F4114.5.3 |
| REQ_DS43.168 | 1.0 | pur_quotation_line | This table stores immutable per-line prices, currencies and tax references. | Developer | REQ_F4114.5.1, REQ_F4114.5.2, REQ_F4114.5.3 |

## WU-PUR-005b 詢價草稿資料表

`Purchasing_RfqDraft` migration 已建立 `pur_rfq`、`pur_rfq_line`、`pur_rfq_invitation`。表頭保存永久唯一的公司單號、業務日、回覆期限、負責人及組織歸屬、草稿／已發送／關閉狀態、送出時間和軟刪欄位。行保存物料、可空的同公司請購行來源、需求日及 `decimal(19,6)` 數量；邀請保存 MD 供應商 ID，發送時填入代碼／名稱和時間快照。三表均有 `bigint identity` 主鍵、公司、審計及版本欄位。MD／SYS 引用只有 ID，沒有跨模組 FK；請購來源使用 `(company_id,requisition_line_id)` 複合 FK。此切片只有持久化模型，草稿 API 與引用有效性檢查留 005c。

`(company_id,document_no)` 永久唯一，即使草稿軟刪仍不能重用；`(company_id,rfq_id,line_no)` 及 `(company_id,rfq_id,supplier_id)` 唯一。子表以 `(company_id,rfq_id)` 複合 FK 指向表頭，禁止跨公司拼接；已發送資料無級聯刪除。日期、狀態、正數 ID／數量及邀請快照完整性有 SQL CHECK。索引涵蓋公司／日期、狀態、負責人、分店、部門列表及子表的物料、來源請購行、供應商查詢。前端和 Application 仍需在送 SQL 前拒絕多於六位小數，因 SQL `decimal(19,6)` 可能自行取整。

```mermaid
erDiagram
    PUR_RFQ ||--|{ PUR_RFQ_LINE : contains
    PUR_RFQ ||--|{ PUR_RFQ_INVITATION : invites
    PUR_REQUISITION_LINE ||--o{ PUR_RFQ_LINE : optional_source
```

| Requirement ID | Version | Table Name | Description | Author | Related REQ_F |
|---|---|---|---|---|---|
| REQ_DS43.163 | 1.0 | pur_rfq | This table stores company-scoped requests for quotation, document numbers, deadlines and send state. | Developer | REQ_F4114.4.1, REQ_F4114.4.2, REQ_F4114.4.3, REQ_F4114.4.4, REQ_F4114.4.5, REQ_F4114.4.6 |
| REQ_DS43.164 | 1.0 | pur_rfq_line | This table stores item demand, quantities and optional requisition line references for requests for quotation. | Developer | REQ_F4114.4.1, REQ_F4114.4.2, REQ_F4114.4.3, REQ_F4114.4.4 |
| REQ_DS43.165 | 1.0 | pur_rfq_invitation | This table stores invited supplier IDs and send-time name snapshots. | Developer | REQ_F4114.4.1, REQ_F4114.4.2, REQ_F4114.4.4 |

## WU-PUR-005a 詢價至轉單資料模型預案（尚未建表）

此段是 WU-PUR-005b／d／e 的建模契約；詢價三表已在 005b 落地，報價三表在 005d 落地，決標表在 005e3 落地；採購單表待 005e4 migration 與 REQ_DS43 ID。詢價表頭保存公司、單號、業務日期、回覆期限、負責人／分店／部門、狀態、版本及審計；詢價行保存 MD 物料 ID、請購行 ID（可空）、基本單位數量與需求日期；邀請表保存供應商 ID 及發送快照。報價表頭保存詢價及供應商、有效期、目前修訂號與狀態；報價版本與行保存不可變的價格、幣別、稅別及原始商務條件。決標行保存詢價行／報價版本行與換算快照、理由；轉單狀態由 005e4 的獨立來源連結表示。採購單草稿與來源連結於 005e4 建立，完整欄位由 006 補足。

同模組子表使用 `(company_id,parent_id)` 複合 FK 與索引；公司單號永久唯一，邀請名單 `(company_id,rfq_id,supplier_id)` 唯一，報價同一詢價／供應商只有一筆目前有效表頭，修訂版本以 `(company_id,quotation_id,revision_no)` 唯一；決標來源行及轉單來源以唯一約束防重。所有 FK 欄位另有查詢索引。MD／SYS ID 僅存 ID，透過其公開契約驗證，不建立跨模組 FK。金額 `decimal(19,4)`、單價 `decimal(19,6)`、數量 `decimal(19,6)`、匯率 `decimal(19,8)`，業務日期 `date`，建立／修訂時間 `datetime2(6)`；具體 SQL CHECK、可逆 migration 與索引效能在建表切片驗證。

```mermaid
erDiagram
    PUR_RFQ ||--|{ PUR_RFQ_LINE : contains
    PUR_RFQ ||--|{ PUR_RFQ_INVITATION : invites
    PUR_RFQ ||--o{ PUR_SUPPLIER_QUOTATION : receives
    PUR_SUPPLIER_QUOTATION ||--|{ PUR_QUOTATION_REVISION : revises
    PUR_QUOTATION_REVISION ||--|{ PUR_QUOTATION_LINE : prices
    PUR_RFQ_LINE ||--o| PUR_RFQ_AWARD : selects
    PUR_QUOTATION_LINE ||--o| PUR_RFQ_AWARD : selected
    PUR_RFQ_AWARD ||--o| PUR_ORDER_SOURCE : converts
```

詢價不消耗請購餘量；決標也不消耗。後續採購單提交時才以行鎖及請購累計量約束核實剩餘數量。詢價或報價被引用後保留歷史，不級聯刪除；軟刪限未發送草稿。所有表具公司、建立者／建立時間、更新者／更新時間、版本與軟刪欄位（適用時），遵循第 6 節。

## WU-PUR-002a 請購資料表

`Purchasing_RequisitionDraft` migration 建立 `pur_requisition` 表頭及 `pur_requisition_line` 明細。表頭有公司、SYS 單號、業務日期、請購人 ID、狀態、備註、審計、版本及軟刪欄位；明細有公司、表頭 ID、行號、MD 物料 ID、需求日期、六位基本單位數量、已轉採購數量、備註及審計。兩表使用 `bigint identity` 主鍵，日期為 `date`，數量為 `decimal(19,6)`；表頭 `(company_id,document_no)` 永久唯一，行 `(company_id,requisition_id,line_no)` 唯一。公司／日期／ID、公司／狀態／ID、公司／請購人／ID 支援列表索引；行表頭外鍵由唯一索引涵蓋，物料另有公司複合索引。MD／SYS ID 只保存，不建立跨模組 FK 或 navigation。

`Purchasing_RequisitionDataScope` 增加表頭可空的 `branch_id`、`department_id`，並分別建立 `(company_id,branch_id,id)`、`(company_id,department_id,id)` 索引。既有記錄未填組織歸屬時，分店／部門資料範圍不可見，只有本人或全公司可見；建立端點須從 SYS 當前授權寫入歸屬，不能信任用戶提供的組織 ID。

已核准數量是請購行數量，後續採購單消耗的累計量只由 PUR 同交易維護，並受 `0 ≤ 已轉採購量 ≤ 請購量` 守衛；請購本身不產生庫存異動。後續轉單設計須以同公司與行鎖避免雙重引用。

WU-PUR-002b2c 不變更資料庫結構。修改草稿或駁回單時在交易內刪除原行並插入整組新行，沿用同公司複合 FK 與行號唯一約束；表頭單號與建立歸屬保持不變。軟刪只改表頭 `is_deleted`、`deleted_at`，單號唯一索引仍保留舊號。

```mermaid
erDiagram
    PUR_REQUISITION ||--|{ PUR_REQUISITION_LINE : contains
    PUR_REQUISITION {
        bigint id PK
        bigint company_id
        string document_no
        date business_date
        bigint requester_id
        bigint branch_id
        bigint department_id
        string status
    }
    PUR_REQUISITION_LINE {
        bigint id PK
        bigint company_id
        bigint requisition_id FK
        int line_no
        bigint item_id
        date required_date
        decimal quantity
        decimal ordered_quantity
    }
```

| Requirement ID | Version | Table Name | Description | Author | Related REQ_F |
|---|---|---|---|---|---|
| REQ_DS43.161 | 1.0 | pur_requisition | This table stores company-scoped purchase requisition headers, document numbers and approval state. | Developer | REQ_F4114.3.1, REQ_F4114.3.2, REQ_F4114.3.3, REQ_F4114.3.4, REQ_F4114.3.5, REQ_F4114.3.6 |
| REQ_DS43.162 | 1.0 | pur_requisition_line | This table stores ordered item demand, required dates, quantities and downstream order consumption. | Developer | REQ_F4114.3.1, REQ_F4114.3.2, REQ_F4114.3.3, REQ_F4114.3.4 |
