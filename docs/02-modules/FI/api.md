# FI API and public contract (planned)

WU-FI-000d exposes no endpoint. The proposed `ar` route and permission names are reserved for implementation review; Swagger descriptions, request/response examples, authorisation and HTTP tests belong to WU-FI-001b.

| Method | Proposed route | Permission | Purpose |
|---|---|---|---|
| GET | `/api/v1/ar/credit-limits` | `AR.CREDIT.VIEW` | Current-company, SYS-scope paged list with customer/status/date filters. |
| POST | `/api/v1/ar/credit-limits` | `AR.CREDIT.CREATE` | Create an approved limit interval under the credit key lock. |
| PUT | `/api/v1/ar/credit-limits/{id}` | `AR.CREDIT.EDIT` | Replace an active interval by version; preserve audit/history. |
| POST | `/api/v1/ar/credit-limits/{id}/deactivate` | `AR.CREDIT.DEACTIVATE` | Deactivate by version without reusing identity. |

Public FI Contracts proposal: `AcquireCreditLockAsync(companyId, customerId, transaction)` and `GetSnapshotAsync(companyId, customerId, evaluationDate, transaction)`; the exact C# signature may vary, but both operations must use the caller's transaction. Snapshot fields: identity, base currency, limit, posted open AR, evaluation date, timestamp/version, availability code. No cross-module entity navigation or FI document detail is exposed. A read-only snapshot outside a transaction is advisory. All writes reject stale version or overlapping dates with 409, invalid inputs with 400, inaccessible IDs with 404, missing authority with 403, unavailable reference with named 422 and unexpected errors with traceId. FI is authoritative for these results; SD maps contract failures to its own `SAL.CREDIT.*` errors.
