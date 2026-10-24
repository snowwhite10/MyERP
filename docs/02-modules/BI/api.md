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
