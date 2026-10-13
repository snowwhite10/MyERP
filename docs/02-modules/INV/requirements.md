# INV 庫存需求

## WU-INV-001 模組基線

| Sub-function No. | Sub-function |
|---|---|
| 5.21 | Inventory Foundation |

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4115.21.1 | 1.0 | System should enable authorized users to access company-scoped inventory capabilities. | Developer | INV 與 Contracts 組件由組合根載入；權限目錄僅宣告 `INV.` 前綴；無公司或跨公司不能改變單據狀態；實際資料 API 在後續單元提供。 |
| REQ_F4115.21.2 | 1.0 | System should enable inventory documents to follow controlled approval and posting states. | Developer | 草稿只能送審或作廢；待審只可核准或拒絕；核准後才能過帳；重複或跨公司狀態轉換拒絕；已過帳不可改寫。 |
| REQ_F4115.21.3 | 1.0 | System should enable inventory writes to reference valid company master data. | Developer | 寫入物料、倉庫與可選儲位時，必須驗證同公司、未刪除且啟用，儲位屬該倉庫；從驗證到保存持有與 MD 刪除互斥的共享鎖；已被庫存引用者不可刪除。WU-INV-002 目前已完成鎖與驗證，Usage 查詢及真實寫入測試待續。 |

庫存異動帳、數量精度與負庫存規則見 ADR-051；過帳契約及資料表於 WU-INV-003 實作。
