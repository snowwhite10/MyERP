# INV 測試計畫

## WU-INV-011 組裝入庫（ADR-066；011a 草稿、011b1 估值鎖、011b2 審批過帳已驗收）

測試層級：U 單元、I 真實 SQL／HTTP、E Playwright。011d 的 DocGen.Tests 驗證來源契約；011a 的 `AssemblyDraftTests` 與 `AssemblyDraftHttpEnforcesPermissionsAndCompanyScope` 驗證草稿持久化、版本、公司隔離、主檔引用、批序號保存及基本 HTTP 權限。011b1 的 `AssemblyCostSnapshotTests` 驗證成本快照與過帳共用交易鎖。011b2 的 `AssemblyDraftTests` 擴充案例驗證即時／待審核准、成本守恆、未知／零成本、精度拒絕、批序號衝突及整單回滾；HTTP 案例驗證 SUBMIT 權限、422 與補價後成功。011c 先紅後綠：前端客戶端、建立／編輯表單、列表搜尋及安全 CSV；真實 SQL／HTTP 覆蓋單號、備註搜尋及列表成品名稱；模擬 E2E 覆蓋建立後送審、唯讀角色、三語亮暗、400／422／500 回饋；逐狀態 QA 截圖見 `tests/qa/INV/screenshots/5.9–5.11-*`。未測項保持部分通過，尤其真實多級 SYS HTTP 與人手啟動冒煙。

| Test Case ID | Related REQ_F | Case | Expected | Status |
|---|---|---|---|---|
| INV-ASM-001 | REQ_F4115.4.2, REQ_F4115.4.4 | 正常組裝（I） | 手動建一成品、多耗料草稿無異動；SYS 最終核准同交易各耗料 Out、成品 In，數量與來源行一致，Posted；PP BOM 不必存在 | 通過：真實 SQL 即時核准與待審 handler；HTTP 即時核准；真實多級 SYS HTTP 待模組收尾 |
| INV-ASM-002 | REQ_F4115.4.2, REQ_F4115.4.3 | 空值與容量邊界（U,I） | 缺成品、零耗料、重複行號、零／負數量、超六位小數、1001 行、備註超 1000 字回 400；合法 1000 行、六位小數及月底／跨年日期可保存 | 通過：真實 SQL／HTTP 驗證上述草稿邊界，含合法 1000 行與閏日 |
| INV-ASM-003 | REQ_F4115.4.2, REQ_F4115.4.4 | 主檔引用（I） | 其他公司、已刪、停用物料／倉庫、儲位不屬倉庫、同物料作成品及耗料，拒絕且無單據或異動；已保存後停用在送審／核准再檢查 | 部分通過：無效倉庫、同物料及回滾；其他引用與送審再檢查待補 |
| INV-ASM-004 | REQ_F4115.4.4 | 不足及整單回滾（I） | 第二個耗料不足、來源批號不足、序號不存在、成品序號已在庫，SYS 決定、單據狀態及所有異動一起回滾；沒有部分成功或全部略過提示 | 部分通過：第二耗料不足、批號不足、成品序號在庫回滾；不存在序號與 SYS 決定回滾待補 |
| INV-ASM-005 | REQ_F4115.4.4 | 並發與冪等（I） | 兩連線爭同一耗料只一方可過帳；相同來源快照重放不增異動，差異快照回 409；重複 SYS 核准回狀態衝突 | 通過：雙連線兩張組裝單爭同耗料僅一張 Posted；同來源相同快照重放不增異動，差異快照及重複 SYS handler 核准 409 |
| INV-ASM-006 | REQ_F4115.4.4 | 成本守恆（I） | 依核准時鎖定的各耗料移動平均取四位出庫行值，成品行值等於總耗料值；零成本有明確零值，未知成本拒絕，尾差與溢位不生虛構成本 | 部分通過：四位值守恆、六位單價、明確零／未知、不可表示尾差拒絕；溢位專項待補 |
| INV-ASM-017 | REQ_F4115.4.4 | 估值快照與過帳同鎖（I） | 未入庫物料快照為未知；交易持有快照鎖時，另一連線同物料過帳須等待提交；提交後可讀到明確零成本，未知與零不混同 | 通過：真實 SQL 雙連線驗證；011b2 尚須驗收消耗成本、尾差及整單回滾 |
| INV-ASM-007 | REQ_F4115.4.2, REQ_F4115.4.4 | 批序號（I） | 草稿可暫缺；送審缺少、種類不符、合計不符時 422；成品新批號與序號入庫、耗料按來源位置出庫，追溯與實際結存一致 | 部分通過：草稿保存、批號不足／成品序號衝突、改正後過帳；缺少／種類／合計及追溯查詢專項待補 |
| INV-ASM-008 | REQ_F4115.4.3, REQ_F4115.4.5, REQ_F4115.4.6 | 狀態與版本（I） | 舊版本 409；駁回無異動，修訂回草稿可再送；只有草稿可軟刪，待審／已過帳不可修改、刪除 | 通過：真實 SQL 待審／駁回／修訂／核准、舊版本、重複核准及刪除限制 |
| INV-ASM-009 | REQ_F4115.4.1, REQ_F4115.4.2, REQ_F4115.4.3 | 權限／資料範圍（I,E） | 匿名 401、缺動作權限 403、他公司 404；列表按公司及資料範圍隔離；只持 VIEW 不能取得建立選項或提交 | 部分通過：真實 HTTP 401／403／404、公司隔離；模擬 E2E 唯讀無建立／送審／刪除、路由 403 且不請求建立選項；選項端點直接 403 的真實 HTTP 待補 |
| INV-ASM-010 | REQ_F4115.4.1 | 查詢與列印（U,I,E） | 日期／狀態／成品篩選與穩定分頁正確，空列表和無結果分別提示；列印及 CSV 只用已保存資料，跨頁完整且防公式注入 | 通過：真實 SQL 日期／狀態／成品／單號／備註及分頁，HTTP 批次成品名稱；模擬 E2E 空與無結果、單號／成品篩選、201 筆跨頁 CSV 逐單讀已保存明細、防公式注入，列印呼叫時包含已保存成品及耗料 |
| INV-ASM-011 | REQ_F4115.4.2, REQ_F4115.4.3 | 介面與取消（E） | 三語亮暗建立／編輯；選項取消不改草稿、換倉清儲位、已保存停用引用有標記；禁用按鈕可見原因 | 通過：建立／送審、已存停用引用、三語亮暗截圖、未儲存離開守衛；E2E 驗證取消選項不改已存倉儲、換倉清儲位且 PUT 只送新倉與 null 儲位 |
| INV-ASM-012 | REQ_F4115.4.2, REQ_F4115.4.4 | API／網絡錯誤（I,E） | 400 欄位、409 版本、422 行原因、500 traceId、逾時／斷線均顯示易懂三語訊息；失敗時畫面不顯示成功，資料無變動 | 部分通過：400 欄位路徑／原因、409 版本、422 未定價、500 traceId 的單元／模擬 E2E 與截圖；斷線時繁中提示且草稿仍在；逾時、其他 422 逐行原因與三語來源訊息待補 |
| INV-ASM-013 | REQ_F4115.4.2, REQ_F4115.4.4 | 0 筆與部分結果（U,E） | 組裝是整單原子操作，0 行拒絕；不存在部分成功／略過路徑，畫面只報全成功或明確全失敗原因；E2E 比對訊息與列表及結存 | 部分通過：零耗料前端拒絕、建立／送審 E2E 比對列表；真實結存與畫面聯查待補 |
| INV-ASM-014 | REQ_F4115.4.2 | 檔案（U） | 不適用：此單元沒有匯入或附件 API；CSV 僅匯出，見 INV-ASM-010 | 不適用 |
| INV-ASM-015 | REQ_F4115.4.1, REQ_F4115.4.4 | 無權與審批畫面（E） | 唯讀角色不可見提交／刪除，直接路由與 API 均拒絕；駁回理由及最新審批狀態與實際單據一致 | 部分通過：唯讀按鈕隱藏、建立直達路由 403、API 403；駁回理由與審批歷史畫面待補 |
| INV-ASM-016 | REQ_F4115.4.4 | 真實啟動冒煙（E） | 模組打 tag 前按 README 路徑啟動並驗證登入，無測試專用埠或設定；審批過帳另用真實 SQL／HTTP 測試 | 待測 |

## WU-INV-015d 跨模組過帳設計（ADR-064）

| Test Case ID | Related REQ_F | Description | Expected Result | Status |
|---|---|---|---|---|
| INV-XMOD-001 | REQ_F4115.22.4 | PUR 已核准單據多行入庫，含批號、序號與單價 | 真實 SQL 同交易寫全部 In 異動及追蹤；物料基本單位與成本快照一致 | INV 契約 SQL 通過；來源單據待 015b |
| INV-XMOD-002 | REQ_F4115.22.5 | SD 已核准單據多行出庫；兩條連線爭同一結存鍵 | 真實 SQL 只容許可用量內的交易提交，Out 成本 null，無負庫存／負批序號結存 | INV 契約 SQL 通過；來源單據待 015b |
| INV-XMOD-003 | REQ_F4115.22.4, REQ_F4115.22.5 | 同公司同來源 ID 相同／不同內容重送，另一公司使用相同來源 ID | 相同快照 `Replayed=true` 且原異動 ID 不變；不同快照 409；公司之間隔離 | INV 契約 SQL 通過 |
| INV-XMOD-004 | REQ_F4115.22.4, REQ_F4115.22.5 | 呼叫端交易在 INV 成功後回滾，或 MD／INV 驗證失敗 | SYS 決定、來源 Posted、引用與異動全部不提交；回滾後可用同來源重試 | INV 異動／引用 SQL 通過；SYS／來源待 015b |
| INV-XMOD-005 | REQ_F4115.22.4, REQ_F4115.22.5 | 真實 HTTP 無權、他公司、駁回、重複核准與待審明細被竄改 | 無權拒絕、他公司 404／403、駁回零異動、重複核准不新增異動；過帳只用已保存明細 | 待 015b（PUR／SD 建立後） |
| INV-XMOD-006 | REQ_F4115.22.4, REQ_F4115.22.5 | 收貨或出貨缺批序號、序號不在指定位置、物料停用 | 原交易拒絕且無部分過帳；問題代碼沿用 INV／MD 契約 | INV 缺追蹤／位置檢查 SQL 通過；停用與來源待 015b 前補齊 |

015a 先寫編譯失敗的契約測試，再以真實 SQL 測試受限入口、重放、隔離、交易回滾、批序號與並發。015b 需實際 PUR／SD 單據、SYS 工作流與 HTTP 測試；上述 SQL 結果不等於來源核准驗收。

## WU-INV-014d 估值驗收案例（014a／014b 執行）

| Test Case ID | Related REQ_F | Description | Acceptance Criteria | Status |
|---|---|---|---|---|
| INV-VAL-001 | REQ_F4115.25.1, REQ_F4115.25.3 | 期初、入庫、出庫混合 | 10 @ 2.555555 加 5 @ 4.000000 後出 3，期末 12.000000、36.4445；不改異動成本；按行四位取整，完全出庫帶走尾差 | 通過，`WeightedAverageRoundsLinesAndDrainsResidualValue` |
| INV-VAL-002 | REQ_F4115.25.1, REQ_F4115.25.3 | 跨倉調撥與盤點 | 調撥配對使公司物料總量及價值不變；有已知池值的盤盈沿用入庫前均價，盤虧按均價扣除 | 通過，`TransferIsValueNeutralAndUnknownCostIsDistinguishedFromZero` |
| INV-VAL-003 | REQ_F4115.25.2 | 無成本盤盈及零價入庫 | 無已知池值時盤盈標 `Unpriced`、金額為 NULL；明確零單價入庫仍為 `Valued`；池歸零後可由已知入庫重新估值 | 通過，`TransferIsValueNeutralAndUnknownCostIsDistinguishedFromZero` |
| INV-VAL-004 | REQ_F4115.25.1, REQ_F4115.25.2 | 公司隔離、截止與異常 | 相同物料跨公司互不影響，晚於截止時間的異動不計；負池量、調撥不配對、溢位報錯 | 通過，`CutoffAndCompanyScopeExcludeOtherMovements`、`UnpairedTransferReturnsBusinessError`、`NegativePoolAndOverflowReturnBusinessErrors` |
| INV-VAL-005 | REQ_F4115.25.1 | SQL 效能 | 100,000 筆同物料異動的實際執行計畫使用公司物料時間索引，不全表掃描；記錄 CPU、邏輯讀取與耗時 | 通過，`HundredThousandMovementsUseValuationIndex`；Index Seek；實測 4,316 ms／CPU 2,295 ms／106,274 logical reads |
| INV-VAL-006 | REQ_F4115.25.1, REQ_F4115.25.2 | HTTP 與三語介面 | 匿名／無權拒絕；他公司物料 404；僅估值權限可列物料；截止篩選、未定價空金額與零價區分、錯誤提示；三語暗色，標明暫估而非已過帳財務值 | 014b：`ValuationEndpointEnforcesPermissionCompanyAndPostingCutoff`、`ValuationService` Vitest、`inventory-valuation.spec.ts`；全量結果見 progress.md |

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
# WU-INV-016d 選擇資料驗收（016a 後端、016b1 前端選項入口完成；016b2 表單接入待執行）

| Test Case ID | Related REQ_F | Case | Expected | Status |
|---|---|---|---|---|
| INV-SEL-001 | REQ_F4115.20.1, REQ_F4115.20.2 | 真實 SQL 搜尋與分頁 | 代碼／名稱搜尋、代碼加 ID 穩定分頁；物料回傳追蹤模式；停用、刪除與他公司主檔不列出 | 通過：InventorySelectionOptionsTests，2026-10-14T16:11:21+08:00 |
| INV-SEL-002 | REQ_F4115.20.2 | 儲位父倉庫 | 只列所選啟用倉庫的儲位；不存在、停用或他公司倉庫無資料且不洩漏 | 通過：InventorySelectionOptionsTests，2026-10-14T15:31:33+08:00 |
| INV-SEL-003 | REQ_F4115.20.1, REQ_F4115.20.2 | HTTP 權限 | 未登入 401；只有單據 CREATE／EDIT 可查；只有 INV VIEW 或 MD VIEW 不可查；持 INV 權限但無 MD VIEW 可查 | 通過：OtherReceiptApiTests.SelectionOptionsRequireDocumentEditPermissionWithoutMdView，2026-10-14T15:31:33+08:00 |
| INV-SEL-004 | REQ_F4115.20.3 | 選擇框狀態 | 搜尋重設頁碼、快速切換忽略舊結果、確認套用、取消／Esc 不改草稿；換倉庫清空儲位 | 通過：共用框、其他入庫／出庫、調撥／期初、盤點 Vitest／E2E |
| INV-SEL-005 | REQ_F4115.20.1, REQ_F4115.20.2, REQ_F4115.20.3 | 三語與暗色 E2E | 建立與編輯單據能以物料／倉庫／儲位選擇框完成選取；三語文字和暗色可讀；無權限者進不了表單 | 通過：b2 七張、b3 九張、盤點六張截圖與 E2E；唯讀路由保護 |
| INV-SEL-006 | REQ_F4115.20.1, REQ_F4115.20.2 | 選後停用及歷史顯示 | 選後 MD 停用，保存／送審仍由 MD guard 拒絕；既有已保存停用引用可辨識，但新選項不再列出 | 通過：b1 SQL／HTTP，其他入庫／出庫、調撥、盤點停用引用與保存拒絕 E2E |

016a 完成 INV-SEL-001～003；016b2b1 完成歷史資料的受權讀取契約。016b2b2 完成其他入庫／出庫，016b2b3 完成調撥／期初，016b2b4 完成盤點；INV-SEL-004～006 結案。

### WU-INV-016b2b4 盤點選擇情境（2026-10-15T14:51:27+08:00）

| ID | 類別 | 需求 | 層級 | 情境與預期 | 結果 |
|---|---|---|---|---|---|
| INV-SEL-B4-001 | 正常 | REQ_F4115.20.1–3 | U、E | 建立範圍跨頁多選倉庫／物料；實地新增行單選物料、範圍內倉庫及儲位，保存 ID 與畫面一致，無 MD 三類全量預載 | 通過：stocktake-selection、inventory-stocktake |
| INV-SEL-B4-002 | 邊界 | REQ_F4115.20.2–3 | U、E | 多選框可確認清空全部選擇；空範圍阻止建立；取消／Esc 不改草稿；無倉庫不能選儲位；換倉清儲位，追蹤模式由選中物料決定 | 通過：stocktake-selection、inventory-stocktake |
| INV-SEL-B4-003 | 重複與衝突 | REQ_F4115.20.3 | U、E | 快速搜尋忽略舊結果，公司或倉庫切換後舊選項結果不覆寫；版本 409 保留草稿 | 通過：共用框、stocktake-selection；版本由既有盤點測試覆蓋 |
| INV-SEL-B4-004 | 部分結果 | REQ_F4115.20.3 | U | 範圍多選只在確認後整批套用；取消／Esc 零筆變更。部分成功／全部略過／全部失敗不適用：選項操作未寫資料 | 通過：stocktake-selection、inventory-stocktake |
| INV-SEL-B4-005 | 權限 | REQ_F4115.20.1–2 | I、E | INV CREATE／EDIT 才可讀選項，唯讀不可進建立／實盤頁；無 MD.ITEM_CATEGORY.VIEW 時仍可用倉庫／物料建立，分類顯示權限提示 | 通過：016a HTTP、stocktake-selection、唯讀 E2E |
| INV-SEL-B4-006 | 狀態與關聯 | REQ_F4115.20.1–3 | U、E | 已存停用引用可識別，不出現在新選項；重新快照引入新行後補名稱；選後停用由 MD guard 在保存時拒絕，畫面顯示原因並保留輸入；非草稿仍由後端拒絕 | 通過：stocktake-selection、inventory-stocktake；狀態沿 INV-STK 測試 |
| INV-SEL-B4-007 | 錯誤 | REQ_F4115.20.1–3 | U、E | 400／409／422、500／逾時及網絡錯誤交共用錯誤處理；停用物料／分類顯示三語具體原因與可重試草稿 | 通過：stocktake.service、inventory-stocktake；通用錯誤沿共用 E2E |
| INV-SEL-B4-008 | 檔案 | REQ_F4115.20.1–3 | — | 不適用：選擇框與盤點表單不匯入檔案 | 不適用 |

### WU-INV-016b2b3 調撥／期初表單選擇情境（2026-10-14T20:43:38+08:00）

| ID | 類別 | 需求 | 層級 | 情境與預期 | 結果 |
|---|---|---|---|---|---|
| INV-SEL-B3-001 | 正常 | REQ_F4115.20.1–3 | U、E | 調撥與期初用 INV 分頁框確認選物料／倉庫／儲位；實際保存／預覽 ID 與畫面一致，表單不預載 MD 主檔 | 通過：transfer-opening-selection、inventory-transfer／opening-stock E2E |
| INV-SEL-B3-002 | 邊界 | REQ_F4115.20.2–3 | U、E | 未選倉庫不能選儲位；取消／Esc 不改草稿；調撥來源與目的各自清對應儲位，另一側保持；換物料清批序號，同物料重選保留 | 通過：transfer-opening-selection；共用框取消／Esc 測試 |
| INV-SEL-B3-003 | 重複與衝突 | REQ_F4115.20.3 | U | 快速搜尋舊請求由共用框忽略；選儲位後換倉庫，舊選擇結果不覆寫；公司切換後舊單據與選項結果不覆寫 | 通過：共用框、transfer-opening-selection |
| INV-SEL-B3-004 | 部分結果 | REQ_F4115.20.3 | U | 單選框不批次修改；部分成功、全部略過不適用；取消是零筆草稿變更 | 通過：transfer-opening-selection；共用框取消測試 |
| INV-SEL-B3-005 | 權限 | REQ_F4115.20.1–2 | I、E | 只有單據 CREATE／EDIT 可讀選項，唯讀不能進建立頁；跨公司隔離；已存引用限單據 VIEW | 通過：016a／b1 HTTP；transfer／opening-stock 唯讀 E2E |
| INV-SEL-B3-006 | 狀態與關聯 | REQ_F4115.20.3 | U、E | 調撥已存停用引用有代碼、名稱與停用標記但不再列於新選項；編輯受 MD guard 拒絕並保留草稿；期初一經過帳無編輯頁 | 調撥顯示 U 通過；MD guard 共用後端及 b2 E2E 通過；期初不適用編輯 |
| INV-SEL-B3-007 | 錯誤 | REQ_F4115.20.3 | U、E | 400／409／422／500／逾時沿共用錯誤攔截與既有單據測試顯示；MD 停用拒絕顯示三語原因；選項查詢失敗留在彈窗重試 | 共用框、既有調撥／期初 E2E 及 b2 guard 測試通過 |
| INV-SEL-B3-008 | 檔案 | REQ_F4115.20.1–3 | — | 不適用：選擇框與兩張表單不接收檔案 | 不適用 |

### WU-INV-016b2b2 情境（2026-10-14T20:18:15+08:00）

| ID | 類別 | 對應需求 | 層級 | 情境與預期 | 結果 |
|---|---|---|---|---|---|
| INV-SEL-B2-001 | 正常 | REQ_F4115.20.1–3 | U、E | 其他入庫／出庫以受權分頁選項選物料、倉庫、儲位；確認後才寫入草稿，保存實際 ID 與畫面一致，沒有 MD 全量預載 | 通過：other-selection-form、other-receipt／other-issue E2E |
| INV-SEL-B2-002 | 邊界 | REQ_F4115.20.2–3 | U、E | 未選倉庫時儲位停用並說明；取消／Esc 保留空值；同物料重選保留批序號，換物料清空；換倉庫清儲位，舊儲位回應不覆寫 | 通過：other-selection-form、other-receipt E2E |
| INV-SEL-B2-003 | 重複與衝突 | REQ_F4115.20.3 | U | 搜尋／分頁的舊請求由共用選擇框取消；已存引用慢於新選項返回仍保留新值；公司切換後舊回應不覆寫 | 通過：shared-ui、other-selection-form（公司切換防護按 revision 審查） |
| INV-SEL-B2-004 | 部分結果 | REQ_F4115.20.3 | U | 單選框不批次修改；部分成功、全部略過不適用，取消等同零筆草稿變更 | 通過：取消／Esc E2E |
| INV-SEL-B2-005 | 權限 | REQ_F4115.20.1–2 | I、E | 未登入、單據無 CREATE／EDIT、跨公司選項受 016a API 保護；唯讀者無法進建立頁；已存引用限單據 VIEW／公司 | 通過：016a／b1 SQL／HTTP；other-receipt／issue 唯讀 E2E |
| INV-SEL-B2-006 | 狀態與關聯 | REQ_F4115.20.3 | U、E | 已存停用物料／倉庫／儲位保留代碼與名稱及停用標記，不可從啟用選項新選；保存由 MD guard 回 422 及具體原因，草稿不消失 | 通過：other-selection-form、other-receipt E2E |
| INV-SEL-B2-007 | 錯誤 | REQ_F4115.20.3 | U | 400／409／500／逾時沿用共用 API 提示；422 停用引用用 MD 錯誤碼三語提示；選項查詢失敗顯示共用對話框錯誤 | 通過：shared-ui、other-selection-form、other-receipt E2E；其他錯誤在既有單據案例 |
| INV-SEL-B2-008 | 檔案 | REQ_F4115.20.1–3 | — | 不適用：選擇介面與表單不接收檔案 | 不適用 |
