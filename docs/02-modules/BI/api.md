# BI API design

## WU-BI-000d proposed financial reads

The routes below are plans, not implemented endpoints. BI calls FI Application/Contracts; it cannot open FI repositories or tables. Each FI call validates company, partner/account identity, SYS data scope and its source-specific authority. BI checks report VIEW/EXPORT authority separately.

| Method | Proposed route | BI permission | FI source and response |
|---|---|---|---|
| GET | `/api/v1/bi/reports/cash-bank` | `BI.CASH_BANK.VIEW` | `GetCashBookAsync` (FI-010a); fund account, range, cutoff, opening, debit/credit lines, closing, page token. |
| GET | `/api/v1/bi/reports/ap-details` | `BI.AP_DETAIL.VIEW` | Planned FI Posted payable detail read; supplier, original/allocated/open base amounts and source IDs. |
| GET | `/api/v1/bi/reports/ar-details` | `BI.AR_DETAIL.VIEW` | Planned FI Posted receivable detail read; customer, original/allocated/open base amounts and source IDs. |
| GET | `/api/v1/bi/reports/client-statement` | `BI.CLIENT_STATEMENT.VIEW` | `GetCustomerStatementAsync` (FI-009a); opening, ordered lines, closing, cutoff and page token. |
| GET | `/api/v1/bi/reports/supplier-statement` | `BI.SUPPLIER_STATEMENT.VIEW` | `GetSupplierStatementAsync` (FI-009a); opening, ordered lines, closing, cutoff and page token. |
| GET | `/api/v1/bi/reports/other-income-expenses` | `BI.OTHER_INCOME_EXPENSE.VIEW` | Planned FI Posted other collection/payment read; signed categories, lines and totals. |
| GET | `/api/v1/bi/reports/other-income-expenses/export` | `BI.OTHER_INCOME_EXPENSE.EXPORT` | Same filter-bound committed cutoff as the view; Excel with safe text and totals. |

Query accepts company context, inclusive `from`/`through`, report-specific account/partner, page size and opaque continuation token. The initial bounded range is at most 366 days. First response returns a durable committed cutoff and deterministic order; continuation and export must pass the same cutoff token. No client-supplied SQL column, journal, account mapping or row-state override is accepted. 400 means invalid range/filter/token, 401 anonymous, 403 missing BI or FI authority, 404 hidden identity, 409 expired/stale cutoff, 422 unavailable or inconsistent FI source/currency/overflow, and 500 includes traceId. Swagger and authorised SQL/HTTP proof belong to implementation units.

## WU-BI-001d FI source bindings

The proposed `ap-details` and `ar-details` routes bind respectively to FI `GetPayableDetailsAsync` and `GetReceivableDetailsAsync` (REQ_F4116.19.1–2). Their filter is one company, optional scoped partner, inclusive `asOf` date and include-zero-open flag; FI returns all-page original/credit/settled/open base totals. They do not reuse today's open-balance query. The proposed `other-income-expenses` view and Excel export bind to `GetOtherIncomeExpensesAsync` (REQ_F4116.19.3); its inclusive range and optional OtherCollection/OtherPayment kind yield FI-saved positive income/expense and signed net totals. Export passes the view's same authenticated cutoff token and filters and needs both `BI.OTHER_INCOME_EXPENSE.EXPORT` and FI `FI.OTHER_REPORT.VIEW` authority. Each detail route needs its own BI VIEW and corresponding FI DETAIL.VIEW authority. FI rechecks current authority for every page; a once-valid cutoff never bypasses a revoked role. BI renders FI's named incomplete/expired reason and source IDs, and does not calculate a replacement balance. These are proposed bindings only; no route or Swagger change is claimed.

## WU-BI-002d proposed operational read contracts

All routes below are designs, absent from Swagger. `GET /api/v1/bi/reports/{slug}` takes inclusive `from`/`through` (maximum 366 days), owner-scoped optional item/partner/warehouse/location, stable page size and opaque continuation. First page obtains a filter-bound, durable committed owner manifest; continuation and future export reuse it. Response carries cutoff, source status, totalCount, full-set totals, rows and next token. Owner rechecks its own permission and SYS scope per page. BI never queries owner tables.

| Slugs | Owner read contract proposed | BI permission | Owner permission/dependency |
|---|---|---|---|
| `purchase-order-tracking`, `purchase-detail`, `purchase-by-product`, `purchase-by-supplier` | `IPurchaseReportSource.ReadOrdersAndPostedFlowAsync` returns saved order/receipt/return/invoice identity, status, dates, line quantities, saved amounts and full-set totals. | `BI.PURCHASE_REPORT.VIEW` | `PUR.REPORT.VIEW`; owner contract and committed manifest need implementation. |
| `sales-order-tracking`, `sales-detail`, `sales-by-product`, `sales-by-client` | `ISalesReportSource.ReadOrdersAndPostedFlowAsync` returns saved order/delivery/return/invoice identity, status, dates, quantities and saved commercial amounts. | `BI.SALES_REPORT.VIEW` | `SAL.REPORT.VIEW`; owner contract and committed manifest need implementation. |
| `production-plan-tracking`, `material-requisition-detail`, `material-requisition-summary`, `production-inbound-detail`, `production-inbound-summary` | `IProductionReportSource.ReadPlanAndPostedFlowAsync` returns saved PP lines and confirmed INV movement references. | `BI.PRODUCTION_REPORT.VIEW` | `PP.REPORT.VIEW`; PP is unimplemented, so all five must return named unavailable-source result until contract and INV confirmation exist. |
| `inventory-balance`, `inventory-flow-detail`, `inventory-flow-summary` | `IInventoryReportSource.ReadPostedFlowAsync` returns opening and immutable Posted movement lines, direction, source, location and full-set totals. | `BI.INVENTORY_REPORT.VIEW` | `INV.REPORT.VIEW`; existing stock-flow query is a candidate, but needs stable cross-page manifest. |

Owner reports must return 401/403/404 according to authentication, report/source authority and scoped identity; 400 for invalid dates/filter/token, 409 for expired or changed manifest, 422 for unavailable/inconsistent source or overflow, 500 with traceId. A future `/export` requires both BI EXPORT and owner source VIEW, unchanged manifest and a complete safe file. No upload route applies.

## WU-BI-003d proposed dashboard contract

`GET /api/v1/bi/dashboard/kpis?from=YYYY-MM-DD&through=YYYY-MM-DD` is a proposed route, absent from Swagger. It accepts one company context and an inclusive interval of 1–366 days. The response has four independent card results: key, unit/currency, current value, prior value, comparison kind (`interval` or `asOf`), owner, owner cutoff, status and named reason. No aggregate cutoff is claimed. FI `ReadPostedSalesRevenueAsync` and `ReadOpenReceivablesAsOfAsync`, SD `ReadFirstApprovedOrderCountAsync`, and INV `ReadLowStockCountAsOfAsync` are proposed owner read contracts; each returns exact full-set total and durable committed manifest. BI requires `BI.DASHBOARD.VIEW`; the respective owner requires `FI.REVENUE_REPORT.VIEW`, `FI.AR_REPORT.VIEW`, `SAL.ORDER_REPORT.VIEW` or `INV.STOCK_REPORT.VIEW`, plus current SYS data scope. Missing owner source is a card-level unavailable result. Anonymous request is 401; missing BI authority is 403; invalid dates are 400; revoked owner authority is denied, never cached; expired manifest is 409; incomplete source is 422; unexpected server failure is 500 with traceId. No export/upload is in this slice.

## WU-BI-004d proposed comparison, drill and schedule contracts

All routes are design only and absent from Swagger. `GET /api/v1/bi/reports/{slug}/compare` takes two inclusive `from`/`through` pairs (each 1–366 days), one shared filter/group/sort and two opaque owner manifests. Its response names both cutoffs, units, currencies, full-set group totals, difference, percentage status and completeness for each side. `GET /api/v1/bi/reports/{slug}/drill` takes one aggregate key and its filter-bound owner manifest; continuation returns the same source IDs and full-set total. `GET .../compare/export` and `GET .../drill/export` require the corresponding `BI.{REPORT}.EXPORT` plus owner VIEW, and preserve the view manifests. BI's comparison/drill VIEW permission follows the underlying report's `BI.{REPORT}.VIEW`; owner permission follows its existing report source. The owner Application contract must explicitly support comparable metrics and group-to-source membership; BI cannot query owner tables to reconstruct it.

| Method | Proposed route | Permission | Response / invariant |
|---|---|---|---|
| GET, POST | `/api/v1/bi/report-schedules` | `BI.SCHEDULE.VIEW`, `BI.SCHEDULE.CREATE` | Company-scoped list/create; creation validates report, recipient, bounded filters, cadence and current owner source authority. |
| GET, PUT, DELETE | `/api/v1/bi/report-schedules/{id}` | `BI.SCHEDULE.VIEW`, `BI.SCHEDULE.EDIT`, `BI.SCHEDULE.DELETE` | Scoped definition, version/ETag conflict 409; delete cancels future occurrences and keeps history. |
| POST | `/api/v1/bi/report-schedules/{id}/pause` | `BI.SCHEDULE.EDIT` | Versioned pause; no invented successful run. |
| GET | `/api/v1/bi/report-schedules/{id}/runs` | `BI.SCHEDULE.VIEW` | Occurrence key, resolved local time, status, reason, row count, owner cutoff and artifact expiry; no owner rows in metadata. |
| GET | `/api/v1/bi/report-runs/{runId}/artifact` | `BI.SCHEDULE.DOWNLOAD` plus report EXPORT and owner VIEW | Current recipient/company/scope recheck, complete digest-checked file only. |

The worker is an internal hosted service using BI's own job/run persistence, not an unauthorised HTTP caller. Its lease and occurrence-key protocol is specified by BR-BI-030–33. No email route or external recipient is implied. Invalid input/token is 400; anonymous 401; revoked BI/owner authority 403; hidden company/recipient/source 404; version or expired manifest 409; unavailable/incomplete owner, overflow or size limit 422; 500 includes traceId. An unknown timeout is not labelled completed. Implementation units add Swagger, true SQL and HTTP tests.
