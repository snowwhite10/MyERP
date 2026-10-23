# SD API baseline (WU-SD-000; endpoints not implemented)

All routes use `/api/v1/sal/` to match the `sal_` module prefix; Swagger groups `SD - Quotations`, `SD - Orders`, `SD - Deliveries`, `SD - Pricing`, `SD - Invoices`, `SD - Credit` and `SD - Returns`. Every route requires the current company and its own `SAL.<RESOURCE>.<ACTION>` permission. Read and write scope follow SYS action data scope before loading a document. Lists use stable server pagination, whitelist sort and date/status/customer filters. 401/403/404/400/409/422/500 use ProblemDetails with `errorCode` and `traceId`.

| Method | Proposed path | Purpose | Permission |
|---|---|---|---|
| GET/POST | `quotations` | Page saved quotations / create Draft | `SAL.QUOTATION.VIEW` / `CREATE` |
| GET/PUT/DELETE | `quotations/{id}` | Detail / versioned Draft edit / Draft delete | `SAL.QUOTATION.VIEW` / `EDIT` / `DELETE` |
| POST | `quotations/{id}/submit` | Request SYS approval | `SAL.QUOTATION.SUBMIT` |
| POST | `quotations/{id}/orders` | Convert approved, unexpired source once | `SAL.ORDER.CREATE` |
| GET/POST | `orders` | Page saved orders / create Draft | `SAL.ORDER.VIEW` / `CREATE` |
| GET/PUT/DELETE | `orders/{id}` | Detail / versioned Draft edit / Draft delete | `SAL.ORDER.VIEW` / `EDIT` / `DELETE` |
| POST | `orders/{id}/submit` | Recheck credit and request SYS approval | `SAL.ORDER.SUBMIT` |
| POST | `orders/{id}/cancel` | Cancel approved undelivered order | `SAL.ORDER.CANCEL` |
| GET/POST | `deliveries` | Page saved outbound / create Draft | `SAL.DELIVERY.VIEW` / `CREATE` |
| GET/PUT/DELETE | `deliveries/{id}` | Detail / versioned Draft edit / Draft delete | `SAL.DELIVERY.VIEW` / `EDIT` / `DELETE` |
| POST | `deliveries/{id}/submit` | Request SYS approval; final callback posts INV | `SAL.DELIVERY.SUBMIT` |
| GET | `price-rules/quote` | Calculate dated, bounded net prices | `SAL.PRICING.VIEW` |
| GET/POST | `invoices` | Page invoices / issue against Posted delivery | `SAL.INVOICE.VIEW` / `ISSUE` |
| GET | `credit-checks/{customerId}` | Display contract-derived credit decision | `SAL.CREDIT.VIEW` |
| GET/POST | `returns` | Page returns / draft against Posted delivery | `SAL.RETURN.VIEW` / `CREATE` |

SYS approval decisions remain on SYS approval endpoints. A delivery final-decision handler calls INV within the caller transaction; clients cannot invoke the INV posting contract through SD HTTP. Create/edit options require their corresponding action permission, not unrelated MD.VIEW. DTOs accept decimal strings/values, version and business inputs only; server derives company, actor, numbering, totals and posting data. Exact request/response DTOs and Swagger examples are fixed with implementation slices.

WU-SD-001 foundation: `SalesModule` is registered and declares the `SAL.*` action catalog; no SD HTTP endpoint exists yet. The permission prefix follows `ErpModuleCatalog` (`sal`), while SD remains the module code. `SalesActionAccess` obtains SYS scope for each requested action and rejects absent permission or mismatched company. Document queries must apply that filter before lookup; MD options and SQL/HTTP validation remain before document endpoints are exposed.

The `ISalesSelectionOptions` MD public contract now supplies paged approved customers and active items plus active currencies and business-date output tax rates (`Output` or `Both`). It does not perform SD authorization: each future form endpoint must first require its exact `SAL.*.CREATE` or `EDIT` action, then call MD. No such HTTP endpoint is exposed yet. Real SQL and HTTP acceptance remain blocked by the stopped SQL service.
