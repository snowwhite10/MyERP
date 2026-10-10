# ADR-002 需求 ID 與介面編號分配（BlueWhale 基線對照）

- 狀態：Accepted
- 日期：2026-10-10
- 工作單元：WU-P0-002
- 備註：ADR-001 保留給技術版本固定（MASTER_PROMPT 第 4 節，WU-P0-004）。

## 背景

MASTER_PROMPT 13、15 節要求 MyERP 涵蓋 BlueWhale SRS 4.1.1 與 4.4 的全部功能與介面，並在 `reference-notes.md` 維護「BlueWhale → MyERP 需求 ID → 狀態」對照表。需求 ID 含模組與子功能編號，一經分配永不改變，所以必須在任何模組開始前定好分配規則。

BlueWhale 原文有三個問題：部分 ID 漏了模組編號；SRS 4.4 / 4.5 與 SDS 4.2 的介面次序不同；部分描述與表名有誤（詳見 `reference-notes.md` 第 4 節）。MASTER_PROMPT 15.1 又規定 Sales 的子功能 1 為 Sales Quotation（BlueWhale 沒有報價功能）。

## 決定

### 1. 功能需求 REQ_F

1. 模組 1–9 的子功能編號與序號**沿用 BlueWhale**，只把 ID 正規化為 `REQ_F411{模組}.{子功能}.{序號}`：
   - `REQ_F411.1.x`（User）→ `REQ_F4111.1.x`
   - `REQ_F411.8.x`（Settings → User Management，章節 4.1.1.8.6）→ `REQ_F4118.6.x`
   - `REQ_F411.{16–19}.x`（Operation Log、Parameter、Print Settings、Logo & Stamp）→ `REQ_F4118.{16–19}.x`
2. **例外：Sales（模組 2）**。依 MASTER_PROMPT 15.1 例子，子功能 1 = Sales Quotation，2 = Sales Order，3 = Sales Outbound。所以 BlueWhale `REQ_F4112.1.x` → MyERP `REQ_F4112.2.x`，`REQ_F4112.2.x` → `REQ_F4112.3.x`（與 MASTER_PROMPT「`REQ_F4112.2.4` = Sales Order Approve」一致）。
3. 單據類子功能的動作序號基線為：1 view、2 create、3 edit、4 approve、5 reject、6 delete（BlueWhale 已是此次序）。MyERP 新增的動作（print、export、void、import 等）與新子功能，一律在既有最大序號之後**按建立次序追加**；不插隊、不重排。
4. 已預留但尚未建立的子功能（例如 Sales Quotation）在該模組 `requirements.md` 建立時才正式寫入，但編號已在本 ADR 保留，不得另作他用。

### 2. 介面（SRS 4.4 / 4.5、SDS 4.2、REQ_IO_A）

1. 沿用 BlueWhale SRS 4.4 的編號與名稱（名稱以 4.4 為準，`Logo & Stamps` 統一為 `Logo & Stamp`）。
2. 同一功能的介面一律 **View → Create → Edit** 次序。BlueWhale 只有 Sales 與 Inventory 模組不符，因此只重排這兩個模組：
   - Sales：4.4.1.2.1–2.3 保留給 Sales Quotation View / Create / Edit；Sales Order 2.4–2.6；Sales Outbound 2.7–2.9；Select Client 2.10。
   - Inventory：Other Inbound、Other Outbound、Product Assembly、Product Disassembly、Inventory Transfer 各組內改為 View → Create → Edit，組的次序與編號範圍不變（5.1–5.20）。
3. SRS 4.4、4.5 與 SDS 4.2 使用同一份清單、同一次序（docgen 一致性檢查會驗證）。
4. 新增介面在各模組最後按建立次序追加。

### 3. 資料表 REQ_DS43

`REQ_DS43` 是全系統連續編號，在資料表**實際建立**時按 `state.json.idCounters.REQ_DS43` 分配；對照表只記錄預計 MyERP 表名（MASTER_PROMPT 6.1 前綴 + snake_case）。多張 BlueWhale 表可合併為一張 MyERP 表；不需要的表標 WONT_DO 並寫明理由：

| BlueWhale 表 | 理由 |
|---|---|
| CartList | 微信商城購物車，不在 MyERP 範圍 |
| DataBacklist | 資料庫備份記錄，屬 DBA 維運（SQL Server Agent），不在應用層實作 |
| Months | 報表用的月份輔助表，改用 SQL 日期函數 / 日曆 CTE |

### 4. 模組歸屬

- Settings（模組 8）中，Client / Supplier / Product / Warehouse / Process / 各類分類 / UoM / Settlement Method / Income & Expense Category 歸 **MD**；Account Management（資金帳戶）介面歸 MD，資料表屬 FI 的 `cm_` 前綴；User Management、Operation Log、Parameter、Print Settings、Logo & Stamp 歸 **SYS**。
- `Sales Outbound (Approval)` 與 `Purchase Inbound (Approval)` 介面保留在 Inventory（模組 5），代表倉庫審核出入庫；單據本身屬 SD / PUR。

### 5. SRS / SDS 版面

以 BlueWhale 實測樣式為準（`reference-notes.md` 第 2.3、3 節），但：兩份文件統一 A4；頁尾加「Page X of Y」；圖說統一為「Figure {章節}.{序號} {標題}」置中斜體放圖下；輸出不得含追蹤修訂與空白標題段落。

## 後果

- 正面：MyERP 與 BlueWhale ID 幾乎一一對應，Parity 分頁容易核對；規則簡單，無需另外查表。
- 負面：Sales 的 ID 與 BlueWhale 相差一個子功能編號，核對時要看對照表；介面編號在 Sales / Inventory 與 BlueWhale 不同。
- 對照表（`reference-notes.md` 第 5–7 節）已依本 ADR 填好；之後模組建立 `requirements.md` / `ui-spec.md` 時必須使用表中的 ID。
