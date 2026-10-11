# SYS 組織資料結構

## IP 白名單（WU-SYS-006）

| Requirement ID | Version | Table Name | Description | Author | Related REQ_F |
|---|---|---|---|---|---|
| REQ_DS43.102 | 1.0 | sys_ip_whitelist | System-wide IP ranges scoped to the system, a role, or a user, with inclusive effective dates and audit/version fields. | Developer | REQ_F4118.32.1, REQ_F4118.32.2, REQ_F4118.32.3, REQ_F4118.32.4 |

```mermaid
erDiagram
    sys_role |o--o{ sys_ip_whitelist : restricts
    sys_user |o--o{ sys_ip_whitelist : restricts
    sys_ip_whitelist {
        bigint id PK
        varchar scope
        bigint role_id FK
        bigint user_id FK
        varchar ip_range
        date valid_from
        date valid_to
        bit is_active
        int version
    }
```

`sys_ip_whitelist` 不含 `company_id`，供登入前與跨公司登入後檢查。CHECK 限定三種層級的對象欄位互斥、結束日期不早於開始日期；三個篩選唯一索引分別保護全系統、角色、用戶的同網段。`ip_range` 正規化後最多 43 字，備註最多 200 字；有 `created_at/by`、`updated_at/by`、`version`，停用後仍保留稽核。實際欄位以 migration `System_IpWhitelist` 為準。

| Requirement ID | Version | Table Name | Description | Author | Related REQ_F |
|---|---|---|---|---|---|
| REQ_DS43.20 | 1.0 | sys_company | Company isolation root; globally unique code, audit, version and soft deletion. | Developer | REQ_F4118.20.1, REQ_F4118.20.2, REQ_F4118.20.3, REQ_F4118.20.4, REQ_F4118.30.1, REQ_F4118.30.3 |
| REQ_DS43.89 | 1.0 | sys_branch | Branch master scoped to a company. | Developer | REQ_F4118.21.1, REQ_F4118.21.2, REQ_F4118.21.3, REQ_F4118.30.1 |
| REQ_DS43.22 | 1.0 | sys_department | Department master scoped to a company, optionally linked to a branch and parent department. | Developer | REQ_F4118.22.1, REQ_F4118.22.2, REQ_F4118.22.3, REQ_F4118.30.1 |

```mermaid
erDiagram
    sys_company ||--o{ sys_branch : contains
    sys_company ||--o{ sys_department : contains
    sys_branch |o--o{ sys_department : organizes
    sys_department |o--o{ sys_department : parent_of
```

`sys_company` 為隔離根，沒有 `company_id` 與公司篩選；應用層必須先驗證權限。分店與部門繼承主檔欄位：`id`、`company_id`、`code`、`name`、審計、`version`、`is_active`、`is_deleted`、`custom_fields`。代碼分別以 `(company_id, code)` 唯一索引保護，刪除後代碼仍保留。分店與部門透過外鍵歸屬公司；部門指向分店／上級部門使用 `(company_id, id)` 複合外鍵，在資料庫層阻止跨公司參照。上級循環仍由應用層檢查。三表 `custom_fields` 皆用 ISJSON 約束。公司自身的代碼全域唯一。

## 用戶身分

| Requirement ID | Version | Table Name | Description | Author | Related REQ_F |
|---|---|---|---|---|---|
| REQ_DS43.82 | 1.1 | sys_user | Global login identities with password hash, failure count, lockout and audit fields. | Developer | REQ_F4111.1.2, REQ_F4118.23.1, REQ_F4118.23.2, REQ_F4118.23.3, REQ_F4118.23.4, REQ_F4118.23.5, REQ_F4118.23.6, REQ_F4118.23.7, REQ_F4118.23.8, REQ_F4118.30.2, REQ_F4118.30.3 |

```mermaid
erDiagram
    sys_user {
        bigint id PK
        varchar username
        varchar normalized_username UK
        varchar password_hash
        datetime2 password_changed_at
        int failed_login_count
        datetime2 lockout_end
        bit is_active
        bit is_deleted
        int version
    }
```

`sys_user` 是全系統身分，不綁定單一公司；WU-SYS-004 再建立用戶與獲授權公司的關聯。`normalized_username` 以唯一索引防止不同大小寫及刪除後重用。密碼只存 ASP.NET Core Identity 雜湊。其餘審計欄位沿用 `IAuditable`。

## 角色與授權（WU-SYS-004）

| Requirement ID | Version | Table Name | Description | Author | Related REQ_F |
|---|---|---|---|---|---|
| REQ_DS43.90 | 1.0 | sys_role | System-wide role definitions with data scope, full-access flag, audit, version and soft deletion. | Developer | REQ_F4118.25.1, REQ_F4118.25.2, REQ_F4118.25.3, REQ_F4118.25.4, REQ_F4118.30.1 |
| REQ_DS43.91 | 1.0 | sys_role_permission | Permission codes granted by each role. | Developer | REQ_F4118.25.2, REQ_F4118.25.3, REQ_F4118.25.7, REQ_F4118.30.1, REQ_F4118.30.3 |
| REQ_DS43.92 | 1.0 | sys_user_company | Companies a user may access, with branch and department placement and the default company. | Developer | REQ_F4118.23.5, REQ_F4118.23.6, REQ_F4118.25.5, REQ_F4118.25.6, REQ_F4118.25.7, REQ_F4118.25.8, REQ_F4118.30.2 |
| REQ_DS43.93 | 1.0 | sys_user_role | Roles assigned to a user within a company. | Developer | REQ_F4118.25.6, REQ_F4118.25.7, REQ_F4118.25.8, REQ_F4118.30.2 |

```mermaid
erDiagram
    sys_role ||--o{ sys_role_permission : grants
    sys_user ||--o{ sys_user_company : accesses
    sys_company ||--o{ sys_user_company : admits
    sys_branch |o--o{ sys_user_company : places
    sys_department |o--o{ sys_user_company : places
    sys_user_company ||--o{ sys_user_role : holds
    sys_role ||--o{ sys_user_role : assigned_as
```

- `sys_role`：`code VARCHAR(50)` 唯一（`ux_sys_role_code`，含已刪除）；`data_scope VARCHAR(30)`（Self／Department／Branch／Company）；`is_full_access`；審計、`version`、`is_active`、`is_deleted`。角色為全系統定義，不屬於單一公司。
- `sys_role_permission`：`permission_code VARCHAR(100)`；唯一 `(role_id, permission_code)`，同時作為外鍵索引；刪除角色列時連帶刪除（角色實務上只做軟刪除）。權限目錄在程式碼，不另建權限表（ADR-014）。
- `sys_user_company`：唯一 `(user_id, company_id)`（alternate key，亦為 `sys_user_role` 複合外鍵的主鍵端）；`ux_sys_user_company_default` 篩選唯一索引（`is_default = 1`）保證每位用戶最多一間預設公司；`(company_id, branch_id)`、`(company_id, department_id)` 複合外鍵指向分店／部門，資料庫層阻止跨公司歸屬；`is_active`、審計、`version`。
- `sys_user_role`：唯一 `(user_id, company_id, role_id)`；`(user_id, company_id)` 複合外鍵指向 `sys_user_company`（刪除關聯時連帶刪除），沒有公司存取權就不能指派角色；`role_id` 有索引，外鍵為 Restrict。
- 以上授權表不實作公司篩選（`ICompanyOwned`），因為授權查詢需要跨公司列出用戶可存取的公司；存取一律經 SYS 應用服務。

## 稽核日誌（WU-SYS-007）

| Requirement ID | Version | Table Name | Description | Author | Related REQ_F |
|---|---|---|---|---|---|
| REQ_DS43.94 | 1.0 | sys_audit_log | Append-only audit trail of data changes (field old/new values) and security events, with company, user, IP and trace id. | Developer | REQ_F4118.27.1, REQ_F4118.27.2, REQ_F4118.27.3, REQ_F4118.27.4, REQ_F4118.27.5, REQ_F4118.27.6 |

```mermaid
erDiagram
    sys_audit_log {
        bigint id PK
        bigint company_id
        bigint user_id
        nvarchar username
        datetime2 occurred_at
        varchar category
        varchar action
        varchar table_name
        varchar record_id
        nvarchar changes
        varchar remote_ip
        varchar trace_id
    }
```

`sys_audit_log` 由 Shared.Infrastructure 的 `AuditTrailInterceptor` 寫入，與業務資料同一交易（ADR-019）。

- 刻意不設外鍵：用戶或公司日後刪除，稽核記錄仍保留；安全事件可能沒有用戶（不存在的帳號）或沒有公司（登入）。
- `category`：`DATA` 或 `SECURITY`。`action`：CREATE／UPDATE／DELETE／VOID／APPROVE／POST／LOGIN／LOGIN_FAILED／MASTER_CODE_LOGIN／LOGOUT／PERMISSION_CHANGE／EXPORT。
- `changes`：欄位差異 `{"欄位":{"old":…,"new":…}}` 或安全事件明細，有 ISJSON 約束；敏感欄位值為 `***`。
- `trg_sys_audit_log_append_only`（INSTEAD OF UPDATE, DELETE）拒絕任何修改與刪除。
- 索引：`(company_id, occurred_at)`、`(user_id, occurred_at)`、`(table_name, record_id, occurred_at)`。
- 本表沒有審計欄位、version 與軟刪除，因為記錄本身不可變。

## 單號規則

| Requirement ID | Version | Table Name | Description | Author | Related REQ_F |
|---|---|---|---|---|---|
| REQ_DS43.95 | 1.0 | sys_number_rule | Document number rule per company and document type: prefix, date format, sequence length, reset period and active flag. | Developer | REQ_F4118.28.1, REQ_F4118.28.2, REQ_F4118.28.3, REQ_F4118.28.4, REQ_F4118.28.5, REQ_F4118.28.6 |
| REQ_DS43.96 | 1.0 | sys_number_sequence | Running sequence counter per number rule and reset period, updated atomically when a number is issued. | Developer | REQ_F4118.28.3, REQ_F4118.28.4, REQ_F4118.28.5 |

```mermaid
erDiagram
    sys_company ||--o{ sys_number_rule : configures
    sys_number_rule ||--o{ sys_number_sequence : counts
    sys_number_rule {
        bigint id PK
        bigint company_id FK
        varchar document_type
        nvarchar name
        varchar prefix
        varchar date_format
        int sequence_length
        varchar reset_period
        bit is_active
    }
    sys_number_sequence {
        bigint id PK
        bigint company_id FK
        bigint rule_id FK
        varchar period_key
        bigint current_value
    }
```

- 兩表都有 6.4 節的必備欄位（`company_id`、審計欄位、`version`）。規則是設定資料，沒有軟刪除與 `custom_fields`，以 `is_active` 停用。
- `sys_number_rule`：`(company_id, document_type)` 唯一索引（亦為 company_id 外鍵索引）；`sequence_length` CHECK 3–10；`date_format`、`reset_period` 以字串儲存列舉。
- `sys_number_sequence`：`period_key` 為 `-`（不重設）、`yyyy`、`yyyyMM` 或 `yyyyMMdd`；`(company_id, rule_id, period_key)` 唯一索引是取號 MERGE 的定位索引，亦為 `(company_id, rule_id)` → `sys_number_rule (company_id, id)` 複合外鍵的索引，令計數列不可指向他公司的規則；`current_value` CHECK ≥ 1。
- 計數列只由 `NumberGenerator` 以單一 `MERGE … WITH (HOLDLOCK)` 原子更新（`version` 同步遞增），不經 EF 追蹤，不寫稽核（ADR-021）。

## 審批流程（WU-SYS-009）

| Requirement ID | Version | Table Name | Description | Author | Related REQ_F |
|---|---|---|---|---|---|
| REQ_DS43.97 | 1.0 | sys_approval_flow | Approval flow per company and document type, with the self-approval setting and active flag. | Developer | REQ_F4118.29.1, REQ_F4118.29.2, REQ_F4118.29.4, REQ_F4118.29.8 |
| REQ_DS43.98 | 1.0 | sys_approval_flow_step | Ordered steps of an approval flow: approver role or user and the optional minimum amount that makes the step apply. | Developer | REQ_F4118.29.1, REQ_F4118.29.2, REQ_F4118.29.4, REQ_F4118.29.8 |
| REQ_DS43.99 | 1.0 | sys_approval_delegation | Approval delegation from one user to another within a date range. | Developer | REQ_F4118.29.3, REQ_F4118.29.5, REQ_F4118.29.6, REQ_F4118.29.8 |
| REQ_DS43.100 | 1.0 | sys_approval_instance | One submission of a document for approval: document reference, amount, status, current step, submitter and completion time. | Developer | REQ_F4118.29.4, REQ_F4118.29.5, REQ_F4118.29.6, REQ_F4118.29.7, REQ_F4118.29.9 |
| REQ_DS43.101 | 1.0 | sys_approval_instance_step | Snapshot of the applicable flow steps of an approval instance with each step's decision, actor, delegator, time and comment. | Developer | REQ_F4118.29.4, REQ_F4118.29.5, REQ_F4118.29.6, REQ_F4118.29.7, REQ_F4118.29.9 |

```mermaid
erDiagram
    sys_company ||--o{ sys_approval_flow : configures
    sys_approval_flow ||--|{ sys_approval_flow_step : "ordered steps"
    sys_role ||--o{ sys_approval_flow_step : "approver role"
    sys_user ||--o{ sys_approval_flow_step : "approver user"
    sys_user ||--o{ sys_approval_delegation : "delegator or delegate"
    sys_approval_flow ||--o{ sys_approval_instance : "used by"
    sys_approval_instance ||--o{ sys_approval_instance_step : "step snapshot"
    sys_approval_flow {
        bigint id PK
        bigint company_id FK
        varchar document_type
        nvarchar name
        bit allow_self_approval
        bit is_active
    }
    sys_approval_flow_step {
        bigint id PK
        bigint company_id FK
        bigint flow_id FK
        int step_no
        nvarchar name
        varchar approver_type
        bigint role_id FK
        bigint user_id FK
        decimal min_amount
    }
    sys_approval_delegation {
        bigint id PK
        bigint company_id FK
        bigint delegator_user_id FK
        bigint delegate_user_id FK
        date start_date
        date end_date
        nvarchar remark
        bit is_active
    }
    sys_approval_instance {
        bigint id PK
        bigint company_id FK
        bigint flow_id FK
        varchar document_type
        bigint document_id
        varchar document_no
        nvarchar summary
        decimal amount
        varchar status
        int current_step_no
        bit allow_self_approval
        bigint submitted_by
        datetime2 submitted_at
        datetime2 completed_at
    }
    sys_approval_instance_step {
        bigint id PK
        bigint company_id FK
        bigint instance_id FK
        int step_no
        nvarchar name
        varchar approver_type
        bigint role_id FK
        bigint user_id FK
        decimal min_amount
        varchar status
        bigint acted_by FK
        bigint on_behalf_of_user_id FK
        datetime2 acted_at
        nvarchar comment
    }
```

- 五張表都有 6.4 節的必備欄位（`company_id`、審計欄位、`version`）。流程與代理是設定資料，以 `is_active` 停用，不刪除；實例與實例步驟是審批記錄，不刪除。
- `document_id` 是單據模組的記錄 Id，跨模組只存 Id、不建 FK；`document_type` 為 `{PREFIX}.{DOCUMENT}`（與單號規則相同）。
- 金額 `amount`、`min_amount` 為 `DECIMAL(19,4)` 本位幣金額，CHECK ≥ 0。`approver_type`、`status` 以字串儲存列舉。
- `sys_approval_flow`：`(company_id, document_type)` 唯一索引（亦為 company_id 外鍵索引）。
- `sys_approval_flow_step`：`(company_id, flow_id, step_no)` 唯一索引，亦為指向 `sys_approval_flow (company_id, id)` 的複合外鍵索引；`step_no` CHECK 1–10；CHECK「角色步驟只有 `role_id`、用戶步驟只有 `user_id`」；`role_id`、`user_id` 各有外鍵索引。
- `sys_approval_delegation`：CHECK `end_date >= start_date`、被代理人 ≠ 代理人；`(company_id, delegate_user_id, start_date)` 用於審批時找出今天的被代理人，`(company_id, delegator_user_id, start_date)` 用於重疊檢查；兩個用戶欄位各有外鍵索引。
- `sys_approval_instance`：篩選唯一索引 `(company_id, document_type, document_id) WHERE status = 'Pending'`，保證同一單據最多一個審批中的實例（併發送審由此擋下）；`(company_id, document_type, document_id, submitted_at)` 供單據審批歷史；`(company_id, flow_id)` 為指向流程的複合外鍵索引（自動核准且沒有流程時 `flow_id` 為 NULL）；CHECK「審批中才有 `current_step_no`」。
- `sys_approval_instance_step`：`(company_id, instance_id, step_no)` 唯一索引，亦為複合外鍵索引；`(company_id, status, user_id)`、`(company_id, status, role_id)` 供待辦清單；`role_id`、`user_id`、`acted_by`、`on_behalf_of_user_id` 各有外鍵索引。

## 系統參數（WU-SYS-010a）

| Requirement ID | Version | Table Name | Description | Author | Related REQ_F |
|---|---|---|---|---|---|
| REQ_DS43.73 | 1.0 | sys_parameter | Company-level values of module-declared system parameters; only changed values are stored, normalized as text, with audit/version fields. | Developer | REQ_F4118.17.1, REQ_F4118.17.2, REQ_F4118.17.3, REQ_F4118.17.4, REQ_F4118.18.1, REQ_F4118.18.2, REQ_F4118.18.3 |

```mermaid
erDiagram
    sys_company ||--o{ sys_parameter : configures
    sys_parameter {
        bigint id PK
        bigint company_id FK
        varchar param_key
        nvarchar param_value
        int version
    }
```

`param_key` 為 `VARCHAR(80)`（`{PREFIX}.{NAME}`），`param_value` 為 `NVARCHAR(500)`。唯一索引 `ux_sys_parameter_company_id_param_key` 保證每間公司每個參數最多一列，也作為 `company_id` 外鍵索引。表內只存修改過的參數，沒有資料列時使用模組宣告的預設值。值的型別與範圍不在資料庫約束，由參數宣告驗證（ADR-026）。有 `created_at/by`、`updated_at/by`、`version`。實際欄位以 migration `System_Parameter` 為準。

## Logo 與印章（WU-SYS-010d）

| Requirement ID | Version | Table Name | Description | Author | Related REQ_F |
|---|---|---|---|---|---|
| REQ_DS43.103 | 1.0 | sys_company_image | Company logo and stamp images (one per kind per company) stored as binary with content-detected type, original file name, size, dimensions and audit/version fields. | Developer | REQ_F4118.19.1, REQ_F4118.19.2, REQ_F4118.19.3 |

```mermaid
erDiagram
    sys_company ||--o{ sys_company_image : owns
    sys_company_image {
        bigint id PK
        bigint company_id FK
        varchar image_kind
        varchar content_type
        nvarchar file_name
        int size_bytes
        int width
        int height
        varbinary content
        int version
    }
```

`image_kind` 為 `VARCHAR(10)`（`Logo`／`Stamp`），`content_type` 為 `VARCHAR(30)`（`image/png`／`image/jpeg`，由上傳時解析內容取得），`file_name` 為 `NVARCHAR(200)`（只有檔名部分），`content` 為 `VARBINARY(MAX)`（上限 1 MB 由應用層檢查）。檢查約束：`ck_sys_company_image_kind`、`ck_sys_company_image_content_type`、`ck_sys_company_image_size`（`size_bytes > 0` 且等於 `DATALENGTH(content)`）、`ck_sys_company_image_dimensions`。唯一索引 `ux_sys_company_image_company_id_image_kind` 保證每間公司每種圖片最多一列，也作為 `company_id` 外鍵索引。刪除為實體刪除（設定資料），稽核日誌保留記錄。有 `created_at/by`、`updated_at/by`、`version`。實際欄位以 migration `System_CompanyImage` 為準（ADR-028）。
