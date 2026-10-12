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

### 客戶匯入（WU-MD-005b；ADR-044）

| Method | Path | Permission | Request / Response | Related requirement |
|---|---|---|---|---|
| GET | `/api/v1/md/customers/import/template?format=xlsx\|csv` | `MD.CUSTOMER.IMPORT` | xlsx／UTF-8 BOM CSV 空白表頭範本 | REQ_F4118.1.7 |
| POST | `/api/v1/md/customers/import/preview` | `MD.CUSTOMER.IMPORT` | `multipart/form-data` 的 `file` → `{rows,createdCount,skippedCount,errorCount}`，唯讀 | REQ_F4118.1.7 |
| POST | `/api/v1/md/customers/import` | `MD.CUSTOMER.IMPORT` | 同一檔案重新上傳，整批提交；逐列 `Create`／`Skip`／`Error` | REQ_F4118.1.7 |

`rows` 含實際檔案列號、代碼、名稱、狀態及 `{field,code}` 錯誤；有錯誤列時整批回 422 `MD.CUSTOMER_IMPORT.HAS_ERRORS`，客戶端重新預覽可取得逐列錯誤。檔案格式／大小／表頭錯誤回 422 `COMMON.IMPORT.*`；同時由其他請求新增相同代碼回 409 `MD.CUSTOMER.DUPLICATE_CODE`。已存在本公司代碼含停用／軟刪除時略過，不更新；`categoryCode` 只對應本公司啟用分類。提交在公司級交易鎖內重新驗證。

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

## 物料品牌與分類（WU-MD-011）

| Method | Path | Permission | Request / Response | Related requirement |
|---|---|---|---|---|
| GET | `/api/v1/md/item-brands` | `MD.ITEM_BRAND.VIEW` | `page`, `pageSize`, `keyword`, `sort=code\|-code\|name\|-name` → `PagedResult<ItemBrandResponse>` | REQ_F4118.11.1 |
| POST | `/api/v1/md/item-brands` | `MD.ITEM_BRAND.CREATE` | `{code,name}` → 201 `ItemBrandResponse` | REQ_F4118.11.2 |
| PUT | `/api/v1/md/item-brands/{id}` | `MD.ITEM_BRAND.EDIT` | `{name,isActive,version}` → `ItemBrandResponse` | REQ_F4118.11.3 |
| DELETE | `/api/v1/md/item-brands/{id}` | `MD.ITEM_BRAND.DELETE` | `version` query → 204 | REQ_F4118.11.4 |
| GET | `/api/v1/md/item-categories` | `MD.ITEM_CATEGORY.VIEW` | → `ItemCategoryResponse[]`（全部未刪除分類，按代碼排序，不分頁） | REQ_F4118.10.1 |
| POST | `/api/v1/md/item-categories` | `MD.ITEM_CATEGORY.CREATE` | `{code,name,parentId?}` → 201 `ItemCategoryResponse` | REQ_F4118.10.2 |
| PUT | `/api/v1/md/item-categories/{id}` | `MD.ITEM_CATEGORY.EDIT` | `{name,isActive,parentId?,version}` → `ItemCategoryResponse` | REQ_F4118.10.3 |
| DELETE | `/api/v1/md/item-categories/{id}` | `MD.ITEM_CATEGORY.DELETE` | `version` query → 204 | REQ_F4118.10.4 |

`ItemBrandResponse` 為 `id,code,name,isActive,version`；`ItemCategoryResponse` 為 `id,code,name,parentId,isActive,version`（`parentId` null 為根）。錯誤碼：`MD.ITEM_BRAND.DUPLICATE_CODE`／`MD.ITEM_CATEGORY.DUPLICATE_CODE`（409）、`MD.ITEM_BRAND.NOT_FOUND`／`MD.ITEM_CATEGORY.NOT_FOUND`（404）、`MD.ITEM_CATEGORY.INVALID_PARENT`／`CYCLE`／`TOO_DEEP`（422）、`MD.ITEM_CATEGORY.HAS_CHILDREN`（409）、`COMMON.CONCURRENCY.CONFLICT`（409）；欄位驗證 400（`code`、`name`）。兩個服務皆繼承 `MasterCategoryService<T>`；分類的新增、修改、刪除包在 `CompanyLock.RunAsync`（公司級 applock，與單位換算共用）。

## 物料主檔後端（WU-MD-012a）

| Method | Path | Permission | Request / Response | Related requirement |
|---|---|---|---|---|
| GET | `/api/v1/md/items` | `MD.ITEM.VIEW` | `page`, `pageSize`, `keyword`, `sort=code\|-code\|name\|-name` → `PagedResult<ItemResponse>` | REQ_F4118.3.1 |
| POST | `/api/v1/md/items` | `MD.ITEM.CREATE` | `{code,name,specification?,baseUomId,brandId?,categoryId?}` → 201 `ItemResponse` | REQ_F4118.3.2 |
| PUT | `/api/v1/md/items/{id}` | `MD.ITEM.EDIT` | `{name,specification?,baseUomId,brandId?,categoryId?,isActive,version}` → `ItemResponse` | REQ_F4118.3.3 |

`ItemResponse` 加 `id,code,name,specification,baseUomId,brandId,categoryId,isActive,version`。錯誤碼 `MD.ITEM.DUPLICATE_CODE`（409）、`MD.ITEM.NOT_FOUND`（404）、`MD.ITEM.BASE_UOM_LOCKED`（409，有專屬換算時修改基本單位）、`COMMON.CONCURRENCY.CONFLICT`（409），參照或欄位不合規回 400；已被物料引用的單位／品牌／分類刪除回各自的 `IN_USE` 409。

### 物料匯入（WU-MD-014a）

| Method | Path | Permission | Request / Response | Related requirement |
|---|---|---|---|---|
| GET | `/api/v1/md/items/import/template` | `MD.ITEM.IMPORT` | `format=xlsx\|csv` → `item-import.xlsx`／`.csv` | REQ_F4118.3.6 |
| POST | `/api/v1/md/items/import/preview` | `MD.ITEM.IMPORT` | multipart `file` → `ItemImportResult`，只讀 | REQ_F4118.3.6 |
| POST | `/api/v1/md/items/import` | `MD.ITEM.IMPORT` | multipart `file` → `ItemImportResult`，重新驗證後整批提交 | REQ_F4118.3.6 |

欄位：`code,name,specification,baseUomCode,brandCode,categoryCode`；`code`、`name`、`baseUomCode` 為必需表頭，未知表頭忽略。列狀態 `Create`／`Skip`／`Error`，錯誤包含欄位與 `REQUIRED`／`INVALID`／`NOT_FOUND`／`DUPLICATE_IN_FILE`。提交時若有錯誤列回 422 `MD.ITEM_IMPORT.HAS_ERRORS`；檔案錯誤回 422 `COMMON.IMPORT.*`；並行同碼衝突回 409 `MD.ITEM.DUPLICATE_CODE`。匯入依公司鎖 `md_item_import` 序列化，沒有庫存欄位。

## 物料專屬換算（WU-MD-012c）

| Method | Path | Permission | Request / Response | Related requirement |
|---|---|---|---|---|
| GET | `/api/v1/md/items/{itemId}/uom-conversions` | `MD.ITEM.VIEW` | → `ItemUomConversionView[]`（依單位代碼排序） | REQ_F4118.3.5 |
| GET | `/api/v1/md/items/{itemId}/uom-conversions/convert` | `MD.ITEM.VIEW` | `fromUomId`, `toUomId`, `quantity` → `{itemId,fromUomId,toUomId,quantity,result}` | REQ_F4118.3.5 |
| POST | `/api/v1/md/items/{itemId}/uom-conversions` | `MD.ITEM.EDIT` | `{uomId,factor}` → 201 `ItemUomConversionView` | REQ_F4118.3.5 |
| PUT | `/api/v1/md/items/{itemId}/uom-conversions/{id}` | `MD.ITEM.EDIT` | `{factor,version}` → `ItemUomConversionView` | REQ_F4118.3.5 |
| DELETE | `/api/v1/md/items/{itemId}/uom-conversions/{id}` | `MD.ITEM.EDIT` | `version` query → 204 | REQ_F4118.3.5 |

`ItemUomConversionView` = `id,itemId,uomId,uomCode,factor,version`，意義為 1 個 `uomCode` = `factor` 個物料基本單位。錯誤碼：`MD.ITEM.NOT_FOUND`（404，物料不在目前公司）、`MD.ITEM_UOM_CONVERSION.NOT_FOUND`（404，換算不存在或不屬該物料）、`MD.ITEM_UOM_CONVERSION.DUPLICATE`（409）、`MD.UOM.NOT_FOUND`（404，試算單位不存在）、`MD.UOM_CONVERSION.NO_PATH`（422）、`COMMON.CONCURRENCY.CONFLICT`（409）；`uomId`／`factor` 不合規回 400。模組間契約：`IUomConverter.ConvertForItemAsync(itemId, fromUomId, toUomId, quantity)`（ADR-035）。

## 倉庫與儲位（WU-MD-015）

| Method | Path | Permission | Request / Response | Related requirement |
|---|---|---|---|---|
| GET | `/api/v1/md/warehouses` | `MD.WAREHOUSE.VIEW` | `page`, `pageSize`, `keyword`（代碼／名稱／地址）, `sort=code\|-code\|name\|-name` → `PagedResult<WarehouseResponse>` | REQ_F4118.4.1 |
| POST | `/api/v1/md/warehouses` | `MD.WAREHOUSE.CREATE` | `{code,name,address?,contactName?,phone?}` → 201 `WarehouseResponse` | REQ_F4118.4.2 |
| PUT | `/api/v1/md/warehouses/{id}` | `MD.WAREHOUSE.EDIT` | `{name,address?,contactName?,phone?,isActive,version}` → `WarehouseResponse` | REQ_F4118.4.3 |
| DELETE | `/api/v1/md/warehouses/{id}` | `MD.WAREHOUSE.DELETE` | `version` query → 204 | REQ_F4118.4.4 |
| GET | `/api/v1/md/warehouses/{warehouseId}/locations` | `MD.WAREHOUSE.VIEW` | `page`, `pageSize`, `keyword`, `sort=code\|-code\|name\|-name` → `PagedResult<StorageLocationResponse>` | REQ_F4118.4.5 |
| POST | `/api/v1/md/warehouses/{warehouseId}/locations` | `MD.WAREHOUSE.EDIT` | `{code,name}` → 201 `StorageLocationResponse` | REQ_F4118.4.5 |
| PUT | `/api/v1/md/warehouses/{warehouseId}/locations/{id}` | `MD.WAREHOUSE.EDIT` | `{name,isActive,version}` → `StorageLocationResponse` | REQ_F4118.4.5 |
| DELETE | `/api/v1/md/warehouses/{warehouseId}/locations/{id}` | `MD.WAREHOUSE.EDIT` | `version` query → 204 | REQ_F4118.4.5 |

`WarehouseResponse` = `id,code,name,address,contactName,phone,isActive,version`；`StorageLocationResponse` = `id,warehouseId,code,name,isActive,version`。錯誤碼：`MD.WAREHOUSE.DUPLICATE_CODE`／`MD.STORAGE_LOCATION.DUPLICATE_CODE`（409）、`MD.WAREHOUSE.NOT_FOUND`／`MD.STORAGE_LOCATION.NOT_FOUND`（404）、`MD.WAREHOUSE.HAS_LOCATIONS`（409）、`MD.STORAGE_LOCATION.WAREHOUSE_INACTIVE`（422）、`COMMON.CONCURRENCY.CONFLICT`（409）；欄位驗證 400（`code`、`name`、`address`、`contactName`、`phone`）。倉庫服務繼承 `MasterCategoryService<T>`；刪除倉庫與新增儲位在公司級 applock `md_warehouse` 內執行（ADR-036）。

## 幣別與匯率（WU-MD-016）

| Method | Path | Permission | Request / Response | Related requirement |
|---|---|---|---|---|
| GET | `/api/v1/md/currencies` | `MD.CURRENCY.VIEW` | `page`, `pageSize`, `keyword`（代碼／名稱）, `sort=code\|-code\|name\|-name` → `PagedResult<CurrencyResponse>` | REQ_F4118.35.1 |
| POST | `/api/v1/md/currencies` | `MD.CURRENCY.CREATE` | `{code,name,symbol?,decimalPlaces,isBase}` → 201 `CurrencyResponse` | REQ_F4118.35.2 |
| PUT | `/api/v1/md/currencies/{id}` | `MD.CURRENCY.EDIT` | `{name,symbol?,decimalPlaces,isBase,isActive,version}` → `CurrencyResponse` | REQ_F4118.35.3 |
| DELETE | `/api/v1/md/currencies/{id}` | `MD.CURRENCY.DELETE` | `version` query → 204 | REQ_F4118.35.4 |
| GET | `/api/v1/md/currencies/{currencyId}/rates` | `MD.CURRENCY.VIEW` | `page`, `pageSize`, `sort=-effectiveFrom`（預設）`\|effectiveFrom`, `on`（yyyy-MM-dd，可空）→ `PagedResult<ExchangeRateResponse>` | REQ_F4118.36.1 |
| POST | `/api/v1/md/currencies/{currencyId}/rates` | `MD.CURRENCY.EDIT` | `{effectiveFrom,effectiveTo?,rate}` → 201 `ExchangeRateResponse` | REQ_F4118.36.2 |
| PUT | `/api/v1/md/currencies/{currencyId}/rates/{id}` | `MD.CURRENCY.EDIT` | `{effectiveFrom,effectiveTo?,rate,version}` → `ExchangeRateResponse` | REQ_F4118.36.3 |
| DELETE | `/api/v1/md/currencies/{currencyId}/rates/{id}` | `MD.CURRENCY.EDIT` | `version` query → 204 | REQ_F4118.36.4 |
| GET | `/api/v1/md/exchange-rates/lookup` | `MD.CURRENCY.VIEW` | `currencyCode`, `date` → `{currencyCode,baseCurrencyCode,date,rate}` | REQ_F4118.36.5 |

`CurrencyResponse` = `id,code,name,symbol,decimalPlaces,isBase,isActive,version`；`ExchangeRateResponse` = `id,currencyId,effectiveFrom,effectiveTo,rate,version`（1 外幣 = `rate` 本位幣）。錯誤碼：`MD.CURRENCY.DUPLICATE_CODE`／`BASE_LOCKED`／`IS_BASE`／`IN_USE`（409）、`MD.CURRENCY.BASE_REQUIRED`／`BASE_INACTIVE`（422）、`MD.CURRENCY.NOT_FOUND`（404）、`MD.EXCHANGE_RATE.OVERLAP`（409）、`MD.EXCHANGE_RATE.BASE_CURRENCY`／`NO_BASE_CURRENCY`／`CURRENCY_INACTIVE`／`NOT_FOUND`（查詢，422）、`MD.EXCHANGE_RATE.NOT_FOUND`（修改／刪除，404）、`COMMON.CONCURRENCY.CONFLICT`（409）；欄位驗證 400（`code`、`name`、`symbol`、`decimalPlaces`、`effectiveTo`、`rate`）。模組間契約 `ICurrencyLookup`：`GetBaseCurrencyAsync`、`FindCurrencyAsync(code)`、`GetRateAsync(code, date)`、`GetPrecisionAsync`（ADR-037）。

## 稅別與稅率（WU-MD-017）

| Method | Path | Permission | Request / Response | Related requirement |
|---|---|---|---|---|
| GET | `/api/v1/md/tax-codes` | `MD.TAX_CODE.VIEW` | `page`, `pageSize`, `keyword`（代碼／名稱）, `sort=code\|-code\|name\|-name`, `on`（yyyy-MM-dd，預設今日業務日）→ `PagedResult<TaxCodeResponse>` | REQ_F4118.37.1 |
| POST | `/api/v1/md/tax-codes` | `MD.TAX_CODE.CREATE` | `{code,name,taxType}` → 201 `TaxCodeResponse` | REQ_F4118.37.2 |
| PUT | `/api/v1/md/tax-codes/{id}` | `MD.TAX_CODE.EDIT` | `{name,taxType,isActive,version}` → `TaxCodeResponse` | REQ_F4118.37.3 |
| DELETE | `/api/v1/md/tax-codes/{id}` | `MD.TAX_CODE.DELETE` | `version` query → 204 | REQ_F4118.37.4 |
| GET | `/api/v1/md/tax-codes/{taxCodeId}/rates` | `MD.TAX_CODE.VIEW` | `page`, `pageSize`, `sort=-effectiveFrom`（預設）`\|effectiveFrom`, `on`（可空）→ `PagedResult<TaxRateResponse>` | REQ_F4118.38.1 |
| POST | `/api/v1/md/tax-codes/{taxCodeId}/rates` | `MD.TAX_CODE.EDIT` | `{effectiveFrom,effectiveTo?,ratePercent}` → 201 `TaxRateResponse` | REQ_F4118.38.2 |
| PUT | `/api/v1/md/tax-codes/{taxCodeId}/rates/{id}` | `MD.TAX_CODE.EDIT` | `{effectiveFrom,effectiveTo?,ratePercent,version}` → `TaxRateResponse` | REQ_F4118.38.3 |
| DELETE | `/api/v1/md/tax-codes/{taxCodeId}/rates/{id}` | `MD.TAX_CODE.EDIT` | `version` query → 204 | REQ_F4118.38.4 |
| GET | `/api/v1/md/tax-rates/lookup` | `MD.TAX_CODE.VIEW` | `taxCode`, `direction=Output\|Input`, `date` → `{taxCode,direction,date,ratePercent}` | REQ_F4118.38.5 |
| POST | `/api/v1/md/tax-rates/calculate` | `MD.TAX_CODE.VIEW` | `{taxCode,direction,date,amount,priceIncludesTax,currencyCode}` → `{ratePercent,net,tax,gross}` | REQ_F4118.38.6 |

`TaxCodeResponse` = `id,code,name,taxType,currentRatePercent,isActive,version`（`currentRatePercent` 為 `on` 當日適用稅率，沒有則 null）；`TaxRateResponse` = `id,taxCodeId,effectiveFrom,effectiveTo,ratePercent,version`。錯誤碼：`MD.TAX_CODE.DUPLICATE_CODE`／`IN_USE`（409）、`MD.TAX_CODE.NOT_FOUND`（404）、`MD.TAX_RATE.OVERLAP`（409）、`MD.TAX_RATE.TAX_CODE_INACTIVE`／`DIRECTION_MISMATCH`／`NOT_FOUND`（查詢，422）、`MD.TAX_RATE.NOT_FOUND`（修改／刪除，404）、`COMMON.CONCURRENCY.CONFLICT`（409）；欄位驗證 400（`code`、`name`、`taxType`、`effectiveTo`、`ratePercent`、`currencyCode`）。模組間契約 `ITaxRateLookup`：`FindTaxCodeAsync(code)`、`GetRatePercentAsync(code, direction, date)`、`CalculateAsync(...)`；計算函式 `MyERP.Shared.Monetary.Tax.Calculate`（ADR-038）。

## 會計科目（WU-MD-019）

| 方法 | 路徑 | 權限 | 參數／回應 | 需求 |
|---|---|---|---|---|
| GET | `/api/v1/md/accounts` | `MD.ACCOUNT.VIEW` | → `AccountResponse[]`（目前公司全部未刪除科目，含停用，按代碼排序；不分頁，前端以 `parentId` 組樹） | REQ_F4118.39.1 |
| POST | `/api/v1/md/accounts` | `MD.ACCOUNT.CREATE` | `{code,name,parentId?,accountType?,balanceDirection?}` → 201 `AccountResponse` | REQ_F4118.39.2 |
| PUT | `/api/v1/md/accounts/{id}` | `MD.ACCOUNT.EDIT` | `{name,balanceDirection,isActive,version}` → `AccountResponse` | REQ_F4118.39.3 |
| DELETE | `/api/v1/md/accounts/{id}` | `MD.ACCOUNT.DELETE` | `version` query → 204 | REQ_F4118.39.4 |

`AccountResponse` = `id,code,name,parentId,accountType,balanceDirection,isActive,isPostable,version`；`accountType`、`balanceDirection` 以名稱字串傳遞。錯誤碼：`MD.ACCOUNT.DUPLICATE_CODE`／`HAS_CHILDREN`／`IN_USE`（409）、`MD.ACCOUNT.NOT_FOUND`（404）、`MD.ACCOUNT.INVALID_PARENT`／`CODE_PREFIX`／`TYPE_MISMATCH`／`PARENT_IN_USE`／`HAS_ACTIVE_CHILDREN`／`PARENT_INACTIVE`（422）、`COMMON.CONCURRENCY.CONFLICT`（409）；欄位驗證 400（`code`、`name`、`accountType`、`balanceDirection`）。模組間契約 `IChartOfAccounts`：`FindAsync(code)`、`RequirePostableAsync(code)`（另有 `MD.ACCOUNT.INACTIVE`／`NOT_POSTABLE` 422）；使用方實作 `IAccountUsage.IsInUseAsync(code)`（ADR-039）。

## 結算方式與付款條件（WU-MD-018）

| 方法 | 路徑 | 權限 | 參數／回應 | 需求 |
|---|---|---|---|---|
| GET | `/api/v1/md/settlement-methods` | `MD.SETTLEMENT_METHOD.VIEW` | `page,pageSize,sort`（`code`／`name`，可加 `-`）、`keyword` → `PagedResult<SettlementMethodResponse>` | REQ_F4118.14.1 |
| POST | `/api/v1/md/settlement-methods` | `MD.SETTLEMENT_METHOD.CREATE` | `{code,name}` → 201 `SettlementMethodResponse` | REQ_F4118.14.2 |
| PUT | `/api/v1/md/settlement-methods/{id}` | `MD.SETTLEMENT_METHOD.EDIT` | `{name,isActive,version}` → `SettlementMethodResponse` | REQ_F4118.14.3 |
| DELETE | `/api/v1/md/settlement-methods/{id}` | `MD.SETTLEMENT_METHOD.DELETE` | `version` query → 204 | REQ_F4118.14.4 |
| GET | `/api/v1/md/payment-terms` | `MD.PAYMENT_TERM.VIEW` | `page,pageSize,sort`（`code`／`name`，可加 `-`）、`keyword` → `PagedResult<PaymentTermResponse>` | REQ_F4118.40.1 |
| POST | `/api/v1/md/payment-terms` | `MD.PAYMENT_TERM.CREATE` | `{code,name,dueType,days?,closingDay?,months?,paymentDay?}` → 201 `PaymentTermResponse` | REQ_F4118.40.2 |
| PUT | `/api/v1/md/payment-terms/{id}` | `MD.PAYMENT_TERM.EDIT` | `{name,dueType,days?,closingDay?,months?,paymentDay?,isActive,version}` → `PaymentTermResponse` | REQ_F4118.40.3 |
| DELETE | `/api/v1/md/payment-terms/{id}` | `MD.PAYMENT_TERM.DELETE` | `version` query → 204 | REQ_F4118.40.4 |
| GET | `/api/v1/md/payment-terms/due-date` | `MD.PAYMENT_TERM.VIEW` | `code,documentDate` → `{code,documentDate,dueDate}` | REQ_F4118.40.5 |

`SettlementMethodResponse` = `id,code,name,isActive,version`；`PaymentTermResponse` = `id,code,name,dueType,days,closingDay,months,paymentDay,isActive,version`（`dueType` 以名稱字串 `NetDays`／`ClosingDays`／`ClosingMonthDay` 傳遞，不適用欄位為 null）。錯誤碼：`MD.SETTLEMENT_METHOD.DUPLICATE_CODE`／`IN_USE`、`MD.PAYMENT_TERM.DUPLICATE_CODE`／`IN_USE`（409）、`MD.SETTLEMENT_METHOD.NOT_FOUND`、`MD.PAYMENT_TERM.NOT_FOUND`（404）、`MD.PAYMENT_TERM.INACTIVE`（試算，422）、`COMMON.CONCURRENCY.CONFLICT`（409）；欄位驗證 400（`code`、`name`、`dueType`、`days`、`closingDay`、`months`、`paymentDay`、`documentDate`）。模組間契約 `ISettlementMethodLookup`：`FindAsync(code)`、`RequireActiveAsync(code)`（另有 `MD.SETTLEMENT_METHOD.INACTIVE` 422）；`IPaymentTermLookup`：`FindAsync(code)`、`CalculateDueDateAsync(code, documentDate)`；引用方實作 `ISettlementMethodUsage`／`IPaymentTermUsage.IsInUseAsync(code)`（ADR-040）。

## 收支類別與資金帳戶（WU-MD-020a）

| 方法 | 路徑 | 權限 | 參數／回應 | 需求 |
|---|---|---|---|---|
| GET | `/api/v1/md/fund-accounts` | `MD.FUND_ACCOUNT.VIEW` | 分頁、關鍵字、排序及可選 `fundType` → `PagedResult<FundAccountResponse>` | REQ_F4118.5.1 |
| POST | `/api/v1/md/fund-accounts` | `MD.FUND_ACCOUNT.CREATE` | `{code,name,currencyId,fundType,bankName?,bankAccountNo?,ledgerAccountId?}` → 201 | REQ_F4118.5.2 |
| PUT | `/api/v1/md/fund-accounts/{id}` | `MD.FUND_ACCOUNT.EDIT` | `{name,fundType,bankName?,bankAccountNo?,ledgerAccountId?,isActive,version}` → 帳戶 | REQ_F4118.5.3 |
| DELETE | `/api/v1/md/fund-accounts/{id}` | `MD.FUND_ACCOUNT.DELETE` | `version` query → 204 | REQ_F4118.5.4 |
| GET | `/api/v1/md/income-expense-categories` | `MD.INCOME_EXPENSE_CATEGORY.VIEW` | 分頁、關鍵字、排序及可選 `direction` → `PagedResult<IncomeExpenseCategoryResponse>` | REQ_F4118.12.1 |
| POST | `/api/v1/md/income-expense-categories` | `MD.INCOME_EXPENSE_CATEGORY.CREATE` | `{code,name,direction,ledgerAccountId?}` → 201 | REQ_F4118.12.2 |
| PUT | `/api/v1/md/income-expense-categories/{id}` | `MD.INCOME_EXPENSE_CATEGORY.EDIT` | `{name,ledgerAccountId?,isActive,version}` → 類別 | REQ_F4118.12.3 |
| DELETE | `/api/v1/md/income-expense-categories/{id}` | `MD.INCOME_EXPENSE_CATEGORY.DELETE` | `version` query → 204 | REQ_F4118.12.4 |

列舉以名稱傳遞；錯誤為 `DUPLICATE_CODE`／`IN_USE`（409）、`NOT_FOUND`（404）、`INVALID_CURRENCY`／`INVALID_LEDGER_ACCOUNT`／`INACTIVE`／`DIRECTION_MISMATCH`（422），以及欄位驗證 400。其他模組經 `IFundAccountLookup`／`IIncomeExpenseCategoryLookup` 讀取，使用方實作各自 Usage 契約（ADR-041）。

## 工序分類與工序（WU-MD-021a）

| 方法 | 路徑 | 權限 | 參數／回應 | 需求 |
|---|---|---|---|---|
| GET | `/api/v1/md/process-categories` | `MD.PROCESS_CATEGORY.VIEW` | 分頁、關鍵字、排序（`sortOrder` 預設、`code`、`name`）→ `PagedResult<ProcessCategoryResponse>` | REQ_F4118.15.1 |
| POST | `/api/v1/md/process-categories` | `MD.PROCESS_CATEGORY.CREATE` | `{code,name,sortOrder}` → 201 | REQ_F4118.15.2 |
| PUT | `/api/v1/md/process-categories/{id}` | `MD.PROCESS_CATEGORY.EDIT` | `{name,sortOrder,isActive,version}` → 類別 | REQ_F4118.15.3 |
| DELETE | `/api/v1/md/process-categories/{id}` | `MD.PROCESS_CATEGORY.DELETE` | `version` query → 204 | REQ_F4118.15.4 |
| GET | `/api/v1/md/processes` | `MD.PROCESS.VIEW` | 分頁、關鍵字、排序（同上）及可選 `categoryId` → `PagedResult<ProcessResponse>` | REQ_F4118.7.1 |
| POST | `/api/v1/md/processes` | `MD.PROCESS.CREATE` | `{code,name,categoryId?,uomId?,standardRate,sortOrder}` → 201 | REQ_F4118.7.2 |
| PUT | `/api/v1/md/processes/{id}` | `MD.PROCESS.EDIT` | `{name,categoryId?,uomId?,standardRate,sortOrder,isActive,version}` → 工序 | REQ_F4118.7.3 |
| DELETE | `/api/v1/md/processes/{id}` | `MD.PROCESS.DELETE` | `version` query → 204 | REQ_F4118.7.4 |

`ProcessCategoryResponse` = `id,code,name,sortOrder,processCount,isActive,version`；`ProcessResponse` = `id,code,name,categoryId,uomId,standardRate,sortOrder,isActive,version`。錯誤碼：`MD.PROCESS_CATEGORY.DUPLICATE_CODE`／`HAS_PROCESSES`、`MD.PROCESS.DUPLICATE_CODE`／`IN_USE`、`COMMON.CONCURRENCY.CONFLICT`（409）；`NOT_FOUND`（404）；`MD.PROCESS.INVALID_CATEGORY`／`INVALID_UOM`／`INACTIVE`（422）；欄位驗證 400（`code`、`name`、`sortOrder`、`standardRate`、`sort`）。模組間契約 `IProcessLookup`：`FindAsync(code)`、`RequireActiveAsync(code)`、`ListActiveAsync(categoryCode?)`；PP 實作 `IProcessUsage.IsInUseAsync(code)`（ADR-042，REQ_F4118.7.5）。

## 客戶聯絡人與地址（WU-MD-004a）

| 方法 | 路徑 | 權限 | 參數／回應 | 需求 |
|---|---|---|---|---|
| GET | `/api/v1/md/customers/{customerId}/contacts` | `MD.CUSTOMER.VIEW` | → `CustomerContactResponse[]`（預設排第一，不分頁） | REQ_F4118.41.1 |
| POST | `/api/v1/md/customers/{customerId}/contacts` | `MD.CUSTOMER.EDIT` | `{name,position?,mobile?,phone?,email?,messagingId?,note?,isDefault}` → 201 | REQ_F4118.41.2 |
| PUT | `/api/v1/md/customers/{customerId}/contacts/{id}` | `MD.CUSTOMER.EDIT` | 同上加 `isActive,version` → 聯絡人 | REQ_F4118.41.3 |
| DELETE | `/api/v1/md/customers/{customerId}/contacts/{id}` | `MD.CUSTOMER.EDIT` | `version` query → 204 | REQ_F4118.41.4 |
| GET | `/api/v1/md/customers/{customerId}/addresses` | `MD.CUSTOMER.VIEW` | → `CustomerAddressResponse[]` | REQ_F4118.42.1 |
| POST | `/api/v1/md/customers/{customerId}/addresses` | `MD.CUSTOMER.EDIT` | `{address,postalCode?,recipient?,phone?,isDefault}` → 201 | REQ_F4118.42.2 |
| PUT | `/api/v1/md/customers/{customerId}/addresses/{id}` | `MD.CUSTOMER.EDIT` | 同上加 `isActive,version` → 地址 | REQ_F4118.42.3 |
| DELETE | `/api/v1/md/customers/{customerId}/addresses/{id}` | `MD.CUSTOMER.EDIT` | `version` query → 204 | REQ_F4118.42.4 |

`CustomerContactResponse` = `id,customerId,name,position,mobile,phone,email,messagingId,note,isDefault,isActive,version`；`CustomerAddressResponse` = `id,customerId,address,postalCode,recipient,phone,isDefault,isActive,version`。設為預設時自動取消同客戶原預設。錯誤碼：`MD.CUSTOMER.NOT_FOUND`、`MD.CUSTOMER_CONTACT.NOT_FOUND`、`MD.CUSTOMER_ADDRESS.NOT_FOUND`（404）；`COMMON.CONCURRENCY.CONFLICT`（409）；欄位驗證 400（`name`、`position`、`mobile`、`phone`、`email`、`messagingId`、`note`、`address`、`postalCode`、`recipient`、`isDefault`）（ADR-043）。

## 供應商聯絡人（WU-MD-008）

| 方法 | 路徑 | 權限 | 參數／回應 | 需求 |
|---|---|---|---|---|
| GET | `/api/v1/md/suppliers/{supplierId}/contacts` | `MD.SUPPLIER.VIEW` | → `SupplierContactResponse[]`（預設排第一，不分頁） | REQ_F4118.43.1 |
| POST | `/api/v1/md/suppliers/{supplierId}/contacts` | `MD.SUPPLIER.EDIT` | `{name,position?,mobile?,phone?,email?,messagingId?,note?,isDefault}` → 201 | REQ_F4118.43.2 |
| PUT | `/api/v1/md/suppliers/{supplierId}/contacts/{id}` | `MD.SUPPLIER.EDIT` | 同上加 `isActive,version` → 聯絡人 | REQ_F4118.43.3 |
| DELETE | `/api/v1/md/suppliers/{supplierId}/contacts/{id}` | `MD.SUPPLIER.EDIT` | `version` query → 204 | REQ_F4118.43.4 |

`SupplierContactResponse` = `id,supplierId,name,position,mobile,phone,email,messagingId,note,isDefault,isActive,version`。錯誤碼：`MD.SUPPLIER.NOT_FOUND`、`MD.SUPPLIER_CONTACT.NOT_FOUND`（404）；`COMMON.CONCURRENCY.CONFLICT`（409）；欄位驗證 400 同客戶聯絡人（ADR-043）。

## 供應商匯入（WU-MD-009；ADR-044）

| Method | Path | Permission | Request / Response | Related requirement |
|---|---|---|---|---|
| GET | `/api/v1/md/suppliers/import/template?format=xlsx\|csv` | `MD.SUPPLIER.IMPORT` | xlsx／UTF-8 BOM CSV 空白表頭範本 `supplier-import.*` | REQ_F4118.2.7 |
| POST | `/api/v1/md/suppliers/import/preview` | `MD.SUPPLIER.IMPORT` | `multipart/form-data` 的 `file` → `PartnerImportResult {rows,createdCount,skippedCount,errorCount}`，唯讀 | REQ_F4118.2.7 |
| POST | `/api/v1/md/suppliers/import` | `MD.SUPPLIER.IMPORT` | 同一檔案重新上傳，整批提交；逐列 `Create`／`Skip`／`Error` | REQ_F4118.2.7 |

欄位：`code,name,categoryCode,contactName,contactMobile,contactPhone,contactEmail`（沒有地址欄；未知表頭忽略）。錯誤碼：有錯誤列 422 `MD.SUPPLIER_IMPORT.HAS_ERRORS`；檔案 422 `COMMON.IMPORT.*`；併發同碼 409 `MD.SUPPLIER.DUPLICATE_CODE`。公司鎖名 `md_supplier_import`。回應型別與客戶匯入同為 `PartnerImportResult`（原 `CustomerImportResult`，JSON 不變）。

## 物料圖片（WU-MD-013a；ADR-045）

| Method | Path | Permission | Request / Response | Related requirement |
|---|---|---|---|---|
| GET | `/api/v1/md/items/{itemId}/images` | `MD.ITEM.VIEW` | → `ItemImageView[]`（主圖排第一，其餘按排序號、id；不含內容） | REQ_F4118.44.1 |
| GET | `/api/v1/md/items/{itemId}/images/{id}/content` | `MD.ITEM.VIEW` | → `image/png`／`image/jpeg` inline；`ETag` 為版本、`Cache-Control: private, no-cache`；`If-None-Match` 相符回 304 | REQ_F4118.44.1 |
| POST | `/api/v1/md/items/{itemId}/images` | `MD.ITEM.EDIT` | `multipart/form-data`：`file`、`sortOrder?`（0–9999）、`isPrimary` → 201 `ItemImageView`；請求上限 2 MB + 64 KB | REQ_F4118.44.2 |
| PUT | `/api/v1/md/items/{itemId}/images/{id}` | `MD.ITEM.EDIT` | `{sortOrder,isPrimary,version}` → `ItemImageView` | REQ_F4118.44.3 |
| DELETE | `/api/v1/md/items/{itemId}/images/{id}` | `MD.ITEM.EDIT` | `version` query → 204 | REQ_F4118.44.4 |

`ItemImageView` = `id,itemId,contentType,fileName,sizeBytes,width,height,sortOrder,isPrimary,version`。錯誤碼：`MD.ITEM.NOT_FOUND`、`MD.ITEM_IMAGE.NOT_FOUND`（404）；422 `MD.ITEM_IMAGE.UNSUPPORTED_TYPE`、`TOO_LARGE`、`INVALID_DIMENSIONS`、`LIMIT_REACHED`；400 `file`、`sortOrder`、`isPrimary`；409 `COMMON.CONCURRENCY.CONFLICT`。公司鎖名 `md_item_image`。

## 物料屬性（WU-MD-013b；ADR-045）

| Method | Path | Permission | Request / Response | Related requirement |
|---|---|---|---|---|
| GET | `/api/v1/md/item-attributes` | `MD.ITEM_ATTRIBUTE.VIEW` | `page,pageSize,sort(sortOrder/code/name),keyword` → `PagedResult<ItemAttributeResponse>` | REQ_F4118.45.1 |
| POST | `/api/v1/md/item-attributes` | `MD.ITEM_ATTRIBUTE.CREATE` | `{code,name,sortOrder}` → 201 `ItemAttributeResponse` | REQ_F4118.45.2 |
| PUT | `/api/v1/md/item-attributes/{id}` | `MD.ITEM_ATTRIBUTE.EDIT` | `{name,sortOrder,isActive,version}` → `ItemAttributeResponse` | REQ_F4118.45.3 |
| DELETE | `/api/v1/md/item-attributes/{id}` | `MD.ITEM_ATTRIBUTE.DELETE` | `version` query → 204 | REQ_F4118.45.4 |
| GET | `/api/v1/md/items/{itemId}/attributes` | `MD.ITEM.VIEW` | → `ItemAttributeValueView[]`（按屬性排序號、代碼） | REQ_F4118.45.5 |
| PUT | `/api/v1/md/items/{itemId}/attributes` | `MD.ITEM.EDIT` | `{attributes:[{attributeId,value}]}`（完整集合，空陣列清除）→ `ItemAttributeValueView[]` | REQ_F4118.45.6 |

`ItemAttributeResponse` = `id,code,name,sortOrder,itemCount,isActive,version`；`ItemAttributeValueView` = `attributeId,attributeCode,attributeName,sortOrder,isAttributeActive,value`。錯誤碼：404 `MD.ITEM_ATTRIBUTE.NOT_FOUND`、`MD.ITEM.NOT_FOUND`；409 `MD.ITEM_ATTRIBUTE.DUPLICATE_CODE`、`MD.ITEM_ATTRIBUTE.IN_USE`、`COMMON.CONCURRENCY.CONFLICT`；400 `code`、`name`、`sortOrder`、`sort`、`attributes`（缺少或超過 50 個）、`attributes[i].attributeId`（缺少、重複、不存在、停用的新屬性）、`attributes[i].value`。公司鎖名 `md_item_attribute`（刪除屬性與整批取代共用）。
