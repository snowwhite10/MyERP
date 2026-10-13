# INV 測試計畫

| Test Case ID | Related REQ_F | Case | Expected | Status |
|---|---|---|---|---|
| INV-LOT-001 | REQ_F4115.24.1, REQ_F4115.24.3 | 批號入庫與按儲位出庫 | 首次入庫建立批號與日期，大小寫／空白不同視為同批號；按儲位加總；他儲位無該批號、未知批號、超量都回 `INV.LOT.INSUFFICIENT` 且不寫異動 | 通過（LotSerialTrackingTests，008b） |
| INV-LOT-002 | REQ_F4115.24.1 | 批號日期一致 | 既有批號提供不同有效期限、同一來源兩行同批號日期不同都回 `INV.LOT.ATTRIBUTE_MISMATCH`；省略日期可沿用 | 通過（LotSerialTrackingTests，008b） |
| INV-LOT-003 | REQ_F4115.24.2, REQ_F4115.24.3 | 序號在庫唯一 | 在庫序號（不分大小寫）再入庫 409 `INV.SERIAL.IN_STOCK`；未知或不在該儲位的序號出庫 `INV.SERIAL.NOT_AVAILABLE`；同來源調撥可行；出庫後可再入庫 | 通過（LotSerialTrackingTests，008b） |
| INV-LOT-004 | REQ_F4115.24.4 | 追蹤方式檢查 | None 帶批號、批號／序號種類不符、序號物料小數數量回 400；缺少或合計不符回 422 `INV.TRACKING.REQUIRED`；無任何寫入；None 物料小數數量仍可過帳 | 通過（LotSerialTrackingTests，008b） |
| INV-LOT-005 | REQ_F4115.24.4 | 批序號格式 | 14 組：擇一、空白、長度、數量、精度、序號數量、行內重複、混用、日期用於出庫／序號、有效期限早於生產日期、跨行重複序號，都回 400 且無引用 | 通過（LotSerialTrackingTests，008b） |
| INV-LOT-006 | REQ_F4115.24.4 | 冪等重放含批序號 | 行序、批序號順序、大小寫、數量小數位不同仍重放原異動；改批號數量或序號回 `INV.POSTING.CONFLICT` | 通過（LotSerialTrackingTests，008b） |
| INV-LOT-007 | REQ_F4115.24.2 | 並發序號入庫 | 雙連線入庫同一序號，第二方等待鎖後回 409，只有一筆序號與明細 | 通過（LotSerialTrackingTests，008b） |
| INV-LOT-008 | REQ_F4115.24.1 | 並發首次建立批號 | 雙連線首次入庫同批號，第二方等待後沿用同一批號，兩筆結存正確 | 通過（LotSerialTrackingTests，008b） |
| INV-LOT-009 | REQ_F4115.24.3 | 不可改寫與公司隔離 | 直接 UPDATE／DELETE 批序號明細被 trigger 拒絕；他公司看不到批號與明細 | 通過（LotSerialTrackingTests，008b） |
| INV-LOT-010 | REQ_F4115.24.4 | 不追蹤物料回歸 | 既有異動帳、其他入庫／出庫、期初、HTTP 測試（None 物料）行為不變，重放仍比較成功 | 通過（StockLedgerTests、OtherReceipt／OtherIssue／OpeningStock 既有測試，008b） |
| INV-LOT-011 | REQ_F4115.24.1, REQ_F4115.24.2, REQ_F4115.24.4 | 其他入庫採集 | None 物料帶批號 400；草稿缺序號可保存、送審 422 且未送 SYS；整組替換後送審、SYS 核准過帳建立批號／序號；詳情依輸入順序；在庫序號再入庫 409 並回滾 | 通過（OtherReceiptTests，008b） |
| INV-LOT-012 | REQ_F4115.24.5, REQ_F4115.24.6 | 追溯查詢 | 批號列表結存、在庫篩選、關鍵字；批號各儲位結存與歷史；序號在庫狀態與出入歷史；他公司 404；itemId=0 400 | 通過（LotSerialTrackingTests，008b） |
| INV-LOT-013 | REQ_F4115.24.3, REQ_F4115.24.4 | 其他出庫核對 | 出庫批號帶日期 400；缺批號送審 422；批號不足 `INV.LOT.INSUFFICIENT`、未知序號 `INV.SERIAL.NOT_AVAILABLE`；成功過帳後批號結存與序號狀態正確 | 通過（OtherIssueTests，008b） |
| INV-LOT-014 | REQ_F4115.24.1, REQ_F4115.24.2, REQ_F4115.24.4 | 期初採集 | 預覽逐行 `INV.TRACKING.REQUIRED`／`INV.TRACKING.MODE_MISMATCH`；提交建立批號（含生產日期）與序號；相同請求重送；序號不同回 409 | 通過（OpeningStockTests，008b） |
| INV-LOT-015 | REQ_F4115.24.4, REQ_F4115.24.5, REQ_F4115.24.6 | HTTP 追溯與權限 | 期初經 HTTP 帶字串數量批號；只有 INV.INVENTORY.VIEW 不可查追溯（403）；列表、追溯、跨公司 404、400；缺批號期初 422 | 通過（OtherReceiptApiTests，008b） |
| INV-OPN-001 | REQ_F4115.23.2 | 預覽逐行錯誤 | 物料／倉庫不存在、物料停用、儲位不屬倉庫、批次內重複各回對應欄位與錯誤碼；合計含全部行；無批次、引用或異動 | 通過（OpeningStockTests，007a） |
| INV-OPN-002 | REQ_F4115.23.3, REQ_F4115.23.4 | 提交、重送、衝突與重複期初 | `OPENING_STOCK` 入庫含單價，結存正確；同鍵同內容回原批次不再入庫；內容不同 409；新鍵重複登記 422 不留資料 | 通過（OpeningStockTests，007a） |
| INV-OPN-003 | REQ_F4115.23.3 | 啟用日期規則 | 晚於今天回 400；晚於既有異動回 `AFTER_MOVEMENT`；等於最早異動日期可提交 | 通過（OpeningStockTests，007a） |
| INV-OPN-004 | REQ_F4115.23.2, REQ_F4115.23.3 | 格式錯誤 | 請求鍵、日期、備註、行數、行號、ID、數量／單價精度與千行金額上限，預覽與提交都回 400 且不寫資料 | 通過（17 組資料列，含結尾換行請求鍵，007a） |
| INV-OPN-005 | REQ_F4115.23.1, REQ_F4115.23.4 | 並發、公司隔離與資料庫防線 | 雙連線不同請求鍵登記同一結存鍵只一方成功；他公司看不到；直接插入重複結存鍵（含 NULL 儲位）被唯一索引拒絕 | 通過（OpeningStockTests，007a） |
| INV-OPN-006 | REQ_F4115.23.1, REQ_F4115.23.2, REQ_F4115.23.3, REQ_F4115.23.4 | HTTP 權限與流程 | 未登入 401、只有 VIEW 不可預覽／提交；預覽、201 提交、200 重送、409、422、列表篩選、詳情與跨公司 404 | 通過（OtherReceiptApiTests，007a） |
| INV-OPN-007 | REQ_F4115.23.1, REQ_F4115.23.2, REQ_F4115.23.3 | 三語介面與 E2E | 批次輸入、預覽錯誤顯示、提交／重送、列表、列印／匯出、暗色與權限 | 通過（Vitest opening-stock.service／form 8 個；Playwright opening-stock.spec 4 個，007b） |
| INV-QRY-001 | REQ_F4115.1.1 | 真實 SQL 多公司、多儲位、正負異動與分頁 | 只列目前公司非零結存；篩選與總數、分頁一致；數量保留六位 | 通過（StockLedgerTests，006a） |
| INV-QRY-002 | REQ_F4115.1.2 | 真實 SQL 日期、方向、儲位與公司篩選 | 明細按業務日期及 ID 穩定分頁，來源與代碼快照正確，跨公司不可見 | 通過（StockLedgerTests，006a） |
| INV-QRY-003 | REQ_F4115.1.1, REQ_F4115.1.2 | 非法參數與 API 權限 | 參數回 400；兩端點各只接受其 VIEW 權限 | 通過（OtherReceiptApiTests，006a） |
| INV-QRY-004 | REQ_F4115.1.1, REQ_F4115.1.2 | 三語頁、列印、CSV 匯出及權限 | 三語與暗色可用，CSV 依篩選且防公式注入 | 通過（stock-query.service.spec 2、stock-query.spec Playwright 6，006b） |


| Test Case ID | Related REQ_F | Case | Expected | Status |
|---|---|---|---|---|
| INV-BASE-001 | REQ_F4115.21.1 | 模組、Contracts 與權限目錄 | 組合根載入兩個組件，模組代碼 INV，所有權限只屬 INV | 通過（架構測試） |
| INV-BASE-002 | REQ_F4115.21.1, REQ_F4115.21.2 | 跨公司及非法單據狀態轉換 | 跨公司、重複核准拒絕；合法草稿送審及核准成功 | 通過（架構測試） |
| INV-BASE-003 | REQ_F4115.21.1, REQ_F4115.22.3 | SQL 公司隔離 | 異動與結存跨公司不可見；跨公司引用寫入拒絕 | 通過（StockLedgerTests） |
| INV-REF-001 | REQ_F4115.21.3 | MD 引用驗證及共享鎖 | 缺少物料／儲位回 404；保存交易期間排他鎖不可取得；提交後可取得 | 通過（SQL 整合） |
| INV-REF-002 | REQ_F4115.21.3 | 儲位刪除排他鎖 | 刪除交易持有 `md_warehouse` 排他鎖直到提交 | 通過（SQL 整合） |
| INV-REF-003 | REQ_F4115.21.3 | MD 刪除與 INV 實際寫入並發 | 寫入先提交則刪除 409；刪除先提交則寫入 404；無懸空引用 | 通過（SQL 整合，兩種重疊交易）；2026-10-13 全量後端 773/773 |
| INV-LED-001 | REQ_F4115.22.1, REQ_F4115.22.3 | 入庫、出庫與結存 | 來源快照、六位數量、分儲位結存正確 | 通過（SQL 整合） |
| INV-LED-002 | REQ_F4115.22.1 | 相同與不同內容重送 | 相同回既有 ID，不同回 409，未增加異動 | 通過（SQL 整合） |
| INV-LED-003 | REQ_F4115.22.2 | 不足庫存與部分寫入 | 全部回滾，引用及異動均無殘留 | 通過（SQL 整合） |
| INV-LED-004 | REQ_F4115.22.2 | 同交易轉倉 | 借出／借入同時成功，總量守恆 | 通過（SQL 整合） |
| INV-LED-005 | REQ_F4115.22.2 | 並發出庫 | 同一結存鍵不能超賣 | 通過（SQL 雙連線整合） |
| INV-LED-006 | REQ_F4115.22.2 | 呼叫端交易回滾 | 異動與引用均撤銷，來源可重新過帳 | 通過（SQL 整合） |
| INV-LED-007 | REQ_F4115.22.3 | 異動帳不可改寫 | 原生 SQL UPDATE／DELETE 被 trigger 拒絕 | 通過（SQL 整合） |
| INV-LED-008 | REQ_F4115.22.1, REQ_F4115.22.2 | 非法輸入 | 方向、數量精度、極小值、成本與來源行重複均拒絕 | 通過（14 組 SQL 整合資料） |
| INV-REC-001 | REQ_F4115.2.2, REQ_F4115.2.3, REQ_F4115.2.6 | 草稿建立、編輯及刪除 | 單號唯一、明細整組替換、版本衝突 409；無庫存異動；只准草稿軟刪除 | 通過（OtherReceiptTests.DraftsAreNumbered…，2026-10-13 WU-INV-004a） |
| INV-REC-002 | REQ_F4115.2.2, REQ_F4115.2.3 | 非法明細與引用 | 空明細、重複行號、負數／超精度、超額、跨公司或不相符儲位拒絕，無部分保存且不取號 | 通過（13 組資料列 + 表頭／行數 + 引用案例） |
| INV-REC-003 | REQ_F4115.2.4, REQ_F4115.2.5 | 立即核准與多級審批 | SYS 結果與單據狀態、異動帳同交易；駁回不過帳，修訂後可重送 | 通過（服務層立即核准／處理器／駁回重送 + OtherReceiptApiTests 經真實 SYS 審批 API 核准後過帳） |
| INV-REC-004 | REQ_F4115.2.4 | 核准重送、失敗回滾與並發 | 重送只過帳一次；無效主檔或過帳失敗回滾 SYS 決定與單據；不能併發改寫已送審明細 | 通過（重複處理 409、停用物料回滾、雙連線編輯／送審只一方成功） |
| INV-REC-005 | REQ_F4115.2.1, REQ_F4115.2.6 | 公司隔離與權限 | 列表、詳情只見目前公司；無權限 403；非草稿刪除拒絕；缺單號規則 422 不留資料 | 通過（API）；列印／匯出待 004b 隨 INV-REC-006 驗證 |
| INV-REC-006 | REQ_F4115.2.1, REQ_F4115.2.2, REQ_F4115.2.3, REQ_F4115.2.4, REQ_F4115.2.5, REQ_F4115.2.6 | 三語介面與 E2E | 建立、查看、編輯、提交、列印／匯出，在亮暗主題與三語下可用；SYS 審批由 INV-REC-003 真實 API 驗證 | 通過：Vitest API 契約 2、Playwright 模擬 API 4（建立／送審、唯讀、駁回修訂／列印／匯出、三語／暗色）；後端 SYS 審批見 INV-REC-003；2026-10-13 |
| INV-ISS-001 | REQ_F4115.3.2, REQ_F4115.3.4 | 無庫存時送審立即核准 | 回 `INV.STOCK.INSUFFICIENT`，單據仍草稿，無出庫異動 | 通過（OtherIssueTests，真實 SQL） |
| INV-ISS-002 | REQ_F4115.3.4 | 入庫後核准出庫及重送 | 結存按出庫量減少，異動成本留空，重送不再扣庫存 | 通過（OtherIssueTests，真實 SQL） |
| INV-ISS-003 | REQ_F4115.3.4 | 同公司同結存鍵兩張單並發出庫 | 只有一張過帳，另一張不足庫存，最終結存非負 | 通過（OtherIssueTests，雙連線真實 SQL） |
| INV-ISS-005 | REQ_F4115.3.2 | 千行極大估算金額 | 回 `lines` 驗證錯誤，不發生 decimal 溢位、取號或資料寫入 | 通過（OtherIssueTests） |
| INV-ISS-004 | REQ_F4115.3.1, REQ_F4115.3.2, REQ_F4115.3.3, REQ_F4115.3.4, REQ_F4115.3.5, REQ_F4115.3.6 | 三語介面與 E2E | 建立／修訂／查看／送審、庫存不足提示、列印／匯出、暗色模式與權限 | 通過：Vitest 2、Playwright 5（含庫存不足保留草稿）；後端 SYS 審批見 INV-ISS-001～003；2026-10-13 |
