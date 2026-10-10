# 資料庫規範

> 濃縮 MASTER_PROMPT 第 5、6 節（強制）並補充命名細節；資料存取架構見 [ADR-003](adr/ADR-003-模組專案結構與資料存取.md)。與 MASTER_PROMPT 衝突時以 MASTER_PROMPT 為準。

## 1. 資料庫與連線

| 項目 | 規定 |
|---|---|
| 實例 / 資料庫 | `localhost\MSSQLSERVER2022` / `myerp`；測試 `myerp_test`（只改 Database） |
| 驗證 | Windows 整合驗證；連線字串在 `appsettings.Development.json`（見 MASTER_PROMPT 第 4 節） |
| 驅動 | `Microsoft.Data.SqlClient`；**禁止** `System.Data.SqlClient` |
| 定序 | `Chinese_Traditional_Stroke_Count_100_CI_AS_SC`（MASTER_PROMPT 寫的 `Chinese_Taiwan_Stroke_100_CI_AS_SC` 不存在，此為其 100 版正式名稱，ADR-006）；`Migrate` 建庫時設定，`Core_Baseline` 檢查並修正預先建立的庫 |
| Migration | 組件 `MyERP.Migrations`；`dotnet ef migrations add <Module>_<Purpose> -p src/backend/MyERP.Migrations -s src/backend/MyERP.Api`（以 Api 主機 DI 建立 DbContext，ADR-006） |
| Schema | 全部 `dbo`，以前綴分模組，不使用多 schema |
| 結構變更 | 只經 EF Core migration；可向下還原（`Down` 必須完整） |

## 2. 命名

| 物件 | 規則 | 例子 |
|---|---|---|
| 資料表 | `{前綴}_{單數名詞}`，snake_case | `sal_order`、`inv_stock_move` |
| 明細表 | `{表頭}_line` | `sal_order_line` |
| 關聯表（多對多） | `{前綴}_{a}_{b}` | `sys_user_role` |
| 欄位 | snake_case（`UseSnakeCaseNamingConvention()`） | `doc_date`、`customer_id` |
| 外鍵欄位 | `{參照實體}_id` | `order_id`、`item_id` |
| 布林 | `is_` / `has_` 開頭 | `is_active`、`is_deleted` |
| 主鍵 / 索引 / FK | 採 EF + NamingConventions 預設：`pk_{表}`、`ix_{表}_{欄}`、`fk_{表}_{參照表}_{欄}`；唯一索引在設定中命名 `ux_{表}_{欄}` | `ux_sal_order_company_id_doc_no` |
| Check 約束 | `ck_{表}_{用途}` | `ck_sal_order_custom_fields_json` |
| SP | `usp_{前綴}_{用途}` | `usp_ar_aging_report` |
| Migration | `{Module}_{Purpose}`（PascalCase 模組名） | `Sales_AddCreditLimit`、`Core_CreateDatabase` |

前綴對照（新增前綴先寫 ADR 並更新本表）：

| 前綴 | 模組 | 前綴 | 模組 |
|---|---|---|---|
| `sys_` | 系統管理（SYS） | `co_` | 成本 / 管理會計（CO） |
| `md_` | 主檔（MD） | `pp_` | 生產製造（PP） |
| `gl_` | 總帳（FI） | `hr_` | 人力資源（HR） |
| `ar_` | 應收（FI） | `crm_` | 客戶關係（CRM） |
| `ap_` | 應付（FI） | `scm_` | 供應鏈（SCM） |
| `fa_` | 固定資產（FI） | `ps_` | 項目管理（PS） |
| `cm_` | 銀行及現金（FI） | `qm_` | 品質管理（QM） |
| `tax_` | 稅務（FI） | `pm_` | 設備維護（PM） |
| `sal_` | 銷售（SD） | `bi_` | 報表及商業智能（BI） |
| `pur_` | 採購（PUR） | `inv_` | 庫存 / 倉庫（INV） |

## 3. 主鍵、外鍵與索引

- 主鍵 `id BIGINT IDENTITY(1,1)`，叢集索引。
- **每個外鍵欄位必須手動建索引**（含跨模組 Id 欄位，例如 `customer_id`）。
- 跨模組：只存 Id，**不建 FK、不建 navigation**；模組內可建 FK 與 navigation（刪除行為預設 `Restrict`，明細對表頭可 `Cascade`）。
- 單號唯一：`ux_{表}_company_id_doc_no`；主檔代碼唯一：`ux_{表}_company_id_code`（軟刪除資料也佔用代碼，避免重用造成追溯混亂）。
- 複合索引：常用列表查詢（例如 `company_id, status, doc_date`）須設計複合索引，並在模組 `data-structure.md` 說明用途。
- 篩選索引可用於軟刪除主檔（`WHERE is_deleted = 0`）。

## 4. 型別

| 用途 | C# | SQL Server |
|---|---|---|
| 金額 | `decimal` | `DECIMAL(19,4)` |
| 單價 | `decimal` | `DECIMAL(19,6)` |
| 數量 | `decimal` | `DECIMAL(19,4)` |
| 匯率 | `decimal` | `DECIMAL(19,8)` |
| 比率 / 百分比（稅率、折扣率） | `decimal` | `DECIMAL(9,6)`（以小數存，例如 5% = 0.05） |
| 業務日期 | `DateOnly` | `DATE` |
| 時間戳 | `DateTime` | `DATETIME2(6)`，存 UTC+8 |
| 幣別 | `string` | `CHAR(3)` |
| 狀態 / 列舉 | `enum` | `VARCHAR(30)`（`HasConversion<string>()`） |
| 代碼類（單號、前綴、物料代碼） | `string` | `VARCHAR(n)`；含中文的代碼改用 `NVARCHAR` |
| 一般文字 | `string` | `NVARCHAR(n)`；長文 `NVARCHAR(MAX)` |
| 布林 | `bool` | `BIT` |
| 自訂欄位 | `string` / `JsonDocument` | `NVARCHAR(MAX)` + `CHECK (ISJSON(custom_fields) = 1)` |

常用長度：代碼 `VARCHAR(30)`、單號 `VARCHAR(30)`、名稱 `NVARCHAR(200)`、備註 `NVARCHAR(500)`、地址 `NVARCHAR(500)`、電話 `VARCHAR(30)`、電郵 `VARCHAR(254)`。

**禁止**：`float` / `real`（C# `float` / `double`）用於業務數值；`DATETIME`、`SMALLDATETIME`、`MONEY`、`TEXT`、`NTEXT`；`DateTime.Now` / `DateTime.UtcNow`（用 `IClock`）。業務日期不做時區轉換。

## 5. 必備欄位

每張業務表：

```sql
company_id  BIGINT       NOT NULL,           -- 多公司隔離 + Global Query Filter
created_at  DATETIME2(6) NOT NULL,           -- UTC+8，interceptor 填入
created_by  BIGINT       NOT NULL,
updated_at  DATETIME2(6) NULL,
updated_by  BIGINT       NULL,
version     INT          NOT NULL DEFAULT 1  -- 樂觀鎖（EF concurrency token），interceptor 遞增
```

| 表類別 | 另加欄位 |
|---|---|
| 主檔 | `code`、`name`、`is_active BIT NOT NULL DEFAULT 1`、`is_deleted BIT NOT NULL DEFAULT 0`、`custom_fields` |
| 單據表頭 | `doc_no`、`doc_date DATE`、`status VARCHAR(30)`、`branch_id`、`remark`、`custom_fields`；外幣單據另加第 8 節欄位 |
| 單據明細 | `line_no INT`（同表頭內唯一）；金額欄同樣分原幣 / 本位幣 |
| 系統表（如 `sys_audit_log`） | 不需 `version`；`company_id` 依用途（全系統設定可為 NULL 並在設計文件說明） |

## 6. 刪除與狀態

- 主檔：軟刪除（`is_deleted`）；被單據引用後禁止刪除，只能停用（`is_active = 0`）。
- 單據：`Draft` 可實體刪除；送審後只能作廢（`Voided`）；已過帳只能沖銷（產生沖銷單，原單不改）。
- 標準單據狀態（各模組可增加，但名稱沿用）：`Draft` → `Submitted` → `Approved` / `Rejected` → `Posted` / `Closed`；`Voided`。
- Global Query Filter：`company_id = 目前公司` 且（主檔）`is_deleted = 0`。需要跨公司或含已刪資料時明確 `IgnoreQueryFilters()` 並在程式註解說明原因。
  - 兩者為具名篩選（`ErpQueryFilters.Company`、`ErpQueryFilters.SoftDelete`）：查已刪主檔用 `IgnoreQueryFilters([ErpQueryFilters.SoftDelete])`，公司隔離仍有效。
  - 沒有公司上下文時篩選為 `WHERE 0 = 1`（封閉失敗）。
- 必備欄位由實體基底類別提供：單據繼承 `BusinessEntity`、主檔繼承 `MasterEntity`（`MyERP.Shared.Domain`）；`created_*`、`updated_*`、`version`、新增時的 `company_id` 由 `AuditSaveChangesInterceptor` 填入，沒有登入用戶時 `created_by` / `updated_by` = 0（系統用戶）。詳見 [shared-core.md](shared-core.md)、ADR-005。

## 7. Stored Procedure

- 用途：大量運算、批次、複雜報表（月結、MRP、帳齡、庫存估值、儀表板彙總）；一般 CRUD 用 EF Core。
- 原始碼：`database/procedures/{前綴}/usp_{前綴}_{用途}.sql`；以 `CREATE OR ALTER PROCEDURE`，開頭 `SET NOCOUNT ON`。
- 部署：.sql 檔嵌入 `MyERP.Migrations`，migration 內 `migrationBuilder.CreateOrAlterProcedure("{前綴}/usp_xxx")`；修改 SP 必須新增 migration（`Down` 還原為上一版或 `DropProcedure`）。範例：`database/procedures/sys/usp_sys_database_info.sql`（ADR-006）。
- 參數化，**禁止動態拼接 SQL**；必須有 `@company_id` 參數並以之過濾。
- 多表寫入：`SET XACT_ABORT ON` + `BEGIN TRAN` + `TRY...CATCH`（`THROW` 重拋）。
- 模組邊界：SP 只寫入本模組資料表；**報表讀取例外**——`usp_bi_*` 與各模組報表 SP 可唯讀 JOIN 其他模組資料表，須在 `data-structure.md` 列明讀取的表。
- 數值型別同第 4 節；SP 內時間由參數傳入（`@as_of_date`），不使用 `GETDATE()` 作業務判斷。

## 8. 多幣別

外幣交易同時存：

| 欄位 | 型別 |
|---|---|
| `currency_code` | `CHAR(3)` |
| `exchange_rate` | `DECIMAL(19,8)` |
| `amount`（原幣） | `DECIMAL(19,4)` |
| `amount_base`（本位幣） | `DECIMAL(19,4)` |

- 本位幣金額在過帳時鎖定，之後不重算；匯差另以重估傳票處理。報表一律以 `amount_base` 加總。
- 取整：`Money.Round(value, currency)` 於明細行；表頭 = 明細加總。

## 9. 帳簿類資料只增不改

- 庫存：`inv_stock_move` 是唯一真相；`inv_stock_balance` 是彙總，**禁止直接 UPDATE 數量**（只能由異動流程或重算 SP 維護）。
- 總帳：`gl_journal` + `gl_journal_line`；`debit`、`credit` ≥ 0、一行只能一邊非零、整張借貸相等（以 Check 約束 + 領域規則雙重保證）；已過帳傳票不可修改。
- 稽核：`sys_audit_log` 只增不改不刪。

## 10. 測試資料庫

- `myerp_test` 每次整合測試執行前重建（DROP → migration → 種子），由 assembly fixture `TestDatabase` 處理（`tests/backend.integration/MyERP.IntegrationTests/Database`）；測試模組 `tst_*` 表由 fixture 另建，不屬於 migration。
- 測試不得連到 `myerp`：連線字串可用環境變數 `MYERP_TEST_CONNECTION` 覆寫，但資料庫名稱不是 `myerp_test` 時 fixture 拋出。
- 測試資料以不同 `company_id` 隔離（`TestCompanies.Next()`），不需逐測試清表。

## 11. 檢查清單（code review 用）

- [ ] 表名 / 欄位 snake_case、前綴正確、單數
- [ ] 必備欄位齊全；主檔 / 表頭有 `custom_fields` + ISJSON
- [ ] 金額 / 數量 / 單價 / 匯率型別正確，無 float / double / MONEY / DATETIME
- [ ] 每個 FK 欄位（含跨模組 Id）有索引；單號 / 代碼唯一索引含 `company_id`
- [ ] 跨模組無 FK / navigation
- [ ] Migration 命名正確、`Down` 可還原
- [ ] SP 參數化、有交易與錯誤處理、帶 `@company_id`
