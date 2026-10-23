# SD API baseline (WU-SD-000; endpoints not implemented)

All routes use `/api/v1/sal/` to match the `sal_` module prefix; Swagger groups `SD - Quotations`, `SD - Orders`, `SD - Deliveries`, `SD - Pricing`, `SD - Invoices`, `SD - Credit` and `SD - Returns`. Every route requires the current company and its own `SD.<RESOURCE>.<ACTION>` permission. Read and write scope follow SYS action data scope before loading a document. Lists use stable server pagination, whitelist sort and date/status/customer filters. 401/403/404/400/409/422/500 use ProblemDetails with `errorCode` and `traceId`.

| Method | Proposed path | Purpose | Permission |
|---|---|---|---|
| GET/POST | `quotations` | Page saved quotations / create Draft | `SD.QUOTATION.VIEW` / `CREATE` |
| GET/PUT/DELETE | `quotations/{id}` | Detail / versioned Draft edit / Draft delete | `SD.QUOTATION.VIEW` / `EDIT` / `DELETE` |
| POST | `quotations/{id}/submit` | Request SYS approval | `SD.QUOTATION.SUBMIT` |
| POST | `quotations/{id}/orders` | Convert approved, unexpired source once | `SD.ORDER.CREATE` |
| GET/POST | `orders` | Page saved orders / create Draft | `SD.ORDER.VIEW` / `CREATE` |
| GET/PUT/DELETE | `orders/{id}` | Detail / versioned Draft edit / Draft delete | `SD.ORDER.VIEW` / `EDIT` / `DELETE` |
| POST | `orders/{id}/submit` | Recheck credit and request SYS approval | `SD.ORDER.SUBMIT` |
| POST | `orders/{id}/cancel` | Cancel approved undelivered order | `SD.ORDER.CANCEL` |
| GET/POST | `deliveries` | Page saved outbound / create Draft | `SD.DELIVERY.VIEW` / `CREATE` |
| GET/PUT/DELETE | `deliveries/{id}` | Detail / versioned Draft edit / Draft delete | `SD.DELIVERY.VIEW` / `EDIT` / `DELETE` |
| POST | `deliveries/{id}/submit` | Request SYS approval; final callback posts INV | `SD.DELIVERY.SUBMIT` |
| GET | `price-rules/quote` | Calculate dated, bounded net prices | `SD.PRICING.VIEW` |
| GET/POST | `invoices` | Page invoices / issue against Posted delivery | `SD.INVOICE.VIEW` / `ISSUE` |
| GET | `credit-checks/{customerId}` | Display contract-derived credit decision | `SD.CREDIT.VIEW` |
| GET/POST | `returns` | Page returns / draft against Posted delivery | `SD.RETURN.VIEW` / `CREATE` |

SYS approval decisions remain on SYS approval endpoints. A delivery final-decision handler calls INV within the caller transaction; clients cannot invoke the INV posting contract through SD HTTP. Create/edit options require their corresponding action permission, not unrelated MD.VIEW. DTOs accept decimal strings/values, version and business inputs only; server derives company, actor, numbering, totals and posting data. Exact request/response DTOs and Swagger examples are fixed with implementation slices.
