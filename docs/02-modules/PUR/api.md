# PUR API

## WU-PUR-005a 詢價／報價／比價預定契約（端點尚未實作）

基址 `/api/v1/pur/rfqs`，Swagger 分組 `PUR - RFQs`。所有端點先驗目前公司與 SYS 動作權限／資料範圍；範圍外及他公司資源回 404，輸入 400、過期版本 409、商業狀態／停用引用 422，ProblemDetails 帶 `errorCode` 和 `traceId`。列表由 SQL 先套公司及資料範圍，再白名單排序和分頁；前端不得用其他模組資料表。以下契約於 005c～e 落地，屆時補 Swagger DTO 和具體錯誤碼。

| Method | Path | 用途 | 權限 |
|---|---|---|---|
| GET | `/` | 單號、日期、狀態、請購來源篩選及穩定分頁 | `PUR.RFQ.VIEW` |
| GET | `/{id}` | 保存行、邀請名單、狀態及版本 | `PUR.RFQ.VIEW` |
| POST | `/` | 建立詢價草稿、SYS 單號及 MD 物料／供應商驗證 | `PUR.RFQ.CREATE` |
| PUT | `/{id}?version=` | 整組替換草稿行及邀請名單 | `PUR.RFQ.EDIT` |
| DELETE | `/{id}?version=` | 軟刪未發送草稿 | `PUR.RFQ.DELETE` |
| POST | `/{id}/send` | 驗證並保存發送快照 | `PUR.RFQ.SEND` |
| POST | `/{id}/close` | 關閉已發送詢價 | `PUR.RFQ.CLOSE` |
| GET | `/{id}/quotations` | 受邀報價、版本與保存行 | `PUR.QUOTATION.VIEW` |
| POST | `/{id}/quotations` | 為受邀供應商記錄第一版報價 | `PUR.QUOTATION.QUOTE` |
| POST | `/{id}/quotations/{quotationId}/revisions` | 以目前版本建立不可變修訂版 | `PUR.QUOTATION.QUOTE` |
| POST | `/{id}/quotations/{quotationId}/withdraw` | 撤回未決標報價 | `PUR.QUOTATION.QUOTE` |
| GET | `/{id}/comparison` | 逐行原幣及本位幣比較，標示缺匯率 | `PUR.RFQ.COMPARE` |
| POST | `/{id}/awards` | 逐行決標並保存理由與價格快照 | `PUR.RFQ.AWARD` |
| POST | `/{id}/orders` | 將同一供應商已決標且未轉行建立採購單草稿 | `PUR.RFQ.CONVERT` |

建立／修訂 body 只接受業務欄位與行號，不接受公司、單號、擁有人或審計欄位；歸屬從 SYS 取得。送審、決標、轉單均帶版本；重試相同轉單請求須回已建立單的可辨識結果或明確衝突，不能重複建立。MD 物料、供應商、幣別、稅率及匯率經公開 Contracts 驗證。詢價發送目前不發外部 email；介面只回報系統內的發送狀態。

## WU-PUR-000 請購契約（待 WU-PUR-001～003 實作）

基址 `/api/v1/pur/requisitions`，Swagger 標籤 `PUR - Requisitions`。所有錯誤用含 `errorCode`、`traceId` 的 ProblemDetails；不存在或他公司 ID 回 404。數量使用 decimal 定點型別的 JSON 數值，不使用浮點型別。

WU-PUR-001 註冊權限目錄和 Application 公司守衛；WU-PUR-002a 已建立兩張資料表。002b1 實作 GET 列表／詳情；002b2a 將 GET 限定於 SYS 授權資料範圍，範圍外詳情回 404，缺 VIEW 回 403，DTO 加上分店／部門歸屬。002b2b／002b2c 完成 POST、PUT、DELETE，003a～c 完成送審及審批歷史。`PUR.REQUISITION.EXPORT` 控制介面 4.8 CSV 按鈕；CSV 使用既有受 VIEW 授權的 GET 列表與詳情組合，沒有新增伺服器匯出端點。端點服務先檢查正數公司，查詢必須套用公司篩選，單筆讀取再檢查表頭公司。列表支援 `keyword`（單號）、`from`、`to`、`status`、`requesterId`、`page`、`pageSize`、`sort`（`documentNo`／`businessDate`；負號降冪）；詳情含已保存行、版本和歷次審批。

| Method | Path | 用途 | 權限 | 主要錯誤 |
|---|---|---|---|---|
| GET | `/` | 單號／日期／狀態／請購人篩選、白名單排序、穩定分頁 | VIEW | 400, 403 |
| GET | `/{id}` | 表頭、行、版本、審批歷史及已轉採購量 | VIEW | 404 |
| GET | `/item-options` | 搜尋目前公司啟用的 MD 物料，按代碼穩定分頁 | CREATE 或 EDIT | 400, 403 |
| GET | `/{id}/edit` | 修訂頁專用，按 EDIT 資料範圍讀已保存表頭、行與版本 | EDIT | 403, 404 |
| GET | `/{id}/saved-item-options` | 先檢 EDIT 資料範圍，再返回該單已保存物料名稱與啟用狀態 | EDIT | 403, 404 |
| POST | `/` | `{businessDate,remark,lines[]}` 建立草稿；回 201、Location 及保存詳情；請購人／分店／部門由 SYS 授權取得 | CREATE | 400, 403, 404, 422 |
| PUT | `/{id}?version=...` | `{businessDate,remark,lines[]}` 整組替換草稿或駁回單；駁回修訂轉回草稿 | EDIT | 400, 403, 404, 409, 422 |
| DELETE | `/{id}?version=...` | 軟刪草稿；回 204 | DELETE | 404, 409, 422 |
| POST | `/{id}/submit` | `{version}` 送 SYS 審批；回保存後詳情與 SYS 審批狀態；無適用步驟可立即核准 | SUBMIT | 403, 404, 409, 422 |

WU-PUR-002b2c 已提供 PUT／DELETE：兩端點都先按動作權限和 SYS 資料範圍定位單據，再檢查版本；PUT 在交易內驗 MD 引用並整組替換行，DELETE 只標記草稿軟刪。

`lines[]` 每行含 `lineNo,itemId,requiredDate,quantity,remark`，行號唯一；1～1000 行。核准與駁回只走 SYS 審批 API，沒有 PUR 直接 approve 端點。MD 物料與 SYS 審批只能透過各自 Application／Contracts 查詢，不跨模組讀表。前端選項須按 CREATE／EDIT 權限受權，詳情只讀已保存資料。

WU-PUR-003a 送審回應為 `{requisition,approval}`；重複送審、非草稿或舊版本拒絕，並維持原狀。歷次審批記錄與 SYS 最終決定由 003b 加入詳情；003a 不提供直接核准／駁回端點。

WU-PUR-003b：`GET /{id}` 增加 `approvalHistory[]`，按最新輪次在前，每輪含 `instanceId,status,submittedAt,steps[]`；逐步提供 `stepNo,name,status,actedBy,actedAt,comment`。必須先通過 PUR VIEW 公司及資料範圍守衛；最終決定使用既有 `POST /api/v1/sys/approvals/{id}/approve|reject`，PUR 不另開決定端點。

WU-PUR-004a：前端 `RequisitionService` 對齊上述六個端點；數量由表單十進位字串送入，真實 HTTP 已驗 ASP.NET Core 可解析並以 decimal 原值保存，避免在瀏覽器轉成二進位浮點。列表只送有效篩選值。建立／修改按畫面行序重新編號，不傳請購人／分店／部門。後續 004b～d 接入畫面、錯誤翻譯及完整 CSV。

WU-PUR-004c：新增兩個唯讀選項端點，均委派 MD `IInventorySelectionOptions`；已保存選項先經請購 EDIT 公司及資料範圍定位單據，再只把該單行上的物料 ID 交給 MD，含停用物料供顯示。另有編輯專用 `GET /{id}/edit`，使只有 EDIT、沒有 VIEW 權限的用戶也可載入其 EDIT 範圍內的單據；一般詳情仍使用 VIEW。新增選項僅啟用物料，後端 POST／PUT 仍重新驗證引用。匿名 401、缺權 403、資料範圍外 404；沒有跨模組資料表存取。

WU-PUR-004d：介面以目前已套用篩選重取 `GET /` 所有分頁（每頁 200），核對原總筆數與唯一 ID，再逐張讀 `GET /{id}` 的保存行。任一端點 403／404／5xx 或匯出期間資料變動，取消整個 CSV；最多 10000 明細行，BOM UTF-8 與公式字元跳脫由前端處理。EXPORT 是前端顯示權限；持 VIEW 者本來可透過唯讀 API 讀取相同資料，後端每次仍強制 VIEW 公司與資料範圍。
