# FI API and public contract (planned)

WU-FI-000d exposes no endpoint. The proposed `ar` route and permission names are reserved for implementation review; Swagger descriptions, request/response examples, authorisation and HTTP tests belong to WU-FI-001b.

| Method | Proposed route | Permission | Purpose |
|---|---|---|---|
| GET | `/api/v1/ar/credit-limits` | `AR.CREDIT.VIEW` | Current-company, SYS-scope paged list with customer/status/date filters. |
| POST | `/api/v1/ar/credit-limits` | `AR.CREDIT.CREATE` | Create an approved limit interval under the credit key lock. |
| PUT | `/api/v1/ar/credit-limits/{id}` | `AR.CREDIT.EDIT` | Replace an active interval by version; preserve audit/history. |
| POST | `/api/v1/ar/credit-limits/{id}/deactivate` | `AR.CREDIT.DEACTIVATE` | Deactivate by version without reusing identity. |

Public FI Contracts proposal: `AcquireCreditLockAsync(companyId, customerId, transaction)` and `GetSnapshotAsync(companyId, customerId, evaluationDate, transaction)`; the exact C# signature may vary, but both operations must use the caller's transaction. Snapshot fields: identity, base currency, limit, posted open AR, evaluation date, timestamp/version, availability code. No cross-module entity navigation or FI document detail is exposed. A read-only snapshot outside a transaction is advisory. All writes reject stale version or overlapping dates with 409, invalid inputs with 400, inaccessible IDs with 404, missing authority with 403, unavailable reference with named 422 and unexpected errors with traceId. FI is authoritative for these results; SD maps contract failures to its own `SAL.CREDIT.*` errors.

## WU-FI-002d planned internal source contract and read API

No endpoint or C# interface is implemented by this design unit. `IFinancialSalesSource` is an FI Contracts proposal with `PostSalesInvoiceAsync(savedInvoiceId, sourceVersion, fingerprint, transaction)`, `PostSalesReturnCreditAsync(savedReturnId, sourceVersion, fingerprint, transaction)` and `FindCommittedSourceAsync(companyId, sourceType, sourceId)`. Commands require the caller's existing SQL transaction and FI credit lock. FI fetches the saved SD snapshot through an SD Contracts read interface, verifies it, and returns `{sourceType, sourceId, postingId, journalId, companyId, customerId, currency, originalGross, baseGross, changed}`. Credit result also returns original invoice posting IDs and allocated amounts. An identical committed replay has `changed=false`; it cannot emit a new-save success message. Signatures may be refined in implementation, but source identity, transaction participation and result comparison are mandatory.

| Method | Proposed route | Permission | Purpose |
|---|---|---|---|
| GET | `/api/v1/ar/sales-sources` | `AR.SALES_SOURCE.VIEW` | Paged, company/SYS-scope list of committed invoice and credit sources; filter by customer, source type, date and number. |
| GET | `/api/v1/ar/sales-sources/{id}` | `AR.SALES_SOURCE.VIEW` | Immutable source, journal reference, original/open amounts and linked credit allocations; hidden IDs return 404. |

FI does not expose a public posting HTTP route: only SD's authorised ISSUE or final return decision calls the internal transaction contract. The read API belongs to WU-FI-002c; Swagger and HTTP tests are pending. Invalid fields return 400, duplicate/source-version conflicts 409, unavailable source, account, credit or mapping 422, inaccessible ID 404, missing permission 403 and unexpected 500 includes traceId. Unknown timeout must resolve by stable source key before a retry.

## WU-FI-003d planned purchase source contract and read API

No endpoint or C# interface is implemented here. Proposed FI Contracts operation: `PostPurchasePayableAsync(savedPurchaseInvoiceId, sourceVersion, fingerprint, transaction)` plus `FindCommittedPurchaseSourceAsync(companyId, purchaseInvoiceId)`. The implementation may refine signatures, but the caller's existing SQL transaction, saved PUR verification, stable source identity and returned result comparison are mandatory. FI returns `{sourceId, sourceVersion, companyId, supplierId, currency, originalGross, baseGross, payableId, journalId, changed}`. The PUR caller must hold order/receipt/invoice locks before invocation; FI never reads `pur_` or writes `inv_` tables.

| Method | Proposed route | Permission | Purpose |
|---|---|---|---|
| GET | `/api/v1/ap/purchase-sources` | `AP.PURCHASE_SOURCE.VIEW` | Stable paged company/SYS-scope list, filtered by supplier, source number, posting date and state. |
| GET | `/api/v1/ap/purchase-sources/{id}` | `AP.PURCHASE_SOURCE.VIEW` | Immutable source, saved amount components, journal reference and original/open payable; inaccessible IDs return 404. |

There is no public FI posting HTTP route. Only the authorised PUR POST action calls this internal contract. Swagger, scope, error examples and HTTP verification belong to WU-FI-003c. A source read after timeout distinguishes committed, absent and still in-flight outcomes; only committed identical data can be displayed as Posted. Invalid format 400, absent/hidden 404, missing authority 403, version/fingerprint conflict 409, unavailable mapping or source 422, unexpected 500 with traceId.

## WU-FI-004d planned GL posting contract and read API

No C# interface or endpoint exists from this design unit. Proposed FI Contracts operation `IGlPostingService.PostAsync(savedSourceKey, sourceVersion, fingerprint, transaction)` accepts only an authenticated FI source adapter, joins the caller's existing transaction, verifies the owning module's saved source and FI-owned account mapping, then returns `{companyId, sourceKey, journalId, periodId, baseCurrency, debitTotal, creditTotal, changed}`. Source adapters for AR/AP do not pass arbitrary client journal lines. `FindCommittedAsync(companyId, sourceKey)` reads a committed result after an unknown timeout; absent or in-flight is not success. The implementation may refine signatures while retaining these invariants.

| Method | Proposed route | Permission | Purpose |
|---|---|---|---|
| GET | `/api/v1/gl/journals` | `GL.JOURNAL.VIEW` | Stable paged company/SYS-scope list filtered by source, period, date and account. |
| GET | `/api/v1/gl/journals/{id}` | `GL.JOURNAL.VIEW` | Immutable header, balanced entries, saved account labels and source; inaccessible IDs return 404. |
| GET | `/api/v1/gl/periods` | `GL.PERIOD.VIEW` | Company-scoped period list and state. |
| POST | `/api/v1/gl/periods` | `GL.PERIOD.CREATE` | Create nonoverlapping Open period under company lock. |
| PUT | `/api/v1/gl/periods/{id}` | `GL.PERIOD.EDIT` | Versioned dates/state edit; posted history cannot be excluded. |

No public journal POST, PUT or DELETE exists. Period closure/reopen permissions and workflow will be fixed in WU-FI-004b; until then only FI's internal transaction path may post. Swagger, 401/403/404/409/422/500 responses, timeout recovery and authorised SQL/HTTP proof belong to WU-FI-004b. A 422 for absent/Closed period or invalid MD account includes a named reason; a 409 covers stale version/fingerprint or concurrent period overlap.
