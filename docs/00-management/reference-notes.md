# BlueWhale 參考筆記與功能對照表

> **預填說明**：BlueWhale 側（ID、描述、介面、資料表）已由人手用腳本從 `BlueWhaleERP_SRS.docx` 抽取。
> AI 在 WU-P0-002 必須：(1) 核對並補充「章節大綱」「表格格式」兩節；(2) 逐行填寫 MyERP 側欄位；(3) 之後每完成功能即更新「狀態」。
> 本檔會提交到公開 repo：只寫功能名稱與結構，**不得**抄錄 NDA、訪談、客戶名稱、金額等內容。

## 1. 規模
- 功能需求 REQ_F：222 項；資料表 REQ_DS43：88 張；介面：150 個；介面欄位 REQ_IO_A：約 1,414 項

## 2. 章節大綱（SRS / SDS）

SRS：NDA → 封面 → 目錄 → 1 Introduction（1.1–1.7）→ 2 Overall Description（2.1–2.6，2.2 每模組一小節）→ 3 System Analysis（3.1 Interview、3.2 Feasibility 3.2.1–3.2.3）→ 4 Requirement Analysis（4.1 Functional / 4.1.1 General Functions data、4.2 Non-Functional 4.2.1–4.2.5、4.3 Data Structure、4.4 User Interface Requirements / 4.4.1 User Portal、4.5 User Interface Prototype、4.6 Project Planning）。

SDS：封面 → 目錄 → 1 Introduction（1.1–1.6）→ 2 Design Consideration（2.1–2.3）→ 3 System Architecture and Architecture Design（3.1–3.3）→ 4 System Design（4.1 Database Design、4.2 User Interface Design / 4.2.1 User Portal）。

（AI 待補：各節字數規模、圖說格式、頁首頁尾、字型與標題樣式。）

## 3. 表格格式

見 MASTER_PROMPT 15.3。（AI 待補：核對實際 docx 的欄寬、粗體、框線、合併儲存格。）

## 4. 已知不一致（不要照抄）

- BlueWhale 部分 REQ_F 編號漏了模組編號：User（4.1.1.1）用 `REQ_F411.1.x`；Settings 的 User Management（8.6）、Operation Log、Parameter、Print Settings、Logo & Stamp（8.16–8.19）用 `REQ_F411.{子功能}.{序號}`（例如 `REQ_F411.19.2`）。MyERP 一律用完整格式 `REQ_F411{模組}.{子功能}.{序號}`，User 為 `REQ_F4111.x.y`。
- SRS 4.4 與 SDS 4.2 的部分介面次序不同（例如 Sales Order Create / View 先後）；MyERP 兩處必須完全一致。
- 4.3 的表名為 PascalCase 英文（Account、Area…），MyERP 用模組前綴 + snake_case。

## 5. 功能對照表（REQ_F）

| BlueWhale 章節 | BlueWhale ID | 功能描述 | MyERP 模組 | MyERP 需求 ID | 狀態 |
|---|---|---|---|---|---|
| 4.1.1.1 User | REQ_F411.1.1 | System should allow user to login into the website | SYS |  | TODO |
| 4.1.1.1 User | REQ_F411.1.2 | System should allow user to change password | SYS |  | TODO |
| 4.1.1.1 User | REQ_F411.1.3 | System should allow user to logout | SYS |  | TODO |
| 4.1.1.2.1 Sales Order | REQ_F4112.1.1 | System should enable user to view sales order | SD |  | TODO |
| 4.1.1.2.1 Sales Order | REQ_F4112.1.2 | System should enable user to create sales order | SD |  | TODO |
| 4.1.1.2.1 Sales Order | REQ_F4112.1.3 | System should enable user to edit sales order | SD |  | TODO |
| 4.1.1.2.1 Sales Order | REQ_F4112.1.4 | System should enable user to approve sales order | SD |  | TODO |
| 4.1.1.2.1 Sales Order | REQ_F4112.1.5 | System should enable user to reject sales order | SD |  | TODO |
| 4.1.1.2.1 Sales Order | REQ_F4112.1.6 | System should enable user to delete sales order | SD |  | TODO |
| 4.1.1.2.2 Sales Outbound | REQ_F4112.2.1 | System should enable user to view sales outbounds | SD |  | TODO |
| 4.1.1.2.2 Sales Outbound | REQ_F4112.2.2 | System should enable user to create sales outbounds | SD |  | TODO |
| 4.1.1.2.2 Sales Outbound | REQ_F4112.2.3 | System should enable user to edit sales outbounds | SD |  | TODO |
| 4.1.1.2.2 Sales Outbound | REQ_F4112.2.4 | System should enable user to approve sales outbounds | SD |  | TODO |
| 4.1.1.2.2 Sales Outbound | REQ_F4112.2.5 | System should enable user to reject sales outbounds | SD |  | TODO |
| 4.1.1.2.2 Sales Outbound | REQ_F4112.2.6 | System should enable user to delete sales outbounds | SD |  | TODO |
| 4.1.1.3.1 Bill Of Material (BOM) | REQ_F4113.1.1 | System should enable user to view BOM | PP |  | TODO |
| 4.1.1.3.1 Bill Of Material (BOM) | REQ_F4113.1.2 | System should enable user to create BOM | PP |  | TODO |
| 4.1.1.3.1 Bill Of Material (BOM) | REQ_F4113.1.3 | System should enable user to edit BOM | PP |  | TODO |
| 4.1.1.3.1 Bill Of Material (BOM) | REQ_F4113.1.4 | System should enable user to approve BOM | PP |  | TODO |
| 4.1.1.3.1 Bill Of Material (BOM) | REQ_F4113.1.5 | System should enable user to reject BOM | PP |  | TODO |
| 4.1.1.3.1 Bill Of Material (BOM) | REQ_F4113.1.6 | System should enable user to delete BOM | PP |  | TODO |
| 4.1.1.3.1 Bill Of Material (BOM) | REQ_F4113.1.7 | System should enable user to view BOM Grouping | PP |  | TODO |
| 4.1.1.3.1 Bill Of Material (BOM) | REQ_F4113.1.8 | System should enable user to create BOM Grouping | PP |  | TODO |
| 4.1.1.3.1 Bill Of Material (BOM) | REQ_F4113.1.9 | System should enable user to edit BOM Grouping | PP |  | TODO |
| 4.1.1.3.1 Bill Of Material (BOM) | REQ_F4113.1.10 | System should enable user to delete BOM Grouping | PP |  | TODO |
| 4.1.1.3.2 Production Planning | REQ_F4113.2.1 | System should enable user to view production planning | PP |  | TODO |
| 4.1.1.3.2 Production Planning | REQ_F4113.2.2 | System should enable user to create production planning | PP |  | TODO |
| 4.1.1.3.2 Production Planning | REQ_F4113.2.3 | System should enable user to edit production planning | PP |  | TODO |
| 4.1.1.3.2 Production Planning | REQ_F4113.2.4 | System should enable user to approve production planning | PP |  | TODO |
| 4.1.1.3.2 Production Planning | REQ_F4113.2.5 | System should enable user to reject production planning | PP |  | TODO |
| 4.1.1.3.2 Production Planning | REQ_F4113.2.6 | System should enable user to delete production planning | PP |  | TODO |
| 4.1.1.3.3 Material Requisition | REQ_F4113.3.1 | System should enable user to view material requisition | PP |  | TODO |
| 4.1.1.3.3 Material Requisition | REQ_F4113.3.2 | System should enable user to create material requisition | PP |  | TODO |
| 4.1.1.3.3 Material Requisition | REQ_F4113.3.3 | System should enable user to edit material requisition | PP |  | TODO |
| 4.1.1.3.3 Material Requisition | REQ_F4113.3.4 | System should enable user to approve material requisition | PP |  | TODO |
| 4.1.1.3.3 Material Requisition | REQ_F4113.3.5 | System should enable user to reject material requisition | PP |  | TODO |
| 4.1.1.3.3 Material Requisition | REQ_F4113.3.6 | System should enable user to delete material requisition | PP |  | TODO |
| 4.1.1.3.4 Production Inbound | REQ_F4113.4.1 | System should enable user to view production inbound | PP |  | TODO |
| 4.1.1.3.4 Production Inbound | REQ_F4113.4.2 | System should enable user to create production inbound | PP |  | TODO |
| 4.1.1.3.4 Production Inbound | REQ_F4113.4.3 | System should enable user to edit production inbound | PP |  | TODO |
| 4.1.1.3.4 Production Inbound | REQ_F4113.4.4 | System should enable user to approve production inbound | PP |  | TODO |
| 4.1.1.3.4 Production Inbound | REQ_F4113.4.5 | System should enable user to reject production inbound | PP |  | TODO |
| 4.1.1.3.4 Production Inbound | REQ_F4113.4.6 | System should enable user to delete production inbound | PP |  | TODO |
| 4.1.1.4.1 Purchase Order | REQ_F4114.1.1 | System should enable user to view purchase order | PUR |  | TODO |
| 4.1.1.4.1 Purchase Order | REQ_F4114.1.2 | System should enable user to create purchase order | PUR |  | TODO |
| 4.1.1.4.1 Purchase Order | REQ_F4114.1.3 | System should enable user to edit purchase order | PUR |  | TODO |
| 4.1.1.4.1 Purchase Order | REQ_F4114.1.4 | System should enable user to approve purchase order | PUR |  | TODO |
| 4.1.1.4.1 Purchase Order | REQ_F4114.1.5 | System should enable user to reject purchase order | PUR |  | TODO |
| 4.1.1.4.1 Purchase Order | REQ_F4114.1.6 | System should enable user to delete purchase order | PUR |  | TODO |
| 4.1.1.4.2 Purchase Inbound | REQ_F4114.2.1 | System should enable user to view purchase inbound | PUR |  | TODO |
| 4.1.1.4.2 Purchase Inbound | REQ_F4114.2.2 | System should enable user to create purchase inbound | PUR |  | TODO |
| 4.1.1.4.2 Purchase Inbound | REQ_F4114.2.3 | System should enable user to edit purchase inbound | PUR |  | TODO |
| 4.1.1.4.2 Purchase Inbound | REQ_F4114.2.4 | System should enable user to approve purchase inbound | PUR |  | TODO |
| 4.1.1.4.2 Purchase Inbound | REQ_F4114.2.5 | System should enable user to reject purchase inbound | PUR |  | TODO |
| 4.1.1.4.2 Purchase Inbound | REQ_F4114.2.6 | System should enable user to delete purchase inbound | PUR |  | TODO |
| 4.1.1.5.1 Inventory | REQ_F4115.1.1 | System should enable user to view records of inventory | INV |  | TODO |
| 4.1.1.5.1 Inventory | REQ_F4115.1.2 | System should enable user to export inventory records as excel spreadsheet | INV |  | TODO |
| 4.1.1.5.2 Other Inbound | REQ_F4115.2.1 | System should enable user to view records of other inbound | INV |  | TODO |
| 4.1.1.5.2 Other Inbound | REQ_F4115.2.2 | System should enable user to create records of other inbound | INV |  | TODO |
| 4.1.1.5.2 Other Inbound | REQ_F4115.2.3 | System should enable user to edit records of other inbound | INV |  | TODO |
| 4.1.1.5.2 Other Inbound | REQ_F4115.2.4 | System should enable user to approve requests of other inbound | INV |  | TODO |
| 4.1.1.5.2 Other Inbound | REQ_F4115.2.5 | System should enable user to reject requests of other inbound | INV |  | TODO |
| 4.1.1.5.2 Other Inbound | REQ_F4115.2.6 | System should enable user to delete records of other inbound | INV |  | TODO |
| 4.1.1.5.3 Other Outbound | REQ_F4115.3.1 | System should enable user to view records of other outbound | INV |  | TODO |
| 4.1.1.5.3 Other Outbound | REQ_F4115.3.2 | System should enable user to create records of other outbound | INV |  | TODO |
| 4.1.1.5.3 Other Outbound | REQ_F4115.3.3 | System should enable user to edit records of other outbound | INV |  | TODO |
| 4.1.1.5.3 Other Outbound | REQ_F4115.3.4 | System should enable user to approve requests of other outbound | INV |  | TODO |
| 4.1.1.5.3 Other Outbound | REQ_F4115.3.5 | System should enable user to reject requests of other outbound | INV |  | TODO |
| 4.1.1.5.3 Other Outbound | REQ_F4115.3.6 | System should enable user to delete records of other outbound | INV |  | TODO |
| 4.1.1.5.4 Product Assembly | REQ_F4115.4.1 | System should enable user to view records of product assembly | INV |  | TODO |
| 4.1.1.5.4 Product Assembly | REQ_F4115.4.2 | System should enable user to create records of product assembly | INV |  | TODO |
| 4.1.1.5.4 Product Assembly | REQ_F4115.4.3 | System should enable user to edit records of product assembly | INV |  | TODO |
| 4.1.1.5.4 Product Assembly | REQ_F4115.4.4 | System should enable user to approve requests of product assembly | INV |  | TODO |
| 4.1.1.5.4 Product Assembly | REQ_F4115.4.5 | System should enable user to reject requests of product assembly | INV |  | TODO |
| 4.1.1.5.4 Product Assembly | REQ_F4115.4.6 | System should enable user to delete records of product assembly | INV |  | TODO |
| 4.1.1.5.5 Product Disassembly | REQ_F4115.5.1 | System should enable user to view records of product disassembly | INV |  | TODO |
| 4.1.1.5.5 Product Disassembly | REQ_F4115.5.2 | System should enable user to create records of product disassembly | INV |  | TODO |
| 4.1.1.5.5 Product Disassembly | REQ_F4115.5.3 | System should enable user to edit records of product disassembly | INV |  | TODO |
| 4.1.1.5.5 Product Disassembly | REQ_F4115.5.4 | System should enable user to approve requests of product disassembly | INV |  | TODO |
| 4.1.1.5.5 Product Disassembly | REQ_F4115.5.5 | System should enable user to reject requests of product disassembly | INV |  | TODO |
| 4.1.1.5.5 Product Disassembly | REQ_F4115.5.6 | System should enable user to delete records of product disassembly | INV |  | TODO |
| 4.1.1.5.6 Inventory Transfer | REQ_F4115.6.1 | System should enable user to view records of inventory transfer | INV |  | TODO |
| 4.1.1.5.6 Inventory Transfer | REQ_F4115.6.2 | System should enable user to create records of inventory transfer | INV |  | TODO |
| 4.1.1.5.6 Inventory Transfer | REQ_F4115.6.3 | System should enable user to edit records of inventory transfer | INV |  | TODO |
| 4.1.1.5.6 Inventory Transfer | REQ_F4115.6.4 | System should enable user to approve requests of inventory transfer | INV |  | TODO |
| 4.1.1.5.6 Inventory Transfer | REQ_F4115.6.5 | System should enable user to reject requests of inventory transfer | INV |  | TODO |
| 4.1.1.5.6 Inventory Transfer | REQ_F4115.6.6 | System should enable user to delete records of inventory transfer | INV |  | TODO |
| 4.1.1.5.7 Stocktake | REQ_F4115.7.1 | System should enable user to create records of inventory transfer | INV |  | TODO |
| 4.1.1.5.7 Stocktake | REQ_F4115.7.2 | System should enable user to generate inventory profit/loss reports | INV |  | TODO |
| 4.1.1.6.1 Sales Collection | REQ_F4116.1.1 | System should enable user to view sales collection details | FI |  | TODO |
| 4.1.1.6.1 Sales Collection | REQ_F4116.1.2 | System should enable user to create new sales collection | FI |  | TODO |
| 4.1.1.6.1 Sales Collection | REQ_F4116.1.3 | System should enable user to edit sales collection details | FI |  | TODO |
| 4.1.1.6.1 Sales Collection | REQ_F4116.1.4 | System should enable user to approve sales collections | FI |  | TODO |
| 4.1.1.6.1 Sales Collection | REQ_F4116.1.5 | System should enable user to reject sales collections | FI |  | TODO |
| 4.1.1.6.1 Sales Collection | REQ_F4116.1.6 | System should enable users to delete sales collection records. | FI |  | TODO |
| 4.1.1.6.2 Other Collection | REQ_F4116.2.1 | System should enable user to view other collection details | FI |  | TODO |
| 4.1.1.6.2 Other Collection | REQ_F4116.2.2 | System should enable user to create new other collection | FI |  | TODO |
| 4.1.1.6.2 Other Collection | REQ_F4116.2.3 | System should enable user to edit other collection details | FI |  | TODO |
| 4.1.1.6.2 Other Collection | REQ_F4116.2.4 | System should enable user to approve other collections | FI |  | TODO |
| 4.1.1.6.2 Other Collection | REQ_F4116.2.5 | System should enable user to reject other collections | FI |  | TODO |
| 4.1.1.6.2 Other Collection | REQ_F4116.2.6 | System should enable users to delete other collection records. | FI |  | TODO |
| 4.1.1.6.3 Purchase Payment | REQ_F4116.3.1 | System should enable user to view purchase payment details | FI |  | TODO |
| 4.1.1.6.3 Purchase Payment | REQ_F4116.3.2 | System should enable user to create purchase payments | FI |  | TODO |
| 4.1.1.6.3 Purchase Payment | REQ_F4116.3.3 | System should enable user to edit purchase payment details | FI |  | TODO |
| 4.1.1.6.3 Purchase Payment | REQ_F4116.3.4 | System should enable user to approve purchase payments | FI |  | TODO |
| 4.1.1.6.3 Purchase Payment | REQ_F4116.3.5 | System should enable user to reject purchase payments | FI |  | TODO |
| 4.1.1.6.3 Purchase Payment | REQ_F4116.3.6 | System should enable users to delete purchase payments | FI |  | TODO |
| 4.1.1.6.4 Other Payment | REQ_F4116.4.1 | System should enable user to view other payment details | FI |  | TODO |
| 4.1.1.6.4 Other Payment | REQ_F4116.4.2 | System should enable user to create other payments | FI |  | TODO |
| 4.1.1.6.4 Other Payment | REQ_F4116.4.3 | System should enable user to edit other payment details | FI |  | TODO |
| 4.1.1.6.4 Other Payment | REQ_F4116.4.4 | System should enable user to approve other payments | FI |  | TODO |
| 4.1.1.6.4 Other Payment | REQ_F4116.4.5 | System should enable user to reject other payments | FI |  | TODO |
| 4.1.1.6.4 Other Payment | REQ_F4116.4.6 | System should enable users to delete other payments | FI |  | TODO |
| 4.1.1.6.5 Collection Write-off | REQ_F4116.5.1 | System should enable user to view collection write-off details | FI |  | TODO |
| 4.1.1.6.5 Collection Write-off | REQ_F4116.5.2 | System should enable user to create collection write-offs | FI |  | TODO |
| 4.1.1.6.5 Collection Write-off | REQ_F4116.5.3 | System should enable user to edit collection write-off details | FI |  | TODO |
| 4.1.1.6.5 Collection Write-off | REQ_F4116.5.4 | System should enable user to approve collection write-offs | FI |  | TODO |
| 4.1.1.6.5 Collection Write-off | REQ_F4116.5.5 | System should enable user to reject collection write-offs | FI |  | TODO |
| 4.1.1.6.5 Collection Write-off | REQ_F4116.5.6 | System should enable users to delete collection write-offs | FI |  | TODO |
| 4.1.1.6.6 Payment Write-off | REQ_F4116.6.1 | System should enable user to view payment write-off details | FI |  | TODO |
| 4.1.1.6.6 Payment Write-off | REQ_F4116.6.2 | System should enable user to create payment write-offs | FI |  | TODO |
| 4.1.1.6.6 Payment Write-off | REQ_F4116.6.3 | System should enable user to edit payment write-off details | FI |  | TODO |
| 4.1.1.6.6 Payment Write-off | REQ_F4116.6.4 | System should enable user to approve payment write-offs | FI |  | TODO |
| 4.1.1.6.6 Payment Write-off | REQ_F4116.6.5 | System should enable user to reject payment write-offs | FI |  | TODO |
| 4.1.1.6.6 Payment Write-off | REQ_F4116.6.6 | System should enable users to delete payment write-offs | FI |  | TODO |
| 4.1.1.7.1 Purchase | REQ_F4117.1.1 | System should enable user to view Purchase Order Tracking Report | BI |  | TODO |
| 4.1.1.7.1 Purchase | REQ_F4117.1.2 | System should enable user to view Purchase Detail Report | BI |  | TODO |
| 4.1.1.7.1 Purchase | REQ_F4117.1.3 | System should enable user to view Purchase Summary Report (By Product) | BI |  | TODO |
| 4.1.1.7.1 Purchase | REQ_F4117.1.4 | System should enable user to view Purchase Summary Report (By Supplier) | BI |  | TODO |
| 4.1.1.7.2 Sales | REQ_F4117.2.1 | System should enable user to view Sales Order Tracking Report | BI |  | TODO |
| 4.1.1.7.2 Sales | REQ_F4117.2.2 | System should enable user to view Sales Detail Report | BI |  | TODO |
| 4.1.1.7.2 Sales | REQ_F4117.2.3 | System should enable user to view Sales Summary Report (By Product) | BI |  | TODO |
| 4.1.1.7.2 Sales | REQ_F4117.2.4 | System should enable user to view Sales Summary Report (By Client) | BI |  | TODO |
| 4.1.1.7.3 Production | REQ_F4117.3.1 | System should enable user to view Production Planning Tracking Report | BI |  | TODO |
| 4.1.1.7.3 Production | REQ_F4117.3.2 | System should enable user to view Material Requisition Detail Report | BI |  | TODO |
| 4.1.1.7.3 Production | REQ_F4117.3.3 | System should enable user to view Material Requisition Summary Report | BI |  | TODO |
| 4.1.1.7.3 Production | REQ_F4117.3.4 | System should enable user to view Production Inbound Detail Report | BI |  | TODO |
| 4.1.1.7.3 Production | REQ_F4117.3.5 | System should enable user to view Production Inbound Summary Report | BI |  | TODO |
| 4.1.1.7.4 Warehouse | REQ_F4117.4.1 | System should enable user to view Product Inventory Balance Report | BI |  | TODO |
| 4.1.1.7.4 Warehouse | REQ_F4117.4.2 | System should enable user to view Product Inbound & Outbound Detail Report | BI |  | TODO |
| 4.1.1.7.4 Warehouse | REQ_F4117.4.3 | System should enable user to view Product Inbound & Outbound Summary Report | BI |  | TODO |
| 4.1.1.7.5 Financial | REQ_F4117.5.1 | System should enable user to view Cash & Bank Report | BI |  | TODO |
| 4.1.1.7.5 Financial | REQ_F4117.5.2 | System should enable user to view Accounts Payable Details Report | BI |  | TODO |
| 4.1.1.7.5 Financial | REQ_F4117.5.3 | System should enable user to view Accounts Receivable Details Report | BI |  | TODO |
| 4.1.1.7.5 Financial | REQ_F4117.5.4 | System should enable user to view Client Statement | BI |  | TODO |
| 4.1.1.7.5 Financial | REQ_F4117.5.5 | System should enable user to view Supplier Statement | BI |  | TODO |
| 4.1.1.7.5 Financial | REQ_F4117.5.6 | System should enable user to view Other Income & Expenses Detail Report | BI |  | TODO |
| 4.1.1.7.5 Financial | REQ_F4117.5.7 | System should enable user to export Other Income & Expenses Detail Report as Excel | BI |  | TODO |
| 4.1.1.8.1 Client Management | REQ_F4118.1.1 | System should enable user to view records of clients | MD / SYS |  | TODO |
| 4.1.1.8.1 Client Management | REQ_F4118.1.2 | System should enable user to create records of clients | MD / SYS |  | TODO |
| 4.1.1.8.1 Client Management | REQ_F4118.1.3 | System should enable user to edit records of clients | MD / SYS |  | TODO |
| 4.1.1.8.1 Client Management | REQ_F4118.1.4 | System should enable user to approve records of clients to be added | MD / SYS |  | TODO |
| 4.1.1.8.1 Client Management | REQ_F4118.1.5 | System should enable user to reject records of clients to be added | MD / SYS |  | TODO |
| 4.1.1.8.1 Client Management | REQ_F4118.1.6 | System should enable user to delete records of clients | MD / SYS |  | TODO |
| 4.1.1.8.1 Client Management | REQ_F4118.1.7 | System should enable user to import records of clients from excel spreadsheets | MD / SYS |  | TODO |
| 4.1.1.8.2 Supplier Management | REQ_F4118.2.1 | System should enable user to view records of suppliers | MD / SYS |  | TODO |
| 4.1.1.8.2 Supplier Management | REQ_F4118.2.2 | System should enable user to create records of suppliers | MD / SYS |  | TODO |
| 4.1.1.8.2 Supplier Management | REQ_F4118.2.3 | System should enable user to edit records of suppliers | MD / SYS |  | TODO |
| 4.1.1.8.2 Supplier Management | REQ_F4118.2.4 | System should enable user to approve records of suppliers to be added | MD / SYS |  | TODO |
| 4.1.1.8.2 Supplier Management | REQ_F4118.2.5 | System should enable user to reject records of suppliers to be added | MD / SYS |  | TODO |
| 4.1.1.8.2 Supplier Management | REQ_F4118.2.6 | System should enable user to delete records of suppliers | MD / SYS |  | TODO |
| 4.1.1.8.2 Supplier Management | REQ_F4118.2.7 | System should enable user to import records of suppliers from excel spreadsheets | MD / SYS |  | TODO |
| 4.1.1.8.3 Product Management | REQ_F4118.3.1 | System should enable user to view product | MD / SYS |  | TODO |
| 4.1.1.8.3 Product Management | REQ_F4118.3.2 | System should enable user to create product | MD / SYS |  | TODO |
| 4.1.1.8.3 Product Management | REQ_F4118.3.3 | System should enable user to edit product | MD / SYS |  | TODO |
| 4.1.1.8.3 Product Management | REQ_F4118.3.4 | System should enable user to delete product | MD / SYS |  | TODO |
| 4.1.1.8.4 Warehouse Management | REQ_F4118.4.1 | System should enable user to view records of warehouse | MD / SYS |  | TODO |
| 4.1.1.8.4 Warehouse Management | REQ_F4118.4.2 | System should enable user to create records of warehouse | MD / SYS |  | TODO |
| 4.1.1.8.4 Warehouse Management | REQ_F4118.4.3 | System should enable user to edit records of warehouse | MD / SYS |  | TODO |
| 4.1.1.8.4 Warehouse Management | REQ_F4118.4.4 | System should enable user to delete records of warehouse | MD / SYS |  | TODO |
| 4.1.1.8.5 Account Management | REQ_F4118.5.1 | System should enable the user to view accounts. | MD / SYS |  | TODO |
| 4.1.1.8.5 Account Management | REQ_F4118.5.2 | System should enable users to create new accounts. | MD / SYS |  | TODO |
| 4.1.1.8.5 Account Management | REQ_F4118.5.3 | System should enable the user to edit existing accounts. | MD / SYS |  | TODO |
| 4.1.1.8.5 Account Management | REQ_F4118.5.4 | System should enable the user to delete accounts. | MD / SYS |  | TODO |
| 4.1.1.8.6 User Management | REQ_F411.8.1 | System should enable user to view users’ details | MD / SYS |  | TODO |
| 4.1.1.8.6 User Management | REQ_F411.8.2 | System should enable user to create new user | MD / SYS |  | TODO |
| 4.1.1.8.6 User Management | REQ_F411.8.3 | System should enable user to edit users’ details and permissions | MD / SYS |  | TODO |
| 4.1.1.8.6 User Management | REQ_F411.8.4 | System should enable user to delete users | MD / SYS |  | TODO |
| 4.1.1.8.7 Process Management | REQ_F4118.7.1 | System should enable user to view process | MD / SYS |  | TODO |
| 4.1.1.8.7 Process Management | REQ_F4118.7.2 | System should enable user to create process | MD / SYS |  | TODO |
| 4.1.1.8.7 Process Management | REQ_F4118.7.3 | System should enable user to edit process | MD / SYS |  | TODO |
| 4.1.1.8.7 Process Management | REQ_F4118.7.4 | System should enable user to delete process | MD / SYS |  | TODO |
| 4.1.1.8.8 Client Category | REQ_F4118.8.1 | System should enable user to view records of client categories | MD / SYS |  | TODO |
| 4.1.1.8.8 Client Category | REQ_F4118.8.2 | System should enable user to create records of client categories | MD / SYS |  | TODO |
| 4.1.1.8.8 Client Category | REQ_F4118.8.3 | System should enable user to edit records of client categories | MD / SYS |  | TODO |
| 4.1.1.8.8 Client Category | REQ_F4118.8.4 | System should enable user to delete records of client categories | MD / SYS |  | TODO |
| 4.1.1.8.9 Supplier Category | REQ_F4118.9.1 | System should enable user to view records of supplier categories | MD / SYS |  | TODO |
| 4.1.1.8.9 Supplier Category | REQ_F4118.9.2 | System should enable user to create records of supplier categories | MD / SYS |  | TODO |
| 4.1.1.8.9 Supplier Category | REQ_F4118.9.3 | System should enable user to edit records of supplier categories | MD / SYS |  | TODO |
| 4.1.1.8.9 Supplier Category | REQ_F4118.9.4 | System should enable user to delete records of supplier categories | MD / SYS |  | TODO |
| 4.1.1.8.10 Item Category | REQ_F4118.10.1 | System should enable user to view item category | MD / SYS |  | TODO |
| 4.1.1.8.10 Item Category | REQ_F4118.10.2 | System should enable user to create item category | MD / SYS |  | TODO |
| 4.1.1.8.10 Item Category | REQ_F4118.10.3 | System should enable user to edit item category | MD / SYS |  | TODO |
| 4.1.1.8.10 Item Category | REQ_F4118.10.4 | System should enable user to delete item category | MD / SYS |  | TODO |
| 4.1.1.8.11 Item Brand | REQ_F4118.11.1 | System should enable user to view item brand | MD / SYS |  | TODO |
| 4.1.1.8.11 Item Brand | REQ_F4118.11.2 | System should enable user to create item brand | MD / SYS |  | TODO |
| 4.1.1.8.11 Item Brand | REQ_F4118.11.3 | System should enable user to edit item brand | MD / SYS |  | TODO |
| 4.1.1.8.11 Item Brand | REQ_F4118.11.4 | System should enable user to delete item brand | MD / SYS |  | TODO |
| 4.1.1.8.12 Income & Expense Category | REQ_F4118.12.1 | System should enable the user to view income & expense categories. | MD / SYS |  | TODO |
| 4.1.1.8.12 Income & Expense Category | REQ_F4118.12.2 | System should enable users to create income & expense categories. | MD / SYS |  | TODO |
| 4.1.1.8.12 Income & Expense Category | REQ_F4118.12.3 | System should enable the user to edit income & expense categories. | MD / SYS |  | TODO |
| 4.1.1.8.12 Income & Expense Category | REQ_F4118.12.4 | System should enable the user to delete income & expense categories | MD / SYS |  | TODO |
| 4.1.1.8.13 Unit of Measurement | REQ_F4118.13.1 | System should enable user to view unit of measurement | MD / SYS |  | TODO |
| 4.1.1.8.13 Unit of Measurement | REQ_F4118.13.2 | System should enable user to create unit of measurement | MD / SYS |  | TODO |
| 4.1.1.8.13 Unit of Measurement | REQ_F4118.13.3 | System should enable user to edit unit of measurement | MD / SYS |  | TODO |
| 4.1.1.8.13 Unit of Measurement | REQ_F4118.13.4 | System should enable user to delete unit of measurement | MD / SYS |  | TODO |
| 4.1.1.8.14 Settlement Method | REQ_F4118.14.1 | System should enable the user to view settlement methods. | MD / SYS |  | TODO |
| 4.1.1.8.14 Settlement Method | REQ_F4118.14.2 | System should enable users to create new settlement methods. | MD / SYS |  | TODO |
| 4.1.1.8.14 Settlement Method | REQ_F4118.14.3 | System should enable the user to edit existing settlement methods. | MD / SYS |  | TODO |
| 4.1.1.8.14 Settlement Method | REQ_F4118.14.4 | System should enable the user to delete existing settlement methods. | MD / SYS |  | TODO |
| 4.1.1.8.15 Process Category | REQ_F4118.15.1 | System should enable user to view process category | MD / SYS |  | TODO |
| 4.1.1.8.15 Process Category | REQ_F4118.15.2 | System should enable user to create process category | MD / SYS |  | TODO |
| 4.1.1.8.15 Process Category | REQ_F4118.15.3 | System should enable user to edit process category | MD / SYS |  | TODO |
| 4.1.1.8.15 Process Category | REQ_F4118.15.4 | System should enable user to delete process category | MD / SYS |  | TODO |
| 4.1.1.8.16 Operation Log | REQ_F411.16.1 | System should enable user to view system operation logs | MD / SYS |  | TODO |
| 4.1.1.8.16 Operation Log | REQ_F411.16.2 | System should enable user to delete system operation logs | MD / SYS |  | TODO |
| 4.1.1.8.17 Parameter Settings | REQ_F411.17.1 | System should enable user to view parameter settings | MD / SYS |  | TODO |
| 4.1.1.8.17 Parameter Settings | REQ_F411.17.2 | System should enable user to edit parameter settings | MD / SYS |  | TODO |
| 4.1.1.8.18 Print Settings | REQ_F411.18.1 | System should enable user to view print settings | MD / SYS |  | TODO |
| 4.1.1.8.18 Print Settings | REQ_F411.18.2 | System should enable user to edit print settings | MD / SYS |  | TODO |
| 4.1.1.8.19 Logo & Stamp | REQ_F411.19.1 | System should enable user to view the logo and stamp | MD / SYS |  | TODO |
| 4.1.1.8.19 Logo & Stamp | REQ_F411.19.2 | System should enable user to edit the logo and stamp | MD / SYS |  | TODO |

## 6. 介面對照表（SRS 4.4 / 4.5、SDS 4.2）

| BlueWhale 編號 | 介面名稱 | MyERP 模組 | MyERP 介面編號 | 狀態 |
|---|---|---|---|---|
| 4.4.1.1.1 | Login Interface | SYS |  | TODO |
| 4.4.1.1.2 | Homepage Interface | SYS |  | TODO |
| 4.4.1.1.3 | Change Password Interface | SYS |  | TODO |
| 4.4.1.2.1 | Sales Order Create Interface | SD |  | TODO |
| 4.4.1.2.2 | Sales Order View Interface | SD |  | TODO |
| 4.4.1.2.3 | Sales Order Edit Interface | SD |  | TODO |
| 4.4.1.2.4 | Sales Outbound Create Interface | SD |  | TODO |
| 4.4.1.2.5 | Sales Outbound View Interface | SD |  | TODO |
| 4.4.1.2.6 | Sales Outbound Edit Interface | SD |  | TODO |
| 4.4.1.2.7 | Select Client Interface | SD |  | TODO |
| 4.4.1.3.1 | BOM Grouping Interface | PP |  | TODO |
| 4.4.1.3.2 | BOM List View Interface | PP |  | TODO |
| 4.4.1.3.3 | BOM List Create Interface | PP |  | TODO |
| 4.4.1.3.4 | BOM List Edit Interface | PP |  | TODO |
| 4.4.1.3.5 | Production Planning View Interface | PP |  | TODO |
| 4.4.1.3.6 | Production Planning Create Interface | PP |  | TODO |
| 4.4.1.3.7 | Production Planning Edit Interface | PP |  | TODO |
| 4.4.1.3.8 | Material Requisition View Interface | PP |  | TODO |
| 4.4.1.3.9 | Material Requisition Create Interface | PP |  | TODO |
| 4.4.1.3.10 | Material Requisition Edit Interface | PP |  | TODO |
| 4.4.1.3.11 | Production Inbound View Interface | PP |  | TODO |
| 4.4.1.3.12 | Production Inbound Create Interface | PP |  | TODO |
| 4.4.1.3.13 | Production Inbound Edit Interface | PP |  | TODO |
| 4.4.1.3.14 | Select Order Interface | PP |  | TODO |
| 4.4.1.3.15 | Select Production Plan Interface | PP |  | TODO |
| 4.4.1.4.1 | Purchase Order View Interface | PUR |  | TODO |
| 4.4.1.4.2 | Purchase Order Create Interface | PUR |  | TODO |
| 4.4.1.4.3 | Purchase Order Edit Interface | PUR |  | TODO |
| 4.4.1.4.4 | Purchase Inbound View Interface | PUR |  | TODO |
| 4.4.1.4.5 | Purchase Inbound Create Interface | PUR |  | TODO |
| 4.4.1.4.6 | Purchase Inbound Edit Interface | PUR |  | TODO |
| 4.4.1.4.7 | Select Supplier Interface | PUR |  | TODO |
| 4.4.1.5.1 | Inventory View Interface | INV |  | TODO |
| 4.4.1.5.2 | Other Inbound Create Interface | INV |  | TODO |
| 4.4.1.5.3 | Other Inbound View Interface | INV |  | TODO |
| 4.4.1.5.4 | Other Inbound Edit Interface | INV |  | TODO |
| 4.4.1.5.5 | Other Outbound Create Interface | INV |  | TODO |
| 4.4.1.5.6 | Other Outbound View Interface | INV |  | TODO |
| 4.4.1.5.7 | Other Outbound Edit Interface | INV |  | TODO |
| 4.4.1.5.8 | Sales Outbound (Approval) Interface | INV |  | TODO |
| 4.4.1.5.9 | Product Assembly Create Interface | INV |  | TODO |
| 4.4.1.5.10 | Product Assembly View Interface | INV |  | TODO |
| 4.4.1.5.11 | Product Assembly Edit Interface | INV |  | TODO |
| 4.4.1.5.12 | Product Disassembly Create Interface | INV |  | TODO |
| 4.4.1.5.13 | Product Disassembly View Interface | INV |  | TODO |
| 4.4.1.5.14 | Product Disassembly Edit Interface | INV |  | TODO |
| 4.4.1.5.15 | Inventory Transfer Create Interface | INV |  | TODO |
| 4.4.1.5.16 | Inventory Transfer View Interface | INV |  | TODO |
| 4.4.1.5.17 | Inventory Transfer Edit Interface | INV |  | TODO |
| 4.4.1.5.18 | Stocktake Create Interface | INV |  | TODO |
| 4.4.1.5.19 | Purchase Inbound (Approval) Interface | INV |  | TODO |
| 4.4.1.5.20 | Select Data Interface | INV |  | TODO |
| 4.4.1.6.1 | Sales Collection View Interface | FI |  | TODO |
| 4.4.1.6.2 | Sales Collection Create Interface | FI |  | TODO |
| 4.4.1.6.3 | Sales Collection Edit Interface | FI |  | TODO |
| 4.4.1.6.4 | Other Collection View Interface | FI |  | TODO |
| 4.4.1.6.5 | Other Collection Create Interface | FI |  | TODO |
| 4.4.1.6.6 | Other Collection Edit Interface | FI |  | TODO |
| 4.4.1.6.7 | Purchase Payment View Interface | FI |  | TODO |
| 4.4.1.6.8 | Purchase Payment Create Interface | FI |  | TODO |
| 4.4.1.6.9 | Purchase Payment Edit Interface | FI |  | TODO |
| 4.4.1.6.10 | Other Payment View Interface | FI |  | TODO |
| 4.4.1.6.11 | Other Payment Create Interface | FI |  | TODO |
| 4.4.1.6.12 | Other Payment Edit Interface | FI |  | TODO |
| 4.4.1.6.13 | Collection Write-off View Interface | FI |  | TODO |
| 4.4.1.6.14 | Collection Write-off Create Interface | FI |  | TODO |
| 4.4.1.6.15 | Payment Write-off View Interface | FI |  | TODO |
| 4.4.1.6.16 | Payment Write-off Create Interface | FI |  | TODO |
| 4.4.1.7.1 | Purchase Order Tracking Report Interface | BI |  | TODO |
| 4.4.1.7.2 | Purchase Detail Report Interface | BI |  | TODO |
| 4.4.1.7.3 | Purchase Summary Report (By Product) Interface | BI |  | TODO |
| 4.4.1.7.4 | Purchase Summary Report (By Supplier) Interface | BI |  | TODO |
| 4.4.1.7.5 | Sales Order Tracking Report Interface | BI |  | TODO |
| 4.4.1.7.6 | Sales Detail Report Interface | BI |  | TODO |
| 4.4.1.7.7 | Sales Summary Report (By Product) Interface | BI |  | TODO |
| 4.4.1.7.8 | Sales Summary Report (By Client) Interface | BI |  | TODO |
| 4.4.1.7.9 | Production Planning Tracking Report Interface | BI |  | TODO |
| 4.4.1.7.10 | Material Requisition Detail Report Interface | BI |  | TODO |
| 4.4.1.7.11 | Material Requisition Summary Report Interface | BI |  | TODO |
| 4.4.1.7.12 | Production Inbound Detail Report Interface | BI |  | TODO |
| 4.4.1.7.13 | Production Inbound Summary Report Interface | BI |  | TODO |
| 4.4.1.7.14 | Product Inventory Balance Report Interface | BI |  | TODO |
| 4.4.1.7.15 | Product Inbound & Outbound Detail Report Interface | BI |  | TODO |
| 4.4.1.7.16 | Product Inbound & Outbound Summary Report Interface | BI |  | TODO |
| 4.4.1.7.17 | Cash & Bank Report Interface | BI |  | TODO |
| 4.4.1.7.18 | Accounts Payable Detail Report Interface | BI |  | TODO |
| 4.4.1.7.19 | Accounts Receivable Detail Report Interface | BI |  | TODO |
| 4.4.1.7.20 | Client Statement Interface | BI |  | TODO |
| 4.4.1.7.21 | Supplier Statement Interface | BI |  | TODO |
| 4.4.1.7.22 | Other Income & Expense Detail Report Interface | BI |  | TODO |
| 4.4.1.8.1 | Client View Interface | MD / SYS |  | TODO |
| 4.4.1.8.2 | Client Create Interface | MD / SYS |  | TODO |
| 4.4.1.8.3 | Client Edit Interface | MD / SYS |  | TODO |
| 4.4.1.8.4 | Client Contact Management Interface | MD / SYS |  | TODO |
| 4.4.1.8.5 | Client Import Interface | MD / SYS |  | TODO |
| 4.4.1.8.6 | Supplier View Interface | MD / SYS |  | TODO |
| 4.4.1.8.7 | Supplier Create Interface | MD / SYS |  | TODO |
| 4.4.1.8.8 | Supplier Edit Interface | MD / SYS |  | TODO |
| 4.4.1.8.9 | Supplier Contact Management Interface | MD / SYS |  | TODO |
| 4.4.1.8.10 | Supplier Import Interface | MD / SYS |  | TODO |
| 4.4.1.8.11 | Product View Interface | MD / SYS |  | TODO |
| 4.4.1.8.12 | Product Create Interface | MD / SYS |  | TODO |
| 4.4.1.8.13 | Product Edit Interface | MD / SYS |  | TODO |
| 4.4.1.8.14 | Product Initial Stock Interface | MD / SYS |  | TODO |
| 4.4.1.8.15 | Product Image Interface | MD / SYS |  | TODO |
| 4.4.1.8.16 | Product Details Interface | MD / SYS |  | TODO |
| 4.4.1.8.17 | Warehouse View Interface | MD / SYS |  | TODO |
| 4.4.1.8.18 | Warehouse Create Interface | MD / SYS |  | TODO |
| 4.4.1.8.19 | Warehouse Edit Interface | MD / SYS |  | TODO |
| 4.4.1.8.20 | Account View Interface | MD / SYS |  | TODO |
| 4.4.1.8.21 | Account Create Interface | MD / SYS |  | TODO |
| 4.4.1.8.22 | Account Edit Interface | MD / SYS |  | TODO |
| 4.4.1.8.23 | User View Interface | MD / SYS |  | TODO |
| 4.4.1.8.24 | User Create Interface | MD / SYS |  | TODO |
| 4.4.1.8.25 | User Edit Interface | MD / SYS |  | TODO |
| 4.4.1.8.26 | User Permission Edit Interface | MD / SYS |  | TODO |
| 4.4.1.8.27 | Process View Interface | MD / SYS |  | TODO |
| 4.4.1.8.28 | Process Create Interface | MD / SYS |  | TODO |
| 4.4.1.8.29 | Process Edit Interface | MD / SYS |  | TODO |
| 4.4.1.8.30 | Client Category View Interface | MD / SYS |  | TODO |
| 4.4.1.8.31 | Client Category Create Interface | MD / SYS |  | TODO |
| 4.4.1.8.32 | Client Category Edit Interface | MD / SYS |  | TODO |
| 4.4.1.8.33 | Supplier Category View Interface | MD / SYS |  | TODO |
| 4.4.1.8.34 | Supplier Category Create Interface | MD / SYS |  | TODO |
| 4.4.1.8.35 | Supplier Category Edit Interface | MD / SYS |  | TODO |
| 4.4.1.8.36 | Item Category View Interface | MD / SYS |  | TODO |
| 4.4.1.8.37 | Item Category Create Interface | MD / SYS |  | TODO |
| 4.4.1.8.38 | Item Category Edit Interface | MD / SYS |  | TODO |
| 4.4.1.8.39 | Item Brand View Interface | MD / SYS |  | TODO |
| 4.4.1.8.40 | Item Brand Create Interface | MD / SYS |  | TODO |
| 4.4.1.8.41 | Item Brand Edit Interface | MD / SYS |  | TODO |
| 4.4.1.8.42 | Income & Expense Category View Interface | MD / SYS |  | TODO |
| 4.4.1.8.43 | Income & Expense Category Create Interface | MD / SYS |  | TODO |
| 4.4.1.8.44 | Income & Expense Category Edit Interface | MD / SYS |  | TODO |
| 4.4.1.8.45 | Unit of Measurement View Interface | MD / SYS |  | TODO |
| 4.4.1.8.46 | Unit of Measurement Create Interface | MD / SYS |  | TODO |
| 4.4.1.8.47 | Unit of Measurement Edit Interface | MD / SYS |  | TODO |
| 4.4.1.8.48 | Settlement Method View Interface | MD / SYS |  | TODO |
| 4.4.1.8.49 | Settlement Method Create Interface | MD / SYS |  | TODO |
| 4.4.1.8.50 | Settlement Method Edit Interface | MD / SYS |  | TODO |
| 4.4.1.8.51 | Process Category View Interface | MD / SYS |  | TODO |
| 4.4.1.8.52 | Process Category Create Interface | MD / SYS |  | TODO |
| 4.4.1.8.53 | Process Category Edit Interface | MD / SYS |  | TODO |
| 4.4.1.8.54 | Operation Log View Interface | MD / SYS |  | TODO |
| 4.4.1.8.55 | Parameter Edit Interface | MD / SYS |  | TODO |
| 4.4.1.8.56 | Print Settings Edit Interface | MD / SYS |  | TODO |
| 4.4.1.8.57 | Logo & Stamps Edit Interface | MD / SYS |  | TODO |
| 4.4.1.9.1 | Confirmation Box Interface | 前端共用 |  | TODO |
| 4.4.1.9.2 | Alert Box Interface | 前端共用 |  | TODO |
| 4.4.1.9.3 | Select Item Interface | 前端共用 |  | TODO |

## 7. 資料表對照表（SRS 4.3）

| BlueWhale ID | 表名 | 說明 | MyERP 表名 | MyERP REQ_DS43 | 狀態 |
|---|---|---|---|---|---|
| REQ_DS43.1 | Account | This table stores the information of user accounts. |  |  | TODO |
| REQ_DS43.2 | Area | This table stores the information of areas in cities. |  |  | TODO |
| REQ_DS43.3 | Brand | This table stores the information of item brands. |  |  | TODO |
| REQ_DS43.4 | Inventory | This table stores the information of inventories. |  |  | TODO |
| REQ_DS43.5 | Client | This table stores the information of clients. |  |  | TODO |
| REQ_DS43.6 | ClientAddress | This table stores the information of client addresses. |  |  | TODO |
| REQ_DS43.7 | ClientContact | This table stores information about client contacts. |  |  | TODO |
| REQ_DS43.8 | ClientType | This table stores the information about client types. |  |  | TODO |
| REQ_DS43.9 | CartList | This table stores the information of carts. |  |  | TODO |
| REQ_DS43.10 | CheckBill | This table stores the information of check bills. |  |  | TODO |
| REQ_DS43.11 | CheckBillItemPayment | This table stores information about item payment of a check bill. |  |  | TODO |
| REQ_DS43.12 | CheckBillItemPur | This table stores the information of item purchase of a check bill. |  |  | TODO |
| REQ_DS43.13 | CheckBillItemSales | This table stores information about item sales of a check bill. |  |  | TODO |
| REQ_DS43.14 | City | This table stores the information of cities. |  |  | TODO |
| REQ_DS43.15 | CostChange | This table stores information about cost changes. |  |  | TODO |
| REQ_DS43.16 | ClientTypeDis | This table stores information about discounts for different client types. |  |  | TODO |
| REQ_DS43.17 | CostChangeItem | This table stores detailed information about cost change of items. |  |  | TODO |
| REQ_DS43.18 | CheckBillItemReceivable | This table stores information about items receivable on check bills. |  |  | TODO |
| REQ_DS43.19 | InventoryTransfer | This table stores information about inventory transfers. |  |  | TODO |
| REQ_DS43.20 | Company | This table stores the information of companies. |  |  | TODO |
| REQ_DS43.21 | DataBacklist | This table stores information about data backlists. |  |  | TODO |
| REQ_DS43.22 | Dept | This table stores information about departments. |  |  | TODO |
| REQ_DS43.23 | Logs | This table stores information about system logs. |  |  | TODO |
| REQ_DS43.24 | InventoryTransferItem | This table stores information about items transferred by inventories. |  |  | TODO |
| REQ_DS43.25 | GoodsAssembly | This table stores information about goods assemblies. |  |  | TODO |
| REQ_DS43.26 | GoodsAssemblyDetails | This table stores detailed information about goods assemblies. |  |  | TODO |
| REQ_DS43.27 | Months | This table stores information about months. |  |  | TODO |
| REQ_DS43.28 | NewsList | This table stores information about news. |  |  | TODO |
| REQ_DS43.29 | AttendanceList | This table stores information about attendance. |  |  | TODO |
| REQ_DS43.30 | GoodsInitialStock | This table stores information about initial stock of goods. |  |  | TODO |
| REQ_DS43.31 | GoodsPriceClientType | This table stores information about goods prices for client type. |  |  | TODO |
| REQ_DS43.32 | GoodsAttribute | This table stores information about attributes of goods. |  |  | TODO |
| REQ_DS43.33 | GoodsBomListItem | This table stores information about BOM list details. |  |  | TODO |
| REQ_DS43.34 | Goods | This table stores information about goods. |  |  | TODO |
| REQ_DS43.35 | NoticeInfo | This table stores information about notices. |  |  | TODO |
| REQ_DS43.36 | OtherGet | This table stores information on money collection bills of other types. |  |  | TODO |
| REQ_DS43.37 | PageList | This table stores information about page lists. |  |  | TODO |
| REQ_DS43.38 | ProduceGetList | This table stores information about material requisitions. |  |  | TODO |
| REQ_DS43.39 | GoodsTypePicList | This table stores information about goods types images. |  |  | TODO |
| REQ_DS43.40 | ProcessType | This table stores information about the process types. |  |  | TODO |
| REQ_DS43.41 | OtherGetItem | This table stores detailed information of money collection bills of other types. |  |  | TODO |
| REQ_DS43.42 | Stocktake | This table stores information about stocktake. |  |  | TODO |
| REQ_DS43.43 | ProduceGetListItem | This table stores information of produce material requisition details. |  |  | TODO |
| REQ_DS43.44 | GoodsType | This table stores information about goods types. |  |  | TODO |
| REQ_DS43.45 | OtherIn | This table stores information about other inbounds. |  |  | TODO |
| REQ_DS43.46 | PayGet | This table stores information about payment received. |  |  | TODO |
| REQ_DS43.47 | ProduceIn | This table stores information about produces inbounds. |  |  | TODO |
| REQ_DS43.48 | OtherInItem | This table stores information about other inbound details. |  |  | TODO |
| REQ_DS43.49 | Payment | This table stores information about payments. |  |  | TODO |
| REQ_DS43.50 | ProduceInItem | This table stores detailed information about produce inbounds. |  |  | TODO |
| REQ_DS43.51 | GoodsBomListType | This table stores information about goods BOM list types. |  |  | TODO |
| REQ_DS43.52 | GoodsBrand | This table stores information about goods brands. |  |  | TODO |
| REQ_DS43.53 | OtherOutItem | This table stores information about other outbound details. |  |  | TODO |
| REQ_DS43.54 | PaymentSourceBillItem | This table stores information about details of payment source bills. |  |  | TODO |
| REQ_DS43.55 | PayType | This table stores information about payment types. |  |  | TODO |
| REQ_DS43.56 | GoodsDisassemblyDetails | This table stores detailed information about goods disassembly. |  |  | TODO |
| REQ_DS43.57 | OtherPay | This table stores information about other types of payments. |  |  | TODO |
| REQ_DS43.58 | PayType | This table stores information about product processes. |  |  | TODO |
| REQ_DS43.59 | GoodsDisassembly | This table stores information about goods disassembly. |  |  | TODO |
| REQ_DS43.60 | GoodsImages | This table stores information about goods images. |  |  | TODO |
| REQ_DS43.61 | OtherPayItem | This table stores information about details of other type of payments. |  |  | TODO |
| REQ_DS43.62 | ProcessList | This table stores information about processes. |  |  | TODO |
| REQ_DS43.63 | PurOrder | This table stores information about purchase orders. |  |  | TODO |
| REQ_DS43.64 | PurOrder | This table stores information about purchase orders. |  |  | TODO |
| REQ_DS43.65 | SalesReceipt | This table stores information about sales receipts. |  |  | TODO |
| REQ_DS43.66 | UsersPageList | This table stores information about users' pages. |  |  | TODO |
| REQ_DS43.67 | PurReceipt | This table stores information about purchase receipts. |  |  | TODO |
| REQ_DS43.68 | SalesReceiptItem | This table stores information about details of sales receipts. |  |  | TODO |
| REQ_DS43.69 | VendorType | This table stores information about vendor types. |  |  | TODO |
| REQ_DS43.70 | PurReceiptItem | This table stores information about purchase receipt details. |  |  | TODO |
| REQ_DS43.71 | Shop | This table stores information about shops. |  |  | TODO |
| REQ_DS43.72 | Vendor | This table stores information about vendors. |  |  | TODO |
| REQ_DS43.73 | SystemSet | This table stores information about system settings. |  |  | TODO |
| REQ_DS43.74 | Roles | This table stores information about roles. |  |  | TODO |
| REQ_DS43.75 | SalesOrderItem | This table stores detailed information about sales orders. |  |  | TODO |
| REQ_DS43.76 | SalesOrder | This table stores information about sales orders. |  |  | TODO |
| REQ_DS43.77 | VendorLinkman | This table stores information about vendors’ linkmen. |  |  | TODO |
| REQ_DS43.78 | ProduceListItem | This table stores information about details of products. |  |  | TODO |
| REQ_DS43.79 | ProduceListItemBom | This table stores information about produces list BOM details. |  |  | TODO |
| REQ_DS43.80 | ReceivableSourceBillItem | This table stores information about receivable source details. |  |  | TODO |
| REQ_DS43.81 | PageList | This table stores information about pages. |  |  | TODO |
| REQ_DS43.82 | Users | This table stores information about users. |  |  | TODO |
| REQ_DS43.83 | ReceivableAccountItem | This table stores information about receivable account details. |  |  | TODO |
| REQ_DS43.84 | SalesQuote | This table stores information about sales quotes. |  |  | TODO |
| REQ_DS43.85 | Unit | This table stores information about units. |  |  | TODO |
| REQ_DS43.86 | Logistics | This table stores information about logistics. |  |  | TODO |
| REQ_DS43.87 | Receivable | This table stores information about receivables. |  |  | TODO |
| REQ_DS43.88 | SalesQuoteItem | This table stores information about sales quote details. |  |  | TODO |

## 8. 舊系統原始碼（docs/reference/legacy-source，不提交）

- `00-LEGACY-SCHEMA.md`：136 張表、141 個檢視、10 個 SP 的欄位摘要。
- 業務邏輯主要在檢視與 DAL：庫存（`viewGoodsStoreNum`、`viewGoodsOutInFlow*`、`viewGoodsBalance`）、成本調整（`costChange*`）、核銷（`CheckBill*`）、往來帳（`viewClientNeedPayFlow`、`viewAccountFlow`）、單號（`makeBillNumber`）、價格（`procGetGoodsPrice*`）。
- 只可參考業務規則並以自己的文字寫入 `business-rules.md`；不得複製代碼、SQL 或表結構。
