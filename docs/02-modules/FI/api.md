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

## WU-FI-005d planned sales collection and settlement API

These routes and FI Contracts signatures are proposed, not implemented. `ApproveSalesCollectionAsync(savedReceiptId, version, fingerprint, transaction)` and `ApproveCollectionSettlementAsync(savedSettlementId, version, fingerprint, transaction)` join the SYS caller's existing transaction after the credit key lock and verify FI-saved records. `FindCommittedCollectionSourceAsync(companyId, type, id)` resolves timeout/replay in a fresh transaction. Results include company/customer, saved version/fingerprint, receipt/settlement/journal IDs, original/allocated/unapplied base amounts and `changed`. Read-only `GetOpenArSnapshotAsync` exposes the committed sum for the credit contract, without SD table access.

| Method | Proposed route | Permission | Purpose |
|---|---|---|---|
| GET | `/api/v1/ar/sales-collections` | `AR.COLLECTION.VIEW` | Company/SYS-scope paged receipt list and filters. |
| GET | `/api/v1/ar/sales-collections/{id}` | `AR.COLLECTION.VIEW` | Receipt detail, immutable source and unapplied amount. |
| POST | `/api/v1/ar/sales-collections` | `AR.COLLECTION.CREATE` | Save Draft; no GL or AR exposure change. |
| PUT | `/api/v1/ar/sales-collections/{id}` | `AR.COLLECTION.EDIT` | Versioned Draft/Rejected edit. |
| POST | `/api/v1/ar/sales-collections/{id}/submit` | `AR.COLLECTION.SUBMIT` | Enter SYS workflow; final approval invokes FI contract. |
| POST | `/api/v1/ar/sales-collections/{id}/reject` | `AR.COLLECTION.APPROVE` | Authorised SYS rejection with reason. |
| DELETE | `/api/v1/ar/sales-collections/{id}` | `AR.COLLECTION.DELETE` | Soft delete unposted Draft/Rejected only. |
| GET | `/api/v1/ar/collection-settlements` | `AR.SETTLEMENT.VIEW` | Paged settlement list with receipt/invoice filters. |
| GET | `/api/v1/ar/collection-settlements/{id}` | `AR.SETTLEMENT.VIEW` | Ordered allocations and saved receipt/AR balances. |
| POST | `/api/v1/ar/collection-settlements` | `AR.SETTLEMENT.CREATE` | Save Draft one-receipt allocation. |
| PUT | `/api/v1/ar/collection-settlements/{id}` | `AR.SETTLEMENT.EDIT` | Versioned Draft/Rejected edit. |
| POST | `/api/v1/ar/collection-settlements/{id}/submit` | `AR.SETTLEMENT.SUBMIT` | SYS final approval atomically posts GL and reduces AR. |
| POST | `/api/v1/ar/collection-settlements/{id}/reject` | `AR.SETTLEMENT.APPROVE` | Authorised SYS rejection with reason. |
| DELETE | `/api/v1/ar/collection-settlements/{id}` | `AR.SETTLEMENT.DELETE` | Soft delete unposted Draft/Rejected only. |

Final approval is a SYS workflow callback guarded by the corresponding APPROVE authority, never an arbitrary posting body. WU-FI-005b settles exact route and workflow shape, Swagger, error codes and authorised SQL/HTTP tests; 005c handles the UI. No API exists in this design unit.

## WU-FI-006d planned purchase payment and AP settlement API

No route or C# interface is implemented here. Proposed FI Contracts operations `ApprovePurchasePaymentAsync(savedPaymentId, version, fingerprint, transaction)` and `ApprovePaymentSettlementAsync(savedSettlementId, version, fingerprint, transaction)` join the SYS caller's transaction, verify FI-saved documents and call `IGlPostingService`. `FindCommittedPaymentSourceAsync(companyId, type, id)` resolves an unknown timeout with a fresh transaction. The result gives source/payment/settlement/journal IDs, company/supplier, saved version/fingerprint, original/allocated/unapplied payment and open payable base amounts, and `changed`. `GetOpenApSnapshotAsync` exposes committed FI payables less committed allocations, without reading PUR tables.

| Method | Proposed route | Permission | Purpose |
|---|---|---|---|
| GET | `/api/v1/ap/purchase-payments` | `AP.PAYMENT.VIEW` | Company/SYS-scope paged payment list and filters. |
| GET | `/api/v1/ap/purchase-payments/{id}` | `AP.PAYMENT.VIEW` | Saved payment, allocations, unapplied balance and journal. |
| POST | `/api/v1/ap/purchase-payments` | `AP.PAYMENT.CREATE` | Save Draft; no GL or AP change. |
| PUT | `/api/v1/ap/purchase-payments/{id}` | `AP.PAYMENT.EDIT` | Versioned Draft/Rejected edit. |
| POST | `/api/v1/ap/purchase-payments/{id}/submit` | `AP.PAYMENT.SUBMIT` | Enter SYS workflow; final approval invokes FI contract. |
| DELETE | `/api/v1/ap/purchase-payments/{id}` | `AP.PAYMENT.DELETE` | Soft delete unposted Draft/Rejected only. |
| GET | `/api/v1/ap/payment-settlements` | `AP.SETTLEMENT.VIEW` | Paged settlement list by supplier, payment or payable. |
| GET | `/api/v1/ap/payment-settlements/{id}` | `AP.SETTLEMENT.VIEW` | Ordered allocations and saved balances. |
| POST | `/api/v1/ap/payment-settlements` | `AP.SETTLEMENT.CREATE` | Save Draft one-payment allocation. |
| PUT | `/api/v1/ap/payment-settlements/{id}` | `AP.SETTLEMENT.EDIT` | Versioned Draft/Rejected edit. |
| POST | `/api/v1/ap/payment-settlements/{id}/submit` | `AP.SETTLEMENT.SUBMIT` | Enter SYS workflow; final approval atomically posts GL and reduces AP. |
| DELETE | `/api/v1/ap/payment-settlements/{id}` | `AP.SETTLEMENT.DELETE` | Soft delete unposted Draft/Rejected only. |

SYS final decision endpoints remain SYS-owned and require the relevant FI APPROVE permission through the final callback; rejection saves its reason without posting. There is no public arbitrary posting route. WU-FI-006b fixes route shape, Swagger and 401/403/404/409/422/500 examples against authorised real SQL/HTTP; 006c implements UI. The draft request cannot specify posting journal, account, actor or source amount. No API exists from this design unit.

## WU-FI-007d planned other collection API

No endpoint or C# interface is implemented here. Proposed FI Contracts operation `ApproveOtherCollectionAsync(savedCollectionId, version, fingerprint, transaction)` is callable only from SYS final approval in the caller's transaction. It re-reads the FI-owned saved document, validates MD category/bank and FI mapping, then calls `IGlPostingService`. `FindCommittedOtherCollectionAsync(companyId, collectionId)` resolves unknown timeout by permanent source key in a fresh transaction. Result includes document/source/journal IDs, company, saved category/bank/reference/date, base amount, version/fingerprint and `changed`; it contains no AR allocation or credit delta.

| Method | Proposed route | Permission | Purpose |
|---|---|---|---|
| GET | `/api/v1/ar/other-collections` | `AR.OTHER_COLLECTION.VIEW` | Company/SYS-scope paged list by state, date, category and bank. |
| GET | `/api/v1/ar/other-collections/{id}` | `AR.OTHER_COLLECTION.VIEW` | Saved document, income mapping snapshot and committed journal; hidden ID is 404. |
| POST | `/api/v1/ar/other-collections` | `AR.OTHER_COLLECTION.CREATE` | Save Draft, without GL or AR effect. |
| PUT | `/api/v1/ar/other-collections/{id}` | `AR.OTHER_COLLECTION.EDIT` | Versioned Draft/Rejected edit. |
| POST | `/api/v1/ar/other-collections/{id}/submit` | `AR.OTHER_COLLECTION.SUBMIT` | Enter SYS workflow; final approval invokes the internal FI contract. |
| DELETE | `/api/v1/ar/other-collections/{id}` | `AR.OTHER_COLLECTION.DELETE` | Soft delete unposted Draft/Rejected only. |

SYS owns final approve/reject routes; the final callback requires `AR.OTHER_COLLECTION.APPROVE` and saves the rejection reason. There is no public arbitrary GL posting route. WU-FI-007b fixes Swagger and error examples (400/401/403/404/409/422/500 with traceId), scope and real SQL/HTTP proof. Request bodies cannot supply a journal, account, actor or AR target.

## WU-FI-008d planned other payment API

No endpoint or C# interface is implemented here. Proposed FI Contracts operation `ApproveOtherPaymentAsync(savedPaymentId, version, fingerprint, transaction)` is callable only from SYS final approval in the caller transaction. It re-reads the saved FI document, validates MD category/bank and FI mapping, then calls `IGlPostingService`. `FindCommittedOtherPaymentAsync(companyId, paymentId)` resolves an unknown timeout by permanent source key in a fresh transaction. The result contains payment/source/journal IDs, company, category/bank/reference/date, base amount, saved version/fingerprint and `changed`, with no AP allocation or payable delta.

| Method | Proposed route | Permission | Purpose |
|---|---|---|---|
| GET | `/api/v1/ap/other-payments` | `AP.OTHER_PAYMENT.VIEW` | Company/SYS-scope paged list by state, date, category and bank. |
| GET | `/api/v1/ap/other-payments/{id}` | `AP.OTHER_PAYMENT.VIEW` | Saved payment, expense mapping snapshot and committed journal; hidden ID is 404. |
| POST | `/api/v1/ap/other-payments` | `AP.OTHER_PAYMENT.CREATE` | Save Draft without bank, GL or AP effect. |
| PUT | `/api/v1/ap/other-payments/{id}` | `AP.OTHER_PAYMENT.EDIT` | Versioned Draft/Rejected edit. |
| POST | `/api/v1/ap/other-payments/{id}/submit` | `AP.OTHER_PAYMENT.SUBMIT` | Enter SYS workflow; final approval invokes internal FI posting. |
| DELETE | `/api/v1/ap/other-payments/{id}` | `AP.OTHER_PAYMENT.DELETE` | Soft delete unposted Draft/Rejected only. |

SYS owns final approve/reject routes; final callback requires `AP.OTHER_PAYMENT.APPROVE` and saves a rejection reason. There is no public arbitrary GL posting route. WU-FI-008b fixes Swagger, scope and 400/401/403/404/409/422/500 examples with real SQL/HTTP. Request bodies cannot supply journal, account, actor, AP target or amount different from the saved source.

## WU-FI-009d planned statement source contract

Proposed FI Contracts operations `GetCustomerStatementAsync(companyId, customerId, from, through, pageToken)` and `GetSupplierStatementAsync(companyId, supplierId, from, through, pageToken)` return a scoped, consistent read of committed FI movements. Response fields: partner, base currency, inclusive range, cutoff, opening, period charges, period credits/allocations, closing, count, ordered lines with source and running balance, and next token. A token binds filters and cutoff; changed filters, expiry or tampering require a fresh request. BI owns the public Client/Supplier Statement routes, export and Swagger for REQ_F4117.5.4–5. FI owns `AR.STATEMENT.VIEW`/`AP.STATEMENT.VIEW` data authorization and cannot be bypassed by BI. 400 covers malformed range/token, 401/403 authority, 404 hidden partner, 409 expired cutoff, 422 unsupported currency or overflow and 500 with traceId unexpected failures. No route or C# interface exists from this design unit.

## WU-FI-010d planned cash-book source contract

Proposed FI Contracts operation `GetCashBookAsync(companyId, fundAccountId, from, through, pageToken)` returns a company/SYS-scope, base-currency, consistent read of Posted cash-side lines. Response fields: saved fund-account identity/type, currency, inclusive range, cutoff, opening, period debit/credit totals, closing, count, ordered lines with source/journal/line IDs, running balance and next token. Token binds every filter and cutoff; a fresh request is required after expiry. FI checks `CM.CASH_BOOK.VIEW` and uses MD's public fund-account lookup. BI owns the public Cash & Bank Report route, Swagger and export for REQ_F4117.5.1; it cannot bypass FI authorization or read GL/MD tables. 400 covers malformed range/token, 401/403 authority, 404 hidden account, 409 expired cutoff, 422 unsupported currency or mapping/overflow, and unexpected 500 carries traceId. No route or C# interface exists from this design unit. Bank-statement import, matching and adjustment commands are outside this read API and belong to WU-FI-011d.
