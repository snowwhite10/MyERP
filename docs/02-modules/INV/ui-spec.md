# INV 介面規格

## WU-INV-009b 庫存調撥介面（設計，尚未實作）

MyERP 介面次序固定 4.4.1.5.15 建立、5.16 查看、5.17 編輯。路由 `/inv/transfers/new`、`/inv/transfers`、`/inv/transfers/:id/edit`；三語及暗色模式沿用 INV 單據頁。MD API 提供物料、兩側倉庫／儲位名稱，兩側儲位選項各按所選倉庫過濾。數量保持六位十進位文字，批序號依物料追蹤方式輸入，送審前顯示必填與合計錯誤；批序號原樣從來源轉到目的，不提供目的端改號欄位。列表以 API 分頁，列印與防公式注入 CSV 只使用已保存單據並依目前篩選逐頁取完。

| Interface No. | Interface | Route | Related REQ_F | Access |
|---|---|---|---|---|
| 5.15 | Inventory Transfer Create Interface | `/inv/transfers/new` | REQ_F4115.6.2 | `INV.TRANSFER.CREATE` |
| 5.16 | Inventory Transfer View Interface | `/inv/transfers` | REQ_F4115.6.1, REQ_F4115.6.4, REQ_F4115.6.5, REQ_F4115.6.6 | `INV.TRANSFER.VIEW`；操作另需 SUBMIT／DELETE |
| 5.17 | Inventory Transfer Edit Interface | `/inv/transfers/:id/edit` | REQ_F4115.6.3 | `INV.TRANSFER.EDIT` |

| Requirement ID | Item | Description | Purpose | Format | Valid Range | Related I/O | Author | Related REQ_F |
|---|---|---|---|---|---|---|---|---|
| REQ_IO_A5.15.1 | Business Date (Date Picker) | Set transfer business date. | Date both movements. | Date | Valid business date | None | Developer | REQ_F4115.6.2 |
| REQ_IO_A5.15.2 | Item (Dropdown) | Select an active company item per line. | Identify stock. | MD ID | Active item | None | Developer | REQ_F4115.6.2 |
| REQ_IO_A5.15.3 | Source Warehouse / Location (Dropdown) | Select the issuing warehouse and optional bin. | Set source balance key. | MD IDs | Current company, active | REQ_IO_A5.15.2 | Developer | REQ_F4115.6.2 |
| REQ_IO_A5.15.4 | Destination Warehouse / Location (Dropdown) | Select a different receiving balance key. | Set destination. | MD IDs | Different key, active | REQ_IO_A5.15.3 | Developer | REQ_F4115.6.2 |
| REQ_IO_A5.15.5 | Quantity / Tracking (Input) | Enter six-decimal base quantity and source lot or serial numbers. | Transfer exact tracked units. | Decimal(19,6) / text | Positive; tracking total equals quantity | REQ_IO_A5.15.2 | Developer | REQ_F4115.6.2, REQ_F4115.6.4 |
| REQ_IO_A5.15.6 | Save Draft (Button) | Validate and save without changing stock. | Prepare approval. | Button | CREATE | REQ_IO_A5.15.1 | Developer | REQ_F4115.6.2 |
| REQ_IO_A5.16.1 | Filters (Input) | Filter by document number, date and status. | Find transfers. | Text / date / status | From ≤ To | None | Developer | REQ_F4115.6.1 |
| REQ_IO_A5.16.2 | Transfer List (Table) | Show paged company transfers, quantity and approval state. | Review records. | Table | VIEW | REQ_IO_A5.16.1 | Developer | REQ_F4115.6.1 |
| REQ_IO_A5.16.3 | Detail (Panel) | Show stored lines, both locations, tracking and approval. | Inspect movement pair. | Panel | VIEW | REQ_IO_A5.16.2 | Developer | REQ_F4115.6.1 |
| REQ_IO_A5.16.4 | Submit / Delete (Button) | Submit a draft or delete a draft with confirmation. | Control lifecycle. | Button | SUBMIT / DELETE | REQ_IO_A5.16.2 | Developer | REQ_F4115.6.4, REQ_F4115.6.6 |
| REQ_IO_A5.16.5 | Print / Export (Button) | Print detail or export filtered records to safe UTF-8 CSV. | Keep transfer records. | Button | VIEW | REQ_IO_A5.16.2 | Developer | REQ_F4115.6.1 |
| REQ_IO_A5.17.1 | Business Date / Remark (Input) | Revise a draft or rejected transfer. | Correct header. | Date / text | Editable status | None | Developer | REQ_F4115.6.3 |
| REQ_IO_A5.17.2 | Source / Destination (Dropdown) | Revise both balance keys per line. | Correct route. | MD IDs | Different active keys | REQ_IO_A5.17.1 | Developer | REQ_F4115.6.3 |
| REQ_IO_A5.17.3 | Quantity / Tracking (Input) | Revise quantity and matching lot or serial list. | Correct moved units. | Decimal(19,6) / text | Tracking total equals quantity at submit | REQ_IO_A5.17.2 | Developer | REQ_F4115.6.3 |
| REQ_IO_A5.17.4 | Save Revision (Button) | Replace draft lines with version check. | Persist correction. | Button | EDIT | REQ_IO_A5.17.1 | Developer | REQ_F4115.6.3 |

WU-INV-006a 無新介面；006b 完成 4.4.1.5.1 Inventory View Interface。API 回應使用 MD ID 及過帳時代碼快照，頁面按權限顯示結存／異動分頁。

## WU-INV-007b 物料期初庫存介面

對應 BlueWhale 4.4.1.8.14 Product Initial Stock Interface。列表路由 `/inv/opening-stocks`（庫存選單「期初庫存」），批次輸入 `/inv/opening-stocks/new`。物料、倉庫、儲位以 MD API 選擇（只列啟用）並顯示「代碼 · 名稱」。每個批次進入頁面時以 `crypto.randomUUID()` 產生 `requestKey`；網絡失敗重試沿用同一鍵（伺服器 200 `replayed` 視為成功）；409 `REQUEST_CONFLICT` 換新鍵並要求重新預覽；422 `HAS_ERRORS` 自動重新預覽顯示逐行錯誤。任何修改都使預覽失效，只有預覽零錯誤且內容未變才可提交。列表列印單批明細，CSV 依目前篩選逐頁取得全部批次並防公式注入（ADR-056）。批次提交即過帳，無修改／刪除。

| Interface No. | Interface | Route | Related REQ_F | Access |
|---|---|---|---|---|
| 5.8 | Product Initial Stock View Interface | `/inv/opening-stocks` | REQ_F4115.23.1 | `INV.OPENING_STOCK.VIEW`；新增按鈕另需 CREATE |
| 5.9 | Product Initial Stock Create Interface | `/inv/opening-stocks/new` | REQ_F4115.23.2, REQ_F4115.23.3, REQ_F4115.23.4 | `INV.OPENING_STOCK.CREATE` |

| Requirement ID | Item | Description | Purpose | Format | Valid Range | Related I/O | Author | Related REQ_F |
|---|---|---|---|---|---|---|---|---|
| REQ_IO_A5.8.1 | Opening Date Range (Date Picker) | Filter batches by opening date inclusively. | Narrow results. | Date | From ≤ To | None | Developer | REQ_F4115.23.1 |
| REQ_IO_A5.8.2 | Item / Warehouse (Dropdown) | Filter batches containing the MD item or warehouse. | Narrow results. | ID | Current company | None | Developer | REQ_F4115.23.1 |
| REQ_IO_A5.8.3 | Keyword (Textbox) | Match remark or request key. | Find a batch. | Text | Partial match | None | Developer | REQ_F4115.23.1 |
| REQ_IO_A5.8.4 | Batch List (Table) | Paged batches with lines, quantity, amount, remark and posted time. | Review opening stock. | Table | Authorized company | REQ_IO_A5.8.1 | Developer | REQ_F4115.23.1 |
| REQ_IO_A5.8.5 | Batch Detail (Panel) | Lines by line number with MD names, quantity, unit cost and amount. | Inspect a batch. | Table | Authorized company | REQ_IO_A5.8.4 | Developer | REQ_F4115.23.1 |
| REQ_IO_A5.8.6 | Print / Export (Button) | Print one batch; export filtered batches as formula-safe UTF-8 CSV. | Keep records. | Button | Authorized | REQ_IO_A5.8.4 | Developer | REQ_F4115.23.1 |
| REQ_IO_A5.9.1 | Opening Date (Date Picker) | Date the opening balance takes effect. | Set start of stock history. | Date | ≤ today; ≤ earliest movement | None | Developer | REQ_F4115.23.2, REQ_F4115.23.3 |
| REQ_IO_A5.9.2 | Item / Warehouse / Location (Dropdown) | Active MD references per line; location limited to the warehouse. | Identify balance key. | ID | Active, current company | REQ_IO_A5.9.1 | Developer | REQ_F4115.23.3 |
| REQ_IO_A5.9.3 | Quantity / Unit Cost (Textbox) | Base-unit quantity and unit cost. | Value opening stock. | Decimal(6) | Quantity > 0; cost ≥ 0 | REQ_IO_A5.9.2 | Developer | REQ_F4115.23.3 |
| REQ_IO_A5.9.4 | Preview (Button) | Validate without saving; show per-line trilingual errors and totals. | Catch errors before posting. | Button | CREATE | REQ_IO_A5.9.1 | Developer | REQ_F4115.23.2 |
| REQ_IO_A5.9.5 | Submit (Button) | Post a clean, unchanged preview once; retry reuses the batch request key. | Post opening stock idempotently. | Button | Clean preview | REQ_IO_A5.9.4 | Developer | REQ_F4115.23.3, REQ_F4115.23.4 |

## WU-INV-006b 庫存查詢介面

路由 `/inv/stock-query`；任一查看權限可進入，結存與異動分頁各按自己的權限顯示。物料、倉庫、儲位名稱由 MD API 取得；同公司篩選、分頁與日期／方向查詢由 INV API 執行。列印及供 Excel 開啟的 UTF-8 CSV 按目前分頁以外的完整篩選結果逐頁擷取，CSV 文字欄位防公式注入（ADR-056）。

| Interface No. | Interface | Route | Related REQ_F | Access |
|---|---|---|---|---|
| 5.1 | Inventory View Interface | `/inv/stock-query` | REQ_F4115.1.1, REQ_F4115.1.2 | `INV.INVENTORY.VIEW` 或 `INV.MOVEMENT.VIEW`；分頁各自授權 |

| Requirement ID | Item | Description | Purpose | Format | Valid Range | Related I/O | Author | Related REQ_F |
|---|---|---|---|---|---|---|---|---|
| REQ_IO_A5.1.1 | Balance Tab (Button) | Show current on-hand balance. | Inspect stock. | Button | INVENTORY.VIEW | None | Developer | REQ_F4115.1.1 |
| REQ_IO_A5.1.2 | Movement Tab (Button) | Show posted movement history. | Trace stock changes. | Button | MOVEMENT.VIEW | None | Developer | REQ_F4115.1.2 |
| REQ_IO_A5.1.3 | Item (Dropdown) | Filter by MD item. | Narrow results. | ID | Current company | None | Developer | REQ_F4115.1.1, REQ_F4115.1.2 |
| REQ_IO_A5.1.4 | Warehouse (Dropdown) | Filter by MD warehouse. | Narrow results. | ID | Current company | None | Developer | REQ_F4115.1.1, REQ_F4115.1.2 |
| REQ_IO_A5.1.5 | Location (Dropdown) | Filter by warehouse location. | Narrow results. | ID | Selected warehouse | REQ_IO_A5.1.4 | Developer | REQ_F4115.1.1, REQ_F4115.1.2 |
| REQ_IO_A5.1.6 | Date Range (Date Picker) | Filter movement business dates inclusively. | Limit history. | Date | From ≤ To | REQ_IO_A5.1.2 | Developer | REQ_F4115.1.2 |
| REQ_IO_A5.1.7 | Direction (Dropdown) | Filter inward or outward movements. | Limit history. | In / Out | Posted directions | REQ_IO_A5.1.2 | Developer | REQ_F4115.1.2 |
| REQ_IO_A5.1.8 | Search (Button) | Apply selected filters. | Refresh result. | Button | Authorized | REQ_IO_A5.1.3 | Developer | REQ_F4115.1.1, REQ_F4115.1.2 |
| REQ_IO_A5.1.9 | Stock Results (Table) | Show paged balances or movement details with MD names. | Inspect company stock. | Table | Authorized company | REQ_IO_A5.1.1, REQ_IO_A5.1.2 | Developer | REQ_F4115.1.1, REQ_F4115.1.2 |
| REQ_IO_A5.1.10 | Print (Button) | Print all filtered rows. | Produce stock register. | Button | Authorized | REQ_IO_A5.1.9 | Developer | REQ_F4115.1.1, REQ_F4115.1.2 |
| REQ_IO_A5.1.11 | Export (Button) | Download safe CSV for Excel. | Analyze filtered stock. | Button | Authorized | REQ_IO_A5.1.9 | Developer | REQ_F4115.1.1, REQ_F4115.1.2 |

WU-INV-001 無使用者頁面；前端在 WU-INV-004 起隨業務單據加入三語、暗色模式、路由權限與 Playwright 驗收。庫存查詢介面由 WU-INV-006 實作，期初庫存介面 4.4.1.8.14 由 WU-INV-007 實作。

## WU-INV-004 其他入庫介面（WU-INV-004b 實作）

MyERP 介面次序固定為 4.4.1.5.2 建立、5.3 查看、5.4 編輯；BlueWhale 4.4／4.5 原次序不一致，採 `reference-notes.md` 已整理的 MyERP 次序。三頁均須三語、暗色模式與鍵盤可操作，無權限時隱藏操作。

前端 lazy 路由為 `/inv/other-receipts`、`/new`、`/:id/edit`，各自以 VIEW／CREATE／EDIT 守衛。列表以 API 分頁，支援關鍵字、日期及狀態篩選；草稿列可送審與刪除，駁回列可修訂；查看明細和列印需 VIEW。物料、倉庫、儲位選項與名稱由 MD API 取得，表單按倉庫載入儲位。六位小數的數量與單價在提交前檢查。列印與匯出格式見 ADR-056。

| Interface No. | Interface | Route | Related REQ_F | Access |
|---|---|---|---|---|
| 5.2 | Other Inbound Create Interface | `/inv/other-receipts/new` | REQ_F4115.2.2 | `INV.OTHER_RECEIPT.CREATE` |
| 5.3 | Other Inbound View Interface | `/inv/other-receipts` | REQ_F4115.2.1, REQ_F4115.2.4, REQ_F4115.2.5, REQ_F4115.2.6 | `INV.OTHER_RECEIPT.VIEW`；操作另需 SUBMIT／DELETE |
| 5.4 | Other Inbound Edit Interface | `/inv/other-receipts/:id/edit` | REQ_F4115.2.3 | `INV.OTHER_RECEIPT.EDIT` |

| Requirement ID | Item | Description | Purpose | Format | Valid Range | Related I/O | Author | Related REQ_F |
|---|---|---|---|---|---|---|---|---|
| REQ_IO_A5.2.1 | Business Date (Date Picker) | Receipt business date. | Set posting date. | Date | Valid business date | None | Developer | REQ_F4115.2.2 |
| REQ_IO_A5.2.2 | Warehouse (Dropdown) | Select a company warehouse. | Choose receipt destination. | ID | Active warehouse | None | Developer | REQ_F4115.2.2 |
| REQ_IO_A5.2.3 | Item (Dropdown) | Select a company item for each row. | Identify stock. | ID | Active item | None | Developer | REQ_F4115.2.2 |
| REQ_IO_A5.2.4 | Location (Dropdown) | Select a warehouse location when required. | Identify stock bin. | ID | Active location in warehouse | REQ_IO_A5.2.2 | Developer | REQ_F4115.2.2 |
| REQ_IO_A5.2.5 | Quantity (Input) | Enter basic-unit receipt quantity. | Increase stock after approval. | Decimal (19,6) | > 0 | REQ_IO_A5.2.3 | Developer | REQ_F4115.2.2 |
| REQ_IO_A5.2.6 | Unit Cost (Input) | Enter non-negative receipt cost. | Capture cost basis. | Decimal (19,6) | >= 0 | REQ_IO_A5.2.3 | Developer | REQ_F4115.2.2 |
| REQ_IO_A5.2.7 | Save Draft (Button) | Save without posting stock. | Create receipt draft. | Button | Authorized | REQ_IO_A5.2.1 | Developer | REQ_F4115.2.2 |
| REQ_IO_A5.3.1 | Receipt List (Table) | Company-scoped paged receipts. | View records and state. | Table | Current company | None | Developer | REQ_F4115.2.1 |
| REQ_IO_A5.3.2 | Submit (Button) | Submit draft for SYS approval. | Start approval. | Button | Draft only | REQ_IO_A5.3.1 | Developer | REQ_F4115.2.4 |
| REQ_IO_A5.3.3 | Print (Button) | Print saved receipt. | Produce document copy. | Button | Authorized | REQ_IO_A5.3.1 | Developer | REQ_F4115.2.1 |
| REQ_IO_A5.3.4 | Export (Button) | Export filtered records. | Share receipt register. | Button | Authorized | REQ_IO_A5.3.1 | Developer | REQ_F4115.2.1 |
| REQ_IO_A5.3.5 | Delete (Button) | Soft-delete a draft. | Withdraw unused receipt. | Button | Draft only | REQ_IO_A5.3.1 | Developer | REQ_F4115.2.6 |
| REQ_IO_A5.4.1 | Receipt Lines (Table) | Replace all draft lines and show validation. | Revise item, quantity and cost. | Table | Draft only | REQ_IO_A5.2.3 | Developer | REQ_F4115.2.3 |
| REQ_IO_A5.4.2 | Save Changes (Button) | Save with version check. | Prevent lost updates. | Button | Draft only | REQ_IO_A5.4.1 | Developer | REQ_F4115.2.3 |

## WU-INV-005b 其他出庫介面（已實作）

介面次序固定為 5.5 建立、5.6 查看、5.7 編輯；路由 `/inv/other-issues/new`、`/inv/other-issues`、`/inv/other-issues/:id/edit`。三語、暗色模式、權限守衛、MD 名稱及列印／CSV 匯出沿用其他入庫模式；提交時後端可能因庫存不足回 422，須保留草稿內容並顯示本地化錯誤。單價顯示為「估算單價」，不可暗示已入帳成本。

| Interface No. | Interface | Route | Related REQ_F | Access |
|---|---|---|---|---|
| 5.5 | Other Outbound Create Interface | `/inv/other-issues/new` | REQ_F4115.3.2 | `INV.OTHER_ISSUE.CREATE` |
| 5.6 | Other Outbound View Interface | `/inv/other-issues` | REQ_F4115.3.1, REQ_F4115.3.4, REQ_F4115.3.5, REQ_F4115.3.6 | `INV.OTHER_ISSUE.VIEW`；操作另需 SUBMIT／DELETE |
| 5.7 | Other Outbound Edit Interface | `/inv/other-issues/:id/edit` | REQ_F4115.3.3 | `INV.OTHER_ISSUE.EDIT` |

| Requirement ID | Item | Description | Purpose | Format | Valid Range | Related I/O | Author | Related REQ_F |
|---|---|---|---|---|---|---|---|---|
| REQ_IO_A5.5.1 | Business Date (Date Picker) | Issue business date. | Set posting date. | Date | Valid business date | None | Developer | REQ_F4115.3.2 |
| REQ_IO_A5.5.2 | Warehouse (Dropdown) | Select company warehouse. | Identify source warehouse. | ID | Active warehouse | None | Developer | REQ_F4115.3.2 |
| REQ_IO_A5.5.3 | Item (Dropdown) | Select company item. | Identify stock. | ID | Active item | None | Developer | REQ_F4115.3.2 |
| REQ_IO_A5.5.4 | Location (Dropdown) | Select source location when required. | Identify stock bin. | ID | Active location | REQ_IO_A5.5.2 | Developer | REQ_F4115.3.2 |
| REQ_IO_A5.5.5 | Quantity (Input) | Enter basic-unit issue quantity. | Decrease stock after approval. | Decimal (19,6) | > 0 | REQ_IO_A5.5.3 | Developer | REQ_F4115.3.2 |
| REQ_IO_A5.5.6 | Estimated Unit Cost (Input) | Enter non-negative approval estimate. | Calculate approval threshold. | Decimal (19,6) | >= 0 | REQ_IO_A5.5.3 | Developer | REQ_F4115.3.2 |
| REQ_IO_A5.5.7 | Save Draft (Button) | Save without posting stock. | Create issue draft. | Button | Authorized | REQ_IO_A5.5.1 | Developer | REQ_F4115.3.2 |
| REQ_IO_A5.6.1 | Issue List (Table) | Company-scoped paged issues. | View records. | Table | Current company | None | Developer | REQ_F4115.3.1 |
| REQ_IO_A5.6.2 | Submit (Button) | Submit draft to SYS. | Start approval. | Button | Draft only | REQ_IO_A5.6.1 | Developer | REQ_F4115.3.4 |
| REQ_IO_A5.6.3 | Print (Button) | Print saved issue. | Produce document copy. | Button | Authorized | REQ_IO_A5.6.1 | Developer | REQ_F4115.3.1 |
| REQ_IO_A5.6.4 | Export (Button) | Export filtered issues. | Share issue register. | Button | Authorized | REQ_IO_A5.6.1 | Developer | REQ_F4115.3.1 |
| REQ_IO_A5.6.5 | Delete (Button) | Soft-delete draft. | Withdraw unused issue. | Button | Draft only | REQ_IO_A5.6.1 | Developer | REQ_F4115.3.6 |
| REQ_IO_A5.7.1 | Issue Lines (Table) | Replace all draft lines. | Revise item and quantity. | Table | Draft or rejected | REQ_IO_A5.5.3 | Developer | REQ_F4115.3.3 |
| REQ_IO_A5.7.2 | Save Changes (Button) | Save with version check. | Prevent lost updates. | Button | Draft or rejected | REQ_IO_A5.7.1 | Developer | REQ_F4115.3.3 |

## WU-INV-008c 批號／序號輸入及追溯

物料 `trackingMode` 決定輸入區：`Lot` 可逐列輸入批號、數量，入庫可填生產日期及有效期限；`Serial` 用多行貼上，每行一個序號，數量固定 1。三種單據提交前以基本單位六位小數檢查合計，期初預覽同樣檢查。追溯列表使用伺服器分頁，明細顯示目前位置及最近最多 1000 筆異動，截斷時顯示提示。

| Interface No. | Interface | Route | Related REQ_F | Access |
|---|---|---|---|---|
| 5.10 | Lot and Serial Trace Interface | `/inv/tracking` | REQ_F4115.24.5, REQ_F4115.24.6 | `INV.TRACKING.VIEW` |

| Requirement ID | Item | Description | Purpose | Format | Valid Range | Related I/O | Author | Related REQ_F |
|---|---|---|---|---|---|---|---|---|
| REQ_IO_A5.2.8 | Lot or Serial (Input) | Capture receipt tracking entries and optional lot dates. | Track inbound stock. | Lot rows or serial lines | Sum equals line quantity | REQ_IO_A5.2.5 | Developer | REQ_F4115.24.1, REQ_F4115.24.2, REQ_F4115.24.4 |
| REQ_IO_A5.5.8 | Lot or Serial (Input) | Select numbers at the issue location. | Validate outbound tracking. | Lot rows or serial lines | Sum equals line quantity | REQ_IO_A5.5.5 | Developer | REQ_F4115.24.3, REQ_F4115.24.4 |
| REQ_IO_A5.9.6 | Lot or Serial (Input) | Capture opening stock tracking entries. | Establish tracked opening stock. | Lot rows or serial lines | Sum equals line quantity | None | Developer | REQ_F4115.24.1, REQ_F4115.24.2, REQ_F4115.24.4 |
| REQ_IO_A5.10.1 | Tracking Type (Button) | Switch between lot and serial lists. | Select tracking type. | Button | Lot / Serial | None | Developer | REQ_F4115.24.5 |
| REQ_IO_A5.10.2 | Item (Dropdown) | Filter by company item. | Narrow trace results. | ID | Current company | None | Developer | REQ_F4115.24.5 |
| REQ_IO_A5.10.3 | Number Search (Input) | Filter lot or serial number. | Find tracking number. | Text | Up to 100 characters | None | Developer | REQ_F4115.24.5 |
| REQ_IO_A5.10.4 | In Stock Only (Checkbox) | Exclude fully issued numbers. | Show current stock. | Boolean | True / false | None | Developer | REQ_F4115.24.5 |
| REQ_IO_A5.10.5 | Tracking Results (Table) | Show paged lot or serial records. | Inspect on-hand status. | Table | Current company | REQ_IO_A5.10.1 | Developer | REQ_F4115.24.5 |
| REQ_IO_A5.10.6 | Trace Detail (Table) | Show current locations and movement history with truncation warning. | Trace provenance. | Table | Most recent 1000 movements | REQ_IO_A5.10.5 | Developer | REQ_F4115.24.6 |
