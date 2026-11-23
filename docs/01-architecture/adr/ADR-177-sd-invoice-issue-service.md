# ADR-177: SD invoice ISSUE service runs inside the caller's transaction (WU-SD-007a2)

Date: 2026-11-23. Status: Accepted (WU-SD-007a2).

## Context

ADR-176 split WU-SD-007a. 007a2 must save an `Issuing` invoice from Posted delivery lines (BR-SD-037～040) but must not
call FI; 007a3 adds the FI/AR posting call, the Issuing → Posted update and HTTP. A committed invoice must always be
Posted (ADR-176), and BR-SD-041 requires the FI customer credit lock to be taken *before* any SD source lock. Several
details were left open by WU-SD-007d: duplicate source rows in one request, the invoice date, the canonical payload for
request-key replay, and the lock statement shape.

## Decision

1. `SalesInvoiceIssuing.SaveIssuingAsync(input, beforeSourceLocks, token)` requires the caller's open transaction on the
   shared scoped DbContext (otherwise `InvalidOperationException`) and never commits. Order of work: validate (400) →
   `SAL.INVOICE.ISSUE` + current company (403) → replay lookup by request key → unlocked source resolution
   (404 `SAL.INVOICE.SOURCE_NOT_FOUND` for missing, foreign-company or out-of-scope sources; 422
   `SAL.INVOICE.SOURCE_MIXED` for lines of different orders) → **`beforeSourceLocks(customerId)`** (007a3 passes the FI
   credit lock here) → order row `UPDLOCK, HOLDLOCK` → each delivery line by primary key in ascending ID → replay lookup
   again → reload and check (Approved order, Posted non-deleted delivery, invoice date ≥ delivery date) → balance →
   allocation → SYS number → save `Issuing`.
2. Request lines are `{lineNo, deliveryLineId, quantity}`. Line numbers and delivery-line IDs must each be unique in one
   request (400 by field). Rejecting a repeated delivery line is stricter than BR-SD-038's "combine" wording and
   removes any aggregate-bypass path.
3. **BR-SD-055**: the invoice date may not be earlier than the business date of any source delivery (422
   `SAL.INVOICE.DATE_BEFORE_DELIVERY` with `lineNo`). Period/FI date rules stay with FI in 007a3.
4. Balance (BR-SD-038): requested ≤ saved Posted delivery-line quantity − quantity on every saved invoice line of it;
   otherwise 422 `SAL.INVOICE.BALANCE_EXCEEDED` with `lineNo` and `remaining` (never negative). Invoices are never
   deleted, so no row is excluded.
5. Allocation (BR-SD-039) is the pure `SalesInvoiceAllocation`: the cumulative component is
   `Money.Round(saved × cumulativeQty ÷ ordered)` per original/base net and tax; the portion completing the ordered
   quantity takes the saved component exactly. Inconsistent snapshots, over-billing, a negative portion or overflow are
   422 `SAL.INVOICE.SOURCE_INVALID` naming the line. Header totals are the sum of the lines.
6. Replay: the fingerprint is lower-case hex SHA-256 of `SD-INVOICE-ISSUE-v1|date|lineNo:deliveryLineId:qty(6dp)...`
   ordered by line number (key excluded). Same key + same fingerprint + order visible in the user's ISSUE scope →
   the saved invoice with `Replayed = true`, no hook call, no number. Otherwise 409 `SAL.INVOICE.REQUEST_CONFLICT`
   (also when a same-key insert races in from another order and hits the unique index).
7. Locks: the delivery-line lock is one parameterised primary-key equality seek per line. A set-based
   `JOIN OPENJSON` under `HOLDLOCK` was observed (SD-INVOICE-ISSUE-005) to take a company-wide key-range lock and block
   issuers of other orders. Balance and prior-billing reads use `FORCESEEK` on the
   `(company_id, delivery_line_id|order_line_id, invoice_id)` indexes under Read Committed.
8. Number rule `SAL.INVOICE` is seeded as `SI` + year-month + 4 digits (ADR-166 pattern).

## Consequences

- 007a3 only adds: the outer transaction, the credit lock in the hook, `ISalesInvoicePostingService` call, the
  Issuing → Posted update with FI IDs, SD commitment release, rollback on Rejected/Fault and HTTP/Swagger.
- Per-line lock statements cost one round trip per line (≤ 1000); acceptable for ISSUE and revisited only if measured.
