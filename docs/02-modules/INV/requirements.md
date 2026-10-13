# INV 庫存需求

## WU-INV-001 模組基線

| Sub-function No. | Sub-function |
|---|---|
| 5.21 | Inventory Foundation |

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4115.21.1 | 1.0 | System should enable authorized users to access company-scoped inventory capabilities. | Developer | INV 與 Contracts 組件由組合根載入；權限目錄僅宣告 `INV.` 前綴；無公司或跨公司不能改變單據狀態；實際資料 API 在後續單元提供。 |
| REQ_F4115.21.2 | 1.0 | System should enable inventory documents to follow controlled approval and posting states. | Developer | 草稿只能送審或作廢；待審只可核准或拒絕；核准後才能過帳；重複或跨公司狀態轉換拒絕；已過帳不可改寫。 |
| REQ_F4115.21.3 | 1.1 | System should enable inventory writes to reference valid company master data. | Developer | 寫入物料、倉庫與可選儲位時，必須驗證同公司、未刪除且啟用，儲位屬該倉庫；從驗證到保存持有與 MD 刪除互斥的共享鎖；已被庫存引用者不可刪除；兩種提交順序無懸空引用。 |

變更記錄：2026-10-13 WU-INV-002 將 REQ_F4115.21.3 提升至 1.1，加入兩種並發提交順序的驗收條件。

庫存異動帳、數量精度與負庫存規則見 ADR-051、ADR-053。

## WU-INV-003 庫存異動帳與結存

| Sub-function No. | Sub-function |
|---|---|
| 5.22 | Inventory Posting Foundation |

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4115.22.1 | 1.0 | System should enable authorized inventory workflows to post approved source documents exactly once. | Developer | 同公司來源類型與 ID 首次過帳追加全部異動；相同內容重送回原 ID，內容不同回 409；來源行號加方向唯一；驗證物料、倉庫、儲位並持有 MD 引用鎖。來源核准由呼叫方單據服務在同一交易檢查。 |
| REQ_F4115.22.2 | 1.0 | System should enable inventory workflows to post receipts and issues atomically without negative stock. | Developer | 基本單位數量為正且最多六位小數；同交易出入庫全成或全退；同結存鍵並發出庫序列化，低於零時拒絕且不留下引用或異動。 |
| REQ_F4115.22.3 | 1.0 | System should enable inventory workflows to query company-scoped on-hand balances derived from immutable movements. | Developer | 依物料、倉庫、儲位即時計算；無異動為零；跨公司不可見；資料庫拒絕已過帳異動 UPDATE／DELETE，沖回須另發來源單據。公開列表頁與 HTTP 查詢留 WU-INV-006。 |
