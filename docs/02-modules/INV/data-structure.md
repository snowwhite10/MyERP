# INV 資料結構

## WU-INV-015d PUR／SD 過帳設計

沿用 `inv_stock_movement`（REQ_DS43.135）及批序號表 REQ_DS43.142–144；不新增 INV 表。`source_type` 固定為 `PURCHASE_INBOUND` 或 `SALES_OUTBOUND`，`source_id` 只保存來源模組單據 ID，不建跨模組外鍵或 EF navigation。既有公司＋來源＋行號＋方向唯一約束和來源鎖用於防重放。PUR／SD 的單據表與 ER 圖由各自模組定義；本輪不推測欄位。

## WU-INV-014d 估值讀取設計

估值以現有 `inv_stock_movement`（REQ_DS43.135）的不可變異動為唯一來源，不新增持久化表。014a 已新增 `(company_id, item_id, posted_at, id)` 覆蓋索引，涵蓋 `direction, quantity, unit_cost, source_type, source_id, source_line_no`；只在同公司物料內作順序讀取，SQL 不 JOIN 其他模組表。估值結果是查詢資料，不是庫存彙總或 FI 憑證。

```mermaid
erDiagram
    INV_STOCK_MOVEMENT {
        bigint id PK
        bigint company_id
        bigint item_id
        datetime posted_at
        string direction
        decimal quantity
        decimal unit_cost
        string source_type
        bigint source_id
    }
```

## WU-INV-010a 盤點（010a1：`Inventory_Stocktake` migration 已套用 myerp）

| Requirement ID | Version | Table Name | Description | Author | Related REQ_F |
|---|---|---|---|---|---|
| REQ_DS43.151 | 1.0 | inv_stocktake | This table stores company-scoped stocktake headers, their count scope, snapshot marker and approval and posting state. | Developer | REQ_F4115.7.1, REQ_F4115.7.2, REQ_F4115.7.3, REQ_F4115.7.4, REQ_F4115.7.5, REQ_F4115.7.6, REQ_F4115.7.7 |
| REQ_DS43.152 | 1.0 | inv_stocktake_line | This table stores the snapshot book quantity and counted quantity of each item, warehouse and location stock key. | Developer | REQ_F4115.7.1, REQ_F4115.7.2, REQ_F4115.7.3, REQ_F4115.7.4, REQ_F4115.7.5 |
| REQ_DS43.153 | 1.0 | inv_stocktake_line_tracking | This table stores the snapshot book quantity and counted quantity of each lot or serial number in a stocktake line. | Developer | REQ_F4115.7.1, REQ_F4115.7.2, REQ_F4115.7.3, REQ_F4115.7.4, REQ_F4115.7.5 |

對應 BlueWhale REQ_DS43.42 Stocktake。舊系統 `pandianItem` 以 `decimal(18,2)` 存帳面、實盤與差異；MyERP 只存帳面與實盤，差異在查詢時計算，避免三欄不一致。

`inv_stocktake` 沿用庫存單據的審計欄位：`company_id`、建立／修改人與時間、`version`、軟刪除欄位。其他欄位：
- `document_no varchar(30)`、`business_date date`、`status varchar(30)`。
- `scope_json nvarchar(max)`：倉庫、類別、物料 ID 陣列，CHECK `ISJSON`。
- `snapshot_at datetime2(6)`。
- `snapshot_movement_id bigint NULL`：快照時公司內最大的異動 ID；公司沒有異動時為 NULL。
- `remark nvarchar(1000)`、`custom_fields nvarchar(max)`、`posted_at datetime2(6)`。

CHECK 狀態與過帳時間、軟刪除的組合，以及 JSON 格式。`(company_id, document_no)` 永久唯一；`(company_id, business_date, id)` 與 `(company_id, status, id)` 供列表篩選。

`inv_stocktake_line` 含 `stocktake_id`、`line_no`、`item_id`、`warehouse_id`、`location_id` 及審計欄位，另有：
- `book_quantity decimal(19,6)`：大於等於 0。
- `counted_quantity decimal(19,6) NULL`：未盤為 NULL，否則大於等於 0。
- `is_added bit`：實地新增的行，帳面必須為 0。
- `recount_required bit`：重新快照時帳面有變。

約束與索引：
- CHECK 行號為正數、數量非負、新增行帳面為 0。
- `(company_id, stocktake_id)` 複合 FK 指向表頭，Cascade 刪草稿。
- `(company_id, stocktake_id, line_no)` 唯一；`(company_id, stocktake_id, item_id, warehouse_id, location_id)` 唯一（NULL 儲位視為同一鍵）。
- `(company_id, item_id, warehouse_id)` 索引，供範圍互斥檢查。
- 跨模組 MD ID 不建 FK。

`inv_stocktake_line_tracking` 含 `line_id`、`seq_no`、`lot_no nvarchar(50)` 或 `serial_no nvarchar(100)`（擇一）、`book_quantity decimal(19,6)`、`counted_quantity decimal(19,6) NULL` 及審計欄位。
- 序號的帳面與實盤只能是 0 或 1。
- `(company_id, line_id)` 複合 FK 指向明細，Cascade。
- `(company_id, line_id, seq_no)` 唯一；同一行內批號、序號各以篩選唯一索引防止重複（按 ADR-060 正規化後的值）。
- 送審時，子表帳面合計須等於行帳面、實盤合計須等於行實盤。
- 核准後不再修改。

```mermaid
erDiagram
    INV_STOCKTAKE ||--|{ INV_STOCKTAKE_LINE : counts
    INV_STOCKTAKE_LINE ||--o{ INV_STOCKTAKE_LINE_TRACKING : details
    INV_STOCKTAKE ||--o{ STOCK_MOVEMENT : "posts gain/loss"
    INV_STOCKTAKE {
        bigint id PK
        bigint company_id
        string document_no
        date business_date
        string status
        string scope_json
        datetime snapshot_at
        bigint snapshot_movement_id
    }
    INV_STOCKTAKE_LINE {
        bigint id PK
        bigint stocktake_id FK
        bigint item_id
        bigint warehouse_id
        bigint location_id
        decimal book_quantity
        decimal counted_quantity
        bool is_added
        bool recount_required
    }
    INV_STOCKTAKE_LINE_TRACKING {
        bigint id PK
        bigint line_id FK
        string lot_no
        string serial_no
        decimal book_quantity
        decimal counted_quantity
    }
```

## WU-INV-009a 庫存調撥（`Inventory_Transfer` migration 已套用 myerp）

| Requirement ID | Version | Table Name | Description | Author | Related REQ_F |
|---|---|---|---|---|---|
| REQ_DS43.148 | 1.0 | inv_transfer | This table stores company-scoped inventory transfer headers and their approval and posting state. | Developer | REQ_F4115.6.1, REQ_F4115.6.2, REQ_F4115.6.3, REQ_F4115.6.4, REQ_F4115.6.5, REQ_F4115.6.6 |
| REQ_DS43.149 | 1.0 | inv_transfer_line | This table stores one item, source and destination stock key and equal transfer quantity per line. | Developer | REQ_F4115.6.1, REQ_F4115.6.2, REQ_F4115.6.3, REQ_F4115.6.4 |
| REQ_DS43.150 | 1.0 | inv_transfer_line_tracking | This table stores the lot or serial numbers carried unchanged from source to destination. | Developer | REQ_F4115.6.1, REQ_F4115.6.2, REQ_F4115.6.3, REQ_F4115.6.4 |

`inv_transfer` 沿用庫存單據審計欄位：`company_id`、建立／修改人與時間、`version`、`document_no varchar(30)`、`business_date date`、`status varchar(30)`、`remark nvarchar(500)`、`custom_fields nvarchar(max)`、`posted_at datetime2(6)`、軟刪除欄位；CHECK 狀態與過帳時間、軟刪除的組合及 JSON。`(company_id, document_no)` 永久唯一；`(company_id, business_date, id)` 與 `(company_id, status, id)` 供列表篩選。

`inv_transfer_line` 含 `transfer_id`、`line_no`、`item_id`、`source_warehouse_id`、`source_location_id`、`destination_warehouse_id`、`destination_location_id`、`quantity decimal(19,6)` 及審計欄位；CHECK 行號／數量正數、來源與目的的倉庫加儲位組合不同（含 NULL 儲位）。`(company_id, transfer_id)` 複合 FK 指向表頭、Cascade 刪草稿；`(company_id, transfer_id, line_no)` 唯一。來源及目的各有 `(company_id, warehouse_id, location_id)` 索引，物料有 `(company_id, item_id)` 索引；跨模組 MD ID 不建 FK。

`inv_transfer_line_tracking` 含 `line_id`、`seq_no`、`lot_no nvarchar(50)` 或 `serial_no nvarchar(100)`、`quantity decimal(19,6)` 及審計欄位；批號／序號擇一，序號數量必須為 1。`(company_id, line_id)` 複合 FK 指向明細、Cascade；`(company_id, line_id, seq_no)` 唯一並支援按序載入。草稿整組替換明細時子表一起替換；核准後不修改。

```mermaid
erDiagram
    INV_TRANSFER ||--|{ INV_TRANSFER_LINE : contains
    INV_TRANSFER_LINE ||--o{ INV_TRANSFER_LINE_TRACKING : carries
    INV_TRANSFER ||--o{ STOCK_MOVEMENT : posts
    INV_TRANSFER {
        bigint id PK
        bigint company_id
        string document_no
        date business_date
        string status
    }
    INV_TRANSFER_LINE {
        bigint id PK
        bigint transfer_id FK
        bigint item_id
        bigint source_warehouse_id
        bigint source_location_id
        bigint destination_warehouse_id
        bigint destination_location_id
        decimal quantity
    }
    INV_TRANSFER_LINE_TRACKING {
        bigint id PK
        bigint line_id FK
        string lot_no
        string serial_no
        decimal quantity
    }
```

## WU-INV-008b 批號與序號（migration `Inventory_LotSerial`）

| Requirement ID | Version | Table Name | Description | Author | Related REQ_F |
|---|---|---|---|---|---|
| REQ_DS43.142 | 1.0 | inv_lot | This table stores company-scoped lot numbers per item with optional manufacture and expiry dates. | Developer | REQ_F4115.24.1, REQ_F4115.24.3, REQ_F4115.24.5, REQ_F4115.24.6, REQ_F4115.22.4, REQ_F4115.22.5 |
| REQ_DS43.143 | 1.0 | inv_serial | This table stores company-scoped serial numbers per item, each representing one base unit. | Developer | REQ_F4115.24.2, REQ_F4115.24.3, REQ_F4115.24.5, REQ_F4115.24.6, REQ_F4115.22.4, REQ_F4115.22.5 |
| REQ_DS43.144 | 1.0 | inv_stock_movement_tracking | This table stores append-only lot or serial quantities of each stock movement. | Developer | REQ_F4115.24.1, REQ_F4115.24.2, REQ_F4115.24.3, REQ_F4115.24.4, REQ_F4115.24.5, REQ_F4115.24.6, REQ_F4115.22.4, REQ_F4115.22.5 |
| REQ_DS43.145 | 1.0 | inv_other_receipt_line_tracking | This table stores the lot or serial numbers entered on other receipt lines. | Developer | REQ_F4115.24.1, REQ_F4115.24.2, REQ_F4115.24.4 |
| REQ_DS43.146 | 1.0 | inv_other_issue_line_tracking | This table stores the lot or serial numbers selected on other issue lines. | Developer | REQ_F4115.24.3, REQ_F4115.24.4 |
| REQ_DS43.147 | 1.0 | inv_opening_stock_line_tracking | This table stores the lot or serial numbers of product initial stock lines. | Developer | REQ_F4115.24.1, REQ_F4115.24.2, REQ_F4115.24.4 |

`inv_lot`：`lot_no nvarchar(50)`，`(company_id, item_id, lot_no)` 唯一（資料庫定序不分大小寫），可選 `manufacture_date`／`expiry_date date`，CHECK 有效期限不早於生產日期、批號非空；不存數量。`inv_serial`：`serial_no nvarchar(100)`，`(company_id, item_id, serial_no)` 唯一；不存在庫狀態。兩表的物料為 MD 跨模組 ID，不建 FK；另有 `(company_id, id)` 替代鍵供同公司複合 FK。

`inv_stock_movement_tracking`：`(company_id, movement_id)` 複合 FK 指向 `inv_stock_movement`，`(company_id, lot_id)`／`(company_id, serial_id)` 複合 FK 指向批號／序號（皆 Restrict）；CHECK 批號與序號擇一、數量為正、序號數量為 1；`trg_inv_stock_movement_tracking_immutable` 拒絕 UPDATE／DELETE。索引：`(company_id, movement_id)`；`(company_id, lot_id) INCLUDE (movement_id, quantity)` 與 `(company_id, serial_id) INCLUDE (movement_id, quantity)` 供結存核對與追溯。批序號結存 = 明細數量依所屬異動方向加總，按異動的倉庫／儲位分組。

三張單據批序號子表結構相同：`(company_id, line_id)` 複合 FK 指向各自明細（其他入庫／出庫 Cascade，期初 Restrict 因期初不可刪），`(company_id, line_id, seq_no)` 唯一保留輸入順序；`lot_no`／`serial_no` 擇一，CHECK 數量為正、序號數量 1；入庫與期初另有生產日期／有效期限（序號不可帶日期），出庫子表沒有日期欄。草稿整組替換明細時子表一併替換。

```mermaid
erDiagram
    STOCK_MOVEMENT ||--o{ INV_STOCK_MOVEMENT_TRACKING : details
    INV_LOT ||--o{ INV_STOCK_MOVEMENT_TRACKING : lot
    INV_SERIAL ||--o{ INV_STOCK_MOVEMENT_TRACKING : serial
    INV_OTHER_RECEIPT_LINE ||--o{ INV_OTHER_RECEIPT_LINE_TRACKING : captures
    INV_OTHER_ISSUE_LINE ||--o{ INV_OTHER_ISSUE_LINE_TRACKING : selects
    INV_OPENING_STOCK_LINE ||--o{ INV_OPENING_STOCK_LINE_TRACKING : captures
    INV_LOT {
        bigint id PK
        bigint company_id
        bigint item_id
        string lot_no
        date manufacture_date
        date expiry_date
    }
    INV_SERIAL {
        bigint id PK
        bigint company_id
        bigint item_id
        string serial_no
    }
    INV_STOCK_MOVEMENT_TRACKING {
        bigint id PK
        bigint company_id
        bigint movement_id FK
        bigint lot_id FK
        bigint serial_id FK
        decimal quantity
    }
    INV_OTHER_RECEIPT_LINE_TRACKING {
        bigint id PK
        bigint company_id
        bigint line_id FK
        int seq_no
        string lot_no
        string serial_no
        decimal quantity
        date manufacture_date
        date expiry_date
    }
```

## WU-INV-007a 物料期初庫存

| Requirement ID | Version | Table Name | Description | Author | Related REQ_F |
|---|---|---|---|---|---|
| REQ_DS43.140 | 1.0 | inv_opening_stock | This table stores immutable product initial stock batches with the idempotent request key, opening date and posting time. | Developer | REQ_F4115.23.1, REQ_F4115.23.3, REQ_F4115.23.4 |
| REQ_DS43.141 | 1.0 | inv_opening_stock_line | This table stores one opening quantity and unit cost per item, warehouse and optional location. | Developer | REQ_F4115.23.1, REQ_F4115.23.2, REQ_F4115.23.3, REQ_F4115.23.4 |

`inv_opening_stock` 含通用審計、版本與 `custom_fields`（ISJSON 檢查）；`(company_id, request_key)` 唯一，`request_key varchar(64)`；`(company_id, opening_date, id)` 支援列表排序。`inv_opening_stock_line` 以 `(company_id, opening_stock_id)` 複合 FK 指向表頭，`(company_id, opening_stock_id, line_no)` 唯一；`ux_inv_opening_stock_line_balance_key`（`company_id, item_id, warehouse_id, location_id`）不帶篩選條件的唯一索引，令每個結存鍵（含無儲位）只有一筆期初；另建公司／倉庫／儲位索引。`quantity`、`unit_cost decimal(19,6)`，`amount decimal(19,4)`；CHECK 保證數量為正、單價與金額非負、行號為正。物料、倉庫、儲位為跨模組 ID，不建 FK。批次以 `source_type = OPENING_STOCK`、`source_id = id` 寫入 `inv_stock_movement`，MD 不存數量。

```mermaid
erDiagram
    INV_OPENING_STOCK ||--|{ INV_OPENING_STOCK_LINE : contains
    INV_OPENING_STOCK ||--o{ STOCK_MOVEMENT : posts
    INV_OPENING_STOCK {
        bigint id PK
        bigint company_id
        string request_key
        date opening_date
        string remark
        datetime posted_at
    }
    INV_OPENING_STOCK_LINE {
        bigint id PK
        bigint company_id
        bigint opening_stock_id FK
        int line_no
        bigint item_id
        bigint warehouse_id
        bigint location_id
        decimal quantity
        decimal unit_cost
        decimal amount
    }
```

WU-INV-006 查詢沿用 `inv_stock_movement`（REQ_DS43.135）與其公司／結存鍵、公司／業務日期索引，不新增資料表或可修改彙總。結存按物料、倉庫、儲位分組；明細按業務日期及 ID 穩定分頁。對應 REQ_F4115.1.1–2。

WU-INV-002 新增 `inv_reference`；WU-INV-003 新增 `inv_stock_movement`。所有交易表含 `company_id`，異動帳只增不改，來源行唯一，數量 `decimal(19,6)`。詳細約束見 ADR-051、ADR-053。

| Requirement ID | Version | Table Name | Description | Author | Related REQ_F |
|---|---|---|---|---|---|
| REQ_DS43.134 | 1.0 | inv_reference | This table stores immutable inventory source references to master items, warehouses and optional locations. | Developer | REQ_F4115.21.3 |
| REQ_DS43.135 | 1.0 | inv_stock_movement | This table stores append-only inventory receipts and issues from approved source documents. | Developer | REQ_F4115.22.1, REQ_F4115.22.2, REQ_F4115.22.3, REQ_F4115.22.4, REQ_F4115.22.5, REQ_F4115.1.1, REQ_F4115.1.2, REQ_F4115.25.1, REQ_F4115.25.2, REQ_F4115.25.3 |
| REQ_DS43.136 | 1.0 | inv_other_receipt | This table stores company-scoped other receipt headers, document numbers and approval state. | Developer | REQ_F4115.2.1, REQ_F4115.2.2, REQ_F4115.2.3, REQ_F4115.2.4, REQ_F4115.2.5, REQ_F4115.2.6 |
| REQ_DS43.137 | 1.0 | inv_other_receipt_line | This table stores the ordered item, warehouse, optional location, quantity and cost snapshot of each other receipt. | Developer | REQ_F4115.2.1, REQ_F4115.2.2, REQ_F4115.2.3, REQ_F4115.2.4 |
| REQ_DS43.138 | 1.0 | inv_other_issue | This table stores company-scoped other issue headers, document numbers and approval state. | Developer | REQ_F4115.3.1, REQ_F4115.3.2, REQ_F4115.3.3, REQ_F4115.3.4, REQ_F4115.3.5, REQ_F4115.3.6 |
| REQ_DS43.139 | 1.0 | inv_other_issue_line | This table stores ordered stock issue lines with quantity and estimated approval amount. | Developer | REQ_F4115.3.1, REQ_F4115.3.2, REQ_F4115.3.3, REQ_F4115.3.4 |

`inv_reference` 的每個來源鍵在公司內唯一；物料、倉庫、儲位 ID 與代碼快照在 guard 保護下同時寫入。`company_id + item_code`、`company_id + warehouse_code + location_code` 索引用於 MD Usage 判斷；各引用 ID 亦有公司複合索引。跨模組不建 FK。引用保留歷史，即使來源單據作廢亦不可刪除。`location_id` 與 `location_code` 必須同時為空或同時有值。通用審計欄位由 interceptor 寫入。

```mermaid
erDiagram
    INVENTORY_REFERENCE {
        bigint company_id
        string source_key
        bigint item_id
        string item_code
        bigint warehouse_id
        string warehouse_code
        bigint location_id
        string location_code
    }
    INVENTORY_DOCUMENT ||--o{ INVENTORY_REFERENCE : claims
    INVENTORY_REFERENCE }o--|| ITEM : references
    INVENTORY_REFERENCE }o--|| WAREHOUSE : references
    INVENTORY_REFERENCE }o--o| STORAGE_LOCATION : references
    INVENTORY_DOCUMENT ||--o{ STOCK_MOVEMENT : posts
    INVENTORY_DOCUMENT {
        bigint company_id
        string status
    }
    STOCK_MOVEMENT {
        bigint company_id
        string source_type
        bigint source_id
        int source_line_no
        string direction
        bigint reference_id
        bigint item_id
        bigint warehouse_id
        bigint location_id
        decimal quantity
        decimal unit_cost
        date business_date
    }
    INVENTORY_REFERENCE ||--o| STOCK_MOVEMENT : supports
```

`STOCK_MOVEMENT` 已落實；圖中的 `INVENTORY_DOCUMENT` 是後續單據概念，未建表。跨模組線為應用層參照，不是 SQL 外鍵。異動帳以 `(company_id, reference_id)` 複合外鍵連到引用，來源行以 `(company_id, source_type, source_id, source_line_no, direction)` 唯一。結存索引 `(company_id, item_id, warehouse_id, location_id)` INCLUDE `direction, quantity`；另有公司與業務日期、倉庫與儲位索引。`quantity > 0`、方向及成本 CHECK 防止非法列；trigger 拒絕 UPDATE、DELETE。

## 其他入庫（WU-INV-004a，migration `Inventory_OtherReceipt`）

`inv_other_receipt`：`id`、`company_id`、`document_no varchar(30)`、`business_date date`、`status varchar(30)`、`remark nvarchar(500)`、`custom_fields nvarchar(max)`（ISJSON 檢查）、`posted_at datetime2(6)`、`is_deleted bit`、`deleted_at datetime2(6)`，以及共同審計／版本欄位。`(company_id, document_no)` 唯一（含已刪草稿，單號不重用）；`(company_id, business_date, id)` 及 `(company_id, status, id)` 支援列表篩選與排序；`(company_id, id)` 為明細複合 FK 的備用鍵。CHECK：狀態只可為 `Draft`／`PendingApproval`／`Rejected`／`Posted`；`posted_at` 只在 `Posted` 有值；`is_deleted` 與 `deleted_at` 一致且只有草稿可刪（ADR-055）。

`inv_other_receipt_line`：`id`、`company_id`、`receipt_id`、`line_no`、`item_id`、`warehouse_id`、`location_id`、`quantity decimal(19,6)`、`unit_cost decimal(19,6)`、`amount decimal(19,4)` 與共同審計／版本欄位。`(company_id, receipt_id)` 以複合 FK 指向表頭（同公司）；唯一索引 `(company_id, receipt_id, line_no)` 同時覆蓋該 FK。`quantity > 0`、`unit_cost >= 0`、`amount >= 0`、`line_no > 0` 由 CHECK 保護；物料、倉庫、儲位跨模組只存 ID，不建 SQL FK。`(company_id, item_id)`、`(company_id, warehouse_id, location_id)` 索引用於查詢與引用判斷。`amount` = 數量 × 單價取 4 位，表頭金額為明細加總。

```mermaid
erDiagram
    INV_OTHER_RECEIPT ||--|{ INV_OTHER_RECEIPT_LINE : contains
    INV_OTHER_RECEIPT ||--o{ STOCK_MOVEMENT : posts
    INV_OTHER_RECEIPT {
        bigint id PK
        bigint company_id
        string document_no
        date business_date
        string status
        string remark
        datetime posted_at
        bool is_deleted
        datetime deleted_at
    }
    INV_OTHER_RECEIPT_LINE {
        bigint id PK
        bigint company_id
        bigint receipt_id FK
        int line_no
        bigint item_id
        bigint warehouse_id
        bigint location_id
        decimal quantity
        decimal unit_cost
        decimal amount
    }
```

圖中的過帳關聯由 `source_type = OTHER_RECEIPT`、`source_id = receipt.id` 與來源行號實現，不另建異動帳到表頭的 SQL FK。

## 其他出庫（WU-INV-005a，migration `Inventory_OtherIssue`）

`inv_other_issue` 與入庫表頭相同的審計、版本、軟刪除及狀態約束；公司內單號永久唯一，日期與狀態各有公司複合索引。`inv_other_issue_line` 以 `(company_id, issue_id)` 複合 FK 指向表頭，`(company_id, issue_id, line_no)` 唯一；物料、倉庫及儲位只存跨模組 ID，另建公司與物料、公司與倉庫／儲位索引。`quantity decimal(19,6) > 0`；`unit_cost decimal(19,6) >= 0` 與 `amount decimal(19,4) >= 0` 是審批用估算快照，異動帳出庫 `unit_cost` 為 null，成本估值留 WU-INV-014。CHECK 保證行號、JSON、狀態及過帳時間合法。

```mermaid
erDiagram
    INV_OTHER_ISSUE ||--|{ INV_OTHER_ISSUE_LINE : contains
    INV_OTHER_ISSUE ||--o{ STOCK_MOVEMENT : posts
    INV_OTHER_ISSUE {
        bigint id PK
        bigint company_id
        string document_no
        date business_date
        string status
        datetime posted_at
    }
    INV_OTHER_ISSUE_LINE {
        bigint id PK
        bigint company_id
        bigint issue_id FK
        int line_no
        bigint item_id
        bigint warehouse_id
        bigint location_id
        decimal quantity
        decimal unit_cost
        decimal amount
    }
```

異動帳以 `source_type = OTHER_ISSUE`、`source_id = issue.id` 連回來源，不建 SQL FK；同公司與來源行的防重放由過帳服務保證。
