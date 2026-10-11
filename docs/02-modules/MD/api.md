# MD API

WU-MD-001 不開放資料 API。後續 API 使用 `/api/v1/md/{resource}`，每個 action 以 `MD.{RESOURCE}.{ACTION}` 權限保護，透過共用中介層驗證 `X-Company-Id`，在服務查詢中再使用公司篩選。未登入回 401、無公司存取權或權限回 403，資料不存在或非本公司資料回 404；錯誤使用共用 ProblemDetails。

| 權限 | 用途 |
|---|---|
| `MD.MASTER.VIEW` | `/md` 前端主檔入口 |

客戶分類等 CRUD API 由 WU-MD-002 起逐項定義，不預先暴露空白端點。
