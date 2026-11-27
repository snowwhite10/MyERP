# PUR 測試計劃

## WU-PUR-006c1 採購單 DELETE 軟刪切片（2026-11-28T07:08:03+08:00）

依 `myerp-test-scenarios` 逐類核對：正常（Draft 軟刪後單號／行／金額保留、請購已轉量不變）、邊界（版本 0 與 +1、ID 0）、重複衝突（重複刪除 404、舊版本 409、刪除後同單號不可再建）、部分結果（單筆整體動作，無批次；UPDATE 故障整筆回滾）、權限（匿名 401、未指派公司 403、EDIT 不含 DELETE 403、本人範圍他人／分店不符／他公司 404）、狀態（Draft／Rejected 可刪；PendingApproval／Approved／Cancelled 422）、關聯（駁回已釋回不再釋回、SYS 歷史保留）、錯誤（ProblemDetails 含 errorCode／traceId）；檔案不適用。略過狀態守衛的變異使 5 個案例轉紅。

| ID | 類別 | 情境／預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-PO-DEL-001 | 正常／重複 | 已轉量 4 的請購行被 Draft 引用（未送審）：刪除後 query filter 不可見；忽略過濾讀到 is_deleted、deleted_at＝IClock、狀態 Draft、單號／金額／旗標 false 不變、2 行保留、已轉量 4、無 SYS 實例；再刪（任何版本）404；以同單號新建 DbUpdateException（單號不重用）。 | I | REQ_F4114.1.6 | PASS 1/1 |
| PUR-PO-DEL-002 | 關聯／狀態 | 一步流程送審（已轉量 1→6）後 SYS 駁回釋回至 1；以目前版本刪除 Rejected 成功，已轉量仍 1（不二次釋回），SYS 駁回實例保留。 | I | REQ_F4114.1.5, REQ_F4114.1.6 | PASS 1/1 |
| PUR-PO-DEL-003 | 狀態 | PendingApproval（旗標 true）、Approved（true／false）、Cancelled：422 `PUR.ORDER.NOT_DELETABLE`，未刪除、狀態／版本／旗標／已轉量不變。 | I | REQ_F4114.1.6 | PASS 4/4 |
| PUR-PO-DEL-004 | 權限／衝突／邊界 | ID 0 400；版本 +1 與 0 409；缺 DELETE 403 `COMMON.PERMISSION.DENIED`；本人範圍他人、分店不符、他公司訂單 404；皆零變更；擁有人本人範圍可刪。 | I | REQ_F4114.1.6 | PASS 1/1 |
| PUR-PO-DEL-005 | 錯誤／交易 | 注入 `UPDATE [pur_purchase_order]` 故障：未刪除、deleted_at null、版本不變；原版本重試成功。 | I | REQ_F4114.1.6 | PASS 1/1 |
| PUR-PO-DEL-HTTP-001 | 權限／狀態／錯誤 | 真實 JWT：匿名 401、未指派公司 403 `COMMON.COMPANY.ACCESS_DENIED`、VIEW＋EDIT 無 DELETE 403、他公司標頭 404、舊版本 409、Approved 422 `NOT_DELETABLE`，ProblemDetails 含 traceId 且零變更；成功 204 後 GET 404、再刪 404，行保留。 | I | REQ_F4114.1.6 | PASS 1/1 |

## WU-PUR-006b3 SUBMIT／SYS 決策受權 HTTP 切片（2026-11-28T06:57:39+08:00）

依 `myerp-test-scenarios` 逐類核對：正常（一步流程核准保留、駁回釋回、零步驟立即核准）、邊界（最小精度超量 3＋3.000001 對 6）、重複衝突（舊版本 409、已送審再送 422、完成實例再駁回 422 不二次釋回）、部分結果（不足／無效來源／MD 失效皆整單零變更，無部分成功）、權限（匿名 401、未指派公司 403 `COMMON.COMPANY.ACCESS_DENIED`、只有 VIEW／CREATE／EDIT 無 SUBMIT 403、送審人無 SYS.APPROVAL.PROCESS 不能自審 403、他公司／本人範圍外／不存在 404）、狀態（Draft 才可送審）、關聯（MD 供應商未核准、物料停用、請購 Draft）、錯誤（ProblemDetails 均含 errorCode／traceId，422 帶 `shortages`／`lineNos`；決策 409 時 SYS 實例仍 Pending、PUR 不變）；檔案不適用。真實 JWT／角色／MD／SYS 引擎 `PurchaseOrderSubmitHttpTests` 3/3；略過供應商核准檢查的變異使 SUB-HTTP-002 轉紅。HTTP 層故障注入不另做：SYS 決策三種 UPDATE 故障回滾已由 006b2 PUR-PO-DEC-005 在同一服務驗收，HTTP 以資料漂移／旗標不符 409 驗證整筆回滾。

| ID | 類別 | 情境／預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-PO-SUB-HTTP-001 | 權限／衝突 | 匿名 401；未指派公司 403 `COMMON.COMPANY.ACCESS_DENIED`；只有 VIEW／CREATE／EDIT 403 `COMMON.PERMISSION.DENIED`；SUBMIT 本人範圍送他人訂單 404、他公司標頭 404、不存在 404；舊版本 409 `COMMON.CONCURRENCY.CONFLICT`；兩單皆仍 Draft、版本不變、已轉量 0、無 SYS 實例。 | I | REQ_F4114.1.4 | PASS 1/1 |
| PUR-PO-SUB-HTTP-002 | 部分結果／關聯／錯誤 | 3＋3.000001 對可用 6：422 `PUR.ORDER.REQUISITION_INSUFFICIENT`＋`shortages[{requisitionLineId,orderLineNos:[1,2],requested:6.000001,available:6}]`；引用 Draft 請購 422 `INVALID_REQUISITION_SOURCE`＋`lineNos:[2]`；供應商改待審 422 `MD.SUPPLIER.NOT_APPROVED`；物料停用 422 `MD.ITEM.INACTIVE`；皆零變更。修正後同版本送審 200 零步驟 `Approved`（stepCount 0、已轉量 2），再送 422 `NOT_SUBMITTABLE` 不二次消耗。 | I | REQ_F4114.1.4 | PASS 1/1 |
| PUR-PO-SUB-HTTP-003 | 正常／狀態／錯誤 | 一步流程四單送審皆 PendingApproval（已轉量 18）；送審人自審 403；審批人 HTTP 核准→Approved 保留 18；駁回→Rejected 釋回 4（14），重複駁回 422 `SYS.APPROVAL.NOT_PENDING` 不再釋回；已轉量漂移至 3 後駁回 409 `PUR.ORDER.REQUISITION_RELEASE_INVALID`、清旗標後核准 409 `PUR.ORDER.APPROVAL_STATE_CHANGED`，SYS 實例 ID／版本／Pending 不變、訂單與已轉量不變。 | I | REQ_F4114.1.4, REQ_F4114.1.5 | PASS 1/1 |

## WU-PUR-006b2 SYS 最終決定處理器切片（2026-11-28T06:37:51+08:00）

依 `myerp-test-scenarios` 逐類核對：正常（多級核准、駁回釋回）、邊界（中間核准、人工行無來源不釋回、兩請購行聚合）、重複衝突（完成後再決定、訂單狀態／旗標不符 409）、部分結果（任何一行釋回不一致整筆 409、無部分釋回）、權限（審批人資格由 SYS 引擎守衛；受權 HTTP 留 006b3）、狀態（Draft／Approved／Rejected／旗標 false 均拒絕）、關聯（請購行讀取忽略軟刪過濾，釋回不受來源後續狀態影響）、錯誤交易（三種 UPDATE 故障整筆回滾）；檔案不適用（無匯入匯出）。`PurchaseOrderSubmissionTests`（含 `.Approval`）18/18，處理器經 `PurchasingModule` 註冊解析；未註冊時 6 案例紅，略過釋回的變異使 5 案例紅。

| ID | 類別 | 情境／預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-PO-DEC-001 | 正常／邊界 | 兩步流程：中間核准後訂單仍 PendingApproval、已轉量 6 不變；最終核准 Approved、旗標保留 true、已轉量仍 6；完成後再駁回 422 `SYS.APPROVAL.NOT_PENDING`，訂單與請購量不變。 | I | REQ_F4114.1.4, REQ_F4114.1.5 | PASS 1/1（PUR-PO-004 核准部分、PUR-PO-015） |
| PUR-PO-DEC-002 | 正常／關聯 | 兩條請購行（同行兩訂單行聚合 2＋3、另一行 5）加人工行：駁回帶意見後 Rejected、旗標 false、兩條請購行各釋回一次（6→1、5→0），意見保存於 SYS 步驟；他單取走餘量後修訂重送按當時餘量 422 `PUR.ORDER.REQUISITION_INSUFFICIENT`（本單 5、可用 2），不消耗。 | I | REQ_F4114.1.4, REQ_F4114.1.5 | PASS 1/1（PUR-PO-004 駁回部分） |
| PUR-PO-DEC-003 | 重複／狀態 | 處理器遇訂單 Rejected、Approved、PendingApproval 但旗標 false、Draft，對核准與駁回皆 409 `PUR.ORDER.APPROVAL_STATE_CHANGED`，已轉量不變；訂單不存在 404；非最終狀態拒絕。 | I | REQ_F4114.1.5 | PASS 1/1（PUR-PO-015） |
| PUR-PO-DEC-004 | 部分結果／錯誤 | 已轉量被外部改至低於本單量：SYS 駁回整筆 409 `PUR.ORDER.REQUISITION_RELEASE_INVALID`，SYS 實例仍 Pending、訂單 PendingApproval＋旗標 true、已轉量不變（不低於 0、無部分釋回）。 | I | REQ_F4114.1.5 | PASS 1/1 |
| PUR-PO-DEC-005 | 錯誤／交易 | 駁回時注入請購行、採購單或 SYS 實例 UPDATE 故障：SYS 實例、訂單狀態／旗標及已轉量全回滾；恢復後駁回只釋回一次（5→2）。 | I | REQ_F4114.1.5 | PASS 3/3（PUR-PO-022 駁回部分；CANCEL 留 006c） |

## WU-PUR-006b1 SUBMIT、聚合消耗與零步驟核准切片（2026-11-28T06:16:01+08:00）

依 `myerp-test-scenarios` 逐類核對：單張整單送審，無批次部分成功或檔案；正常／邊界／重複衝突／部分結果／權限／狀態／關聯／錯誤交易均有真實 SQL＋真實 SYS 引擎案例；受權 HTTP 與 ProblemDetails 擴充欄位留 006b3，SYS 決策處理器留 006b2，畫面留 006e／006f。`PurchaseOrderSubmissionTests` 12/12；停用餘量檢查的變異使 2 案例轉紅（證明測試有效）。

| ID | 類別 | 情境／預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-PO-SUB-001 | 正常／重複 | 零步驟：同單兩行引用同一請購行（2＋3）加一人工行，送審即 Approved、SYS 實例 Approved（0 步、金額＝保存 baseGross、單號一致）；另一單再引用 4，已轉量 1→10 為兩單總和；消耗旗標 true；無 INV 異動。 | I | REQ_F4114.1.4, REQ_F4114.3.1 | PASS 1/1（PUR-PO-003） |
| PUR-PO-SUB-002 | 邊界／狀態 | 一步流程：本單量剛好等於餘量（10−4＝6）成功，PendingApproval＋Pending 實例；以新版本重送 422 `PUR.ORDER.NOT_SUBMITTABLE`、舊版本 409，已轉量不再增加、實例仍一筆。 | I | REQ_F4114.1.4 | PASS 1/1（PUR-PO-007／014／015） |
| PUR-PO-SUB-003 | 部分結果 | 一行足量、另一請購行由兩訂單行聚合後超最小精度 0.000001：整單 422 `PUR.ORDER.REQUISITION_INSUFFICIENT`，`shortages` 指出請購行、訂單行 2／3、本單 6.000001、可用 6；兩個請購行均未消耗、訂單仍 Draft／原版本、無 SYS 實例。 | I | REQ_F4114.1.4 | PASS 1/1（PUR-PO-007／010／011） |
| PUR-PO-SUB-004 | 關聯 | 來源請購為 Draft／Closed／Rejected／已軟刪草稿或物料不符：整單 422 `PUR.ORDER.INVALID_REQUISITION_SOURCE`，`lineNos=[2]`，有效行也不消耗、零 SYS 實例。 | I | REQ_F4114.1.4 | PASS 5/5（PUR-PO-016 提交部分） |
| PUR-PO-SUB-005 | 權限／狀態／衝突 | 版本＋1 與 0 皆 409；本人範圍他人、分店範圍不符、他公司 404；缺 SUBMIT 403；Rejected（須先修訂）與 Cancelled 422；已軟刪 404；全部零消耗零實例。 | I | REQ_F4114.1.4 | PASS 1/1（PUR-PO-008 SUBMIT、PUR-PO-014） |
| PUR-PO-SUB-006 | 重複／衝突 | 雙連線同時送審兩張各 5 的訂單競爭餘量 8：恰一單成功，敗者僅可 422 不足或 409，仍 Draft、旗標 false、無實例；已轉量 7 不超核准量。 | I | REQ_F4114.1.4 | PASS 1/1（PUR-PO-009） |
| PUR-PO-SUB-007 | 錯誤／交易 | 注入請購行 UPDATE 或 SYS 實例 INSERT 故障：訂單狀態／版本／旗標、全部請購已轉量與審批實例全回滾；修復後原版本重送成功並只消耗一次。 | I | REQ_F4114.1.4 | PASS 2/2（PUR-PO-018 請購量／SYS 實例部分） |

## WU-PUR-006a4 受權 HTTP、交易回滾、競態與溢位切片（2026-11-28T06:02:50+08:00）

依 `myerp-test-scenarios` 逐類核對：單張整單保存，無批次部分成功或檔案；前端錯誤顯示與畫面留 006e／006f。

| ID | 類別 | 情境／預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-PO-HTTP-001 | 權限 | 匿名 POST、GET `/edit` 401；未指派公司 POST 403 `COMMON.COMPANY.ACCESS_DENIED`；只有無關 PUR 權限時 POST／PUT／GET `/edit`／GET 各 403 `COMMON.PERMISSION.DENIED`（含 traceId）；VIEW＋CREATE 可 GET 詳情，但 GET `/edit` 與 PUT 仍 403；保存版本、備註、行量零變更。 | I | REQ_F4114.1.2, REQ_F4114.1.3 | PASS 1/1 |
| PUR-PO-HTTP-002 | 權限／衝突／狀態 | 他公司（同使用者全權）GET、GET `/edit`、PUT 皆 404 `PUR.ORDER.NOT_FOUND`；PUT 舊版本 409 `COMMON.CONCURRENCY.CONFLICT`；PendingApproval PUT 422 `PUR.ORDER.NOT_EDITABLE`；均含 traceId，兩張訂單零變更；EDIT 可讀 `/edit`。 | I | REQ_F4114.1.3 | PASS 1/1 |
| PUR-PO-TXN-001 | 錯誤／交易 | 真實 SYS 發號後注入 `pur_purchase_order_line`／`pur_purchase_order` INSERT 故障：表頭、行與 `sys_number_sequence` 全回滾；修復後重試得 `PO20280229001`。 | I | REQ_F4114.1.2 | PASS 1/1（PUR-PO-018 CREATE 部分） |
| PUR-PO-TXN-002 | 重複／衝突 | 雙連線同時 CREATE：成功者單號互異且連號（001、002…），失敗只可為 409，保存行數 = 成功數 × 2，流水號 = 成功數。 | I | REQ_F4114.1.2 | PASS 1/1（PUR-PO-008 建立部分） |
| PUR-PO-EDIT-008 | 邊界 | 決標行保存本位幣 9×10^14 加人工行 2×10^14：表頭超 DECIMAL(19,4) 整單 422 `PUR.ORDER.AMOUNT_OVERFLOW`（修正前為 SQL 參數越界 500 類 DbUpdateException），版本、備註、行數不變；單元 `SumStorable` 剛好上限內成功、達上限與 decimal 溢位均 422。 | U,I | REQ_F4114.1.3、BR-PUR-022 | 先紅後綠：I 1/1、U 1/1 |
| PUR-PO-MIG-001 | 資料移轉 | `myerp_test` 帶 15 張訂單／21 行（含請購連結 2 行）執行 `Purchasing_OrderDraftFoundation` Down→Up：列數不變，CHECK 9→7→9、FK 2→1→2，消耗旗標全為 0。 | I | REQ_F4114.1.1 | PASS（Down 會刪除 006a1 新欄位資料，屬預期） |

## WU-PUR-006a3 EDIT 整組修訂切片（2026-11-28T05:37:56+08:00）

依 `myerp-test-scenarios` 逐類核對：單張整單修訂，無批次部分成功或檔案。受權 HTTP 缺 EDIT 403／跨公司、雙連線競態與 Down／Up 留 006a4；前端修訂頁留 006e。

| ID | 類別 | 情境／預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-PO-EDIT-001 | 正常／關聯 | 改供應商、業務日、備註（去空白）；行 1 改量、刪行 2、加行 3；單號不變、版本 +1、行 1 保留 ID、表頭與行原幣／本位幣價稅重算，請購 `ordered_quantity` 不變。 | I | REQ_F4114.1.3 | PASS 1/1 |
| PUR-PO-EDIT-002 | 重複／衝突／狀態 | 版本 +1 與 0 均 409；PendingApproval／Approved／Cancelled 422 `PUR.ORDER.NOT_EDITABLE`；保存行與備註零變更。 | I | REQ_F4114.1.3 | PASS 1/1 |
| PUR-PO-EDIT-003 | 狀態 | Rejected 修訂後回 Draft、金額重算、版本 +1。 | I | REQ_F4114.1.3 | PASS 1/1 |
| PUR-PO-EDIT-004 | 權限／資料範圍 | EDIT 本人範圍：他人 404；無 EDIT 403；他公司 404；版本不變。 | I | REQ_F4114.1.3 | PASS 1/1；HTTP 缺權 403 留 006a4 |
| PUR-PO-EDIT-005 | 關聯／全部失敗 | 新增行引用不存在請購行整單 422，原行數量不變。 | I | REQ_F4114.1.3 | PASS 1/1 |
| PUR-PO-EDIT-006 | 狀態／關聯 | 決標行改量、改價、改物料、改稅率、被刪除，或改供應商，各 422 `PUR.ORDER.AWARD_LINE_LOCKED`；保持決標行並加人工行可保存：決標行保留 6% 快照與保存金額、原幣金額仍 null、決標來源連結仍在，人工行按 MD 10% 計價，表頭 75。 | I | REQ_F4114.1.3、BR-PUR-022 | PASS 1/1 |
| PUR-PO-EDIT-007 | 權限 | 匿名 PUT 401。 | I | REQ_F4114.1.3 | 隨 RequisitionHttpTests 全量 |
| PUR-PO-EDIT-NA | 部分結果／檔案 | PUT 整單原子保存，任何一行錯誤整單失敗；無上傳下載。 | I | REQ_F4114.1.3 | 按契約不適用 |

## WU-PUR-006a2 人工 CREATE／READ 切片（2026-11-28T05:20:00+08:00）

依 `myerp-test-scenarios` 逐類核對 PUR-PO-001～024；本切片是單張整單保存，沒有批次部分成功或檔案上傳。無權限、跨公司、MD 停用／生效日、來源狀態、發號後故障、雙連線與 EDIT／VIEW 尚待 006a 後續真實 HTTP／SQL 驗收；前端錯誤顯示與 CSV 留 006e／006f。

| ID | 類別 | 情境／預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-PO-CREATE-001 | 正常／關聯 | 兩行人工草稿在交易內保存，原幣／本位幣價稅逐行與表頭一致，來源請購 `ordered_quantity` 不變。 | I | REQ_F4114.1.2 | PASS 1/1 |
| PUR-PO-CREATE-002 | 狀態／關聯／全部失敗 | 不存在請購來源整單 422；沒有表頭或行保存。 | I | REQ_F4114.1.2 | PASS 1/1 |
| PUR-PO-CREATE-003 | 權限 | 匿名 POST 401；缺 CREATE、跨公司及資料範圍由後續受權 HTTP 驗。 | I | REQ_F4114.1.2 | 匿名 401 PASS；缺 CREATE／跨公司 HTTP 留 006a4 |
| PUR-PO-CREATE-004 | 邊界／重複／錯誤 | 0／1001 行、混幣、重複行號、精度及非法 MD 引用須在發號前拒絕；發號後 SQL 故障及雙連線唯一號須整單回滾。 | U,I | REQ_F4114.1.2 | 部分：U 21/21、混幣 I PASS；發號後故障與雙連線留 006a4 |
| PUR-PO-CREATE-005 | 狀態／全部失敗 | 一行無來源＋一行引用 Draft 請購整單 422 `PUR.ORDER.INVALID_REQUISITION_SOURCE`；混幣 400；表頭與行均零筆。 | I | REQ_F4114.1.2 | PASS 1/1 |
| PUR-PO-READ-001 | 權限／資料範圍 | VIEW 本人範圍：他人建立 404（不洩漏存在）；只有 VIEW 讀 `/edit` 403；本人 VIEW 200。 | I | REQ_F4114.1.1 | PASS 1/1 |
| PUR-PO-CREATE-NA | 部分結果／檔案 | POST 是單張整單保存，任何一行錯誤整單失敗，無逐行略過；沒有上傳或下載。 | I | REQ_F4114.1.2 | 按契約不適用 |

## WU-PUR-006a 採購單草稿結構切片（2026-11-28T03:16:01+08:00）

依 `myerp-test-scenarios` 核對 PUR-PO-001～024：本切片只驗持久化結構；人工 CREATE／EDIT／READ、MD 有效期、版本／權限／狀態、交易回滾及畫面仍由後續 006a 實作與原情境逐項驗收。無檔案上傳，PUR-PO-020 已註明 CSV 將於 VIEW 前端驗證。

| ID | 類別 | 情境／預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-PO-SCHEMA-001 | 正常／升級／狀態 | 既有採購來源列升級後保留；新增消耗旗標預設 false、備註預設 null；原幣三金額欄存在，草稿不得把消耗旗標改 true。 | I | REQ_F4114.1.2、REQ_DS43.186～187 | PASS（先紅後綠，1/1） |
| PUR-PO-SCHEMA-002 | 關聯／權限 | 訂單行的請購來源受同公司複合 FK 限制；不存在或他公司 ID 整列拒絕，沒有部分行保存。 | I | REQ_F4114.1.2、REQ_DS43.187 | PASS（同一真實 SQL 測試） |
| PUR-PO-SCHEMA-003 | 邊界／錯誤 | 原幣淨額、稅額、含稅額須一致；不一致的 SQL 寫入被 CHECK 拒絕，保存行數不變。 | I | REQ_F4114.1.2、REQ_DS43.187 | PASS（同一真實 SQL 測試） |
| PUR-PO-SCHEMA-NA | 重複／衝突／部分結果／狀態／檔案 | 此切片無 API、批次或上傳；永久單號唯一已由 005e4 驗，版本 409、訂單狀態、部分失敗回滾、CSV 與畫面由 PUR-PO-001～024 後續案例驗收。 | I,E | REQ_F4114.1.1～6 | 待 006a～f |


## WU-PUR-008b6c 真實 API Playwright：發票過帳

來源採購單／收貨尚無 API，沿 ADR-204 以單一 SQL 交易安排；採購單帶 TWD 本位幣與匯率 1，FI 科目映射由 Development seed 提供。發票建立經受權 API，其後 MATCH／POST 走 4.19 畫面；畫面訊息與 SQL 保存值雙重斷言。

| ID | 類別 | 情境／預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-INV-REAL-007 | 正常／狀態／關聯 | Matched 發票有過帳鈕，確認框顯示號碼及金額；畫面 POST 後顯示 Posted、成功訊息帶應付／傳票 ID、明細財務參照與過帳時間、列表同步、不再有過帳鈕；SQL 發票 FI 三 ID 與 ap_purchase_source／ap_posted_payable／gl_journal 各一且相同，收貨預留 4→0、已開票 0→4。 | E | REQ_F4114.8.5、REQ_IO_A4.19.8～9 | PASS（真實 API／SQL） |
| PUR-INV-REAL-NA2 | 邊界／衝突／部分結果／權限／錯誤／檔案 | 此 E2E 為單張一行，沒有批次部分結果或檔案；金額與來源邊界、同時 POST／重放、版本 409、非 Matched 422、無權限 403、FI 映射缺失 422 與回滾由 PUR-POST-001～007、105～108 驗證；畫面拒絕、500、逾時由 PUR-INV-UI-020～025 驗證。 | I,U | REQ_F4114.8.5 | 已覆蓋於所列測試 |

本 E2E 使用已存在的業務流程，不新增產品 API 或 migration。

執行記錄：2026-11-27T08:56:22+08:00；SQL 記憶體守衛 PASS（三次 10.100／10.302／10.305 GiB）；Debug 建置 0 警告／錯誤，migration 與 Development seed 成功；`Invoke-RealApiE2E.ps1 -NoBuild -Specs specs/real-api-purchase-invoice.spec.ts` 6/6。首跑新情境已成功過帳，但測試文字誤寫「憑證」（實際介面用「傳票」），修正斷言後全套通過。截圖 `tests/qa/PUR/screenshots/4.19-real-posted-light-zh-Hant.png` 已檢視：狀態、成功訊息、FI 參照與列表一致，無 P1／P2 問題。

## WU-PUR-008c3c3 真實 API Playwright：MATCH／RESOLVE／匯出（2026-11-26T23:49:18+08:00）

來源：採購單／收貨尚無 API，以 sqlcmd 安排來源列（ADR-204）；Draft 經 `POST /api/v1/pur/invoices` 建立，其後全部經畫面；每步畫面與 SQL 讀回雙斷言。

| ID | 類別 | 情境／預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-INV-REAL-001 | 正常／狀態 | 單價 2.5 對採購單 2：畫面 MATCH→「有差異」提示、差異「單價 2 → 2.5 (+0.5)」；SQL Disputed、版本 +1、收貨 matched_reserved=4。拒絕＋前後空白原因→確認框「接受 0 項、拒絕 1 項」→退回草稿提示；SQL Draft、預留釋回 0、1 筆 Reject（修剪後原因、對應 Disputed 版本、含稅差 2.1），畫面歷史 1 行「拒絕／+2.1」，比對按鈕可再用；亮暗截圖。 | E | REQ_F4114.8.1 | PASS（真實 API／SQL） |
| PUR-INV-REAL-002 | 正常 | 同差異全部接受→「已比對，尚未過帳」提示、無比對按鈕；SQL Matched、預留保留 4、決定只有 Accept。 | E | REQ_F4114.8.1 | PASS |
| PUR-INV-REAL-003 | 正常／數量不足 | 與採購單同價直接「已比對」、無決定歷史；共用收貨 4 的兩張 3：第一張已比對（預留 3），第二張 422「收貨可開票數量不足」，畫面仍草稿；SQL 狀態與版本未變、預留仍 3。 | E | REQ_F4114.8.1 | PASS |
| PUR-INV-REAL-004 | 檔案／語言 | 篩選本輪前綴：列表 5 張；繁中、英文各匯出一次：BOM、14 欄表頭、5 行＝SQL 保存行（逐欄字串一致、狀態為該語言文字）、發票號次序＝列表次序；成功樣式與行數 5。 | E | REQ_F4114.8.1 | PASS |
| PUR-INV-REAL-005 | 權限 | viewer：可見 5 張、無匯出；草稿無比對按鈕；有差異只顯示無決定權限說明，無單選／提交。 | E | REQ_F4114.8.1 | PASS |
| PUR-INV-REAL-NA | 不適用 | 409 版本衝突、500、逾時、422 上限：真實環境無法穩定重現，已由模擬 E2E（008c3b E03、008c3c2 E04）與整合測試覆蓋；跨公司由 I74／008a55 覆蓋；簡中匯出與繁英同一路徑（I72 已驗簡中表頭）。 | - | - | 記錄 |

執行記錄：2026-11-26T23:49+08:00；守衛 PASS（11.7 GiB）；`Invoke-RealApiE2E.ps1 -Specs specs/real-api-purchase-invoice` 5/5。截圖 `tests/qa/PUR/screenshots/4.19-real-*.png` 9 張經檢視無 P1／P2（P3：供應商／採購單只顯示 #ID，與 008c1 相同；成功提示底色與模擬截圖一致）。

## WU-PUR-008c3c2 介面 4.19 匯出 CSV 按鈕（2026-11-26T23:37:15+08:00）

| ID | 類別 | 情境／預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-INV-EXPORT-UI-U01 | 錯誤 | 403→無匯出權限、422 `PUR.INVOICE.EXPORT_LIMIT`→共用 errors 三語、400 帶欄位原因、逾時、網絡、429、5xx 帶 traceId、未知 422／非 HTTP→通用失敗；15 個匯出文字三語齊全。 | U | REQ_F4114.8.1 | PASS |
| PUR-INV-EXPORT-UI-001 | 正常 | 送已套用篩選（修剪關鍵字）與表頭排序、不送 page／pageSize、輸入未套用的值不送；blob 回應計數相符才以 `purchase-invoices.csv` 下載，成功提示寫出行數。 | U | REQ_F4114.8.1 | PASS |
| PUR-INV-EXPORT-UI-002 | 部分結果／檔案 | 0 行→警告樣式、不下載；`X-Export-Row-Count` 與檔案列數不符→「不完整」錯誤、不下載。 | U | REQ_F4114.8.1 | PASS |
| PUR-INV-EXPORT-UI-003 | 重複 | 匯出中按鈕停用並說明「正在匯出」，再呼叫不送第二個請求。 | U | REQ_F4114.8.1 | PASS |
| PUR-INV-EXPORT-UI-004 | 衝突 | 匯出中套用新篩選→請求取消、舊結果清除、不下載；切換公司經 reset 同路徑。 | U | REQ_F4114.8.1 | PASS |
| PUR-INV-EXPORT-UI-005 | 權限 | 只有 VIEW：按鈕、說明、結果區均不渲染。 | U | REQ_F4114.8.1 | PASS |
| PUR-INV-EXPORT-UI-006 | 狀態 | 空列表、載入中、篩選無結果、列表載入失敗各自停用並說明原因，不送匯出。 | U | REQ_F4114.8.1 | PASS |
| PUR-INV-EXPORT-UI-007 | 錯誤 | blob 錯誤本文還原：422 上限、403、500 traceId 顯示，按鈕恢復可用、不下載。 | U | REQ_F4114.8.1 | PASS |
| PUR-INV-EXPORT-UI-E01 | 正常／語言 | 繁→英→簡切換後各匯出一次：下載檔名、BOM＋該語言表頭、該語言狀態字、引號內換行按 RFC 4180 計 2 列（畫面）＝成功提示「2」（資料）；3 次請求皆帶 status／keyword、無分頁；亮暗截圖。 | E | REQ_F4114.8.1 | PASS（模擬） |
| PUR-INV-EXPORT-UI-E02 | 部分結果 | 0 行警告（繁、英）及行數不符錯誤，下載事件 0 次。 | E | REQ_F4114.8.1 | PASS（模擬） |
| PUR-INV-EXPORT-UI-E03 | 狀態／重複 | 尚無發票、篩選無結果停用原因；匯出中再強制點擊只送 1 次，完成後提示 1 行。 | E | REQ_F4114.8.1 | PASS（模擬） |
| PUR-INV-EXPORT-UI-E04 | 錯誤 | 422 上限／403／400（欄位原因）／500（traceId）三語原因，按鈕恢復可用；亮暗截圖。 | E | REQ_F4114.8.1 | PASS（模擬） |
| PUR-INV-EXPORT-UI-E05 | 權限 | 只有 VIEW 時不顯示匯出、0 個匯出請求。 | E | REQ_F4114.8.1 | PASS（模擬） |
| PUR-INV-EXPORT-UI-NA | 不適用 | 單據狀態：匯出唯讀且涵蓋五種狀態，無狀態禁止；關聯／軟刪除由後端 I71～I74 覆蓋；逾時以 U01 單元覆蓋（15 秒，模擬 E2E 未重複 SD 已驗的同一共用機制）；真實 API 檔案內容與列表一致屬 008c3c3。 | - | - | 記錄 |

## WU-PUR-008c3c1 發票 CSV 匯出後端（2026-11-26T23:22:11+08:00）

| ID | 類別 | 情境／預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-INV-EXPORT-U01 | 正常 | 三語表頭首欄與欄數一致；未知語言回繁中；各語言欄名不重複。 | U | REQ_F4114.8.1 | PASS |
| PUR-INV-EXPORT-U02 | 正常 | 五種狀態三語文字與 4.19 狀態標籤一致；未定義狀態拋例外而非輸出數字。 | U | REQ_F4114.8.1 | PASS |
| PUR-INV-EXPORT-U03 | 邊界 | 行輸出 ISO 日期（閏日 2028-02-29）、不分地區數字（1000.5 不變成 1,000.5、6 位小數保留）、含逗號／引號文字加引號。 | U | REQ_F4114.8.1 | PASS |
| PUR-INV-EXPORT-U04 | 安全 | `=`、`+`、`-`、`@`、Tab、前置空白後 `=` 開頭文字加 `'`；空／null 為空欄；加引號時 `'` 在引號內。 | U | REQ_F4114.8.1 | PASS |
| PUR-INV-EXPORT-U05 | 邊界 | 上限 10,000 行可匯出、0 行可匯出、10,001 行拋 `PUR.INVOICE.EXPORT_LIMIT`。 | U | REQ_F4114.8.1 | PASS |
| PUR-INV-EXPORT-U06 | 權限 | 模組權限目錄含 `PUR.INVOICE.EXPORT`。 | U | REQ_F4114.8.1 | PASS |
| PUR-INV-EXPORT-I71 | 正常 | 英文：`page=2&pageSize=1` 仍輸出 2 行（忽略分頁）；BOM、CRLF、`text/csv; charset=utf-8`、檔名、`Content-Language: en`、`X-Export-Row-Count: 2`；整檔逐字比對（Draft／Matched、保存位數 4.000000／14.4000）。 | I | REQ_F4114.8.1 | PASS（真實 HTTP／SQL） |
| PUR-INV-EXPORT-I72 | 語言／篩選 | 簡中 `status=Matched` 1 行、表頭簡中、狀態「已比对」；繁中 `sort=totalGross` 次序反轉、表頭繁中、狀態「草稿」。 | I | REQ_F4114.8.1 | PASS |
| PUR-INV-EXPORT-I73 | 邊界／錯誤 | 無結果（`supplierId=8`）只有表頭、計數 0；反向日期、未知／多欄排序、`supplierId=0`、未知狀態回 400。 | I | REQ_F4114.8.1 | PASS |
| PUR-INV-EXPORT-I74 | 權限／跨公司 | 只有 VIEW、只有 CREATE／MATCH 均 403；在另一公司只有 VIEW 時 403，不輸出他公司資料。 | I | REQ_F4114.8.1 | PASS |
| PUR-INV-EXPORT-NA | 不適用 | HTTP 422 上限需造 10,001 行，以 U05＋「先計數後輸出」代碼順序覆蓋；部分結果／檔案上傳／併發修改不適用（唯讀匯出、快照交易）；500／逾時畫面提示屬 008c3c2。 | - | - | 記錄 |

執行記錄：2026-11-26T23:22+08:00；守衛 PASS（8.8 GiB）；測試與實作同輪撰寫，定向單元 33/33＋5/5、真實 HTTP 1/1。

## WU-PUR-008c3b 介面 4.19 MATCH／RESOLVE 畫面動作（2026-11-26T22:57:18+08:00）

| ID | 類別 | 情境／預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-INV-UI-U04 | 邊界 | 決定預檢：缺動作、空白理由、修剪後 501 字被拒；前後有空白的 500 字通過；無差異行不要求決定。 | U | REQ_F4114.8.4 | PASS |
| PUR-INV-UI-010 | 權限／狀態 | 比對按鈕只在 Draft＋`PUR.INVOICE.MATCH`；無權或 Matched 不顯示；Disputed 無 RESOLVE 權只見差異不見表單。 | U | REQ_F4114.8.3 | PASS |
| PUR-INV-UI-011 | 正常 | 確認後 POST `/match?version=2`（空 body）；回 Disputed 提示逐行決定、重讀明細、列表列狀態／版本更新；取消確認不送請求。 | U | REQ_F4114.8.3 | PASS |
| PUR-INV-UI-012 | 正常 | MATCH 回 Matched 提示「尚未過帳到應付」。 | U | REQ_F4114.8.3 | PASS |
| PUR-INV-UI-013 | 錯誤 422／409 | 收貨量不足 422 顯示原因、不重讀；版本衝突 409 顯示原因並重讀明細。 | U | REQ_F4114.8.3 | PASS |
| PUR-INV-UI-014 | 正常 | Disputed 明細只為差異非空行列出決定欄，差異顯示訂單值→發票值與帶符號差額（19 位精確）。 | U | REQ_F4114.8.4 | PASS |
| PUR-INV-UI-015 | 驗證／部分 | 未填齊逐行說明且不送；一行接受一行拒絕送出修剪理由及版本；回 Draft 提示已退回草稿並重讀。 | U | REQ_F4114.8.4～5 | PASS |
| PUR-INV-UI-016 | 正常／錯誤 | 全部接受回 Matched 提示尚未過帳；INVALID_RESOLUTION 重讀且保留仍適用理由；鎖逾時只顯示原因、保留輸入、不重讀；500 顯示 traceId。 | U | REQ_F4114.8.4 | PASS |
| PUR-INV-UI-017 | 關聯 | Disputed 但沒有任何行有差異（來源失效）時說明原因、不顯示送出。 | U | REQ_F4114.8.4 | PASS |
| PUR-INV-UI-018 | 衝突／競態 | 處理中重複點擊只送一次；切換到其他發票後舊動作回應不覆蓋畫面；404 重載列表。 | U | REQ_F4114.8.3～4 | PASS |
| PUR-INV-UI-E03 | 正常／422／409／驗證／三語亮暗 | 模擬 API：MATCH 422 不變、再 MATCH 成 Disputed；未填決定逐行說明並聚焦且不送；拒絕＋修剪理由；鎖逾時錯誤顯示在提交鈕旁且保留輸入；成功退回草稿。斷言畫面訊息與實際 POST 路徑／版本／body 一致；zh-Hant 亮暗＋en＋zh-Hans 截圖。 | E | REQ_IO_A4.19.5～6 | PASS |
| PUR-INV-UI-NA2 | 不適用 | 批次／檔案：單張發票動作無批次或檔案。真實 API Playwright 屬 008c3c；真實 SQL MATCH／RESOLVE 已由 008a53／54 HTTP 整合測試驗收。 | — | — | N/A（已列原因） |

執行記錄：2026-11-26T23:11:02+08:00；測試先於實作撰寫（未在實作前單獨執行紅燈）；定向 Vitest 24/24、前端全量 1288/1288（後續 UX 修正後重跑）、ng build 成功、Playwright purchasing-invoice 3/3。

## WU-PUR-008c3a — 明細回 Disputed 逐行差異（2026-11-26T22:48:06+08:00）

| ID | 類別 | 情境／預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-INV-VIEW-I69 | 正常 | 訂單單價 2／稅率 20%，發票數量 4、單價 3：MATCH 成 Disputed 後 GET 明細第 1 行 `differences` 依序為 UNIT_PRICE 2→3（+1）、NET 8→12（+4）、TAX 1.6→2.4（+0.8）、GROSS 9.6→14.4（+4.8）；稅率相同不列。 | I | REQ_F4114.8.4 | PASS（真實 HTTP／SQL） |
| PUR-INV-VIEW-I70 | 狀態限制 | 同一發票拒絕後回 Draft v3：明細 `differences` 為空。 | I | REQ_F4114.8.4 | PASS |
| PUR-INV-VIEW-NA | 不適用 | 來源失效／金額無效回空陣列屬防禦分支（RESOLVE 已有 422 驗收 PUR-MATCH 系列）；未另造損壞來源資料。權限／他公司沿用 I67。 | - | - | 記錄 |

## WU-PUR-008a55 — 發票 GET 列表／明細（`PUR.INVOICE.VIEW`）

| ID | 類別 | 情境／預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-INV-VIEW-I64 | 正常 | 兩張發票（Disputed→拒絕成 Draft v3；零差異 Matched）：預設 `-businessDate` 列表回 2 筆、次序 A→B，摘要含狀態、版本、行數、含稅總額、業務日（閏日 2028-02-29）。 | I | REQ_F4114.8.1 | PASS（真實 HTTP／SQL） |
| PUR-INV-VIEW-I65 | 邊界／分頁 | `sort=totalGross` 反轉次序；`page=2&pageSize=1` 回第二筆且總數 2；狀態、關鍵字（供應商發票號）、日期範圍、供應商＋訂單篩選各命中正確；不存在供應商回空頁。 | I | REQ_F4114.8.1 | PASS |
| PUR-INV-VIEW-I66 | 錯誤 | 起日晚於迄日、未知排序欄、多欄排序、`pageSize=0`、`supplierId=0`、`purchaseOrderId=-1` 回 400；不存在 ID 回 404。 | I | REQ_F4114.8.1 | PASS |
| PUR-INV-VIEW-I67 | 權限 | 只有 CREATE／MATCH 的使用者列表與明細均 403；另一公司（具 VIEW）讀本公司發票 404、列表 0 筆。 | I | REQ_F4114.8.1 | PASS（真實 HTTP／SQL） |
| PUR-INV-VIEW-I68 | 正常／歷史 | 明細回保存行（收貨行 ID、數量 4、單價 3、含稅 14.4）、建立人、版本 3，及決定歷史（決定時版本 2、Reject、原因、GROSS 差額 4.8、決定人）。 | I | REQ_F4114.8.1, REQ_DS43.212 | PASS（真實 HTTP／SQL） |

2026-11-26T22:30+08:00：守衛 10.69 GiB PASS；先寫 I64～I68 真實 HTTP 測試（紅燈 GET 405），補 `PurchaseInvoiceQueryService`、`PUR.INVOICE.VIEW`、控制器 GET 後定向 1/1。

依 `myerp-test-scenarios` 覆核：重複與衝突——唯讀端點，不適用 409；版本於回應中供下一步動作帶入。部分結果——查詢無批次，不適用。狀態——查詢不受狀態限制，各狀態皆可讀（Draft／Matched 已驗）。已軟刪發票由全域軟刪篩選排除，DELETE 尚未實作（008a 後續），待 DELETE 單元補真實 SQL 情境。資料範圍：發票無分店／部門／擁有人欄位，窄於全公司的 VIEW 授權與寫入動作一致回 403（同 I49／I62 共同授權邊界）。檔案不適用。三語畫面、空狀態與錯誤提示 E2E 留 008c。

## WU-PUR-008a54 — RESOLVE 受權 HTTP 與真實 SQL 決定保存

| ID | 類別 | 情境／預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-RESOLVE-I58 | 正常 | Disputed（version=4）全部接受回 200 `Matched`、version 5；`matched_reserved` 保持 4；決定列保存原因與 GROSS 差額 4.8。 | I | REQ_F4114.8.4, REQ_DS43.212 | PASS（真實 HTTP／SQL） |
| PUR-RESOLVE-I59 | 狀態／回滾 | Disputed（version=2）拒絕回 200 `Draft`、version 3，本單預留 4 精確釋回為 0；原因去頭尾空白保存；之後可重新 MATCH。Draft 直接 RESOLVE 回 422。 | I | REQ_F4114.8.4 | PASS（真實 HTTP／SQL） |
| PUR-RESOLVE-I60 | 邊界／錯誤 | version=0、空 lines 回 400；不存在 ID 回 404；空白原因、憑空行號回 422 且零寫入（之後同版本仍可決定）。 | I | REQ_F4114.8.4 | PASS |
| PUR-RESOLVE-I61 | 衝突 | 已決定後以舊 version=2 重送回 409，不新增決定列；兩輪決定按版本各一列（2:Reject、4:Accept）。 | I | REQ_F4114.8.4 | PASS（真實 HTTP／SQL） |
| PUR-RESOLVE-I62 | 權限 | 只有 `PUR.INVOICE.MATCH` 的使用者 RESOLVE 回 403。 | I | REQ_F4114.8.4 | PASS |
| PUR-RESOLVE-I63 | 關聯／migration | `Purchasing_InvoiceResolution` 在 `myerp_test` Down 後表 0、發票列保留；Up 後表恢復。同公司複合 FK 限制刪除。 | I | REQ_DS43.212 | PASS，測試庫實測 |

2026-11-26T22:16:58+08:00：先寫 I58～I62 真實 HTTP 測試（建置紅燈 CS0712 名稱衝突、缺表時 PendingModelChanges），補實體、migration 後定向真實 HTTP／SQL 1/1；Debug 建置 0 警告／錯誤；後端全量 3949/3949（含 Architecture 權限清單加 `PUR.INVOICE.RESOLVE`）。新 migration 不屬第 19.5 節冒煙觸發條件。

依 `myerp-test-scenarios` 覆核：部分結果——單張整單決定，無批次部分成功／全部略過；不完整決定整單 422（BR-PUR-039a 離線單元測試已涵蓋缺行、重複行、非法動作、超長原因）。跨公司 404 與資料範圍窄於全公司 403 沿用 I49 共同授權邊界；鎖逾時 409 沿用 008a49 分類函式。全部接受時餘量不足 422 由 `ForResolve`／`CheckRequested` 單元測試涵蓋，真實 SQL 需他單同時預留，留 008a 併發驗收。檔案輸入不適用。三語畫面與 E2E 留 008c。

## WU-PUR-008a53 — MATCH 受權 HTTP 與真實 SQL 預留

| ID | 類別 | 情境／預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-MATCH-I55 | 正常／衝突 | 量足、零價稅差異的 Draft 以 version=1 MATCH 回 200 `Matched`；同版本重送回 409，不重複預留。 | I | REQ_F4114.8.2 | PASS（真實 HTTP／SQL） |
| PUR-MATCH-I56 | 邊界／錯誤 | version=0 回 400；不存在 ID 回 404，不進入交易。 | I | REQ_F4114.8.2 | PASS |
| PUR-MATCH-I57 | 部分結果／狀態 | 第二張發票請求 5（餘量 4）整單 422、零預留；改為 4 後以新版本 MATCH，價差回 200 `Disputed` 並預留。最終 `matched_reserved`=5、第一張仍 Matched。 | I | REQ_F4114.8.2 | PASS（真實 HTTP／SQL） |

依 `myerp-test-scenarios` 覆核：權限 403 與跨公司 404 沿用 I49 的共同授權邊界（MATCH 使用同一 `RequirePermission`＋全公司範圍檢查）；鎖逾時 409 由 008a49 同一分類函式覆蓋；單張整單操作，批次部分成功／全部略過及檔案輸入不適用。RESOLVE／POST 及三語畫面留 008a 後續與 008c。

2026-11-26T22:03:51+08:00：定向真實 HTTP／SQL 1/1；Debug 建置 0 警告／錯誤；後端全量 3948/3948（含 Architecture 權限清單更新為含 `PUR.INVOICE.MATCH`）。無 migration、啟動或前端變更，不觸發第 19.5 節真實啟動冒煙。

## WU-PUR-008a52 — Draft 競態、永久號碼與來源重驗

| ID | 類別 | 情境／預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-MATCH-I52 | 正常／重複／衝突 | 同公司同供應商兩個受權 HTTP 建立請求同時用同一正規號碼，恰一張成功、另一張 409；SQL 僅一表頭、一行、一活動持有列，無殘留寫入。 | I | REQ_F4114.8.2 | PASS（真實 HTTP／SQL） |
| PUR-MATCH-I53 | 重複／關聯／狀態 | Draft 改號後舊鍵永久保留，新鍵活動；他張重用任一鍵 409；本張可改回歷史鍵，新的舊鍵繼續保留。 | I | REQ_F4114.8.2～3 | PASS（真實 HTTP／SQL） |
| PUR-MATCH-I54 | 關聯／錯誤 | 訂單行物料身份漂移後，保存重新讀權威回 422，發票、行、號碼持有列筆數不變；恢復來源後可用同號重試。已刪收貨來源另由 I50 覆蓋。 | I | REQ_F4114.8.2～3 | PASS（真實 HTTP／SQL） |

依 `myerp-test-scenarios` 覆核：空 body、1001 行、六位量、閏日由 I50；401／403／跨公司 404 由 I49；非 Draft、舊版本 409 由 I47／I50。單張發票整單保存，批次部分成功與全部略過不適用；無檔案輸入。500／逾時的三語畫面與資料雙斷言留 008c；本單元只驗真實 SQL／HTTP，不宣稱畫面驗收。

2026-11-26T21:39:14+08:00：`InvoiceDraftConcurrentNumberClaimsAndSourceRecheckAreAtomic` 先紅（全新號碼錯回 `INVALID_NUMBER_SNAPSHOT`）、修後真實 HTTP／SQL 1/1。Debug 建置 0 警告／錯誤，後端全量 3947/3947。沒有改 migration、啟動流程或前端，故本單元不觸發第 19.5 節真實啟動冒煙測試。

## WU-PUR-008a51 — Draft CREATE／EDIT 受權 HTTP

2026-11-26T21:21:40+08:00 結案驗收：三樣本記憶體守衛 13.136／13.128／13.111 GiB PASS；`InvoiceDraftHttpUsesLockedSourcesAndPersistsIdentityAndVersion` 真實 HTTP／SQL 1/1 通過，覆蓋 I47～I50 的來源身份漂移、已刪來源、失敗零新增、最終 SQL 版本／來源行／含稅額。標準輸出 Debug 建置 0 警告／錯誤，後端全量 3946/3946 通過。隔離共享輸出目錄令架構測試載入非依賴 DocGen 失敗，改用各專案標準輸出目錄後 4 組全量通過；未改產品碼或測試斷言。I51 的畫面錯誤仍由 008c 驗收。

2026-11-26T17:21:22+08:00 續驗記錄：兩次三樣本 SQL 記憶體守衛最低 0.464／0.575 GiB，均 DEFER；未跑真實 HTTP／SQL、後端全量或離線測試。靜態核對 I47～I50 定向案例仍有身份漂移、已刪來源、失敗零新增及最終 SQL 版本／值斷言，待守衛 PASS 才執行，不更新待驗狀態。

2026-11-26T17:18:08+08:00 續驗記錄：兩次三樣本 SQL 記憶體守衛最低 0.297／0.190 GiB，均 DEFER；資源不足未重跑離線 Draft，也未跑真實 HTTP／SQL 或後端全量。靜態確認 I47～I50 定向案例的身份漂移、已刪來源、失敗零新增與最終 SQL 版本／值斷言仍在；沿用前輪離線 155/155 的歷史結果，不更新 I47～I50 的待驗狀態。

2026-11-26T17:13:58+08:00 續驗記錄：兩次三樣本 SQL 記憶體守衛最低 1.285／0.803 GiB，均 DEFER；離線 Draft 定向 155/155 通過。靜態核對 I47～I50 的已刪來源、身份漂移、最終 SQL 版本／值與失敗零寫入斷言；完整真實 HTTP／SQL 與後端全量未執行，不標 PASS。

2026-11-26T17:10:01+08:00 續驗記錄：兩次三樣本 SQL 記憶體守衛最低 1.339／1.247 GiB，均 DEFER；離線 Draft 定向 155/155 通過。I47～I50 的修後真實 HTTP／SQL 及後端全量未執行，仍待守衛 PASS，不標 PASS。

2026-11-26T17:06:15+08:00 續驗記錄：兩次三樣本 SQL 記憶體守衛最低 1.207／1.243 GiB，均 DEFER；離線 Draft 定向 155/155 通過。I47～I50 的修後真實 HTTP／SQL 及後端全量仍待守衛 PASS，不標 PASS。

2026-11-26T17:02:24+08:00 續驗記錄：兩次 SQL 記憶體守衛最低 1.455／1.186 GiB，均 DEFER；離線 Draft 定向 155/155 通過。未跑真實 HTTP／SQL 或後端全量，I47～I50 的修後完整斷言仍待守衛 PASS 後複驗，不標 PASS。

2026-11-26T16:58:10+08:00 續驗記錄：兩次完整 SQL 記憶體守衛均 DEFER，最低均 1.604 GiB；離線 Draft 定向 155/155 通過。按守衛規則未執行真實 HTTP／SQL 或後端全量；I47～I50 的身份漂移、已刪來源、最終 SQL 版本／值與零寫入仍待完整複驗，不標 PASS。

2026-11-26T16:53:39+08:00 續驗記錄：兩輪 SQL 記憶體守衛均 DEFER，最低 1.397／1.514 GiB；離線 Draft 定向 155/155 通過。按守衛規則未執行真實 HTTP／SQL 或後端全量；I47～I50 的身份漂移、已刪來源、最終 SQL 版本／值與零寫入仍待完整複驗，不標 PASS。

2026-11-26T16:49:57+08:00 續驗記錄：兩輪 SQL 記憶體守衛均 DEFER，最低 1.366／1.417 GiB；離線 Draft 定向 155/155 通過。按守衛規則未執行真實 HTTP／SQL 或後端全量；I47～I50 的身份漂移、已刪來源、最終 SQL 版本／值與零寫入仍待完整複驗，不標 PASS。

2026-11-26T16:46:20+08:00 續驗記錄：兩輪 SQL 記憶體守衛均 DEFER，最低 1.256／1.520 GiB；離線 Draft 定向 155/155 通過。按守衛規則未執行真實 HTTP／SQL 或後端全量；I47～I50 的身份漂移、已刪來源、最終 SQL 版本／值與零寫入仍待完整複驗，不標 PASS。

2026-11-26T16:42:34+08:00 續驗記錄：兩輪 SQL 記憶體守衛均 DEFER，最低 0.772／0.971 GiB；離線 Draft 定向 155/155 通過。按守衛規則未執行真實 HTTP／SQL 或後端全量；I47～I50 的修正後身份漂移、已刪來源、最終 SQL 版本／值與零寫入仍待完整複驗，不標 PASS。

2026-11-26T16:38:35+08:00 續驗記錄：三輪 SQL 記憶體守衛均 DEFER，最低 1.052／1.179／1.120 GiB；離線 Draft 定向 155/155 通過。按守衛規則未執行真實 HTTP／SQL 或後端全量；I47～I50 的修正後身份漂移、已刪來源、最終 SQL 版本／值與零寫入仍待完整複驗，不標 PASS。

2026-11-26T16:34:14+08:00 續驗記錄：三輪 SQL 記憶體守衛均 DEFER，最低 1.156／0.996／1.259 GiB；離線 Draft 定向 155/155 通過。按守衛規則未執行真實 HTTP／SQL 或後端全量；I47～I50 的修正後身份漂移、已刪來源、最終 SQL 版本／值與零寫入仍待完整複驗，不標 PASS。

2026-11-26T16:30:52+08:00 續驗記錄：三輪 SQL 記憶體守衛均 DEFER，最低 1.851／1.723／1.181 GiB；離線 Draft 定向 155/155 通過。按守衛規則未跑真實 HTTP／SQL 或後端全量；I47～I50 的身份漂移、已刪來源、最終 SQL 版本／值及零寫入仍待完整複驗，不標 PASS。

2026-11-26T16:27:16+08:00 續驗記錄：三輪 SQL 記憶體守衛均 DEFER，最低 2.086／1.997／2.040 GiB；離線 Draft 定向 155/155 通過。按守衛規則未跑真實 HTTP／SQL 或後端全量；I47～I50 的身份漂移、已刪來源、最終 SQL 版本／值及零寫入仍待完整複驗，不標 PASS。

2026-11-26T16:23:13+08:00 續驗記錄：三輪 SQL 記憶體守衛均 DEFER，最低 2.104／1.988／2.075 GiB；按規則未執行真實 SQL／HTTP 或後端全量。I47～I50 的修後完整定向案例仍待複驗，現有結果不提升為 PASS。

2026-11-26T16:16:10+08:00 續驗記錄：本輪三次 SQL 記憶體守衛均 DEFER，最低分別 2.129／2.265／2.352 GiB，均未達三樣本至少 2.5 GiB 的門檻；按守衛規則未執行 SQL／HTTP 或後端全量。離線 Draft 定向測試 155/155 通過。靜態核對 I47～I50 的真實 HTTP 與最終 SQL 斷言；上輪已見跨公司 EDIT 404，但修正後身份漂移及其後已刪來源、最終 SQL 值仍待完整定向測試複驗，不能標 PASS。

2026-11-26T16:11:01+08:00 續驗記錄：守衛先 DEFER（0.523～0.745 GiB），釋放本輪編譯程序後 PASS（最低 2.852 GiB）；整合測試隔離建置 0 警告／錯誤，再次 PASS（最低 2.679 GiB）後真實 HTTP 驗證跨公司 EDIT 404、CREATE／EDIT null body 400、無權 403、非 Draft 422、1001 行 422 等均走過。測試在物料身份漂移的精確錯誤碼斷言紅燈：實際 `INVALID_SOURCE_IDENTITY`，原預期 `INVALID_AMOUNT_SOURCE`；依來源身份先於金額來源的規則修正測試預期，保留 422 與零寫入斷言。修正後再建置 0 警告／錯誤，但守衛三輪 DEFER（最低 2.405／2.415／2.305 GiB），故完整案例、最終 SQL 值與後端全量仍待複驗；I47～I50 保持待驗。

2026-11-26T12:21:27+08:00 續驗記錄：SQL 記憶體守衛曾 PASS（最低 2.800 GiB），真實 HTTP 定向測試執行到跨公司 EDIT：CREATE／EDIT null body 400 已通過，但跨公司 EDIT 預期 404、實得 500（SQL session 將缺失訂單哨兵誤作作用域錯誤）。已修正此分類，採購模組隔離建置 0 警告／錯誤。修後守衛持續 DEFER，原定向案例與後端全量仍待重跑；I47～I50 不標為通過。

| ID | 類別 | 情境／預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-MATCH-U171 | 正常／來源 | 命令在交易開始與權威讀取後組裝，一次 Prepare、Save、Commit；真實 SQL 指派 IDENTITY。 | U,I | REQ_F4114.8.2 | U PASS；I PASS |
| PUR-MATCH-U172 | 關聯／錯誤 | 鎖內缺少收貨來源時，Prepare 前回滾；HTTP 非法來源 422，零新增。 | U,I | REQ_F4114.8.2～3 | U PASS；I PASS |
| PUR-MATCH-I47 | 正常／版本／稽核 | POST 201 返回 SQL ID 與版本 1；EDIT 即使表頭不變仍替換行、版本加一；舊版本 409；表頭、行、永久號碼列各一且無重複；SQL 最終版本、來源行及含稅額與回應一致。 | I | REQ_F4114.8.2～3 | PASS（真實 HTTP／SQL，含最終值） |
| PUR-MATCH-I48 | 重複／軟刪 | 同供應商號碼重複 409；已軟刪號碼仍永久持有。 | I | REQ_F4114.8.2 | 重複 PASS；軟刪由 008a50 SQL 測試 PASS |
| PUR-MATCH-I49 | 權限／跨公司／範圍 | 匿名 401、無授權 403、本人範圍 403；不存在或他公司 EDIT ID 404。 | I | REQ_F4114.8.2～3 | PASS（完整真實 HTTP 案例） |
| PUR-MATCH-I50 | 邊界／狀態／關聯 | CREATE／EDIT 缺少整個 body 回 400 共用驗證碼；1000 行上限、6 位數量、閏日；非 Draft、已刪來源、無效權威價格拒絕且無變更。 | U,I | REQ_F4114.8.2～3 | PASS（空 body、1001 行、非 Draft、已刪來源、來源身份漂移及零寫入；非法價稅值由資料表 CHECK 與離線 U 測試保護） |
| PUR-MATCH-I51 | 錯誤 | 400、409、422、500、逾時在 API 正確分類，後續畫面三語顯示原因並雙斷言資料。 | I,E | REQ_F4114.8.2～3 | 409／422 I PASS；其餘及 E 待 008c |
| PUR-MATCH-047 | 部分結果／檔案 | 單張 Draft 整單保存，沒有批次、略過、上傳檔案；部分成功、全部略過、檔案格式／編碼不適用。 | — | REQ_F4114.8.2～3 | N/A（已列原因） |

2026-11-26T12:09:44+08:00 續驗記錄：守衛三輪最低 2.072／2.059／1.709 GiB，均 DEFER；離線 Draft 定向 155/155。I47～I50 新斷言和 null body 400 修正未執行真實 HTTP／SQL，故結果欄仍維持待驗；後端全量亦未跑。下輪守衛 PASS 後先跑 `InvoiceDraftHttpUsesLockedSourcesAndPersistsIdentityAndVersion`。

2026-11-26T12:04:27+08:00 續驗記錄：SQL 記憶體守衛四輪最低 1.573／2.253／2.450／2.381 GiB，均 DEFER；離線 Draft 定向 155/155。靜態核對 I47～I50 既有斷言，沒有執行真實 HTTP／SQL 或後端全量；null body 400 修正與新增斷言保持待驗。守衛 PASS 後執行 `InvoiceDraftHttpUsesLockedSourcesAndPersistsIdentityAndVersion`。

2026-11-26T11:57:47+08:00 續驗記錄：SQL 記憶體守衛三輪最低 2.243／2.172／2.183 GiB，均 DEFER；沒有執行 I47～I50 新增的真實 HTTP／SQL 斷言，也未把 null body 400 修正標為 PASS。離線 Draft 定向 155/155；PASS 後先跑單一定向 HTTP 案例，再跑低並行後端全量。

2026-11-26T11:51:30+08:00 續驗記錄：定向真實 HTTP 首跑在 CREATE null body 斷言紅燈（實際 422 `PUR.INVOICE.INVALID_HEADER`，預期 400）。授權後缺少 body 已改為共用 400 驗證例外，補 CREATE／EDIT 精確錯誤碼；隔離整合測試建置 0 警告／錯誤。修後記憶體守衛三輪最低 2.037／1.760／1.833 GiB，均 DEFER；修正與其餘新增 SQL 斷言尚未通過真實複驗。

2026-11-26T11:40:02+08:00 續驗記錄：SQL 記憶體守衛四輪最低 2.353／2.273／2.135／2.119 GiB，均 DEFER；離線 Architecture 359/359 通過。I47／I49／I50 新斷言仍未執行，不標 PASS；I51 的 500／逾時與畫面提示仍由後續畫面切片驗收。

2026-11-26T11:35:51+08:00 續驗記錄：補 I47 真實 SQL 最終版本 2、收貨行 ID、含稅額 2.4 與 HTTP 回應／輸入一致的斷言；隔離輸出建置 0 警告／錯誤。守衛兩輪最低 1.703／1.110 GiB，均 DEFER；新斷言、先前 I49～I50 新增斷言及後端全量仍待真實執行，不標 PASS。

2026-11-26T11:31:02+08:00 續驗記錄：價格來源身份漂移的 HTTP 斷言補上精確錯誤碼 `PUR.INVOICE.INVALID_AMOUNT_SOURCE`，隔離輸出建置 0 警告／錯誤。記憶體守衛四輪最低 2.067／2.354／2.252／1.002 GiB，均 DEFER；新斷言及後端全量未跑，不標 PASS。

2026-11-26T11:24:43+08:00 續驗記錄：補訂單行物料變更後價格來源身份不再對應收貨行的 CREATE 422 與發票筆數維持 1 的真實 HTTP 斷言；原表 CHECK 阻止非法數值價格落庫，故不停用約束製造假資料。整合測試專案隔離輸出建置 0 警告／錯誤。守衛三輪最低 1.597／1.421／1.037 GiB，均 DEFER；新增斷言未跑 SQL。下輪守衛 PASS 後先跑定向 HTTP，再按回應修正分類與斷言，低並行跑後端全量。

2026-11-26T11:19:36+08:00 續驗記錄：null JSON body 400、1001 行 422、兩者零新增，以及 Matched 發票 EDIT 422、已刪 Posted 收貨來源 CREATE 422、失敗後發票／行／持有列筆數不變的整合斷言已補並以隔離輸出編譯零警告／錯誤。守衛兩輪 2.015／2.007／2.010 與 1.036／1.035／0.958 GiB 均 DEFER；新斷言未執行，I50 不標 PASS。下輪守衛 PASS 後先跑定向測試，如分類不符先按真實回應修正；再補無效價格、完整失敗零寫入與全量後端。

2026-11-26T11:13:18+08:00 續驗記錄：守衛首輪 2.545／2.410／2.402 GiB、建置後 1.161／1.123／1.112 GiB，均 DEFER。跨公司 EDIT 404 情境已加入整合測試，受權切到另一公司且有 EDIT 權限時仍應只見 404；隔離輸出的整合測試專案建置零警告／錯誤，離線 Draft 定向 155/155。此新增情境尚未執行真實 SQL，不標 PASS。下一輪先重測守衛，達標後跑該測試及其餘 I49～I51 邊界、狀態、零寫入斷言，再低並行全量回歸。

2026-11-26T11:07:53+08:00 續驗記錄：SQL 記憶體守衛 0.887／0.858／0.838 GiB，判定 DEFER；因此本輪未將 I49～I51 的待補情境標為通過。離線 Architecture 2296/2296 通過；解決方案建置遇既有 `MyERP.Api.exe`／`devenv.exe` 鎖住 API 輸出 DLL，未完成全量建置。


## WU-PUR-008a50 — 來源、Draft、永久號碼表真實 SQL 結構

| ID | 類別 | 情境／預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-INV-SCHEMA-001 | 正常／關聯 | 四張來源表、Draft 表頭／行與號碼持有表均由 migration 建立；同公司訂單／收貨／發票關聯與來源查詢索引存在；008a55i 起含列表日期索引 `ix_pur_purchase_invoice_company_id_business_date_id`。 | I | REQ_F4114.8.2～3；REQ_DS43.183～188、211 | PASS |
| PUR-INV-VIEW-U71 | 效能／邊界 | 008a55i：發票表頭 EF 模型有且只有一個非唯一 `(CompanyId,BusinessDate,Id)` 索引，對應列表預設排序。 | U | REQ_F4114.8.1；REQ_DS43.184 | PASS |
| PUR-INV-VIEW-U72 | 部分結果／回滾 | 008a55i：`Purchasing_InvoiceListDateIndex` Up 只建立該索引、Down 只刪除該索引，不動資料。 | U | REQ_DS43.184 | PASS |
| PUR-INV-SCHEMA-002 | 邊界 | 同公司複合鍵與唯一行號索引保留；空值、六位量／價、日期邊界由既有模型及輸入規則測試覆蓋。 | U,I | REQ_F4114.8.2～3 | 索引 PASS；輸入沿用既有測試 |
| PUR-INV-SCHEMA-003 | 重複／衝突 | 發票軟刪後同公司／供應商仍不得重用號碼；唯一索引 2601／2627。併發與版本 409 留 008a52。 | I | REQ_F4114.8.2～3 | 軟刪佔號 PASS；競態待續 |
| PUR-INV-SCHEMA-004 | 部分結果 | migration Down／Up 往返後訂單、發票、持有列各保留一筆，關聯恢復；單張 Draft 原子保存與全部略過不屬於 schema 切片。 | I | REQ_F4114.8.2～3 | PASS，測試庫實測 |
| PUR-INV-SCHEMA-005 | 權限／跨公司 | 複合 FK 阻止跨公司參照；HTTP 401／403、資料範圍留 008a51。 | I,E | REQ_F4114.8.2～3 | FK PASS；HTTP 待續 |
| PUR-INV-SCHEMA-006 | 狀態／關聯 | 已軟刪表頭仍保留號碼持有者；已過帳／非 Draft 禁止修改由後續保存服務及 HTTP 驗收。 | I,E | REQ_F4114.8.2～3 | 佔號 PASS；狀態待續 |
| PUR-INV-SCHEMA-007 | 錯誤 | FK 與唯一索引拒絕無效參照及重複鍵；400／409／422／500／逾時畫面及回滾留 008a51／52／008c。 | I,E | REQ_F4114.8.2～3 | 唯一鍵 PASS；HTTP 待續 |
| PUR-INV-SCHEMA-008 | 檔案 | 不適用：migration 與 Draft 不收檔案。 | — | REQ_F4114.8.2～3 | 不適用 |

真實 SQL：`PurchaseInvoiceSchemaTests` 先因缺兩個發票來源 FK 紅燈，補 migration 與 EF 模型後 2/2。`myerp_test` 新 migration Down 後資料 1／1／1、來源 FK 0；Up 後資料仍 1／1／1、來源 FK 2。來源四表、Draft 與持有表均由測試 fixture 的完整 migration Up 建立；沒有對正式 `myerp` 套用。

## WU-PUR-008a48 — Draft 保存服務請求邊界

| ID | 類別 | 情境／預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-INV-BOUND-001 | 正常 | 建立使用正數暫存 ID，已驗快照進入鎖內協調並返回實際 ID；修訂需正數發票 ID 與期望版本。 | U | REQ_F4114.8.2～3 | 建立 PASS；修訂入口由參數拒絕測試及既有修訂意圖測試覆蓋 |
| PUR-INV-BOUND-002 | 邊界 | 建立暫存 ID 零、建立卻帶期望版本、修訂 ID 零或負數、修訂缺失／零／負版本、修訂卻帶暫存 ID 均在 session 前拒絕。 | U | REQ_F4114.8.2～3 | PASS |
| PUR-INV-BOUND-003 | 重複／衝突 | 同來源多行去重；已軟刪號碼、併發唯一索引及舊版 409 在鎖內／真實 SQL 處理。 | U,I | REQ_F4114.8.2～3 | 去重 PASS；SQL 待 008a |
| PUR-INV-BOUND-004 | 部分結果 | 無效身份、空行或無效收貨 ID 整單拒絕且不建 session；單張保存沒有部分成功或略過。 | U | REQ_F4114.8.2～3 | 身份及既有空行 PASS；其他既有驗證 |
| PUR-INV-BOUND-005 | 權限／狀態／關聯 | 跨公司、無權限、已刪／非 Draft 與來源失效仍須鎖內及受權 HTTP 驗收；入口不信客戶端身份。 | I,E | REQ_F4114.8.2～3 | 待 008a／008c |
| PUR-INV-BOUND-006 | 錯誤 | 400／409／422／500／逾時及回滾的畫面、資料一致性由 HTTP／E2E 驗；本服務錯誤不建立 session。 | U,I,E | REQ_F4114.8.2～3 | 入口 PASS；HTTP 待 008a／008c |
| PUR-INV-BOUND-007 | 檔案 | 不適用：Draft 保存服務不收檔案。 | — | REQ_F4114.8.2～3 | 不適用 |

執行記錄：2026-11-26T06:01:46+08:00；入口測試先紅（建立帶版本、無效幣別於 session 後失敗），修正後定向 18/18、Architecture 2285/2285、DocGen 22/22、Debug 建置零警告／錯誤。記憶體守衛 1.594／1.593／1.672 GiB DEFER，真實 SQL／HTTP 與競態仍待 008a。

## WU-PUR-008a47 — Draft 保存服務接線

| ID | 類別 | 情境／預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-INV-WIRE-001 | 正常／關聯 | 已驗快照的公司、供應商、訂單與去重收貨 ID 進入 session；一次保存後提交並返回實際 ID。 | U | REQ_F4114.8.2 | PASS |
| PUR-INV-WIRE-002 | 邊界 | 無效公司或空行不建立 session；閏日日期沿用已驗快照。數值與行數上限由 Draft 輸入驗證測試涵蓋。 | U | REQ_F4114.8.2 | 公司 PASS；其他沿用既有測試 |
| PUR-INV-WIRE-003 | 重複／衝突 | 同來源多行只讀一個來源 ID；已軟刪號碼與併發 409 由持有列及真實 SQL 測試驗。 | U,I | REQ_F4114.8.2～3 | 去重 PASS；SQL 待 008a |
| PUR-INV-WIRE-004 | 部分結果／錯誤 | 來源價稅漂移在準備前整單回滾；零列保存回滾並釋放 session，無部分成功或全部略過。 | U | REQ_F4114.8.2 | PASS |
| PUR-INV-WIRE-005 | 權限／狀態 | 跨公司、無權限、非 Draft 及版本衝突在 HTTP 與 SQL 實際保存驗證。 | I,E | REQ_F4114.8.2～3 | 待 008a／008c |
| PUR-INV-WIRE-006 | 錯誤 | 400／409／422／500／逾時的訊息及保存資料一致性。 | I,E | REQ_F4114.8.2～3 | 待 008a／008c |
| PUR-INV-WIRE-007 | 檔案 | 不適用：Draft 保存服務不收檔案。 | — | REQ_F4114.8.2 | 不適用 |

執行記錄：2026-11-12T16:23:37+08:00；缺少 factory 型別先 CS0246 紅燈，接線測試 4/4、Architecture 1938/1938、PUR Debug 零警告／錯誤。記憶體守衛 2.441／2.449／2.430 GiB DEFER，真實 SQL 和 HTTP 未驗。

## WU-PUR-008a46 — Draft 權威來源資料模型與讀取

| ID | 類別 | 情境／預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-INV-SOURCE-001 | 正常／關聯 | 四表同公司複合關聯，訂單行保存單價與稅率；讀取器依訂單、收貨、價稅順序取得同公司來源。 | U,I | REQ_F4114.8.2～3 | U 1/1；I 待守衛 PASS |
| PUR-INV-SOURCE-002 | 邊界 | 空、負數或重複收貨 ID 在查詢前拒絕；來源缺漏整單拒絕。 | U,I | REQ_F4114.8.2 | U 沿用 READ-002；I 待驗 |
| PUR-INV-SOURCE-003 | 重複／衝突 | 同供應商兩次保存與來源量競態只容一筆；已軟刪訂單或收貨不可開票。 | I | REQ_F4114.8.2 | 待 008a 真實 SQL |
| PUR-INV-SOURCE-004 | 部分結果 | 單張發票原子保存，任一來源失效不產生部分成功、全部略過結果。 | U,I | REQ_F4114.8.2 | U 既有規則；I 待驗 |
| PUR-INV-SOURCE-005 | 權限／狀態 | 跨公司、非 Approved 訂單、非 Posted 收貨與已刪來源整單拒絕；HTTP 403／404 待端點。 | I,E | REQ_F4114.8.2 | 待 008a／008c |
| PUR-INV-SOURCE-006 | 錯誤 | 400／409／422／500／逾時均保證零寫入，畫面與保存資料一致。 | I,E | REQ_F4114.8.2 | 待 008a／008c |
| PUR-INV-SOURCE-007 | 檔案 | 不適用：來源讀取不收檔案。 | — | REQ_F4114.8.2 | 不適用 |

執行記錄：2026-11-12T16:14:55+08:00，先以缺模型 CS0246 紅燈；資料模型定向 1/1、Architecture 1934/1934、Debug 解決方案零警告／錯誤。記憶體守衛 1.699／1.697／1.635 GiB DEFER，真實 SQL Up／Down、HTTP 和競態未驗。

## WU-PUR-008a45 — Draft 鎖內讀取契約

| ID | 類別 | 情境／預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-INV-READ-001 | 正常 | 訂單、排序收貨、價稅固定次序讀取；完整號碼持有列含歷史鍵，修訂讀全部行 | U、I | REQ_F4114.8.2～3 | U 通過；I 待 SQL |
| PUR-INV-READ-002 | 邊界 | 空、非正數、重複收貨 ID 在讀取前拒絕 | U | REQ_F4114.8.2 | 通過 |
| PUR-INV-READ-003 | 重複與衝突 | 已軟刪號碼仍佔用；並發號碼碰撞由未過濾唯一索引拒絕 | I | REQ_F4114.8.2 | 待 SQL |
| PUR-INV-READ-004 | 部分結果 | 單張發票保存不可部分成功；任一來源缺漏整單拒絕 | U、I | REQ_F4114.8.2 | 既有離線規則；I 待 SQL |
| PUR-INV-READ-005 | 權限 | 無權限和跨公司不能取得另一公司表頭、行或持有列 | I、E | REQ_F4114.8.2 | 待 HTTP |
| PUR-INV-READ-006 | 狀態／關聯 | 已軟刪、非 Draft、版本不符的修訂須拒絕；來源停用亦拒絕 | U、I | REQ_F4114.8.2 | 既有離線規則；I 待 SQL |
| PUR-INV-READ-007 | 錯誤 | 400／409／422／500／逾時回滾，畫面顯示正確訊息 | I、E | REQ_F4114.8.2～3 | 待 SQL／HTTP |
| PUR-INV-READ-008 | 檔案 | 不適用：Draft 讀取不收檔案 | — | REQ_F4114.8.2 | 不適用 |

執行記錄：2026-11-12T16:06:48+08:00；SQL 守衛 DEFER；定向 7/7、Architecture 1933/1933、PUR Debug 零警告。來源介面尚無持久化實作，真實 SQL、HTTP 與 E2E 未驗。

## WU-PUR-008a39 — Draft EF 持有列套用集合

| ID | 類別 | 情境／預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-MATCH-U143 | 正常／邊界／關聯 | 新建 Draft 交回唯一待新增活動鍵；完整既有與變更追蹤列皆為空。 | U | REQ_F4114.8.2 | PASS |
| PUR-MATCH-U144 | 正常／狀態／關聯 | 改回本張歷史鍵交回舊活動列及歷史列的追蹤參照，兩列均已按序套用；沒有待新增列。 | U | REQ_F4114.8.3 | PASS |
| PUR-MATCH-U145 | 正常／邊界／部分結果 | 原號修訂交回未變的完整既有列；新增及變更集合為空，單張單據不適用批次部分成功。 | U | REQ_F4114.8.3 | PASS |
| PUR-MATCH-033 | 重複／衝突／權限／跨公司／錯誤／檔案 | 漂移或後段錯誤不交部分集合，由 U142 覆蓋；真實 403／404、409、422、500、逾時及 SQL 競態待 008a，畫面與資料雙斷言待 008c。無檔案，不適用檔案情境。 | U,I,E | REQ_F4114.8.2～3 | U PASS；I/E TODO |

執行記錄：2026-11-12T11:37:15+08:00，先 CS0815 紅燈；守衛 1.553／1.644／1.645 GiB DEFER，定向 33/33。SQL／HTTP 待 008a。

## WU-PUR-008a38 — Draft EF 完整預檢後套用

| ID | 類別 | 情境／預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-MATCH-U141 | 正常／狀態／關聯 | Draft 改號的共同計畫先驗版本及行，再依序停用舊活動鍵並保留新增活動鍵。 | U | REQ_F4114.8.2～3 | PASS |
| PUR-MATCH-U142 | 邊界／重複／衝突／錯誤 | 後段新增鍵或零變更歷史列漂移、或表頭版本漂移時，整體套用拒絕，先前活動持有列保持原值。 | U | REQ_F4114.8.2～3 | PASS |
| PUR-MATCH-032 | 權限／跨公司／部分結果／HTTP 錯誤／檔案 | SQL 403／404、409、422、500、逾時和同交易回滾待 008a；畫面及資料雙斷言待 008c。單張非批次且沒有檔案，部分成功／略過及檔案不適用。 | I,E | REQ_F4114.8.2～3 | TODO（008a／008c） |

執行記錄：2026-11-12T11:32:15+08:00，先 CS1061 紅燈；守衛 1.434／1.408／1.415 GiB DEFER，定向 1/1、Architecture 1902/1902、PUR Debug 零警告，真實 SQL／HTTP 待 008a。

## WU-PUR-008a37 — Draft EF 保存前漂移檢查

| ID | 類別 | 情境／預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-MATCH-U138 | 正常／邊界／狀態／關聯 | 新建共同計畫未變更可通過；表頭版本或行來源在計畫後漂移即整單拒絕。 | U | REQ_F4114.8.2～3 | PASS |
| PUR-MATCH-U139 | 重複／衝突／關聯 | 修訂保留原號時，零變更持有列的活動狀態漂移亦拒絕。 | U | REQ_F4114.8.3 | PASS |
| PUR-MATCH-U140 | 正常／錯誤 | 新建活動持有列鍵在計畫後漂移時拒絕，不套用部分變更。 | U | REQ_F4114.8.2 | PASS |
| PUR-MATCH-031 | 權限／跨公司／部分結果／後端錯誤／檔案 | 公司及身份由共同計畫核對；真實 403／404、409、422、500、逾時與交易回滾待 008a，畫面與資料雙斷言待 008c。單張非批次且無檔案，故部分結果及檔案不適用。 | I,E | REQ_F4114.8.2～3 | TODO（I／E 待父項） |

執行記錄：2026-11-12T11:23:37+08:00，守衛 1.757／2.313／2.287 GiB DEFER；CS1061 紅燈後定向 28/28，後續全量 Architecture 驗證見本輪進度。SQL／HTTP 未驗。

## WU-PUR-008a36 — Draft 實體與持有列共同組裝

| ID | 類別 | 情境／預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-MATCH-U136 | 正常／邊界／關聯 | 新建單一意圖、正數指派 ID 形成表頭、行及一筆活動持有列，身份與鍵完全相同；零 ID 拒絕。 | U | REQ_F4114.8.2 | PASS |
| PUR-MATCH-U137 | 狀態／重複／衝突 | Draft 修訂保留保存 ID 與版本；錯 ID、同範圍重複持有鍵快照整單拒絕，不返回部分計畫。 | U | REQ_F4114.8.3 | PASS |
| PUR-MATCH-030 | 權限／跨公司／部分結果／錯誤／檔案 | 公司及身份離線核對已由 U136～137 與前置意圖驗證；SQL 403／404、409、422、500、逾時和原子回滾待 008a，三語畫面待 008c。單張非批次且無檔案。 | I,E | REQ_F4114.8.2～3 | TODO（部分結果、檔案 N/A） |

執行記錄：2026-11-12T11:17:17+08:00，守衛 1.266／1.274／1.219 GiB DEFER；先 CS1061 紅燈，定向 2/2。真實 SQL／HTTP 未驗。

## WU-PUR-008a35 — 已驗號碼持有列 EF 變更

| ID | 類別 | 情境／預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-MATCH-U132 | 正常／邊界／關聯 | 新建以已驗公司、供應商、指派發票 ID 和正規鍵建立一個活動 EF 實體；其他範圍同鍵可存在。 | U | REQ_F4114.8.2 | PASS |
| PUR-MATCH-U133 | 正常／狀態 | 改號先停用本張舊活動列，再新增新活動列；改回本張歷史鍵回啟既有列；原號修訂零變更，保留所有歷史鍵。 | U | REQ_F4114.8.2～3 | PASS |
| PUR-MATCH-U134 | 重複／衝突／跨公司／關聯 | 他張歷史鍵佔號回 409；同一快照重複列 ID、同張跨公司／供應商或錯發票 ID 拒絕且無部分變更。 | U | REQ_F4114.8.2～3 | PASS |
| PUR-MATCH-U135 | 錯誤／併發 | EF 追蹤列在計畫後改變身份、號碼鍵或活動旗標，Apply 拒絕；真實雙連線競態及 SQL 2601／2627 轉 409 待驗。 | U,I | REQ_F4114.8.2～3 | U PASS；I 待 008a |
| PUR-MATCH-029 | 權限／部分結果／錯誤／介面／檔案 | SQL 鎖內追蹤、同交易回滾、403／404、400／409／422／500／逾時及三語畫面／資料雙斷言由 008a／008c 驗；單張原子保存無批次部分結果或檔案。 | I,E | REQ_F4114.8.2～3 | TODO（批次、檔案 N/A） |

執行記錄：2026-11-12T11:10:02+08:00，守衛 2.187／1.951／1.867 GiB DEFER；先 CS0103 紅燈，定向 5/5、Architecture 1896/1896、PUR Debug 零警告。SQL／HTTP 未驗。

## WU-PUR-008a32 — Draft 表頭與行離線模型

| ID | 類別 | 情境／預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-MATCH-U126 | 正常／邊界 | 表頭與行欄位具正確長度、小數精度、BIN2 號碼鍵；行對同公司表頭有複合 FK，行號唯一，同來源可多行。 | U | REQ_F4114.8.2～3, REQ_DS43.184～185 | PASS（模型） |
| PUR-MATCH-U127 | 關聯／錯誤／回滾 | migration 建表順序為表頭再行；Down 先行後表頭；行來源只存 ID、無跨模組 FK。 | U,I | REQ_F4114.8.2～3, REQ_DS43.184～185 | U PASS；I 待 008a |
| PUR-MATCH-020 | 重複／衝突／狀態／權限／部分結果／檔案 | 唯一行號、軟刪仍佔號、版本及狀態、跨公司、400／409／422／500／逾時、原子整單回滾及畫面／資料雙斷言由 008a／008c 驗；單張非批次且無檔案。 | I,E | REQ_F4114.8.2～3, REQ_F4114.8.6 | TODO（部分結果、檔案 N/A） |

執行記錄：2026-11-12T07:07:30+08:00，SQL 守衛 1.732／1.723／1.718 GiB DEFER；缺型別 CS0246 紅燈，定向 2/2、Architecture 1876/1876、PUR 與 migration Debug 零警告。真實 SQL／HTTP 未驗。

## WU-PUR-008a31 — Draft 保存意圖與持有列計畫接線

| ID | 類別 | 情境／預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-MATCH-U123 | 正常／邊界 | 新建以已驗公司、供應商、正規鍵及指派正數 ID 輸出唯一 InsertActive；其他公司或供應商可使用同鍵。 | U | REQ_F4114.8.2 | PASS |
| PUR-MATCH-U124 | 重複／衝突／關聯 | 他張活動或歷史鍵佔號回 409；新建 ID 已有任何列、同範圍重複鍵、非正規鍵或缺快照拒絕，整單無計畫。 | U | REQ_F4114.8.2, REQ_F4114.8.6 | PASS |
| PUR-MATCH-U125 | 狀態／身份／歷史 | 修訂只能用保存 ID，改號保留舊及歷史鍵，活動舊鍵缺失拒絕；既有 Draft／版本守衛沿用 U98。 | U | REQ_F4114.8.2～3 | PASS |
| PUR-MATCH-019 | 權限／錯誤／部分結果／檔案 | 真實 SQL 鎖、權限、HTTP 400／409／422／500／逾時及畫面／資料雙斷言待 008a／008c；單張原子保存無批次部分結果或檔案。 | I,E | REQ_F4114.8.2～3 | TODO（批次、檔案 N/A） |

執行記錄：2026-11-12T06:59:52+08:00，守衛 2.198／2.153／2.156 GiB DEFER；缺接線方法 CS1061 紅燈，定向 23/23、Architecture 1874/1874、PUR Debug 零警告。真實 SQL／HTTP 未驗。

## WU-PUR-008a30 — Draft 改號持有列離線寫入順序

| ID | 類別 | 情境／預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-MATCH-U120 | 正常／邊界／狀態 | Draft 改新號先停用舊活動鍵再插入新鍵；歷史鍵全部保留；原號修訂零持有列變更。 | U | REQ_F4114.8.2～3 | PASS |
| PUR-MATCH-U121 | 正常／重複／關聯 | 改回本張歷史號時啟用既有列，不插重複鍵；他張佔用新號含非活動鍵回 409。 | U | REQ_F4114.8.2～3, REQ_F4114.8.6 | PASS |
| PUR-MATCH-U122 | 衝突／跨公司／錯誤 | 缺或多個活動列、表頭舊鍵不一致、同張跨範圍、非正規鍵拒絕；僅指定永久號碼唯一索引的 2601／2627 轉重號。 | U | REQ_F4114.8.2～3, REQ_F4114.8.6 | PASS |
| PUR-MATCH-018 | 權限／部分結果／檔案／介面 | 單張原子改號無批次部分成功或檔案；真實 SQL 交易、權限、HTTP 409／500／逾時與三語畫面及資料雙斷言留 008a／008c。 | I,E | REQ_F4114.8.2～3 | TODO（批次、檔案 N/A） |

執行記錄：2026-11-12T06:53:25+08:00，守衛 1.864／1.851／1.840 GiB DEFER；先後定向 8/8、補邊界後 Architecture 1871/1871、PUR Debug 零警告；真實 SQL／HTTP 未驗。

## WU-PUR-008a29 — 發票號永久持有模型與 migration

| ID | 類別 | 情境／預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-MATCH-U117 | 正常／邊界／關聯 | 持有表允許同張多個不同鍵；鍵為 NVARCHAR(80)、二進位定序，只有一個活動鍵；發票表尚未建，暫無同模組 FK。 | U | REQ_F4114.8.2～3 | PASS（模型） |
| PUR-MATCH-U118 | 重複／衝突／狀態 | 公司＋供應商＋鍵唯一索引不過濾軟刪；同張重複鍵拒絕；軟刪及改號舊鍵仍佔號。 | U,I | REQ_F4114.8.2, REQ_F4114.8.6 | U PASS（模型與 migration）；I 待 008a |
| PUR-MATCH-U119 | 錯誤／回滾 | migration Down 移除本表；實際 SQL Up／Down、重號競態與唯一衝突轉 409 驗收。 | U,I | REQ_F4114.8.2 | U PASS；I 待 008a |
| PUR-MATCH-017 | 權限／跨公司／部分結果／介面／檔案 | 真實保存權限與跨公司、400／409／422／500／逾時畫面及資料一致性由 008a／008c 驗；單張保存無批次部分成功／全部略過，也無檔案。 | I,E | REQ_F4114.8.2～3 | TODO（批次、檔案 N/A） |

執行記錄：2026-11-12T06:44:18+08:00，SQL 記憶體守衛 1.393／1.385／1.396 GiB DEFER；Architecture 1861/1861、PUR 與 migration Debug 建置零警告。SQL 實際套用、交易及 HTTP 未驗。

## WU-PUR-008a25 — Draft 修訂號碼鍵一致性

| ID | 類別 | 情境／預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-MATCH-U101 | 正常／邊界 | 保存鍵與本張持有者一致時可保留原號；改用未佔用新號時，意圖保留原發票 ID／版本並寫新鍵。 | U | REQ_F4114.8.2～3 | PASS |
| PUR-MATCH-U102 | 重複／衝突 | 改號後新鍵由另一張含軟刪發票持有時回 409；本張 ID 不豁免他張。 | U | REQ_F4114.8.2～3 | PASS |
| PUR-MATCH-U103 | 關聯／權限／跨公司 | 保存鍵與持有者鍵不同、鍵非正規、缺持有者、持有者已刪、公司或供應商不符，整單回失效快照；真實權限與跨公司 HTTP 留 008a。 | U,I | REQ_F4114.8.2～3 | U PASS；I 待 008a |
| PUR-MATCH-U104 | 狀態／部分結果／錯誤／檔案 | Draft 與版本守衛沿用 U98；單張原子保存沒有部分成功、全部略過，無檔案。400／409／422／500／逾時畫面與資料雙斷言留 008c。 | U,E | REQ_F4114.8.2～3 | U PASS；E 待 008c（批次、檔案 N/A） |

執行記錄：2026-11-12T06:20:02+08:00，守衛 1.756／1.752／1.749 GiB DEFER；CS1729 紅燈，定向 17/17、Architecture 1854/1854、PUR Debug 零警告。SQL／HTTP、權限、唯一索引和交易仍屬 008a。

## WU-PUR-008a24 — Draft 修訂保存離線預檢

| ID | 類別 | 情境／預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-MATCH-U97 | 正常／邊界 | 閏日 Draft 修訂沿用已保存發票 ID／版本；相同持有者可留原號，已驗身份與待寫入意圖相同。 | U | REQ_F4114.8.3 | PASS |
| PUR-MATCH-U98 | 狀態／衝突 | 待審、已過帳、已刪拒絕；舊版本或零版本回 409，不回意圖。 | U | REQ_F4114.8.3 | PASS |
| PUR-MATCH-U99 | 關聯／跨公司 | 缺保存表頭、非法 ID、公司、供應商、採購單或原幣不符整單拒絕；真正跨公司 HTTP 定位須回 404。 | U,I | REQ_F4114.8.3 | U PASS；I 待 008a |
| PUR-MATCH-U100 | 重複／部分結果 | 已軟刪的另一發票仍佔號並回 409；單張原子修訂無批次部分成功／全部略過。 | U | REQ_F4114.8.3 | PASS（批次 N/A） |
| PUR-MATCH-017 | 權限／錯誤／介面／檔案 | SQL 鎖內版本、權限 403、跨公司 404、唯一索引、400／409／422／500／逾時三語畫面與資料雙斷言由 008a／008c 驗；無檔案輸入。 | I,E | REQ_F4114.8.3 | TODO（檔案 N/A） |

執行記錄：2026-11-12T06:14:56+08:00，缺型別／入口 CS0246／CS0117 先紅；定向 12/12、Architecture 1848/1848、PUR Debug 零警告。記憶體守衛 1.310／1.302／1.310 GiB DEFER，未跑 SQL／HTTP；API 輸出受執行程序鎖定，Architecture 測試不重建參照。

## WU-PUR-008a23 — Draft 保存意圖離線編排

| ID | 類別 | 情境／預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-MATCH-U93 | 正常／邊界 | 閏日及合法價稅差異保留；身份、號碼、待寫入行、總額與差異證據均取自同一新驗快照。 | U | REQ_F4114.8.2～3 | PASS |
| PUR-MATCH-U94 | 重複／狀態／關聯 | 同公司同供應商已軟刪發票仍佔號；編輯本張可保留，其他持有者拒絕。 | U | REQ_F4114.8.2 | PASS |
| PUR-MATCH-U95 | 衝突／錯誤／部分結果 | 權威價稅變動、空持有者清單整單拒絕，不回任何部分意圖；單張保存不適用批次部分成功、全部略過或全部失敗筆數。 | U | REQ_F4114.8.2～3 | PASS |
| PUR-MATCH-U96 | 邊界／錯誤 | 保存意圖的逐行差異證據不暴露可修改陣列，不能在預檢後改寫待保存差異。 | U | REQ_F4114.8.2～3 | PASS |
| PUR-MATCH-017 | 權限／跨公司／介面／檔案 | 真實 SQL 鎖、版本 409、權限 403、跨公司 404、唯一索引與交易原子性由 008a 驗；400／409／422／500／逾時三語畫面由 008c 驗。無檔案輸入。 | I,E | REQ_F4114.8.2 | TODO（008a／008c；檔案 N/A） |

執行記錄：2026-11-12T06:09:01+08:00，先新增測試；直接建置 API 參照遇既有程序鎖定 DLL，改用 PUR 模組建置及 Architecture `BuildProjectReferences=false`；差異證據陣列測試先紅後綠，Architecture 1841/1841、PUR Debug 0 警告／錯誤。SQL 守衛 1.309／1.293／1.286 GiB DEFER，真實 SQL／HTTP 未驗。

## WU-PUR-008a22 — Draft 保存前來源與價稅快照重驗

| ID | 類別 | 情境／預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-MATCH-U90 | 正常／邊界 | 閏日、同一權威來源且零差異時返回新驗快照；合法價差／稅差保留五種差異，不改價。 | U | REQ_F4114.8.2～3 | PASS |
| PUR-MATCH-U91 | 重複／衝突／關聯／狀態 | 權威訂單單價、稅率、訂單行或收貨已刪變動，或價格來源缺漏／錯置，整單拒絕。 | U | REQ_F4114.8.2～3 | PASS |
| PUR-MATCH-U92 | 邊界／部分結果／錯誤 | 快照行來源、金額、總額、身份或差異明細被改，整單回 `PUR.INVOICE.DRAFT_SNAPSHOT_CHANGED`；沒有部分成功或略過。 | U | REQ_F4114.8.2～3 | PASS |
| PUR-MATCH-017 | 權限／衝突／介面／檔案 | SQL 鎖內來源、版本 409、無權 403、跨公司 404、非 Draft 422；400／409／422／500／逾時三語畫面與資料零變更由 008a／c 驗。純規則無檔案輸入。 | I,E | REQ_F4114.8.2 | TODO（008a／008c；檔案 N/A） |

此切片只重驗離線快照，保存服務、稽核、權限及真實 SQL／HTTP 待 008a。單張發票原子保存，批次部分成功及全部略過不適用。

執行記錄：2026-11-12T06:01:09+08:00，CS0103 紅燈後定向 4/4，補合法差異、被改差異明細及空快照後 Architecture 1836/1836、PUR Debug 0 警告／錯誤。守衛 DEFER，未驗真實 SQL／HTTP。

## WU-PUR-008a21 — Draft 逐行權威價稅證據

| ID | 類別 | 情境／預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-MATCH-U87 | 正常／邊界 | 同收貨來源多行各自按權威訂單價稅算四位基準；零差異與合法差異均保留原 Draft 價稅，輸出逐行差異供後續 MATCH。 | U | REQ_F4114.8.2～3 | PASS |
| PUR-MATCH-U88 | 關聯／重複／衝突／狀態 | 價格來源缺漏、重複、額外、其他收貨／公司／訂單／訂單行／物料，或非法精度／金額均整單拒絕，無部分證據；訂單及收貨狀態沿用 U85。 | U | REQ_F4114.8.2～3 | PASS |
| PUR-MATCH-U89 | 邊界／部分結果／錯誤 | 壞 Draft 行先回輸入錯誤；權威價格非法整單拒絕，沒有部分成功、略過或靜默改價。純規則無檔案輸入。 | U | REQ_F4114.8.2～3 | PASS（檔案 N/A） |
| PUR-MATCH-017 | 權限／衝突／介面 | SQL 鎖內來源、已軟刪號碼重複、版本 409、無權限 403、跨公司 404、非 Draft 422；400／409／422／500／逾時的三語畫面原因與資料零變更。 | I,E | REQ_F4114.8.2 | TODO（008a／008c） |

執行記錄：2026-11-12T02:07:10+08:00，缺型別 CS0246 紅燈後定向 6/6；此切片只組裝離線證據，並發修改與資料範圍由 008a 真實 SQL／HTTP 驗。

## WU-PUR-008a20 — Draft 保存快照離線組裝

| ID | 類別 | 情境／預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-MATCH-U84 | 正常／邊界 | 閏日、同收貨來源多行可組裝；公司身份、已驗價稅行與整單總額一致，原始行清單後續修改不影響快照。 | U | REQ_F4114.8.2 | PASS |
| PUR-MATCH-U85 | 重複／關聯／狀態 | 公司不符、訂單非 Approved／已刪、來源缺漏／重複／已刪或物料不一致時整單拒絕，沒有部分結果。 | U | REQ_F4114.8.2 | PASS |
| PUR-MATCH-U86 | 錯誤／部分結果 | 行號重複時先回輸入錯誤，不輸出快照或部分總額；空檔案與格式錯檔不適用，無檔案輸入。 | U | REQ_F4114.8.2 | PASS（檔案 N/A） |
| PUR-MATCH-017 | 權限／衝突／介面 | SQL 鎖內來源、已軟刪號碼重複、版本 409、無權限 403、跨公司 404、非 Draft 422；400／409／422／500／逾時的三語畫面原因與資料零變更。 | I,E | REQ_F4114.8.2 | TODO（008a／008c） |

執行記錄：2026-11-12T02:01:14+08:00，記憶體守衛 DEFER；定向測試在避開既有 API 程序輸出鎖後通過。無 SQL／HTTP 保存，批次部分成功／全部略過不適用。

## WU-PUR-008a19 — Draft 已驗行總額

| ID | 類別 | 情境／預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-MATCH-U80 | 正常／邊界 | 多行稅額各先四位取整再加總；閏日與最小六位數量有效；返回總額與已驗行一致。 | U | REQ_F4114.8.2 | PASS |
| PUR-MATCH-U81 | 錯誤／部分結果 | 後段壞行或整單總額達 SQL 上限時整單拒絕，不回部分總額；原始清單後續改動不改返回行或總額。 | U | REQ_F4114.8.2 | PASS（含既有總額上限測試） |
| PUR-MATCH-U82 | 重複／權限／狀態／關聯 | 同來源多行可分別計入；重複號、軟刪、並發 409、跨公司、非 Draft、來源有效性由 008a 真實 SQL／HTTP 驗。 | I | REQ_F4114.8.2 | TODO（008a） |
| PUR-MATCH-U83 | 介面／檔案 | 400／409／422／500／逾時及三語提示由 008c 驗；純計算不收檔案、無略過或部分成功。 | E | REQ_F4114.8.2 | TODO（008c；檔案 N/A） |

執行記錄：2026-11-12T01:16:36+08:00，缺 `Totals` 的 CS1061 紅燈後，定向 13/13、Architecture 1775/1775、Purchasing Debug 0 警告／錯誤。記憶體守衛 2.138／2.139／2.167 GiB DEFER，未跑真實 SQL／HTTP 或後端全量。

## WU-PUR-008a18 — Draft 驗證行快照不可改寫

| ID | 類別 | 情境／預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-MATCH-U76 | 正常／邊界 | 有效閏日、一行價稅通過；驗證後原始清單新增或替換行不影響返回快照。 | U | REQ_F4114.8.2 | PASS（原有快照測試及本輪補測） |
| PUR-MATCH-U77 | 重複／衝突／部分結果／錯誤 | 返回集合轉型後替換或新增行均拒絕，原有效行保持不變；整單沒有部分成功或全部略過。 | U | REQ_F4114.8.2 | PASS（先紅後綠） |
| PUR-MATCH-U78 | 權限／狀態／關聯 | 此純規則不查資料庫；跨公司、已刪來源、版本與非 Draft 狀態由 008a 真實 SQL／HTTP 測試。 | I | REQ_F4114.8.2 | TODO（008a） |
| PUR-MATCH-U79 | 介面／檔案 | 400／409／422／500／逾時的三語回饋與零變更由 008c 驗；本切片無檔案輸入。 | E | REQ_F4114.8.2 | TODO（008c；檔案 N/A） |

此切片只保護驗證輸出集合，沒有新增 API 或持久化；SQL／HTTP 全量因記憶體守衛 DEFER 未執行。

## WU-PUR-008a14 — Draft 發票行保存前形狀

| ID | 類別 | 情境／預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-MATCH-U61 | 正常／邊界 | 1 行及 1000 行通過；同收貨行可分多行引用；六位最小正量、最大 SQL 可存量及最大正數行號／來源 ID 通過。 | U | REQ_F4114.8.2 | PASS |
| PUR-MATCH-U62 | 邊界／錯誤 | null、0 行、1001 行、null 行、非正數或重複行號、非正數來源 ID、零／負／七位小數／達 SQL 上限的數量整單拒絕；後段壞行亦不得返回部分結果。 | U | REQ_F4114.8.2 | PASS |
| PUR-MATCH-U63 | 關聯／衝突／持久化 | 008a 保存服務接線後，驗同張發票重複收貨來源聚合與鎖內餘量、舊版本 409、無權限 403、跨公司 404、非 Draft 422；HTTP 400 錯誤指出行與原因，失敗保存零變更。 | I,E | REQ_F4114.8.2 | TODO（008a／008c） |

部分成功、全部略過不適用：一張發票整單原子保存。檔案不適用：不收檔案。500／逾時、三語畫面及資料雙斷言留 PUR-MATCH-004、008～028 與 008c。U61／U62 僅驗離線形狀，不代表 SQL／HTTP 已驗收。

執行記錄：2026-11-11T20:57:22+08:00，CS0246 紅燈後定向 12/12；補後段壞行及 SQL 上限，Architecture 1751/1751、Purchasing Debug 0 警告／錯誤。守衛 DEFER，未跑真實 SQL、全量後端或 HTTP。

## WU-PUR-008a13 — 供應商發票號重複離線判定

| ID | 類別 | 情境／預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-MATCH-U57 | 正常／衝突 | 同公司同供應商等價鍵已存在時 409；軟刪持有者仍佔號。 | U | REQ_F4114.8.2, REQ_F4114.8.6 | PASS |
| PUR-MATCH-U58 | 邊界／重複 | 編輯本張發票可沿用原號，另一張 ID 不可；不同公司或供應商可用同號。 | U | REQ_F4114.8.2 | PASS |
| PUR-MATCH-U59 | 錯誤／權威 | 空號碼、非法公司／供應商／編輯 ID、重複權威 ID 或非正規化保存鍵失敗關閉。 | U | REQ_F4114.8.2 | PASS |
| PUR-MATCH-U60 | 併發／持久化 | 真實 SQL 不過濾軟刪的唯一索引；雙連線同時建立只一張成功，另一張 409；跨公司／供應商、軟刪重用及編輯豁免核對保存資料。 | I,E | REQ_F4114.8.2, REQ_F4114.8.6 | TODO（008a／008c） |

部分結果不適用：單張發票寫入為原子操作。權限、狀態、關聯、400／422／500／逾時與三語畫面由 PUR-MATCH-004、008～028 及 008c 驗收；此離線函式沒有 I/O 或檔案。U57～U59 不代表 SQL 併發、HTTP 或資料範圍已驗收。

執行記錄：2026-11-11T20:51:51+08:00，先缺型別編譯紅燈；補充邊界測試時修正 xUnit `InlineData` 的 `long?` 型別，Architecture 1737/1737 通過。記憶體守衛 DEFER，未跑真實 SQL／HTTP。


## WU-PUR-008a12 — 供應商發票號正規化

| ID | 類別 | 情境／預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-MATCH-U53 | 正常／重複 | 外圍空白、大小寫及全半形等價號碼產生同一鍵；中間有效字符保留。 | U | REQ_F4114.8.2 | PASS |
| PUR-MATCH-U54 | 邊界／錯誤 | 空值、全空白、控制／格式字元、非法 UTF-16、顯示值或鍵超過 80 字元拒絕；恰好 80 字元接受。 | U | REQ_F4114.8.2 | PASS |
| PUR-MATCH-U55 | 衝突／關聯 | 正規化鍵供同公司＋供應商永久唯一索引；軟刪後同鍵仍 409，其他供應商可重用。 | I | REQ_F4114.8.2, REQ_F4114.8.6 | TODO（008a） |
| PUR-MATCH-U56 | 權限／狀態／錯誤 | 無 CREATE／EDIT 權限、跨公司或非 Draft 編輯不得觸發寫入；400／409／422／500／逾時以三語顯示原因。 | I,E | REQ_F4114.8.2 | TODO（008a／008c） |

部分成功／全部略過不適用：單張發票為原子寫入，沒有批次結果。檔案不適用：此功能不收檔案。U53／U54 僅驗無 I/O 的鍵函式；併發、SQL 唯一約束、畫面訊息與保存資料雙斷言保留 PUR-MATCH-008 及 008a／008c。

執行記錄：2026-11-11T20:44:27+08:00，先 CS0103 編譯紅燈，補邊界後 Architecture 1729/1729、Purchasing Debug 0 警告／錯誤。SQL 記憶體守衛 DEFER，未驗真實資料庫或 HTTP。

## WU-PUR-008a11 — POST 保存快照離線組裝

| ID | 類別 | 情境／預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-MATCH-U49 | 正常 | 同一保存快照輸出整單預檢、穩定指紋及 FI 來源身份／日期／逐行價稅，數量計畫與行量一致。 | U | REQ_F4114.8.5 | PASS |
| PUR-MATCH-U50 | 邊界／權限身份 | 請求公司或發票 ID 與保存快照不符，整單拒絕；空行或重複行不回 FI 資料。 | U | REQ_F4114.8.5 | PASS |
| PUR-MATCH-U51 | 狀態／關聯／錯誤 | Draft、失效收貨或過期差異接受證據整單拒絕；不得輸出部分 FI 請求。 | U | REQ_F4114.8.5 | PASS |
| PUR-MATCH-U52 | 重放 | Posted 有本單已過帳量證據時數量零變更，FI 來源仍使用同保存快照指紋。 | U | REQ_F4114.8.5 | PASS |

部分成功與全部略過不適用：單一發票原子處理，任一驗證失敗不返回結果。真正權限／跨公司資料範圍、409 版本、FI 500／逾時、SQL 競態與持久化、HTTP／畫面雙斷言由 PUR-MATCH-001～028 及 008a～c 驗收；此純規則沒有 I/O 或檔案。日期與最大行數沿用 008a9／a10 的已保存快照檢查。

## WU-PUR-008a10 — POST 保存快照指紋離線規則

| ID | 類別 | 情境／預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-MATCH-U45 | 正常／重複 | 同一保存快照在行與接受決定載入順序不同時指紋相同；包含版本域及 64 位雜湊。 | U | REQ_F4114.8.5 | PASS |
| PUR-MATCH-U46 | 邊界／衝突 | 公司、發票、供應商、原幣、日期、行號、收貨來源或已保存價稅／數量變更時指紋不同；等價 decimal 尺度相同。 | U | REQ_F4114.8.5 | PASS |
| PUR-MATCH-U47 | 狀態／關聯 | 接受理由變更時指紋不同；差異缺接受、重複或過期證據不可產生指紋。 | U | REQ_F4114.8.5 | PASS |
| PUR-MATCH-U48 | 錯誤／部分結果 | 非法身份／幣別、重複行、超過 1000 行整單拒絕，不輸出部分雜湊。 | U | REQ_F4114.8.5 | PASS |

權限、跨公司資料範圍、版本 409、Posted 來源鍵重放、FI 500／逾時、HTTP／畫面及檔案：此切片只讀已保存快照作無 I/O 純規則，實際授權、FI 同交易與可見訊息仍由 PUR-MATCH-019～027、008a～c 驗收；無檔案或部分提交。執行記錄：2026-11-11T20:28:57+08:00，先 CS0103 紅燈，補邊界後 Architecture 1711/1711、Purchasing Debug 0 警告／錯誤；SQL 守衛 DEFER，未驗真實 SQL／HTTP。

## WU-PUR-008a9 — POST 離線整單預檢

| ID | 類別 | 情境與預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-MATCH-U39 | 正常 | Matched 零差異重驗身份、價稅與量後，產生整單預留轉已開票量計畫。 | U | REQ_F4114.8.5 | PASS |
| PUR-MATCH-U40 | 正常／關聯 | 差異行有相同種類、基準、值、差額的保存 Accept 決定及原因，才可 POST；訂單價稅變動後拒絕。 | U | REQ_F4114.8.5 | PASS |
| PUR-MATCH-U41 | 重複／狀態 | Posted 有本單已過帳量證據時只回零變更；Draft／Disputed／Rejected 不可 POST。 | U | REQ_F4114.8.5 | PASS |
| PUR-MATCH-U42 | 關聯／權限邊界 | 收貨失效或身份不同整單拒絕；跨公司來源由身份檢查拒絕。實際操作者權限／資料範圍待 HTTP。 | U,I | REQ_F4114.8.5 | U PASS；I TODO |
| PUR-MATCH-U43 | 邊界／部分結果 | 多來源任一舊預留或量不足整單拒絕，沒有部分計畫；非法金額亦拒絕。 | U | REQ_F4114.8.5 | PASS |
| PUR-MATCH-U44 | 衝突／錯誤 | 新差異無接受、拒絕／過期／重複／額外決定，或 Posted 缺本單過帳證據，整單拒絕。 | U | REQ_F4114.8.5 | PASS |

此離線預檢不處理使用者權限、舊版 409、SQL 競態／回滾、FI 失敗、HTTP 400／409／422／500／逾時與畫面訊息；PUR-MATCH-001～028 的 I/E 情境仍待 008a／008b／008c。部分成功、全部略過不適用：POST 整單原子，任一失敗不回計畫。檔案不適用：POST 不接收檔案。日期、最大長度與最大筆數由保存行及既有 U01～U38 驗證；本編排沿用那些檢查。執行記錄：2026-11-11T20:21:47+08:00，CS0103 紅燈後定向 10/10，補邊界後 Architecture 1705/1705。

## WU-PUR-008a8 — POST 預留轉已開票量離線計畫

| ID | 類別 | 情境與預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-MATCH-U34 | 正常／重複 | Matched 同來源多行先合量並按來源 ID 排序，精確釋回本單預留且等量增加已開票。 | U | REQ_F4114.8.5 | PASS |
| PUR-MATCH-U35 | 重複／狀態 | Posted 重放有本單已過帳量證據及零預留時零變更；Draft／Disputed／Rejected 不可 POST。 | U | REQ_F4114.8.5 | PASS |
| PUR-MATCH-U36 | 邊界／部分結果 | 任一來源因退貨而不足、舊預留少於保存量，整單拒絕而不輸出部分計畫。 | U | REQ_F4114.8.5 | PASS |
| PUR-MATCH-U37 | 衝突／關聯 | Posted 無本單過帳證據或仍有預留拒絕；證據大於來源總已開票量拒絕。 | U | REQ_F4114.8.5 | PASS |
| PUR-MATCH-U38 | 錯誤／邊界 | 來源缺漏、重複、額外，或請求超六位小數精度，整單拒絕。 | U | REQ_F4114.8.5 | PASS |

權限、跨公司／資料範圍、舊版本 409、SQL 競態與回滾、FI 失敗、API 400／409／422／500／逾時及畫面／資料雙斷言保留 PUR-MATCH-001～028 的 I/E 驗收；本純計畫不讀寫資料。部分成功不適用，任一來源不合法整單失敗。檔案不適用：POST 不接收檔案。執行記錄：2026-11-11T20:13:55+08:00，CS0246 紅燈後定向與 Architecture 通過；真實 SQL／HTTP 待 008a／008b。

## WU-PUR-008a7 — MATCH／RESOLVE 預留轉移離線計畫

| ID | 類別 | 情境與預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-MATCH-U29 | 正常／重複 | MATCH 按收貨行合量排序建立預留；同一保存快照重驗差額為零，不重複扣量。 | U | REQ_F4114.8.3 | PASS |
| PUR-MATCH-U30 | 邊界／狀態 | 本單預留由較大量修訂至較小量，只釋回差額；Shortage 不可轉預留。 | U | REQ_F4114.8.2～3 | PASS |
| PUR-MATCH-U31 | 正常／關聯 | Disputed 全行接受仍預留原量；任一拒絕按各來源精確釋回至零。 | U | REQ_F4114.8.4 | PASS |
| PUR-MATCH-U32 | 衝突／部分結果 | 來源缺漏、重複、舊預留不等保存量或量不足整單拒絕，沒有部分計畫。 | U | REQ_F4114.8.3～4 | PASS |
| PUR-MATCH-U33 | 錯誤／邊界 | 非法狀態、零量與來源快照拒絕；接受覆核時來源餘量變少則拒絕，拒絕決定仍可精確釋回。 | U | REQ_F4114.8.3～4 | PASS |

權限、跨公司／資料範圍、舊版本 409、SQL 並發與回滾、API 400／409／422／500／逾時、畫面與資料雙斷言由 PUR-MATCH-001～028 的 I/E 情境留待 008a／c；本純計畫沒有 I/O。部分成功不適用，整單失敗關閉。檔案：MATCH／RESOLVE 不收檔案，空檔、格式、大小及編碼不適用。執行記錄：2026-11-11T20:07:44+08:00，先 CS0103 紅燈；本輪定向與離線回歸結果見 progress.md。

## WU-PUR-008a6 — MATCH 整單離線編排

| ID | 類別 | 情境與預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-MATCH-U25 | 正常 | Draft 的完整身份、量與價稅一致，回 Matched 及行號排序的證據；價稅差異則整單 Disputed。 | U | REQ_F4114.8.3 | PASS |
| PUR-MATCH-U26 | 邊界／重複 | 同收貨行重複引用先合量；空單拒絕，多來源不足全列且按來源 ID 排序。 | U | REQ_F4114.8.2～3 | PASS |
| PUR-MATCH-U27 | 部分結果／關聯 | 身份錯先於餘量與價稅；餘量不足先於價稅；不返回部分匹配或部分差異證據。 | U | REQ_F4114.8.3 | PASS |
| PUR-MATCH-U28 | 狀態／錯誤 | Matched、Disputed、Posted 不可再 MATCH；缺少或非法量來源、非法價稅整單拒絕。Rejected 可重新匹配。 | U | REQ_F4114.8.3～5 | PASS |

權限、跨公司與資料範圍、版本衝突 409、SQL 500／逾時、HTTP／畫面、審計及預留持久化：此純函式不讀寫資料，保留 PUR-MATCH-001～028 的 I/E 驗收。檔案：MATCH 不收檔案，空檔／格式／編碼／大小不適用。記錄：2026-11-11T20:01:11+08:00，CS0246 紅燈；定向與 Architecture 結果見本輪進度記錄。

## WU-PUR-008a5 — 價稅差異逐行決定離線預檢

| ID | 類別 | 情境／預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-MATCH-U20 | 正常 | Disputed 的每個差異行各接受一次且附原因，結果 Matched；輸出保留原行差額與原因。 | U | REQ_F4114.8.4 | PASS |
| PUR-MATCH-U21 | 正常／狀態 | 任一行拒絕使整單 Draft；全部決定保留為歷史，但不得作過帳授權。 | U | REQ_F4114.8.4 | PASS（純決策；保存待 008a） |
| PUR-MATCH-U22 | 邊界／錯誤 | 原因去空白後 1～500 字；空白、501 字、未知動作拒絕；500 字及單行可用。 | U | REQ_F4114.8.4 | PASS（單行可用沿用完整決定測試） |
| PUR-MATCH-U23 | 重複／衝突／部分結果 | 漏行、重複行、額外行、零行決定整單拒絕，不返回部分成功。 | U | REQ_F4114.8.4 | PASS |
| PUR-MATCH-U24 | 狀態／關聯 | 非 Disputed、無差異、非法差異快照不得作決定。 | U | REQ_F4114.8.4 | PASS（非法差異來源仍由 008a 鎖內重驗） |

權限、跨公司與資料範圍、舊版 409、SQL 500／逾時、實際預留釋回及畫面訊息：純規則無 I/O 或保存，由 PUR-MATCH-003、010、014～017、023、025～026 的 I/E 情境驗收。檔案：不適用，此動作不收檔案。

執行記錄：2026-11-11T19:55:30+08:00，先 CS0103／CS0246 紅燈；修正測試集合語法並補單行及矛盾快照案例後定向 14/14、Architecture 1667/1667、PUR Debug 0 警告／錯誤。守衛 1.711／1.712／1.706 GiB DEFER；未執行 SQL／HTTP／全量後端。

## WU-PUR-008a4 — 保存訂單基準與發票原幣價稅差異（測試先行）

| ID | 類別 | 情境／預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-MATCH-U15 | 正常 | 保存訂單單價／稅率按發票數量計四位原幣基準；相同發票價稅額零差異。 | U | REQ_F4114.8.3 | PASS |
| PUR-MATCH-U16 | 邊界 | 逐行四位 AwayFromZero 取整後合計；最小 0.0001 差額為 Disputed，預設零允差。 | U | REQ_F4114.8.3～4 | PASS |
| PUR-MATCH-U17 | 衝突／關聯 | 發票單價、稅率、淨額、稅額及含稅額各自與已保存訂單基準比較，差異逐項保留；不讀當日 MD。 | U | REQ_F4114.8.3～4 | PASS |
| PUR-MATCH-U18 | 部分結果 | 多行中任一價稅差異整張發票 Disputed；不能把其他行當成已過帳。 | U | REQ_F4114.8.3～4 | PASS（純決策；Disputed 持久化待 008a） |
| PUR-MATCH-U19 | 錯誤／狀態 | 非正數／超六位數量、負價格、非法稅率、超四位發票額或溢位來源整單拒絕。 | U | REQ_F4114.8.2～4 | PASS（含總額溢位） |

權限、跨公司、狀態、重複來源、併發 409、SQL 500／逾時、HTTP／畫面及檔案：本切片只作無 I/O 價稅決策；PUR-MATCH-001～028 的 I/E 情境由 008a～c 驗收。沒有部分保存或檔案操作。

執行記錄：2026-11-11T16:08:38+08:00，先缺型別 CS0103／CS0246 紅燈，後補逐行四位取整及整單差異決策；追加總額溢位與多行邊界後 Architecture 1653/1653、Purchasing Debug 0 警告／錯誤。SQL 守衛 DEFER，真實 SQL／HTTP 未驗。

## WU-PUR-008a2 — 整單來源餘量離線預檢

| ID | 類別 | 情境／預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-MATCH-U06 | 正常／重複 | 重複請求先合量；來源快照可任意順序傳入，恰等可用量整單可匹配。 | U | REQ_F4114.8.2～3 | PASS |
| PUR-MATCH-U07 | 邊界／部分結果 | 多個來源同時不足時按來源 ID 升冪回全部請求量與可用量；任一不足整單不可匹配，沒有逐行成功。 | U | REQ_F4114.8.2～3 | PASS |
| PUR-MATCH-U08 | 衝突／關聯 | 缺少、重複、額外、非正數 ID 或空快照一律拒絕；非法來源量即使出現在不足來源之後也整單拒絕。 | U | REQ_F4114.8.2～5 | PASS |
| PUR-MATCH-U09 | 錯誤次序 | 同時有非法請求及缺來源時先回請求 400 對應錯誤，不用缺來源掩蓋輸入錯誤。 | U | REQ_F4114.8.2 | PASS |

權限／跨公司、Posted／刪除狀態、並發 409、SQL 500／逾時、HTTP／畫面及檔案：本切片是無 I/O 純規則，均由 PUR-MATCH-001～028 的 I/E 情境驗收；沒有資料寫入或部分提交。實際 SQL 鎖內來源讀取及 422 行號錯誤仍屬 008a。

執行記錄：2026-11-11T15:53:05+08:00，先 CS0246／CS0117 紅燈；補充錯誤次序與遲到非法來源後，Architecture 1619/1619、Purchasing Debug 0 警告／錯誤。SQL 守衛 DEFER，未執行真實 SQL／HTTP。

## WU-PUR-008a1 — 同單重複收貨來源聚合

| ID | 類別 | 情境／預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-MATCH-U04 | 正常／重複 | 同單重複收貨行合併數量，按來源 ID 升冪；聚合量供可開票量檢查。 | U | REQ_F4114.8.2～3 | PASS |
| PUR-MATCH-U05 | 邊界／衝突 | 空請求、零或負 ID、零或負量、七位小數、聚合總量到達 SQL 精度上限都拒絕。 | U | REQ_F4114.8.2～3 | PASS |

部分結果：聚合是純函式，任一非法行整筆拒絕且沒有保存；權限、跨公司、Posted 來源狀態、已刪來源、併發 409、SQL 500／逾時、畫面錯誤及檔案由 PUR-MATCH-001～028 的 I/E 情境驗收，本切片沒有 HTTP、UI 或檔案。執行記錄：2026-11-11T15:45:56+08:00，先 CS0117／CS0246 紅燈，定向 19/19、Architecture 1610/1610；真實 SQL／HTTP 未驗。

## WU-PUR-008a0 — 收貨行可開票量離線規則

| ID | 類別 | 情境／預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-MATCH-U01 | 正常 | 淨收貨扣 Posted 退貨、待審退貨、已開票與其他 Matched 預留；本單預留於重驗時排除。 | U | REQ_F4114.8.2～5 | PASS |
| PUR-MATCH-U02 | 邊界 | 恰等於可用量可匹配；增加 0.000001 即不足；零、負數或超六位請求拒絕。 | U | REQ_F4114.8.2～3 | PASS（零與恰等／超額；其餘沿用精度規則） |
| PUR-MATCH-U03 | 重複／衝突／狀態 | 本單預留超總預留、來源已超量或負數／七位小數快照拒絕；不得用不一致來源推算負餘量。 | U | REQ_F4114.8.2～5 | PASS |

部分結果：純計算不寫資料；整單零變更／回滾由 PUR-MATCH-012～013 的 I/E 驗收。權限／跨公司、來源 Posted 狀態、關聯退貨與重複來源聚合、409 併發、500／逾時、檔案及畫面由 PUR-MATCH-001～028 的 I/E 情境驗收；本切片沒有 HTTP、檔案或 UI。執行記錄：2026-11-11T15:39:06+08:00，先缺型別編譯紅燈，補邊界後定向 11/11、Architecture 1602/1602。真實 SQL／HTTP 未驗。

## WU-PUR-008d 三單比對與 FI 應付情境（設計；產品測試待 008a／b／c）

依 `myerp-test-scenarios` 檢查正常、邊界、衝突、部分結果、權限、狀態、關聯、錯誤及檔案。U=單元、I=真實 SQL／受權 HTTP 整合、E=Playwright；E 必須同時核對畫面訊息與實際保存發票、來源餘量和 FI 應付。008d 沒有產品測試，以下全部 TODO。

| ID | 類別 | 情境／斷言 | 層級 | 需求 | 狀態 |
|---|---|---|---|---|---|
| PUR-MATCH-001 | 正常 | 同一 Approved 訂單兩行已 Posted 收貨，Draft 零預留；MATCH 無差異預留，POST 恰一筆 FI 應付、已開票量與畫面一致。 | I,E | REQ_F4114.8.2～5 | TODO |
| PUR-MATCH-002 | 正常 | 一張收貨分次開票，第二張只取剩餘量；原幣／本位幣逐行取整後合計一致。 | U,I | REQ_F4114.8.2～5 | TODO |
| PUR-MATCH-003 | 正常 | 價差與稅差分別呈 Disputed；RESOLVE 各行附理由接受後 POST，FI 保存接受後實際金額與差額歷史。 | I,E | REQ_F4114.8.3～5 | TODO |
| PUR-MATCH-004 | 邊界 | 空／1001 行、重複行號、零／負 ID、零／負及七位小數量、空發票號逐欄 400；1／1000 行可保存。 | U,I,E | REQ_F4114.8.2 | TODO |
| PUR-MATCH-005 | 邊界 | 月底、跨年、閏日、四位金額及六位量；DECIMAL(19,4) 金額溢位整單拒絕。 | U,I | REQ_F4114.8.2～3 | TODO |
| PUR-MATCH-006 | 邊界 | 可開票量恰等成功，超最小精度一單位 422；同單重複收貨來源先聚合再驗。 | I | REQ_F4114.8.2～3 | TODO |
| PUR-MATCH-007 | 邊界 | 預設允差 0，一個最小金額差額成 Disputed；無版本化設定時不可自行傳允差。 | U,I | REQ_F4114.8.3～4 | TODO |
| PUR-MATCH-008 | 重複／衝突 | 同公司同供應商正規化發票號重複與軟刪後重用皆拒絕；其他供應商可用同號。 | I | REQ_F4114.8.2, REQ_F4114.8.6 | TODO |
| PUR-MATCH-009 | 重複／衝突 | 兩連線同時 MATCH 同一收貨餘量，最多一筆成功；總預留與已開票不超實收淨量。 | I | REQ_F4114.8.3 | TODO |
| PUR-MATCH-010 | 重複／衝突 | 舊版 EDIT／MATCH／RESOLVE／POST、重複 MATCH／POST 回 409 或既有冪等結果，不二次佔量或建 FI 應付。 | I,E | REQ_F4114.8.2～5 | TODO |
| PUR-MATCH-011 | 重複／衝突 | 同 FI 來源／相同快照重放回原應付 ID；不同快照 409，FI 僅一筆。 | I | REQ_F4114.8.5 | TODO |
| PUR-MATCH-012 | 部分結果 | 任一行來源或價稅驗證失敗，整單 MATCH／POST 零變更；不縮量、不略過、不建部分應付。 | I,E | REQ_F4114.8.3～5 | TODO |
| PUR-MATCH-013 | 部分結果 | 全部行不足及 0 行分別 422／400，逐行理由與資料零變更；本功能整單原子，部分成功不適用。 | I,E | REQ_F4114.8.2～5 | TODO |
| PUR-MATCH-014 | 權限 | 匿名 401；VIEW／CREATE／EDIT／MATCH／RESOLVE／POST／DELETE 分權 403；只有 CREATE／EDIT 者仍可讀專用來源上下文。 | I,E | REQ_F4114.8.1～6 | TODO |
| PUR-MATCH-015 | 權限 | 跨公司、本人／部門／分店範圍外 404；偽造公司、供應商、成本、FI ID、差額及狀態不能保存。 | I | REQ_F4114.8.1～5 | TODO |
| PUR-MATCH-016 | 狀態 | Draft 可修訂／刪；Matched／Disputed 不可改輸入；Rejected 修訂後可重匹配；Posted 不可改刪或重過帳。 | I,E | REQ_F4114.8.2～6 | TODO |
| PUR-MATCH-017 | 狀態 | RESOLVE 拒絕一次釋回全部本單預留；修改重匹配重新佔量；重複拒絕不二次釋回。 | I | REQ_F4114.8.4 | TODO |
| PUR-MATCH-018 | 關聯 | 未核准／取消訂單、未 Posted 收貨、其他供應商／物料／貨幣或已軟刪來源均不得匹配。 | I,E | REQ_F4114.8.2～3 | TODO |
| PUR-MATCH-019 | 關聯 | 收貨部分退貨與待審退貨都扣可開票量；已開票部分退貨須先 FI 信用票據，不能留下無調整應付。 | I | REQ_F4114.7.3, REQ_F4114.8.3～5 | TODO |
| PUR-MATCH-020 | 關聯 | MD 匯率／稅率後續變更不重估已保存訂單基準或 Posted 發票；歷史仍可讀。 | I | REQ_F4114.8.1～5 | TODO |
| PUR-MATCH-021 | 錯誤／交易 | FI 建應付前、中、後與 PUR 狀態保存時注入故障，FI／PUR／稽核一起回滾，原版本可重試。 | I | REQ_F4114.8.5 | TODO |
| PUR-MATCH-022 | 錯誤／交易 | FI 契約未就緒明確 422，零應付／零 Posted；不可只在 PUR 標過帳。 | I,E | REQ_F4114.8.5 | TODO |
| PUR-MATCH-023 | 錯誤／介面 | 400 逐欄、409 重載、422 行號／來源與可用量、500 traceId、逾時／斷線均無假成功，重讀核對資料。 | I,E | REQ_F4114.8.2～5 | TODO |
| PUR-MATCH-024 | 檔案 | 無發票檔案上傳：空檔、格式、編碼及大小不適用；若提供 VIEW CSV，0 行不下載、10000 行上限、BOM、公式防護與整檔取消。 | E | REQ_F4114.8.1 | TODO（上傳 N/A） |
| PUR-MATCH-025 | 介面 | 4.19～4.21 正常、空／無結果、無權、驗證、500、成功、Disputed／Rejected 三語亮暗截圖；畫面及資料雙斷言。 | E | REQ_F4114.8.1～6 | TODO |
| PUR-MATCH-026 | 審計 | 建立、修訂、匹配、逐行差異決定、FI 過帳與軟刪留下審計；回滾無假成功稽核。 | I | REQ_F4114.8.2～6 | TODO |
| PUR-MATCH-027 | 資料庫 | 同模組 FK 及索引、永久唯一發票鍵、來源量 CHECK、FI 來源唯一、Posted 不可變及 Down migration。 | I | REQ_F4114.8.2～6 | TODO |
| PUR-MATCH-028 | 邊界／併發 | MATCH 與退貨送審兩連線爭同一收貨行，固定鎖序無死鎖，最多一方取用最後餘量。 | I | REQ_F4114.8.3～5 | TODO |


## WU-PUR-007d 收貨／退貨情境（設計，全部 TODO）

### WU-PUR-007a1 收貨草稿輸入與修訂狀態（離線）

| ID | 類別 | 情境及斷言 | 層級 | 需求 | 狀態 |
|---|---|---|---|---|---|
| PUR-RECV-U01 | 正常／日期邊界 | 閏日、正數來源、最小六位量通過；輸出按行號排序且原清單／返回集合不可改變快照。 | U | REQ_F4114.2.2 | PASS |
| PUR-RECV-U02 | 邊界 | 空行、重複／零行號、零來源、零／七位量、SQL 數量上限拒絕；1000 行通過，1001 行拒絕。 | U | REQ_F4114.2.2 | PASS |
| PUR-RECV-U03 | 重複／狀態 | 同一訂單行在不同收貨行可保留待鎖內聚合；僅 Draft／Rejected 可修訂，PendingApproval／Posted／未知狀態拒絕。 | U | REQ_F4114.2.2～3 | PASS |
| PUR-RECV-U04 | 部分結果／權限／關聯／錯誤 | 後行非法使整單無輸出；重複單號、軟刪來源、版本 409、公司／資料範圍、Approved 訂單與 400／422／500／逾時仍由 007a 真實 SQL／HTTP 驗。 | I,E | REQ_F4114.2.2～3 | TODO（007a） |
| PUR-RECV-U05 | 檔案 | 此收貨草稿輸入沒有檔案；空檔、格式、大小、編碼不適用，CSV 留 VIEW 驗收。 | E | REQ_F4114.2.2 | N/A |

本切片只驗離線輸入／狀態契約；Posted 收貨、INV movement、SQL／HTTP 保存及權威來源驗證仍屬 007a～c。E2E 須同時斷言畫面訊息與重讀資料一致。

### WU-PUR-007a2 Approved 來源及可收量（離線）

| ID | 類別 | 情境及斷言 | 層級 | 需求 | 狀態 |
|---|---|---|---|---|---|
| PUR-RECV-U06 | 正常／邊界 | 同來源多行先合量，固定來源排序；恰等剩餘量通過。 | U | REQ_F4114.2.2, REQ_F4114.2.4 | PASS |
| PUR-RECV-U07 | 重複／衝突／關聯 | 缺漏、重複、額外、他公司、他單、已刪來源及非 Approved 訂單整單拒絕；軟刪來源不可重用。 | U | REQ_F4114.2.2 | PASS |
| PUR-RECV-U08 | 部分結果／狀態 | 任一行超量或已提交量不一致時，不返回部分可收計畫；已過帳與待審量均扣除。 | U | REQ_F4114.2.4 | PASS |
| PUR-RECV-U09 | 邊界 | 聚合達 SQL 上限、負已提交量與非法精度拒絕；零與最大合法來源量由 007a SQL 驗。 | U,I | REQ_F4114.2.2～4 | U PASS，I TODO |
| PUR-RECV-U10 | 權限／錯誤 | 純規則由呼叫端提供公司及快照；匿名、權限、資料範圍、版本 409、500／逾時須於 007a SQL／HTTP 驗。 | I,E | REQ_F4114.2.2～4 | TODO（007a） |
| PUR-RECV-U11 | 檔案 | 無檔案輸入；空檔、格式、大小、編碼不適用。 | E | REQ_F4114.2.2 | N/A |

本切片未建立鎖、資料表、端點或畫面；PUR-RECV-001～028 仍以真實 SQL／HTTP／E2E 驗收，E2E 同時核對提示與保存資料。

### WU-PUR-007a3 SUBMIT 預留與駁回釋回（離線）

| ID | 類別 | 情境及斷言 | 層級 | 需求 | 狀態 |
|---|---|---|---|---|---|
| PUR-RECV-U12 | 正常／邊界 | 同來源多行合量並排序，恰等餘量可預留；不修改傳入來源。 | U | REQ_F4114.2.4 | PASS |
| PUR-RECV-U13 | 重複／狀態 | PendingApproval 同保存量重送零差額；駁回精確釋回一次，Draft／Rejected／Posted 不可駁回。 | U | REQ_F4114.2.3～4 | PASS |
| PUR-RECV-U14 | 部分結果／衝突 | 後行不足、本單預留不符、來源缺漏、他公司、非法精度或提交時已有本單預留整單拒絕。 | U | REQ_F4114.2.4 | PASS |
| PUR-RECV-U15 | 關聯／邊界 | 他單待審量佔滿剩餘額度時，本單仍可精確駁回；不可釋放他單預留。 | U | REQ_F4114.2.4 | PASS |
| PUR-RECV-U16 | 權限／錯誤 | 無權限／跨資料範圍、舊版 409、422、500 與逾時仍由 007a／007b SQL／HTTP／E2E 驗證，畫面訊息須與重讀資料一致。 | I,E | REQ_F4114.2.3～4 | TODO |
| PUR-RECV-U17 | 檔案 | SUBMIT 與 SYS 駁回不收檔案，空檔、格式、大小、編碼不適用。 | E | REQ_F4114.2.3～4 | N/A |

純計畫未建立 SQL 鎖、SYS 回呼、API、畫面或持久化；PUR-RECV-001～028 的整合／E2E 驗收保持 TODO。

### WU-PUR-007a4 最終核准轉已收量（離線）

| ID | 類別 | 情境及斷言 | 層級 | 需求 | 狀態 |
|---|---|---|---|---|---|
| PUR-RECV-U18 | 正常／邊界 | 同來源多行合量並升冪排序；容量已滿時只轉本單精確預留，其他單待審量保留；輸入來源不被修改。 | U | REQ_F4114.2.4 | PASS |
| PUR-RECV-U19 | 重複／狀態 | Posted 逐來源已過帳證據等於保存量時兩欄零變更；缺少、過少、非法精度或殘留預留拒絕；Draft／Rejected／未知狀態不可核准。 | U | REQ_F4114.2.4 | PASS |
| PUR-RECV-U20 | 部分結果／關聯 | 本單預留不足、已有本單已收、來源缺漏／重複／軟刪／跨公司、容量不一致時整單不返回計畫；回傳集合不可修改。 | U | REQ_F4114.2.4 | PASS |
| PUR-RECV-U21 | 權限／衝突／錯誤 | SYS 授權、資料範圍、舊版 409、SQL 競態、INV 失敗回滾、500／逾時及畫面與保存資料一致性仍待 007a／007b 真實 SQL／HTTP／E2E。 | I,E | REQ_F4114.2.4 | TODO |
| PUR-RECV-U22 | 檔案 | SYS 最終核准無檔案輸入；空檔、格式、大小、編碼不適用。 | E | REQ_F4114.2.4 | N/A |

本切片只有純計畫；PUR-RECV-001～028 的整合／E2E 驗收仍待真實 SQL／HTTP。

### WU-PUR-007a5 保存快照與來源證據組裝（離線）

| ID | 類別 | 情境及斷言 | 層級 | 需求 | 狀態 |
|---|---|---|---|---|---|
| PUR-RECV-U23 | 正常／邊界 | 僅用請求公司、收貨單 ID 定位；由保存行按來源聚合並排序，核准量只從保存行與本單證據得出；閏日及多行同來源可用，回傳計畫唯讀。 | U | REQ_F4114.2.4 | PASS |
| PUR-RECV-U24 | 重複／衝突／狀態 | 請求與保存公司或單號、保存訂單 ID 不一致拒絕；Posted 只有逐來源已收證據相符才零變更；Draft、Rejected、未知狀態拒絕。 | U | REQ_F4114.2.4 | PASS |
| PUR-RECV-U25 | 部分結果／關聯／邊界 | 空行、重複行號、零量或保存量與來源預留不符，整單不產生計畫；失效／跨公司／缺漏來源由 007a4 規則拒絕。 | U | REQ_F4114.2.4 | PASS |
| PUR-RECV-U26 | 權限／錯誤 | 鎖內權威快照、SYS 權限與資料範圍、版本 409、SQL 併發、INV 失敗回滾、HTTP 400／422／500／逾時與 E2E 畫面／保存資料雙斷言留 007a／007b。 | I,E | REQ_F4114.2.4 | TODO |
| PUR-RECV-U27 | 檔案 | 最終核准沒有檔案輸入，空檔、格式、大小與編碼不適用。 | E | REQ_F4114.2.4 | N/A |

2026-11-12T01:48:31+08:00：先以缺少 `PurchaseReceiptApprovalSnapshot` 得到 CS0246 紅燈，新增組裝後定向測試通過。此切片沒有 SQL／HTTP 產品驗收。

### WU-PUR-007a6 SUBMIT／駁回保存快照組裝（離線）

| ID | 類別 | 情境及斷言 | 層級 | 需求 | 狀態 |
|---|---|---|---|---|---|
| PUR-RECV-U28 | 正常／邊界 | 閏日保存行同來源合量、來源升冪，Draft 提交只用保存量，結果唯讀且不改輸入。 | U | REQ_F4114.2.3～4 | PASS |
| PUR-RECV-U29 | 重複／狀態 | PendingApproval 同保存快照重驗零變更，駁回精確釋回；其他狀態不可駁回，Posted 不可重送。 | U | REQ_F4114.2.4～5 | PASS |
| PUR-RECV-U30 | 部分結果／衝突／關聯 | 請求身份、訂單身份不符，空／重複保存行、缺漏來源、本單預留與保存量不符時整單拒絕；軟刪及跨公司來源由 007a3 計畫拒絕。 | U | REQ_F4114.2.3～5 | PASS |
| PUR-RECV-U31 | 權限／錯誤 | 版本 409、SYS 權限、資料範圍、SQL 鎖／競態／回滾、HTTP 400／422／500／逾時及 E2E 訊息與實際資料雙斷言留 007a／007b。 | I,E | REQ_F4114.2.3～5 | TODO |
| PUR-RECV-U32 | 檔案 | SUBMIT／駁回沒有檔案輸入；空檔、格式、大小、編碼不適用。 | E | REQ_F4114.2.3～5 | N/A |

2026-11-12T01:54:36+08:00：缺少組裝類別先得 CS0103 紅燈；單專案建置與離線定向測試綠燈。記憶體守衛 DEFER，真實 SQL／HTTP 未驗。

U=單元、I=真實 SQL／HTTP 整合、E=Playwright 畫面與保存資料雙斷言。007d 未寫產品測試；007a～f 先寫失敗測試，再實作。每個 E 都須核對提示的單號／行數／原因與重讀的 PUR、INV 狀態一致。

| ID | 類別 | 情境及斷言 | 層級 | 需求 | 狀態 |
|---|---|---|---|---|---|
| PUR-RECV-001 | 正常 | Approved 訂單兩行分次收貨；草稿零 INV，最終核准後各行已收量及 movement 正確。 | I,E | REQ_F4114.2.2, REQ_F4114.2.4 | TODO |
| PUR-RECV-002 | 正常 | SYS 多級中間核准不過帳，最終決定只過帳一次；零步驟立即核准同交易。 | I | REQ_F4114.2.4 | TODO |
| PUR-RECV-003 | 正常 | Posted 入庫分次退貨；原 movement 不變，新增 PURCHASE_RETURN 出庫，原成本及可退量正確。 | I,E | REQ_F4114.7.2～3 | TODO |
| PUR-RECV-004 | 邊界 | 0／1001 行、零／負 ID、重複行號、零／負／七位小數量、空倉庫逐欄 400；1／1000 行合法。 | U,I,E | REQ_F4114.2.2, REQ_F4114.7.2 | TODO |
| PUR-RECV-005 | 邊界 | 月底、跨年、閏日與成本六位精度；本位幣單位成本溢位整單拒絕。 | U,I | REQ_F4114.2.2 | TODO |
| PUR-RECV-006 | 邊界 | 批號量合計等於行量；序號逐件唯一；無追蹤物料禁止帶追蹤，必追蹤物料不得漏。 | I,E | REQ_F4114.2.2, REQ_F4114.7.2 | TODO |
| PUR-RECV-007 | 邊界 | 恰等可收／可退量成功，超最小精度一單位 422；同單多行來源須先聚合。 | I | REQ_F4114.2.4, REQ_F4114.7.3 | TODO |
| PUR-RECV-008 | 重複／衝突 | 兩連線對同一訂單行／入庫行爭餘量，最多一單成功，預留與過帳總量不超來源。 | I | REQ_F4114.2.4, REQ_F4114.7.3 | TODO |
| PUR-RECV-009 | 重複／衝突 | 舊版 EDIT／DELETE／SUBMIT、重複 SYS 決定回 409，不二次預留、釋回或過帳。 | I,E | REQ_F4114.2.3～6, REQ_F4114.7.3 | TODO |
| PUR-RECV-010 | 重複／衝突 | 相同保存快照的 INV 重放回原 movement；不同快照、同 sourceId 409。軟刪號碼不重用。 | I | REQ_F4114.2.4, REQ_F4114.2.6 | TODO |
| PUR-RECV-011 | 部分結果 | 任一行超量、追蹤不符或 INV 失敗，全部來源預留／SYS／PUR／INV／稽核回滾；不得縮量或略過。 | I,E | REQ_F4114.2.4, REQ_F4114.7.3 | TODO |
| PUR-RECV-012 | 部分結果 | 全部行不足與 0 行分別 422／400，列出每行原因；畫面警告且重讀證明零變更。單據整單原子，部分成功不適用。 | I,E | REQ_F4114.2.4, REQ_F4114.7.3 | TODO |
| PUR-RECV-013 | 權限 | 匿名 401、缺 VIEW／CREATE／EDIT／DELETE／SUBMIT 分別 403；僅有 CREATE／EDIT 者仍可讀專用來源或保存上下文。 | I,E | REQ_F4114.2.1～6, REQ_F4114.7.1～3 | TODO |
| PUR-RECV-014 | 權限 | 跨公司、本人／部門／分店範圍外資源 404；偽造公司、來源、成本、狀態和 movement ID 不得修改資料。 | I | REQ_F4114.2.1～4, REQ_F4114.7.1～3 | TODO |
| PUR-RECV-015 | 狀態 | Draft 可修訂／刪／送審；Rejected 修訂後回 Draft；Pending 只 SYS 決定；Posted 不可修訂／刪。 | I,E | REQ_F4114.2.3～6 | TODO |
| PUR-RECV-016 | 狀態 | 退貨 Draft／Pending／Rejected／Posted 同上；駁回精確釋回一次且不寫 INV，重送重新驗量。 | I | REQ_F4114.7.2～3 | TODO |
| PUR-RECV-017 | 關聯 | 未核准、取消、軟刪、跨公司或物料不符採購單不得收貨；已收貨訂單即使全退也不能取消。 | I,E | REQ_F4114.1.6, REQ_F4114.2.2 | TODO |
| PUR-RECV-018 | 關聯 | 退貨只引用 Posted 入庫與該行原 movement；MD 值後來變更不重估成本，保存歷史仍可讀。 | I | REQ_F4114.7.1～3 | TODO |
| PUR-RECV-019 | 關聯 | 退貨批序號必來自原入庫且目前位置可出庫；已由其他來源耗盡時整單 422。 | I,E | REQ_F4114.7.3 | TODO |
| PUR-RECV-020 | 錯誤／交易 | 在 SYS 建例、預留、INV 第二行、狀態更新後注入故障，交易全回滾；原版本可重試。 | I | REQ_F4114.2.4, REQ_F4114.7.3 | TODO |
| PUR-RECV-021 | 錯誤／介面 | 400 逐欄、409 要重載、422 顯示行號／可用量、500 traceId、逾時／斷線不顯示成功；重讀核對零變更。 | I,E | REQ_F4114.2.2～4, REQ_F4114.7.2～3 | TODO |
| PUR-RECV-022 | 檔案 | 收貨／退貨無上傳，空檔／格式／編碼／列數不適用；VIEW CSV 驗 0 行不下載、10000 行、BOM、公式防護及錯誤整檔取消。 | E | REQ_F4114.2.1, REQ_F4114.7.1 | TODO（上傳 N/A） |
| PUR-RECV-023 | 介面 | 4.4～4.6、4.16～4.18 正常／空／無結果／無權／驗證／500／成功／拒絕三語亮暗截圖；訊息和保存資料相符。 | E | REQ_F4114.2.1～6, REQ_F4114.7.1～3 | TODO |
| PUR-RECV-024 | 審計 | 建立、修訂、送審、SYS 決定、過帳與退貨都有對應審計，回滾時不留假成功稽核。 | I | REQ_F4114.2.2～5, REQ_F4114.7.2～3 | TODO |
| PUR-RECV-025 | 成本 | 多幣別採購單使用保存匯率與未稅本位幣成本；入庫及部分退貨同單位成本，與 FI 應付未自動聯動。 | I | REQ_F4114.2.4, REQ_F4114.7.3 | TODO |
| PUR-RECV-026 | 權限／錯誤 | 換公司或來源選項 403／404／500／逾時，清除舊行和倉儲選項；禁用操作顯示具體原因。 | E | REQ_F4114.2.1～3, REQ_F4114.7.1～2 | TODO |
| PUR-RECV-027 | 設計契約 | PUR 僅使用 INV 專用外部契約；退貨專用來源與原 movement 成本，禁止通用 PostAsync／銷售出庫。 | U,I | REQ_F4114.7.3 | TODO |
| PUR-RECV-028 | 資料庫 | 同公司 FK、每個 FK 索引、來源行／號碼唯一、非負預留與上限 CHECK、Posted 不可變及 Down migration。 | I | REQ_F4114.2.2～6, REQ_F4114.7.2～3 | TODO |

## WU-PUR-006d 採購單設計情境（尚未執行）

依 `myerp-test-scenarios` 覆蓋正常、邊界、重複／衝突、部分結果、權限、狀態、關聯、錯誤與檔案。每個 E2E 須同時驗畫面訊息／樣式及 API 重讀的實際單號、行、狀態、請購餘量或零變更；006d 沒有產品測試，以下全為待實作，不標 PASS。U＝單元、I＝真實 SQL／HTTP 整合、E＝Playwright。

| ID | 類別 | 情境與斷言 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-PO-001 | 正常 | 人工建兩行草稿、再以目前版本修訂；單號、保存行、原幣／本位幣金額與重讀一致，請購餘量及 INV 異動不變。 | U,I,E | REQ_F4114.1.1, REQ_F4114.1.2, REQ_F4114.1.3 | TODO（006a～f） |
| PUR-PO-002 | 正常 | 005e4 轉出的決標行保留來源與商業快照；訂單詳情可追溯原詢價／決標，EDIT 不改其商業值。 | I,E | REQ_F4114.1.1, REQ_F4114.1.3, REQ_F4114.6.3 | TODO（005e4、006） |
| PUR-PO-003 | 正常 | 兩張訂單分次引用同一核准請購行，提交後已轉量為兩單總和，餘量相應減少；SYS 最終核准不再次增加。 | I,E | REQ_F4114.1.4, REQ_F4114.3.1 | SUBMIT 兩單累計 PASS（006b1 PUR-PO-SUB-001）；SYS 最終核准不重消耗待 006b2 |
| PUR-PO-004 | 正常 | 多級 SYS 中間核准不重消耗，最終核准保存歷史；駁回帶意見並釋回，修訂重送只按當時餘量再消耗。 | I,E | REQ_F4114.1.4, REQ_F4114.1.5 | I PASS（006b2：PUR-PO-DEC-001／002）；E 待 006e／006f |
| PUR-PO-005 | 邊界 | 0／1001 行、零／負 ID、重複行號、零／負／超六位數量、負／超六位單價、無幣別、超長備註逐欄 400；1／1000 行及零單價合法。 | U,I,E | REQ_F4114.1.2, REQ_F4114.1.3 | U 輸入驗證 17/17（006a 切片）；I／E 與來源引用待驗 |
| PUR-PO-006 | 邊界 | 月底、跨年及閏日業務日與進項稅率有效期端點準確；行金額逐行 `Money.Round` 後加總，溢位整單拒絕，不由 SQL 默默取整。 | U,I | REQ_F4114.1.2, REQ_F4114.1.3 | U 輸入閏日／六位精度及行級原幣／本位幣、進項稅、行與表頭 `DECIMAL(19,4)` 溢位 4/4 通過；MD 有效期及 I 待 SQL 驗 |
| PUR-PO-007 | 邊界 | 同一訂單多行引用同一請購行須先聚合；剛好等於餘量成功，超過最小精度一單位整單 422。 | U,I | REQ_F4114.1.4 | I PASS（006b1 PUR-PO-SUB-002／003） |
| PUR-PO-008 | 重複／衝突 | 雙連線建立單號唯一；已軟刪／已取消號碼及決標來源不重用；舊版 EDIT／DELETE／SUBMIT／CANCEL 回 409，保存資料不變。 | I | REQ_F4114.1.2, REQ_F4114.1.3, REQ_F4114.1.4, REQ_F4114.1.6 | 建立雙連線單號唯一 PASS（006a4 PUR-PO-TXN-002）；舊版 EDIT 409 PASS（006a3／006a4 HTTP）；DELETE 舊版本 409 PASS（006c1 PUR-PO-DEL-004／HTTP）；SUBMIT 409 PASS（006b1）；CANCEL 待 006c2 |
| PUR-PO-009 | 重複／衝突 | 兩個連線同時提交對同一請購剩餘量的競爭訂單，最多一個成功；量不超核准量，失敗訂單仍為草稿且無 SYS 實例。 | I | REQ_F4114.1.4 | PASS（006b1 PUR-PO-SUB-006） |
| PUR-PO-010 | 部分結果 | 任一行來源不足、MD 失效或 SYS 建流程故障，整單零消耗、零審批、零狀態變化；無逐行成功、靜默略過或部分成功。 | I,E | REQ_F4114.1.4 | I：來源不足／失效、請購量與 SYS 實例故障整單零變更 PASS（006b1）；MD 供應商／物料失效 HTTP 422 零變更 PASS（006b3 SUB-HTTP-002）；E 待 006f |
| PUR-PO-011 | 部分結果 | 0 行提交為 400；全部來源不足回 422 並列每個衝突行與可用量；成功才顯示實際變更筆數，全部失敗顯示警告及原因。 | I,E | REQ_F4114.1.4 | TODO（006） |
| PUR-PO-012 | 權限 | 匿名 401，缺各動作權限 403；只有 CREATE／EDIT 無 VIEW／MD.VIEW 仍可取得專用選項／詳情；跨公司與本人／部門／分店範圍外 404。 | I,E | REQ_F4114.1.1, REQ_F4114.1.2, REQ_F4114.1.3 | CREATE／EDIT／VIEW／SUBMIT 匿名 401、缺權 403、跨公司及範圍外 404 PASS（006a2～006a4、006b3）；選項端點待 006e |
| PUR-PO-013 | 權限 | 偽造公司、擁有人、單號、金額及決標來源欄位不得改變持久資料；來源選項與已保存引用不能洩漏其他公司資料。 | I | REQ_F4114.1.2, REQ_F4114.1.3 | TODO（006） |
| PUR-PO-014 | 狀態 | Draft 可編輯／刪除／提交；PendingApproval 只能 SYS 決定；Approved 不可修改／刪除／重送；Rejected 修訂後回 Draft；Cancelled 不可再操作。每種非法操作 422。 | I,E | REQ_F4114.1.3, REQ_F4114.1.4, REQ_F4114.1.5, REQ_F4114.1.6 | DELETE：Draft／Rejected 可刪、PendingApproval／Approved／Cancelled 422 PASS（006c1 PUR-PO-DEL-001～003）；其餘待 006c2／006f |
| PUR-PO-015 | 狀態 | 重複提交／核准／駁回／取消均不得二次消耗或釋回；審批過時決定 409 且 SYS 實例與 PUR 狀態一同回滾。 | I | REQ_F4114.1.4, REQ_F4114.1.5, REQ_F4114.1.6 | 提交／核准／駁回 PASS（006b1、006b2 PUR-PO-DEC-001／003；HTTP 006b3 SUB-HTTP-002／003）；取消待 006c |
| PUR-PO-016 | 關聯 | 未核准、軟刪、跨公司、物料不符或已無餘量請購行拒絕；保存後 MD 停用仍能讀歷史，修改或提交重新驗引用並列具體行。 | I,E | REQ_F4114.1.1, REQ_F4114.1.2, REQ_F4114.1.4 | TODO（006） |
| PUR-PO-017 | 關聯 | 未提交草稿軟刪不釋回；核准且無入庫引用可取消並只釋回一次；已有入庫引用不得取消／刪除，INV 異動不受採購單取消直接修改。 | I,E | REQ_F4114.1.6 | 未提交草稿／駁回單軟刪不釋回 PASS（006c1 PUR-PO-DEL-001／002）；取消待 006c2、入庫引用待 007 |
| PUR-PO-018 | 錯誤／交易 | 注入 SYS 發號後、第二行保存後、請購量更新後或 SYS 實例寫入後故障，表頭／行／單號流水號／請購量／審批全回滾；修復後原版本可重試。 | I | REQ_F4114.1.2, REQ_F4114.1.4 | CREATE 發號後兩種 INSERT 故障全回滾 PASS（006a4）；請購量／SYS 實例故障待 006b |
| PUR-PO-019 | 錯誤 | 400 逐欄提示、409 保留草稿並要求重載、422 指出狀態／引用／行及可用量、500 顯示 traceId、逾時／斷線不顯示成功；API 重讀核對零變更。 | I,E | REQ_F4114.1.2, REQ_F4114.1.3, REQ_F4114.1.4 | TODO（006） |
| PUR-PO-020 | 檔案 | 建立／編輯／審批沒有上傳，空檔、格式、編碼、列數不適用；VIEW CSV 驗 0 行不下載、10000 行上限、BOM、公式防護、取消及任一頁錯誤整檔失敗。 | E | REQ_F4114.1.1 | TODO（006 前端；上傳 N/A） |
| PUR-PO-021 | 介面 | 4.1～4.3 正常、空列表、無搜尋結果、無權、驗證、成功、全部拒絕、500／斷線各有三語亮暗截圖；提示筆數與列表／API 保存行及請購餘量一致。 | E | REQ_F4114.1.1, REQ_F4114.1.2, REQ_F4114.1.3, REQ_F4114.1.4 | TODO（006 前端） |
| PUR-PO-022 | 錯誤／交易 | SYS 駁回或 CANCEL 釋回中注入故障：審批、訂單狀態與全部請購行量不變；恢復後只釋回一次。 | I | REQ_F4114.1.5, REQ_F4114.1.6 | 駁回 PASS 3/3（006b2 PUR-PO-DEC-005）；CANCEL 待 006c |
| PUR-PO-023 | 關聯 | 訂單核准不產生 INV 異動；後續已核准採購入庫用保存明細、固定 PURCHASE_INBOUND 來源及 INV 公開契約原子過帳，重放不重複。 | I | REQ_F4114.1.4 | TODO（006、007、INV-015b） |
| PUR-PO-024 | 權限／介面 | 無 VIEW 的有權編輯者只見自身 EDIT 範圍內的保存資料；換公司、403／404／500 或逾時清舊詳情／選項，停用按鈕有具體原因。 | I,E | REQ_F4114.1.1, REQ_F4114.1.3 | TODO（006 前端） |

## WU-PUR-005f3e QUOTE 專屬上下文與選項

依 `myerp-test-scenarios`：單一唯讀 GET 無重複代碼、版本寫入衝突、批次部分結果或檔案輸入；這些情境不適用。報價 POST 的重複供應商、409 與原子結果由 PUR-RFQ-042～050 驗證，這次 GET 不寫資料。

| ID | 類別 | 情境與斷言 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-QUOTE-CTX-001 | 正常／邊界 | QUOTE 受權端點以指定閏日返回保存詢價行、邀請、啟用幣別及該日進項稅率；省略日期用 IClock。空選項明示不可選，過期／未生效／銷項稅率不列入。 | U,I,E | REQ_F4114.5.2 | 前端契約 1/1、日期切換 E2E 1/1；2026-11-28 真實 HTTP／SQL `QuotationContextRequiresQuoteOnlyAndHidesOutOfScopeRfqs` 1/1（閏日／次日、停用幣別、銷項稅率排除）通過 |
| PUR-QUOTE-CTX-002 | 權限／跨公司 | 有 QUOTE 但無 RFQ.VIEW／MD.VIEW 可讀同公司範圍內上下文及選項；無 QUOTE 403、匿名 401、他公司／本人範圍外 404，不洩漏選項。 | I,E | REQ_F4114.5.2 | QUOTE-only 模擬 E2E 1/1；真實 HTTP 401／403、本人 200／範圍外 404 通過；2026-11-28 新增真實 SQL `QuoteContextFollowsQuoteScopeAndHidesOtherCompanies`（本人／部門／分店／公司、只有 RFQ.VIEW 403、他公司全權限 404）及真實 HTTP 雙向跨公司 404，均通過 |
| PUR-QUOTE-CTX-003 | 狀態／關聯 | 草稿、已關閉詢價可讀狀態但無寫入入口；停用／軟刪幣別、稅別及日期不適用稅率不可選，修訂引用過時時由保存端拒絕。 | I,E | REQ_F4114.5.2～3 | 2026-11-28 真實 HTTP `QuotationContextHidesRetiredChoicesAndOtherCompanyRfqs`：草稿／已關閉可讀狀態；軟刪幣別、停用／軟刪稅別、軟刪稅率、未生效稅率不列入，`Both` 稅別列入，通過。前端草稿／關閉無寫入入口與保存端拒絕沿用 PUR-QUOTE-UI 既有測試 |
| PUR-QUOTE-CTX-004 | 錯誤／日期 | 無效日期 400；上下文 404／422／500／逾時清舊資料與選項，500 顯示 traceId；換公司與換報價日後舊請求不可覆蓋新選項。 | I,E | REQ_F4114.5.2 | 500／traceId 模擬 E2E 1/1；真實 HTTP 無效字串 400、404 通過；2026-11-28 發現並修正 `0001-01-01` 被當作有效日期（`DateOnly?` 與 default 比較恆為 null），現回 400。逾時與換公司舊請求覆蓋只有元件代碼的世代守衛，尚無專屬 E2E，列入 backlog |
| PUR-QUOTE-CTX-005 | 部分結果／檔案 | 本 GET 無批次寫入、部分成功、全部略過或檔案；選項 0 筆只顯示空選擇，不報成功，不送保存請求。 | I,E | REQ_F4114.5.2 | 2026-11-28 真實 HTTP 空公司 200 + 兩個空陣列；模擬 E2E `PUR-QUOTE-CTX-005` 1/1（只剩預設選項、保存顯示欄位錯誤、0 個寫入請求、無成功訊息，亮／暗截圖） |

## WU-PUR-005f3b 報價首版／修訂／撤回畫面

依 `myerp-test-scenarios` 設計。單張報價寫入為原子操作，E2E 同時核對提示、列表／詳情保存資料與請求次數。

| ID | 類別 | 情境與斷言 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-QUOTE-UI-005 | 正常 | 已發送詢價的受邀供應商逐行建立首版；保存後列表／歷史顯示實際版本與六位價格。修訂只新增不可變版本；撤回確認後仍能讀歷史。 | U,E | REQ_F4114.5.2～4 | PASS：契約及模擬 E2E 核對三次寫入、版本、列表與兩版歷史；真實寫入 API 沿用 005d3／d4 驗收。 |
| PUR-QUOTE-UI-006 | 邊界 | 空行、缺供應商、無效日期或有效期倒置、500 字備註上限、負價／七位小數／超上限、非三字母幣別及無效稅率均逐欄拒絕且零寫入；零價、閏日及六位價格可送出。 | U,E | REQ_F4114.5.2～3 | PASS：單元驗 0／1000／1001 行、重複行、日期、價格、幣別、稅率；E2E 空表單零寫入及六位價格／閏日保存，400 行欄位顯示。 |
| PUR-QUOTE-UI-007 | 重複／衝突 | 同受邀供應商重複首版與舊版本修訂／撤回回 409；清過時詳情並提示重載，原草稿留存供核對。 | E | REQ_F4114.5.2～4 | PASS：修訂 409 清過時詳情、保留草稿並確認重載；重複首版／撤回的伺服器 409 已於 005d3／d4 真實 SQL／HTTP 驗收。 |
| PUR-QUOTE-UI-008 | 部分結果／狀態 | 單張整單提交無部分成功或略過；全部失敗不顯示成功。草稿／關閉詢價、過期、撤回及已決標報價無操作入口，伺服器 422 有具體原因。 | E | REQ_F4114.5.2～4 | PASS：單張 422／500 零成功；撤回後無修訂；過期 422 清舊詳情。草稿／關閉及已決標的後端拒絕沿用 005d3／d4 測試。 |
| PUR-QUOTE-UI-009 | 權限／關聯 | VIEW-only 無寫入按鈕；QUOTE 權限與目前公司一致才可操作；供應商非受邀或 MD 幣別／稅率失效時顯示原因，跨公司／資料範圍 404 不保留舊資料。 | E | REQ_F4114.5.2～4 | PASS（本切片）：VIEW-only 截圖無寫入入口；005f3e 後 QUOTE-only 可由 QUOTE 專屬上下文讀保存行與選項。受邀供應商只來自保存詢價；MD 失效碼有三語映射。真實 API 守衛沿用 005d3／d4。 |
| PUR-QUOTE-UI-010 | 錯誤 | 400／409／422 逐欄或狀態原因、500 traceId、逾時／網絡中斷均不報成功、草稿可重試；撤回取消或連點只發 0／1 請求。 | E | REQ_F4114.5.2～4 | PASS：400 行欄位、409、422、500 traceId、斷線及取消零寫入；草稿保留，撤回帶版本且進行中禁連點。逾時沿用共用 HTTP 錯誤處理。 |
| PUR-QUOTE-UI-011 | 檔案／顯示 | 此操作沒有檔案輸入、批次及下載，空檔／格式／大小／編碼不適用；正常、驗證、409、422、500、無權、成功、撤回亮暗逐張截圖，簡英正常截圖。 | E | REQ_F4114.5.2～4 | PASS：新增 25 張 4.14 截圖逐張審查；原有上下文權限缺口列 005f3e，畫面已有原因；檔案／批次 N/A。 |

## WU-PUR-005f3a 報價唯讀列表與不可變修訂歷史

依 `myerp-test-scenarios` 設計；E2E 同時核對畫面與 GET 回應的保存版、狀態、行值，並斷言沒有寫入請求。唯讀介面不提供成功提示。

| ID | 類別 | 情境與斷言 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-QUOTE-UI-001 | 正常／狀態／關聯 | 受權列表顯示 Active／Withdrawn／Awarded 及即時過期標記；詳情保留全部修訂與六位原幣價格、幣別、稅率 ID；已停用 MD 引用仍顯示保存值，不發寫入；三語亮暗畫面可讀。 | U,E | REQ_F4114.5.1 | 通過；契約及模擬 E2E，真實 GET 已於 005d2 驗收 |
| PUR-QUOTE-UI-002 | 邊界／重複與衝突／部分結果／錯誤 | 供應商 ID 非正整數及無效日期不發請求；空列表／搜尋無結果分明；分頁與篩選同步 URL；404、403、500 traceId、斷線清過時詳情和列表；延遲舊回應不可覆蓋新公司／篩選。唯讀無批次部分成功、全部略過或 409 寫入衝突；逾時由共用 HTTP 錯誤顯示處理。 | E | REQ_F4114.5.1 | 通過；供應商 0、無效日期、空與無結果、404／500、斷線、延遲舊回應及換公司 |
| PUR-QUOTE-UI-003 | 權限／跨公司／資料範圍 | 缺 QUOTATION.VIEW 導至 403 且零查詢；跨公司和範圍外 404 由後端已驗收契約守衛，換公司立即清舊資料。 | I,E | REQ_F4114.5.1 | 通過；無權零查詢、切公司與無效草稿篩選清舊資料，真實 GET 已於 005d2 驗收 |
| PUR-QUOTE-UI-004 | 檔案／顯示 | 本唯讀切片無上傳或下載，空檔、格式、列數、編碼不適用；正常、詳情、空、驗證、500、無權截亮暗畫面並逐張檢視，英簡各正常一張。 | E | REQ_F4114.5.1 | 通過；4.14 新截圖 16 張、4.11 入口 2 張逐張檢視；無權使用既有 403 頁 |

## WU-PUR-005f2c 詢價 CSV／列印

依 `myerp-test-scenarios` 列齊情境。匯出與列印均唯讀；E2E 同時核對畫面筆數／原因與下載內容或零下載，並核對 API 保存資料未變。匯入檔案不適用。

| ID | 類別 | 情境與斷言 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-RFQ-092 | 正常 | 已套用篩選跨頁讀取全部詢價及保存行，CSV 筆數與畫面提示、GET 資料一致；列印重新讀保存詳情。 | U,E | REQ_F4114.4.1 | PASS：201 張跨頁、201 個保存行與提示一致；列印 GET 二次，print media 只含保存詳情。 |
| PUR-RFQ-093 | 邊界 | 0 行不下載；10000 行可下載、10001 行拒絕；含閏日及六位小數保持文字精度。 | U,E | REQ_F4114.4.1 | PASS：單元驗 10000／10001；E2E 0 行、單張 10001 行零下載，閏日與 1.123456 CSV 保存。 |
| PUR-RFQ-094 | 重複／衝突 | 跨頁重複 ID、總筆數改變、詳情版本改變或軟刪 404 均整檔拒絕；查詢／公司變動取消舊匯出。 | U,E | REQ_F4114.4.1 | PASS：跨頁重複、保存版本變更及篩選取消 E2E；404 列印零輸出，公司變更沿用同一取消守衛。 |
| PUR-RFQ-095 | 部分結果 | 中途一頁或一張詳情失敗、全部失敗、全為 0 行均零部分下載，顯示具體原因，不報成功。 | E | REQ_F4114.4.1 | PASS：500 traceId、403、0 行、取消與版本衝突均零部分下載；整檔成功後才顯示行數。 |
| PUR-RFQ-096 | 權限／狀態 | VIEW-only 隱藏匯出；EXPORT + VIEW 才顯示；列印與匯出可讀 Draft／Sent／Closed，但 403／範圍外 404 零檔／零列印。 | E | REQ_F4114.4.1 | PASS：VIEW-only 無按鈕、detail 403 不下載、print 404 不列印；唯讀狀態不變由 RFQ VIEW 測試覆蓋。 |
| PUR-RFQ-097 | 關聯／錯誤 | 引用的 MD 物料或供應商停用仍輸出保存 ID／快照，不重新寫入；400／409／422／500 traceId、逾時與斷線顯示原因並可重試。 | E | REQ_F4114.4.1 | PASS：CSV 保存快照與 500 traceId／重試 E2E；通用 400／409／422／逾時／斷線沿用 RFQ 列表及請購匯出共用錯誤顯示測試。 |
| PUR-RFQ-098 | 檔案／顯示 | BOM UTF-8、逗號／引號／換行及公式前導安全；匯入空檔、格式、大小、編碼、缺／多欄不適用。4.11 正常、空、錯誤、匯出結果亮暗與英簡排版截圖檢視。 | U,E | REQ_F4114.4.1 | PASS：BOM／引號／換行／公式單元及瀏覽器下載；19 張匯出／列印亮暗與英簡截圖逐張審查；匯入 N/A。 |

## WU-PUR-005f2b 詢價發送／關閉畫面

依 `myerp-test-scenarios` 逐類檢查；單張原子操作沒有部分成功或逐行略過，亦不接收檔案。每個 E2E 同時斷言提示與列表／詳情狀態。

| ID | 類別 | 情境與斷言 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-RFQ-086 | 正常 | 草稿 SEND、已發送 CLOSE 均先確認，帶保存版本；回應後詳情與列表狀態／版本一致，成功訊息含單號。發送確認明示不寄電郵。 | U,E | REQ_F4114.4.4, REQ_F4114.4.5 | PASS：契約單元與模擬 E2E；兩次 POST 版本 2／3，列表與詳情為 Sent／Closed。 |
| PUR-RFQ-087 | 邊界／衝突 | 取消不發請求，連點只發一次；舊版本 409 清過時詳情、重查列表，沒有成功提示。 | E | REQ_F4114.4.4, REQ_F4114.4.5 | PASS：取消 0 POST；單次確認 1 POST；409 刷新已發送列表且錯誤可見。 |
| PUR-RFQ-088 | 部分結果 | 單張操作原子執行；部分成功／全部略過不適用。全部失敗時狀態不變，顯示原因。 | E | REQ_F4114.4.4, REQ_F4114.4.5 | PASS：422／500 保持 Draft，沒有成功提示；批次部分結果 N/A。 |
| PUR-RFQ-089 | 權限／狀態 | VIEW 無 SEND／CLOSE 時無按鈕；草稿只能 SEND，已發送只能 CLOSE，關閉後均不可執行；換公司清舊詳情。 | E | REQ_F4114.4.4, REQ_F4114.4.5 | PASS：VIEW-only 與 Draft／Sent／Closed 模擬 E2E；換公司清詳情沿用 PUR-RFQ-072。 |
| PUR-RFQ-090 | 關聯／錯誤 | MD 引用失效、來源餘量不足由 422 顯示後端原因；400／404／409／500 traceId、斷線／逾時不顯示成功，可重試適用情況。 | U,E | REQ_F4114.4.4, REQ_F4114.4.5 | PASS：MD 物料／供應商、請購來源錯誤映射單元；供應商 422、狀態 422、409、500 traceId 動作 E2E；通用 400／404、斷線／逾時沿用 RFQ 列表／表單 E2E。 |
| PUR-RFQ-091 | 檔案／顯示 | 不接收檔案，空檔、格式、大小及編碼不適用；正常、確認、成功、錯誤、無權及空畫面作三語亮暗檢視。 | E | REQ_F4114.4.4, REQ_F4114.4.5 | PASS：4.11 動作 20 張新增亮暗／英簡截圖逐張審查；既有 4.11 空畫面沿用 PUR-RFQ-072；檔案 N/A。 |

## WU-PUR-005e4 決標轉採購單草稿（設計切片）

依 `myerp-test-scenarios` 按正常、邊界、重複、部分結果、權限、狀態、關聯、錯誤及檔案逐類檢查。轉單是整批原子操作，部分成功與逐行略過不適用；全部失敗必須列原因。資料結構定向真實 SQL 1/1 已通過；2026-11-28 CONVERT 真實 SQL／SYS 發號專項 `RfqConversionTransactionTests` 6/6 及帶資料 Down／Up 通過；前端畫面與資料雙斷言屬 005f。

| ID | 類別 | 情境與斷言 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-RFQ-060 | 正常 | 同供應商同原幣兩個決標行以目前版本轉一張 Draft；回應單號／行數／新版本與 SQL 重讀一致，決標商務快照不變，請購已轉量及 INV 異動為零。 | I | REQ_F4114.6.3 | PASS（2026-11-28 `RfqConversionTransactionTests`：雙行以真實 SYS 規則發 `PO20280229001`，SQL 重讀單號／行數／合計／來源連結與回應一致，決標快照逐欄不變；轉單不讀寫請購已轉量與 INV，請購餘量依 backlog 於 006 提交才消耗） |
| PUR-RFQ-061 | 邊界 | 空集合、重複／零／負 ID、1001 行回 400；單一行及 1000 行上限可保存；原幣單價六位、金額四位及月底／閏日業務日不被自動取整或改日。 | U,I | REQ_F4114.6.3 | PASS（輸入集合單元 9/9；真實 SQL 1000 行上限一單、六位單價 0.000001／數量 999999.999999、四位金額及 2028-02-29 業務日原值保存） |
| PUR-RFQ-062 | 重複／衝突 | 同版雙連線只一張單；舊詢價版本 409，新版重送已轉行 422；已軟刪或取消的後續訂單單號及來源連結不重用。 | I | REQ_F4114.6.3 | PASS（I）：雙連線同版只一單、一個流水號，另一方 409；舊版 409、新版重送 422、來源連結不可改刪。訂單取消功能屬 006，屆時補驗取消後不重用單號 |
| PUR-RFQ-063 | 部分結果 | 同批一行已轉或另一供應商／詢價／原幣時整批零新單、零來源連結及零單號消耗；全部已轉亦拒絕且指明衝突行，不提供部分成功或靜默略過。 | I,E | REQ_F4114.6.3 | PASS（I：一行已轉＋一行未轉整批 422 並指明衝突決標 ID；混供應商／混匯率零新單、零連結、零流水號）；E 待 005f |
| PUR-RFQ-064 | 權限 | 匿名 401、缺 CONVERT 403；他公司及本人／部門／分店範圍外詢價 404，來源 ID 不能洩漏他公司資料。 | I,E | REQ_F4114.6.3 | PASS（I：HTTP 401／403／本人範圍；真實 SQL 他公司、分店、部門、本人範圍外 404 且不耗號）；E 待 005f |
| PUR-RFQ-065 | 狀態／關聯 | Draft、Closed、已刪詢價不可轉；未決標行、錯誤供應商、跨行原幣／本位幣／保存匯率不一致（ADR-210）或已轉來源拒絕；後續 MD 停用或匯率改變不改既有決標快照。 | I | REQ_F4114.6.3 | PASS（Draft／Closed 422 `PUR.RFQ.NOT_OPEN`、已刪 404、錯供應商、混匯率、已轉來源拒絕；轉單只讀決標快照，不再查 MD） |
| PUR-RFQ-066 | 錯誤／交易 | Body 錯誤 400、舊版 409、商業衝突 422；SYS 發號後或第二行／來源連結 INSERT 注入 500，訂單、連結、版本及單號流水號全回滾，同版恢復後可成功。 | I | REQ_F4114.6.3 | PASS（HTTP 400／422、SQL 409／422；真實 SYS 發號後於訂單行或來源連結 INSERT 注入故障，訂單／行／連結／詢價版本／流水號全回滾，同版重試成功發 001） |
| PUR-RFQ-067 | 介面 | 成功時三語顯示實際單號與行數並以 API／保存來源核對；0 行、部分衝突、全部已轉時用警告及逐行原因；400／409／422／500 traceId、逾時及斷線不顯示成功。亮暗截圖檢查。 | E | REQ_F4114.6.3 | PASS（前端單元 2、模擬 E2E 6；真實 SQL 來源連結 1/1；詳見下表） |
| PUR-RFQ-068 | 檔案 | CONVERT 不上傳檔案，空檔、格式、大小、行數與編碼錯誤不適用；005f CSV 匯出另驗。 | E | REQ_F4114.6.3 | N/A（無檔案輸入） |
| PUR-RFQ-069 | 關聯／遷移 | 既有 `pur_purchase_order`／`pur_purchase_order_line` 發票來源資料在 005e4 migration Up／Down 後保留原 ID、公司、供應商、幣別、匯率、行價與三單比對可讀性；新轉單來源唯一連結只指同公司既有訂單行與決標。 | I | REQ_F4114.1.2, REQ_F4114.6.3 | PASS（2026-11-28 臨時庫 `myerp_rt_pur005e4`：舊格式訂單／行在 Down 至 `Purchasing_OrderRateSnapshot` 與再 Up 後 ID、公司、供應商、幣別、匯率、版本、數量、單價、稅率逐欄不變，新欄為 NULL、來源表與觸發器重建；驗後刪庫） |

### WU-PUR-005f3d 轉單畫面情境（REQ_F4114.6.3、REQ_IO_A4.15.2）

| 類別 | 情境與畫面／資料雙斷言 | 層級 | 結果 |
|---|---|---|---|
| 正常 | 同供應商、原幣、本位幣與匯率的兩行確認後 POST 一次；成功訊息的單號與行數須與重讀來源的兩行一致。 | U,E,I | PASS |
| 邊界 | 0 決標顯示原因不 POST；全部取消勾選時按鈕停用並說明；1～1000 行由後端限制，前端只分組。 | U,E,I | PASS；1000 行屬 005e4 真實 SQL |
| 重複與衝突 | 一行已轉＋一行未轉時全批 422，顯示已轉行及單號，未轉行仍可選；全部已轉不再提供 POST；409 舊版重新讀取。 | E,I | PASS |
| 部分結果 | 轉單為原子操作，部分成功／逐行略過不適用；衝突時顯示整批失敗及逐行保存狀態，不能顯示成功。 | E,I | PASS |
| 權限 | 無 CONVERT 權不顯示轉單操作；跨公司及資料範圍外由後端 404 拒絕。 | E,I | PASS（E 只讀角色仍可看快照、無轉單操作；後端 005e4 真實 SQL） |
| 狀態／關聯 | Closed 不提供轉單；已轉來源保留原採購單號，訂單軟刪亦可追溯。 | E,I | PASS（軟刪顯示由查詢實作；SQL 定向驗來源） |
| 錯誤 | 400 欄位原因、409、422、500 traceId、逾時、斷線均不顯示成功；API 回傳與重讀來源不符亦不宣稱成功。 | E | PASS（六種拒絕及來源不符均有 E2E） |
| 檔案 | 轉單無檔案輸入，空檔／格式／大小／編碼不適用。 | — | N/A |

## WU-PUR-005f3c2 決標畫面情境（REQ_F4114.6.2 v1.1，介面 4.15）

| ID | 類別 | 情境與雙斷言 | 層級 | 結果 |
|---|---|---|---|---|
| PUR-AWARD-UI-001 | 正常／關聯 | 可比報價填理由、確認後只 POST 一次；畫面顯示保存快照，刷新後 GET 仍顯示相同理由／原幣／本位幣值；保存版本遞增。 | I,E | PASS：定向 SQL 與模擬 E2E |
| PUR-AWARD-UI-002 | 邊界／驗證 | 空白理由、501 字不得送出；1 與 500 字可送。詢價無行、該行無報價、不可比報價均無決標入口。 | E | PASS：空白與無報價既有 E2E；2026-11-28 補 1 字、500 字原樣送出並顯示快照，501 字被 maxlength 截至 500 字（亮暗截圖 reason500） |
| PUR-AWARD-UI-003 | 重複／衝突 | 已決標行只顯示不可變快照，無再次選擇；舊版 409 重讀，拒絕原因可見且不顯示成功。 | I,E | PASS：已決標重整及 409 E2E |
| PUR-AWARD-UI-004 | 部分結果 | 單行原子決標無部分成功、全部略過情境；全部失敗不顯示成功，保存筆數為零。 | E | PASS：422／500 模擬 E2E 無成功與零快照；後端原子性沿用 005e3 |
| PUR-AWARD-UI-005 | 權限 | 無 COMPARE 直接路由 403 且不發 GET；有 COMPARE 無 AWARD 只讀；他公司或資料範圍外 GET 404。 | I,E | PASS：路由、唯讀角色與真實 SQL 本人範圍 |
| PUR-AWARD-UI-006 | 狀態 | Draft 不可比價；Closed／已決標不可再決標；撤回、過期、缺匯率／稅率不可選，顯示具體原因。 | I,E | PASS：既有比價 E2E 與 AWARD SQL；2026-11-28 補 Closed 無選擇按鈕、每行三語顯示「詢價已不在可決標狀態」、零 POST |
| PUR-AWARD-UI-007 | 錯誤 | 400 理由欄位、409、422、500 traceId、逾時及斷線均顯示三語原因，重讀時清除舊資料。 | E | PASS：409／422／500 既有；2026-11-28 補 400 顯示通用訊息＋`reason` 欄位原因、逾時與斷線顯示無法連線，三者均無成功通知與快照、保留理由可重試，重整後清除錯誤 |
| PUR-AWARD-UI-008 | 檔案 | 不適用：此頁無檔案輸入或輸出。 | — | N/A |

2026-11-27T14:14:38+08:00：005f3c2 定向真實 SQL 1/1、前端全量 1305/1305、比價／決標模擬 E2E 7/7，修正 500 夾具後錯誤專項 1/1；後端全量因可用記憶體降至 0.94 GiB 主動停止，按資源不足規則延後。未完成邊界／錯誤情境同列於 `state.json.deferredVerifications`，本單元不宣稱全部驗收。

2026-11-28T02:14:49+08:00：延後驗證補驗完成。記憶體守衛 PASS；新增 PUR-AWARD-UI-002（1／500 字，501 字截斷）、007（400 欄位原因、逾時、斷線）、006（Closed）模擬 E2E，`purchasing-comparison.spec.ts` 11/11；後端 Debug 全量 4178/4178。產品代碼無需修改。


## WU-PUR-005e3 逐行決標

依 `myerp-test-scenarios` 按類別檢查。逐行只接受單筆，沒有部分成功／全部略過；檔案情境不適用。前端三語、500／逾時畫面與資料雙斷言留 005f。

| ID | 類別 | 情境與斷言 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-RFQ-055 | 正常／關聯 | 決標保存理由、報價行及價格／稅率／匯率／有效期間快照，詢價版本遞增，報價轉 Awarded；後續 MD 變更不改歷史。 | I | REQ_F4114.6.2 | PASS（RfqAwardTests） |
| PUR-RFQ-056 | 邊界／錯誤 | 空或超長理由、無效行／報價 ID 回 400／404；缺有效匯率／稅率、過期、撤回或未生效報價拒絕且無資料變更。 | I | REQ_F4114.6.2 | PASS（RfqAwardTests；未生效報價拒絕且零決標） |
| PUR-RFQ-057 | 重複／衝突／狀態 | 舊版 409，同版雙連線同一行只一筆；已決標行不得覆寫，關閉／草稿不得決標。 | I | REQ_F4114.6.2 | PASS（RfqAwardTests、RfqAwardEndpointEnforcesAuthenticationScopeAndOpenState） |
| PUR-RFQ-058 | 權限 | 匿名 401、缺 AWARD 權 403、他公司或資料範圍外 404。 | I | REQ_F4114.6.2 | PASS（RfqAwardTests 真實 SQL 公司隔離；受權 HTTP 本人範圍 404、401／403） |
| PUR-RFQ-059 | 錯誤／交易 | MD 查詢或保存故障整交易回滾；恢復後同版本可重試，快照不被 SQL UPDATE／DELETE 改寫。 | I | REQ_F4114.6.2 | PASS（RfqAwardTests；INSERT 注入故障後零決標、報價／版本未變，同版重試成功） |

2026-11-27T09:26:16+08:00 重試驗收：PUR-RFQ-055～059 對應 `RfqAwardTests` 與受權 HTTP 專項 2/2；Release 後端全量 4071/4071。正式 SQL 已有 `Purchasing_RfqAward` migration、`pur_rfq_award` 表及 `(company_id, rfq_line_id)` 唯一索引；帶 SQL DocGen 0 Error／613 Warning。本單元無新增畫面；決標畫面仍屬 WU-PUR-005f3c2。

## WU-PUR-005d4 撤回、過期、決標守衛與引用故障

依 `myerp-test-scenarios` 逐類核對：正常撤回、過期日界與版本零、重複撤回／雙連線競態、MD 停用或軟刪、公司與 QUOTE 權限、關閉／已決標狀態、400／409／422 與故障回滾均由下列 SQL／受權 HTTP 測試覆蓋。報價無批次略過或檔案上傳，部分成功、全部略過、空檔與編碼不適用；前端 500／逾時提示及畫面與資料雙斷言留 005f。

| ID | 類別 | 情境與斷言 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-RFQ-047 | 正常／狀態 | 撤回只改表頭且版本遞增；全部不可變修訂可查，舊版及同版重試分別 409／422。 | I | REQ_F4114.5.4 | PASS（QuotationMutationTests、QuotationWriteEndpoints） |
| PUR-RFQ-048 | 邊界／重複／衝突 | 目前修訂有效期早於業務日則 `isExpired` 為真且不可修訂／撤回；已決標同樣不可修改；雙連線撤回與修訂只有一次成功。 | I | REQ_F4114.5.1、5.3～4 | PASS（QuotationQueryTests、QuotationMutationTests） |
| PUR-RFQ-049 | 權限／關聯 | 撤回 401／403、他公司或範圍外 404；真實 MD 軟刪幣別／稅率時修訂 404，恢復後可重試。 | I | REQ_F4114.5.3～4 | PASS（QuotationMutationTests、QuotationWriteEndpoints、RfqDraftMutationEndpoints） |
| PUR-RFQ-050 | 錯誤／交易 | 輸入 400、舊版 409、狀態 422；MD 查詢注入故障不留下部分新修訂，原版本可重試。 | I | REQ_F4114.5.3～4 | PASS（QuotationMutationTests、QuotationWriteEndpoints）；500／逾時畫面留 005f |

## WU-PUR-005d3 報價首版與不可變修訂

依 `myerp-test-scenarios` 全類列舉。此 API 是整單原子寫入，不存在部分成功／全部略過；檔案情境不適用。前端尚未交付，三語訊息、逾時及 E2E 畫面與資料雙斷言留 005f。

| ID | 類別 | 情境與斷言 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-RFQ-042 | 正常／邊界 | 受邀供應商於已發送詢價首版報價、閏日與六位價格；零價修訂保留舊版價格、版號遞增。 | I | REQ_F4114.5.2～3 | PASS（QuotationMutationTests、QuotationWriteEndpoints） |
| PUR-RFQ-043 | 邊界／部分結果 | 空行、負價、七位小數、有效期倒置、錯配詢價行拒絕；整單零行保存。 | I | REQ_F4114.5.2 | PASS（QuotationMutationTests、HTTP 400） |
| PUR-RFQ-044 | 重複／衝突／狀態 | 同詢價供應商不可重複建立；修訂舊版 409；非受邀、關閉、過期、撤回、已決標拒絕；雙連線同版只有一次成功。 | I | REQ_F4114.5.2～3 | PASS（QuotationMutationTests、QuotationWriteEndpoints） |
| PUR-RFQ-045 | 權限／關聯 | 匿名 401、缺 QUOTE 403、跨公司或資料範圍外 404；MD 幣別停用／刪除及進項稅率無效拒絕，不跨模組查表。 | I | REQ_F4114.5.2～3 | PASS（QuotationMutationTests、QuotationWriteEndpoints；他公司及軟刪引用） |
| PUR-RFQ-046 | 錯誤／交易 | 400／409／422／500、MD 查詢故障及雙連線競態後無部分修訂；同版本可重試。 | I,E | REQ_F4114.5.2～3 | PASS（400／409／422、注入故障回滾與重試）；前端 E 留 005f |

## WU-PUR-005d2 報價受權唯讀查詢

依 `myerp-test-scenarios` 全類檢查。本切片為唯讀：重複代碼／併發版本 409、部分成功／全部略過、寫入 422／故障回滾及檔案均不適用於此兩個 GET，交由 005d3～d4／005f 驗。沒有畫面，E2E 的訊息與持久資料雙斷言留 005f。

| ID | 類別 | 情境與斷言 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-RFQ-039 | 正常／邊界／狀態 | 真實 SQL 讀撤回報價及全部修訂、逐行六位原幣單價與有效期；同一詢價多供應商按穩定排序跨頁，供應商、狀態、有效日及報價日排序返回保存摘要；無報價為空列表。 | I | REQ_F4114.5.1 | PASS（QuotationQueryTests、QuotationReadEndpoints） |
| PUR-RFQ-040 | 權限／關聯 | `PUR.QUOTATION.VIEW` 按本人／部門／分店／公司歸屬先驗詢價；他公司、範圍外、軟刪草稿及錯配報價 ID 為 404；撤回歷史仍可見。HTTP 401／403／404。 | I | REQ_F4114.5.1 | PASS（QuotationQueryTests、QuotationReadEndpoints） |
| PUR-RFQ-041 | 錯誤／邊界 | 無效頁大小、排序、供應商 ID、狀態回 400；GET 無寫入，400／401／403／404 皆不改持久資料。500／逾時畫面提示由 005f 驗。 | I,E | REQ_F4114.5.1 | PASS（SQL／HTTP 400）；E 待 005f |

## WU-PUR-005d1 供應商報價資料表

按 `myerp-test-scenarios` 分類：本切片只交付資料模型與 SQL 約束；權限、狀態轉換、停用／軟刪 MD 引用、業務輸入、整單回滾及 HTTP 錯誤由 005d2～d4 驗，畫面／資料雙斷言和檔案由 005f 驗。模型沒有批次略過；部分成功不適用，寫入 API 必須整單成功或整單回滾。

| ID | 類別 | 情境與斷言 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-RFQ-036 | 正常／邊界 | 閏日及跨月有效期、零價與六位小數逐行價格、兩個不可變版本保存後可重讀。 | I | REQ_F4114.5.1～3 | PASS（QuotationSchemaTests） |
| PUR-RFQ-037 | 重複／衝突／關聯 | 同公司詢價／供應商重複表頭、重複修訂號、跨公司詢價邀請及負單價由 SQL 拒絕；UPDATE／DELETE 歷史版拒絕。 | I | REQ_F4114.5.2～3 | PASS（QuotationSchemaTests） |
| PUR-RFQ-038 | 權限／狀態／部分結果／錯誤／檔案 | 本切片沒有端點、畫面或批次及檔案操作。401／403／404、關閉與撤回、停用 MD、409／422／500、整單回滾及 UI 訊息由 005d2～d4／005f 驗。 | I,E | REQ_F4114.5.1～4 | TODO（後續切片；本切片不適用） |

## WU-PUR-005c3 詢價發送與關閉

沿用 PUR-RFQ-001、007、011～013、015 的契約。發送／關閉是整單狀態轉換，不含批次逐行略過或檔案輸入；500／逾時的前端訊息留 005f 畫面驗收。

| ID | 類別 | 情境與斷言 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-RFQ-031 | 正常／邊界 | 閏日草稿發送後表頭及全部邀請同一 sentAt，保存供應商代碼／名稱快照；關閉保留快照，後續主檔改名不改歷史，INV 與請購已轉量均為零。 | I | REQ_F4114.4.4, REQ_F4114.4.5 | PASS（RfqDraftMutationTests） |
| PUR-RFQ-032 | 關聯／狀態 | 發送前重驗真實 MD 物料、供應商與已核准同公司請購來源；MD 停用／軟刪、來源駁回或餘量不足整單 422／404，恢復後同版本可重試。草稿不可關閉，已發不可重發，已關閉不可重關或修訂。請購核准後軟刪由 SQL CHECK 禁止，不適用。 | I | REQ_F4114.4.3～6 | PASS（RfqDraftMutationTests、RequisitionAuthorizedHttpTests） |
| PUR-RFQ-033 | 權限／衝突 | SEND／CLOSE 各自 401／403；本人範圍外及他公司 404；舊版 409，雙連線同版競態只一次成功，拒絕分支資料不變。 | I | REQ_F4114.4.4, REQ_F4114.4.5 | PASS（RfqDraftMutationTests、RequisitionAuthorizedHttpTests） |
| PUR-RFQ-034 | 錯誤／部分結果 | 第二供應商查詢故障回滾整單與全部邀請，無部分發送；恢復後同版本重試成功。 | I | REQ_F4114.4.4 | PASS（RfqDraftMutationTests） |
| PUR-RFQ-035 | 檔案／介面 | 本切片沒有檔案、前端畫面或批次操作；空檔／編碼、零變更提示、500／逾時 UI 在 005f 驗。 | E | REQ_F4114.4.4, REQ_F4114.4.5 | 不適用於本切片 |

## WU-PUR-005c2 詢價草稿寫入

按 `myerp-test-scenarios` 檢查各類情境：正常建立／整組修訂／軟刪、閏日與六位數量、重複供應商與版本 409、整單回滾、CREATE／EDIT／DELETE 權限與資料範圍、草稿狀態、MD 有效引用與同公司已核准請購來源、400／404／422、SYS 發號故障。此 API 沒有逐行略過或部分成功，任一行無效整單失敗；沒有檔案或前端畫面，檔案與 E2E 留 005f。

| ID | 類別 | 情境 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-RFQ-026 | 正常／邊界／衝突 | 真實 SQL 建立閏日六位數量草稿、整組替換行與供應商、舊版 409、軟刪保留明細及 INV 零異動。 | I | REQ_F4114.4.2／3／6 | PASS（RfqDraftMutationTests） |
| PUR-RFQ-027 | 關聯／錯誤／部分結果 | 重複供應商、七位小數與不存在來源整單拒絕且零詢價；來源未核准、物料不符或超餘量拒絕，合法量不消耗請購。 | I | REQ_F4114.4.2／3 | PASS（RfqDraftMutationTests） |
| PUR-RFQ-028 | 權限 | POST 未登入 401、只有 VIEW 的 POST／PUT／DELETE 各 403。 | I | REQ_F4114.4.2／3／6 | PASS（RequisitionAuthorizedHttpTests） |
| PUR-RFQ-029 | 正常／競態／回滾 | 真實 MD 物料及供應商、SYS 同交易取號、受權 HTTP 201／200／204、範圍外 404、雙連線取號及發號失敗回滾／重試。 | I | REQ_F4114.4.2／3／6 | PASS（RfqDraftMutationTests、RequisitionAuthorizedHttpTests） |
| PUR-RFQ-030 | 狀態／關聯 | 已發送拒絕修訂／軟刪；停用或軟刪 MD 物料／供應商拒絕，拒絕後恢復可重試。關閉由 005c3 驗。 | I | REQ_F4114.4.3／6 | PASS（RfqDraftMutationTests、RequisitionAuthorizedHttpTests） |

## WU-PUR-005c1 詢價受權唯讀 API

沿用 PUR-RFQ-001～018 完整情境基線。本切片沒有寫入、批次操作或檔案：重複代碼、409、部分成功、422、交易回滾、前端 500／逾時及檔案情境在 005c2～f 驗，不能當作本切片已通過。唯讀的正常、日期／篩選／分頁邊界、權限、軟刪與錯誤情境如下；E2E 畫面／資料雙斷言由 005f 完成。

| Test Case ID | Category | Description | Level | Related REQ_F | Status |
|---|---|---|---|---|---|
| PUR-RFQ-023 | 正常／邊界 | 真實 SQL 保存的閏日、六位數量、行、邀請、版本由詳情原值讀出；單號／日期篩選與白名單排序穩定分頁。 | I | REQ_F4114.4.1 | PASS（RfqQueryTests） |
| PUR-RFQ-024 | 權限／關聯／狀態 | VIEW 的本人／部門／分店／全公司範圍先於分頁套用；範圍外、他公司及軟刪詢價不顯示，單筆回 404。 | I | REQ_F4114.4.1 | PASS（RfqQueryTests） |
| PUR-RFQ-025 | 錯誤／邊界 | 無效頁大小、排序、反向日期及未知狀態拒絕；HTTP 未登入 401、缺 VIEW 403、有效角色讀行邀請 200、錯誤排序 400、不存在 404。 | I | REQ_F4114.4.1 | PASS（RfqQueryTests、RequisitionAuthorizedHttpTests） |

## WU-PUR-005b 詢價資料表真實 SQL 驗收

本切片只驗持久化模型。`myerp-test-scenarios` 的全類功能清單見下方 PUR-RFQ-001～018；HTTP、權限、狀態轉換、部分結果、檔案與 E2E 在 005c～f 驗，不把尚無端點的項目標為通過。

| Test Case ID | Category | Description | Level | Related REQ_F | Status |
|---|---|---|---|---|---|
| PUR-RFQ-019 | 正常／邊界 | 閏日及跨年需求日、六位小數行與一個邀請持久化後原值重讀，另一公司查不到。 | I | REQ_F4114.4.1, REQ_F4114.4.2 | PASS（RfqSchemaTests） |
| PUR-RFQ-020 | 重複／衝突 | 同公司邀請供應商重複、軟刪草稿單號重用由唯一索引拒絕；不同公司子行不能掛原公司表頭。 | I | REQ_F4114.4.2, REQ_F4114.4.6 | PASS（RfqSchemaTests） |
| PUR-RFQ-021 | 邊界／關聯 | 負數量、回覆期限早於業務日及不完整發送快照由 SQL CHECK 拒絕；請購來源跨公司由複合 FK 拒絕。 | I | REQ_F4114.4.2, REQ_F4114.4.4 | PASS（RfqSchemaTests；引用業務狀態待 005c） |
| PUR-RFQ-022 | 權限／狀態／部分結果／錯誤／檔案 | 此資料模型無 API、畫面或批次操作；無權、草稿轉換、部分成功、4xx／5xx、逾時及檔案情境由 005c～f 的 PUR-RFQ-001～018 驗收。 | I,E | REQ_F4114.4.1, REQ_F4114.4.2 | TODO（005c～f；本切片不適用） |

## WU-PUR-005a 詢價／供應商報價／比價與轉單情境基線

### WU-PUR-005f3c1 唯讀比價畫面（REQ_F4114.6.1、REQ_IO_A4.15.1）

| ID | 類別 | 情境與畫面／資料斷言 | 層級 | 結果 |
|---|---|---|---|---|
| PUR-COMPARE-UI-001 | 正常／關聯 | 只發 GET，逐行顯示後端已排序的原幣單價、匯率來源期間、進項稅率、本位幣三項成本；缺匯率報價保留且明示原因，空報價行明示無報價。畫面成本與 GET 原值一致，無資料寫入。 | U/E | PASS |
| PUR-COMPARE-UI-002 | 邊界／部分結果 | 零詢價行顯示空狀態；有行但零報價逐行顯示。批次部分成功、全部略過、最大筆數、小數輸入、日期輸入不適用：此頁只讀單一 GET，輸入均由後端保存。 | E | PASS |
| PUR-COMPARE-UI-003 | 重複／衝突／狀態 | 撤回、過期、未生效、缺匯率／稅率及溢位原因依後端代碼呈現，不自行推導可比性；草稿 422 顯示原因並清舊成本。409 不適用：此頁沒有版本寫入。 | E | PASS（422、缺匯率）；其餘由 005e2 SQL 測試覆蓋 |
| PUR-COMPARE-UI-004 | 權限／跨公司 | 路由只允許 COMPARE；缺權不發 GET，換公司先清舊資料並以新公司重新 GET；資料範圍 404 由後端 005e2 驗收。 | E/I | PASS |
| PUR-COMPARE-UI-005 | 錯誤 | 500 顯示 traceId，斷線／逾時可重試；400、409、422、500 及無效引用中，400／409 不適用於本唯讀 GET，422 及 500 已測。 | E | PASS（422、500、斷線）；逾時沿用共用網絡錯誤處理 |
| PUR-COMPARE-UI-006 | 檔案 | 不適用：無匯入或下載。 | - | N/A |

按 `myerp-test-scenarios` 的正常、邊界、重複與衝突、部分結果、權限、狀態、關聯、錯誤、檔案逐類列出。這是測試先行清單；005a 尚無資料表／端點，以下 `TODO` 不代表通過。005b 起先寫失敗測試再實作，005f 的每個 E2E 須同時驗畫面訊息／樣式與 API 重查或列表實際資料。金額採逐行 `Money.Round`，不可用浮點。

| Test Case ID | Category | Description | Level | Related REQ_F | Status |
|---|---|---|---|---|---|
| PUR-RFQ-001 | 正常 | 建立兩行、兩受邀供應商詢價，SYS 單號唯一；發送後保存快照，INV 異動與請購已轉量為零。 | I,E | REQ_F4114.4.2, REQ_F4114.4.4 | TODO（005b/c/f） |
| PUR-RFQ-002 | 正常 | 兩個受邀供應商報價各有逐行原幣價格、有效期與稅率；修訂後新舊版本均可查。 | I,E | REQ_F4114.5.1, REQ_F4114.5.2, REQ_F4114.5.3 | TODO（005d/f） |
| PUR-RFQ-003 | 正常 | 不同幣別按同一詢價行及有效 MD 匯率／稅率比較，顯示原幣、匯率日期與本位幣行成本，逐行四捨五入。 | U,I,E | REQ_F4114.6.1 | TODO（005e/f） |
| PUR-RFQ-004 | 正常 | 逐行決標並記理由；同一供應商的已選行只產生一張採購單草稿，畫面單號／行數與重新讀取相同，庫存零異動。 | I,E | REQ_F4114.6.2, REQ_F4114.6.3 | TODO（005e/f） |
| PUR-RFQ-005 | 邊界 | 0／1001 行、0／51 供應商、重複供應商、零／負／超六位數量、超長備註及截止日早於業務日逐欄 400；閏日／跨年可保存。 | U,I,E | REQ_F4114.4.2, REQ_F4114.4.3 | TODO（005c/f） |
| PUR-RFQ-006 | 邊界 | 報價空行、負單價、超六位小數、缺幣別／稅率、有效期早於報價日拒絕；零單價合法且顯示免費。 | U,I,E | REQ_F4114.5.2 | TODO（005d/f） |
| PUR-RFQ-007 | 重複與衝突 | 軟刪草稿單號不可重用；同公司雙連線建立號碼唯一；過期版本修改／發送／決標 409，資料與歷史不變。 | I | REQ_F4114.4.2, REQ_F4114.4.3, REQ_F4114.4.4, REQ_F4114.4.5, REQ_F4114.4.6, REQ_F4114.6.2 | TODO（005b/c/e） |
| PUR-RFQ-008 | 重複與衝突 | 同一詢價／供應商只一個有效報價；雙連線同時修訂／決標只一次成功；重複轉同一決標行不得有第二張單或第二次消耗。 | I | REQ_F4114.5.2, REQ_F4114.5.3, REQ_F4114.6.2, REQ_F4114.6.3 | TODO（005d/e） |
| PUR-RFQ-009 | 部分結果 | 詢價或報價任一無效行令整單 0 行新增；轉單其中一行已轉時整批回滾，0 張新單／0 行連結，畫面列明衝突行。 | I,E | REQ_F4114.4.2, REQ_F4114.5.2, REQ_F4114.6.3 | TODO（005c/e/f） |
| PUR-RFQ-010 | 部分結果 | 全部決標行已轉、選擇空集合或沒有可比報價時顯示警告及原因，不顯示成功；此功能沒有逐行略過，部分成功不適用。 | I,E | REQ_F4114.6.1, REQ_F4114.6.2, REQ_F4114.6.3 | TODO（005e/f） |
| PUR-RFQ-011 | 權限 | 未登入 401、缺各動作權 403；本人／部門／分店資料範圍外及他公司詢價／報價／轉單 404，列表不洩漏，按鈕禁用說明原因。 | I,E | REQ_F4114.4.1, REQ_F4114.4.2, REQ_F4114.4.3, REQ_F4114.4.4, REQ_F4114.4.5, REQ_F4114.4.6, REQ_F4114.5.1, REQ_F4114.5.2, REQ_F4114.5.3, REQ_F4114.5.4, REQ_F4114.6.1, REQ_F4114.6.2, REQ_F4114.6.3 | TODO（005c～f） |
| PUR-RFQ-012 | 狀態 | 草稿只可修訂／軟刪，已發送不可修訂／刪；關閉後不可新報價；撤回／過期／已決標報價不可再次決標或修訂；已轉行不可再轉。 | I,E | REQ_F4114.4.3, REQ_F4114.4.4, REQ_F4114.4.5, REQ_F4114.4.6, REQ_F4114.5.2, REQ_F4114.5.3, REQ_F4114.5.4, REQ_F4114.6.2, REQ_F4114.6.3 | TODO（005c～f） |
| PUR-RFQ-013 | 關聯 | 發送前物料、供應商、請購來源停用／軟刪／他公司時拒絕並指出行；發送後停用仍可檢視歷史。詢價來源未核准或超目前請購餘量拒絕。 | I,E | REQ_F4114.4.1, REQ_F4114.4.2, REQ_F4114.4.3, REQ_F4114.4.4 | TODO（005c/f） |
| PUR-RFQ-014 | 關聯 | 缺有效匯率或稅率、報價已過期時顯示不可比原因且不可決標；來源報價被引用後不得刪，價格快照不隨 MD 後續更改。 | I,E | REQ_F4114.5.1, REQ_F4114.6.1, REQ_F4114.6.2, REQ_F4114.6.3 | TODO（005e/f） |
| PUR-RFQ-015 | 錯誤 | HTTP 400 欄位錯、409 版本、422 狀態／停用引用、500 traceId、逾時／斷線，前端三語具體提示且原輸入保留，不誤報成功。 | I,E | REQ_F4114.4.2, REQ_F4114.4.3, REQ_F4114.4.4, REQ_F4114.4.5, REQ_F4114.4.6, REQ_F4114.5.2, REQ_F4114.5.3, REQ_F4114.5.4, REQ_F4114.6.2, REQ_F4114.6.3 | TODO（005c～f） |
| PUR-RFQ-016 | 錯誤／交易 | SYS 發號、報價保存、決標或採購單草稿寫入途中注入故障，整交易回滾；恢復後同版本可重試且不跳號。 | I | REQ_F4114.4.2, REQ_F4114.5.2, REQ_F4114.6.2, REQ_F4114.6.3 | TODO（005c～e） |
| PUR-RFQ-017 | 檔案 | 005a～e 沒有檔案上傳；空檔、格式／編碼／欄位錯不適用。005f 若提供 CSV，另補 0 行、10000 行上限、BOM、公式防護、取消及整檔失敗 E2E。 | E | REQ_F4114.4.1, REQ_F4114.6.1 | TODO（005f） |
| PUR-RFQ-018 | 介面 | 三語亮暗下正常、空列表、無搜尋結果、無權、驗證失敗、伺服器錯誤、成功／零變更；逐張截圖審查，提示筆數與持久資料一致。 | E | REQ_F4114.4.1, REQ_F4114.4.2, REQ_F4114.4.3, REQ_F4114.4.4, REQ_F4114.4.5, REQ_F4114.4.6, REQ_F4114.5.1, REQ_F4114.5.2, REQ_F4114.5.3, REQ_F4114.5.4, REQ_F4114.6.1, REQ_F4114.6.2, REQ_F4114.6.3 | TODO（005f） |
| PUR-RFQ-051 | 關聯／日期 | MD 公開匯率快照按公司與業務日期返回數值、有效起迄及來源 ID；本位幣為 1 且無來源 ID。缺期間、軟刪及他公司資料不可作比較。 | I | REQ_F4114.6.1, REQ_F4114.6.2 | PASS（005e1；比價端點待 005e2） |
| PUR-RFQ-052 | 關聯／狀態 | MD 公開稅率快照按 ID、公司、進項方向與有效日返回百分比和期間；不存在、跨公司、到期或方向不符拒絕。 | I | REQ_F4114.6.1, REQ_F4114.6.2 | PASS（005e1；比價端點待 005e2） |
| PUR-RFQ-053 | 正常／關聯／部分結果 | 真實 SQL 比價按本位幣含稅成本穩定排序，已決標報價仍供歷史對照；原幣及快照證據保留。同幣別快照一次查詢；撤回、過期、未生效、缺匯率或進項稅率的報價在不可比尾部附原因及空比較金額。無報價則行的報價陣列為空，不會寫入或誤報成功。 | I | REQ_F4114.6.1 | PASS（005e2 SQL 專項） |
| PUR-RFQ-054 | 權限／狀態／錯誤 | 比價 HTTP 匿名 401、缺 COMPARE 403、本人範圍外 404；草稿或無本位幣 422，已授權 GET 返回目前不可比原因。日期由 `IClock` 注入，MD 故障不可被誤判為缺率。 | I | REQ_F4114.6.1 | PASS（005e2 SQL／受權 HTTP 專項） |
| PUR-RFQ-070 | 正常／空與部分結果／錯誤 | 唯讀列表顯示保存的單號、狀態、期限、行數、受邀數；詳情顯示保存行與供應商快照。查詢無結果、載入 500 時清掉舊詳情，500 顯示 traceId。唯讀操作沒有資料寫入或部分成功。 | E | REQ_F4114.4.1 | PASS（005f1） |
| PUR-RFQ-071 | 邊界／驗證 | 起始日晚於截止日、請購來源 ID 非正整數時顯示欄位原因且不送 API；合法邊界及空篩選可查。 | E | REQ_F4114.4.1 | PASS（005f1） |
| PUR-RFQ-072 | 權限／公司／狀態 | 無 VIEW 權限路由拒絕；換公司清除前公司列表與詳情；草稿／已發送／已關閉在唯讀頁只顯示保存狀態，不冒充寫入動作。 | E | REQ_F4114.4.1 | PASS（005f1） |
| PUR-RFQ-073 | UI／錯誤 | 4.11 正常、詳情、空、搜尋無結果、驗證錯、500、逾時、無權、三語與亮暗逐張審查；唯讀頁的 409／422、成功／略過／失敗寫入、檔案情境不適用，因無變更或上傳。 | E | REQ_F4114.4.1 | PASS（005f1，20 張截圖逐張審查） |
| PUR-RFQ-074 | 權限／關聯／錯誤 | 點開後才被刪除或移出資料範圍的詢價詳情回 404，三語顯示具體原因，不留下舊詳情。 | E | REQ_F4114.4.1 | PASS（005f1；404 重查列表） |
| PUR-RFQ-075 | 導航／篩選 | 從可分享 URL 還原單號／狀態等篩選及頁碼，修改查詢後 URL 同步；返回時不誤顯示前公司的詳情。 | E | REQ_F4114.4.1 | PASS（005f1；第二頁與重設） |

005e1 只新增跨模組唯讀契約與真實 SQL 測試；正常、日期邊界、公司與狀態由 PUR-RFQ-051／052 覆蓋，專項 2/2 通過，Release 整合全量於 2026-10-21T16:41:55+08:00 單獨單執行緒 617/617 通過。重複／競態及原子轉單由 005e3／e4 覆蓋；部分結果與錯誤提示由 005e2～f 覆蓋。此契約沒有檔案、批次寫入或 UI，對應情境不適用。

005e2 是唯讀 API，沒有重複代碼、版本 409、併發寫入、批次部分成功或檔案情境；400 輸入欄位、500／逾時前端訊息及三語畫面由 005f 驗。SQL 專項用保存的詢價／報價行驗 Money.Round、穩定排序與不可比原因；MD 真實 SQL 快照用 051／052 獨立驗公司、日期、軟刪和方向。決標重驗及不可比時拒絕由 005e3 驗。

## WU-PUR-004c 建立／修訂表單（介面 4.9／4.10）

| ID | 類別 | 情境與資料斷言 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-FORM-001 | 正常 | CREATE 受權選 MD 啟用物料，閏日與六位數量字串保存後列表出現新單；駁回單 EDIT 載入保存行及版本。 | U,I,E | REQ_F4114.3.2, REQ_F4114.3.3 | PASS（表單、真實受權 HTTP、Playwright） |
| PUR-FORM-002 | 邊界 | 空日期／物料／量、無效日期、需求日早於單據日、七位小數、零量、超 500 字與 0／1001 行逐欄拒絕；最大 1000 行與合法六位量由單元測試驗。 | U,E | REQ_F4114.3.2, REQ_F4114.3.3 | PASS（服務與表單單元、Playwright 驗證畫面） |
| PUR-FORM-003 | 重複與衝突 | 同物料可多行；軟刪單號不可重用由後端保障；PUT 舊版本 409 保留輸入、顯示重新載入並警告會丟失修改。 | U,I,E | REQ_F4114.3.2, REQ_F4114.3.3 | PASS（既有後端、Playwright 衝突畫面） |
| PUR-FORM-004 | 部分結果 | 保存為整單原子操作；無逐行略過、部分成功、全部略過與批次統計，失敗時列表不增且沒有成功提示。 | I,E | REQ_F4114.3.2, REQ_F4114.3.3 | PASS（真實 SQL 及 Playwright）；批次不適用 |
| PUR-FORM-005 | 權限 | 無 CREATE／EDIT 的用戶路由 403；選項只供 CREATE／EDIT、只列本公司啟用 MD 物料；修訂詳情與保存名稱依 EDIT 資料範圍，只有 EDIT 權限可讀可修訂單據。 | I,E | REQ_F4114.3.1～3 | PASS（受權 HTTP、Playwright）；跨公司／範圍沿用 PUR-SCOPE-001～003 |
| PUR-FORM-006 | 狀態／關聯 | 草稿或駁回可修訂；待審、核准、關閉不可修訂並顯示原因；已保存停用物料可見名稱與原因，保存仍由 MD 拒絕。 | I,E | REQ_F4114.3.1～3 | PASS（後端既有狀態測試、Playwright） |
| PUR-FORM-007 | 錯誤 | 400 欄位訊息、409、422 停用、500 traceId、斷線及逾時均不顯示成功且保留輸入；無效本地欄位聚焦首項。 | U,E | REQ_F4114.3.2, REQ_F4114.3.3 | PASS（Playwright 各狀態亮暗、表單驗證） |
| PUR-FORM-008 | 檔案 | 此表單不收檔案；空檔、格式、大小、列數、編碼與缺／多欄均不適用。列表 CSV 由 004d 完成。 | E | REQ_F4114.3.2 | N/A 匯入；匯出見 PUR-CSV-001～008 |
| PUR-FORM-009 | 導航 | 未保存離開有確認，取消後原輸入仍在；保存後才顯示成功並回列表。 | E | REQ_F4114.3.2, REQ_F4114.3.3 | PASS（Playwright） |

## WU-PUR-004b 請購列表與已保存詳情

| ID | 類別 | 情境與斷言 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-UI-008 | 正常 | 分頁、單號／日期／狀態／請購人篩選；詳情顯示保存行、數量、已轉量及全部審批輪次。 | U,E | REQ_F4114.3.1, REQ_F4114.3.5 | PASS（頁面單元、Playwright；多輪歷史） |
| PUR-UI-009 | 邊界 | 空列表與搜尋無結果不同；日期倒置及請購人非正整數不送 API；最大查詢分頁由伺服器約束。 | U,E | REQ_F4114.3.1 | PASS（頁面單元、Playwright；API 分頁上限既有 PUR-READ-003） |
| PUR-UI-010 | 重複與衝突 | 舊版本送審／刪除回 409，刷新後仍保留錯誤原因；軟刪單號不再出現在列表。 | E | REQ_F4114.3.4, REQ_F4114.3.6 | 送審 409、刪除後列表 PASS；刪除 409 由後端 PUR-MUT 測試覆蓋 |
| PUR-UI-011 | 部分結果 | 單筆送審／刪除為原子操作，沒有部分成功或略過；失敗不顯示成功。 | E | REQ_F4114.3.4, REQ_F4114.3.6 | PASS（Playwright 錯誤後保持保存草稿、刪除確認與 204 後列表；批次 N/A） |
| PUR-UI-012 | 權限 | VIEW 守衛、CREATE／EDIT／SUBMIT／DELETE 分別控制按鈕；跨公司及資料範圍由 API 遮蔽。 | E | REQ_F4114.3.1, REQ_F4114.3.2, REQ_F4114.3.3, REQ_F4114.3.4, REQ_F4114.3.5, REQ_F4114.3.6 | VIEW／SUBMIT／DELETE／CREATE／EDIT PASS（Playwright）；跨公司／範圍由 PUR-SCOPE-001～003 真實 SQL／HTTP 驗證 |
| PUR-UI-013 | 狀態／關聯 | 草稿可送審及刪除、駁回只可修訂；待審／已核准／關閉不可編刪；已保存的停用物料仍顯示 ID 與行資料。 | E | REQ_F4114.3.1, REQ_F4114.3.2, REQ_F4114.3.3, REQ_F4114.3.4, REQ_F4114.3.5, REQ_F4114.3.6 | 列表與修訂狀態 PASS；停用物料名稱與原因可見，保存拒絕 PASS |
| PUR-UI-014 | 錯誤 | 400／409／422／500 traceId、斷線／逾時均有三語訊息與逐欄原因，畫面不誤報成功。 | U,E | REQ_F4114.3.1, REQ_F4114.3.2, REQ_F4114.3.3, REQ_F4114.3.4 | 4.8／4.9／4.10 錯誤與表單逐欄原因 PASS（Playwright）；英／簡正常與驗證畫面已截圖 |
| PUR-UI-015 | 檔案／列印 | 此頁不接收檔案；列印重新取得已保存詳情；CSV 依已套用篩選跨頁匯出完整單據明細，公式字元防護、10000 行上限、取消或查詢變動不下載、錯誤不產生部分檔；畫面列數與 CSV 資料列一致。 | U,E | REQ_F4114.3.1 | PASS：列印與 CSV 專項；匯入 N/A |
| PUR-CSV-001 | 正常 | 201 張以上跨頁請購，含不同狀態及多明細，匯出數與保存資料一致。 | E | REQ_F4114.3.1 | PASS：201 張跨頁、保存行與畫面 201 行一致；五種狀態另驗 |
| PUR-CSV-002 | 邊界 | 0 筆不下載、10000 行可下載、超過 10000 行拒絕並顯示原因。 | U,E | REQ_F4114.3.1 | PASS：零明細無檔、10000／10001 行單元、超上限畫面 |
| PUR-CSV-003 | 重複與衝突 | 單據刪除或版本更新導致詳情 404／409 時整檔失敗；匯出期間重新查詢，舊匯出取消。 | E | REQ_F4114.3.1 | PASS：跨頁重複 ID、409 與重新查詢取消 E2E；公司變更使用相同取消路徑 |
| PUR-CSV-004 | 部分結果 | 任何分頁或詳情失敗都不得下載部分 CSV；沒有「部分成功」提示。 | E | REQ_F4114.3.1 | PASS：500／400／409／422／斷線／逾時均無檔 |
| PUR-CSV-005 | 權限 | 無 EXPORT 權限不顯示按鈕；後端 VIEW 資料範圍仍逐頁驗證，403／404 顯示原因。 | E | REQ_F4114.3.1 | PASS：VIEW-only 無按鈕；既有 PUR-SCOPE 真實 SQL／HTTP 驗 VIEW 隔離 |
| PUR-CSV-006 | 狀態 | 草稿、待審、駁回、核准、關閉均可讀取保存資料匯出；匯出不改單據狀態。 | E | REQ_F4114.3.1 | PASS：五種狀態各一行，零寫入 |
| PUR-CSV-007 | 錯誤 | 400／409／422／500 traceId、斷線／逾時顯示錯誤，可重試，不下載。 | E | REQ_F4114.3.1 | PASS：13 個 CSV E2E 覆蓋，錯誤後按鈕可重試 |
| PUR-CSV-008 | 檔案 | UTF-8 BOM、引號／換行正確跳脫，公式前導 = + - @（含空白／Tab）加單引號；匯入檔案的空檔、格式、編碼、缺／多欄不適用。 | U,E | REQ_F4114.3.1 | PASS：CSV 單元與瀏覽器下載；匯入檔 N/A |

## WU-PUR-004a 前端請購 API 契約與輸入守衛

| ID | 類別 | 情境與資料斷言 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-UI-001 | 正常 | 列表傳伺服器分頁／篩選；GET 詳情保留審批歷史；建立／修改依行序重編號、修剪備註；送審／刪除用對應版本，不提交請購人及組織歸屬。 | U | REQ_F4114.3.1, REQ_F4114.3.2, REQ_F4114.3.3, REQ_F4114.3.4, REQ_F4114.3.6 | PASS（requisition.service.spec.ts） |
| PUR-UI-002 | 邊界 | 閏日、1000 行、六位正數有效；空行、1001 行、無效日期、過早需求日、零量、七位小數、達 10^13 與 501 字備註無效。 | U | REQ_F4114.3.2, REQ_F4114.3.3 | PASS（requisition.service.spec.ts） |
| PUR-UI-003 | 重複與衝突 | 同物料多行可建立；代碼唯一及軟刪單號由 SYS／PUR 後端驗；過期版本由服務帶往後端回 409，不在前端假定成功。 | U,I | REQ_F4114.3.2, REQ_F4114.3.3 | 前端同物料 PASS；後端既有 RequisitionMutationTests PASS |
| PUR-UI-004 | 部分結果／狀態 | 單據寫入原子處理，沒有批次部分成功、全部略過或全部失敗統計；草稿以外狀態由後端拒絕，畫面禁用原因留 004b～c。 | I,E | REQ_F4114.3.3, REQ_F4114.3.4 | 原子／狀態後端既有 PASS；畫面待 004b～c |
| PUR-UI-005 | 權限／關聯 | 無權、跨公司、範圍外與停用／已刪 MD 引用由受權後端處理；前端只送必要欄位，畫面權限留 004b～c。 | I,E | REQ_F4114.3.1, REQ_F4114.3.2, REQ_F4114.3.3, REQ_F4114.3.4 | 後端既有 RequisitionAuthorizedHttpTests PASS；畫面待 004b～c |
| PUR-UI-006 | 錯誤 | 400／409／422／500 traceId、逾時／斷線須在 004b～c 由共用錯誤機制及逐欄訊息顯示；本切片僅提供原始 HTTP 失敗，不吞錯。 | E | REQ_F4114.3.1, REQ_F4114.3.2, REQ_F4114.3.3, REQ_F4114.3.4 | 待 004b～c |
| PUR-UI-007 | 檔案 | 請購輸入沒有匯入檔，空檔、格式、大小、編碼、缺欄不適用；完整 CSV 匯出及公式防護見 004d。 | E | REQ_F4114.3.1 | 匯入 N/A；匯出 PASS |
| PUR-UI-008 | 十進位契約 | 真實 HTTP 接受六位數量字串，建立及整組修改後均保存相同 decimal 值。 | I | REQ_F4114.3.2, REQ_F4114.3.3 | PASS（RequisitionAuthorizedHttpTests） |


## WU-PUR-003c 送審邊界與併發

| ID | 類別 | 情境與資料斷言 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-APR-009 | 邊界／衝突 | 零、負及過期版本不能送審；請購維持草稿，沒有審批實例。 | I | REQ_F4114.3.4 | PASS（RequisitionSubmissionTests；過期版本既有測試） |
| PUR-APR-010 | 狀態 | 駁回未修訂、待審、已核准、已關閉不可送審；軟刪單回 404，沒有新實例。 | I | REQ_F4114.3.4 | PASS（RequisitionSubmissionTests） |
| PUR-APR-011 | 關聯／錯誤 | 已保存 MD 物料停用回 422、軟刪回 404；復原後相同版本可送審；失敗時 PUR／SYS 均不改、INV 零異動。 | I | REQ_F4114.3.4 | PASS（RequisitionAuthorizedHttpTests） |
| PUR-APR-012 | 權限 | 匿名 401、缺 SUBMIT 403；本人、部門、分店範圍外與他公司 404，不建立實例。 | I | REQ_F4114.3.4 | PASS（RequisitionSubmissionTests、RequisitionAuthorizedHttpTests；他公司沿用既有案例） |
| PUR-APR-013 | 併發／稽核 | 兩連線同版本送審或決定，最多一次成功；敗方 409／422，單據、實例、步驟與審批稽核一致。故障後可重試。 | I | REQ_F4114.3.4, REQ_F4114.3.5 | PASS（RequisitionAuthorizedHttpTests、RequisitionApprovalTests；狀態衝突回滾既有案例） |
| PUR-APR-014 | 正常／部分結果／檔案 | 單筆原子送審與決定；沒有批次、逐行略過、檔案或部分成功路徑。 | I | REQ_F4114.3.4 | 單筆既有測試；其餘不適用 |
| PUR-APR-015 | 前端錯誤 | 400、409、422、500 traceId、逾時／斷線與逐欄原因由 WU-PUR-004 畫面驗收。 | E | REQ_F4114.3.4 | WU-PUR-004 |

## WU-PUR-003b 人工決定與審批歷史

| ID | 類別 | 情境與斷言 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-APR-001 | 正常 | 真實 SYS 流程最終核准使同公司請購成 Approved，無 INV 異動。 | I | REQ_F4114.3.4 | PASS（RequisitionApprovalTests） |
| PUR-APR-002 | 正常／狀態 | 駁回保存意見，修訂後重送並核准；詳情歷史按新到舊，兩輪逐步意見皆保留。 | I | REQ_F4114.3.1, REQ_F4114.3.5 | PASS（RequisitionApprovalTests、RequisitionAuthorizedHttpTests） |
| PUR-APR-003 | 衝突／錯誤 | SYS 決定時請購已離開待審回 409，SYS 實例維持 Pending；狀態修正後可重試。 | I | REQ_F4114.3.4 | PASS（RequisitionApprovalTests） |
| PUR-APR-004 | 權限 | 請購詳情先經 PUR VIEW 授權及資料範圍；他公司、無權及範圍外不得取得歷史。 | I | REQ_F4114.3.1 | VIEW 範圍／公司既有 RequisitionAuthorizedHttpTests PASS；歷史同路徑讀取 PASS |
| PUR-APR-005 | 邊界 | 無流程立即核准及零步驟歷史沿用 PUR-SUB-001；多級中間核准不改 PUR 狀態。 | I | REQ_F4114.3.4 | 零步驟 PUR-SUB-001 PASS；多級由 SYS ApprovalWorkflowTests PASS |
| PUR-APR-006 | 部分結果／關聯 | 單據審批是原子單筆操作，沒有部分成功、全部略過、批次或檔案；不產生採購單或庫存異動。 | I | REQ_F4114.3.4, REQ_F4114.3.5 | N/A 批次／檔案；零 INV 異動 PASS |
| PUR-APR-007 | HTTP 錯誤 | SYS 決定 400／409／422、未登入 401 與無 PROCESS 權 403 由 SYS ApprovalApiTests 覆蓋；PUR 詳情可由受權 HTTP 讀歷史，受權 SYS HTTP 決定同時保存請購及實例。前端 500 traceId／逾時留 004。 | I,E | REQ_F4114.3.1, REQ_F4114.3.4 | SYS 共用 HTTP 已驗；PUR 決定／詳情 PASS（RequisitionAuthorizedHttpTests）；前端留 004 |
| PUR-APR-008 | 併發／資料 | 同一實例重複決定和雙連線競態由 SYS 審批版本／唯一約束保護；PUR 與 SYS 狀態一致。 | I | REQ_F4114.3.4 | SYS ApprovalWorkflowTests 已驗；PUR 專項留 003c |

## WU-PUR-003a 請購送審

| ID | 類別 | 情境與斷言 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-SUB-001 | 正常 | 真實 SQL 草稿送審建立 SYS 實例、請購成待審，INV 異動為零；無適用步驟立即核准。 | I | REQ_F4114.3.4 | PUR 狀態與零 INV 異動 PASS（RequisitionSubmissionTests）；真實 SYS 實例待 003b |
| PUR-SUB-002 | 邊界 | 最小一行、閏日與六位數量保存後可送審；零或負版本拒絕。送審不接受新明細或檔案。 | I | REQ_F4114.3.4 | 一行／閏日／六位數量 PASS；零／負版本待 003c |
| PUR-SUB-003 | 重複與衝突 | 舊版本 409；重複送審及非草稿狀態 422，實例及單據不重複改動；軟刪單 404。 | I | REQ_F4114.3.4 | 舊版本、重複送審 PASS；其他狀態／軟刪待 003c |
| PUR-SUB-004 | 部分結果 | 送審是單筆原子操作，沒有逐行成功／略過；SYS 建立失敗時請購狀態及版本不變。批次部分成功、全部略過與全部失敗不適用。 | I | REQ_F4114.3.4 | PUR 回滾 PASS（契約替身）；真實 SYS 原子實例待 003b；批次 N/A |
| PUR-SUB-005 | 權限 | 缺 SUBMIT 權 403；本人、部門、分店範圍外及他公司單據 404；未登入 HTTP 401。 | I | REQ_F4114.3.4 | 缺權、本人、跨公司與未登入 PASS；部門／分店待 003c |
| PUR-SUB-006 | 狀態／關聯 | 送審前已停用物料 422、已軟刪物料 404；失敗不留下審批實例。待審／已核准／已關閉／駁回未修訂不可重送。 | I | REQ_F4114.3.4 | 停用替身／待審拒絕 PASS；真實 MD 與其他狀態待 003c |
| PUR-SUB-007 | 錯誤 | HTTP 400 無效請求、409 版本、422 狀態／停用引用；SYS 失敗回滾，前端 500 traceId／逾時提示留 004。 | I,E | REQ_F4114.3.4 | SQL 409／422／失敗回滾 PASS；HTTP 400／409／422 待 003b／003c，前端待 004 |
| PUR-SUB-008 | 檔案 | 端點只收版本 JSON；空檔、格式、大小、列數、編碼、欄位缺失與多餘欄位不適用。 | I | REQ_F4114.3.4 | N/A |

## WU-PUR-002b2c 修改／軟刪

| ID | 類別 | 情境與斷言 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-MUT-001 | 正常／邊界 | 草稿 1 行整組換為閏日後六位數量新行，舊行消失；備註修剪、版本增加，INV 異動仍為零。 | I | REQ_F4114.3.3 | PASS（RequisitionMutationTests） |
| PUR-MUT-002 | 衝突／錯誤／部分結果 | 舊版本 409；新行 MD 停用拒絕，原行、單號與版本不變；整單原子保存，沒有逐行略過。 | I | REQ_F4114.3.3 | PASS（RequisitionMutationTests） |
| PUR-MUT-003 | 權限／狀態 | 本人範圍與他公司請購回 404；待審、已核准、已關閉修改與刪除 422；駁回修訂回草稿；草稿軟刪後列表隱藏，再刪回 404。 | I | REQ_F4114.3.3, REQ_F4114.3.6 | PASS（RequisitionMutationTests） |
| PUR-MUT-004 | 權限／錯誤 | 未登入 PUT／DELETE 401；登入缺動作權 403；真實 HTTP 更新、舊版本 409、軟刪 204 後 GET 404。 | I | REQ_F4114.3.3, REQ_F4114.3.6 | PASS（RequisitionHttpTests、RequisitionAuthorizedHttpTests） |
| PUR-MUT-005 | 邊界／關聯／檔案 | 修改共用建立的 1～1000 行、日期、精度及長度驗證；真實 MD 停用回 422、軟刪回 404 且舊行不變。PUR 並發建立單號唯一，無效引用不佔號，恢復後重試接續取號。請購不接受檔案或批次，檔案編碼與部分成功不適用。 | I | REQ_F4114.3.2, REQ_F4114.3.3, REQ_F4114.3.6 | PASS（RequisitionCreateTests、RequisitionAuthorizedHttpTests） |

## WU-PUR-002b2b 建立草稿

依 `myerp-test-scenarios` 覆核：正常為兩行草稿、同交易 SYS 單號與零庫存異動；邊界為閏日、空行、1000 行上限、六位數量、日期、備註及行號；重複單號由 SYS 併發保護與資料庫唯一鍵守衛，版本衝突在 002b2c；部分結果不適用，整單原子保存，任一行失敗整單回滾；權限含未登入 401、缺 CREATE 403、請求偽造歸屬無效；狀態只有建立 Draft，其他狀態操作待 002b2c／003；關聯為 MD 停用／不存在物料拒絕；錯誤含 400／404／422，前端 500／逾時在 004 驗；本切片沒有檔案。

| ID | 類別 | 情境 | 層級 | 對應需求 | 結果 |
|---|---|---|---|---|---|
| PUR-CREATE-001 | 正常／關聯 | 真實 HTTP 建立草稿，MD 有效物料、SYS 月流水單號、保存行與版本可讀，庫存異動為零。 | I | REQ_F4114.3.2 | PASS（RequisitionAuthorizedHttpTests、RequisitionCreateTests） |
| PUR-CREATE-002 | 邊界 | 閏日及六位數量可保存；空行、超 1000 行、重複／零行號、零／超精度／超上限數量、早於業務日及超長備註逐欄 400，無部分保存。 | I | REQ_F4114.3.2 | PASS（RequisitionCreateTests） |
| PUR-CREATE-003 | 權限 | 未登入 401、缺 CREATE 403；請求偽造請購人／分店／部門時仍以 SYS 授權歸屬保存。 | I | REQ_F4114.3.2 | PASS（RequisitionAuthorizedHttpTests） |
| PUR-CREATE-004 | 關聯／錯誤 | 不存在物料 404、停用物料 422，整單及 SYS 流水號不變。 | I | REQ_F4114.3.2 | PASS（RequisitionAuthorizedHttpTests） |
| PUR-CREATE-005 | 重複／衝突 | 同公司併發建立單號唯一；無規則或單號用盡 422；整交易失敗重試不跳號。 | I | REQ_F4114.3.2 | PUR 已登入 HTTP 併發兩單唯一、停用引用拒絕後重試不跳號通過；無規則／耗盡由 SYS NumberGeneratorTests 覆蓋 |
| PUR-CREATE-006 | 部分結果／狀態／檔案 | 無逐行略過、批次及檔案操作；建立結果僅 Draft，修改／刪除狀態檢查在 002b2c，審批在 003。 | I | REQ_F4114.3.2 | N/A；後續單元追蹤 |

## WU-PUR-002b2a 資料範圍與已登入讀取

按 `myerp-test-scenarios` 覆核：正常為已登入 VIEW 列表及詳情 200；邊界為組織歸屬空值降為本人、空列表及日期／數量沿用 PUR-READ-001／003；重複／併發與部分結果不適用於唯讀；權限為缺 VIEW 403、本人／部門／分店／全公司各自篩選、他公司與範圍外詳情 404；狀態不限制讀取已保存單；關聯為保存的組織 ID 不建跨模組 FK；錯誤 400／404 沿用 PUR-READ-002／003，500／逾時畫面待 004；檔案不適用。

| ID | 類別 | 情境 | 層級 | 對應需求 | 結果 |
|---|---|---|---|---|---|
| PUR-SCOPE-001 | 正常／權限 | 真實 SQL 本人、部門、分店、全公司範圍依序可見 1／2／3／4 筆，範圍外詳情 404。 | I | REQ_F4114.3.1 | PASS（RequisitionQueryTests） |
| PUR-SCOPE-002 | 邊界／權限 | 無 VIEW 回 403；用戶無組織歸屬時分店範圍降為本人；SQL 公司篩選與軟刪沿用 READ-002。 | I | REQ_F4114.3.1 | PASS（RequisitionQueryTests） |
| PUR-SCOPE-003 | 正常／權限 | 真實登入 VIEW 列表與詳情 200、無 VIEW 403，已授權公司查詢他公司單據 404。 | I | REQ_F4114.3.1 | PASS（RequisitionAuthorizedHttpTests） |

## WU-PUR-002b1 唯讀列表與詳情

依 `myerp-test-scenarios` 核對：正常為保存單據的分頁與逐行詳情；邊界為閏日、六位數量、空結果及無效頁碼／排序／篩選；重複與衝突不適用於唯讀操作；部分成功、全略過及全失敗不適用於唯讀操作；權限為未登入 401、VIEW 守衛與跨公司隱藏；狀態為軟刪後不可見，其餘狀態可檢視歷史；關聯為只讀已保存物料 ID，停用物料仍可看歷史；錯誤為 400／404，寫入衝突與 422 留待 002b2／003，畫面 500／逾時留待 004；本切片沒有檔案。

| ID | 類別 | 情境 | 層級 | 對應需求 | 結果 |
|---|---|---|---|---|---|
| PUR-READ-001 | 正常／邊界 | 真實 SQL 兩張單分頁與單號、閏日、請購人篩選；詳情保存六位數量、版本及行順序。 | I | REQ_F4114.3.1 | PASS（RequisitionQueryTests） |
| PUR-READ-002 | 權限／狀態 | 他公司列表為空且詳情 404；軟刪單不在列表與詳情；兩個 HTTP 路由未登入 401。 | I | REQ_F4114.3.1 | PASS（RequisitionQueryTests、RequisitionHttpTests） |
| PUR-READ-003 | 邊界／錯誤 | 非法頁大小、排序、日期範圍、請購人與狀態回 400，原有資料不變。 | I | REQ_F4114.3.1 | PASS（RequisitionQueryTests） |
| PUR-READ-004 | 權限 | 真實已登入 VIEW 200、無 VIEW 403、跨公司 404；資料範圍限本人／部門／分店。 | I | REQ_F4114.3.1 | PASS（PUR-SCOPE-001～003） |

## WU-PUR-002a 資料表切片

依 `myerp-test-scenarios` 逐類核對：正常為兩表保存及讀回；邊界為閏日與六位數量；重複／衝突由公司單號、公司行號唯一索引與版本欄位保護，CRUD 409 在 002b 驗；部分結果不適用，因這個切片沒有批次 API；權限與狀態由 EF 公司篩選及 SQL CHECK 保護，HTTP 授權在 002b 驗；關聯為同公司複合 FK，MD／SYS 只存 ID；錯誤為非法數量受 SQL CHECK 拒絕，400／422／500／逾時由 002b／004 驗；本切片無檔案。

| ID | 類別 | 情境 | 層級 | 對應需求 | 結果 |
|---|---|---|---|---|---|
| PUR-SCHEMA-001 | 正常／邊界／權限 | 真實 SQL 保存閏日及六位數量；他公司 EF 查詢不見單據。 | I | REQ_F4114.3.1, REQ_F4114.3.2 | PASS（PurchasingSchemaTests） |
| PUR-SCHEMA-002 | 邊界／錯誤 | 負數數量在 DB CHECK 拒絕，原有明細不變。 | I | REQ_F4114.3.2 | PASS（PurchasingSchemaTests） |
| PUR-SCHEMA-003 | 重複／關聯／狀態 | 公司單號及行號唯一、同公司 FK、已轉採購量上下限與軟刪狀態由 DB 約束保護；API 競態另驗。 | I | REQ_F4114.3.2, REQ_F4114.3.6 | 待補（002b） |

## WU-PUR-001 模組骨架與存取守衛

本切片只建立模組、契約與存取守衛；請購資料表與 HTTP 端點在 002 建立。以下情境沿用 PUR-REQ-001～018 的完整類別，避免把未交付功能標為通過。

| ID | 類別 | 情境 | 層級 | 對應需求 | 結果 |
|---|---|---|---|---|---|
| PUR-BASE-001 | 正常 | PUR 與 Contracts 獨立組件由組合根載入，六個請購權限完整註冊並可在目錄查詢。 | U | REQ_F4114.3.1, REQ_F4114.3.2, REQ_F4114.3.3, REQ_F4114.3.4, REQ_F4114.3.5, REQ_F4114.3.6 | PASS（PurchasingModuleTests） |
| PUR-BASE-002 | 邊界 | 沒有公司上下文、公司 ID 為零或負數時拒絕；正數公司可取得。 | U | REQ_F4114.3.1, REQ_F4114.3.2, REQ_F4114.3.3, REQ_F4114.3.4, REQ_F4114.3.5, REQ_F4114.3.6 | PASS（PurchasingModuleTests） |
| PUR-BASE-003 | 重複與衝突 | 重複權限由既有 PermissionCatalog 拒絕；單號及版本衝突待 002。 | U,I | REQ_F4114.3.2～3 | TODO（002） |
| PUR-BASE-004 | 部分結果 | 此骨架無批次操作、資料寫入或略過路徑；部分成功／全略過／全失敗不適用。 | U | REQ_F4114.3.2 | N/A |
| PUR-BASE-005 | 權限 | 六個權限有 PUR 前綴；不屬目前公司之單據 ID 以 404 隱藏。HTTP 403 與資料範圍待 002。 | U,I | REQ_F4114.3.1, REQ_F4114.3.2, REQ_F4114.3.3, REQ_F4114.3.4, REQ_F4114.3.5, REQ_F4114.3.6 | U PASS；I 待 002 |
| PUR-BASE-006 | 狀態與關聯 | 草稿／待審／核准、MD 引用及下游採購單尚無實體；在 002／003／006 驗。 | I | REQ_F4114.3.2～6 | TODO（002／003／006） |
| PUR-BASE-007 | 錯誤 | 無公司為 403、他公司 ID 為 404；400／409／422／500 與逾時待端點及前端切片。 | U,I,E | REQ_F4114.3.1, REQ_F4114.3.2, REQ_F4114.3.3, REQ_F4114.3.4, REQ_F4114.3.5, REQ_F4114.3.6 | U PASS；I／E 待 002～004 |
| PUR-BASE-008 | 檔案 | 本切片不處理檔案或匯入；空檔、格式、大小、編碼、欄位不適用。 | U | REQ_F4114.3.2 | N/A |

## WU-PUR-000 請購情境（待 WU-PUR-001～004 實作）

以下是測試先行清單，`U` 單元、`I` 真實 SQL／HTTP、`E` Playwright。設計切片不宣稱情境已通過；實作單元先寫紅燈測試，再逐項改為通過。每個 E2E 同時核對畫面文案／樣式與重新查詢 API 或列表的實際資料。

| Test Case ID | 類別 | Description | Level | Related REQ_F | Status |
|---|---|---|---|---|---|
| PUR-REQ-001 | 正常 | 建立兩行草稿、SYS 單號唯一，庫存異動為零；畫面成功提示與資料一致。 | I,E | REQ_F4114.3.2 | TODO |
| PUR-REQ-002 | 正常 | 修改草稿、送審、SYS 最終核准及列表／詳情審批歷史一致。 | I,E | REQ_F4114.3.1, REQ_F4114.3.3, REQ_F4114.3.4 | TODO |
| PUR-REQ-003 | 正常 | 駁回附意見，修訂後重新送審；原歷史不消失。 | I,E | REQ_F4114.3.3, REQ_F4114.3.5 | TODO |
| PUR-REQ-004 | 邊界 | 空行、零／負數、超六位小數、超 1000 行、重複／零行號逐欄拒絕，資料不變。 | U,I,E | REQ_F4114.3.2 | TODO |
| PUR-REQ-005 | 邊界 | 業務日跨年／閏日可保存；需求日早於單據日、過長備註拒絕。 | U,I | REQ_F4114.3.2, REQ_F4114.3.3 | TODO |
| PUR-REQ-006 | 重複與衝突 | 同公司併發建立單號不重複；軟刪後原單號不可重用；舊版本修改／刪除 409。 | I | REQ_F4114.3.2, REQ_F4114.3.3, REQ_F4114.3.6 | TODO |
| PUR-REQ-007 | 部分結果 | 請購保存整單原子成功或失敗，不提供逐行略過；若一行無效，全部 0 行新增且顯示行原因。 | I,E | REQ_F4114.3.2 | TODO |
| PUR-REQ-008 | 部分結果 | 批次匯入／批次核准不在請購契約；部分成功、全部略過、全部失敗不適用。若日後增加批次功能須另列自動案例。 | U | REQ_F4114.3.2 | N/A |
| PUR-REQ-009 | 權限 | 缺各動作權限 403、無權頁與停用原因可理解；他公司詳情 404、列表不洩漏。 | I,E | REQ_F4114.3.1, REQ_F4114.3.2, REQ_F4114.3.3, REQ_F4114.3.6 | TODO |
| PUR-REQ-010 | 權限 | 資料範圍限制請購人／部門時，列表與詳情一致，超範圍不可修改。 | I | REQ_F4114.3.1, REQ_F4114.3.3 | TODO |
| PUR-REQ-011 | 狀態 | 待審、核准、已關閉不可修改／刪除；駁回未修訂不可刪；重複核准不重複計數。 | I,E | REQ_F4114.3.3, REQ_F4114.3.4, REQ_F4114.3.6 | TODO |
| PUR-REQ-012 | 關聯 | MD 物料停用或軟刪後，草稿保存／送審拒絕並指出行；已保存歷史仍可檢視。 | I,E | REQ_F4114.3.1, REQ_F4114.3.2, REQ_F4114.3.4 | TODO |
| PUR-REQ-013 | 關聯 | 已被採購單引用請購不得刪，轉單餘量不可負且雙連線同時轉單不可超額。 | I | REQ_F4114.3.1, REQ_F4114.3.6 | TODO（WU-PUR-006） |
| PUR-REQ-014 | 錯誤 | 400 欄位錯、409 版本、422 狀態／引用、500 traceId、逾時／斷線，前端中文原因清楚且不誤報成功。 | I,E | REQ_F4114.3.2, REQ_F4114.3.3 | TODO |
| PUR-REQ-015 | 錯誤 | SYS 審批決定或單據狀態寫入失敗時整交易回滾，重試可成功。 | I | REQ_F4114.3.4, REQ_F4114.3.5 | TODO |
| PUR-REQ-016 | 檔案 | 此切片沒有上傳或匯入；空檔、格式、大小、列數、編碼及欄位錯誤不適用。 | U | REQ_F4114.3.2 | N/A |
| PUR-REQ-017 | 介面 | 空列表、無搜尋結果、無權限、驗證錯誤、伺服器錯誤、成功各亮暗／三語畫面，訊息與資料一致。 | E | REQ_F4114.3.1, REQ_F4114.3.2 | TODO |
| PUR-REQ-018 | 介面 | 列印只用已保存資料；CSV 須完整分頁、公式安全及實際列數。 | E | REQ_F4114.3.1 | PASS：列印及 PUR-CSV-001～008 |
# WU-PUR-005f2a 詢價草稿建立／修訂畫面（REQ_F4114.4.2／3）

| ID | 類別 | 情境／斷言 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-RFQ-076 | 正常 | 建立草稿、編輯保存行及供應商；成功訊息單號與 GET 保存資料一致。 | E | REQ_F4114.4.2／3 | PASS（2026-11-28 重試：模擬 E2E 15/15；080／081 真實 HTTP／SQL `RfqFormOptionsEnforceCreateEditPermissionScopeAndSourceAvailability` 1/1，保存時失效拒絕由 `RfqDraftMutationTests` 真實 SQL 驗收） |
| PUR-RFQ-077 | 邊界 | 日期倒置、空物料／供應商、零量／七位小數、重複供應商、行及邀請上限於送出前逐欄指出；閏日與六位小數有效。 | U,E | REQ_F4114.4.2 | PASS（2026-11-28 重試：模擬 E2E 15/15；080／081 真實 HTTP／SQL `RfqFormOptionsEnforceCreateEditPermissionScopeAndSourceAvailability` 1/1，保存時失效拒絕由 `RfqDraftMutationTests` 真實 SQL 驗收） |
| PUR-RFQ-078 | 重複／衝突 | 保存帶目前版本，409 保留輸入並可確認重新載入；無變更不顯示保存成功。軟刪或重複代碼由 MD／後端拒絕，不在表單重用。 | E | REQ_F4114.4.3 | PASS（2026-11-28 重試：模擬 E2E 15/15；080／081 真實 HTTP／SQL `RfqFormOptionsEnforceCreateEditPermissionScopeAndSourceAvailability` 1/1，保存時失效拒絕由 `RfqDraftMutationTests` 真實 SQL 驗收） |
| PUR-RFQ-079 | 部分結果 | 整單保存不可部分成功或略過；任何行／供應商錯誤時零成功提示且保留草稿。 | E | REQ_F4114.4.2／3 | PASS（2026-11-28 重試：模擬 E2E 15/15；080／081 真實 HTTP／SQL `RfqFormOptionsEnforceCreateEditPermissionScopeAndSourceAvailability` 1/1，保存時失效拒絕由 `RfqDraftMutationTests` 真實 SQL 驗收） |
| PUR-RFQ-080 | 權限／狀態 | CREATE／EDIT 路由獨立授權；跨公司或資料範圍外 404；已發／關閉不可保存，換公司清舊表單；未存離開須確認。 | E | REQ_F4114.4.2／3 | PASS（2026-11-28 重試：模擬 E2E 15/15；080／081 真實 HTTP／SQL `RfqFormOptionsEnforceCreateEditPermissionScopeAndSourceAvailability` 1/1，保存時失效拒絕由 `RfqDraftMutationTests` 真實 SQL 驗收） |
| PUR-RFQ-081 | 關聯 | MD 物料及已核准供應商選項；停用、軟刪及不符請購來源由後端明示拒絕；來源量不超剩餘量；空來源不更改草稿。 | E | REQ_F4114.4.2／3 | PASS（2026-11-28 重試：模擬 E2E 15/15；080／081 真實 HTTP／SQL `RfqFormOptionsEnforceCreateEditPermissionScopeAndSourceAvailability` 1/1，保存時失效拒絕由 `RfqDraftMutationTests` 真實 SQL 驗收） |
| PUR-RFQ-082 | 錯誤 | 400 逐欄、409 重載、422 行原因、500 traceId、逾時及斷線保留輸入並可重試；三語亮暗截圖。 | E | REQ_F4114.4.2／3 | PASS（2026-11-28 重試：模擬 E2E 15/15；080／081 真實 HTTP／SQL `RfqFormOptionsEnforceCreateEditPermissionScopeAndSourceAvailability` 1/1，保存時失效拒絕由 `RfqDraftMutationTests` 真實 SQL 驗收） |
| PUR-RFQ-083 | 檔案 | 本表單沒有檔案輸入；空檔、格式、大小與編碼不適用，CSV 另由 005f2c 驗。 | E | REQ_F4114.4.2／3 | N/A |

WU-PUR-005f2a2 切片檢查（2026-10-23T06:21:24+08:00）：PUR-RFQ-080／081 已新增真實 SQL 測試原始碼，涵蓋 EDIT 無 VIEW 的自身／範圍外查詢，以及供應商選項的已核准、待審、停用、軟刪、跨公司與已保存引用。SQL 服務停止，測試尚未執行，故情境仍維持 TODO；前端服務路由契約紅燈後 3/3 通過。

WU-PUR-005f2a2 BLOCKED 重試（2026-11-28T01:15:39+08:00）：記憶體守衛 17.36／17.38／17.38 GiB PASS、SQL 服務正常。`RfqQueryTests`／`SupplierSelectionOptionsTests` 3/3；新增真實 HTTP／SQL `RequisitionAuthorizedHttpTests.RfqFormOptionsEnforceCreateEditPermissionScopeAndSourceAvailability`：VIEW-only 對三個選項端點、`/edit`、`saved-options` 皆 403，CREATE-only 對 `saved-options` 403，EDIT-only（Self）不能讀列表；CREATE／EDIT 的 `source-options` 只回同公司 Approved 且有剩餘量的請購行（3.123456），零餘量、Draft、他公司均排除，`itemId=0` 400；供應商選項只回已核准；`saved-options` 保留待審供應商與零餘量來源並標 `isApproved=false`／`isAvailable=false`；他人、他公司及不存在 RFQ 的 `/edit` 與 `saved-options` 404，未指派公司 403。實作既有，首次執行即通過（驗收補測，無產品變更）。模擬 E2E `purchasing-rfq-form.spec.ts` 15/15；帶正式 myerp SQL DocGen 0 Error／614 Warning。

WU-PUR-005f2a2 前端切片（2026-10-23T06:35:10+08:00）：`purchasing-rfq-form.spec.ts` 7/7，涵蓋 076 建立及保存資料一致、077 無效欄位零寫入、078 無修改與 409 保留、079 整單失敗零成功、080 已發不可修訂、082 400／422／500 traceId／網絡中斷與重試；與既有 4.11 合跑 15/15。`rfq-form.spec.ts` 驗已保存行、供應商、六位小數還原，前端全量 689/689。介面 4.12／4.13 亮暗及三語截圖見 `tests/qa/PUR/screenshots/`。未補：080 路由獨立授權與換公司 E2E、081 已核准請購來源選擇及停用引用拒絕、082 真實逾時、未存離開明確 E2E；SQL／HTTP 測試仍未跑。檔案情境 083 不適用：此表單無檔案輸入；CSV 屬後續 005f2 切片。故 076～082 維持 TODO，不將部分案例當整項完成。
| PUR-RFQ-084 | 契約 | POST／PUT 僅送單據日、期限、備註、順序行與供應商 ID；六位小數保留為字串，PUT 帶目前版本。 | U | REQ_F4114.4.2／3 | PASS：rfq.service.spec.ts 2/2 |
| PUR-RFQ-085 | 邊界 | 草稿預檢逐欄列出無效日期、空／過量行與供應商、重複供應商、錯誤來源 ID、零量／七位小數。 | U | REQ_F4114.4.2／3 | PASS：rfq.service.spec.ts 2/2 |

WU-PUR-005f2a2 來源選擇器情境：正常（081）顯示單號、行號、剩餘量並保存行 ID，前端服務測試及模擬 E2E 驗畫面與 POST 一致；邊界（081）空清單、頁碼／查詢、未選物料時按鈕停用並提示先選物料；重複／衝突（078）物料變更清除舊來源、保存時後端再次驗餘量；權限（080）無 RFQ CREATE／EDIT 403、他公司不列；狀態（081）未核准或餘量為零不列；錯誤（082）選擇器載入失敗顯示錯誤。單張 RFQ 整單保存無部分成功或略過；本表單無檔案。真實 SQL／HTTP 與後續 E2E 分支待驗，故 080～082 仍維持 TODO。

WU-PUR-005f2a2 補充（2026-10-23T06:56:37+08:00）：081 補已保存請購來源可讀單號／行號與停用或無餘量提示（E），同公司已保存及軟刪來源可讀、他公司不可洩露（I；SQL 尚未執行）；082 補來源選擇器 500 traceId、逾時提示及原草稿保留（E）；共用選擇器 Enter 鍵選取（E）。076～082 仍須真實 SQL／HTTP 和完整回歸後才可標 PASS。正常、邊界、重複衝突、部分結果、權限、狀態、關聯、錯誤及檔案不適用理由沿用上表。

WU-PUR-005f2a2 阻擋核對（2026-10-23T11:17:26+08:00）：按 `myerp-test-scenarios` 重核上表九類情境，原有 076～083 涵蓋正常、邊界、衝突、整單失敗、權限、狀態、關聯、錯誤及檔案不適用理由。已提交的 `RfqQueryTests` 驗已保存失效來源可讀與跨公司隱藏，但尚無 `SourceOptionsAsync` 真實 SQL 案例；恢復服務後補已核准、零餘量、跨公司及分頁斷言。`SavedOptions`、EDIT 無 VIEW、CREATE／EDIT 選項亦須受權 HTTP 驗 200／403／404，並按 081 驗保存時失效拒絕。SQL 服務停止，以上不得標 PASS；完整重試見 `blocked.md`。
## WU-PUR-008a3 — 鎖內來源身份離線預檢

| ID | 類別 | 情境 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-MATCH-U10 | 正常／關聯 | Approved 訂單與 Posted 收貨逐一對應請求；同公司、供應商、訂單、訂單行物料與幣別，全單通過。 | U | REQ_F4114.8.2～3 | PASS |
| PUR-MATCH-U11 | 邊界／重複 | 空請求、缺漏、重複、額外或非法收貨 ID 拒絕；同張發票重複引用同收貨行先聚合後只驗一個來源。 | U | REQ_F4114.8.2～3 | PASS |
| PUR-MATCH-U12 | 權限／關聯 | 訂單或收貨跨公司、供應商、採購單或幣別不符均拒絕；不得以客戶端來源身份代替鎖內資料。 | U | REQ_F4114.8.2～3 | PASS（純規則；鎖內來源待驗） |
| PUR-MATCH-U13 | 狀態／關聯 | 非 Approved 或已刪訂單、非 Posted 或已刪收貨、訂單行物料與收貨物料不符均拒絕。 | U | REQ_F4114.8.2～3 | PASS |
| PUR-MATCH-U14 | 部分結果／錯誤 | 多行中任一非法來源整單拒絕，不輸出部分成功；空身份快照視為來源錯誤。 | U | REQ_F4114.8.2～3 | PASS |

併發修改 409、資料範圍 404、SQL 500／逾時、HTTP 畫面與檔案情境：純規則沒有 I/O 或資料寫入；對應 PUR-MATCH-009、015、018、023～025 的 I/E 驗收仍待 008a／008c。沒有部分提交。

執行記錄：2026-11-11T16:00:37+08:00，先缺型別 CS0246 紅燈；定向 24/24、Architecture 1643/1643、Purchasing Debug 0 警告／錯誤。記憶體守衛 2.149／2.155／2.186 GiB DEFER，真實 SQL／HTTP 未驗。
## WU-PUR-008a15 — Draft 保存輸入整單驗證

| ID | 類別 | 情境 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-MATCH-U64 | 正常／日期邊界 | 正數供應商與訂單 ID、三字母原幣、閏日、有效發票號及行通過，回傳正規化號碼。 | U | REQ_F4114.8.2 | PASS |
| PUR-MATCH-U65 | 邊界／錯誤 | null 輸入、零 ID、空日期、非法長度／空白／非 ASCII 原幣整單拒絕；號碼與行非法保留既有專用錯誤。 | U | REQ_F4114.8.2 | PASS |
| PUR-MATCH-U66 | 重複／衝突／權限／狀態／關聯／部分結果／檔案 | 重複號碼、已刪記錄、舊版、權限、非 Draft 與權威來源留真實 SQL／HTTP 008a；本純輸入驗證不讀資料庫、無批次部分成功、無檔案。 | I,E | REQ_F4114.8.2 | TODO（008a／008c） |
| PUR-MATCH-U67 | 錯誤／介面 | 400／409／422／500／逾時的三語訊息與零寫入在真實 API／頁面驗收。 | I,E | REQ_F4114.8.2 | TODO（008a／008c） |

## WU-PUR-008a16 — Draft 行原幣價稅形狀

| ID | 類別 | 情境 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-MATCH-U68 | 正常／邊界 | 零單價／零稅、上限內單價和四位金額可保存；負值、超精度、稅率超 100 或單行／整單金額達 SQL 上限整單拒絕。 | U | REQ_F4114.8.2 | PASS（Architecture 測試） |
| PUR-MATCH-U69 | 部分結果／錯誤 | 後行金額無效時整張 Draft 拒絕，沒有部分有效行輸出；decimal 加總溢位回業務錯誤。 | U | REQ_F4114.8.2 | PASS（無寫入） |
| PUR-MATCH-U70 | 重複／衝突／權限／狀態／關聯 | 發票號重複與軟刪、並發版本、跨公司及非 Draft 由 008a 真實 SQL／HTTP 驗證；此規則不讀權威來源。 | I | REQ_F4114.8.2 | TODO（008a） |
| PUR-MATCH-U71 | 錯誤／介面／檔案 | 400／409／422／500／逾時訊息及實際零寫入由 008a／008c E2E 驗；Draft 輸入沒有檔案，上傳情境不適用。 | I,E | REQ_F4114.8.2 | TODO（檔案 N/A） |

## WU-PUR-008a17 — Draft 行價稅算術一致性

| ID | 類別 | 情境 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-MATCH-U72 | 正常／邊界 | 每行先取整未稅再算稅；最小量與半位小數 AwayFromZero 可成立。 | U | REQ_F4114.8.2 | PASS（Architecture 測試） |
| PUR-MATCH-U73 | 錯誤／部分結果 | 未稅、稅額、含稅任一與自身數量／單價／稅率不一致，整單拒絕；後行錯誤不回部分保存結果。計算溢位由 checked 捕捉回業務錯誤，合法欄位上限下無法觸發。 | U | REQ_F4114.8.2 | PASS（Architecture 測試） |
| PUR-MATCH-U74 | 重複／衝突／權限／狀態／關聯 | 本純規則不讀資料庫；重複號碼、軟刪、版本 409、權限與公司範圍、非 Draft、失效來源由 008a 真實 SQL／HTTP 驗。 | I | REQ_F4114.8.2 | TODO（008a） |
| PUR-MATCH-U75 | 介面／檔案 | 400／409／422／500／逾時須於 008c 驗三語訊息和保存資料零變更；本功能沒有檔案輸入，檔案情境不適用。 | E | REQ_F4114.8.2 | TODO（008c；檔案 N/A） |

## WU-PUR-008a26 — Draft 修訂號碼鍵決策

| ID | 類別 | 情境 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-MATCH-U105 | 正常／邊界 | 原號與新號正規鍵相同時輸出 Retained，舊／新鍵均為已驗正規鍵；顯示值只改大小寫仍保留。新建沒有修訂資料。 | U | REQ_F4114.8.3 | PASS |
| PUR-MATCH-U106 | 正常／重複 | 改號時輸出 Changed、保存舊鍵與新驗鍵；新鍵被其他持有者（含軟刪）佔用時不回意圖。 | U | REQ_F4114.8.3 | PASS |
| PUR-MATCH-U107 | 狀態／權限／關聯／部分結果 | 非 Draft、舊版、跨公司或失效舊鍵持有者整單拒絕；本純規則不提交資料，部分成功不適用。SQL 權限、範圍與併發由 008a 驗。 | U,I | REQ_F4114.8.3 | U PASS；I TODO（008a） |
| PUR-MATCH-U108 | 錯誤／介面／檔案 | 400／409／422／500／逾時的三語提示及零寫入由 008a／008c 驗；本規則無 HTTP、畫面或檔案，上傳情境不適用。 | I,E | REQ_F4114.8.3 | TODO（008a／008c；檔案 N/A） |

執行記錄：2026-11-12T06:25:49+08:00，缺少輸出型別及屬性先 CS1061／CS0246／CS0103 紅燈，修正後定向 19/19。真實 SQL／HTTP 未驗。

## WU-PUR-008a27 — Draft 改號鍵持久化計畫

| ID | 類別 | 情境 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-MATCH-U109 | 正常／重複 | 已驗 Draft 改號輸出新活動鍵與須永久保留的舊鍵；舊鍵供後續重用判衝突。 | U | REQ_F4114.8.2～3 | PASS（離線計畫） |
| PUR-MATCH-U110 | 正常／邊界 | 原號修訂（顯示值大小寫不同）及新建只佔用新正規鍵，不另外保留舊鍵。 | U | REQ_F4114.8.2～3 | PASS（離線計畫） |
| PUR-MATCH-U111 | 狀態／權限／關聯／部分結果 | 非 Draft、舊版、跨公司或失效權威仍整單拒絕；此純計畫無批次寫入，部分成功不適用。SQL 同交易保存、權限、唯一索引和併發由 008a 驗。 | U,I | REQ_F4114.8.2～3 | U PASS；I TODO（008a） |
| PUR-MATCH-U112 | 錯誤／介面／檔案 | 400／409／422／500／逾時及三語提示與零寫入由 008a／008c 驗；此規則無 HTTP、畫面或檔案，檔案情境不適用。 | I,E | REQ_F4114.8.2～3 | TODO（008a／008c；檔案 N/A） |

執行記錄：2026-11-12T06:31:21+08:00，缺少輸出型別及屬性先 CS1061／CS0246 紅燈，定向 20/20 通過；SQL 守衛 DEFER，真實持久化未驗。

## WU-PUR-008a28 — 同張歷史與活動號碼鍵

| ID | 類別 | 情境 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-MATCH-U113 | 正常／重複 | 同張同範圍的歷史鍵及活動鍵均可在快照中；本張修訂可沿用，其他發票使用任一鍵都 409。 | U | REQ_F4114.8.2～3 | PASS |
| PUR-MATCH-U114 | 邊界／衝突／權限 | 同發票跨公司或供應商、同鍵重複、非正規鍵均拒絕失效快照；真實權限與 SQL 完整快照由 008a 驗。 | U,I | REQ_F4114.8.2～3 | U PASS；I TODO（008a） |
| PUR-MATCH-U115 | 狀態／關聯／部分結果 | 修訂仍要求活動鍵匹配保存表頭，非 Draft／舊版／已刪照既有測試拒絕；單張離線預檢無部分成功、全部略過或檔案輸入。 | U | REQ_F4114.8.3 | PASS（既有測試；批次／檔案 N/A） |
| PUR-MATCH-U116 | 錯誤／介面 | 400／409／422／500／逾時及三語提示與資料零變更由 008a／008c 真實 SQL、HTTP、畫面驗收。 | I,E | REQ_F4114.8.2～3 | TODO（008a／008c） |

執行記錄：2026-11-12T06:37:50+08:00，同張多鍵的 `INVALID_NUMBER_SNAPSHOT` 先紅後綠；Architecture 1859/1859，PUR Debug 零警告。SQL 守衛 DEFER，無持久化驗收。
# WU-PUR-008a33 — Draft 表頭及行離線寫入映射

| ID | 類別 | 情境 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-MATCH-U117 | 正常／關聯 | 新建指定正數 ID；表頭與逐行公司、來源、號碼、日期、三種總額均取同一已重驗意圖；同來源多行按行號穩定排序。 | U | REQ_F4114.8.2 | PASS（離線） |
| PUR-MATCH-U118 | 狀態／衝突 | 修訂保留保存 ID 與期望版本，錯誤或非正數 ID 整單拒絕；非 Draft、舊版本及已刪由保存意圖先拒絕。 | U | REQ_F4114.8.3 | PASS（離線）；SQL 409 待 008a |
| PUR-MATCH-U119 | 邊界／部分結果 | 雙行同來源各保存獨立行號、六位量與四位價稅；返回行清單不可改寫，無部分行計畫。單張離線計畫無批次部分成功／略過。 | U | REQ_F4114.8.2 | PASS（離線） |
| PUR-MATCH-U120 | 權限／重複／關聯／錯誤／檔案 | 真實跨公司及資料範圍、他張含軟刪號碼、併發唯一索引、SQL 400／409／422／500／逾時與畫面提示由 008a／008c 驗；本計畫無 I/O、HTTP、檔案輸入。 | I,E | REQ_F4114.8.2～3 | TODO（008a／008c；檔案 N/A） |

執行記錄：2026-11-12T07:14:58+08:00，先缺 `PlanDraftRows` 與行型別 CS1061／CS0246 紅燈；補實作後定向 6/6、Architecture 1882/1882、PUR Debug 0 警告／錯誤。記憶體守衛 2.149／2.177／2.172 GiB DEFER，未作 SQL／HTTP 驗收。
# WU-PUR-008a34 — Draft 離線 EF 實體組裝

| ID | 類別 | 情境 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-MATCH-U128 | 正常／關聯 | 新建與修訂從行計畫組成 Draft EF 表頭與行；保留指定 ID、公司／來源參照、原幣、日期、號碼、三種總額、行號、價稅與修訂版本；兩次組裝互不影響。 | U | REQ_F4114.8.2～3 | PASS（離線） |
| PUR-MATCH-U129 | 邊界／重複／衝突 | 零或負 ID、零版本、空行、重複／非正數行號、行公司／發票參照錯置、總額不符整單拒絕。軟刪號碼重用與 SQL 409 仍由 008a 驗。 | U,I | REQ_F4114.8.2～3 | PASS（離線）；SQL TODO |
| PUR-MATCH-U130 | 權限／狀態／部分結果／錯誤／檔案 | 已保存 Draft 狀態、版本、跨公司、權限與資料範圍由前序意圖及 008a SQL／HTTP 驗；單張離線組裝無部分成功或略過，亦無檔案輸入。400／409／422／500／逾時與畫面由 008a／008c 驗。 | I,E | REQ_F4114.8.2～3 | TODO（008a／008c；部分結果、檔案 N/A） |

執行記錄：2026-11-12T11:01:46+08:00，缺 `Materialize` 先 CS1061 紅燈；SQL 守衛 0.577／0.587／0.630 GiB DEFER。補邊界後定向 8/8、Architecture 1890/1890、PUR Debug 零警告；真實 SQL／HTTP、交易與競態仍待 008a。
## WU-PUR-008a40 — Draft EF 持有列追蹤準備

| ID | 類別 | 情境／預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-MATCH-U146 | 正常／邊界／關聯 | 新建 Draft 的活動持有列為 Added，沒有既有追蹤列。 | U | REQ_F4114.8.2 | PASS |
| PUR-MATCH-U147 | 正常／狀態／關聯 | 改回本張歷史鍵時兩個同參照既有列皆為 Modified，沒有新增列。 | U | REQ_F4114.8.3 | PASS |
| PUR-MATCH-U148 | 正常／邊界／部分結果 | 原號修訂的活動與歷史列均為 Unchanged；單張單據不適用批次部分成功、全部略過或全部失敗。 | U | REQ_F4114.8.3 | PASS |
| PUR-MATCH-U149 | 重複／衝突／錯誤 | 同 ID 不同實體已追蹤或追蹤列已有待處理變更時，在號碼旗標變更前拒絕。 | U | REQ_F4114.8.3 | PASS |
| PUR-MATCH-U150 | 邊界／狀態／錯誤 | Draft 計畫版本漂移時拒絕，沒有附加或修改持有列。 | U | REQ_F4114.8.3 | PASS |
| PUR-MATCH-034 | 權限／跨公司／狀態／HTTP 錯誤／檔案 | 公司與 Draft 狀態由既有計畫重驗；403／404、409、422、500、逾時、SQL 回滾與競態待 008a。畫面與資料雙斷言待 008c。沒有檔案，不適用檔案情境。 | U,I,E | REQ_F4114.8.2～3 | U PASS；I/E TODO |

執行記錄：2026-11-12T11:45:47+08:00；守衛 1.726／1.721／1.725 GiB DEFER；CS0103 紅燈後定向 6/6、Architecture 1911/1911、PUR Debug 零警告。真實 SQL／HTTP 待 008a。

## WU-PUR-008a41 — Draft EF 表頭及行追蹤準備

| ID | 類別 | 情境／預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-MATCH-U151 | 正常／關聯 | 新建表頭、行與號碼列均為 Added，同一已驗計畫保留來源與行號。 | U | REQ_F4114.8.2 | PASS |
| PUR-MATCH-U152 | 正常／狀態 | 修訂使用同參照已追蹤表頭，保留版本，舊行 Deleted、新行 Added，改號持有列 Modified。 | U | REQ_F4114.8.3 | PASS |
| PUR-MATCH-U153 | 重複／衝突／權限 | 同 ID 不同表頭實體、跨公司或錯誤身份先拒絕；真實公司權限及併發版本 409 待 008a。 | U,I | REQ_F4114.8.3 | U PASS；I TODO |
| PUR-MATCH-U154 | 邊界／部分結果／關聯 | 既有行集合不完整或版本漂移時拒絕，表頭、行與號碼列無部分變更；單張保存不適用批次部分成功、全部略過。 | U | REQ_F4114.8.3 | PASS；批次 N/A |
| PUR-MATCH-035 | 錯誤／介面／檔案 | 400／409／422／500／逾時、SQL 回滾及畫面與資料雙斷言待 008a／008c；本切片無 API、畫面或檔案輸入。 | I,E | REQ_F4114.8.2～3 | TODO（008a／008c；檔案 N/A） |

執行記錄：2026-11-12T11:52:34+08:00；守衛 1.666／1.650／1.720 GiB DEFER；CS0103 紅燈後定向 11/11、Architecture 1916/1916。真實 SQL／HTTP 待 008a。

## WU-PUR-008a42 — 新建 Draft 的 SQL IDENTITY 接線

| ID | 類別 | 情境／預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-MATCH-U155 | 正常／關聯／邊界 | 新建計畫正數預指派 ID 僅作預檢，追蹤後表頭為暫存鍵，行及活動號碼列共用該鍵；模擬實際 ID 回填時一同更新。 | U | REQ_F4114.8.2 | PASS（離線） |
| PUR-MATCH-U156 | 重複／衝突／狀態 | 既有號碼、失效全圖及修訂版本沿既有測試整單拒絕；修訂不改已保存表頭 ID。同模組 FK migration 可逆。 | U | REQ_F4114.8.2～3 | PASS（離線） |
| PUR-MATCH-036 | 權限／部分結果／錯誤／檔案 | 單張保存無批次部分成功或略過；本切片無 API、畫面或檔案輸入。403、409、422、500、逾時、SQL ID 回填與回滾、畫面與資料雙斷言由 008a／008c 驗。 | I,E | REQ_F4114.8.2～3 | I/E TODO；批次／檔案 N/A |

執行記錄：2026-11-12T12:04:26+08:00；記憶體守衛 1.784／1.829／1.836 GiB DEFER。先以缺 `Invoice` 導覽屬性的 CS1061 紅燈，補綁定與 migration 後定向通過、Architecture 1918/1918、PUR 與 migrations Debug 零警告；真實 SQL／HTTP 待 008a。

## WU-PUR-008a44 — Draft SQL session 殼層

| ID | 類別 | 情境／預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-MATCH-U160 | 正常／邊界 | 公司與供應商鎖資源穩定，相同範圍同鍵、不同公司或供應商異鍵；非正數 ID 拒絕。單次保存順序由 008a43 的協調器測試保護。 | U | REQ_F4114.8.2～3 | PASS 4/4 |
| PUR-MATCH-038 | 重複／衝突／關聯 | 同供應商競態、軟刪歷史號、版本 409；Serializable 與交易鎖、權威來源及完整持有列在真實 SQL 下驗證。 | I | REQ_F4114.8.2～3 | TODO 008a |
| PUR-MATCH-039 | 部分結果／狀態／權限／錯誤 | 零列保存與失敗回滾、跨公司、非 Draft、403／409／422／500／逾時及畫面與資料雙斷言。單張無批次略過或部分成功；檔案不適用。 | U,I,E | REQ_F4114.8.2～3 | U 由 008a43 PASS；I/E TODO 008a／008c |

執行記錄：2026-11-12T12:15:58+08:00；守衛 1.837／1.840／1.846 GiB DEFER；缺類型編譯紅燈後定向 4/4。SQL session 尚未接入權威讀取器或 HTTP，不宣稱 SQL 驗收。

## WU-PUR-008a43 — Draft 保存交易協調（離線契約）

| ID | 類別 | 情境／預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-MATCH-U157 | 正常／關聯 | 先開始交易並鎖內讀取、重驗及準備，單次保存後確認 IDENTITY ID 才提交。 | U | REQ_F4114.8.2 | PASS |
| PUR-MATCH-U158 | 錯誤／部分結果 | SaveChanges 失敗或零列變更，整單回滾、不得提交；單張保存無批次部分成功或略過。 | U | REQ_F4114.8.2～3 | PASS；批次 N/A |
| PUR-MATCH-U159 | 狀態／權限／來源 | 鎖內權威價稅來源變動時在準備前拒絕、回滾。跨公司、非 Draft、過期版本由既有意圖先拒絕；真實授權待 008a。 | U,I | REQ_F4114.8.2～3 | U PASS；I TODO |
| PUR-MATCH-037 | 重複／衝突／HTTP／檔案 | 唯一索引 2601／2627 映射 409、真實 IDENTITY、SQL 回滾、403／409／422／500／逾時與畫面資料雙斷言待 008a／008c；無檔案輸入。 | I,E | REQ_F4114.8.2～3 | TODO；檔案 N/A |

執行記錄：2026-11-12T12:10:16+08:00；守衛 2.005／2.006／2.007 GiB DEFER；xUnit1051 編譯紅燈修正後定向 4/4。真實 SQL／HTTP 待 008a。

## WU-PUR-008a49 — Draft 交易鎖逾時錯誤分類

| ID | 類別 | 情境／預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-MATCH-U161 | 正常／衝突 | 應用鎖專屬 SQL 51001 回 `PUR.INVOICE.DRAFT_LOCK_UNAVAILABLE` 409；Begin 失敗回滾、釋放且不提交。 | U | REQ_F4114.8.2 | PASS |
| PUR-MATCH-U162 | 邊界／錯誤 | SQL 1205 與號碼唯一索引 2601 不分類成草稿鎖逾時；其餘錯誤仍由原路徑處理。 | U | REQ_F4114.8.2 | PASS |
| PUR-MATCH-040 | 權限／狀態／關聯／部分結果／檔案 | 此切片只處理單次 SQL 鎖失敗；403、跨公司、非 Draft、零列及關聯來源驗證留 008a51／52；無批次或檔案輸入，故部分成功／全部略過及檔案類別不適用。 | I,E | REQ_F4114.8.2 | TODO 008a51／52；不適用項已列原因 |
| PUR-MATCH-041 | HTTP／畫面 | 409／500／逾時的三語提示與資料未變雙斷言，待公開 CREATE／EDIT 及畫面切片；目前無對應 HTTP。 | I,E | REQ_F4114.8.2 | TODO 008a51／008c |

## WU-PUR-008a49b — Draft HTTP 命令邊界（離線）

| ID | 類別 | 情境／預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-MATCH-U163 | 正常 | 建立命令只持有服務端公司、已驗請求，不帶發票 ID／版本；原幣正規化。 | U | REQ_F4114.8.2 | PASS |
| PUR-MATCH-U164 | 邊界／衝突 | 修訂要求正數路由 ID 與正數期望版本；零／負值不形成命令。真正舊版本 409 留 HTTP／SQL 驗。 | U,I | REQ_F4114.8.3 | U PASS；I TODO 008a51 |
| PUR-MATCH-U165 | 權限／跨公司 | 公司 ID 只由未來控制器的服務端授權上下文供給；零／負 ID 拒絕。無權／其他公司與資料範圍 HTTP 403／404 留 008a51。 | U,I | REQ_F4114.8.2～3 | U PASS；I TODO 008a51 |
| PUR-MATCH-U166 | 錯誤／狀態／關聯 | 空 body 或無效行不形成命令；非 Draft、軟刪、來源停用、號碼重複、版本衝突由鎖內權威重驗。400／409／422／500／逾時的 HTTP 與畫面提示留 008a51／008c。 | U,I,E | REQ_F4114.8.2～3 | U PASS；I/E TODO |
| PUR-MATCH-042 | 部分結果／檔案 | 單張 Draft 命令不提供批次、匯入或檔案；部分成功、全部略過及檔案錯誤不適用。無效輸入沒有部分命令。 | U | REQ_F4114.8.2～3 | N/A（已列原因） |

執行記錄：2026-11-26T06:13:29+08:00；記憶體守衛 2.126／2.110／2.053 GiB DEFER。`PurchaseInvoiceDraftHttpIntentTests` 先因 CS0103 紅燈，實作後 3/3；真實 SQL、權限 HTTP 與畫面仍待 008a50／51／008c。

## WU-PUR-008a49c — 已驗命令的來源組裝（離線）

| ID | 類別 | 情境／預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-MATCH-U167 | 正常／邊界／權限 | 命令以服務端公司與閏日、正規幣別組裝；原請求行清單變動後仍用驗證時的收貨行。 | U | REQ_F4114.8.2～3 | PASS |
| PUR-MATCH-U168 | 跨公司／關聯 | 命令公司與訂單／收貨公司不符時拒絕，不產生來源快照。 | U | REQ_F4114.8.2～3 | PASS |
| PUR-MATCH-043 | 重複／衝突／狀態／錯誤 | 重複號碼、軟刪來源、非 Draft、版本衝突及 400／409／422／500／逾時由 008a51／52 的權威 SQL 與 HTTP 驗；本切片沿用既有來源和金額純規則。 | I,E | REQ_F4114.8.2～3 | TODO 008a51／52 |
| PUR-MATCH-044 | 部分結果／檔案 | 單張 Draft 命令沒有批次、略過或檔案輸入；不適用。 | U | REQ_F4114.8.2～3 | N/A（已列原因） |

## WU-PUR-008a49d — Draft HTTP 來源行查詢範圍（離線）

| ID | 類別 | 情境／預期 | 層級 | 需求 | 結果 |
|---|---|---|---|---|---|
| PUR-MATCH-U169 | 正常／邊界／重複 | 三個有效行含同一收貨行兩次且原順序不同；命令來源 ID 升冪去重。 | U | REQ_F4114.8.2～3 | PASS |
| PUR-MATCH-U170 | 衝突／錯誤 | 命令建立後原行清單清空，來源範圍保持不變；呼叫者嘗試修改唯讀來源清單被拒絕。 | U | REQ_F4114.8.2～3 | PASS |
| PUR-MATCH-045 | 權限／跨公司／狀態／關聯／HTTP 錯誤 | 無權、跨公司、來源停用、非 Draft、409、422、500、逾時由 008a51／52 在 SQL 與 HTTP 驗，現階段只有固定查詢範圍。 | I,E | REQ_F4114.8.2～3 | TODO 008a51／52 |
| PUR-MATCH-046 | 部分結果／檔案 | 單張 Draft 命令沒有批次、略過或檔案輸入，故部分成功、全部略過、檔案錯誤不適用。 | U | REQ_F4114.8.2～3 | N/A（已列原因） |

執行記錄：2026-11-26T06:24:54+08:00；守衛 2.433／2.435／2.437 GiB DEFER；缺屬性先 CS1061 紅燈，實作後定向 4/4。來源查詢尚未接 HTTP，SQL 與畫面不計通過。

## WU-PUR-008c1 介面 4.19 發票唯讀列表／明細（2026-11-26T22:39:48+08:00）

| ID | 類別 | 情境 | 層級 | 需求 | 狀態 |
|---|---|---|---|---|---|
| PUR-INV-UI-U01 | 邊界 | 反向日期範圍、0／負數／小數 ID 前端拒絕；同日範圍、空白、最大安全整數通過。 | U | REQ_IO_A4.19.1 | PASS |
| PUR-INV-UI-U02 | 三語 | 錯誤碼、五狀態及狀態提示、選單與全部畫面文字三語鍵一致。 | U | REQ_IO_A4.19.1～4 | PASS |
| PUR-INV-UI-U03 | 權限 | 路由需 `PUR.INVOICE.VIEW`；無此權限選單不顯示。 | U | REQ_F4114.8.1 | PASS |
| PUR-INV-UI-001 | 正常／狀態 | 列表 19 位精確含稅額、五狀態標籤；唯讀頁沒有任何寫入動作。 | U | REQ_IO_A4.19.2 | PASS |
| PUR-INV-UI-002 | 空／無結果 | 空列表與篩選無結果訊息不同；篩選值修剪後原樣送出。 | U | REQ_IO_A4.19.1～2 | PASS |
| PUR-INV-UI-003 | 驗證 | 無效篩選顯示原因且不送請求；修正後訊息消失。 | U | REQ_IO_A4.19.1 | PASS |
| PUR-INV-UI-004 | 正常 | 伺服器排序／分頁參數，翻頁保留已套用（非輸入中）篩選。 | U | REQ_IO_A4.19.2 | PASS |
| PUR-INV-UI-005 | 正常 | 明細行、Matched「尚未過帳」提示、無決定歷史空狀態、關閉。 | U | REQ_IO_A4.19.3～4 | PASS |
| PUR-INV-UI-006 | 正常 | 決定歷史逐行顯示版本、接受／拒絕、原因與帶符號差額。 | U | REQ_IO_A4.19.4 | PASS |
| PUR-INV-UI-007 | 錯誤 404／競態 | 明細 404 說明原因並重載列表；過期明細回應被忽略。 | U | REQ_IO_A4.19.3 | PASS |
| PUR-INV-UI-008 | 錯誤 500／403 | 500 顯示 traceId 及載入失敗訊息；403 顯示無權限。 | U | REQ_IO_A4.19.2 | PASS |
| PUR-INV-UI-009 | 跨公司 | 切換公司清除篩選與明細，舊公司回應不覆蓋新結果。 | U | REQ_F4114.8.1 | PASS |
| PUR-INV-UI-E01 | 正常／三語亮暗 | 模擬 API：精確金額、五狀態、明細與決定歷史，zh-Hant 亮暗＋en＋zh-Hans 截圖；只發 GET。 | E | REQ_IO_A4.19.1～4 | PASS |
| PUR-INV-UI-E02 | 空／無結果／驗證／404／500 | 各狀態畫面訊息，驗證錯誤不發請求，500 不留舊列；亮暗截圖。 | E | REQ_IO_A4.19.1～3 | PASS |
| PUR-INV-UI-NA | 不適用 | 重複與衝突（409）、部分結果、檔案：唯讀頁無寫入、批次或檔案；寫入情境屬 008c2／008c3。逾時沿用共用錯誤攔截器（UNKNOWN／NETWORK）。真實 SQL 列表／明細已由 008a55 HTTP 整合測試驗收。 | — | — | N/A（已列原因） |

執行記錄：2026-11-26T22:39:48+08:00；前端單元全量 1276/1276、production 建置成功、Playwright `purchasing-invoice.spec.ts` 2/2；截圖 `tests/qa/PUR/screenshots/4.19-*.png` 14 張經檢視無 P1／P2（P3：明細在列表下方需捲動，與其他 PUR 頁一致）。

## WU-PUR-008b1 — 發票 → FI 應付快照離線映射（ADR-205）

| ID | 類別 | 情境 | 層級 | 需求 | 狀態 |
|---|---|---|---|---|---|
| PUR-FISNAP-001 | 正常 | 外幣發票逐行以保存匯率換算本位幣未稅／稅額（按本位幣小數位四捨五入），含稅＝兩者和；表頭本位幣＝行合計；行按行號排序；識別、版本、指紋原樣帶出。 | U | REQ_F4114.8.2～5、BR-PUR-043 | PASS |
| PUR-FISNAP-002 | 正常 | 映射結果與相符命令通過 FI `PurchaseInvoicePostingRules.CheckSnapshot`（外幣與本位幣各一）。 | U | BR-FI-017 | PASS |
| PUR-FISNAP-003 | 邊界 | 本位幣發票：本位幣金額＝原幣（四位小數不再捨入）；匯率非 1 拒絕。 | U | BR-PUR-043 | PASS |
| PUR-FISNAP-004 | 狀態 | Draft／Matched／Disputed／Rejected／Posted 一對一；軟刪除優先映射為 Deleted。 | U | BR-FI-015 | PASS |
| PUR-FISNAP-005 | 狀態 | 非 Matched（例如 Disputed）照實映射，由 FI 回 422 SOURCE_NOT_POSTABLE；PUR 不自行判定可過帳。 | U | BR-FI-015 | PASS |
| PUR-FISNAP-006 | 關聯 | 保存表頭與行合計不符時原樣帶出、不重算；FI 回 422 INVALID_AMOUNTS。 | U | BR-FI-017 | PASS |
| PUR-FISNAP-007 | 邊界 | 八位小數匯率、零稅行保留。 | U | BR-PUR-043 | PASS |
| PUR-FISNAP-008 | 錯誤 | 匯率 0、負數、超過八位小數拒絕（PUR.INVOICE.INVALID_POSTING_SNAPSHOT）。 | U | BR-PUR-043 | PASS |
| PUR-FISNAP-009 | 錯誤 | 公司／發票／供應商 ID、版本 ≤0、幣別非大寫三碼、指紋空白、無行、重複／非正行號、null 行或參數，全部拒絕且不產生部分快照。 | U | BR-FI-017 | PASS |
| PUR-FISNAP-010 | 邊界 | 換算後超出 decimal(19,4) 上限拒絕，不截斷。 | U | BR-FI-017 | PASS |
| PUR-FISNAP-011 | 邊界 | 呼叫端行集合事後變更不影響已建快照。 | U | — | PASS |
| PUR-FISNAP-NA | 不適用 | 權限、重複衝突、部分結果、檔案：純函式無 I/O；SYS 範圍、鎖及 FI 交易屬 008b3／008b4。 | — | — | N/A |

## WU-PUR-008b2 — 匯率／本位幣來源（ADR-206）

| ID | 類別 | 情境 | 層級 | 需求 | 狀態 |
|---|---|---|---|---|---|
| PUR-FXSRC-001 | 正常 | 外幣採購單快照（USD、本位幣 TWD、匯率 31.5）＋公司本位幣 TWD＋發票 USD → `PurchaseInvoiceExchangeSnapshot(TWD, 31.5)`；結果可直接交 008b1 `Build`。 | U | REQ_F4114.8.2～5、BR-PUR-043b | PASS |
| PUR-FXSRC-002 | 正常 | 本位幣採購單（TWD／TWD／1）→ (TWD, 1)。 | U | BR-PUR-043b | PASS |
| PUR-FXSRC-003 | 錯誤 | 採購單沒有匯率快照（本位幣與匯率皆空，006a 前的來源列）→ 422 `PUR.INVOICE.RATE_SNAPSHOT_MISSING`，不以當日匯率代替。 | U | BR-PUR-043b | PASS |
| PUR-FXSRC-004 | 錯誤 | 公司沒有本位幣（ICurrencyLookup 回 null）→ 422 `PUR.INVOICE.BASE_CURRENCY_NOT_SET`。 | U | BR-PUR-043b | PASS |
| PUR-FXSRC-005 | 衝突 | 快照本位幣與公司目前本位幣不同 → 422 `PUR.INVOICE.BASE_CURRENCY_CHANGED`，不換算。 | U | BR-PUR-043b | PASS |
| PUR-FXSRC-006 | 錯誤 | 發票原幣與採購單原幣不同、只有一半快照、匯率 ≤0／超過八位小數、同幣別匯率非 1、幣別非大寫三碼 → `PUR.INVOICE.INVALID_POSTING_SNAPSHOT`（資料錯誤）。 | U | BR-PUR-043 | PASS |
| PUR-FXSRC-007 | 邊界 | 八位小數匯率原樣帶出；公司本位幣大小寫不同（`twd`）視為同一幣別並以大寫輸出。 | U | BR-PUR-043b | PASS |
| PUR-FXSRC-008 | 整合 | 真實 SQL：`pur_purchase_order.base_currency_code char(3) NULL`、`exchange_rate decimal(19,8) NULL`；CHECK `ck_pur_order_rate_snapshot` 接受兩者皆空、外幣正匯率、同幣別 1；拒絕只填一個、匯率 0／負、同幣別非 1。 | I | REQ_DS43.186、MASTER_PROMPT 6.3 | PASS |
| PUR-FXSRC-009 | 整合 | 真實 SQL 讀取：交易內按公司＋採購單讀快照並經 ICurrencyLookup 驗證；他公司／不存在回 null；沒有交易拋 InvalidOperationException；未遷移舊列回 RATE_SNAPSHOT_MISSING。 | I | BR-PUR-043b | PASS |
| PUR-FXSRC-010 | 整合 | Migration `Purchasing_OrderRateSnapshot` 在測試庫 Down／Up 可逆（欄位 2→0→2、約束重建）；新欄可空，既有列不需回填（測試庫當時 0 列，未能實測保留）。 | I | — | PASS |
| PUR-FXSRC-NA | 不適用 | 權限：內部埠，無 HTTP（PUR.INVOICE.POST 屬 008b4）；部分結果：單一採購單整體成功或拒絕；檔案：無；採購單狀態（Approved／Cancelled）與發票狀態由 008a9 預檢及 FI 判定，不在本來源重複。 | — | — | N/A |

執行記錄：2026-11-27T00:29:00+08:00；記憶體守衛 PASS；單元 PUR-FXSRC-001～007（19 案例）通過；真實 SQL PUR-FXSRC-008／009 首次失敗（CHECK 對 NULL 匯率回 UNKNOWN 而放行；測試輔助 decimal 參數被截為 scale 2），修正後 2/2，PurchaseInvoiceSchemaTests 2/2；myerp_test Down／Up；後端全量 4025/4025（--max-threads 2）。

## WU-PUR-008b3 — FI 應付來源埠（ADR-207）

| ID | 類別 | 情境 | 層級 | 需求 | 狀態 |
|---|---|---|---|---|---|
| PUR-FISRC-001 | 邊界 | 目前無差異 → 接受證據為空，即使有舊輪次 RESOLVE（含 Reject）也不採用。 | U | BR-PUR-043c | PASS |
| PUR-FISRC-002 | 正常 | 有差異 → 取最新一輪（最大發票版本）RESOLVE，逐列帶目前重算的逐種差異（UNIT_PRICE／NET／TAX／GROSS），舊輪 Reject 不影響；結果可產生 040a 指紋。 | U | BR-PUR-043c、BR-PUR-040a | PASS |
| PUR-FISRC-003 | 錯誤 | 保存的未稅／稅額／含稅差額合計與重算不符、決定列對應無差異的行、有差異但沒有任何 RESOLVE 列 → 422 `PUR.INVOICE.INVALID_POSTING_DIFFERENCE`。 | U | BR-PUR-043c | PASS |
| PUR-FISRC-004 | 狀態 | 最新一輪只涵蓋部分差異行（舊輪完整也不補）或含 Reject → 指紋預檢 422 INVALID_POSTING_DIFFERENCE。 | U | BR-PUR-040a | PASS |
| PUR-FISRC-005 | 狀態 | Draft／Disputed／Rejected／Posted／軟刪除 → 只報身份、版本、狀態與原幣合計（無指紋／匯率／行），FI `CheckSnapshot` 回 422 SOURCE_NOT_POSTABLE。 | U | BR-FI-015 | PASS |
| PUR-FISRC-006 | 錯誤 | NotPostable 拒絕 Matched、版本 ≤0、幣別非大寫三碼、null（INVALID_POSTING_SNAPSHOT）。 | U | BR-PUR-043c | PASS |
| PUR-FISRC-007 | 整合 | 真實 SQL＋正式 FI 過帳服務：USD 已比對發票（單價差異經 RESOLVE 接受，版本 2）→ 來源讀出原幣 10.5／2.1／12.6、本位幣 330.75／66.15／396.90（匯率 31.5、MD 小數位）與測試端獨立計算的指紋；呼叫端回滾不留列、提交寫 FI 來源／應付／傳票 1／1／1（3 行），收據本位幣含稅 396.90；重放 Unchanged 不重寫。 | I | REQ_F4114.8.5、BR-PUR-043c | PASS |
| PUR-FISRC-008 | 狀態／權限 | Disputed（無匯率快照也不影響）、Draft、軟刪除 Matched → FI 422；他公司發票、不存在 → 404；全部不寫入；軟刪除直接讀為 Deleted；以他公司身分查詢回 null。 | I | BR-FI-015、BR-PUR-043c | PASS |
| PUR-FISRC-009 | 衝突 | 指紋或版本不符 → 409 SOURCE_MISMATCH；保存業務日被另一交易改動後同一命令 409；改回後本位幣發票（TWD、匯率 1）可過帳，本位幣＝原幣 12。 | I | BR-PUR-040a、BR-FI-016 | PASS |
| PUR-FISRC-010 | 錯誤 | 已比對但採購單無匯率快照 → PUR 422 RATE_SNAPSHOT_MISSING；已比對但有差異無 RESOLVE → 422 INVALID_POSTING_DIFFERENCE；FI 不寫任何列；不在呼叫端交易內讀取 → InvalidOperationException。 | I | BR-PUR-043b、BR-PUR-043c | PASS |
| PUR-FISRC-011 | 整合 | PurchasingModule 只註冊一個 `IFinancePurchaseInvoiceSource`（PurchaseInvoiceFinanceSource）。 | I | ADR-207 | PASS |
| PUR-FISRC-NA | 不適用 | 權限（SYS 範圍）：內部埠，由 008b4 POST 命令檢查 `PUR.INVOICE.POST`；部分結果：單張發票整體成功或拒絕；檔案：無；鎖與並發：埠不取鎖，鎖序屬 008b4（BR-PUR-043）。 | — | — | N/A |

執行記錄：2026-11-27T00:51:29+08:00；單元 PUR-FISRC-001～006（10 案例，連同 FISNAP 29/29）通過；真實 SQL PUR-FISRC-007～011 5/5（首跑即通過：純規則與埠先於測試寫成，無紅燈證據，見 optimization-log）；後端全量 4040/4040（--max-threads 2）、Debug 建置 0 警告。

## WU-PUR-008b4 — 發票過帳 POST（ADR-208）

情境依 myerp-test-scenarios 清單：正常、重放、版本衝突、狀態限制、權限（含資料範圍）、他公司／不存在／已刪除、FI 拒絕與故障、來源證據失效、資料庫約束；部分成功與檔案不適用（單張發票整體成功或整體不寫）。

| ID | 類別 | 情境 | 層級 | 需求 | 狀態 |
|---|---|---|---|---|---|
| PUR-POST-001 | 正常／重放 | USD 已比對發票（價差經 RESOLVE 接受，版本 2）過帳：回 Posted、版本 3、過帳時間＝IClock；FI 來源／應付／傳票各 1、本位幣含稅 396.90（訂單快照 31.5）；收貨行 matched_reserved 5→0、posted_invoiced 0→5；發票保存 FI 三個 ID 與 posted_from_version 2。以版本 2 或 3 重送回同一結果 `replayed=true` 且不再寫入；版本 1 → 409。 | I（真實 SQL） | REQ_F4114.8.5、BR-PUR-043d | PASS |
| PUR-POST-002 | 錯誤／權限／狀態 | 版本 0 → 400；舊版本 1、未來版本 3 → 409；他公司、軟刪除、不存在 → 404 `PUR.INVOICE.NOT_FOUND`；Draft、Disputed → 422 `INVALID_POST_STATUS`；只有 MATCH 權限、或 POST 只限本人範圍 → 403；全程 FI 與 PUR 零變更。 | I（真實 SQL） | BR-PUR-042、BR-PUR-043d | PASS |
| PUR-POST-003 | 錯誤 | 採購單無匯率快照 → PUR 422 `RATE_SNAPSHOT_MISSING`（呼叫 FI 前），預留與狀態不變。 | I（真實 SQL） | BR-PUR-043b | PASS |
| PUR-POST-004 | FI 拒絕 | 未設定 FI 採購發票科目映射 → 422 保留 FI `MAPPING_MISSING`，全部回滾；設定映射後同一版本可成功過帳（沒有殘留鎖或半套資料）。 | I（真實 SQL） | BR-PUR-043d | PASS |
| PUR-POST-005 | 證據失效 | 已比對但價差沒有 RESOLVE 接受列 → PUR 422（`PUR.INVOICE.*`），FI 不寫入、發票仍 Matched。 | I（真實 SQL） | BR-PUR-043c | PASS |
| PUR-POST-006 | 約束 | 只把 status 改 Posted、或非 Posted 時只填 FI ID → CHECK `ck_pur_invoice_posted` 拒絕；五欄齊全的 Posted 可保存。 | I（真實 SQL） | REQ_DS43.184 | PASS |
| PUR-POST-007 | HTTP | 未登入 401；只有 MATCH 的用戶 403；版本 0 → 400；不存在 404；舊版本 409；Draft → 422 `errorCode=PUR.INVOICE.INVALID_POST_STATUS`；發票仍 Draft 版本 1。 | I（HTTP） | REQ_F4114.8.5 | PASS |
| PUR-POST-101 | 收據比對 | 公司、發票、供應商、版本、指紋（區分大小寫）、原幣／本位幣與含稅金額、FI ID 任一不符即不接受。 | U | BR-FI-019 | PASS |
| PUR-POST-102 | FI 結果映射 | Rejected 404／409／422 保留 FI 碼與狀態，422 帶 `journalLineNo` 或 `lineNo`；Fault、Unchanged、Rejected 400、缺碼 → 500 `PurchaseInvoicePostingException`。 | U | ADR-208 | PASS |
| PUR-POST-103／104 | 共用價格基準 | 每個收貨行恰有一個正 ID 價格，缺、重複、多出、非正 ID → `INVALID_SOURCE_IDENTITY`；保存行與訂單價配對成比對行。 | U | BR-PUR-038 | PASS |
| PUR-POST-NA | 延後 | 兩個請求同時過帳同一發票（供應商鎖序列化、第二個重放）及真實 API Playwright 過帳：需 FI 映射與 SQL 安排的匯率快照，列入 008b5／008c3 前補驗。前端過帳按鈕屬 008c3。 | — | — | N/A |

執行記錄：2026-11-27T04:53:21+08:00；單元 PUR-POST-101～104 4/4、真實 SQL PUR-POST-001～006 6/6、HTTP PUR-POST-007 1/1；前端相關 spec 4 檔 49/49（三語錯誤文字）；後端全量 4051/4051、Debug 0 警告。中途兩個全量測試同時執行互相刪建測試資料庫造成 306 個假失敗（登入失敗、表不存在），單獨重跑全綠；`PurchasingModuleTests` 權限清單補上新權限 `PUR.INVOICE.POST`（新增期望，未削弱）。

## WU-PUR-008b5 — 退貨對已開票量限制（ADR-209）

情境依 myerp-test-scenarios 清單：正常、邊界（剛好等於可退量、六位小數）、聚合重複行、多行部分不足（整單拒絕、不回部分成功）、狀態（Matched 未過帳亦佔量、本單預留核准重驗）、錯誤輸入、快照不一致、資料庫約束。權限／HTTP／並發不適用：007c 退貨寫入服務尚未存在，規則為純函式，由 007c 在收貨行鎖內呼叫並驗收權限與並發。

| ID | 類別 | 情境 | 層級 | 需求 | 狀態 |
|---|---|---|---|---|---|
| PUR-RETLIM-001 | 正常／邊界 | 可退量 = 收貨 − 已退 −（待審退貨 − 本單預留）− 已開票 − Matched 預留；全數已開票或全數 Matched → 0（5 組）。 | U | BR-PUR-044 | PASS |
| PUR-RETLIM-002 | 正常 | 多收貨行全部在未開票量內 → 可退，無不足行。 | U | BR-PUR-044 | PASS |
| PUR-RETLIM-003 | 聚合／部分不足 | 同一收貨行重複列先聚合（超 0.000001 即不足）；兩行都不足時按收貨行 ID 升冪全列出；任一行不足整單拒絕。 | U | BR-PUR-044、BR-PUR-034 | PASS |
| PUR-RETLIM-004 | 錯誤提示 | `EnsureWithinLimit` 不足 → 422 `PUR.RETURN.INVOICED_QUANTITY_EXCEEDED`，訊息含收貨行、可退量、已開票或預留量與「credit note」；足夠則不拋。 | U | BR-PUR-041、BR-PUR-044 | PASS |
| PUR-RETLIM-005 | 狀態 | 最終核准重驗排除本單預留（不重複扣）；快照違反「消耗合計 ≤ 收貨」→ 422 `INVALID_SOURCE_QUANTITY`，不回負餘量。 | U | BR-PUR-044 | PASS |
| PUR-RETLIM-006 | 錯誤輸入 | 退貨量 0、負數、超過六位小數 → 422 `INVALID_REQUEST_QUANTITY`（3 組）。 | U | BR-PUR-044 | PASS |
| PUR-RETLIM-007 | 錯誤輸入 | 空清單、收貨行 ID 0、null 行 → 422；清單 null → ArgumentNullException。 | U | BR-PUR-044 | PASS |
| PUR-RETLIM-008 | 來源不一致 | 缺來源、多來源、重複來源、null 數量、收貨量 0、負數、本單預留大於待審、消耗超收貨、超六位小數 → 422 `INVALID_SOURCE_QUANTITY`。 | U | BR-PUR-044 | PASS |
| PUR-RETLIM-I01 | 整合 | 真實 SQL：收貨 8、Matched 發票 5 經正式 008b4 POST 過帳（PUR→FI）→ 已開票 5、可退 3；退 4 → 422（訊息 returnable 3／invoiced or matched 5）、退 3 通過；CHECK 拒絕待審預留 4 與已過帳退貨 3.000001，列不變；預留 3 後他單再退 0.000001 被拒、本單核准重驗通過；預留轉已退 3 後已開票 5 保持、發票仍 Posted。 | I | REQ_F4114.7.3、REQ_F4114.8.5、BR-PUR-044 | PASS |
| PUR-RETLIM-I02 | 整合 | 真實 SQL：收貨 5 全數被 Matched（未過帳）發票預留 → 規則回不足（可退 0、已開票或預留 5），CHECK 拒絕預留 1，列不變。 | I | BR-PUR-041、BR-PUR-044 | PASS |
| PUR-RETLIM-NA | 延後 | 退貨 SUBMIT／核准 API 呼叫本規則、權限、收貨行鎖下兩張退貨／退貨與 MATCH 並發：隨 WU-PUR-007c 驗收。 | — | BR-PUR-032、BR-PUR-044 | 延後 |

執行記錄：2026-11-27T05:08:41+08:00；單元 PUR-RETLIM-001～008 14/14（首跑 005 紅燈：不一致快照未被拒絕，修規則後綠）；真實 SQL PUR-RETLIM-I01～I02 2/2（首跑 I01 紅燈：EF decimal(18,2) 參數捨入 3.000001，改夾具後綠）；後端全量 4067/4067（--max-threads 2）、Debug 0 警告。

## WU-PUR-008b6 — 過帳併發驗收＋4.19 過帳按鈕

| ID | 類型 | 情境 | 層 | 追溯 | 結果 |
|---|---|---|---|---|---|
| PUR-POST-105 | 併發 | 測試連線持有供應商發票鎖時，兩個同版本 POST 都等待（800ms 內未完成、FI 0 列）；釋放後一個 Posted、另一個 replayed 且 FI 參照相同；FI 來源／應付／傳票各 1、分錄 3；預留 5→0、已開票 0→5。 | I（真實 SQL） | BR-PUR-043、REQ_F4114.8.5 | PASS |
| PUR-POST-106 | 併發／版本 | 同時送版本 2（目前）與版本 1（過期）：不論誰先取鎖，版本 2 Posted、版本 1 409；FI 各 1。 | I | BR-PUR-043 | PASS |
| PUR-POST-107 | 併發 | 三個不加鎖同時 POST：恰一個 Posted，其餘只可為 replayed（同參照）或 409；FI 各 1。 | I | BR-PUR-043 | PASS |
| PUR-POST-108 | 讀取 | 明細 GET：Matched 時過帳時間與 FI 三參照為 null，過帳後與 POST 回應一致。 | I | REQ_IO_A4.19.9 | PASS |
| PUR-INV-UI-020／020b | 權限／狀態 | 只有 Matched＋PUR.INVOICE.POST 顯示過帳；Draft、Posted、無權限不顯示；未過帳無財務參照。 | U | REQ_IO_A4.19.8 | PASS |
| PUR-INV-UI-021 | 正常 | 取消確認不送；確認帶發票號／金額／幣別；處理中停用＋aria-busy；送版本、空 body；成功訊息帶應付／傳票 ID；列狀態更新；明細 Posted 顯示參照與過帳時間、不再有按鈕。 | U | REQ_IO_A4.19.8～9 | PASS |
| PUR-INV-UI-022 | 重放 | replayed=true 顯示「先前已過帳、未重複寫入」。 | U | REQ_IO_A4.19.8 | PASS |
| PUR-INV-UI-023 | 422／409 | FI MAPPING_MISSING 顯示 FI 原因、不重讀、按鈕可再按；鎖逾時不重讀；版本 409 重讀為 Posted。 | U | REQ_IO_A4.19.8 | PASS |
| PUR-INV-UI-024 | 422／500 | RATE_SNAPSHOT_MISSING 不重讀；FI_POSTING_FAILED 500 顯示原因＋追蹤碼；INVALID_POST_STATUS 重讀（變 Draft 後顯示比對）。 | U | REQ_IO_A4.19.8 | PASS |
| PUR-INV-UI-025 | 三語 | 20 個過帳錯誤碼與 8 個新文字鍵三語齊全，且列入已知錯誤碼。 | U | REQ_IO_A4.19.8～9 | PASS |
| PUR-INV-UI-E04 | 正常／422／三語亮暗 | 模擬 API：Matched 亮暗、確認框亮暗（取消不送）、FI 422 仍已比對、成功 Posted 顯示參照，en／zh-Hans 截圖；送出恰兩次 POST 版本 4、body null。 | E | REQ_IO_A4.19.8～9 | PASS |

執行記錄：2026-11-27T05:30:01+08:00；真實 SQL PUR_POST_* 10/10（含 105～108）；後端全量 4070/4071（INV LotSerialTrackingTests 一次 SQL 死鎖犧牲，與本變更無關，單獨重跑 29/29）；前端 1305/1305、production 建置；Playwright purchasing-invoice 4/4。真實 API Playwright 過帳仍待 WU-PUR-008b6c。
