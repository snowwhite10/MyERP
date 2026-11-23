# ADR-178: SD invoice ISSUE command posts to FI in one transaction; commitment release is derived (WU-SD-007a3a)

Date: 2026-11-23. Status: Accepted (WU-SD-007a3a).

## Context

WU-SD-007a3 combined the ISSUE coordinator (FI posting, Posted update, credit commitment release, rollback) with the
authorised HTTP surface, Swagger and i18n. Together they exceed one 30–90 minute work unit. It was also undecided how
"releasing the SD credit commitment" (BR-SD-041, FI-SALES-003) is stored, how FI results map to SD errors, and how a
non-ErpException 500 carries an error code.

## Decision

1. Split WU-SD-007a3 into **007a3a** (the `SalesInvoiceIssueService` command and real-SQL proof) and **007a3b**
   (`POST/GET /api/v1/sal/invoices`, Swagger, 500 mapping, i18n, authorised HTTP tests). The parent counts as done only
   when both are.
2. `SalesInvoiceIssueService.IssueAsync` owns a Read Committed transaction (an outer transaction is a programming error):
   `SaveIssuingAsync` with the hook `ICustomerCreditService.AcquireCreditLockAsync` → replay returns at once (no FI
   call, nothing written) → `ISalesInvoicePostingService.PostInCurrentTransactionAsync(company, invoiceId, customerId,
   SalesInvoice.SourceRevision, RequestFingerprint)` → only `Posted` continues → the receipt must match company,
   invoice, customer, version, fingerprint, currency + original gross, base currency + base gross and carry positive FI
   IDs → `Status = Posted`, `PostedAt = IClock.LocalNow`, FI source/receivable/journal IDs → commit. Any failure rolls
   back (FI Fault has already rolled back) and clears the change tracker.
3. FI result mapping: Rejected 404 → `NotFoundException`, 409 → `ConflictException`, 422 → `BusinessRuleException`
   (with `journalLineNo` when FI names a line), each keeping FI's code. Rejected 400/500 (SD built a bad command or FI
   is misconfigured), Rejected without a code, Fault and Unchanged (impossible for a fresh invoice) throw
   `SalesInvoicePostingException(errorCode)`, a plain exception that 007a3b maps to HTTP 500 with the code.
4. Commitment release is **derived**, not stored: `SalesOrderCreditCheck.AssessAsync` counts each active Approved order
   as `max(0, base_gross − Σ base_gross of its Posted invoices)` (FORCESEEK on `ix_sal_invoice_company_id_order_id_id`).
   It is read under the same FI credit lock as FI open AR, so the ISSUE commit moves exactly the invoice's base gross
   from commitment to AR and total exposure is unchanged. No counter can drift and rollback needs no compensation.

## Consequences

- Order submit, SYS final decision and the read-only credit check all see released commitments without code changes.
- Over-invoicing of an order is impossible (balance per delivery line), so the `max(0, …)` cap is a safety net only.
- Returns (WU-SD-008a) must extend the same derivation (uninvoiced returns give commitment back; invoiced ones go
  through an FI credit note).
