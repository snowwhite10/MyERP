# BlueWhale 參考筆記與功能對照表

> **預填說明**：BlueWhale 側（ID、描述、介面、資料表）已由人手用腳本從 `BlueWhaleERP_SRS.docx` 抽取。
> WU-P0-002（2026-10-10）已逐項核對 docx 原文、補充第 2–4 節，並填寫第 5–7 節 MyERP 側欄位（分配規則見 `docs/01-architecture/adr/ADR-002-需求ID與介面編號分配.md`）。之後每完成功能即更新「狀態」。
> 本檔會提交到公開 repo：只寫功能名稱與結構，**不得**抄錄 NDA、訪談、客戶名稱、金額等內容。

## 1. 規模（已用腳本逐項核對 docx 原文）

- 功能需求 REQ_F：222 項（ID 無重複；第 5 節 222 行與原文 ID、描述逐字一致）
- 資料表 REQ_DS43：88 張（第 7 節與原文一致；原文表名有重複，見第 4 節）
- 介面：SRS 4.4 = 150 個，SRS 4.5 = 150 個，SDS 4.2 = 150 個（第 6 節與 SRS 4.4 名稱、次序完全一致）
- 介面欄位 REQ_IO_A：1,416 項
- 非功能需求：4.2 為純敘述，**沒有** REQ_NF ID（MyERP 依 MASTER_PROMPT 15.2 補上 `REQ_NF42{小節}.{序號}`）
- SDS 4.1 欄位表：90 張（與 SRS 4.3 的 88 張不是同一組表，見第 4 節）

## 2. 章節大綱（SRS / SDS）

### 2.1 SRS（約 3,630 段落、1,735 張表、161 張圖）

| 章節 | 規模 | 內容形式 |
|---|---|---|
| Non-Disclosure Agreement | 約 540 段 | 條文敘述；雙方資料表（`<Client>` / `<Contractor>` 佔位符）；簽署表 |
| 封面 + 目錄 | — | 封面欄位：Contact No.、Project Name、Editor、Date；Pre-Reviewed / Reviewed / Approved by 簽署表；目錄為 Word TOC 欄位（`TOC \h \u \z \t "Heading 1,1,…,Heading 6,6"`） |
| 1 Introduction（1.1–1.7） | 約 50 段、1 表 | 1.3 縮寫表（2 欄：術語 / 說明）；1.5 References 列出 IEEE Std 830-1998 等 |
| 2 Overall Description（2.1–2.6） | 約 150 段、4 表 | 2.2 每模組一個 Heading3（2.2.1 Sales … 2.2.7 Setting，User 無獨立小節）；2.3 用戶類別表（2 欄）；2.4.1 OS 表（OS / Version）；2.4.2 硬件表（2 欄）；2.6 分 2.6.1 Assumptions、2.6.2 Dependencies |
| 3 System Analysis | 約 40 段 | 3.1 訪談為問答段落；3.2.1–3.2.3 各 1–2 段 |
| 4.1 Functional Requirements | 1 段引言 + 1 圖 + 222 表 | 引言後放「General Use Case」圖（4 張圖片組成）；4.1.1 → Heading4 模組 → Heading5 子功能 → 每需求一表，表與表之間一個空段 |
| 4.2 Non-Functional Requirements | 約 23 段 | 4.2.1–4.2.5 純敘述，無表、無 ID |
| 4.3 Data Structure | 1 段引言 + 88 表 | 每表一張需求表 |
| 4.4 User Interface Requirements | 1 段引言 + 150 介面 + 1,416 表 | 4.4.1 User Portal → Heading4「4.4.1.N {模組} Module」→ Heading5 介面 → 每欄位 / 按鈕一表 |
| 4.5 User Interface Prototype | 150 介面、157 張截圖 | 與 4.4 同層級；每介面一張截圖（無 Figure 編號）；同頁多狀態時截圖下加一行狀態說明（例：Navigation Menu Collapsed / Expanded） |
| 4.6 Project Planning（4.6.1–4.6.5） | 約 65 段、1 圖、1 表 | 4.6.1 Scrum 敘述 4 段；4.6.2 甘特圖 1 張；4.6.3 人力表（No. / Position / Skill Requirement）；4.6.4 預算敘述（MyERP 金額用佔位符）；4.6.5 以普通段落作小標題（User Experience 等） |

### 2.2 SDS（約 1,000 段落、91 張表、159 張圖）

| 章節 | 規模 | 內容形式 |
|---|---|---|
| 封面 + 目錄 | 約 190 段 | 封面與簽署表同 SRS；目錄為**手打文字**（非 TOC 欄位） |
| 1 Introduction（1.1–1.6） | 約 40 段 | 1.3 縮寫為段落列表 |
| 2 Design Consideration（2.1–2.3） | 約 25 段 | 純敘述 |
| 3 System Architecture and Architecture Design（3.1–3.3） | 約 30 段 | 純敘述，**無架構圖**（MyERP 依 MASTER_PROMPT 15.6 補架構圖） |
| 4.1 Database Design | 3 張 ER 圖 + 90 表 | 圖說「Figure 4.1.1 Entity Relationship Diagram (Part 1)」…(Part 3)；每表先 3 段：`Table Name: X`、`Primary Key: id`、一句說明，再接欄位表 |
| 4.2 User Interface Design | 150 介面、156 張截圖 | 4.2.1 User Portal → 4.2.1.N {模組} Module → 介面 Heading5 + 截圖（無 Figure 編號） |

### 2.3 版面與樣式

| 項目 | SRS | SDS | MyERP 決定 |
|---|---|---|---|
| 紙張 | Letter 直向（含 2 個橫向 section） | A4 直向（含橫向 section） | 兩份統一 A4，邊界 1 inch（1440 twips），頁首頁尾距 0.5 inch |
| 內文 | Arial 11pt（東亞字型同為 Arial），行距 1.5（line=360） | 同左 | 同 BlueWhale |
| Heading 1–5 | 20 / 17 / 14 / 12 / 11pt；H3 灰色 #434343、非粗體；其餘黑色 | 20 / 16 / 14 / 12 / 11pt | 採 SRS 數值，兩份共用 |
| 標題編號 | 手打在標題文字內（非 Word 自動編號） | 同左 | 同 BlueWhale（docgen 產生編號文字） |
| 頁首 / 頁尾 | 空白（無頁碼） | 空白 | 頁尾加「Page X of Y」（BlueWhale 欠缺，MyERP 補） |
| 圖說 | 置中、斜體、放圖下方；格式不一（SRS「Figure 1: …」、SDS「Figure 4.1.1 …」） | 同左 | 統一「Figure {章節}.{序號} {標題}」置中斜體放圖下 |
| 修訂痕跡 | 原文殘留追蹤修訂與空白標題段落 | — | docgen 輸出不得含追蹤修訂與空白標題 |

## 3. 表格格式（docx 實測；寬度單位 twips，1 cm ≈ 567）

共通：固定版面（fixed layout）、靠左、黑色單線框、無底色、儲存格內距 100、行距 1.5、欄名粗體、表格 style 無格線（框線全寫在儲存格）。

| 表格 | 總寬 | 欄寬 | 框線 | 結構（**粗體**為欄名；span3 = 合併 3 欄） |
|---|---|---|---|---|
| SRS 4.1.1 REQ_F | 9345 | 2220 / 3525 / 2370 / 1230 | 1pt（sz 8） | **Requirement ID** \| ID \| **Version** \| 1.0；**Description** \| span3；**Author** \| Developer span3。列高 ≥ 465 |
| SRS 4.3 REQ_DS43 | 9360 | 2340 × 4 | 1pt | **Requirement ID** \| ID \| **Version** \| 1.0；**Table Name** span3；**Description** span3；**Author** span3 |
| SRS 4.4 REQ_IO_A | 9360 | 同 4 欄 | 0.75pt（sz 6） | **Requirement ID** \| ID \| **Version** \| 1.0；**Item** span3；**Description** span3；**Purpose** span3；**Format** \| 值 \| **Valid Range** \| 值；**Related I/O** span3；**Author** span3 |
| SDS 4.1 欄位表 | 8000 | 2000 × 4 | 0.5pt（sz 4） | 首列欄名粗體置中：Name of Attribute \| Data Type \| Size \| PK/FK/IS NULL/ NOT NULL；Size 寫法：int 為 `-`、datetime 為 `8`、decimal 為 `(18,2)`、字串為長度；約束寫 `PK, NOT NULL`、`FK, IS NULL`、`IS NULL`。列高 ≥ 530 |
| 封面簽署表 | 9360 | 2070 / 1890 / 720 / 1875 / 1245 / 1560 | 1pt | 3 列：Pre-Reviewed by / Reviewed by / Approved by，各列含 Date、Signature 欄 |
| SRS 1.3 縮寫表 | 9360 | 2985 / 6375 | 1pt | 術語 \| 說明（無表頭列） |
| SRS 4.6.3 人力表 | 9435 | 600 / 2685 / 6150 | 1pt | 表頭 No. \| Position \| Skill Requirement |

MyERP 一律照用以上格式（MASTER_PROMPT 15.3）；SDS 4.1 的 MyERP 版 Size 欄改用實際 SQL Server 型別長度（例 `nvarchar` 100、`decimal` (19,4)），由 docgen 從 `INFORMATION_SCHEMA` 產生。

## 4. 已知不一致（不要照抄）

- BlueWhale 部分 REQ_F 編號漏了模組編號：User（4.1.1.1）用 `REQ_F411.1.x`；Settings 的 User Management（8.6）用 `REQ_F411.8.x`；Operation Log、Parameter、Print Settings、Logo & Stamp（8.16–8.19）用 `REQ_F411.{子功能}.{序號}`（例如 `REQ_F411.19.2`）。MyERP 一律用完整格式 `REQ_F411{模組}.{子功能}.{序號}`，User 為 `REQ_F4111.x.y`。
- SRS 4.4 / 4.5 與 SDS 4.2 介面次序不同：Sales Order 與 Sales Outbound 在 SRS 為 Create → View → Edit，SDS 為 View → Create → Edit。MyERP 統一 View → Create → Edit，三處完全一致。
- 介面名稱三處不一：4.5 寫「Select Client (Sales Unit) Interface」；4.5 / SDS 寫「Supplier Contact Interface」「Operation Log Interface」「Logo & Stamp Edit Interface」，4.4 則為「Supplier Contact Management Interface」「Operation Log View Interface」「Logo & Stamps Edit Interface」。MyERP 以 4.4 名稱為準，`Logo & Stamps` 改為 `Logo & Stamp`。
- REQ_F4115.7.1（Stocktake）描述誤寫為「create records of inventory transfer」；MyERP 改為 create stocktake。
- REQ_DS43 原文錯誤：43.1 Account 描述為 user accounts，實為資金（現金 / 銀行）帳戶；43.58 表名寫 PayType 但描述為 product processes；43.63 / 43.64 同名 PurOrder（43.64 應為訂單明細）；43.37 / 43.81 同名 PageList；Brand 與 GoodsBrand 重複。
- SRS 4.3（88 張）與 SDS 4.1（90 張）表清單不一致：SDS 有 GoodsAttributeList、GoodsBomList、OtherOut、PaymentAccountItem、ProduceList、ProduceProcessList 等，SRS 有 AttendanceList、ProduceGetList 等；MyERP 由 docgen 從實際資料庫產生兩處，保證一致。
- 4.3 的表名為 PascalCase 英文（Account、Area…），MyERP 用模組前綴 + snake_case。
- 標題「4.1.1 General Functionsdata」漏空格；MyERP 寫「4.1.1 General Functions data」。

## 4A. MyERP 對照規則（摘要，詳見 ADR-002）

- **REQ_F**：BlueWhale ID 正規化為完整格式，子功能編號與序號沿用 BlueWhale；唯一例外是 Sales（模組 2）：依 MASTER_PROMPT 15.1，子功能 1 = Sales Quotation，所以 BlueWhale 的 Sales Order（2.1）→ MyERP 2.2、Sales Outbound（2.2）→ 2.3。MyERP 新增的子功能與動作（print、export、void 等）一律在既有編號之後追加。
- **介面**：沿用 BlueWhale 4.4 編號；只有 Sales 與 Inventory 模組在同一功能內改為 View → Create → Edit 次序，Sales 另保留 4.4.1.2.1–2.3 給 Sales Quotation View / Create / Edit。新增介面在各模組最後追加。
- **資料表**：第 7 節只填**預計** MyERP 表名（依 MASTER_PROMPT 6.1 前綴）；`REQ_DS43` 編號在表真正建立時按 `state.json.idCounters.REQ_DS43` 連續分配，故標「待分配」。多張 BlueWhale 表可合併為一張 MyERP 表（例：Area + City → `md_region`）。
- **WONT_DO**：CartList（微信商城購物車）、DataBacklist（資料庫備份記錄，屬 DBA 維運，不在應用層）、Months（報表輔助月份表，改用 SQL 日期函數）；功能 REQ_F411.16.2「刪除操作日誌」（稽核記錄只增不改，MASTER_PROMPT 6.7／8.2，資料庫 trigger 禁止刪改，ADR-019）。
- **SYS 用戶刪除**（REQ_F411.8.4）以停用取代（REQ_F4118.23.7）：用戶被稽核與單據引用，依 MASTER_PROMPT 6.5 只能停用。2026-10-11 WU-SYS-016d 把 SYS 列改為實際需求／介面編號（原預留的 REQ_F4118.6.x、16.x 與 4.4.1.8.23–26、54 沒有被使用）。SYS 資料表：Logs → `sys_audit_log`；PageList／UsersPageList（選單頁面權限）→ 程式定義的權限目錄 + `sys_role_permission`／`sys_user_role`（ADR-014）；NoticeInfo → `sys_notification`；NewsList 為 WONT_DO（BlueWhale 4.1.1／4.4 沒有任何新聞功能或介面，只有資料表；公司內廣播由站內通知 REQ_F4118.33.1 涵蓋）。
- **狀態**：`未開始` / `進行中` / `完成` / `WONT_DO`。

## 5. 功能對照表（REQ_F）

| BlueWhale 章節 | BlueWhale ID | 功能描述 | MyERP 模組 | MyERP 需求 ID | 狀態 |
|---|---|---|---|---|---|
| 4.1.1.1 User | REQ_F411.1.1 | System should allow user to login into the website | SYS | REQ_F4118.24.1 | 完成 |
| 4.1.1.1 User | REQ_F411.1.2 | System should allow user to change password | SYS | REQ_F4111.1.2 | 完成 |
| 4.1.1.1 User | REQ_F411.1.3 | System should allow user to logout | SYS | REQ_F4118.24.3 | 完成 |
| 4.1.1.2.1 Sales Order | REQ_F4112.1.1 | System should enable user to view sales order | SD | REQ_F4112.2.1 | 未開始 |
| 4.1.1.2.1 Sales Order | REQ_F4112.1.2 | System should enable user to create sales order | SD | REQ_F4112.2.2 | 未開始 |
| 4.1.1.2.1 Sales Order | REQ_F4112.1.3 | System should enable user to edit sales order | SD | REQ_F4112.2.3 | 未開始 |
| 4.1.1.2.1 Sales Order | REQ_F4112.1.4 | System should enable user to approve sales order | SD | REQ_F4112.2.4 | 未開始 |
| 4.1.1.2.1 Sales Order | REQ_F4112.1.5 | System should enable user to reject sales order | SD | REQ_F4112.2.5 | 未開始 |
| 4.1.1.2.1 Sales Order | REQ_F4112.1.6 | System should enable user to delete sales order | SD | REQ_F4112.2.6 | 未開始 |
| 4.1.1.2.2 Sales Outbound | REQ_F4112.2.1 | System should enable user to view sales outbounds | SD | REQ_F4112.3.1 | 未開始 |
| 4.1.1.2.2 Sales Outbound | REQ_F4112.2.2 | System should enable user to create sales outbounds | SD | REQ_F4112.3.2 | 未開始 |
| 4.1.1.2.2 Sales Outbound | REQ_F4112.2.3 | System should enable user to edit sales outbounds | SD | REQ_F4112.3.3 | 未開始 |
| 4.1.1.2.2 Sales Outbound | REQ_F4112.2.4 | System should enable user to approve sales outbounds | SD | REQ_F4112.3.4 | 未開始 |
| 4.1.1.2.2 Sales Outbound | REQ_F4112.2.5 | System should enable user to reject sales outbounds | SD | REQ_F4112.3.5 | 未開始 |
| 4.1.1.2.2 Sales Outbound | REQ_F4112.2.6 | System should enable user to delete sales outbounds | SD | REQ_F4112.3.6 | 未開始 |
| 4.1.1.3.1 Bill Of Material (BOM) | REQ_F4113.1.1 | System should enable user to view BOM | PP | REQ_F4113.1.1 | 未開始 |
| 4.1.1.3.1 Bill Of Material (BOM) | REQ_F4113.1.2 | System should enable user to create BOM | PP | REQ_F4113.1.2 | 未開始 |
| 4.1.1.3.1 Bill Of Material (BOM) | REQ_F4113.1.3 | System should enable user to edit BOM | PP | REQ_F4113.1.3 | 未開始 |
| 4.1.1.3.1 Bill Of Material (BOM) | REQ_F4113.1.4 | System should enable user to approve BOM | PP | REQ_F4113.1.4 | 未開始 |
| 4.1.1.3.1 Bill Of Material (BOM) | REQ_F4113.1.5 | System should enable user to reject BOM | PP | REQ_F4113.1.5 | 未開始 |
| 4.1.1.3.1 Bill Of Material (BOM) | REQ_F4113.1.6 | System should enable user to delete BOM | PP | REQ_F4113.1.6 | 未開始 |
| 4.1.1.3.1 Bill Of Material (BOM) | REQ_F4113.1.7 | System should enable user to view BOM Grouping | PP | REQ_F4113.1.7 | 未開始 |
| 4.1.1.3.1 Bill Of Material (BOM) | REQ_F4113.1.8 | System should enable user to create BOM Grouping | PP | REQ_F4113.1.8 | 未開始 |
| 4.1.1.3.1 Bill Of Material (BOM) | REQ_F4113.1.9 | System should enable user to edit BOM Grouping | PP | REQ_F4113.1.9 | 未開始 |
| 4.1.1.3.1 Bill Of Material (BOM) | REQ_F4113.1.10 | System should enable user to delete BOM Grouping | PP | REQ_F4113.1.10 | 未開始 |
| 4.1.1.3.2 Production Planning | REQ_F4113.2.1 | System should enable user to view production planning | PP | REQ_F4113.2.1 | 未開始 |
| 4.1.1.3.2 Production Planning | REQ_F4113.2.2 | System should enable user to create production planning | PP | REQ_F4113.2.2 | 未開始 |
| 4.1.1.3.2 Production Planning | REQ_F4113.2.3 | System should enable user to edit production planning | PP | REQ_F4113.2.3 | 未開始 |
| 4.1.1.3.2 Production Planning | REQ_F4113.2.4 | System should enable user to approve production planning | PP | REQ_F4113.2.4 | 未開始 |
| 4.1.1.3.2 Production Planning | REQ_F4113.2.5 | System should enable user to reject production planning | PP | REQ_F4113.2.5 | 未開始 |
| 4.1.1.3.2 Production Planning | REQ_F4113.2.6 | System should enable user to delete production planning | PP | REQ_F4113.2.6 | 未開始 |
| 4.1.1.3.3 Material Requisition | REQ_F4113.3.1 | System should enable user to view material requisition | PP | REQ_F4113.3.1 | 未開始 |
| 4.1.1.3.3 Material Requisition | REQ_F4113.3.2 | System should enable user to create material requisition | PP | REQ_F4113.3.2 | 未開始 |
| 4.1.1.3.3 Material Requisition | REQ_F4113.3.3 | System should enable user to edit material requisition | PP | REQ_F4113.3.3 | 未開始 |
| 4.1.1.3.3 Material Requisition | REQ_F4113.3.4 | System should enable user to approve material requisition | PP | REQ_F4113.3.4 | 未開始 |
| 4.1.1.3.3 Material Requisition | REQ_F4113.3.5 | System should enable user to reject material requisition | PP | REQ_F4113.3.5 | 未開始 |
| 4.1.1.3.3 Material Requisition | REQ_F4113.3.6 | System should enable user to delete material requisition | PP | REQ_F4113.3.6 | 未開始 |
| 4.1.1.3.4 Production Inbound | REQ_F4113.4.1 | System should enable user to view production inbound | PP | REQ_F4113.4.1 | 未開始 |
| 4.1.1.3.4 Production Inbound | REQ_F4113.4.2 | System should enable user to create production inbound | PP | REQ_F4113.4.2 | 未開始 |
| 4.1.1.3.4 Production Inbound | REQ_F4113.4.3 | System should enable user to edit production inbound | PP | REQ_F4113.4.3 | 未開始 |
| 4.1.1.3.4 Production Inbound | REQ_F4113.4.4 | System should enable user to approve production inbound | PP | REQ_F4113.4.4 | 未開始 |
| 4.1.1.3.4 Production Inbound | REQ_F4113.4.5 | System should enable user to reject production inbound | PP | REQ_F4113.4.5 | 未開始 |
| 4.1.1.3.4 Production Inbound | REQ_F4113.4.6 | System should enable user to delete production inbound | PP | REQ_F4113.4.6 | 未開始 |
| 4.1.1.4.1 Purchase Order | REQ_F4114.1.1 | System should enable user to view purchase order | PUR | REQ_F4114.1.1 | 未開始 |
| 4.1.1.4.1 Purchase Order | REQ_F4114.1.2 | System should enable user to create purchase order | PUR | REQ_F4114.1.2 | 未開始 |
| 4.1.1.4.1 Purchase Order | REQ_F4114.1.3 | System should enable user to edit purchase order | PUR | REQ_F4114.1.3 | 未開始 |
| 4.1.1.4.1 Purchase Order | REQ_F4114.1.4 | System should enable user to approve purchase order | PUR | REQ_F4114.1.4 | 未開始 |
| 4.1.1.4.1 Purchase Order | REQ_F4114.1.5 | System should enable user to reject purchase order | PUR | REQ_F4114.1.5 | 未開始 |
| 4.1.1.4.1 Purchase Order | REQ_F4114.1.6 | System should enable user to delete purchase order | PUR | REQ_F4114.1.6 | 未開始 |
| 4.1.1.4.2 Purchase Inbound | REQ_F4114.2.1 | System should enable user to view purchase inbound | PUR | REQ_F4114.2.1 | 未開始 |
| 4.1.1.4.2 Purchase Inbound | REQ_F4114.2.2 | System should enable user to create purchase inbound | PUR | REQ_F4114.2.2 | 未開始 |
| 4.1.1.4.2 Purchase Inbound | REQ_F4114.2.3 | System should enable user to edit purchase inbound | PUR | REQ_F4114.2.3 | 未開始 |
| 4.1.1.4.2 Purchase Inbound | REQ_F4114.2.4 | System should enable user to approve purchase inbound | PUR | REQ_F4114.2.4 | 未開始 |
| 4.1.1.4.2 Purchase Inbound | REQ_F4114.2.5 | System should enable user to reject purchase inbound | PUR | REQ_F4114.2.5 | 未開始 |
| 4.1.1.4.2 Purchase Inbound | REQ_F4114.2.6 | System should enable user to delete purchase inbound | PUR | REQ_F4114.2.6 | 未開始 |
| 4.1.1.5.1 Inventory | REQ_F4115.1.1 | System should enable user to view records of inventory | INV | REQ_F4115.1.1 | 未開始 |
| 4.1.1.5.1 Inventory | REQ_F4115.1.2 | System should enable user to export inventory records as excel spreadsheet | INV | REQ_F4115.1.2 | 未開始 |
| 4.1.1.5.2 Other Inbound | REQ_F4115.2.1 | System should enable user to view records of other inbound | INV | REQ_F4115.2.1 | 未開始 |
| 4.1.1.5.2 Other Inbound | REQ_F4115.2.2 | System should enable user to create records of other inbound | INV | REQ_F4115.2.2 | 未開始 |
| 4.1.1.5.2 Other Inbound | REQ_F4115.2.3 | System should enable user to edit records of other inbound | INV | REQ_F4115.2.3 | 未開始 |
| 4.1.1.5.2 Other Inbound | REQ_F4115.2.4 | System should enable user to approve requests of other inbound | INV | REQ_F4115.2.4 | 未開始 |
| 4.1.1.5.2 Other Inbound | REQ_F4115.2.5 | System should enable user to reject requests of other inbound | INV | REQ_F4115.2.5 | 未開始 |
| 4.1.1.5.2 Other Inbound | REQ_F4115.2.6 | System should enable user to delete records of other inbound | INV | REQ_F4115.2.6 | 未開始 |
| 4.1.1.5.3 Other Outbound | REQ_F4115.3.1 | System should enable user to view records of other outbound | INV | REQ_F4115.3.1 | 未開始 |
| 4.1.1.5.3 Other Outbound | REQ_F4115.3.2 | System should enable user to create records of other outbound | INV | REQ_F4115.3.2 | 未開始 |
| 4.1.1.5.3 Other Outbound | REQ_F4115.3.3 | System should enable user to edit records of other outbound | INV | REQ_F4115.3.3 | 未開始 |
| 4.1.1.5.3 Other Outbound | REQ_F4115.3.4 | System should enable user to approve requests of other outbound | INV | REQ_F4115.3.4 | 未開始 |
| 4.1.1.5.3 Other Outbound | REQ_F4115.3.5 | System should enable user to reject requests of other outbound | INV | REQ_F4115.3.5 | 未開始 |
| 4.1.1.5.3 Other Outbound | REQ_F4115.3.6 | System should enable user to delete records of other outbound | INV | REQ_F4115.3.6 | 未開始 |
| 4.1.1.5.4 Product Assembly | REQ_F4115.4.1 | System should enable user to view records of product assembly | INV | REQ_F4115.4.1 | 未開始 |
| 4.1.1.5.4 Product Assembly | REQ_F4115.4.2 | System should enable user to create records of product assembly | INV | REQ_F4115.4.2 | 未開始 |
| 4.1.1.5.4 Product Assembly | REQ_F4115.4.3 | System should enable user to edit records of product assembly | INV | REQ_F4115.4.3 | 未開始 |
| 4.1.1.5.4 Product Assembly | REQ_F4115.4.4 | System should enable user to approve requests of product assembly | INV | REQ_F4115.4.4 | 未開始 |
| 4.1.1.5.4 Product Assembly | REQ_F4115.4.5 | System should enable user to reject requests of product assembly | INV | REQ_F4115.4.5 | 未開始 |
| 4.1.1.5.4 Product Assembly | REQ_F4115.4.6 | System should enable user to delete records of product assembly | INV | REQ_F4115.4.6 | 未開始 |
| 4.1.1.5.5 Product Disassembly | REQ_F4115.5.1 | System should enable user to view records of product disassembly | INV | REQ_F4115.5.1 | 未開始 |
| 4.1.1.5.5 Product Disassembly | REQ_F4115.5.2 | System should enable user to create records of product disassembly | INV | REQ_F4115.5.2 | 未開始 |
| 4.1.1.5.5 Product Disassembly | REQ_F4115.5.3 | System should enable user to edit records of product disassembly | INV | REQ_F4115.5.3 | 未開始 |
| 4.1.1.5.5 Product Disassembly | REQ_F4115.5.4 | System should enable user to approve requests of product disassembly | INV | REQ_F4115.5.4 | 未開始 |
| 4.1.1.5.5 Product Disassembly | REQ_F4115.5.5 | System should enable user to reject requests of product disassembly | INV | REQ_F4115.5.5 | 未開始 |
| 4.1.1.5.5 Product Disassembly | REQ_F4115.5.6 | System should enable user to delete records of product disassembly | INV | REQ_F4115.5.6 | 未開始 |
| 4.1.1.5.6 Inventory Transfer | REQ_F4115.6.1 | System should enable user to view records of inventory transfer | INV | REQ_F4115.6.1 | 未開始 |
| 4.1.1.5.6 Inventory Transfer | REQ_F4115.6.2 | System should enable user to create records of inventory transfer | INV | REQ_F4115.6.2 | 未開始 |
| 4.1.1.5.6 Inventory Transfer | REQ_F4115.6.3 | System should enable user to edit records of inventory transfer | INV | REQ_F4115.6.3 | 未開始 |
| 4.1.1.5.6 Inventory Transfer | REQ_F4115.6.4 | System should enable user to approve requests of inventory transfer | INV | REQ_F4115.6.4 | 未開始 |
| 4.1.1.5.6 Inventory Transfer | REQ_F4115.6.5 | System should enable user to reject requests of inventory transfer | INV | REQ_F4115.6.5 | 未開始 |
| 4.1.1.5.6 Inventory Transfer | REQ_F4115.6.6 | System should enable user to delete records of inventory transfer | INV | REQ_F4115.6.6 | 未開始 |
| 4.1.1.5.7 Stocktake | REQ_F4115.7.1 | System should enable user to create records of inventory transfer | INV | REQ_F4115.7.1 | 未開始 |
| 4.1.1.5.7 Stocktake | REQ_F4115.7.2 | System should enable user to generate inventory profit/loss reports | INV | REQ_F4115.7.2 | 未開始 |
| 4.1.1.6.1 Sales Collection | REQ_F4116.1.1 | System should enable user to view sales collection details | FI | REQ_F4116.1.1 | 未開始 |
| 4.1.1.6.1 Sales Collection | REQ_F4116.1.2 | System should enable user to create new sales collection | FI | REQ_F4116.1.2 | 未開始 |
| 4.1.1.6.1 Sales Collection | REQ_F4116.1.3 | System should enable user to edit sales collection details | FI | REQ_F4116.1.3 | 未開始 |
| 4.1.1.6.1 Sales Collection | REQ_F4116.1.4 | System should enable user to approve sales collections | FI | REQ_F4116.1.4 | 未開始 |
| 4.1.1.6.1 Sales Collection | REQ_F4116.1.5 | System should enable user to reject sales collections | FI | REQ_F4116.1.5 | 未開始 |
| 4.1.1.6.1 Sales Collection | REQ_F4116.1.6 | System should enable users to delete sales collection records. | FI | REQ_F4116.1.6 | 未開始 |
| 4.1.1.6.2 Other Collection | REQ_F4116.2.1 | System should enable user to view other collection details | FI | REQ_F4116.2.1 | 未開始 |
| 4.1.1.6.2 Other Collection | REQ_F4116.2.2 | System should enable user to create new other collection | FI | REQ_F4116.2.2 | 未開始 |
| 4.1.1.6.2 Other Collection | REQ_F4116.2.3 | System should enable user to edit other collection details | FI | REQ_F4116.2.3 | 未開始 |
| 4.1.1.6.2 Other Collection | REQ_F4116.2.4 | System should enable user to approve other collections | FI | REQ_F4116.2.4 | 未開始 |
| 4.1.1.6.2 Other Collection | REQ_F4116.2.5 | System should enable user to reject other collections | FI | REQ_F4116.2.5 | 未開始 |
| 4.1.1.6.2 Other Collection | REQ_F4116.2.6 | System should enable users to delete other collection records. | FI | REQ_F4116.2.6 | 未開始 |
| 4.1.1.6.3 Purchase Payment | REQ_F4116.3.1 | System should enable user to view purchase payment details | FI | REQ_F4116.3.1 | 未開始 |
| 4.1.1.6.3 Purchase Payment | REQ_F4116.3.2 | System should enable user to create purchase payments | FI | REQ_F4116.3.2 | 未開始 |
| 4.1.1.6.3 Purchase Payment | REQ_F4116.3.3 | System should enable user to edit purchase payment details | FI | REQ_F4116.3.3 | 未開始 |
| 4.1.1.6.3 Purchase Payment | REQ_F4116.3.4 | System should enable user to approve purchase payments | FI | REQ_F4116.3.4 | 未開始 |
| 4.1.1.6.3 Purchase Payment | REQ_F4116.3.5 | System should enable user to reject purchase payments | FI | REQ_F4116.3.5 | 未開始 |
| 4.1.1.6.3 Purchase Payment | REQ_F4116.3.6 | System should enable users to delete purchase payments | FI | REQ_F4116.3.6 | 未開始 |
| 4.1.1.6.4 Other Payment | REQ_F4116.4.1 | System should enable user to view other payment details | FI | REQ_F4116.4.1 | 未開始 |
| 4.1.1.6.4 Other Payment | REQ_F4116.4.2 | System should enable user to create other payments | FI | REQ_F4116.4.2 | 未開始 |
| 4.1.1.6.4 Other Payment | REQ_F4116.4.3 | System should enable user to edit other payment details | FI | REQ_F4116.4.3 | 未開始 |
| 4.1.1.6.4 Other Payment | REQ_F4116.4.4 | System should enable user to approve other payments | FI | REQ_F4116.4.4 | 未開始 |
| 4.1.1.6.4 Other Payment | REQ_F4116.4.5 | System should enable user to reject other payments | FI | REQ_F4116.4.5 | 未開始 |
| 4.1.1.6.4 Other Payment | REQ_F4116.4.6 | System should enable users to delete other payments | FI | REQ_F4116.4.6 | 未開始 |
| 4.1.1.6.5 Collection Write-off | REQ_F4116.5.1 | System should enable user to view collection write-off details | FI | REQ_F4116.5.1 | 未開始 |
| 4.1.1.6.5 Collection Write-off | REQ_F4116.5.2 | System should enable user to create collection write-offs | FI | REQ_F4116.5.2 | 未開始 |
| 4.1.1.6.5 Collection Write-off | REQ_F4116.5.3 | System should enable user to edit collection write-off details | FI | REQ_F4116.5.3 | 未開始 |
| 4.1.1.6.5 Collection Write-off | REQ_F4116.5.4 | System should enable user to approve collection write-offs | FI | REQ_F4116.5.4 | 未開始 |
| 4.1.1.6.5 Collection Write-off | REQ_F4116.5.5 | System should enable user to reject collection write-offs | FI | REQ_F4116.5.5 | 未開始 |
| 4.1.1.6.5 Collection Write-off | REQ_F4116.5.6 | System should enable users to delete collection write-offs | FI | REQ_F4116.5.6 | 未開始 |
| 4.1.1.6.6 Payment Write-off | REQ_F4116.6.1 | System should enable user to view payment write-off details | FI | REQ_F4116.6.1 | 未開始 |
| 4.1.1.6.6 Payment Write-off | REQ_F4116.6.2 | System should enable user to create payment write-offs | FI | REQ_F4116.6.2 | 未開始 |
| 4.1.1.6.6 Payment Write-off | REQ_F4116.6.3 | System should enable user to edit payment write-off details | FI | REQ_F4116.6.3 | 未開始 |
| 4.1.1.6.6 Payment Write-off | REQ_F4116.6.4 | System should enable user to approve payment write-offs | FI | REQ_F4116.6.4 | 未開始 |
| 4.1.1.6.6 Payment Write-off | REQ_F4116.6.5 | System should enable user to reject payment write-offs | FI | REQ_F4116.6.5 | 未開始 |
| 4.1.1.6.6 Payment Write-off | REQ_F4116.6.6 | System should enable users to delete payment write-offs | FI | REQ_F4116.6.6 | 未開始 |
| 4.1.1.7.1 Purchase | REQ_F4117.1.1 | System should enable user to view Purchase Order Tracking Report | BI | REQ_F4117.1.1 | 未開始 |
| 4.1.1.7.1 Purchase | REQ_F4117.1.2 | System should enable user to view Purchase Detail Report | BI | REQ_F4117.1.2 | 未開始 |
| 4.1.1.7.1 Purchase | REQ_F4117.1.3 | System should enable user to view Purchase Summary Report (By Product) | BI | REQ_F4117.1.3 | 未開始 |
| 4.1.1.7.1 Purchase | REQ_F4117.1.4 | System should enable user to view Purchase Summary Report (By Supplier) | BI | REQ_F4117.1.4 | 未開始 |
| 4.1.1.7.2 Sales | REQ_F4117.2.1 | System should enable user to view Sales Order Tracking Report | BI | REQ_F4117.2.1 | 未開始 |
| 4.1.1.7.2 Sales | REQ_F4117.2.2 | System should enable user to view Sales Detail Report | BI | REQ_F4117.2.2 | 未開始 |
| 4.1.1.7.2 Sales | REQ_F4117.2.3 | System should enable user to view Sales Summary Report (By Product) | BI | REQ_F4117.2.3 | 未開始 |
| 4.1.1.7.2 Sales | REQ_F4117.2.4 | System should enable user to view Sales Summary Report (By Client) | BI | REQ_F4117.2.4 | 未開始 |
| 4.1.1.7.3 Production | REQ_F4117.3.1 | System should enable user to view Production Planning Tracking Report | BI | REQ_F4117.3.1 | 未開始 |
| 4.1.1.7.3 Production | REQ_F4117.3.2 | System should enable user to view Material Requisition Detail Report | BI | REQ_F4117.3.2 | 未開始 |
| 4.1.1.7.3 Production | REQ_F4117.3.3 | System should enable user to view Material Requisition Summary Report | BI | REQ_F4117.3.3 | 未開始 |
| 4.1.1.7.3 Production | REQ_F4117.3.4 | System should enable user to view Production Inbound Detail Report | BI | REQ_F4117.3.4 | 未開始 |
| 4.1.1.7.3 Production | REQ_F4117.3.5 | System should enable user to view Production Inbound Summary Report | BI | REQ_F4117.3.5 | 未開始 |
| 4.1.1.7.4 Warehouse | REQ_F4117.4.1 | System should enable user to view Product Inventory Balance Report | BI | REQ_F4117.4.1 | 未開始 |
| 4.1.1.7.4 Warehouse | REQ_F4117.4.2 | System should enable user to view Product Inbound & Outbound Detail Report | BI | REQ_F4117.4.2 | 未開始 |
| 4.1.1.7.4 Warehouse | REQ_F4117.4.3 | System should enable user to view Product Inbound & Outbound Summary Report | BI | REQ_F4117.4.3 | 未開始 |
| 4.1.1.7.5 Financial | REQ_F4117.5.1 | System should enable user to view Cash & Bank Report | BI | REQ_F4117.5.1 | 未開始 |
| 4.1.1.7.5 Financial | REQ_F4117.5.2 | System should enable user to view Accounts Payable Details Report | BI | REQ_F4117.5.2 | 未開始 |
| 4.1.1.7.5 Financial | REQ_F4117.5.3 | System should enable user to view Accounts Receivable Details Report | BI | REQ_F4117.5.3 | 未開始 |
| 4.1.1.7.5 Financial | REQ_F4117.5.4 | System should enable user to view Client Statement | BI | REQ_F4117.5.4 | 未開始 |
| 4.1.1.7.5 Financial | REQ_F4117.5.5 | System should enable user to view Supplier Statement | BI | REQ_F4117.5.5 | 未開始 |
| 4.1.1.7.5 Financial | REQ_F4117.5.6 | System should enable user to view Other Income & Expenses Detail Report | BI | REQ_F4117.5.6 | 未開始 |
| 4.1.1.7.5 Financial | REQ_F4117.5.7 | System should enable user to export Other Income & Expenses Detail Report as Excel | BI | REQ_F4117.5.7 | 未開始 |
| 4.1.1.8.1 Client Management | REQ_F4118.1.1 | System should enable user to view records of clients | MD | REQ_F4118.1.1 | 未開始 |
| 4.1.1.8.1 Client Management | REQ_F4118.1.2 | System should enable user to create records of clients | MD | REQ_F4118.1.2 | 未開始 |
| 4.1.1.8.1 Client Management | REQ_F4118.1.3 | System should enable user to edit records of clients | MD | REQ_F4118.1.3 | 未開始 |
| 4.1.1.8.1 Client Management | REQ_F4118.1.4 | System should enable user to approve records of clients to be added | MD | REQ_F4118.1.4 | 未開始 |
| 4.1.1.8.1 Client Management | REQ_F4118.1.5 | System should enable user to reject records of clients to be added | MD | REQ_F4118.1.5 | 未開始 |
| 4.1.1.8.1 Client Management | REQ_F4118.1.6 | System should enable user to delete records of clients | MD | REQ_F4118.1.6 | 未開始 |
| 4.1.1.8.1 Client Management | REQ_F4118.1.7 | System should enable user to import records of clients from excel spreadsheets | MD | REQ_F4118.1.7 | 未開始 |
| 4.1.1.8.2 Supplier Management | REQ_F4118.2.1 | System should enable user to view records of suppliers | MD | REQ_F4118.2.1 | 未開始 |
| 4.1.1.8.2 Supplier Management | REQ_F4118.2.2 | System should enable user to create records of suppliers | MD | REQ_F4118.2.2 | 未開始 |
| 4.1.1.8.2 Supplier Management | REQ_F4118.2.3 | System should enable user to edit records of suppliers | MD | REQ_F4118.2.3 | 未開始 |
| 4.1.1.8.2 Supplier Management | REQ_F4118.2.4 | System should enable user to approve records of suppliers to be added | MD | REQ_F4118.2.4 | 未開始 |
| 4.1.1.8.2 Supplier Management | REQ_F4118.2.5 | System should enable user to reject records of suppliers to be added | MD | REQ_F4118.2.5 | 未開始 |
| 4.1.1.8.2 Supplier Management | REQ_F4118.2.6 | System should enable user to delete records of suppliers | MD | REQ_F4118.2.6 | 未開始 |
| 4.1.1.8.2 Supplier Management | REQ_F4118.2.7 | System should enable user to import records of suppliers from excel spreadsheets | MD | REQ_F4118.2.7 | 未開始 |
| 4.1.1.8.3 Product Management | REQ_F4118.3.1 | System should enable user to view product | MD | REQ_F4118.3.1 | 未開始 |
| 4.1.1.8.3 Product Management | REQ_F4118.3.2 | System should enable user to create product | MD | REQ_F4118.3.2 | 未開始 |
| 4.1.1.8.3 Product Management | REQ_F4118.3.3 | System should enable user to edit product | MD | REQ_F4118.3.3 | 未開始 |
| 4.1.1.8.3 Product Management | REQ_F4118.3.4 | System should enable user to delete product | MD | REQ_F4118.3.4 | 未開始 |
| 4.1.1.8.4 Warehouse Management | REQ_F4118.4.1 | System should enable user to view records of warehouse | MD | REQ_F4118.4.1 | 未開始 |
| 4.1.1.8.4 Warehouse Management | REQ_F4118.4.2 | System should enable user to create records of warehouse | MD | REQ_F4118.4.2 | 未開始 |
| 4.1.1.8.4 Warehouse Management | REQ_F4118.4.3 | System should enable user to edit records of warehouse | MD | REQ_F4118.4.3 | 未開始 |
| 4.1.1.8.4 Warehouse Management | REQ_F4118.4.4 | System should enable user to delete records of warehouse | MD | REQ_F4118.4.4 | 未開始 |
| 4.1.1.8.5 Account Management | REQ_F4118.5.1 | System should enable the user to view accounts. | MD（資金帳戶；表屬 FI-CM） | REQ_F4118.5.1 | 未開始 |
| 4.1.1.8.5 Account Management | REQ_F4118.5.2 | System should enable users to create new accounts. | MD（資金帳戶；表屬 FI-CM） | REQ_F4118.5.2 | 未開始 |
| 4.1.1.8.5 Account Management | REQ_F4118.5.3 | System should enable the user to edit existing accounts. | MD（資金帳戶；表屬 FI-CM） | REQ_F4118.5.3 | 未開始 |
| 4.1.1.8.5 Account Management | REQ_F4118.5.4 | System should enable the user to delete accounts. | MD（資金帳戶；表屬 FI-CM） | REQ_F4118.5.4 | 未開始 |
| 4.1.1.8.6 User Management | REQ_F411.8.1 | System should enable user to view users’ details | SYS | REQ_F4118.23.5 | 完成 |
| 4.1.1.8.6 User Management | REQ_F411.8.2 | System should enable user to create new user | SYS | REQ_F4118.23.6 | 完成 |
| 4.1.1.8.6 User Management | REQ_F411.8.3 | System should enable user to edit users’ details and permissions | SYS | REQ_F4118.25.6 | 完成 |
| 4.1.1.8.6 User Management | REQ_F411.8.4 | System should enable user to delete users | SYS | REQ_F4118.23.7 | 完成 |
| 4.1.1.8.7 Process Management | REQ_F4118.7.1 | System should enable user to view process | MD | REQ_F4118.7.1 | 未開始 |
| 4.1.1.8.7 Process Management | REQ_F4118.7.2 | System should enable user to create process | MD | REQ_F4118.7.2 | 未開始 |
| 4.1.1.8.7 Process Management | REQ_F4118.7.3 | System should enable user to edit process | MD | REQ_F4118.7.3 | 未開始 |
| 4.1.1.8.7 Process Management | REQ_F4118.7.4 | System should enable user to delete process | MD | REQ_F4118.7.4 | 未開始 |
| 4.1.1.8.8 Client Category | REQ_F4118.8.1 | System should enable user to view records of client categories | MD | REQ_F4118.8.1 | 完成 |
| 4.1.1.8.8 Client Category | REQ_F4118.8.2 | System should enable user to create records of client categories | MD | REQ_F4118.8.2 | 完成 |
| 4.1.1.8.8 Client Category | REQ_F4118.8.3 | System should enable user to edit records of client categories | MD | REQ_F4118.8.3 | 完成 |
| 4.1.1.8.8 Client Category | REQ_F4118.8.4 | System should enable user to delete records of client categories | MD | REQ_F4118.8.4 | 完成 |
| 4.1.1.8.9 Supplier Category | REQ_F4118.9.1 | System should enable user to view records of supplier categories | MD | REQ_F4118.9.1 | 未開始 |
| 4.1.1.8.9 Supplier Category | REQ_F4118.9.2 | System should enable user to create records of supplier categories | MD | REQ_F4118.9.2 | 未開始 |
| 4.1.1.8.9 Supplier Category | REQ_F4118.9.3 | System should enable user to edit records of supplier categories | MD | REQ_F4118.9.3 | 未開始 |
| 4.1.1.8.9 Supplier Category | REQ_F4118.9.4 | System should enable user to delete records of supplier categories | MD | REQ_F4118.9.4 | 未開始 |
| 4.1.1.8.10 Item Category | REQ_F4118.10.1 | System should enable user to view item category | MD | REQ_F4118.10.1 | 未開始 |
| 4.1.1.8.10 Item Category | REQ_F4118.10.2 | System should enable user to create item category | MD | REQ_F4118.10.2 | 未開始 |
| 4.1.1.8.10 Item Category | REQ_F4118.10.3 | System should enable user to edit item category | MD | REQ_F4118.10.3 | 未開始 |
| 4.1.1.8.10 Item Category | REQ_F4118.10.4 | System should enable user to delete item category | MD | REQ_F4118.10.4 | 未開始 |
| 4.1.1.8.11 Item Brand | REQ_F4118.11.1 | System should enable user to view item brand | MD | REQ_F4118.11.1 | 未開始 |
| 4.1.1.8.11 Item Brand | REQ_F4118.11.2 | System should enable user to create item brand | MD | REQ_F4118.11.2 | 未開始 |
| 4.1.1.8.11 Item Brand | REQ_F4118.11.3 | System should enable user to edit item brand | MD | REQ_F4118.11.3 | 未開始 |
| 4.1.1.8.11 Item Brand | REQ_F4118.11.4 | System should enable user to delete item brand | MD | REQ_F4118.11.4 | 未開始 |
| 4.1.1.8.12 Income & Expense Category | REQ_F4118.12.1 | System should enable the user to view income & expense categories. | MD | REQ_F4118.12.1 | 未開始 |
| 4.1.1.8.12 Income & Expense Category | REQ_F4118.12.2 | System should enable users to create income & expense categories. | MD | REQ_F4118.12.2 | 未開始 |
| 4.1.1.8.12 Income & Expense Category | REQ_F4118.12.3 | System should enable the user to edit income & expense categories. | MD | REQ_F4118.12.3 | 未開始 |
| 4.1.1.8.12 Income & Expense Category | REQ_F4118.12.4 | System should enable the user to delete income & expense categories | MD | REQ_F4118.12.4 | 未開始 |
| 4.1.1.8.13 Unit of Measurement | REQ_F4118.13.1 | System should enable user to view unit of measurement | MD | REQ_F4118.13.1 | 未開始 |
| 4.1.1.8.13 Unit of Measurement | REQ_F4118.13.2 | System should enable user to create unit of measurement | MD | REQ_F4118.13.2 | 未開始 |
| 4.1.1.8.13 Unit of Measurement | REQ_F4118.13.3 | System should enable user to edit unit of measurement | MD | REQ_F4118.13.3 | 未開始 |
| 4.1.1.8.13 Unit of Measurement | REQ_F4118.13.4 | System should enable user to delete unit of measurement | MD | REQ_F4118.13.4 | 未開始 |
| 4.1.1.8.14 Settlement Method | REQ_F4118.14.1 | System should enable the user to view settlement methods. | MD | REQ_F4118.14.1 | 未開始 |
| 4.1.1.8.14 Settlement Method | REQ_F4118.14.2 | System should enable users to create new settlement methods. | MD | REQ_F4118.14.2 | 未開始 |
| 4.1.1.8.14 Settlement Method | REQ_F4118.14.3 | System should enable the user to edit existing settlement methods. | MD | REQ_F4118.14.3 | 未開始 |
| 4.1.1.8.14 Settlement Method | REQ_F4118.14.4 | System should enable the user to delete existing settlement methods. | MD | REQ_F4118.14.4 | 未開始 |
| 4.1.1.8.15 Process Category | REQ_F4118.15.1 | System should enable user to view process category | MD | REQ_F4118.15.1 | 未開始 |
| 4.1.1.8.15 Process Category | REQ_F4118.15.2 | System should enable user to create process category | MD | REQ_F4118.15.2 | 未開始 |
| 4.1.1.8.15 Process Category | REQ_F4118.15.3 | System should enable user to edit process category | MD | REQ_F4118.15.3 | 未開始 |
| 4.1.1.8.15 Process Category | REQ_F4118.15.4 | System should enable user to delete process category | MD | REQ_F4118.15.4 | 未開始 |
| 4.1.1.8.16 Operation Log | REQ_F411.16.1 | System should enable user to view system operation logs | SYS | REQ_F4118.27.6 | 完成 |
| 4.1.1.8.16 Operation Log | REQ_F411.16.2 | System should enable user to delete system operation logs | SYS | - | WONT_DO |
| 4.1.1.8.17 Parameter Settings | REQ_F411.17.1 | System should enable user to view parameter settings | SYS | REQ_F4118.17.1 | 完成 |
| 4.1.1.8.17 Parameter Settings | REQ_F411.17.2 | System should enable user to edit parameter settings | SYS | REQ_F4118.17.2 | 完成 |
| 4.1.1.8.18 Print Settings | REQ_F411.18.1 | System should enable user to view print settings | SYS | REQ_F4118.18.1 | 完成 |
| 4.1.1.8.18 Print Settings | REQ_F411.18.2 | System should enable user to edit print settings | SYS | REQ_F4118.18.2 | 完成 |
| 4.1.1.8.19 Logo & Stamp | REQ_F411.19.1 | System should enable user to view the logo and stamp | SYS | REQ_F4118.19.1 | 完成 |
| 4.1.1.8.19 Logo & Stamp | REQ_F411.19.2 | System should enable user to edit the logo and stamp | SYS | REQ_F4118.19.2 | 完成 |

## 6. 介面對照表（SRS 4.4 / 4.5、SDS 4.2）

| BlueWhale 編號 | 介面名稱 | MyERP 模組 | MyERP 介面編號 | 狀態 |
|---|---|---|---|---|
| 4.4.1.1.1 | Login Interface | SYS | 4.4.1.8.62 | 完成 |
| 4.4.1.1.2 | Homepage Interface | SYS | 4.4.1.1.2 | 完成 |
| 4.4.1.1.3 | Change Password Interface | SYS | 4.4.1.1.3 | 完成 |
| 4.4.1.2.1 | Sales Order Create Interface | SD | 4.4.1.2.5 | 未開始 |
| 4.4.1.2.2 | Sales Order View Interface | SD | 4.4.1.2.4 | 未開始 |
| 4.4.1.2.3 | Sales Order Edit Interface | SD | 4.4.1.2.6 | 未開始 |
| 4.4.1.2.4 | Sales Outbound Create Interface | SD | 4.4.1.2.8 | 未開始 |
| 4.4.1.2.5 | Sales Outbound View Interface | SD | 4.4.1.2.7 | 未開始 |
| 4.4.1.2.6 | Sales Outbound Edit Interface | SD | 4.4.1.2.9 | 未開始 |
| 4.4.1.2.7 | Select Client Interface | SD | 4.4.1.2.10 | 未開始 |
| 4.4.1.3.1 | BOM Grouping Interface | PP | 4.4.1.3.1 | 未開始 |
| 4.4.1.3.2 | BOM List View Interface | PP | 4.4.1.3.2 | 未開始 |
| 4.4.1.3.3 | BOM List Create Interface | PP | 4.4.1.3.3 | 未開始 |
| 4.4.1.3.4 | BOM List Edit Interface | PP | 4.4.1.3.4 | 未開始 |
| 4.4.1.3.5 | Production Planning View Interface | PP | 4.4.1.3.5 | 未開始 |
| 4.4.1.3.6 | Production Planning Create Interface | PP | 4.4.1.3.6 | 未開始 |
| 4.4.1.3.7 | Production Planning Edit Interface | PP | 4.4.1.3.7 | 未開始 |
| 4.4.1.3.8 | Material Requisition View Interface | PP | 4.4.1.3.8 | 未開始 |
| 4.4.1.3.9 | Material Requisition Create Interface | PP | 4.4.1.3.9 | 未開始 |
| 4.4.1.3.10 | Material Requisition Edit Interface | PP | 4.4.1.3.10 | 未開始 |
| 4.4.1.3.11 | Production Inbound View Interface | PP | 4.4.1.3.11 | 未開始 |
| 4.4.1.3.12 | Production Inbound Create Interface | PP | 4.4.1.3.12 | 未開始 |
| 4.4.1.3.13 | Production Inbound Edit Interface | PP | 4.4.1.3.13 | 未開始 |
| 4.4.1.3.14 | Select Order Interface | PP | 4.4.1.3.14 | 未開始 |
| 4.4.1.3.15 | Select Production Plan Interface | PP | 4.4.1.3.15 | 未開始 |
| 4.4.1.4.1 | Purchase Order View Interface | PUR | 4.4.1.4.1 | 未開始 |
| 4.4.1.4.2 | Purchase Order Create Interface | PUR | 4.4.1.4.2 | 未開始 |
| 4.4.1.4.3 | Purchase Order Edit Interface | PUR | 4.4.1.4.3 | 未開始 |
| 4.4.1.4.4 | Purchase Inbound View Interface | PUR | 4.4.1.4.4 | 未開始 |
| 4.4.1.4.5 | Purchase Inbound Create Interface | PUR | 4.4.1.4.5 | 未開始 |
| 4.4.1.4.6 | Purchase Inbound Edit Interface | PUR | 4.4.1.4.6 | 未開始 |
| 4.4.1.4.7 | Select Supplier Interface | PUR | 4.4.1.4.7 | 未開始 |
| 4.4.1.5.1 | Inventory View Interface | INV | 4.4.1.5.1 | 未開始 |
| 4.4.1.5.2 | Other Inbound Create Interface | INV | 4.4.1.5.3 | 未開始 |
| 4.4.1.5.3 | Other Inbound View Interface | INV | 4.4.1.5.2 | 未開始 |
| 4.4.1.5.4 | Other Inbound Edit Interface | INV | 4.4.1.5.4 | 未開始 |
| 4.4.1.5.5 | Other Outbound Create Interface | INV | 4.4.1.5.6 | 未開始 |
| 4.4.1.5.6 | Other Outbound View Interface | INV | 4.4.1.5.5 | 未開始 |
| 4.4.1.5.7 | Other Outbound Edit Interface | INV | 4.4.1.5.7 | 未開始 |
| 4.4.1.5.8 | Sales Outbound (Approval) Interface | INV | 4.4.1.5.8 | 未開始 |
| 4.4.1.5.9 | Product Assembly Create Interface | INV | 4.4.1.5.10 | 未開始 |
| 4.4.1.5.10 | Product Assembly View Interface | INV | 4.4.1.5.9 | 未開始 |
| 4.4.1.5.11 | Product Assembly Edit Interface | INV | 4.4.1.5.11 | 未開始 |
| 4.4.1.5.12 | Product Disassembly Create Interface | INV | 4.4.1.5.13 | 未開始 |
| 4.4.1.5.13 | Product Disassembly View Interface | INV | 4.4.1.5.12 | 未開始 |
| 4.4.1.5.14 | Product Disassembly Edit Interface | INV | 4.4.1.5.14 | 未開始 |
| 4.4.1.5.15 | Inventory Transfer Create Interface | INV | 4.4.1.5.16 | 未開始 |
| 4.4.1.5.16 | Inventory Transfer View Interface | INV | 4.4.1.5.15 | 未開始 |
| 4.4.1.5.17 | Inventory Transfer Edit Interface | INV | 4.4.1.5.17 | 未開始 |
| 4.4.1.5.18 | Stocktake Create Interface | INV | 4.4.1.5.18 | 未開始 |
| 4.4.1.5.19 | Purchase Inbound (Approval) Interface | INV | 4.4.1.5.19 | 未開始 |
| 4.4.1.5.20 | Select Data Interface | INV | 4.4.1.5.20 | 未開始 |
| 4.4.1.6.1 | Sales Collection View Interface | FI | 4.4.1.6.1 | 未開始 |
| 4.4.1.6.2 | Sales Collection Create Interface | FI | 4.4.1.6.2 | 未開始 |
| 4.4.1.6.3 | Sales Collection Edit Interface | FI | 4.4.1.6.3 | 未開始 |
| 4.4.1.6.4 | Other Collection View Interface | FI | 4.4.1.6.4 | 未開始 |
| 4.4.1.6.5 | Other Collection Create Interface | FI | 4.4.1.6.5 | 未開始 |
| 4.4.1.6.6 | Other Collection Edit Interface | FI | 4.4.1.6.6 | 未開始 |
| 4.4.1.6.7 | Purchase Payment View Interface | FI | 4.4.1.6.7 | 未開始 |
| 4.4.1.6.8 | Purchase Payment Create Interface | FI | 4.4.1.6.8 | 未開始 |
| 4.4.1.6.9 | Purchase Payment Edit Interface | FI | 4.4.1.6.9 | 未開始 |
| 4.4.1.6.10 | Other Payment View Interface | FI | 4.4.1.6.10 | 未開始 |
| 4.4.1.6.11 | Other Payment Create Interface | FI | 4.4.1.6.11 | 未開始 |
| 4.4.1.6.12 | Other Payment Edit Interface | FI | 4.4.1.6.12 | 未開始 |
| 4.4.1.6.13 | Collection Write-off View Interface | FI | 4.4.1.6.13 | 未開始 |
| 4.4.1.6.14 | Collection Write-off Create Interface | FI | 4.4.1.6.14 | 未開始 |
| 4.4.1.6.15 | Payment Write-off View Interface | FI | 4.4.1.6.15 | 未開始 |
| 4.4.1.6.16 | Payment Write-off Create Interface | FI | 4.4.1.6.16 | 未開始 |
| 4.4.1.7.1 | Purchase Order Tracking Report Interface | BI | 4.4.1.7.1 | 未開始 |
| 4.4.1.7.2 | Purchase Detail Report Interface | BI | 4.4.1.7.2 | 未開始 |
| 4.4.1.7.3 | Purchase Summary Report (By Product) Interface | BI | 4.4.1.7.3 | 未開始 |
| 4.4.1.7.4 | Purchase Summary Report (By Supplier) Interface | BI | 4.4.1.7.4 | 未開始 |
| 4.4.1.7.5 | Sales Order Tracking Report Interface | BI | 4.4.1.7.5 | 未開始 |
| 4.4.1.7.6 | Sales Detail Report Interface | BI | 4.4.1.7.6 | 未開始 |
| 4.4.1.7.7 | Sales Summary Report (By Product) Interface | BI | 4.4.1.7.7 | 未開始 |
| 4.4.1.7.8 | Sales Summary Report (By Client) Interface | BI | 4.4.1.7.8 | 未開始 |
| 4.4.1.7.9 | Production Planning Tracking Report Interface | BI | 4.4.1.7.9 | 未開始 |
| 4.4.1.7.10 | Material Requisition Detail Report Interface | BI | 4.4.1.7.10 | 未開始 |
| 4.4.1.7.11 | Material Requisition Summary Report Interface | BI | 4.4.1.7.11 | 未開始 |
| 4.4.1.7.12 | Production Inbound Detail Report Interface | BI | 4.4.1.7.12 | 未開始 |
| 4.4.1.7.13 | Production Inbound Summary Report Interface | BI | 4.4.1.7.13 | 未開始 |
| 4.4.1.7.14 | Product Inventory Balance Report Interface | BI | 4.4.1.7.14 | 未開始 |
| 4.4.1.7.15 | Product Inbound & Outbound Detail Report Interface | BI | 4.4.1.7.15 | 未開始 |
| 4.4.1.7.16 | Product Inbound & Outbound Summary Report Interface | BI | 4.4.1.7.16 | 未開始 |
| 4.4.1.7.17 | Cash & Bank Report Interface | BI | 4.4.1.7.17 | 未開始 |
| 4.4.1.7.18 | Accounts Payable Detail Report Interface | BI | 4.4.1.7.18 | 未開始 |
| 4.4.1.7.19 | Accounts Receivable Detail Report Interface | BI | 4.4.1.7.19 | 未開始 |
| 4.4.1.7.20 | Client Statement Interface | BI | 4.4.1.7.20 | 未開始 |
| 4.4.1.7.21 | Supplier Statement Interface | BI | 4.4.1.7.21 | 未開始 |
| 4.4.1.7.22 | Other Income & Expense Detail Report Interface | BI | 4.4.1.7.22 | 未開始 |
| 4.4.1.8.1 | Client View Interface | MD | 4.4.1.8.1 | 未開始 |
| 4.4.1.8.2 | Client Create Interface | MD | 4.4.1.8.2 | 未開始 |
| 4.4.1.8.3 | Client Edit Interface | MD | 4.4.1.8.3 | 未開始 |
| 4.4.1.8.4 | Client Contact Management Interface | MD | 4.4.1.8.4 | 未開始 |
| 4.4.1.8.5 | Client Import Interface | MD | 4.4.1.8.5 | 未開始 |
| 4.4.1.8.6 | Supplier View Interface | MD | 4.4.1.8.6 | 未開始 |
| 4.4.1.8.7 | Supplier Create Interface | MD | 4.4.1.8.7 | 未開始 |
| 4.4.1.8.8 | Supplier Edit Interface | MD | 4.4.1.8.8 | 未開始 |
| 4.4.1.8.9 | Supplier Contact Management Interface | MD | 4.4.1.8.9 | 未開始 |
| 4.4.1.8.10 | Supplier Import Interface | MD | 4.4.1.8.10 | 未開始 |
| 4.4.1.8.11 | Product View Interface | MD | 4.4.1.8.11 | 未開始 |
| 4.4.1.8.12 | Product Create Interface | MD | 4.4.1.8.12 | 未開始 |
| 4.4.1.8.13 | Product Edit Interface | MD | 4.4.1.8.13 | 未開始 |
| 4.4.1.8.14 | Product Initial Stock Interface | MD | 4.4.1.8.14 | 未開始 |
| 4.4.1.8.15 | Product Image Interface | MD | 4.4.1.8.15 | 未開始 |
| 4.4.1.8.16 | Product Details Interface | MD | 4.4.1.8.16 | 未開始 |
| 4.4.1.8.17 | Warehouse View Interface | MD | 4.4.1.8.17 | 未開始 |
| 4.4.1.8.18 | Warehouse Create Interface | MD | 4.4.1.8.18 | 未開始 |
| 4.4.1.8.19 | Warehouse Edit Interface | MD | 4.4.1.8.19 | 未開始 |
| 4.4.1.8.20 | Account View Interface | MD（資金帳戶；表屬 FI-CM） | 4.4.1.8.20 | 未開始 |
| 4.4.1.8.21 | Account Create Interface | MD（資金帳戶；表屬 FI-CM） | 4.4.1.8.21 | 未開始 |
| 4.4.1.8.22 | Account Edit Interface | MD（資金帳戶；表屬 FI-CM） | 4.4.1.8.22 | 未開始 |
| 4.4.1.8.23 | User View Interface | SYS | 4.4.1.8.63 | 完成 |
| 4.4.1.8.24 | User Create Interface | SYS | 4.4.1.8.61 | 完成 |
| 4.4.1.8.25 | User Edit Interface | SYS | 4.4.1.8.63 | 完成 |
| 4.4.1.8.26 | User Permission Edit Interface | SYS | 4.4.1.8.65 | 完成 |
| 4.4.1.8.27 | Process View Interface | MD | 4.4.1.8.27 | 未開始 |
| 4.4.1.8.28 | Process Create Interface | MD | 4.4.1.8.28 | 未開始 |
| 4.4.1.8.29 | Process Edit Interface | MD | 4.4.1.8.29 | 未開始 |
| 4.4.1.8.30 | Client Category View Interface | MD | 4.4.1.8.30 | 完成 |
| 4.4.1.8.31 | Client Category Create Interface | MD | 4.4.1.8.31 | 完成 |
| 4.4.1.8.32 | Client Category Edit Interface | MD | 4.4.1.8.32 | 完成 |
| 4.4.1.8.33 | Supplier Category View Interface | MD | 4.4.1.8.33 | 未開始 |
| 4.4.1.8.34 | Supplier Category Create Interface | MD | 4.4.1.8.34 | 未開始 |
| 4.4.1.8.35 | Supplier Category Edit Interface | MD | 4.4.1.8.35 | 未開始 |
| 4.4.1.8.36 | Item Category View Interface | MD | 4.4.1.8.36 | 未開始 |
| 4.4.1.8.37 | Item Category Create Interface | MD | 4.4.1.8.37 | 未開始 |
| 4.4.1.8.38 | Item Category Edit Interface | MD | 4.4.1.8.38 | 未開始 |
| 4.4.1.8.39 | Item Brand View Interface | MD | 4.4.1.8.39 | 未開始 |
| 4.4.1.8.40 | Item Brand Create Interface | MD | 4.4.1.8.40 | 未開始 |
| 4.4.1.8.41 | Item Brand Edit Interface | MD | 4.4.1.8.41 | 未開始 |
| 4.4.1.8.42 | Income & Expense Category View Interface | MD | 4.4.1.8.42 | 未開始 |
| 4.4.1.8.43 | Income & Expense Category Create Interface | MD | 4.4.1.8.43 | 未開始 |
| 4.4.1.8.44 | Income & Expense Category Edit Interface | MD | 4.4.1.8.44 | 未開始 |
| 4.4.1.8.45 | Unit of Measurement View Interface | MD | 4.4.1.8.45 | 未開始 |
| 4.4.1.8.46 | Unit of Measurement Create Interface | MD | 4.4.1.8.46 | 未開始 |
| 4.4.1.8.47 | Unit of Measurement Edit Interface | MD | 4.4.1.8.47 | 未開始 |
| 4.4.1.8.48 | Settlement Method View Interface | MD | 4.4.1.8.48 | 未開始 |
| 4.4.1.8.49 | Settlement Method Create Interface | MD | 4.4.1.8.49 | 未開始 |
| 4.4.1.8.50 | Settlement Method Edit Interface | MD | 4.4.1.8.50 | 未開始 |
| 4.4.1.8.51 | Process Category View Interface | MD | 4.4.1.8.51 | 未開始 |
| 4.4.1.8.52 | Process Category Create Interface | MD | 4.4.1.8.52 | 未開始 |
| 4.4.1.8.53 | Process Category Edit Interface | MD | 4.4.1.8.53 | 未開始 |
| 4.4.1.8.54 | Operation Log View Interface | SYS | 4.4.1.8.66 | 完成 |
| 4.4.1.8.55 | Parameter Edit Interface | SYS | 4.4.1.8.55 | 完成 |
| 4.4.1.8.56 | Print Settings Edit Interface | SYS | 4.4.1.8.56 | 完成 |
| 4.4.1.8.57 | Logo & Stamps Edit Interface | SYS | 4.4.1.8.57 | 完成 |
| 4.4.1.9.1 | Confirmation Box Interface | 前端共用 | 4.4.1.9.1 | 未開始 |
| 4.4.1.9.2 | Alert Box Interface | 前端共用 | 4.4.1.9.2 | 未開始 |
| 4.4.1.9.3 | Select Item Interface | 前端共用 | 4.4.1.9.3 | 未開始 |

## 7. 資料表對照表（SRS 4.3）

| BlueWhale ID | 表名 | 說明 | MyERP 表名 | MyERP REQ_DS43 | 狀態 |
|---|---|---|---|---|---|
| REQ_DS43.1 | Account | This table stores the information of user accounts. | cm_bank_account | 待分配 | 未開始 |
| REQ_DS43.2 | Area | This table stores the information of areas in cities. | md_region | 待分配 | 未開始 |
| REQ_DS43.3 | Brand | This table stores the information of item brands. | md_item_brand | 待分配 | 未開始 |
| REQ_DS43.4 | Inventory | This table stores the information of inventories. | md_warehouse | 待分配 | 未開始 |
| REQ_DS43.5 | Client | This table stores the information of clients. | md_customer | 待分配 | 未開始 |
| REQ_DS43.6 | ClientAddress | This table stores the information of client addresses. | md_customer_address | 待分配 | 未開始 |
| REQ_DS43.7 | ClientContact | This table stores information about client contacts. | md_customer_contact | 待分配 | 未開始 |
| REQ_DS43.8 | ClientType | This table stores the information about client types. | md_customer_category | REQ_DS43.105 | 完成 |
| REQ_DS43.9 | CartList | This table stores the information of carts. | — | — | WONT_DO |
| REQ_DS43.10 | CheckBill | This table stores the information of check bills. | ar_write_off / ap_write_off | 待分配 | 未開始 |
| REQ_DS43.11 | CheckBillItemPayment | This table stores information about item payment of a check bill. | ap_write_off_payment | 待分配 | 未開始 |
| REQ_DS43.12 | CheckBillItemPur | This table stores the information of item purchase of a check bill. | ap_write_off_bill | 待分配 | 未開始 |
| REQ_DS43.13 | CheckBillItemSales | This table stores information about item sales of a check bill. | ar_write_off_bill | 待分配 | 未開始 |
| REQ_DS43.14 | City | This table stores the information of cities. | md_region | 待分配 | 未開始 |
| REQ_DS43.15 | CostChange | This table stores information about cost changes. | inv_cost_adjustment | 待分配 | 未開始 |
| REQ_DS43.16 | ClientTypeDis | This table stores information about discounts for different client types. | sal_customer_category_discount | 待分配 | 未開始 |
| REQ_DS43.17 | CostChangeItem | This table stores detailed information about cost change of items. | inv_cost_adjustment_line | 待分配 | 未開始 |
| REQ_DS43.18 | CheckBillItemReceivable | This table stores information about items receivable on check bills. | ar_write_off_receipt | 待分配 | 未開始 |
| REQ_DS43.19 | InventoryTransfer | This table stores information about inventory transfers. | inv_transfer | 待分配 | 未開始 |
| REQ_DS43.20 | Company | This table stores the information of companies. | sys_company | REQ_DS43.20 | 完成 |
| REQ_DS43.21 | DataBacklist | This table stores information about data backlists. | — | — | WONT_DO |
| REQ_DS43.22 | Dept | This table stores information about departments. | sys_department | REQ_DS43.22 | 完成 |
| REQ_DS43.23 | Logs | This table stores information about system logs. | sys_audit_log | REQ_DS43.94 | 完成 |
| REQ_DS43.24 | InventoryTransferItem | This table stores information about items transferred by inventories. | inv_transfer_line | 待分配 | 未開始 |
| REQ_DS43.25 | GoodsAssembly | This table stores information about goods assemblies. | inv_assembly | 待分配 | 未開始 |
| REQ_DS43.26 | GoodsAssemblyDetails | This table stores detailed information about goods assemblies. | inv_assembly_line | 待分配 | 未開始 |
| REQ_DS43.27 | Months | This table stores information about months. | — | — | WONT_DO |
| REQ_DS43.28 | NewsList | This table stores information about news. | — | — | WONT_DO |
| REQ_DS43.29 | AttendanceList | This table stores information about attendance. | hr_attendance | 待分配 | 未開始 |
| REQ_DS43.30 | GoodsInitialStock | This table stores information about initial stock of goods. | inv_opening_balance | 待分配 | 未開始 |
| REQ_DS43.31 | GoodsPriceClientType | This table stores information about goods prices for client type. | sal_customer_category_price | 待分配 | 未開始 |
| REQ_DS43.32 | GoodsAttribute | This table stores information about attributes of goods. | md_item_attribute | 待分配 | 未開始 |
| REQ_DS43.33 | GoodsBomListItem | This table stores information about BOM list details. | pp_bom_line | 待分配 | 未開始 |
| REQ_DS43.34 | Goods | This table stores information about goods. | md_item | 待分配 | 未開始 |
| REQ_DS43.35 | NoticeInfo | This table stores information about notices. | sys_notification | REQ_DS43.104 | 完成 |
| REQ_DS43.36 | OtherGet | This table stores information on money collection bills of other types. | cm_other_receipt | 待分配 | 未開始 |
| REQ_DS43.37 | PageList | This table stores information about page lists. | sys_role_permission | REQ_DS43.91 | 完成 |
| REQ_DS43.38 | ProduceGetList | This table stores information about material requisitions. | pp_material_issue | 待分配 | 未開始 |
| REQ_DS43.39 | GoodsTypePicList | This table stores information about goods types images. | md_item_category_image | 待分配 | 未開始 |
| REQ_DS43.40 | ProcessType | This table stores information about the process types. | md_process_category | 待分配 | 未開始 |
| REQ_DS43.41 | OtherGetItem | This table stores detailed information of money collection bills of other types. | cm_other_receipt_line | 待分配 | 未開始 |
| REQ_DS43.42 | Stocktake | This table stores information about stocktake. | inv_stocktake | 待分配 | 未開始 |
| REQ_DS43.43 | ProduceGetListItem | This table stores information of produce material requisition details. | pp_material_issue_line | 待分配 | 未開始 |
| REQ_DS43.44 | GoodsType | This table stores information about goods types. | md_item_category | 待分配 | 未開始 |
| REQ_DS43.45 | OtherIn | This table stores information about other inbounds. | inv_other_receipt | 待分配 | 未開始 |
| REQ_DS43.46 | PayGet | This table stores information about payment received. | md_income_expense_category | 待分配 | 未開始 |
| REQ_DS43.47 | ProduceIn | This table stores information about produces inbounds. | pp_production_receipt | 待分配 | 未開始 |
| REQ_DS43.48 | OtherInItem | This table stores information about other inbound details. | inv_other_receipt_line | 待分配 | 未開始 |
| REQ_DS43.49 | Payment | This table stores information about payments. | ap_payment | 待分配 | 未開始 |
| REQ_DS43.50 | ProduceInItem | This table stores detailed information about produce inbounds. | pp_production_receipt_line | 待分配 | 未開始 |
| REQ_DS43.51 | GoodsBomListType | This table stores information about goods BOM list types. | pp_bom_group | 待分配 | 未開始 |
| REQ_DS43.52 | GoodsBrand | This table stores information about goods brands. | md_item_brand | 待分配 | 未開始 |
| REQ_DS43.53 | OtherOutItem | This table stores information about other outbound details. | inv_other_issue_line | 待分配 | 未開始 |
| REQ_DS43.54 | PaymentSourceBillItem | This table stores information about details of payment source bills. | ap_payment_source | 待分配 | 未開始 |
| REQ_DS43.55 | PayType | This table stores information about payment types. | md_settlement_method | 待分配 | 未開始 |
| REQ_DS43.56 | GoodsDisassemblyDetails | This table stores detailed information about goods disassembly. | inv_disassembly_line | 待分配 | 未開始 |
| REQ_DS43.57 | OtherPay | This table stores information about other types of payments. | cm_other_payment | 待分配 | 未開始 |
| REQ_DS43.58 | PayType | This table stores information about product processes. | pp_item_process | 待分配 | 未開始 |
| REQ_DS43.59 | GoodsDisassembly | This table stores information about goods disassembly. | inv_disassembly | 待分配 | 未開始 |
| REQ_DS43.60 | GoodsImages | This table stores information about goods images. | md_item_image | 待分配 | 未開始 |
| REQ_DS43.61 | OtherPayItem | This table stores information about details of other type of payments. | cm_other_payment_line | 待分配 | 未開始 |
| REQ_DS43.62 | ProcessList | This table stores information about processes. | md_process | 待分配 | 未開始 |
| REQ_DS43.63 | PurOrder | This table stores information about purchase orders. | pur_order | 待分配 | 未開始 |
| REQ_DS43.64 | PurOrder | This table stores information about purchase orders. | pur_order_line | 待分配 | 未開始 |
| REQ_DS43.65 | SalesReceipt | This table stores information about sales receipts. | sal_delivery | 待分配 | 未開始 |
| REQ_DS43.66 | UsersPageList | This table stores information about users' pages. | sys_user_role | REQ_DS43.93 | 完成 |
| REQ_DS43.67 | PurReceipt | This table stores information about purchase receipts. | pur_receipt | 待分配 | 未開始 |
| REQ_DS43.68 | SalesReceiptItem | This table stores information about details of sales receipts. | sal_delivery_line | 待分配 | 未開始 |
| REQ_DS43.69 | VendorType | This table stores information about vendor types. | md_supplier_category | 待分配 | 未開始 |
| REQ_DS43.70 | PurReceiptItem | This table stores information about purchase receipt details. | pur_receipt_line | 待分配 | 未開始 |
| REQ_DS43.71 | Shop | This table stores information about shops. | sys_branch | REQ_DS43.89 | 完成 |
| REQ_DS43.72 | Vendor | This table stores information about vendors. | md_supplier | 待分配 | 未開始 |
| REQ_DS43.73 | SystemSet | This table stores information about system settings. | sys_parameter | REQ_F4118.17.1–3 | 完成 |
| REQ_DS43.74 | Roles | This table stores information about roles. | sys_role | REQ_DS43.90 | 完成 |
| REQ_DS43.75 | SalesOrderItem | This table stores detailed information about sales orders. | sal_order_line | 待分配 | 未開始 |
| REQ_DS43.76 | SalesOrder | This table stores information about sales orders. | sal_order | 待分配 | 未開始 |
| REQ_DS43.77 | VendorLinkman | This table stores information about vendors’ linkmen. | md_supplier_contact | 待分配 | 未開始 |
| REQ_DS43.78 | ProduceListItem | This table stores information about details of products. | pp_production_plan_operation | 待分配 | 未開始 |
| REQ_DS43.79 | ProduceListItemBom | This table stores information about produces list BOM details. | pp_production_plan_material | 待分配 | 未開始 |
| REQ_DS43.80 | ReceivableSourceBillItem | This table stores information about receivable source details. | ar_receipt_source | 待分配 | 未開始 |
| REQ_DS43.81 | PageList | This table stores information about pages. | sys_role_permission | REQ_DS43.91 | 完成 |
| REQ_DS43.82 | Users | This table stores information about users. | sys_user | REQ_DS43.82 | 完成 |
| REQ_DS43.83 | ReceivableAccountItem | This table stores information about receivable account details. | ar_receipt_line | 待分配 | 未開始 |
| REQ_DS43.84 | SalesQuote | This table stores information about sales quotes. | sal_quotation | 待分配 | 未開始 |
| REQ_DS43.85 | Unit | This table stores information about units. | md_uom | 待分配 | 未開始 |
| REQ_DS43.86 | Logistics | This table stores information about logistics. | md_carrier | 待分配 | 未開始 |
| REQ_DS43.87 | Receivable | This table stores information about receivables. | ar_receipt | 待分配 | 未開始 |
| REQ_DS43.88 | SalesQuoteItem | This table stores information about sales quote details. | sal_quotation_line | 待分配 | 未開始 |

## 8. 舊系統原始碼（docs/reference/legacy-source，不提交）

- `00-LEGACY-SCHEMA.md`：136 張表、141 個檢視、10 個 SP 的欄位摘要。
- 業務邏輯主要在檢視與 DAL：庫存（`viewGoodsStoreNum`、`viewGoodsOutInFlow*`、`viewGoodsBalance`）、成本調整（`costChange*`）、核銷（`CheckBill*`）、往來帳（`viewClientNeedPayFlow`、`viewAccountFlow`）、單號（`makeBillNumber`）、價格（`procGetGoodsPrice*`）。
- 只可參考業務規則並以自己的文字寫入 `business-rules.md`；不得複製代碼、SQL 或表結構。
