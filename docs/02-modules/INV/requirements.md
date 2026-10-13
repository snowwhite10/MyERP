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

## WU-INV-004 其他入庫（004a 後端已實作；004b 介面、列印／匯出待實作）

| Sub-function No. | Sub-function |
|---|---|
| 5.2 | Other Inbound |

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4115.2.1 | 1.0 | System should enable user to view records of other inbound. | Developer | 只列目前公司單據；可按單號、日期、狀態篩選並分頁；詳情包含明細及審批狀態；有查看權限才可讀取或匯出。 |
| REQ_F4115.2.2 | 1.0 | System should enable user to create records of other inbound. | Developer | 建立時在交易內取 `INV.OTHER_RECEIPT` 單號；至少一行，同公司有效物料／倉庫／儲位，基本單位正數數量最多六位小數；草稿不產生異動帳。 |
| REQ_F4115.2.3 | 1.0 | System should enable user to edit records of other inbound. | Developer | 只有草稿可編輯日期、備註及整組明細；版本衝突回 409；送審後禁止改寫，駁回後可修訂再送審。 |
| REQ_F4115.2.4 | 1.0 | System should enable user to approve requests of other inbound. | Developer | 草稿透過 SYS 工作流送審；最終核准與單據狀態、一次性入庫過帳在同一交易，重複決定不得重複入庫；列印／匯出內容以已保存單據為準。 |
| REQ_F4115.2.5 | 1.0 | System should enable user to reject requests of other inbound. | Developer | SYS 審批人可駁回待審單據；單據標為駁回，不寫異動帳，保留歷史與意見；可重新編輯及送審。 |
| REQ_F4115.2.6 | 1.0 | System should enable user to delete records of other inbound. | Developer | 只有草稿可刪除並保留稽核所需的軟刪除資訊；待審、已核准、已過帳不得刪除；已過帳沖回須另開反向單據。 |

補充範圍：三語建立／查看／編輯介面、列印與 Excel 匯出屬 WU-INV-004b（ADR-055）。2026-10-13 WU-INV-004a 完成後端：上述驗收中與資料、權限、單號、SYS 審批及過帳有關的部分已由 INV-REC-001～005 驗證；介面、列印與匯出驗收待 INV-REC-006。駁回單據須先修訂（回草稿）才可再送審或刪除。

## WU-INV-005 其他出庫（005a 後端；005b 前端）

| Sub-function No. | Sub-function |
|---|---|
| 5.3 | Other Outbound |

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4115.3.1 | 1.0 | System should enable user to view records of other outbound. | Developer | 只列目前公司；單號／備註、日期、狀態分頁篩選；詳情有明細及審批狀態；列印／匯出需要 VIEW。 |
| REQ_F4115.3.2 | 1.0 | System should enable user to create records of other outbound. | Developer | 交易內取 `INV.OTHER_ISSUE` 單號；至少一行，同公司有效物料／倉庫／儲位，正數六位基本單位數量；草稿不扣庫存。 |
| REQ_F4115.3.3 | 1.0 | System should enable user to edit records of other outbound. | Developer | 草稿或駁回後可整組替換明細，駁回修訂回草稿；版本衝突 409；待審及已過帳不可改。 |
| REQ_F4115.3.4 | 1.0 | System should enable user to approve requests of other outbound. | Developer | SYS 最終核准與出庫異動在同一交易；庫存不足或主檔失效時全數回滾；同結存鍵並發不可超賣；重送不重複扣庫存。 |
| REQ_F4115.3.5 | 1.0 | System should enable user to reject requests of other outbound. | Developer | SYS 駁回保留歷史，不寫異動；先修訂才可再送審。 |
| REQ_F4115.3.6 | 1.0 | System should enable user to delete records of other outbound. | Developer | 只有草稿可軟刪除，單號不重用；已過帳以新反向單據沖回。 |

005a 已實作資料、API 與過帳；005b 已實作三語介面 5.5–5.7、列印、CSV 匯出及 E2E。出庫單價與金額均標示為估算，只用於審批門檻；異動帳成本由 WU-INV-014 的估值流程處理（ADR-057）。庫存不足時後端保留草稿，前端顯示本地化錯誤並保留可修訂操作。
