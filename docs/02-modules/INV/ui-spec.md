# INV 介面規格

## WU-INV-013 安全庫存介面（013b 已實作及審查）

| Interface No. | Interface | Route | Related REQ_F | Access |
|---|---|---|---|---|
| 5.26 | Safety Stock View Interface | `/inv/safety-stock` | REQ_F4115.26.1, REQ_F4115.26.2, REQ_F4115.26.3 | `INV.SAFETY_STOCK.VIEW`；編輯操作另需 EDIT |

| Requirement ID | Item | Description | Purpose | Format | Valid Range | Related I/O | Author | Related REQ_F |
|---|---|---|---|---|---|---|---|---|
| REQ_IO_A5.26.1 | Filters (Input) | Search item code/name and warehouse; optionally show only low stock. | Narrow company thresholds. | Text / MD selector / checkbox | Authorized company | None | Developer | REQ_F4115.26.2 |
| REQ_IO_A5.26.2 | Threshold List (Table) | Show item, warehouse, saved minimum, on-hand, low status, suggested quantity, inactive references and query time. | Explain each warning. | Server-paged table | VIEW | REQ_IO_A5.26.1 | Developer | REQ_F4115.26.2, REQ_F4115.26.3 |
| REQ_IO_A5.26.3 | Add Threshold (Button) | Open a form for an active item and warehouse. | Configure a new minimum. | Button | EDIT | REQ_IO_A5.26.2 | Developer | REQ_F4115.26.1 |
| REQ_IO_A5.26.4 | Item / Warehouse (Dropdown) | Select authorized active references through paged INV selectors. | Choose one stock scope. | MD IDs | Current company | REQ_IO_A5.20.1 | Developer | REQ_F4115.26.1 |
| REQ_IO_A5.26.5 | Minimum Quantity (Input) | Enter positive base-unit quantity with at most six decimals. | Set the warning boundary. | Decimal(19,6) | > 0 | REQ_IO_A5.26.4 | Developer | REQ_F4115.26.1 |
| REQ_IO_A5.26.6 | Save / Edit (Button) | Submit create or version-checked update and show a result matching actual changes. | Persist threshold. | Button | EDIT; active references | REQ_IO_A5.26.5 | Developer | REQ_F4115.26.1 |
| REQ_IO_A5.26.7 | Remove (Button) | Confirm version-checked soft deletion with reason when disabled. | Stop future low-stock alerts. | Button | EDIT | REQ_IO_A5.26.2 | Developer | REQ_F4115.26.1 |
| REQ_IO_A5.26.8 | Replenishment Hint (Label) | State that shortage is a suggestion as of query time and does not create a purchase order. | Prevent misleading action expectations. | Localized text | Low rows only | REQ_IO_A5.26.2 | Developer | REQ_F4115.26.3 |

介面支援繁中／簡中／英文與亮暗模式。無門檻、篩選無結果、無權限顯示不同空狀態；409 版本衝突提示重新整理，422 停用引用指明物料或倉庫，5xx 含 traceId，逾時顯示可重試；停用按鈕說明原因。資料變更為 0 筆時不可顯示成功。

## WU-INV-011d／011c 組裝入庫介面（011c 進行中）

| Interface No. | Interface | Related REQ_F |
|---|---|---|
| 5.9 | Product Assembly View Interface | REQ_F4115.4.1, REQ_F4115.4.4, REQ_F4115.4.5, REQ_F4115.4.6 |
| 5.10 | Product Assembly Create Interface | REQ_F4115.4.2, REQ_F4115.4.4 |
| 5.11 | Product Assembly Edit Interface | REQ_F4115.4.3, REQ_F4115.4.4 |

對應 BlueWhale 5.9 Create／5.10 View 的原始排序；MyERP 依 `reference-notes.md` 已保留的編號採 5.9 View／5.10 Create，5.11 Edit，三份生成文件共用此清單。介面三語、亮暗模式；唯讀權限只能查看，不顯示可操作按鈕。停用按鈕須提示缺權或狀態原因；API 拒絕顯示欄位／行原因，5xx 顯示 traceId。

| Requirement ID | Item | Description | Purpose | Format | Valid Range | Related I/O | Author | Related REQ_F |
|---|---|---|---|---|---|---|---|---|
| REQ_IO_A5.9.1 | Filters (Input) | Search number, date, status and finished item on server pages. | Find assemblies. | Text / date / ID | Current company | None | Developer | REQ_F4115.4.1 |
| REQ_IO_A5.9.2 | Assembly List (Table) | Show document number, date, finished item, quantity, status and version. | Open saved records. | Paged table | VIEW | REQ_IO_A5.9.1 | Developer | REQ_F4115.4.1 |
| REQ_IO_A5.9.3 | Assembly Detail (Panel) | Show saved finished and component lines, tracking, costs and approval history. | Review the actual posted result. | Panel | VIEW | REQ_IO_A5.9.2 | Developer | REQ_F4115.4.1 |
| REQ_IO_A5.9.4 | Submit / Delete (Button) | Submit draft or confirm draft deletion; show state and permission reasons when disabled. | Control document lifecycle. | Button | SUBMIT / DELETE, draft only | REQ_IO_A5.9.2 | Developer | REQ_F4115.4.4, REQ_F4115.4.6 |
| REQ_IO_A5.9.5 | Print / Export (Button) | Print saved detail or export all filtered pages as formula-safe UTF-8 CSV. | Keep review records. | Button | VIEW | REQ_IO_A5.9.2 | Developer | REQ_F4115.4.1 |
| REQ_IO_A5.10.1 | Business Date / Remark (Input) | Set assembly date and optional remark. | Describe assembly work. | Date / text | Valid date; remark ≤ 1000 | None | Developer | REQ_F4115.4.2 |
| REQ_IO_A5.10.2 | Finished Item / Destination (Select) | Choose active finished item, warehouse and optional location through authorized paged selector. | Set the receipt key. | MD IDs | Current company | REQ_IO_A5.20.1 | Developer | REQ_F4115.4.2 |
| REQ_IO_A5.10.3 | Finished Quantity / Tracking (Input) | Enter positive six-place base quantity and lot or serial numbers. | Define output. | Decimal / text | Tracking matches quantity | REQ_IO_A5.10.2 | Developer | REQ_F4115.4.2 |
| REQ_IO_A5.10.4 | Components (Table) | Enter 1–999 component items, source warehouse/location, quantity and tracking. | Define actual consumption. | Paged select / decimal / text | Total ≤ 1000 lines | REQ_IO_A5.20.1 | Developer | REQ_F4115.4.2 |
| REQ_IO_A5.10.5 | Save Draft (Button) | Save entered snapshot without changing stock. | Prepare approval. | Button | CREATE | REQ_IO_A5.10.1 | Developer | REQ_F4115.4.2 |
| REQ_IO_A5.11.1 | Business Date / Remark (Input) | Revise a draft or rejected assembly. | Correct header. | Date / text | Editable state | None | Developer | REQ_F4115.4.3 |
| REQ_IO_A5.11.2 | Finished Line (Input) | Revise item, destination, quantity and tracking. | Correct output. | IDs / decimal / text | Positive; current company | REQ_IO_A5.11.1 | Developer | REQ_F4115.4.3 |
| REQ_IO_A5.11.3 | Component Lines (Table) | Revise component source keys, quantities and tracking. | Correct consumption. | IDs / decimal / text | 1–999 lines | REQ_IO_A5.11.1 | Developer | REQ_F4115.4.3 |
| REQ_IO_A5.11.4 | Save Revision (Button) | Save full replacement with version; rejected document returns to draft. | Preserve a consistent snapshot. | Button | EDIT | REQ_IO_A5.11.2 | Developer | REQ_F4115.4.3 |

## WU-INV-016d Select Data Interface（設計；016b1 前端選項入口、016b2b1 停用引用顯示契約完成，016b2b2–b4 表單接入待實作）

BlueWhale 4.4.1.5.20 的介面名固定為 `Select Data Interface`；MyERP 以共用彈窗在 INV 建立／編輯頁呈現物料、倉庫、儲位三種模式，不建立獨立導航路由。開啟按鈕由所在表單 CREATE／EDIT 權限保護，選項查詢亦由 INV 表單權限保護。彈窗沿用 `erp-select-dialog` 的搜尋、伺服器分頁、確認與取消；三語及暗色用共用 token。實際選擇只更新草稿，保存仍由服務端驗證。既有停用引用需在表單顯示但不進入新選項。

| Interface No. | Interface | Route | Related REQ_F | Access |
|---|---|---|---|---|
| 5.20 | Select Data Interface | INV 單據建立／編輯頁的彈窗 | REQ_F4115.20.1, REQ_F4115.20.2, REQ_F4115.20.3 | 所在單據 CREATE／EDIT；選項 API 同權限 |

| Requirement ID | Item | Description | Purpose | Format | Valid Range | Related I/O | Author | Related REQ_F |
|---|---|---|---|---|---|---|---|---|
| REQ_IO_A5.20.1 | Data Type (Label) | Show Item, Warehouse or Storage Location mode and current warehouse for location mode. | Identify the selection context. | Localized text | Three modes | None | Developer | REQ_F4115.20.1, REQ_F4115.20.2 |
| REQ_IO_A5.20.2 | Keyword (Input) | Search code or name and reset to first page. | Find master data without loading every row. | Text | Trimmed; server paging | REQ_IO_A5.20.1 | Developer | REQ_F4115.20.1, REQ_F4115.20.2 |
| REQ_IO_A5.20.3 | Options (Table) | Show active company options with code and name; location rows belong to selected warehouse. | Choose an eligible ID. | Paged table | Stable code/ID order | REQ_IO_A5.20.2 | Developer | REQ_F4115.20.1, REQ_F4115.20.2 |
| REQ_IO_A5.20.4 | Select (Checkbox) | Mark a row; document lines use one choice at a time. | Stage a selection. | Row ID | Current result or retained selection | REQ_IO_A5.20.3 | Developer | REQ_F4115.20.3 |
| REQ_IO_A5.20.5 | Confirm (Button) | Return the selected ID to the parent draft; location is cleared if warehouse changes. | Apply selection before document save. | Button | Requires selection | REQ_IO_A5.20.4 | Developer | REQ_F4115.20.2, REQ_F4115.20.3 |
| REQ_IO_A5.20.6 | Cancel (Button) | Close without changing the parent draft; Esc has the same result. | Avoid accidental edits. | Button | Always available | None | Developer | REQ_F4115.20.3 |

## WU-INV-015d PUR／SD 核准介面邊界

BlueWhale 的 5.8 Sales Outbound (Approval) 與 5.19 Purchase Inbound (Approval) 保留於基線對照，現階段不分配 INV `REQ_IO_A`：兩個操作畫面要由 SD／PUR 實際單據或 SYS 待審任務提供，使用來源模組權限，不建立可直接呼叫 INV 過帳的頁面。來源模組設計時須將對照表、畫面欄位與截圖追溯至這兩個基線介面；015b 完成前狀態維持未開始。

## WU-INV-014d 估值介面設計（014b 實作）

| Interface No. | Interface | Route | Related REQ_F | Access |
|---|---|---|---|---|
| 5.25 | Inventory Valuation View Interface | `/inv/valuations` | REQ_F4115.25.1, REQ_F4115.25.2, REQ_F4115.25.3 | `INV.VALUATION.VIEW` |

| Requirement ID | Item | Description | Purpose | Format | Valid Range | Related I/O | Author | Related REQ_F |
|---|---|---|---|---|---|---|---|---|
| REQ_IO_A5.25.1 | Item (Select) | Select a company item through the INV valuation options backed by the MD read-only contract; only valuation permission is required. | Choose the cost pool. | MD ID | Positive ID | None | Developer | REQ_F4115.25.1 |
| REQ_IO_A5.25.2 | Posting Cutoff (Date Time Picker) | Set an optional posting time cutoff. | Review a provisional historical value. | Local UTC+8 date and time | Valid datetime | REQ_IO_A5.25.1 | Developer | REQ_F4115.25.1 |
| REQ_IO_A5.25.3 | Valuation Result (Panel) | Show six-place quantity, four-place value, average cost, movement count, cutoff and provisional status. | Review the moving average result. | Decimal / status | Value is blank when Unpriced | REQ_IO_A5.25.1 | Developer | REQ_F4115.25.1, REQ_F4115.25.3 |
| REQ_IO_A5.25.4 | Unpriced Warning (Label) | Show the unpriced quantity and why value is unavailable. | Prevent treating an unknown cost as zero. | Text / decimal(19,6) | Visible when Unpriced | REQ_IO_A5.25.3 | Developer | REQ_F4115.25.2 |

014b 頁面需三語、暗色及按權限顯示；結果固定標「暫估」，不作 FI 已過帳值。無權限者不得進入路由。

## WU-INV-010b 盤點介面（已實作）

4.4.1.5.18 Stocktake Create Interface 沿用 BlueWhale 的範圍查詢與實盤表格。
- 「查詢」按鈕建立快照草稿，網址改為 `/inv/stocktakes/:id/count`，同一元件繼續作為實盤頁；草稿與駁回修訂也用此頁。
- 列表、詳情與盈虧差異報告是 MyERP 新增的 5.24 Stocktake View Interface（ADR-062；5.21–5.23 為期初及批序號擴充介面）。
- 三語與暗色沿用 INV 單據頁；倉庫、類別、物料、儲位名稱由 MD API 取得。
- 實盤數量保持六位十進位文字，差異即時顯示；盤虧以負號表示。重盤行顯示提示，實地新增行可在保存前移除。
- 送審若回 `SNAPSHOT_STALE`，列出變動鍵，並提供「重新快照」按鈕。
- 列表按單號／備註、業務日期及狀態篩選，以 API 分頁；草稿可按版本送審或確認後刪除。詳情顯示 MD 名稱、審批狀態、差異合計及逐批序號，可只列有盈虧異動的行；列印報告及按目前篩選逐頁取完的 UTF-8 CSV 只讀已保存資料，文字欄防公式注入。

| Interface No. | Interface | Route | Related REQ_F | Access |
|---|---|---|---|---|
| 5.18 | Stocktake Create Interface | `/inv/stocktakes/new`、`/inv/stocktakes/:id/count` | REQ_F4115.7.1, REQ_F4115.7.4, REQ_F4115.7.5 | 建立需 `INV.STOCKTAKE.CREATE`；實盤需 EDIT；送審按鈕需 SUBMIT |
| 5.24 | Stocktake View Interface | `/inv/stocktakes` | REQ_F4115.7.2, REQ_F4115.7.3, REQ_F4115.7.5, REQ_F4115.7.6, REQ_F4115.7.7 | `INV.STOCKTAKE.VIEW`；操作另需 SUBMIT／DELETE |

| Requirement ID | Item | Description | Purpose | Format | Valid Range | Related I/O | Author | Related REQ_F |
|---|---|---|---|---|---|---|---|---|
| REQ_IO_A5.18.1 | Warehouse (Select) | Open the paged Select Data Interface for multiple warehouses and show confirmed choices. | Choose one or more warehouses to count. | MD IDs | 1–20 active company warehouses | REQ_IO_A5.20.1 | Developer | REQ_F4115.7.1, REQ_F4115.20.2 |
| REQ_IO_A5.18.2 | Category (Select) | Select labelled "Category". | Limit the count to item categories and their children. | MD IDs | 0–50 categories | None | Developer | REQ_F4115.7.1 |
| REQ_IO_A5.18.3 | Item (Select) | Open the paged Select Data Interface for multiple items and show confirmed choices. | Limit the count to selected items. | MD IDs | 0–500 items | REQ_IO_A5.20.1 | Developer | REQ_F4115.7.1, REQ_F4115.20.1 |
| REQ_IO_A5.18.4 | Search (Button) | Button labeled "Search". | Create the stocktake draft and snapshot of book quantities. | Button | CREATE; up to 5,000 lines | REQ_IO_A5.18.1 | Developer | REQ_F4115.7.1 |
| REQ_IO_A5.18.5 | Stocktaking Inventory (Input) | Input with header "Stock Taking Inventory" in the stocktake table. | Enter the counted quantity of each line, lot or serial. | Decimal(19,6) | ≥ 0; serial 0 or 1 | REQ_IO_A5.18.4 | Developer | REQ_F4115.7.4 |
| REQ_IO_A5.18.6 | Remarks (Input) | Input labelled "Remarks" in the stocktake header. | Enter remarks of the stocktake. | Text | 0–1000 characters | None | Developer | REQ_F4115.7.1, REQ_F4115.7.4 |
| REQ_IO_A5.18.7 | Generate Inventory Gain and Loss Document (Button) | Save counts and submit the stocktake for approval. | Post gains and losses after final approval. | Button | SUBMIT; all lines counted | REQ_IO_A5.18.5 | Developer | REQ_F4115.7.5 |
| REQ_IO_A5.18.8 | Business Date (Date Picker) | Set the stocktake business date. | Date the gain and loss movements. | Date | Not later than today | None | Developer | REQ_F4115.7.1 |
| REQ_IO_A5.18.9 | Add Found Line (Button) | Add an item, warehouse and location found on site with zero book quantity. | Record unbooked stock. | Button / MD IDs | Warehouse in scope, active item | REQ_IO_A5.18.5 | Developer | REQ_F4115.7.4 |
| REQ_IO_A5.18.10 | Save Counts / Fill Uncounted (Button) | Save counts with version, or fill uncounted lines with book quantity. | Keep counting progress. | Button | EDIT | REQ_IO_A5.18.5 | Developer | REQ_F4115.7.4 |
| REQ_IO_A5.18.11 | Refresh Snapshot (Button) | Re-read book quantities, keep counts and flag changed lines. | Resolve a stale snapshot. | Button | EDIT; draft only | REQ_IO_A5.18.4 | Developer | REQ_F4115.7.4 |
| REQ_IO_A5.24.1 | Filters (Input) | Filter by document number or remark, date range and status. | Find stocktakes. | Text / date / status | From ≤ To | None | Developer | REQ_F4115.7.3 |
| REQ_IO_A5.24.2 | Stocktake List (Table) | Show paged company stocktakes with line and counted counts; open detail to inspect approval state. | Review records. | Table | VIEW; server paging | REQ_IO_A5.24.1 | Developer | REQ_F4115.7.3 |
| REQ_IO_A5.24.3 | Gain and Loss Report (Panel) | Show MD item, warehouse and location names, book, counted, net difference, gain/loss quantities and type per line, plus each lot or serial and full-document totals. | Review inventory profit and loss. | Panel | VIEW; difference-only toggle (gain/loss at tracking granularity) | REQ_IO_A5.24.2 | Developer | REQ_F4115.7.2 |
| REQ_IO_A5.24.4 | Submit / Delete (Button) | Submit a draft or delete a draft with confirmation. | Control lifecycle. | Button | SUBMIT / DELETE | REQ_IO_A5.24.2 | Developer | REQ_F4115.7.5, REQ_F4115.7.7 |
| REQ_IO_A5.24.5 | Print / Export (Button) | Print the gain and loss report or fetch every page under current filters for UTF-8 CSV with spreadsheet formula neutralization. | Keep stocktake records. | Button | VIEW | REQ_IO_A5.24.3 | Developer | REQ_F4115.7.2 |

## WU-INV-009b 庫存調撥介面（已實作）

實作：`transfer-page.ts`（5.16）、`transfer-form.ts`（5.15／5.17）、`transfer.service.ts`；選單「庫存調撥」（`INV.TRANSFER.VIEW`）。表單在儲存前阻擋來源與目的結存鍵完全相同（即時提示）；已輸入的批序號須與數量相符，空白則留待送審由後端檢查。列表篩選依 API 契約為單號／備註關鍵字、日期區間、狀態（API 不提供物料／倉庫篩選，需要時另開單元）。詳情只為有指定儲位的倉庫查詢儲位名稱。CSV 檔名 `inventory-transfers.csv`，逐行列出兩側 ID 與批序號，文字以單引號中和公式。

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
| 5.21 | Product Initial Stock View Interface | `/inv/opening-stocks` | REQ_F4115.23.1 | `INV.OPENING_STOCK.VIEW`；新增按鈕另需 CREATE |
| 5.22 | Product Initial Stock Create Interface | `/inv/opening-stocks/new` | REQ_F4115.23.2, REQ_F4115.23.3, REQ_F4115.23.4 | `INV.OPENING_STOCK.CREATE` |

| Requirement ID | Item | Description | Purpose | Format | Valid Range | Related I/O | Author | Related REQ_F |
|---|---|---|---|---|---|---|---|---|
| REQ_IO_A5.21.1 | Opening Date Range (Date Picker) | Filter batches by opening date inclusively. | Narrow results. | Date | From ≤ To | None | Developer | REQ_F4115.23.1 |
| REQ_IO_A5.21.2 | Item / Warehouse (Dropdown) | Filter batches containing the MD item or warehouse. | Narrow results. | ID | Current company | None | Developer | REQ_F4115.23.1 |
| REQ_IO_A5.21.3 | Keyword (Textbox) | Match remark or request key. | Find a batch. | Text | Partial match | None | Developer | REQ_F4115.23.1 |
| REQ_IO_A5.21.4 | Batch List (Table) | Paged batches with lines, quantity, amount, remark and posted time. | Review opening stock. | Table | Authorized company | REQ_IO_A5.21.1 | Developer | REQ_F4115.23.1 |
| REQ_IO_A5.21.5 | Batch Detail (Panel) | Lines by line number with MD names, quantity, unit cost and amount. | Inspect a batch. | Table | Authorized company | REQ_IO_A5.21.4 | Developer | REQ_F4115.23.1 |
| REQ_IO_A5.21.6 | Print / Export (Button) | Print one batch; export filtered batches as formula-safe UTF-8 CSV. | Keep records. | Button | Authorized | REQ_IO_A5.21.4 | Developer | REQ_F4115.23.1 |
| REQ_IO_A5.22.1 | Opening Date (Date Picker) | Date the opening balance takes effect. | Set start of stock history. | Date | ≤ today; ≤ earliest movement | None | Developer | REQ_F4115.23.2, REQ_F4115.23.3 |
| REQ_IO_A5.22.2 | Item / Warehouse / Location (Dropdown) | Active MD references per line; location limited to the warehouse. | Identify balance key. | ID | Active, current company | REQ_IO_A5.22.1 | Developer | REQ_F4115.23.3 |
| REQ_IO_A5.22.3 | Quantity / Unit Cost (Textbox) | Base-unit quantity and unit cost. | Value opening stock. | Decimal(6) | Quantity > 0; cost ≥ 0 | REQ_IO_A5.22.2 | Developer | REQ_F4115.23.3 |
| REQ_IO_A5.22.4 | Preview (Button) | Validate without saving; show per-line trilingual errors and totals. | Catch errors before posting. | Button | CREATE | REQ_IO_A5.22.1 | Developer | REQ_F4115.23.2 |
| REQ_IO_A5.22.5 | Submit (Button) | Post a clean, unchanged preview once; retry reuses the batch request key. | Post opening stock idempotently. | Button | Clean preview | REQ_IO_A5.22.4 | Developer | REQ_F4115.23.3, REQ_F4115.23.4 |

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
| 5.23 | Lot and Serial Trace Interface | `/inv/tracking` | REQ_F4115.24.5, REQ_F4115.24.6 | `INV.TRACKING.VIEW` |

| Requirement ID | Item | Description | Purpose | Format | Valid Range | Related I/O | Author | Related REQ_F |
|---|---|---|---|---|---|---|---|---|
| REQ_IO_A5.2.8 | Lot or Serial (Input) | Capture receipt tracking entries and optional lot dates. | Track inbound stock. | Lot rows or serial lines | Sum equals line quantity | REQ_IO_A5.2.5 | Developer | REQ_F4115.24.1, REQ_F4115.24.2, REQ_F4115.24.4 |
| REQ_IO_A5.5.8 | Lot or Serial (Input) | Select numbers at the issue location. | Validate outbound tracking. | Lot rows or serial lines | Sum equals line quantity | REQ_IO_A5.5.5 | Developer | REQ_F4115.24.3, REQ_F4115.24.4 |
| REQ_IO_A5.22.6 | Lot or Serial (Input) | Capture opening stock tracking entries. | Establish tracked opening stock. | Lot rows or serial lines | Sum equals line quantity | None | Developer | REQ_F4115.24.1, REQ_F4115.24.2, REQ_F4115.24.4 |
| REQ_IO_A5.23.1 | Tracking Type (Button) | Switch between lot and serial lists. | Select tracking type. | Button | Lot / Serial | None | Developer | REQ_F4115.24.5 |
| REQ_IO_A5.23.2 | Item (Dropdown) | Filter by company item. | Narrow trace results. | ID | Current company | None | Developer | REQ_F4115.24.5 |
| REQ_IO_A5.23.3 | Number Search (Input) | Filter lot or serial number. | Find tracking number. | Text | Up to 100 characters | None | Developer | REQ_F4115.24.5 |
| REQ_IO_A5.23.4 | In Stock Only (Checkbox) | Exclude fully issued numbers. | Show current stock. | Boolean | True / false | None | Developer | REQ_F4115.24.5 |
| REQ_IO_A5.23.5 | Tracking Results (Table) | Show paged lot or serial records. | Inspect on-hand status. | Table | Current company | REQ_IO_A5.23.1 | Developer | REQ_F4115.24.5 |
| REQ_IO_A5.23.6 | Trace Detail (Table) | Show current locations and movement history with truncation warning. | Trace provenance. | Table | Most recent 1000 movements | REQ_IO_A5.23.5 | Developer | REQ_F4115.24.6 |
