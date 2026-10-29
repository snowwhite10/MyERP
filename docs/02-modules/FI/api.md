# FI API and public contract (planned)

WU-FI-001a checkpoint: `MyERP.Modules.Finance.Contracts` now defines `ICustomerCreditService` with explicit `DbTransaction` arguments for lock acquisition and snapshot reads. `CreditSnapshot` distinguishes available zero from unavailable reasons and validates nonnegative `decimal(19,4)` values, identity and base-currency code. No implementation is registered, and no SQL table or HTTP route exists yet. Callers must not interpret this type alone as a working FI provider.

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

## WU-FI-011d planned bank reconciliation API

No route or C# interface is implemented here. Proposed FI Contracts `ImportBankStatementAsync` persists external evidence only; `GetReconciliationAsync` pairs a frozen bank revision with `GetCashBookAsync` at its committed cutoff. Client bodies never supply a journal, GL account, book balance, reviewer identity or posting source. Import response includes manifest ID, file digest and `imported/skipped/rejected` counts with row reasons. Match commands carry expected version and idempotency key. A committed replay returns `changed=false`; timeout recovery reads the manifest or reconciliation by durable key.

| Method | Proposed route | Permission | Purpose |
|---|---|---|---|
| POST | `/api/v1/cm/bank-statements/import` | `CM.RECON.IMPORT` | Validate and stage one UTF-8 CSV; report each row outcome without GL change. |
| GET | `/api/v1/cm/bank-statements/{id}` | `CM.RECON.VIEW` | Bank evidence, original digest and row reasons in company scope. |
| GET | `/api/v1/cm/reconciliations/{id}` | `CM.RECON.VIEW` | Bank/book totals, cutoff, paged dispositions and differences. |
| POST | `/api/v1/cm/reconciliations/{id}/auto-match` | `CM.RECON.MATCH` | Propose only unique one-to-one matches; report matched/skipped/ambiguous counts. |
| PUT | `/api/v1/cm/reconciliations/{id}/matches/{bankRowId}` | `CM.RECON.MATCH` | Versioned manual match or unmatch with reason. |
| POST | `/api/v1/cm/reconciliations/{id}/submit` | `CM.RECON.SUBMIT` | Freeze evidence and begin SYS review. |
| POST | `/api/v1/cm/reconciliations/{id}/approve` | `CM.RECON.APPROVE` | Separate reviewer records decision; no GL posting. |
| POST | `/api/v1/cm/reconciliations/{id}/reject` | `CM.RECON.APPROVE` | Record reason and versioned return to Draft. |
| GET | `/api/v1/cm/reconciliations/{id}/export` | `CM.RECON.EXPORT` | Export approved frozen cutoff and row dispositions. |

WU-FI-011b finalises Swagger, SYS workflow route shape and 400/401/403/404/409/422/500 examples with real SQL/HTTP tests. There is no adjustment POST in this slice; it requires a separately specified approved FI source.
## WU-FI-012d planned fixed asset contracts

No route or C# interface is implemented in this design unit. Proposed internal `CapitaliseAssetAsync`, `PostDepreciationAsync` and `DisposeAssetAsync` accept saved FI document IDs, expected versions and the caller's SQL transaction; they re-read saved data and call `IGlPostingService` with permanent typed sources. `FindCommittedAssetSourceAsync` resolves unknown outcomes by company, source type and source ID in a fresh transaction. The contract returns changed, card/source/journal IDs, saved base amounts and state; callers cannot supply GL lines or override the accounting date.

| Method | Proposed route | Permission | Purpose |
|---|---|---|---|
| GET | `/api/v1/fa/assets` | `FA.ASSET.VIEW` | Scoped paged cards, state/class/date filters. |
| GET | `/api/v1/fa/assets/{id}` | `FA.ASSET.VIEW` | Card, calculated book value and immutable posting links. |
| POST | `/api/v1/fa/assets` | `FA.ASSET.CREATE` | Save unposted acquisition Draft. |
| PUT | `/api/v1/fa/assets/{id}` | `FA.ASSET.EDIT` | Versioned Draft/Rejected edit only. |
| POST | `/api/v1/fa/assets/{id}/submit` | `FA.ASSET.CAPITALISE` | SYS final approval triggers atomic capitalisation. |
| POST | `/api/v1/fa/depreciation/preview` | `FA.DEPRECIATION.PREVIEW` | Read-only asset/period schedule and reasons. |
| POST | `/api/v1/fa/depreciation/runs` | `FA.DEPRECIATION.POST` | Per-asset idempotent posting with exact result counts. |
| GET | `/api/v1/fa/depreciation/runs/{id}` | `FA.DEPRECIATION.VIEW` | Committed results and per-asset reasons. |
| POST | `/api/v1/fa/disposals` | `FA.DISPOSAL.CREATE` | Save unposted disposal Draft. |
| POST | `/api/v1/fa/disposals/{id}/submit` | `FA.DISPOSAL.APPROVE` | SYS final approval and atomic GL disposal. |
| GET | `/api/v1/fa/disposals/{id}` | `FA.DISPOSAL.VIEW` | Saved disposal and linked journals. |

WU-FI-012e fixes Swagger, request limits, response shape, scope and 400/401/403/404/409/422/500 with traceId through real SQL/HTTP tests. No arbitrary journal POST is public.

## WU-FI-013d planned tax contracts

No route or C# interface is implemented here. Internal RecordOutputTaxAsync and RecordInputTaxAsync accept only a persisted SD/PUR source ID/version and caller transaction; FI re-reads saved components through public contracts and posts detail with GL atomically. FindCommittedTaxSourceAsync resolves unknown outcomes. PrepareTaxReturnAsync returns a frozen cutoff, source manifest, totals and named incompleteness/difference reasons; clients cannot supply tax totals or arbitrary GL lines.

| Method | Proposed route | Permission | Purpose |
|---|---|---|---|
| GET | `/api/v1/tax/returns` | `TAX.RETURN.VIEW` | Scoped period/revision list. |
| POST | `/api/v1/tax/returns/prepare` | `TAX.RETURN.PREPARE` | Freeze draft from committed sources. |
| GET | `/api/v1/tax/returns/{id}` | `TAX.RETURN.VIEW` | Revision, cutoff, totals and filing state. |
| GET | `/api/v1/tax/returns/{id}/reconciliation` | `TAX.RETURN.VIEW` | Paged source-to-GL differences. |
| POST | `/api/v1/tax/returns/{id}/submit` | `TAX.RETURN.SUBMIT` | Versioned SYS review request. |
| POST | `/api/v1/tax/returns/{id}/approve` | `TAX.RETURN.APPROVE` | Separate reviewer freezes reconciled revision. |
| POST | `/api/v1/tax/returns/{id}/filing-evidence` | `TAX.RETURN.FILE` | Save external acknowledgement and evidence. |
| GET | `/api/v1/tax/returns/{id}/export` | `TAX.RETURN.EXPORT` | Export exact frozen revision. |

WU-FI-013b defines Swagger and 400/401/403/404/409/422/500 behaviour with real SQL/HTTP tests. Filing evidence does not call a government endpoint or create a payment journal; those need separate requirements.

## WU-FI-014d planned exchange-difference contracts

No route or C# interface is implemented here. FI's planned internal `PostCollectionFxAsync` and `PostPaymentFxAsync` take a saved FI allocation ID, expected version and caller transaction; they re-read the committed exposure and cash booking, compute historical base and saved settlement base, release any overlay and post one balanced GL source. `ValueOpenExposureAsync` accepts period, currency and approved policy ID, freezes a committed manifest and rate snapshot, then posts deltas under FI locks. `FindCommittedFxSourceAsync` resolves unknown outcomes by permanent source key. Callers cannot supply GL lines, exchange difference, rate override or accounting period.

| Method | Proposed route | Permission | Purpose |
|---|---|---|---|
| GET | `/api/v1/fx/exposures` | `FX.EXPOSURE.VIEW` | Scoped, paged open original/historical/valued base exposure and saved rates. |
| GET | `/api/v1/fx/differences` | `FX.DIFFERENCE.VIEW` | Paged realised/unrealised source and Posted GL reconciliation. |
| POST | `/api/v1/fx/valuations/preview` | `FX.VALUATION.PREVIEW` | Read-only eligible, excluded and delta figures with per-source reasons. |
| POST | `/api/v1/fx/valuations` | `FX.VALUATION.POST` | Versioned SYS approval request and atomic period valuation; exact result counts. |
| GET | `/api/v1/fx/valuations/{id}` | `FX.DIFFERENCE.VIEW` | Saved revision, cutoff, rate, source manifest and GL status. |
| GET | `/api/v1/fx/differences/export` | `FX.DIFFERENCE.EXPORT` | Export one frozen authorised cutoff without mutation. |

WU-FI-014b/c must specify Swagger request/response and 400/401/403/404/409/422/500 outcomes with real SQL/HTTP and browser tests. Existing AR/AP collection and payment routes retain their ownership; they expose FX result links only after their settlement source exists.
## WU-BI-001d proposed financial report source Contracts

Design only: no C# interface or HTTP route exists. BI calls these FI Application/Contracts operations with authenticated actor context; FI checks its own permissions and SYS scope on every call. A client cannot supply posting state, balance, rate, GL account or cutoff contents.

| Operation | Input | FI authority | Output |
|---|---|---|---|
| `GetReceivableDetailsAsync` | Company, optional customer, `asOf`, include-zero-open, page size, opaque token | `AR.DETAIL.VIEW` | `PrincipalDetailPage` with AR direction. |
| `GetPayableDetailsAsync` | Company, optional supplier, `asOf`, include-zero-open, page size, opaque token | `AP.DETAIL.VIEW` | `PrincipalDetailPage` with AP direction. |
| `GetOtherIncomeExpensesAsync` | Company, inclusive `from/through`, optional kind, page size, opaque token | `FI.OTHER_REPORT.VIEW` | `OtherSourcePage` with income/expense/net totals. |

`PrincipalDetailPage`: company/base currency, `asOf`, immutable committed cutoff, full-set row count, full-set original/credit/settled/open base totals, ordered rows, next token and explicit incomplete reason. Each row: principal FI ID; saved partner ID/label; source module/type/ID/reference; Posted business date/time; journal ID; document currency/original gross/rate snapshot; saved original base gross; committed credit and settlement base sums; open base; allocation IDs/kinds/dates/base amounts. All amounts are decimal and totals cover the frozen full set, independent of page size. The optional zero-open flag changes row inclusion, not outstanding totals.

`OtherSourcePage`: company/base currency, inclusive range, immutable committed cutoff, full-set row count, positive income and expense base totals, signed net base total, rows and next token. Each row: OtherCollection or OtherPayment kind, FI source ID/reference, saved category ID/label and mapped account ID, business/posting date, positive base gross, signed base amount and journal ID. A bank-leg or payment allocation is not sufficient evidence for an other source.

The FI implementation chooses a durable, SQL-proven committed manifest covering principal, allocation, source and journal rows; an identity or timestamp high-water mark alone is invalid. Tokens bind all filters and actor scope and are authenticated. Continuation and export recheck current authority, even when the manifest remains valid. Sort detail by `(businessDate, postingTime, principalId)` and other sources by `(businessDate, postingTime, kind, sourceId)`. Empty results return zero totals and a valid cutoff. Expired manifest 409 requires explicit refresh; malformed or filter-mismatched token 400; missing permission 403; hidden identity 404; unsupported currency, >366-day other-source range, missing provenance or arithmetic mismatch 422; unexpected 500 includes traceId. WU-BI-001a must split the implementation by available FI sources and prove out-of-order commits with two SQL connections before BI exposes these reads.

## WU-FI-015d planned month/year close contracts

Design only: no C# interface or route exists. `CheckCloseReadinessAsync` takes company, period/year ID and authenticated actor; FI obtains a committed source manifest, policy version, GL/subledger differences and named blockers from public module contracts. `CloseMonthAsync`/`CloseYearAsync` accept an opaque readiness revision and expected state version, never client totals. `PostAdjustmentAsync` accepts a saved FI adjustment ID/version in the caller transaction and derives its GL lines from approved FI policy. `FindCloseOutcomeAsync` resolves unknown commit outcomes. `ReopenAsync` takes reason/evidence and approved SYS decision, never a request to edit Posted lines.

| Method | Proposed route | Permission | Purpose |
|---|---|---|---|
| GET | `/api/v1/gl/periods` | `GL.PERIOD.VIEW` | Scoped calendar, states and versions. |
| POST | `/api/v1/gl/periods/{id}/readiness` | `GL.PERIOD.VIEW` | Read-only frozen month readiness, exact blocker/source counts. |
| POST | `/api/v1/gl/periods/{id}/close` | `GL.PERIOD.CLOSE` | Versioned approval using readiness revision and independent reviewer. |
| POST | `/api/v1/gl/periods/{id}/reopen` | `GL.PERIOD.REOPEN` | Reviewed reverse-order reopen with reason/evidence. |
| POST | `/api/v1/gl/years/{id}/readiness` | `GL.YEAR.VIEW` | Read-only annual checks and carry-forward preview. |
| POST | `/api/v1/gl/years/{id}/close` | `GL.YEAR.CLOSE` | Atomic approved close and carry-forward source. |
| POST | `/api/v1/gl/years/{id}/reopen` | `GL.YEAR.REOPEN` | Reviewed compensation and linked new revision. |
| POST | `/api/v1/gl/adjustments` | `GL.ADJUSTMENT.CREATE` | Save versioned unposted FI adjustment draft. |
| POST | `/api/v1/gl/adjustments/{id}/submit` | `GL.ADJUSTMENT.APPROVE` | SYS review and final FI/GL posting. |
| GET | `/api/v1/gl/close-history` | `GL.PERIOD.VIEW` or `GL.YEAR.VIEW` | Scoped revisions, actors, blockers and linked sources. |
| GET | `/api/v1/gl/close-history/export` | `GL.CLOSE.EXPORT` | Frozen, formula-safe history export. |

Readiness response: company, period/year inclusive dates, state/version, policy version, committed cutoff and manifest digest, source counts per family, GL debit/credit and subledger differences, blocker/waiver IDs and reasons, reviewer eligibility and expiry. Close response: changed, old/new state/version, revision ID, cutoff, actor/reviewer, linked adjustment/close/carry-forward journal IDs and actual counts. An expired or changed manifest is 409; missing policy, source provider, incomplete reconciliation or downstream dependency is 422; anonymous 401, absent permission 403, hidden company/source 404, invalid fields 400, unexpected 500 with traceId. Timeout is unknown until a new state/source lookup. WU-FI-015b/c must define Swagger and prove these contracts with real SQL/HTTP and browser tests.
