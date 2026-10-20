# PUR API

## WU-PUR-000 請購契約（待 WU-PUR-001～003 實作）

基址 `/api/v1/pur/requisitions`，Swagger 標籤 `PUR - Requisitions`。所有錯誤用含 `errorCode`、`traceId` 的 ProblemDetails；不存在或他公司 ID 回 404。數量使用 decimal 定點型別的 JSON 數值，不使用浮點型別。

WU-PUR-001 註冊權限目錄和 Application 公司守衛；WU-PUR-002a 已建立兩張資料表。002b1 實作 GET 列表／詳情；002b2a 將 GET 限定於 SYS 授權資料範圍，範圍外詳情回 404，缺 VIEW 回 403，DTO 加上分店／部門歸屬。POST 建立草稿已由 002b2b 實作；PUT／DELETE 待 002b2c，送審待 003。`PUR.REQUISITION.EXPORT` 預留給 004 的完整 CSV，如未實作匯出不得顯示可用按鈕。端點服務先檢查正數公司，查詢必須套用公司篩選，單筆讀取再檢查表頭公司。列表支援 `keyword`（單號）、`from`、`to`、`status`、`requesterId`、`page`、`pageSize`、`sort`（`documentNo`／`businessDate`；負號降冪）；詳情含已保存行及版本，審批歷史待 003。

| Method | Path | 用途 | 權限 | 主要錯誤 |
|---|---|---|---|---|
| GET | `/` | 單號／日期／狀態／請購人篩選、白名單排序、穩定分頁 | VIEW | 400, 403 |
| GET | `/{id}` | 表頭、行、版本、審批歷史及已轉採購量 | VIEW | 404 |
| POST | `/` | `{businessDate,remark,lines[]}` 建立草稿；回 201、Location 及保存詳情；請購人／分店／部門由 SYS 授權取得 | CREATE | 400, 403, 404, 422 |
| PUT | `/{id}?version=...` | `{businessDate,remark,lines[]}` 整組替換草稿或駁回單；駁回修訂轉回草稿 | EDIT | 400, 403, 404, 409, 422 |
| DELETE | `/{id}?version=...` | 軟刪草稿；回 204 | DELETE | 404, 409, 422 |
| POST | `/{id}/submit` | `{version}` 送 SYS 審批；回保存後詳情與 SYS 審批狀態；無適用步驟可立即核准 | SUBMIT | 403, 404, 409, 422 |

WU-PUR-002b2c 已提供 PUT／DELETE：兩端點都先按動作權限和 SYS 資料範圍定位單據，再檢查版本；PUT 在交易內驗 MD 引用並整組替換行，DELETE 只標記草稿軟刪。送審端點仍待 WU-PUR-003。

`lines[]` 每行含 `lineNo,itemId,requiredDate,quantity,remark`，行號唯一；1～1000 行。核准與駁回只走 SYS 審批 API，沒有 PUR 直接 approve 端點。MD 物料與 SYS 審批只能透過各自 Application／Contracts 查詢，不跨模組讀表。前端選項須按 CREATE／EDIT 權限受權，詳情只讀已保存資料。

WU-PUR-003a 送審回應為 `{requisition,approval}`；重複送審、非草稿或舊版本拒絕，並維持原狀。歷次審批記錄與 SYS 最終決定由 003b 加入詳情；003a 不提供直接核准／駁回端點。
