# MD API

WU-MD-001 不開放資料 API。後續 API 使用 `/api/v1/md/{resource}`，每個 action 以 `MD.{RESOURCE}.{ACTION}` 權限保護，透過共用中介層驗證 `X-Company-Id`，在服務查詢中再使用公司篩選。未登入回 401、無公司存取權或權限回 403，資料不存在或非本公司資料回 404；錯誤使用共用 ProblemDetails。

| 權限 | 用途 |
|---|---|
| `MD.MASTER.VIEW` | `/md` 前端主檔入口 |

客戶分類等 CRUD API 由 WU-MD-002 起逐項定義，不預先暴露空白端點。

## 客戶分類（WU-MD-002）

| Method | Path | Permission | Request / Response | Related requirement |
|---|---|---|---|---|
| GET | `/api/v1/md/customer-categories` | `MD.CUSTOMER_CATEGORY.VIEW` | `page`, `pageSize`, `keyword`, `sort=code\|-code\|name\|-name` → `PagedResult<CustomerCategoryResponse>` | REQ_F4118.8.1 |
| POST | `/api/v1/md/customer-categories` | `MD.CUSTOMER_CATEGORY.CREATE` | `{code,name}` → 201 `CustomerCategoryResponse` | REQ_F4118.8.2 |
| PUT | `/api/v1/md/customer-categories/{id}` | `MD.CUSTOMER_CATEGORY.EDIT` | `{name,isActive,version}` → `CustomerCategoryResponse` | REQ_F4118.8.3 |
| DELETE | `/api/v1/md/customer-categories/{id}` | `MD.CUSTOMER_CATEGORY.DELETE` | `version` query → 204 | REQ_F4118.8.4 |

`CustomerCategoryResponse` 僅含 `id,code,name,isActive,version`；錯誤用共用 ProblemDetails：格式 400、公司／權限 403、查無 404、重複代碼或版本衝突 409。WU-MD-003 建立客戶與分類的關聯後，刪除時已被客戶引用也須回 409；分類實體不對外暴露審計欄位。
