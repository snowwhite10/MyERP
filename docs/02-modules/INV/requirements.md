# INV 庫存需求

## WU-INV-010 盤點（010d 設計；010a 後端；010b 介面）

| Sub-function No. | Sub-function |
|---|---|
| 5.7 | Stocktake |

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4115.7.1 | 1.0 | System should enable user to create records of stocktake. | Developer | `INV.STOCKTAKE.CREATE` 以 1–20 個倉庫、0–50 個物料類別（含下層）、0–500 個物料為範圍，在交易內取 `INV.STOCKTAKE` 單號並寫入快照：每個結存大於 0 的「物料＋倉庫＋儲位」一行帳面數量，批序號物料再按號碼記錄帳面，同時保存快照時間與最大異動 ID；超過 5,000 行或 20,000 個批序號回 422；範圍內的物料＋倉庫已在其他未結盤點單時回 422 `INV.STOCKTAKE.SCOPE_IN_USE`；建立不寫異動。 |
| REQ_F4115.7.2 | 1.0 | System should enable user to generate inventory profit/loss reports. | Developer | `INV.STOCKTAKE.VIEW` 可取得單據的差異報告：每行帳面、實盤、差異（實盤 − 帳面）與類型 Gain／Loss／Match／Uncounted，以及盤盈、盤虧總數量與行數；可只列有差異的行；只讀已保存資料，前端可列印與匯出防公式注入的 UTF-8 CSV；金額待估值單元。 |
| REQ_F4115.7.3 | 1.0 | System should enable user to view records of stocktake. | Developer | `INV.STOCKTAKE.VIEW` 只可分頁查目前公司的盤點單，按單號／備註、日期、狀態篩選；詳情按行號顯示倉庫、儲位、物料、帳面、實盤、批序號、重盤標記與最新 SYS 審批狀態；其他公司 ID 回 404。 |
| REQ_F4115.7.4 | 1.0 | System should enable user to edit records of stocktake. | Developer | `INV.STOCKTAKE.EDIT` 以版本整組保存草稿或已駁回單的日期、備註與實盤數量（0 至六位小數，序號 0／1），可新增範圍內倉庫的實地找到行（帳面 0）；駁回修訂回草稿且保留審批歷史；可重新快照：更新帳面、保留實盤、帳面有變的行標記重盤、加入新鍵、移除帳面 0 且未盤的鍵；版本衝突 409；待審或已過帳不得修改。 |
| REQ_F4115.7.5 | 1.0 | System should enable user to approve requests of stocktake. | Developer | `INV.STOCKTAKE.SUBMIT` 送審時每行與每個批序號都須已盤（否則 422 `COUNT_MISSING`），並重算範圍內現存量及檢查快照後異動，任一不符回 422 `INV.STOCKTAKE.SNAPSHOT_STALE` 且列出變動鍵；SYS 最終核准與 Posted 同交易，按行把批序號正差異合計為 `In`、負差異合計為 `Out`（來源 `STOCKTAKE`，成本空），差異 0 不寫異動；盤虧導致負結存或批序號不足時全部回滾；重複決定不重複異動。 |
| REQ_F4115.7.6 | 1.0 | System should enable user to reject requests of stocktake. | Developer | SYS 審批人可駁回待審盤點單，保留意見與歷史，單據轉 Rejected，無任何異動；修訂後才可再送審，範圍仍保持互斥。 |
| REQ_F4115.7.7 | 1.0 | System should enable user to delete records of stocktake. | Developer | `INV.STOCKTAKE.DELETE` 僅可依版本軟刪除草稿，保留單號與稽核資訊，並釋放範圍互斥；待審、駁回未修訂或已過帳不能刪除。 |

BlueWhale REQ_F4115.7.1 原文誤寫為 inventory transfer，MyERP 改為 stocktake；7.3–7.7 依 ADR-002 規則 3 追加。快照、過期偵測與盈虧過帳見 ADR-062。設計追溯：REQ_DS43.151–153、REQ_IO_A5.18.1–5.18.11、REQ_IO_A5.24.1–5.24.5、INV-STK-001～011。

## WU-INV-009 庫存調撥（009d 設計；009a 後端；009b 介面）

| Sub-function No. | Sub-function |
|---|---|
| 5.6 | Inventory Transfer |

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4115.6.1 | 1.0 | System should enable user to view records of inventory transfer. | Developer | `INV.TRANSFER.VIEW` 只可分頁查目前公司的單據，按單號／備註、日期、狀態篩選；詳情按行號顯示來源／目的倉庫與儲位、數量、批序號及最新 SYS 審批狀態；其他公司 ID 回 404；列印及篩選 CSV 須有 VIEW。 |
| REQ_F4115.6.2 | 1.0 | System should enable user to create records of inventory transfer. | Developer | `INV.TRANSFER.CREATE` 在交易內取號建立草稿，1–1000 行；每行同一物料由來源結存鍵移到不同的目的結存鍵，數量正數且最多六位小數；兩側 MD 引用有效且屬目前公司，儲位屬相應倉庫；草稿不寫庫存異動，可暫缺批序號。 |
| REQ_F4115.6.3 | 1.0 | System should enable user to edit records of inventory transfer. | Developer | `INV.TRANSFER.EDIT` 以版本整組替換草稿或已駁回的日期、備註與明細；版本衝突回 409；駁回修訂回草稿且保留審批歷史；待審或已過帳不得修改。 |
| REQ_F4115.6.4 | 1.0 | System should enable user to approve requests of inventory transfer. | Developer | `INV.TRANSFER.SUBMIT` 送 SYS 工作流；最終核准與狀態變為 Posted、每行相同數量的來源 Out／目的 In 在同一交易完成，總量及每批序號守恆；來源不足或批序號不可用時全部回滾；併發爭同一來源庫存只一方成功；重複決定／過帳不重複異動。 |
| REQ_F4115.6.5 | 1.0 | System should enable user to reject requests of inventory transfer. | Developer | SYS 審批人可駁回待審單據，保留意見與歷史、單據轉 Rejected，無任何庫存異動；修訂後才可再送審。 |
| REQ_F4115.6.6 | 1.0 | System should enable user to delete records of inventory transfer. | Developer | `INV.TRANSFER.DELETE` 僅可依版本軟刪除草稿，保留單號與稽核資訊；待審、駁回未修訂或已過帳不能刪除。 |

調撥出入雙向異動與批序號配對見 ADR-061；資料及 API 已於 009a 實作，三語介面與列印／匯出已於 009b 實作。設計追溯：REQ_DS43.148–150、REQ_IO_A5.15.1–5.17.4、INV-TRF-001～009。

## WU-INV-008 批號與序號追蹤（008a MD 追蹤方式；008b 後端；008c 介面）

| Sub-function No. | Sub-function |
|---|---|
| 5.24 | Lot and Serial Tracking（MyERP 擴充；BlueWhale 無對應介面） |

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4115.24.1 | 1.0 | System should enable lot-tracked items to record lot numbers with optional manufacture and expiry dates when stock is received. | Developer | 物料追蹤方式為 `Lot` 時，入庫每行須帶批號，合計等於數量，否則 422 `INV.TRACKING.REQUIRED`；批號 1–50 字元、去除前後空白、同公司同物料不分大小寫唯一；首次入庫建立批號；之後同批號入庫省略日期則沿用、提供不同日期回 422 `INV.LOT.ATTRIBUTE_MISMATCH`；有效期限不得早於生產日期（400）。 |
| REQ_F4115.24.2 | 1.0 | System should enable serial-tracked items to record one serial number per base unit when stock is received. | Developer | 物料追蹤方式為 `Serial` 時數量必須為整數，每個序號數量 1（400）；序號 1–100 字元、同公司同物料不分大小寫唯一；序號在公司任何倉庫仍有結存時入庫回 409 `INV.SERIAL.IN_STOCK`；出庫後可再入庫；兩個連線並發入庫同一序號只一方成功。 |
| REQ_F4115.24.3 | 1.0 | System should check lot and serial availability at the issuing warehouse and location before stock is issued. | Developer | 出庫批號在該物料＋倉庫＋儲位不足回 422 `INV.LOT.INSUFFICIENT`；序號不在該結存鍵回 422 `INV.SERIAL.NOT_AVAILABLE`；整張來源全部或全不過帳；結存一律由只增不改的批序號明細即時加總。 |
| REQ_F4115.24.4 | 1.0 | System should require lot or serial numbers that match the item tracking mode on inventory documents. | Developer | 不追蹤（`None`）的物料帶批序號、批號物料帶序號或反之回 400；其他入庫／其他出庫草稿可暫缺批序號，送審時不完整回 422 `INV.TRACKING.REQUIRED`；期初預覽逐行回報 `INV.TRACKING.REQUIRED`／`INV.TRACKING.MODE_MISMATCH`；同一來源同物料同方向序號不得重複；明細與單據一起保存並於核准／提交時轉為過帳批序號；冪等重放比較包含批序號。 |
| REQ_F4115.24.5 | 1.0 | System should enable user to query lots and serial numbers with their on-hand status. | Developer | 有 `INV.TRACKING.VIEW` 才可查詢；批號列表含生產日期、有效期限與全公司結存，序號列表含是否在庫；可依物料、關鍵字、只列在庫篩選並分頁；跨公司不可見。 |
| REQ_F4115.24.6 | 1.0 | System should enable user to trace a lot or serial number to its current locations and movement history. | Developer | 追溯回傳各倉庫／儲位非零結存與異動歷史（來源類型、來源 ID、行號、業務日期、方向、倉庫、儲位、數量），新到舊最多 1000 筆並標示是否截斷；其他公司或不存在回 404 `INV.LOT.NOT_FOUND`／`INV.SERIAL.NOT_FOUND`。 |

008a 已在 MD 物料主檔提供 `tracking_mode`（REQ_F4118.3.7）。008b 已實作資料表、過帳契約、其他入庫／其他出庫／期初的批序號採集與核對、追溯 API 及真實 SQL／HTTP 驗收（INV-LOT-001～015）；008c 提供各單據頁的批序號輸入與追溯三語頁（ADR-060）。

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
