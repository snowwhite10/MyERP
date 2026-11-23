# ADR-189: SD implements FI's sales-return credit source (WU-SD-008a4)

Date: 2026-11-23. Status: Accepted (WU-SD-008a4).

## Context

The backlog described 008a4 as "FI `PostSalesReturnCreditAsync`". FI already delivered that service in WU-FI-002a3b
(ADR-113) as `ISalesCreditPostingService.PostInCurrentTransactionAsync`: FI credit key lock → replay → SD return read
through the FI-owned port `IFinanceSalesReturnSource` → original FI invoice sources and receivables → cumulative per
invoice line / per invoice / open-AR checks → GL credit journal (Dr revenue, Dr output tax, Cr AR) → `ar_sales_source`
(Return), `ar_credit_allocation` rows and open-AR reduction. FI tests used a fake port; no SD implementation existed,
so with the real modules FI answered 500 `SOURCE_CONTRACT_UNAVAILABLE`. FI must never read `sal_` tables.

`sal_return` keeps the whole line amount (billed + unbilled) on lines and header; billed portions live in
`sal_return_invoice_allocation`. SQL trigger 51035 only allows Draft → Pending → Posted (or Rejected → Draft), and a
Draft can be soft-deleted.

## Decision

1. The remaining FI work of 008a4 is SD's adapter: `SalesReturnSource` (SD Infrastructure, registered by
   `SalesModule`) implements `IFinanceSalesReturnSource`. FI code is unchanged; it keeps the name
   `ISalesCreditPostingService` (no `PostSalesReturnCreditAsync` alias, to avoid two entry points).
2. The snapshot carries **billed portions only**: one FI line per saved allocation, ordered by return line number then
   allocation ID; each line's cap is the SD-saved invoice line (quantity, original/base net and tax). Header amounts
   are the sums of the allocations, never the `sal_return` header (which includes unbilled amounts). A wholly
   unbilled return therefore has zero lines and FI refuses it (422 `INVALID_AMOUNTS`); 008a5 must not call FI for it.
3. State: soft-deleted → `Deleted`; `Pending` → `Issuing` (the only postable state, because Posted is reachable only
   from Pending); Draft, Rejected and Posted as stored. Source version = header row version; fingerprint = saved
   request fingerprint; customer, date, currency, base currency and rate from the header.
4. Like `SalesInvoiceSource`, the port throws when not called on the caller's transaction of the shared DbContext,
   and returns null for unknown, non-positive or other-company IDs (FI answers 404). SYS action scope is checked by
   the SD command (008a5) before FI is called.

## Consequences

- 008a5 must read the return's current version *after* the Draft → Pending update in its transaction and pass it in
  the FI command; a stale version is FI 409 `SOURCE_MISMATCH`. It compares the FI receipt (gross, base gross,
  allocations) with the saved allocations and sets `fi_credit_note_id` to the receipt's FI source ID.
- The INV inbound update of a Pending line (`inv_movement_id`) does not touch the header version, so the INV → FI
  order inside 008a5 does not invalidate the version read after the status change.
- No schema, permission or error-code change.
