# ADR-176: Split WU-SD-007a; invoice tables, Issuing state and SD's FI invoice-source port (WU-SD-007a1)

Date: 2026-11-23. Status: Accepted (WU-SD-007a1).

## Context

WU-SD-007a (issue invoices from Posted deliveries, FI/AR receivable posting in the same transaction, idempotent replay,
authorised HTTP) is too large for one work unit. WU-SD-007d fixed the design (BR-SD-037～042, SD-INVOICE-001～024) and
WU-FI-002a2b already delivered the FI side (`ISalesInvoicePostingService`) against an FI-owned port
`IFinanceSalesInvoiceSource`, tested with a fake. SD must now persist the invoice and answer that port for real.

FI reads the SD source *inside* the SD ISSUE transaction (BR-FI-013 lock order), so the invoice row must exist before FI
posts, but a committed invoice must never exist without its FI receivable (BR-SD-040).

## Decision

1. Split 007a into:
   - **007a1** (this unit): migration `Sales_Invoice` with `sal_invoice` (REQ_DS43.206) and `sal_invoice_line`
     (REQ_DS43.207), entity/DbModel, SQL triggers, and `SalesInvoiceSource` implementing `IFinanceSalesInvoiceSource`;
     real SQL tests only.
   - **007a2**: ISSUE service — lock order/delivery sources, invoiceable remaining quantity per Posted delivery line,
     allocation of order-line snapshot amounts (last-portion remainder), request-key replay; real SQL red first.
   - **007a3**: call `ISalesInvoicePostingService` in the same transaction, mark Posted with FI IDs, release the SD
     credit commitment (FI-SALES-003), whole-unit rollback on FI Fault, authorised HTTP/Swagger and replay/409.
2. Status is `Issuing | Posted` only. `Issuing` exists only inside the ISSUE transaction (saved before the FI call so FI
   can read it); the same transaction updates it to `Posted` with `posted_at`, `fi_sales_source_id`,
   `fi_receivable_id`, `fi_journal_id` (all required when Posted, all NULL when Issuing — `ck_sal_invoice_posted`).
   There is no Draft and no delete.
3. Triggers: `tr_sal_invoice_immutable` refuses any UPDATE/DELETE of a Posted header and any change of saved content
   while Issuing (THROW 51032); `tr_sal_invoice_line_immutable` refuses line UPDATE, and INSERT/DELETE under a Posted
   header (THROW 51033). Therefore an invoice can only reach Posted via Issuing; an Issuing invoice can be cleaned up
   inside a failed transaction (in practice the rollback does it).
4. Unique per company: document number, request key, and filtered `fi_sales_source_id`. Source references to SD order,
   order line, delivery and delivery line are same-company FKs with Restrict (alternate key
   `ak_sal_order_line_company_id_id` added as principal). FI IDs are scalar values without FK (no cross-module FK).
5. `SalesInvoiceSource` reads only `sal_invoice`, requires the caller's transaction on the shared scoped DbContext
   (otherwise `InvalidOperationException`), returns null for missing/foreign-company IDs, and reports the saved totals
   and fingerprint with constant `SourceRevision = 1` (content never changes after save, although the row `version`
   advances when Posted). SYS action scope is enforced by the 007a3 ISSUE command, not the port.
6. SQL trigger THROW numbers 51032 and 51033 are allocated; the next is 51034.

## Consequences

- FI posting can now run against a real SD source; 007a2/007a3 add behaviour without further schema change unless
  tests prove a need (any change gets a new migration and a REQ_DS43 version bump).
- Every invoice row is permanent history; corrections go through returns/credit notes (008a), never edits.
