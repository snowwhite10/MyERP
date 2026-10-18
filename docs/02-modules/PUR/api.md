# PUR API

## WU-PUR-000 請購契約（待 WU-PUR-001～003 實作）

基址 `/api/v1/pur/requisitions`，Swagger 標籤 `PUR - Requisitions`。所有錯誤用含 `errorCode`、`traceId` 的 ProblemDetails；不存在或他公司 ID 回 404。數量使用 decimal 字串，不使用浮點數。

WU-PUR-001 註冊權限目錄和 Application 公司守衛；WU-PUR-002a 已建立兩張資料表。002b1 實作 GET 列表／詳情；002b2a 將 GET 限定於 SYS 授權資料範圍，範圍外詳情回 404，缺 VIEW 回 403，DTO 加上分店／部門歸屬。其餘 HTTP 端點待 002b2 後續切片／003。`PUR.REQUISITION.EXPORT` 預留給 004 的完整 CSV，如未實作匯出不得顯示可用按鈕。端點服務先檢查正數公司，查詢必須套用公司篩選，單筆讀取再檢查表頭公司。列表支援 `keyword`（單號）、`from`、`to`、`status`、`requesterId`、`page`、`pageSize`、`sort`（`documentNo`／`businessDate`；負號降冪）；詳情含已保存行及版本，審批歷史待 003。

| Method | Path | 用途 | 權限 | 主要錯誤 |
|---|---|---|---|---|
| GET | `/` | 單號／日期／狀態／請購人篩選、白名單排序、穩定分頁 | VIEW | 400, 403 |
| GET | `/{id}` | 表頭、行、版本、審批歷史及已轉採購量 | VIEW | 404 |
| POST | `/` | `{businessDate,requesterId,remark,lines[]}` 建立草稿；回 201 | CREATE | 400, 404, 422 |
| PUT | `/{id}` | `{version,businessDate,remark,lines[]}` 整組替換可修改單 | EDIT | 400, 409, 422 |
| DELETE | `/{id}?version=...` | 軟刪草稿；回 204 | DELETE | 404, 409, 422 |
| POST | `/{id}/submit` | `{version}` 送 SYS 審批；回目前狀態 | SUBMIT | 409, 422 |

`lines[]` 每行含 `lineNo,itemId,requiredDate,quantity,remark`，行號唯一；1～1000 行。核准與駁回只走 SYS 審批 API，沒有 PUR 直接 approve 端點。MD 物料與 SYS 審批只能透過各自 Application／Contracts 查詢，不跨模組讀表。前端選項須按 CREATE／EDIT 權限受權，詳情只讀已保存資料。
