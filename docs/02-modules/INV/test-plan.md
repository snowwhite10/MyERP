# INV 測試計畫

## WU-INV-019 介面編號一致性

| Test Case ID | Related REQ_F | Case | Expected | Status |
|---|---|---|---|---|
| INV-DOC-001 | REQ_F4115.23.1, REQ_F4115.23.2, REQ_F4115.24.5 | BlueWhale 保留介面號 | 尚未完成的 5.8 Sales Outbound、5.9／5.10 Product Assembly 若被期初或追溯占用，docgen 報 `PARITY_INTERFACE_CONFLICT`；期初 View／Create 固定 5.21／5.22，追溯固定 5.23 | 通過（DocGen.Tests 保留號失敗案例先紅後綠；帶 DB docgen 0 Error，019） |
| INV-DOC-002 | REQ_F4115.23.1, REQ_F4115.24.5 | 重號與三份文件次序 | 同一介面號重複報 `DUPLICATE_ID`；SRS 4.4／4.5 與 SDS 4.2 次序不同報 `INTERFACE_ORDER` | 通過（DocGen.Tests，019） |

## WU-INV-010 盤點（ADR-062）

010d 只固定案例；010a 先把真實 SQL／HTTP 測試寫成失敗案例，再實作；010b 補前端單元測試與 Playwright。通過欄必須依實際執行結果更新。

| Test Case ID | Related REQ_F | Case | Expected | Status |
|---|---|---|---|---|
| INV-STK-001 | REQ_F4115.7.1 | 快照範圍與粒度 | 多倉、類別含下層、指定物料的交集正確；只收結存大於 0 的鍵，有儲位與無儲位分開；批序號按號碼記帳面；保存快照時間與最大異動 ID；建立不寫異動 | 通過（StocktakeTests，010a1） |
| INV-STK-002 | REQ_F4115.7.1, REQ_F4115.7.4 | MD、上限與範圍互斥 | 他公司或停用的倉庫／類別／物料拒絕；超過 5,000 行或 20,000 個批序號回 422 且不取號；同物料＋倉庫在另一張未結單回 `SCOPE_IN_USE`，刪除或過帳後可再建；兩條連線同時建立只有一方成功 | 通過（StocktakeTests，含雙連線，010a1） |
| INV-STK-003 | REQ_F4115.7.4 | 實盤保存 | 六位小數、0 值、序號 0／1、批序號合計等於行實盤；新增行倉庫須在範圍內，否則 `LINE_OUT_OF_SCOPE`；舊版本 409；未盤送審回 `COUNT_MISSING` | 通過（StocktakeTests 保存、驗證、版本，010a1；`SubmitRequiresDraftVersionEveryCountAndPostableVariance` 行與批序號未盤 `COUNT_MISSING`、盈虧異動上限 `TOO_MANY_LINES`，010a2） |
| INV-STK-004 | REQ_F4115.7.4, REQ_F4115.7.5 | 快照過期與重新快照 | 快照後範圍內有入庫、出庫、調撥或進出淨額為 0，送審都回 `SNAPSHOT_STALE` 並列出鍵；範圍外異動不影響；重新快照更新帳面、保留實盤、標記重盤、加入新鍵、移除帳面 0 且未盤的鍵 | 通過（StocktakeTests 重新快照，010a1；`SubmitRejectsSnapshotChangedByLaterMovementsInScopeOnly` 入、出、儲位轉移、淨額 0 皆過期並回 changedKeys，範圍外物料與倉庫不影響；HTTP 回 `changedKeys`，010a2） |
| INV-STK-005 | REQ_F4115.7.5 | 核准盈虧過帳 | 盤盈寫 In、盤虧寫 Out、差異 0 不寫；同一行可同時盤盈批號 A、盤虧批號 B；成本空；送審後才發生的異動會疊加，最終結存 = 實盤 + 送審後淨異動；全部無差異仍轉 Posted | 通過（StocktakeTests `ApprovalPostsGainAndLossPerLineOnTopOfLaterMovementsAndCannotRepeat`、過期測試末段全無差異 Posted；HTTP 真實 SYS 核准過帳，010a2） |
| INV-STK-006 | REQ_F4115.7.5 | 核准回滾 | 送審後被出庫令盤虧不足、序號已不在該位置、盤盈序號已在他處現存時回 409 `INV.SERIAL.IN_STOCK`（過帳服務既有錯誤碼）；SYS 決定、狀態與全部異動回滾 | 通過（StocktakeTests `ApprovalRollsBackWhenLaterMovementsMakeVarianceUnpostable`，010a2） |
| INV-STK-007 | REQ_F4115.7.4, REQ_F4115.7.6, REQ_F4115.7.7 | 狀態與版本 | 駁回無異動，修訂後回草稿可再送審；只有草稿可軟刪除並釋放範圍；待審及過帳不能編輯、刷新或刪除 | 通過（StocktakeTests 狀態限制、駁回修訂回草稿、刪除釋放範圍，010a1；`RejectionWritesNothingAndRevisedStocktakeCanBeResubmitted`，010a2） |
| INV-STK-008 | REQ_F4115.7.5 | 冪等與重複審批 | 相同來源與內容重放只回原異動，內容不同 409；重複 SYS 決定不再寫異動 | 通過（StocktakeTests 過帳後重複核准 409 `APPROVAL_STATE_CHANGED`、同來源不同內容 `INV.POSTING.CONFLICT`、異動數不變；過帳服務相同內容重放見 TransferTests，010a2） |
| INV-STK-009 | REQ_F4115.7.1, REQ_F4115.7.3 | HTTP 權限、分頁與公司隔離 | 無權限 403；列表與詳情只見目前公司；別公司 ID 404；欄位與排序錯誤 400 | 通過（OtherReceiptApiTests 盤點 API，010a1；`StocktakeSubmitReportsStaleKeysAndSysApprovalPostsVariance` submit 403、variance 401／VIEW 200，010a2） |
| INV-STK-010 | REQ_F4115.7.2 | 盈虧差異報告 | 類型 Gain／Loss／Match／Uncounted 判定正確，總盤盈、總盤虧數量與行數正確，`differenceOnly` 篩選正確，報告只讀已保存資料 | 通過（StocktakeTests 未盤／同行盤盈盤虧／合計與篩選，HTTP `differenceOnly`，010a2） |
| INV-STK-011 | REQ_F4115.7.1, REQ_F4115.7.2, REQ_F4115.7.3, REQ_F4115.7.4, REQ_F4115.7.5, REQ_F4115.7.7 | 三語頁與 E2E | 範圍查詢、實盤輸入與即時差異、新增行、過期時重新快照、送審、列表與差異報告、權限、暗色、列印、安全 CSV；真實 API 冒煙依第 19 節門檻 | 通過：服務 Vitest 4；模擬 E2E 7（零實盤、過期異動鍵、重新快照、實地行、批序號、報告／跨頁安全 CSV／暗色、唯讀權限）。後端 SQL／HTTP 案例 INV-STK-001～010 另列；模組結案前跑第 19 節真實 API 冒煙。 |

## WU-INV-009 庫存調撥（ADR-061）

009d 固定案例；009a 已先寫真實 SQL／HTTP 失敗測試再實作，009b 補前端單元與 Playwright。後端案例由 `TransferTests` 與 `OtherReceiptApiTests.TransferApiEnforcesPermissionsAndCompanyScope` 驗證。

| Test Case ID | Related REQ_F | Case | Expected | Status |
|---|---|---|---|---|
| INV-TRF-001 | REQ_F4115.6.2, REQ_F4115.6.4 | 跨倉及同倉不同儲位調撥 | 建草稿不改結存；最終核准後每行同來源產生一出一入，兩端數量相等、全公司總量不變 | 通過（TransferTests，009a） |
| INV-TRF-002 | REQ_F4115.6.2 | 兩側 MD 與相同結存鍵 | 他公司／停用／錯倉儲位拒絕，來源目的完全相同回 400；同倉不同儲位可建立，拒絕時無引用或單據 | 通過（TransferTests，含 SQL 約束，009a） |
| INV-TRF-003 | REQ_F4115.6.4 | 來源不足及雙連線爭用 | 單行不足與多行其中一行不足均回 422，SYS 決定與全部異動回滾；並發只一方可耗用同一份庫存 | 通過（TransferTests，009a） |
| INV-TRF-004 | REQ_F4115.6.4 | 批號與序號搬遷 | 批號可依數量搬遷，序號從來源變為目的；兩側 tracking 與數量完全配對，追溯只列目的為現存；來源無該批序號全退 | 通過（TransferTests，009a） |
| INV-TRF-005 | REQ_F4115.6.3, REQ_F4115.6.5, REQ_F4115.6.6 | 狀態／版本 | 駁回無異動且修訂可再送審；舊版本 409；僅草稿可軟刪，待審與過帳不可編輯或刪除 | 通過（TransferTests，009a） |
| INV-TRF-006 | REQ_F4115.6.1, REQ_F4115.6.2 | HTTP 權限、分頁與公司隔離 | 無權限 403；列表與詳情只見目前公司；別公司 ID 404；欄位及排序錯誤 400 | 通過（OtherReceiptApiTests，009a） |
| INV-TRF-007 | REQ_F4115.6.4 | 冪等重放與重複審批 | 相同過帳來源與內容重放只回原異動，不同內容 409；重複 SYS 決定不再寫異動 | 通過（TransferTests，009a） |
| INV-TRF-008 | REQ_F4115.6.2, REQ_F4115.6.4 | 批序號及數量邊界 | 六位精度、最多 1000 行、10,000 追蹤項、序號跨行重複、缺少或合計不等；錯誤不取號／不過帳 | 通過（TransferTests，009a） |
| INV-TRF-009 | REQ_F4115.6.1, REQ_F4115.6.2, REQ_F4115.6.3, REQ_F4115.6.4 | 三語頁與 E2E | 列表／建立／駁回編輯、權限、暗色、批序號欄、列印、篩選安全 CSV；真實 API 冒煙依第 19 節門檻 | 通過（009b：`transfer.service.spec`、`transfer-form.spec`、`transfer-page.spec` 共 11 個 Vitest；`inventory-transfer.spec.ts` 5 個 Playwright。未改啟動方式／proxy／路由前綴，19.5 不要求真實 API 冒煙，留待 INV 模組完成前執行） |


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
| INV-LOT-UI-001 | REQ_F4115.24.1, REQ_F4115.24.2, REQ_F4115.24.4 | 批序號表單 | 批號多列合計、序號多行貼上、精確六位小數與重複號碼檢查；三種單據送出 `tracking[]` 且數量保留字串 | 通過（Vitest tracking.spec、Playwright inventory-tracking.spec，008c） |
| INV-LOT-UI-002 | REQ_F4115.24.5, REQ_F4115.24.6 | 追溯介面 | 批／序號分頁、物料與在庫篩選、詳情位置與歷史截斷提示；三語與暗色可讀 | 通過（Playwright inventory-tracking.spec，008c） |
| INV-LOT-UI-003 | REQ_F4115.24.5 | 追溯權限 | 無 `INV.TRACKING.VIEW` 時選單不顯示、直接路由轉 403 | 通過（Playwright inventory-tracking.spec，008c） |
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
