# INV 介面規格

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

## WU-INV-005b 其他出庫介面（設計，待實作）

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
