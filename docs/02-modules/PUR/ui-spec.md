# PUR 介面規格

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
