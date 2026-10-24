# PUR 介面規格

## WU-PUR-008d 三單比對介面預案（尚無畫面）

在既有 4.18 後追加 4.19～4.21，REQ_IO_A 欄位 ID 在 008c 實際畫面時分配。三語亮暗、每頁正常／空／無結果／無權／驗證／500／成功／差異狀態皆需截圖及畫面／資料雙斷言。

| Interface No. | Interface | Route | Related REQ_F | Access |
|---|---|---|---|---|
| 4.19 | Purchase Invoice View Interface | `/pur/purchase-invoices` | REQ_F4114.8.1, REQ_F4114.8.3, REQ_F4114.8.4, REQ_F4114.8.5, REQ_F4114.8.6 | `PUR.INVOICE.VIEW` |
| 4.20 | Purchase Invoice Create Interface | `/pur/purchase-invoices/new` | REQ_F4114.8.2 | `PUR.INVOICE.CREATE` |
| 4.21 | Purchase Invoice Edit Interface | `/pur/purchase-invoices/:id/edit` | REQ_F4114.8.2, REQ_F4114.8.3, REQ_F4114.8.4 | `PUR.INVOICE.EDIT`；差異另需 `RESOLVE` |

列表及詳情分開顯示訂單量、Posted 收貨、退貨／待審退貨、其他匹配預留、已開票與本單可開票量；每行顯示訂單基準、發票輸入、價差／稅差及原因。價稅差異接受按鈕只對 `RESOLVE` 可用，需填理由；數量超額僅提示修訂，不能覆核。POST 按鈕說明「已匹配、FI 可用且仍有可開票量」等停用原因。成功只在服務回保存狀態後顯示，零變更、拒絕、409、422、500／traceId、逾時顯示具體三語訊息；換公司或請求失敗清舊詳情。沒有檔案上傳；VIEW CSV 如實作沿用完整讀取與公式防護。

## WU-PUR-007d 收貨／退貨介面預案（尚無畫面）

沿用 BlueWhale Purchase Inbound View／Create／Edit Interface 4.4～4.6；MyERP 退貨介面按既有 4.15 之後追加 4.16～4.18，不改原編號。正式 REQ_IO_A 欄位 ID 在前端畫面實作時分配；007d 只固定介面順序、權限與交互。INV 的 Purchase Inbound (Approval) 5.19 由 PUR 單據與 SYS 待審承載，不另設 INV 寫入畫面。

| Interface No. | Interface | Route | Related REQ_F | Access |
|---|---|---|---|---|
| 4.4 | Purchase Inbound View Interface | `/pur/purchase-receipts` | REQ_F4114.2.1, REQ_F4114.2.4, REQ_F4114.2.5, REQ_F4114.2.6 | `PUR.RECEIPT.VIEW` |
| 4.5 | Purchase Inbound Create Interface | `/pur/purchase-receipts/new` | REQ_F4114.2.2 | `PUR.RECEIPT.CREATE` |
| 4.6 | Purchase Inbound Edit Interface | `/pur/purchase-receipts/:id/edit` | REQ_F4114.2.3 | `PUR.RECEIPT.EDIT` |
| 4.16 | Purchase Return View Interface | `/pur/purchase-returns` | REQ_F4114.7.1, REQ_F4114.7.3 | `PUR.RETURN.VIEW` |
| 4.17 | Purchase Return Create Interface | `/pur/purchase-returns/new` | REQ_F4114.7.2 | `PUR.RETURN.CREATE` |
| 4.18 | Purchase Return Edit Interface | `/pur/purchase-returns/:id/edit` | REQ_F4114.7.2 | `PUR.RETURN.EDIT` |

列表顯示狀態、已保存行數、來源量／已收或已退／待審預留／可用量，空列表、搜尋無結果與無權分開說明。表單以授權選擇器選來源行，逐行顯示物料、倉儲、批序號、六位量及原成本來源；不可編輯來源與成本。按鈕停用須說明狀態／權限／無餘量；送審確認框說明只有最終核准才改庫存。成功提示只用已保存回應與重讀後的庫存／餘量；零變更、409、422、500／traceId、逾時和斷線皆有具體三語提示。換公司、篩選或錯誤時清除舊詳情與來源選項。每頁正常、空、驗證、伺服器錯誤、成功、整單拒絕的亮暗截圖與畫面／資料雙斷言由前端子單元完成。無檔案上傳；CSV 沿完整讀取、BOM、10000 行和公式防護規則。

## WU-PUR-006d 採購單介面設計（尚無畫面）

沿用 BlueWhale 4.1～4.3 介面名稱與順序。三語亮暗、正常／空／無搜尋結果／無權／逐欄錯誤／伺服器錯誤／成功及警告狀態由 006 前端單元實作並截圖審查。VIEW、CREATE、EDIT 路由各用本動作權限；沒有 VIEW 的 CREATE／EDIT 用戶仍可經專用選項與編輯詳情操作。所有停用按鈕標示狀態或權限原因；提交成功訊息須以保存後單號、行數與請購實際餘量核對，全部失敗不宣稱成功。列印先重新 GET 保存詳情；CSV 沿用完整讀取、10000 明細行上限與公式防護模式。

| Interface No. | Interface | Route | Related REQ_F | Access |
|---|---|---|---|---|
| 4.1 | Purchase Order View Interface | `/pur/purchase-orders` | REQ_F4114.1.1, REQ_F4114.1.4, REQ_F4114.1.5, REQ_F4114.1.6 | `PUR.ORDER.VIEW` |
| 4.2 | Purchase Order Create Interface | `/pur/purchase-orders/new` | REQ_F4114.1.2 | `PUR.ORDER.CREATE` |
| 4.3 | Purchase Order Edit Interface | `/pur/purchase-orders/:id/edit` | REQ_F4114.1.3 | `PUR.ORDER.EDIT` |

| Requirement ID | Item | Description | Purpose | Format | Valid Range | Related I/O | Author | Related REQ_F |
|---|---|---|---|---|---|---|---|---|
| REQ_IO_A4.1.1 | Order Filters (Input) | Filter saved orders by number, date, supplier, state and requisition source. | Find authorized orders. | Text / date / ID / enum | Current company | None | Developer | REQ_F4114.1.1 |
| REQ_IO_A4.1.2 | Order List (Table) | Show saved number, supplier, state, currency and amount with server paging. | Open a saved order. | Paged table | VIEW scope | REQ_IO_A4.1.1 | Developer | REQ_F4114.1.1 |
| REQ_IO_A4.1.3 | Saved Order Detail (Panel) | Show immutable award source, requisition quantities, lines, version and approval history. | Explain order and quantity changes. | Panel | VIEW scope | REQ_IO_A4.1.2 | Developer | REQ_F4114.1.1, REQ_F4114.1.4, REQ_F4114.1.5 |
| REQ_IO_A4.1.4 | Submit / Delete / Cancel (Button) | Show only allowed action and its disabled reason; confirm cancellation and quantity release. | Control order lifecycle. | Button | SUBMIT / DELETE / CANCEL | REQ_IO_A4.1.3 | Developer | REQ_F4114.1.4, REQ_F4114.1.6 |
| REQ_IO_A4.1.5 | Export / Print (Button) | Export complete safe saved lines or refetch one order before printing. | Share current saved orders. | UTF-8 CSV / print | VIEW + EXPORT for CSV; VIEW for print | REQ_IO_A4.1.1 | Developer | REQ_F4114.1.1 |
| REQ_IO_A4.2.1 | Order Header (Form) | Select supplier, business date and currency from authorized current-company choices. | Define purchase terms. | Reference / date | Active supplier and currency | None | Developer | REQ_F4114.1.2 |
| REQ_IO_A4.2.2 | Order Lines (Table) | Select active items and optional approved requisition lines; show remaining quantity, price, input tax and line amount. | Build a draft without reserving demand. | 1–1000 rows / decimal | Positive six-place quantity, nonnegative six-place price | REQ_IO_A4.2.1 | Developer | REQ_F4114.1.2 |
| REQ_IO_A4.2.3 | Save Draft (Button) | Save the whole draft and show actual order number and line count. | Confirm persisted creation. | Button | CREATE | REQ_IO_A4.2.2 | Developer | REQ_F4114.1.2 |
| REQ_IO_A4.3.1 | Order Revision (Form) | Edit eligible manual lines with version; show award lines as immutable. | Prevent changing awarded evidence. | Form / rows | Draft or revised rejection | REQ_IO_A4.1.3 | Developer | REQ_F4114.1.3 |
| REQ_IO_A4.3.2 | Save Revision (Button) | Show stale version, inactive reference and saved result distinctly. | Prevent lost updates. | Button | EDIT | REQ_IO_A4.3.1 | Developer | REQ_F4114.1.3 |

## WU-PUR-005a 詢價／報價／比價介面預案（005f 才交付畫面）

介面按 BlueWhale 4.1～4.7 及既有請購 4.8～4.10 後追加，不改舊編號。預計所有畫面三語、亮暗；列表空、無搜尋結果、無權、驗證錯誤、伺服器錯誤、成功與無變更均有獨立提示。此切片只固定規格，截圖與 E2E 留 005f。

WU-PUR-005f1 先交付 4.11 的唯讀部分：服務端查單號／日期／狀態／請購來源及分頁，篩選與頁碼保存到 URL query 供分享與返回；點單號顯示已保存明細和受邀快照。換公司、重新查詢、換頁或請求失敗時關閉舊詳情。005f2b 在詳情補發送／關閉、確認和三語結果；報價與轉單屬 005f3。

WU-PUR-005f3a 交付 4.14 唯讀切片：以 `PUR.QUOTATION.VIEW` 路由守衛直接開啟詢價報價列表，按供應商 ID、狀態、指定有效日篩選並保存 URL query／頁碼；詳情逐版列出不可變修訂、原幣六位單價與稅率 ID。撤回、已決標及過期仍可查歷史；換公司、換篩選、換頁或錯誤清舊詳情。寫入按鈕由 005f3b 加入。

WU-PUR-005f3b 在 4.14 同頁加入首版／修訂／撤回：只有 `PUR.QUOTATION.QUOTE`、已發送詢價及目前有效報價才顯示對應動作。表單讀取保存行與受邀供應商；上下文讀取失敗時明示原因且不顯示寫入入口。005f3e 已改為 QUOTE 專屬讀取。首版表單由已保存詢價行及受邀供應商產生，修訂從目前不可變版本複製；供應商、日期、逐行原幣六位單價、幣別及進項稅率 ID 逐欄驗證。撤回以危險確認框說明歷史保留。成功後重新查詢列表與全部修訂；409 須確認丟棄未存草稿後才重載，422／404 清舊詳情，500 顯示 traceId，切換公司清舊表單。表單變動及離頁時保護未存內容；此介面無批次部分成功或檔案上傳。

WU-PUR-005f3e 修訂 4.14 上段權限缺口：表單上下文改由 `PUR.QUOTATION.QUOTE` 直接取得，無需 `PUR.RFQ.VIEW`；幣別與進項稅率改為目前公司選單，報價日變更時重讀有效進項稅率。選項載入失敗顯示錯誤且不把舊公司選項留在畫面。現有 `REQ_IO` 編號保持不變。

WU-PUR-005f3c1 交付 4.15 唯讀切片：以 `PUR.RFQ.COMPARE` 守衛由詢價詳情進入；顯示即時比價日、本位幣、每行數量、所有報價的原幣單價、匯率來源／有效期間、進項稅率有效期間、本位幣未稅／稅額／含稅及不可比原因。無行、無報價、422、500／traceId、斷線均有獨立提示；重新整理和換公司先清舊成本，且本頁沒有決標／轉單寫入按鈕。

| Interface No. | Interface | Route | Related REQ_F | Access |
|---|---|---|---|---|
| 4.11 | Request for Quotation View Interface | `/pur/rfqs` | REQ_F4114.4.1, REQ_F4114.4.4, REQ_F4114.4.5 | `PUR.RFQ.VIEW` |
| 4.12 | Request for Quotation Create Interface | `/pur/rfqs/new` | REQ_F4114.4.2 | `PUR.RFQ.CREATE` |
| 4.13 | Request for Quotation Edit Interface | `/pur/rfqs/:id/edit` | REQ_F4114.4.3, REQ_F4114.4.6 | `PUR.RFQ.EDIT` |
| 4.14 | Supplier Quotation Edit Interface | `/pur/rfqs/:id/quotations` | REQ_F4114.5.1, REQ_F4114.5.2, REQ_F4114.5.3, REQ_F4114.5.4 | `PUR.QUOTATION.VIEW/QUOTE` |
| 4.15 | Quotation Comparison Interface | `/pur/rfqs/:id/comparison` | REQ_F4114.6.1, REQ_F4114.6.2, REQ_F4114.6.3 | `PUR.RFQ.COMPARE`；AWARD／CONVERT 待後續單元 |

| Requirement ID | Item | Description | Purpose | Format | Valid Range | Related I/O | Author | Related REQ_F |
|---|---|---|---|---|---|---|---|---|
| REQ_IO_A4.11.1 | RFQ Filters (Input) | Filter number, date, status and requisition. | Find authorized requests. | Text / date / ID | Current company | None | Developer | REQ_F4114.4.1 |
| REQ_IO_A4.11.2 | RFQ List (Table) | Show saved status, deadline and invited supplier count. | Open an RFQ. | Paged table | VIEW scope | REQ_IO_A4.11.1 | Developer | REQ_F4114.4.1 |
| REQ_IO_A4.11.3 | Send / Close (Button) | Show allowed lifecycle action and disabled reason. | Freeze or close an RFQ. | Button | SEND / CLOSE | REQ_IO_A4.11.2 | Developer | REQ_F4114.4.4, REQ_F4114.4.5 |
| REQ_IO_A4.11.4 | Export CSV (Button) | Export every filtered saved RFQ line after checking all pages and details; report row count or the reason no file was downloaded. | Download a complete safe register. | UTF-8 CSV | VIEW + EXPORT, at most 10000 lines | REQ_IO_A4.11.1 | Developer | REQ_F4114.4.1 |
| REQ_IO_A4.11.5 | Print (Button) | Fetch the selected saved RFQ again before opening the print view. | Print current saved lines and invitations. | Button | VIEW scope | REQ_IO_A4.11.2 | Developer | REQ_F4114.4.1 |
| REQ_IO_A4.12.1 | RFQ Header (Input) | Enter business date, response deadline and source. | Describe request terms. | Date / reference | Deadline ≥ business date | None | Developer | REQ_F4114.4.2 |
| REQ_IO_A4.12.2 | RFQ Lines (Table) | Select active MD items, optional approved requisition lines with remaining quantity, and positive six-place quantity. | Define comparable demand. | 1–1000 rows | Current company | REQ_IO_A4.12.1 | Developer | REQ_F4114.4.2 |
| REQ_IO_A4.12.3 | Invited Suppliers (Select) | Select distinct active suppliers. | Record invitation list. | 1–50 IDs | Current company | REQ_IO_A4.12.1 | Developer | REQ_F4114.4.2 |
| REQ_IO_A4.13.1 | RFQ Revision (Form) | Replace draft with version; explain stale or sent state. | Prevent lost updates. | Form | Draft only | REQ_IO_A4.11.2 | Developer | REQ_F4114.4.3, REQ_F4114.4.6 |
| REQ_IO_A4.14.1 | Quotation Lines (Table) | Show saved RFQ lines and invited suppliers; select a current-company active currency and input tax rate effective on the quote date. | Record or revise saved offers with QUOTE permission. | Decimal / date / dropdown | Invited supplier, valid RFQ, effective choices | REQ_IO_A4.11.2 | Developer | REQ_F4114.5.1, REQ_F4114.5.2, REQ_F4114.5.3, REQ_F4114.5.4 |
| REQ_IO_A4.14.2 | Save / Withdraw (Button) | Save version or withdraw with clear reason. | Control offer lifecycle. | Button | QUOTE | REQ_IO_A4.14.1 | Developer | REQ_F4114.5.2, REQ_F4114.5.3, REQ_F4114.5.4 |
| REQ_IO_A4.14.3 | Quotation Filters (Input) | Filter saved offers by supplier, status and validity date with server paging. | Find authorized current and historical offers. | ID / date / enum | Current RFQ and VIEW scope | REQ_IO_A4.14.1 | Developer | REQ_F4114.5.1 |
| REQ_IO_A4.14.4 | Revision History (Table) | Show all saved revisions and lines without editing old versions. | Review exact historic price, currency, tax and validity. | Decimal(19,6) / date | VIEW scope | REQ_IO_A4.14.1 | Developer | REQ_F4114.5.1 |
| REQ_IO_A4.15.1 | Cost Comparison (Table) | Show original currency, base cost, rate date and ineligible reason. | Compare equivalent line costs. | Money / rate | Valid offers only | REQ_IO_A4.14.1 | Developer | REQ_F4114.6.1 |
| REQ_IO_A4.15.2 | Award / Convert (Button) | Save line choice and create order draft; show actual number and count. | Trace selection into order. | Button | AWARD / CONVERT | REQ_IO_A4.15.1 | Developer | REQ_F4114.6.2, REQ_F4114.6.3 |

## WU-PUR-000 請購介面（4.8 由 WU-PUR-004b 交付；4.9～4.10 由 004c 交付）

BlueWhale 採購介面占用 4.1～4.7，請購追加 4.8～4.10。三語、亮暗、空列表、無搜尋結果、無權限、驗證錯誤、伺服器錯誤及成功狀態都須可辨識；禁用動作顯示原因。

| Interface No. | Interface | Route | Related REQ_F | Access |
|---|---|---|---|---|
| 4.8 | Purchase Requisition View Interface | `/pur/requisitions` | REQ_F4114.3.1, REQ_F4114.3.4, REQ_F4114.3.5, REQ_F4114.3.6 | `PUR.REQUISITION.VIEW` |
| 4.9 | Purchase Requisition Create Interface | `/pur/requisitions/new` | REQ_F4114.3.2 | `PUR.REQUISITION.CREATE` |
| 4.10 | Purchase Requisition Edit Interface | `/pur/requisitions/:id/edit` | REQ_F4114.3.3 | `PUR.REQUISITION.EDIT` |

| Requirement ID | Item | Description | Purpose | Format | Valid Range | Related I/O | Author | Related REQ_F |
|---|---|---|---|---|---|---|---|---|
| REQ_IO_A4.8.1 | Requisition Filters (Input) | Filter number, date, requester and state. | Find company requisitions. | Text / date / ID | Current company | None | Developer | REQ_F4114.3.1 |
| REQ_IO_A4.8.2 | Requisition List (Table) | Show number, date, requester, state and saved quantity. | Open a requisition. | Paged table | VIEW | REQ_IO_A4.8.1 | Developer | REQ_F4114.3.1 |
| REQ_IO_A4.8.3 | Saved Detail (Panel) | Show lines, converted quantity, version and approval history. | Explain actual progress. | Panel | VIEW | REQ_IO_A4.8.2 | Developer | REQ_F4114.3.1, REQ_F4114.3.5 |
| REQ_IO_A4.8.4 | Submit / Delete (Button) | Show allowed actions and disabled reasons. | Control draft lifecycle. | Button | SUBMIT / DELETE | REQ_IO_A4.8.2 | Developer | REQ_F4114.3.4, REQ_F4114.3.6 |
| REQ_IO_A4.8.5 | Export CSV (Button) | Export all filtered saved lines with a row count; show cancel, limit and failure reasons. | Download a safe complete register. | UTF-8 CSV | VIEW + EXPORT, at most 10000 lines | REQ_IO_A4.8.1 | Developer | REQ_F4114.3.1 |
| REQ_IO_A4.9.1 | Header (Input) | Enter business date and remark. | Describe the request. | Date / text | Valid date; remark ≤ 500 | None | Developer | REQ_F4114.3.2 |
| REQ_IO_A4.9.2 | Requested Items (Table) | Choose current company active MD items, due dates and quantities. | Define demand. | 1–1000 rows | Positive decimal(19,6) | REQ_IO_A4.9.1 | Developer | REQ_F4114.3.2 |
| REQ_IO_A4.9.3 | Save Draft (Button) | Save only after validation and show created number. | Create a draft. | Button | CREATE | REQ_IO_A4.9.2 | Developer | REQ_F4114.3.2 |
| REQ_IO_A4.10.1 | Revision Form (Table) | Edit saved header and all lines with version. | Correct a draft or rejection. | Date / rows | Editable state only | REQ_IO_A4.8.3 | Developer | REQ_F4114.3.3 |
| REQ_IO_A4.10.2 | Save Revision (Button) | Replace saved version; show conflict and reload option. | Prevent lost updates. | Button | EDIT | REQ_IO_A4.10.1 | Developer | REQ_F4114.3.3 |

WU-PUR-004c：物料由 `GET /item-options` 在 CREATE／EDIT 權限下搜尋並按代碼分頁；修訂以 `GET /{id}/edit` 及 `GET /{id}/saved-item-options` 在 EDIT 範圍守衛後顯示既有行、名稱與停用狀態。表單僅送目前行及表頭，伺服器仍最終驗證 MD 引用。介面 4.9／4.10 的日期、數量、行數、備註驗證錯誤與 API 錯誤顯示於表單；修改保存後回列表，未保存離開需確認。完整 CSV 留 004d。

WU-PUR-004d：介面 4.8 的匯出按目前已套用條件遍歷所有分頁，再依每張請購 VIEW 權限讀取保存明細；下載完成提示實際資料行數。待請求時可取消，0 行、超上限、版本／範圍變動及 API 錯誤均不下載部分 CSV，具體原因顯示於頁面。
