# MD 資料結構

WU-MD-001 為模組與權限基線，沒有新增資料表或 migration。後續主檔表均須有 `company_id`、審計與版本欄位，並採用共用 EF 公司篩選；相關工作單元建立實體時補入 ER 圖與 `REQ_DS43`。

```mermaid
erDiagram
    SYS_COMPANY ||--o{ MD_MASTER_RECORD : owns
```

`MD_MASTER_RECORD` 為後續主檔的概念佔位，並非本輪建立的實體或表。

## WU-MD-002

| Requirement ID | Version | Table Name | Description | Author | Related REQ_F |
|---|---|---|---|---|---|
| REQ_DS43.105 | 1.0 | md_customer_category | This table stores customer categories within a company. | Developer | REQ_F4118.8.1, REQ_F4118.8.2, REQ_F4118.8.3, REQ_F4118.8.4 |

`id` bigint identity 主鍵；`company_id` bigint 非空；`code` varchar(30) 非空、公司內唯一（含軟刪除）；`name` nvarchar(200) 非空；`is_active`、`is_deleted` bit；`custom_fields` nvarchar(max) 可空且須為 JSON；審計欄位及 `version` 遵循第 6 節。唯一索引 `(company_id, code)` 同時支援公司查詢；列表索引 `(company_id, name)` 支援名稱排序。`company_id` 是 SYS 公司 ID，但不跨模組建 navigation 或 FK，授權與存在性由應用層保證。

```mermaid
erDiagram
    SYS_COMPANY ||--o{ MD_CUSTOMER_CATEGORY : owns_by_company_id
    MD_CUSTOMER_CATEGORY {
        bigint id PK
        bigint company_id
        varchar code
        nvarchar name
        bool is_active
        bool is_deleted
        int version
    }
```

## WU-MD-003

| Requirement ID | Version | Table Name | Description | Author | Related REQ_F |
|---|---|---|---|---|---|
| REQ_DS43.106 | 1.0 | md_customer | This table stores customer master records within a company. | Developer | REQ_F4118.1.1, REQ_F4118.1.2, REQ_F4118.1.3 |

`md_customer` 使用 `id` bigint identity 主鍵、`company_id` bigint、`code` varchar(30)、`name` nvarchar(200)、可空 `category_id`、`is_active`／`is_deleted`、`version`、審計欄位與可空且檢查 JSON 的 `custom_fields`。唯一索引 `(company_id, code)` 包含軟刪除資料；分類以 `(company_id, category_id)` 複合外鍵參照 `md_customer_category` 的 `(company_id, id)`，阻止跨公司及刪除已引用分類。未建跨模組 SYS FK。

```mermaid
erDiagram
    SYS_COMPANY ||--o{ MD_CUSTOMER : owns_by_company_id
    MD_CUSTOMER_CATEGORY o|--o{ MD_CUSTOMER : classifies_in_same_company
    MD_CUSTOMER {
        bigint id PK
        bigint company_id
        varchar code
        nvarchar name
        bigint category_id FK
        bool is_active
        bool is_deleted
        int version
    }
```

## WU-MD-006

| Requirement ID | Version | Table Name | Description | Author | Related REQ_F |
|---|---|---|---|---|---|
| REQ_DS43.107 | 1.0 | md_supplier_category | This table stores supplier categories within a company. | Developer | REQ_F4118.9.1, REQ_F4118.9.2, REQ_F4118.9.3, REQ_F4118.9.4 |

欄位與 `md_customer_category` 相同（BlueWhale VendorType，REQ_DS43.69）：`id` bigint identity 主鍵；`company_id` bigint；`code` varchar(30)，公司內唯一（含軟刪除）；`name` nvarchar(200)；`is_active`、`is_deleted`；可空且檢查 JSON 的 `custom_fields`；審計欄位及 `version`。唯一索引 `ux_md_supplier_category_company_id_code`、名稱排序索引 `ix_md_supplier_category_company_id_name`。另建唯一約束 `ak_md_supplier_category_company_id_id`，供 WU-MD-007 的 `md_supplier` 以 `(company_id, category_id)` 複合外鍵參照，在資料庫層阻止跨公司分類。兩種分類共用 EF 設定 `MasterCategoryModel` 與領域基底 `MasterCategory`。

```mermaid
erDiagram
    SYS_COMPANY ||--o{ MD_SUPPLIER_CATEGORY : owns_by_company_id
    MD_SUPPLIER_CATEGORY {
        bigint id PK
        bigint company_id
        varchar code
        nvarchar name
        bool is_active
        bool is_deleted
        int version
    }
```

## WU-MD-007

| Requirement ID | Version | Table Name | Description | Author | Related REQ_F |
|---|---|---|---|---|---|
| REQ_DS43.108 | 1.0 | md_supplier | This table stores supplier master records within a company. | Developer | REQ_F4118.2.1, REQ_F4118.2.2, REQ_F4118.2.3 |

欄位與 `md_customer` 相同（BlueWhale Vendor，REQ_DS43.72）：`id` bigint identity 主鍵、`company_id` bigint、`code` varchar(30)、`name` nvarchar(200)、可空 `category_id`、`is_active`／`is_deleted`、`version`、審計欄位與可空且檢查 JSON 的 `custom_fields`。唯一索引 `ux_md_supplier_company_id_code`（含軟刪除）、名稱索引 `ix_md_supplier_company_id_name`；分類以複合外鍵 `fk_md_supplier_md_supplier_category_company_id_category_id`（`(company_id, category_id)` → `ak_md_supplier_category_company_id_id`）參照，阻止跨公司及刪除已引用分類。客戶與供應商共用領域基底 `BusinessPartner<TCategory>` 及 EF 設定 `BusinessPartnerModel`；migration `MasterData_Supplier` 只新增此表，`md_customer` 結構不變。

```mermaid
erDiagram
    SYS_COMPANY ||--o{ MD_SUPPLIER : owns_by_company_id
    MD_SUPPLIER_CATEGORY o|--o{ MD_SUPPLIER : classifies_in_same_company
    MD_SUPPLIER {
        bigint id PK
        bigint company_id
        varchar code
        nvarchar name
        bigint category_id FK
        bool is_active
        bool is_deleted
        int version
    }
```

## WU-MD-010

| Requirement ID | Version | Table Name | Description | Author | Related REQ_F |
|---|---|---|---|---|---|
| REQ_DS43.109 | 1.0 | md_uom | This table stores units of measurement within a company. | Developer | REQ_F4118.13.1, REQ_F4118.13.2, REQ_F4118.13.3, REQ_F4118.13.4 |
| REQ_DS43.110 | 1.0 | md_uom_conversion | This table stores conversion factors between units of measurement within a company. | Developer | REQ_F4118.13.1, REQ_F4118.13.2, REQ_F4118.13.3, REQ_F4118.13.4 |

`md_uom`（BlueWhale unit，REQ_DS43.85）沿用分類欄位（`id`、`company_id`、`code` varchar(30)、`name` nvarchar(200)、`is_active`／`is_deleted`、`version`、審計欄位、檢查 JSON 的 `custom_fields`），另加 `decimal_places` int（檢查 `ck_md_uom_decimal_places`：0–6）。唯一索引 `ux_md_uom_company_id_code`（含軟刪除）、名稱索引；備用鍵 `ak_md_uom_company_id_id` 供換算複合外鍵。

`md_uom_conversion`（MyERP 新增，BlueWhale 無對應）：`id` bigint identity、`company_id`、`from_uom_id`、`to_uom_id`、`factor` decimal(15,6)、`is_deleted`、`version`、審計欄位。檢查 `ck_md_uom_conversion_factor`（大於 0）與 `ck_md_uom_conversion_units`（兩端不同）；過濾唯一索引 `ux_md_uom_conversion_company_id_from_uom_id_to_uom_id`（`is_deleted = 0`）；兩條複合外鍵 `(company_id, from_uom_id)`／`(company_id, to_uom_id)` 參照 `ak_md_uom_company_id_id`，阻止跨公司單位。反向、循環換算由服務以換算圖及 applock 阻止（ADR-034）。migration `MasterData_Uom`。

```mermaid
erDiagram
    SYS_COMPANY ||--o{ MD_UOM : owns_by_company_id
    MD_UOM ||--o{ MD_UOM_CONVERSION : from_in_same_company
    MD_UOM ||--o{ MD_UOM_CONVERSION : to_in_same_company
    MD_UOM {
        bigint id PK
        bigint company_id
        varchar code
        nvarchar name
        int decimal_places
        bool is_active
        bool is_deleted
        int version
    }
    MD_UOM_CONVERSION {
        bigint id PK
        bigint company_id
        bigint from_uom_id FK
        bigint to_uom_id FK
        decimal factor
        bool is_deleted
        int version
    }
```

## WU-MD-011

| Requirement ID | Version | Table Name | Description | Author | Related REQ_F |
|---|---|---|---|---|---|
| REQ_DS43.111 | 1.0 | md_item_brand | This table stores item brands within a company. | Developer | REQ_F4118.11.1, REQ_F4118.11.2, REQ_F4118.11.3, REQ_F4118.11.4 |
| REQ_DS43.112 | 1.0 | md_item_category | This table stores the item category tree within a company. | Developer | REQ_F4118.10.1, REQ_F4118.10.2, REQ_F4118.10.3, REQ_F4118.10.4 |

`md_item_brand`（BlueWhale Brand REQ_DS43.3／GoodsBrand REQ_DS43.52，兩表重複，合併為一張）沿用分類欄位（`id`、`company_id`、`code` varchar(30)、`name` nvarchar(200)、`is_active`／`is_deleted`、`version`、審計欄位、檢查 JSON 的 `custom_fields`）；唯一索引 `ux_md_item_brand_company_id_code`（含軟刪除）、名稱索引；備用鍵 `ak_md_item_brand_company_id_id` 供物料（WU-MD-012）複合外鍵。

`md_item_category`（BlueWhale GoodsType，REQ_DS43.44）沿用分類欄位，另加 `parent_id` bigint null。複合外鍵 `(company_id, parent_id)` 參照自身備用鍵 `ak_md_item_category_company_id_id`（NO ACTION），資料庫層阻止跨公司掛載；檢查 `ck_md_item_category_parent`（`parent_id <> id`）；索引 `ix_md_item_category_company_id_parent_id`。不可成環、最多 5 層、上層須啟用、有子分類不可刪除由服務以公司級 applock（`md_item_category:{companyId}`）序列化檢查。分類圖片（REQ_DS43.39）留給 WU-MD-013。migration `MasterData_ItemBrandCategory`。

```mermaid
erDiagram
    SYS_COMPANY ||--o{ MD_ITEM_BRAND : owns_by_company_id
    SYS_COMPANY ||--o{ MD_ITEM_CATEGORY : owns_by_company_id
    MD_ITEM_CATEGORY ||--o{ MD_ITEM_CATEGORY : parent_in_same_company
    MD_ITEM_BRAND {
        bigint id PK
        bigint company_id
        varchar code
        nvarchar name
        bool is_active
        bool is_deleted
        int version
    }
    MD_ITEM_CATEGORY {
        bigint id PK
        bigint company_id
        bigint parent_id FK
        varchar code
        nvarchar name
        bool is_active
        bool is_deleted
        int version
    }
```

## WU-MD-012a 物料主檔

| Requirement ID | Version | Table Name | Description | Author | Related REQ_F |
|---|---|---|---|---|---|
| REQ_DS43.113 | 1.0 | md_item | This table stores company item masters without inventory balances. | Developer | REQ_F4118.3.1, REQ_F4118.3.2, REQ_F4118.3.3, REQ_F4118.3.5 |

`md_item`：`id` bigint identity；`company_id` bigint；`code` varchar(30) 公司內永久唯一（含軟刪除）；`name` nvarchar(200)；`specification` nvarchar(500) 可空；`base_uom_id` bigint 必填；`brand_id`／`category_id` bigint 可空；`is_active`／`is_deleted`、`version` 與審計欄位。索引 `(company_id, code)` 唯一、`(company_id, name)` 供列表排序；基本單位、品牌、分類均以 `(company_id, id)` 複合外鍵參照，阻止跨公司參照。migration `MasterData_Item`。不存庫存數量或成本。

```mermaid
erDiagram
    MD_UOM ||--o{ MD_ITEM : base_uom_in_company
    MD_ITEM_BRAND |o--o{ MD_ITEM : brand_in_company
    MD_ITEM_CATEGORY |o--o{ MD_ITEM : category_in_company
    MD_ITEM {
        bigint id PK
        bigint company_id
        varchar code
        nvarchar name
        nvarchar specification
        bigint base_uom_id FK
        bigint brand_id FK
        bigint category_id FK
        bool is_active
        int version
    }
```

## WU-MD-012c 物料專屬換算

| Requirement ID | Version | Table Name | Description | Author | Related REQ_F |
|---|---|---|---|---|---|
| REQ_DS43.114 | 1.0 | md_item_uom_conversion | This table stores item-specific conversion factors from alternate units to the item base unit within a company. | Developer | REQ_F4118.3.5 |

`md_item_uom_conversion`：`id` bigint identity；`company_id` bigint；`item_id` bigint；`uom_id` bigint（換算單位，不可等於物料基本單位，由服務檢查）；`factor` decimal(15,6)（1 換算單位 = factor 基本單位，check `factor > 0`）；`is_deleted`、`version` 與審計欄位。過濾唯一索引 `ux_md_item_uom_conversion_company_id_item_id_uom_id`（`is_deleted = 0`），索引 `(company_id, uom_id)` 供單位刪除檢查；物料與單位均以 `(company_id, id)` 複合外鍵參照（`md_item` 新增唯一約束 `ak_md_item_company_id_id`），阻止跨公司。migration `MasterData_ItemUomConversion`。決策見 ADR-035。

```mermaid
erDiagram
    MD_ITEM ||--o{ MD_ITEM_UOM_CONVERSION : item_in_company
    MD_UOM ||--o{ MD_ITEM_UOM_CONVERSION : unit_in_company
    MD_ITEM_UOM_CONVERSION {
        bigint id PK
        bigint company_id
        bigint item_id FK
        bigint uom_id FK
        decimal factor
        bool is_deleted
        int version
    }
```

## WU-MD-015

| Requirement ID | Version | Table Name | Description | Author | Related REQ_F |
|---|---|---|---|---|---|
| REQ_DS43.115 | 1.0 | md_warehouse | This table stores warehouses within a company. | Developer | REQ_F4118.4.1, REQ_F4118.4.2, REQ_F4118.4.3, REQ_F4118.4.4 |
| REQ_DS43.116 | 1.0 | md_storage_location | This table stores storage locations (bins) within a warehouse. | Developer | REQ_F4118.4.5 |

`md_warehouse` 沿用分類基底欄位（`code` varchar(30) 公司內唯一含軟刪除、`name` nvarchar(200)、`custom_fields` JSON 檢查），另加可空 `address` nvarchar(300)、`contact_name` nvarchar(100)、`phone` nvarchar(50)；唯一約束 `ak_md_warehouse_company_id_id (company_id, id)` 供儲位複合外鍵。`md_storage_location` 有 `warehouse_id` bigint、`code` varchar(30)、`name` nvarchar(200)、啟用、軟刪除、版本與審計欄位；唯一索引 `(company_id, warehouse_id, code)` 包含軟刪除資料，同時支援按倉庫查詢；`(company_id, warehouse_id)` 複合外鍵 `fk_md_storage_location_md_warehouse_company_id_warehouse_id` 阻止跨公司掛載。兩表都沒有數量或金額欄位，庫存只由 INV 異動帳維護（ADR-036）。

```mermaid
erDiagram
    SYS_COMPANY ||--o{ MD_WAREHOUSE : owns_by_company_id
    MD_WAREHOUSE ||--o{ MD_STORAGE_LOCATION : contains_in_same_company
    MD_WAREHOUSE {
        bigint id PK
        bigint company_id
        varchar code
        nvarchar name
        nvarchar address
        nvarchar contact_name
        nvarchar phone
        bool is_active
        bool is_deleted
        int version
    }
    MD_STORAGE_LOCATION {
        bigint id PK
        bigint company_id
        bigint warehouse_id FK
        varchar code
        nvarchar name
        bool is_active
        bool is_deleted
        int version
    }
```

## WU-MD-016

| Requirement ID | Version | Table Name | Description | Author | Related REQ_F |
|---|---|---|---|---|---|
| REQ_DS43.117 | 1.0 | md_currency | This table stores currencies and the base currency of a company. | Developer | REQ_F4118.35.1, REQ_F4118.35.2, REQ_F4118.35.3, REQ_F4118.35.4 |
| REQ_DS43.118 | 1.0 | md_exchange_rate | This table stores exchange rates of foreign currencies against the base currency. | Developer | REQ_F4118.36.1, REQ_F4118.36.2, REQ_F4118.36.3, REQ_F4118.36.4, REQ_F4118.36.5 |

`md_currency` 沿用分類基底欄位（`name` nvarchar(200)、`custom_fields` JSON 檢查、啟用、軟刪除），代碼改為 `code` char(3)（ISO 4217，公司內唯一含軟刪除）；另加可空 `symbol` nvarchar(10)、`decimal_places` int（CHECK 0–4）、`is_base` bit。篩選唯一索引 `ux_md_currency_company_id_base (company_id) WHERE is_base = 1 AND is_deleted = 0` 保證每間公司最多一個本位幣；唯一約束 `ak_md_currency_company_id_id` 供匯率複合外鍵。`md_exchange_rate` 有 `currency_id` bigint、`effective_from` date、可空 `effective_to` date（CHECK ≥ `effective_from`）、`rate` decimal(19,8)（CHECK > 0）、軟刪除、版本與審計欄位；篩選唯一索引 `(company_id, currency_id, effective_from) WHERE is_deleted = 0` 防止同日重複，期間重疊由服務在公司級 applock 內檢查；外鍵索引 `ix_md_exchange_rate_company_id_currency_id`（篩選索引不能支援外鍵檢查）；複合外鍵 `fk_md_exchange_rate_md_currency_company_id_currency_id` 阻止跨公司。匯率意義為 1 外幣 = `rate` 本位幣（ADR-037）。

```mermaid
erDiagram
    SYS_COMPANY ||--o{ MD_CURRENCY : owns_by_company_id
    MD_CURRENCY ||--o{ MD_EXCHANGE_RATE : quoted_in_same_company
    MD_CURRENCY {
        bigint id PK
        bigint company_id
        char code
        nvarchar name
        nvarchar symbol
        int decimal_places
        bool is_base
        bool is_active
        bool is_deleted
        int version
    }
    MD_EXCHANGE_RATE {
        bigint id PK
        bigint company_id
        bigint currency_id FK
        date effective_from
        date effective_to
        decimal rate
        bool is_deleted
        int version
    }
```

## WU-MD-017

| Requirement ID | Version | Table Name | Description | Author | Related REQ_F |
|---|---|---|---|---|---|
| REQ_DS43.119 | 1.0 | md_tax_code | This table stores tax codes and whether they apply to output tax, input tax or both. | Developer | REQ_F4118.37.1, REQ_F4118.37.2, REQ_F4118.37.3, REQ_F4118.37.4 |
| REQ_DS43.120 | 1.0 | md_tax_rate | This table stores the effective periods and percentages of tax codes. | Developer | REQ_F4118.38.1, REQ_F4118.38.2, REQ_F4118.38.3, REQ_F4118.38.4, REQ_F4118.38.5, REQ_F4118.38.6 |

`md_tax_code` 沿用分類基底欄位（`code` varchar(30) 公司內唯一含軟刪除、`name` nvarchar(200)、`custom_fields` JSON 檢查、啟用、軟刪除），另加 `tax_type` varchar(30)（`Output`／`Input`／`Both`，CHECK）；唯一約束 `ak_md_tax_code_company_id_id` 供稅率複合外鍵。`md_tax_rate` 有 `tax_code_id` bigint、`effective_from` date、可空 `effective_to` date（CHECK ≥ `effective_from`）、`rate_percent` decimal(7,4)（CHECK 0–100）、軟刪除、版本與審計欄位；篩選唯一索引 `(company_id, tax_code_id, effective_from) WHERE is_deleted = 0` 防止同日重複，期間重疊由服務在公司級 applock 內檢查；外鍵索引 `ix_md_tax_rate_company_id_tax_code_id`；複合外鍵 `fk_md_tax_rate_md_tax_code_company_id_tax_code_id` 阻止跨公司。稅率為百分比，例如 5% 存 5.0000（ADR-038）。

```mermaid
erDiagram
    SYS_COMPANY ||--o{ MD_TAX_CODE : owns_by_company_id
    MD_TAX_CODE ||--o{ MD_TAX_RATE : effective_in_same_company
    MD_TAX_CODE {
        bigint id PK
        bigint company_id
        varchar code
        nvarchar name
        varchar tax_type
        bool is_active
        bool is_deleted
        int version
    }
    MD_TAX_RATE {
        bigint id PK
        bigint company_id
        bigint tax_code_id FK
        date effective_from
        date effective_to
        decimal rate_percent
        bool is_deleted
        int version
    }
```

## WU-MD-019

| Requirement ID | Version | Table Name | Description | Author | Related REQ_F |
|---|---|---|---|---|---|
| REQ_DS43.121 | 1.0 | md_account | This table stores the chart of accounts with parent accounts, account types and normal balance directions. | Developer | REQ_F4118.39.1, REQ_F4118.39.2, REQ_F4118.39.3, REQ_F4118.39.4, REQ_F4118.39.5 |

`md_account` 沿用分類基底欄位（`code` varchar(30) 公司內唯一含軟刪除、`name` nvarchar(200)、`custom_fields` JSON 檢查、啟用、軟刪除），另加可空 `parent_id` bigint、`account_type` varchar(30)（`Asset`／`Liability`／`Equity`／`Cost`／`Revenue`／`Expense`，CHECK `ck_md_account_account_type`）、`balance_direction` varchar(10)（`Debit`／`Credit`，CHECK `ck_md_account_balance_direction`）。`code` 只含數字（CHECK `ck_md_account_code_digits`），`parent_id <> id`（CHECK `ck_md_account_parent`）。唯一約束 `ak_md_account_company_id_id` 供上層複合外鍵 `fk_md_account_md_account_company_id_parent_id` 阻止跨公司；索引 `ix_md_account_company_id_parent_id`。是否可過帳不存欄位，由「啟用且沒有未刪除子科目」推算（ADR-039）。

```mermaid
erDiagram
    SYS_COMPANY ||--o{ MD_ACCOUNT : owns_by_company_id
    MD_ACCOUNT ||--o{ MD_ACCOUNT : parent_in_same_company
    MD_ACCOUNT {
        bigint id PK
        bigint company_id
        varchar code
        nvarchar name
        bigint parent_id FK
        varchar account_type
        varchar balance_direction
        bool is_active
        bool is_deleted
        int version
    }
```

## WU-MD-018

| Requirement ID | Version | Table Name | Description | Author | Related REQ_F |
|---|---|---|---|---|---|
| REQ_DS43.122 | 1.0 | md_settlement_method | This table stores the settlement methods used by receipts and payments. | Developer | REQ_F4118.14.1, REQ_F4118.14.2, REQ_F4118.14.3, REQ_F4118.14.4, REQ_F4118.14.5 |
| REQ_DS43.123 | 1.0 | md_payment_term | This table stores payment terms and the rules for calculating due dates. | Developer | REQ_F4118.40.1, REQ_F4118.40.2, REQ_F4118.40.3, REQ_F4118.40.4, REQ_F4118.40.5 |

兩張表都沿用分類基底欄位（`code` varchar(30) 公司內唯一含軟刪除 `ux_{table}_company_id_code`、`name` nvarchar(200)、`custom_fields` JSON 檢查、啟用、軟刪除、版本、審計欄位）。`md_settlement_method` 對應 BlueWhale `PayType`（REQ_DS43.55），不參照資金帳戶。`md_payment_term` 另加 `due_type` varchar(20)（`NetDays`／`ClosingDays`／`ClosingMonthDay`，CHECK `ck_md_payment_term_due_type`）、可空 int `days`（0–365）、`closing_day`（1–31，31 = 月底）、`months`（0–12）、`payment_day`（1–31）。CHECK `ck_md_payment_term_rule` 規定各規則只填自己的欄位（`NetDays`：days；`ClosingDays`：closing_day、days；`ClosingMonthDay`：closing_day、months、payment_day），`ck_md_payment_term_ranges` 檢查範圍（ADR-040）。

```mermaid
erDiagram
    SYS_COMPANY ||--o{ MD_SETTLEMENT_METHOD : owns_by_company_id
    SYS_COMPANY ||--o{ MD_PAYMENT_TERM : owns_by_company_id
    MD_SETTLEMENT_METHOD {
        bigint id PK
        bigint company_id
        varchar code
        nvarchar name
        bool is_active
        bool is_deleted
        int version
    }
    MD_PAYMENT_TERM {
        bigint id PK
        bigint company_id
        varchar code
        nvarchar name
        varchar due_type
        int days
        int closing_day
        int months
        int payment_day
        bool is_active
        bool is_deleted
        int version
    }
```

## WU-MD-020a

| Requirement ID | Version | Table Name | Description | Author | Related REQ_F |
|---|---|---|---|---|---|
| REQ_DS43.124 | 1.0 | md_income_expense_category | This table stores the income and expense categories used by other transactions. | Developer | REQ_F4118.12.1, REQ_F4118.12.2, REQ_F4118.12.3, REQ_F4118.12.4, REQ_F4118.12.5 |
| REQ_DS43.125 | 1.0 | md_fund_account | This table stores the cash and bank fund account definitions. | Developer | REQ_F4118.5.1, REQ_F4118.5.2, REQ_F4118.5.3, REQ_F4118.5.4, REQ_F4118.5.5 |

兩表沿用 MD 分類基底與審計欄位。`md_income_expense_category` 加 `direction` varchar(10) CHECK Income／Expense、可空 `ledger_account_id`；`md_fund_account` 加 `fund_type` varchar(10) CHECK Cash／Bank、必填 `currency_id`、可空 `bank_name` nvarchar(200)、`bank_account_no` varchar(50)、`ledger_account_id`。Cash 的銀行欄位必為 null。幣別及科目均用 `(company_id, id)` 複合外鍵；代碼公司內永久唯一。交易、期初及餘額不在 MD 表（ADR-041）。

```mermaid
erDiagram
    MD_CURRENCY ||--o{ MD_FUND_ACCOUNT : currency
    MD_ACCOUNT ||--o{ MD_FUND_ACCOUNT : ledger_account
    MD_ACCOUNT ||--o{ MD_INCOME_EXPENSE_CATEGORY : ledger_account
    MD_FUND_ACCOUNT {
        bigint id PK
        bigint company_id
        varchar code
        varchar fund_type
        bigint currency_id FK
        bigint ledger_account_id FK
        nvarchar bank_name
        varchar bank_account_no
    }
    MD_INCOME_EXPENSE_CATEGORY {
        bigint id PK
        bigint company_id
        varchar code
        varchar direction
        bigint ledger_account_id FK
    }
```
