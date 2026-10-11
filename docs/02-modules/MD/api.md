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

## 客戶（WU-MD-003）

| Method | Path | Permission | Request / Response | Related requirement |
|---|---|---|---|---|
| GET | `/api/v1/md/customers` | `MD.CUSTOMER.VIEW` | `page`, `pageSize`, `keyword`, `sort=code\|-code\|name\|-name` → `PagedResult<CustomerResponse>` | REQ_F4118.1.1 |
| POST | `/api/v1/md/customers` | `MD.CUSTOMER.CREATE` | `{code,name,categoryId?}` → 201 `CustomerResponse` | REQ_F4118.1.2 |
| PUT | `/api/v1/md/customers/{id}` | `MD.CUSTOMER.EDIT` | `{name,isActive,categoryId?,version}` → `CustomerResponse` | REQ_F4118.1.3 |

`CustomerResponse` 為 `id,code,name,categoryId,isActive,version`。服務只允許本公司啟用分類，未指定時 `categoryId=null`；401／403／404／409 與欄位格式 400 依共用 ProblemDetails。分類刪除若有本公司客戶引用回 `MD.CUSTOMER_CATEGORY.IN_USE` 409。

## 供應商分類（WU-MD-006）

| Method | Path | Permission | Request / Response | Related requirement |
|---|---|---|---|---|
| GET | `/api/v1/md/supplier-categories` | `MD.SUPPLIER_CATEGORY.VIEW` | `page`, `pageSize`, `keyword`, `sort=code\|-code\|name\|-name` → `PagedResult<SupplierCategoryResponse>` | REQ_F4118.9.1 |
| POST | `/api/v1/md/supplier-categories` | `MD.SUPPLIER_CATEGORY.CREATE` | `{code,name}` → 201 `SupplierCategoryResponse` | REQ_F4118.9.2 |
| PUT | `/api/v1/md/supplier-categories/{id}` | `MD.SUPPLIER_CATEGORY.EDIT` | `{name,isActive,version}` → `SupplierCategoryResponse` | REQ_F4118.9.3 |
| DELETE | `/api/v1/md/supplier-categories/{id}` | `MD.SUPPLIER_CATEGORY.DELETE` | `version` query → 204 | REQ_F4118.9.4 |

`SupplierCategoryResponse` 僅含 `id,code,name,isActive,version`。錯誤碼：`MD.SUPPLIER_CATEGORY.DUPLICATE_CODE`（409）、`MD.SUPPLIER_CATEGORY.NOT_FOUND`（404）、`MD.SUPPLIER_CATEGORY.IN_USE`（409，WU-MD-007 起生效）、`COMMON.CONCURRENCY.CONFLICT`（409）；其他與客戶分類相同。服務與客戶分類共用 `MasterCategoryService<T>`，差異只在錯誤碼前綴與刪除前的引用檢查。

## 供應商（WU-MD-007）

| Method | Path | Permission | Request / Response | Related requirement |
|---|---|---|---|---|
| GET | `/api/v1/md/suppliers` | `MD.SUPPLIER.VIEW` | `page`, `pageSize`, `keyword`, `sort=code\|-code\|name\|-name` → `PagedResult<SupplierResponse>` | REQ_F4118.2.1 |
| POST | `/api/v1/md/suppliers` | `MD.SUPPLIER.CREATE` | `{code,name,categoryId?}` → 201 `SupplierResponse` | REQ_F4118.2.2 |
| PUT | `/api/v1/md/suppliers/{id}` | `MD.SUPPLIER.EDIT` | `{name,isActive,categoryId?,version}` → `SupplierResponse` | REQ_F4118.2.3 |

`SupplierResponse` 為 `id,code,name,categoryId,isActive,version`。錯誤碼：`MD.SUPPLIER.DUPLICATE_CODE`（409）、`MD.SUPPLIER.NOT_FOUND`（404）、`COMMON.CONCURRENCY.CONFLICT`（409）、分類不屬本公司或已停用（400 `categoryId`）。自本單元起，刪除已被供應商引用的分類回 `MD.SUPPLIER_CATEGORY.IN_USE` 409。服務與客戶共用 `BusinessPartnerService<TPartner, TCategory>`，差異只在錯誤碼前綴與分類型別。

## 計量單位與換算（WU-MD-010）

| Method | Path | Permission | Request / Response | Related requirement |
|---|---|---|---|---|
| GET | `/api/v1/md/uoms` | `MD.UOM.VIEW` | `page`, `pageSize`, `keyword`, `sort=code\|-code\|name\|-name` → `PagedResult<UomResponse>` | REQ_F4118.13.1 |
| POST | `/api/v1/md/uoms` | `MD.UOM.CREATE` | `{code,name,decimalPlaces}` → 201 `UomResponse` | REQ_F4118.13.2 |
| PUT | `/api/v1/md/uoms/{id}` | `MD.UOM.EDIT` | `{name,isActive,decimalPlaces,version}` → `UomResponse` | REQ_F4118.13.3 |
| DELETE | `/api/v1/md/uoms/{id}` | `MD.UOM.DELETE` | `version` query → 204 | REQ_F4118.13.4 |
| GET | `/api/v1/md/uom-conversions` | `MD.UOM.VIEW` | → `UomConversionView[]`（依來源、目標代碼排序） | REQ_F4118.13.1 |
| GET | `/api/v1/md/uom-conversions/convert` | `MD.UOM.VIEW` | `fromUomId`, `toUomId`, `quantity` → `{fromUomId,toUomId,quantity,result}` | REQ_F4118.13.1 |
| POST | `/api/v1/md/uom-conversions` | `MD.UOM.CREATE` | `{fromUomId,toUomId,factor}` → 201 `UomConversionView` | REQ_F4118.13.2 |
| PUT | `/api/v1/md/uom-conversions/{id}` | `MD.UOM.EDIT` | `{factor,version}` → `UomConversionView` | REQ_F4118.13.3 |
| DELETE | `/api/v1/md/uom-conversions/{id}` | `MD.UOM.DELETE` | `version` query → 204 | REQ_F4118.13.4 |

`UomResponse` 為 `id,code,name,decimalPlaces,isActive,version`；`UomConversionView` 為 `id,fromUomId,fromUomCode,toUomId,toUomCode,factor,version`（1 來源 = factor 目標）。錯誤碼：`MD.UOM.DUPLICATE_CODE`（409）、`MD.UOM.NOT_FOUND`（404）、`MD.UOM.IN_USE`（409）、`MD.UOM_CONVERSION.PATH_EXISTS`（409）、`MD.UOM_CONVERSION.NOT_FOUND`（404）、`MD.UOM_CONVERSION.NO_PATH`（422）、`COMMON.CONCURRENCY.CONFLICT`（409）；欄位驗證 400（`code`、`name`、`decimalPlaces`、`fromUomId`、`toUomId`、`factor`、`quantity`）。單位服務繼承 `MasterCategoryService<UnitOfMeasure>`。跨模組契約：`IUomConverter.ConvertAsync(fromUomId, toUomId, quantity)`（ADR-034）。
