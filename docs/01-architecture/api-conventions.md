# API 規範

> 來源：MASTER_PROMPT 第 5、7 節；模組前綴決定見 [ADR-003](adr/ADR-003-模組專案結構與資料存取.md)。所有模組的 Controller 必須遵守，審查時逐項檢查。

## 1. 路由

- 格式：`/api/v{版本}/{prefix}/{resource}`，例如 `/api/v1/sal/orders`、`/api/v1/ar/receipts`。
- `{prefix}`：資料表前綴小寫（`sys`、`md`、`inv`、`pur`、`sal`、`gl`、`ar`、`ap`、`fa`、`cm`、`tax`、`co`、`bi`、`pp`、`qm`、`pm`、`crm`、`hr`、`ps`、`scm`）。
- `{resource}`：**複數、kebab-case** 名詞，不重複模組名（前綴已表示模組），例如寫 `orders`、`price-lists`，不寫 `sales-orders`。
- 子資源最多一層：`/api/v1/sal/orders/{id}/lines`、`/api/v1/sal/orders/{id}/attachments`。
- Id 一律為 `long`（路由 `{id:long}`）；單號（`doc_no`）不作為路由鍵，以篩選參數 `docNo` 查詢。

| 動作 | HTTP | 路由 | 成功狀態 |
|---|---|---|---|
| 列表 | GET | `/orders?page=1&pageSize=20&sort=-docDate` | 200 |
| 單筆 | GET | `/orders/{id}` | 200 |
| 新增 | POST | `/orders` | 201 + `Location` |
| 修改 | PUT | `/orders/{id}`（完整表頭 + 明細，含 `version`） | 200 |
| 刪除（草稿 / 主檔軟刪除） | DELETE | `/orders/{id}?version=3` | 204 |
| 狀態動作 | POST | `/orders/{id}/submit`、`/approve`、`/reject`、`/void`、`/post`、`/reverse`、`/close` | 200（回傳更新後單據） |
| 停用 / 啟用（主檔） | POST | `/customers/{id}/deactivate`、`/activate` | 200 |
| 匯出 | GET | `/orders/export?format=xlsx\|pdf&...篩選` | 200 檔案 |
| 匯入 | POST | `/customers/import`（multipart）；`/customers/import/template` 下載範本 | 200 匯入結果 |
| 列印 | GET | `/orders/{id}/print?format=pdf` | 200 檔案 |
| 下拉 / 選擇視窗 | GET | `/customers/lookup?keyword=&page=` | 200（精簡欄位） |

- 狀態動作是**動詞子路徑 + POST**，不用 PATCH 改 `status` 欄位；Request body 可帶 `version`、`comment`（審批意見）。
- 同一請求不得跨兩個不相關的資源；批次操作用 `POST /orders/batch-approve`（body：`ids[]`），回傳每筆結果。

## 2. 版本

- URL 主版本（`v1`）。破壞性變更（刪欄位、改型別、改語意）才升 `v2`，舊版保留至少一個 Phase。
- 新增欄位、新增端點屬非破壞性，不升版。
- OpenAPI 文件：每個主版本一份（`/openapi/v1.json`），Swagger UI 只在 Development 開啟（ADR-001）。

## 3. Request / Response 格式

- JSON，屬性 **camelCase**；`null` 欄位照常輸出（前端不需判斷缺漏）。
- 列舉以**字串**輸出（`"status": "Approved"`），與資料庫 `VARCHAR(30)` 一致。
- 業務日期 `DateOnly`：`"2026-10-10"`；時間戳 `DateTime`：ISO 8601 含時區 `"2026-10-10T11:07:04+08:00"`。
- 金額、數量、單價、匯率：JSON number，後端 `decimal`；前端計算只作預覽，**以後端回傳為準**（表頭合計由後端計算）。
- 幣別：ISO 4217 三碼大寫。
- DTO 命名：`CreateOrderRequest`、`UpdateOrderRequest`、`OrderDto`（單筆詳情）、`OrderListItemDto`（列表精簡）、`OrderLookupDto`（選擇視窗）。不得直接回傳 EF 實體。
- 跨模組參照欄位同時回傳 Id 與顯示名稱：`customerId`、`customerCode`、`customerName`（由 Application 層批次查詢對方 Contracts 取得）。
- 每筆可修改資源回傳 `version`（樂觀鎖）。

## 4. 錯誤處理（ProblemDetails，RFC 9457）

所有錯誤回應 `Content-Type: application/problem+json`：

```json
{
  "type": "https://myerp.local/errors/validation",
  "title": "輸入資料不正確",
  "status": 400,
  "detail": "訂單明細至少一行",
  "instance": "/api/v1/sal/orders",
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
  "errorCode": "SAL.ORDER.LINES_REQUIRED",
  "errors": { "lines": ["至少一行"] }
}
```

| 狀態 | 用途 | `type` 尾段 | 說明 |
|---|---|---|---|
| 400 | 欄位驗證失敗（FluentValidation） | `validation` | `errors` 以 camelCase 欄位路徑為鍵，例如 `lines[0].qty` |
| 401 | 未登入 / Token 無效或過期 | `unauthorized` | 前端自動以 Refresh Token 換新，失敗才導回登入 |
| 403 | 無權限、IP 白名單拒絕、資料範圍外 | `forbidden` | 不透露資源是否存在 |
| 404 | 資源不存在或不屬於目前公司 | `not-found` | |
| 409 | 樂觀鎖衝突（`version` 不符）、單號重複 | `conflict` | 前端提示「資料已被他人修改，請重新載入」 |
| 422 | 業務規則違反（信用額度不足、庫存不足、狀態不允許此動作） | `business-rule` | 必帶 `errorCode` |
| 429 | Rate limit | `rate-limited` | 帶 `Retry-After` 標頭 |
| 500 | 未預期例外 | `internal` | 只回 `traceId`，不回例外細節；寫 Error 日誌 |

- `errorCode` 格式 `{PREFIX}.{RESOURCE}.{REASON}`（大寫、底線），同時作為前端 i18n key（`errors.SAL.ORDER.CREDIT_LIMIT_EXCEEDED`）。
- `title` / `detail` 依 `Accept-Language` 回傳 zh-Hant / zh-Hans / en；前端優先用 `errorCode` 翻譯，`detail` 為後備。
- 業務錯誤以 `Result` / 自訂 `BusinessRuleException` 表示，由全域處理器轉換；Controller 不寫 try/catch。
- 例外對應（`MyERP.Shared.Errors`）：`RequestValidationException` → 400、`ForbiddenException` → 403、`NotFoundException` → 404、`ConflictException` / EF `DbUpdateConcurrencyException` → 409、`BusinessRuleException` → 422、其他 → 500。跨模組共用代碼前綴 `COMMON`（`COMMON.REQUEST.VALIDATION_FAILED`、`COMMON.CONCURRENCY.CONFLICT`、`COMMON.RESOURCE.NOT_FOUND`、`COMMON.SERVER.INTERNAL_ERROR`），見 ADR-005。

## 5. 分頁、排序、篩選

列表端點**一律**支援：

| 參數 | 說明 | 預設 / 限制 |
|---|---|---|
| `page` | 頁碼，從 1 起 | 1 |
| `pageSize` | 每頁筆數 | 20；前端選項 10 / 20 / 50 / 100 / 200；後端接受 1–200，超出回 400（ADR-005） |
| `sort` | 逗號分隔欄位，`-` 前綴為降冪：`sort=-docDate,docNo` | 各資源指定預設（單據預設 `-docDate,-id`） |
| `keyword` | 模糊搜尋（單號、代碼、名稱等資源指定欄位） | |
| 具名篩選 | camelCase 欄位：`status=Approved,Draft`（多值逗號）、`customerId=12`、`docDateFrom=2026-01-01&docDateTo=2026-01-31`、`amountMin`/`amountMax` | 範圍篩選一律 `From`/`To` 或 `Min`/`Max` 後綴 |

回應：

```json
{ "items": [ ... ], "page": 1, "pageSize": 20, "totalCount": 1234 }
```

- 排序欄位以白名單對應（DTO 屬性 → 實體欄位），不在白名單回 400，**不得**動態拼接 SQL。
- 匯出端點使用相同篩選參數，不分頁但上限 100,000 筆（超過回 422，提示縮小範圍）。

## 6. 權限與資料範圍

- 權限代碼 `{PREFIX}.{RESOURCE}.{ACTION}`，全大寫，例如 `SAL.ORDER.APPROVE`、`MD.CUSTOMER.IMPORT`、`SYS.USER.EDIT`。
- `{RESOURCE}`：單數英文名詞，多字以底線連接（`PRICE_LIST`）。
- 標準 `{ACTION}`：

| 動作 | 對應端點 |
|---|---|
| `VIEW` | 列表、單筆、lookup |
| `CREATE` | POST 新增、複製 |
| `EDIT` | PUT、submit |
| `DELETE` | DELETE（草稿 / 主檔） |
| `APPROVE` / `REJECT` | 審批 |
| `VOID` | 作廢 |
| `POST` / `REVERSE` | 過帳 / 沖銷 |
| `PRINT` / `EXPORT` / `IMPORT` | 列印、匯出、匯入 |

- 每個 Controller action 必須標註 `[RequirePermission("...")]`（ArchitectureTests 檢查，只有登入、Captcha、健康檢查可標 `[AllowAnonymous]`）。
- 資料範圍（全公司 / 分店 / 部門 / 本人）由 Application 層查詢自動套用，不由前端傳入。
- 權限代碼清單維護於各模組 `api.md` 的「權限」小節；程式中由各模組的 `IPermissionDefinitionProvider` 宣告、SYS 的 `PermissionCatalog` 彙整檢查，不另建權限表（ADR-014）。
- 自助端點（本人資料、登出、本人授權 `/api/v1/sys/auth/authorization`）只需登入，列於 ArchitectureTests 白名單。
- 授權失敗回 403：缺權限 `COMMON.PERMISSION.DENIED`；`X-Company-Id` 不可存取 `COMMON.COMPANY.ACCESS_DENIED`。

## 7. 標頭與其他

| 標頭 | 方向 | 說明 |
|---|---|---|
| `Authorization: Bearer <access token>` | 請求 | 除匿名端點外必帶 |
| `X-Company-Id` | 請求 | 目前操作公司；必須在用戶可存取的公司內，否則 403 |
| `Accept-Language` | 請求 | `zh-Hant` / `zh-Hans` / `en`，影響錯誤訊息與匯出檔欄名 |
| `X-Request-Id` | 雙向 | 可選；回應一律帶 `traceId`（W3C traceparent） |
| `Retry-After` | 回應 | 429 時 |

- **冪等**：新增單據可帶 `Idempotency-Key`（UUID）；同一 key 24 小時內重送回傳第一次結果（防雙擊重複建單）。
- 檔案上傳單檔上限 10 MB（附件）、匯入檔 5 MB；允許副檔名白名單由各端點指定。
- 健康檢查：`/health/live`、`/health/ready`（匿名，不在 `/api/v1` 下）。

## 8. 文件（Swagger / OpenAPI）

- 每個 action 需有 XML 摘要（`<summary>`）、回應型別（`ProducesResponseType`）與錯誤狀態碼。
- Controller 以 `[Tags("SAL - Sales Order")]` 分組，方便 SDS 引用。
- 各模組 `api.md` 列出：端點、權限代碼、對應 REQ_F ID。

## 9. 檢查清單（code review 用）

- [ ] 路由符合 `/api/v1/{prefix}/{複數資源}`
- [ ] 列表有分頁 / 排序白名單 / 篩選
- [ ] 每個 action 有權限代碼
- [ ] 修改與狀態動作驗證 `version`
- [ ] 錯誤一律 ProblemDetails，業務錯誤有 `errorCode` 與三語訊息
- [ ] 不回傳 EF 實體、不洩漏例外細節
- [ ] 列表無 N+1（跨模組名稱批次取得）
