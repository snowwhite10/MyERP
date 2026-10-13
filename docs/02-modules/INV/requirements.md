# INV 庫存需求

## WU-INV-007 物料期初庫存（007a API；007b 介面）

| Sub-function No. | Sub-function |
|---|---|
| 5.23 | Product Initial Stock（BlueWhale 4.4.1.8.14 Product Initial Stock Interface） |

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4115.23.1 | 1.0 | System should enable user to view records of product initial stock. | Developer | 有 `INV.OPENING_STOCK.VIEW` 才可查目前公司批次；可依啟用日期範圍、物料、倉庫（同時指定時須同一明細）、關鍵字分頁；詳情按行號列出明細；跨公司 404。 |
| REQ_F4115.23.2 | 1.0 | System should enable user to preview product initial stock before submitting. | Developer | 有 `INV.OPENING_STOCK.CREATE`；格式錯誤回 400 欄位路徑；逐行回報 MD 物料／倉庫／儲位不存在或停用、批次內重複、結存鍵已有期初、啟用日期晚於既有異動；回傳每行金額及合計；不寫任何資料或引用。 |
| REQ_F4115.23.3 | 1.0 | System should enable user to submit product initial stock with warehouse, location, quantity and unit cost. | Developer | 啟用日期不晚於今天；基本單位數量正數、單價非負，最多六位小數，金額明細行取四位；最多 1000 行；任一行有錯回 422 `INV.OPENING_STOCK.HAS_ERRORS` 且無部分保存；成功時同交易保存批次並以 `OPENING_STOCK` 入庫過帳。 |
| REQ_F4115.23.4 | 1.0 | System should enable product initial stock submissions to be idempotent and posted once per balance key. | Developer | 相同 `requestKey` 相同內容重送回原批次且不再入庫；內容不同回 409；同公司每個物料＋倉庫＋儲位只能有一筆期初，並發提交只一方成功；資料庫唯一索引作最後防線。 |

007a 已實作資料、API 與真實 SQL／HTTP 驗收；007b 已提供三語介面（介面 5.8 列表／詳情、5.9 批次輸入；預覽逐行錯誤、冪等重送、列印／匯出），父單元結案（ADR-059）。期初不經 SYS 審批，沒有修改或刪除，更正以其他入庫／出庫處理。

## WU-INV-006 庫存查詢（006a API；006b 介面）

| Sub-function No. | Sub-function |
|---|---|
| 5.1 | Inventory Query |

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4115.1.1 | 1.0 | System should enable user to view current stock balances. | Developer | 有 `INV.INVENTORY.VIEW` 才可依目前公司查看；依物料、倉庫、可選儲位篩選與分頁；數量從不可改寫異動帳按基本單位精度即時加總，零結存不列；排序穩定。006b 提供三語頁與篩選匯出。 |
| REQ_F4115.1.2 | 1.0 | System should enable user to view inventory movement details. | Developer | 有 `INV.MOVEMENT.VIEW` 才可依目前公司查看；依物料、倉庫、儲位、業務日期、方向篩選與分頁；按業務日期與 ID 穩定排序，保留來源、數量、代碼快照，不洩漏其他公司。006b 提供三語頁與篩選匯出。 |

006a 只涵蓋 API 與真實 SQL 驗收；006b 完成介面及匯出後才關閉父工作單元（ADR-058）。2026-10-13 006b 完成介面 4.4.1.5.1（REQ_IO_A5.1.1–11），INV-QRY-004 驗收三語、暗色、權限分頁、列印與防公式注入 CSV。

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
