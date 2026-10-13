# INV 介面規格

WU-INV-001 無使用者頁面；前端在 WU-INV-004 起隨業務單據加入三語、暗色模式、路由權限與 Playwright 驗收。庫存查詢介面由 WU-INV-006 實作，期初庫存介面 4.4.1.8.14 由 WU-INV-007 實作。

## WU-INV-004 其他入庫介面設計（尚未實作）

MyERP 介面次序固定為 4.4.1.5.2 建立、5.3 查看、5.4 編輯；BlueWhale 4.4／4.5 原次序不一致，採 `reference-notes.md` 已整理的 MyERP 次序。三頁均須三語、暗色模式與鍵盤可操作，無權限時隱藏操作。

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
