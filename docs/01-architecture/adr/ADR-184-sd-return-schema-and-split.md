# ADR-184: Split WU-SD-008a; sales return tables, state triggers and Posted evidence (WU-SD-008a1)

Date: 2026-11-23. Status: Accepted (WU-SD-008a1).

## Context

WU-SD-008a (real SQL/HTTP returns: migration, authorised SYS approval, INV `SALES_RETURN` inbound in the same
transaction, FI credit for billed portions, release of SD commitment for unbilled portions, replay and rollback) is far
larger than one work unit. WU-SD-008d fixed the design (REQ_F4112.7.1 v1.1, BR-SD-043～049, SD-RETURN-001～026). Neither
the INV `SALES_RETURN` source nor FI `PostSalesReturnCreditAsync` exists yet.

## Decision

1. Split 008a into:
   - **008a1** (this unit): migration `Sales_Return` with `sal_return` (REQ_DS43.208), `sal_return_line`
     (REQ_DS43.209) and `sal_return_invoice_allocation` (REQ_DS43.210), entities/DbModel and SQL triggers; real SQL
     tests only (SD-RETURN-SCHEMA-001～008).
   - **008a2**: Draft create/edit/delete/view service — load Posted delivery lines of one order/customer, returnable
     and billed/unbilled balances under ascending delivery-/invoice-line locks (BR-SD-044), money snapshots by
     cumulative quantity with exact final remainder, request-key replay / fingerprint 409; real SQL red first.
   - **008a3**: INV public external-posting contract extension for a `SALES_RETURN` inbound (cost from the original
     outbound movement, idempotent per source line) — INV-owned, real SQL.
   - **008a4**: FI `PostSalesReturnCreditAsync` (credit note, tax reversal, AR/GL, source uniqueness) — FI-owned.
   - **008a5**: SUBMIT / SYS final decision coordinator — FI credit lock, SD locks, INV inbound, FI credit, SD
     commitment release, Posted in one transaction; Fault rolls everything back.
   - **008a6**: authorised HTTP/Swagger (`SAL.RETURN.*`), list/detail, replay/409, i18n.
   Each may be split again if it exceeds one unit.
2. Status `Draft | Pending | Rejected | Posted`. Allowed transitions (trigger `tr_sal_return_state`, THROW 51035):
   Draft→Draft/Pending, Pending→Pending/Rejected/Posted, Rejected→Rejected/Draft. A zero-step SYS submission therefore
   goes Draft→Pending→Posted inside one transaction. Posted rows cannot be updated or deleted; only a Draft may be
   deleted (soft via `is_deleted`/`deleted_at`, `ck_sal_return_deleted`; physical delete of a Draft is still allowed
   for EF cleanup), and a soft-deleted row is frozen. Number, request key, fingerprint, customer, order, currency,
   rate and creator never change; Pending/Rejected content (date, remark, totals, deletion flag) is frozen.
3. Posted evidence, checked by 51035 at the moment a row becomes Posted: at least one line; every line has its INV
   inbound movement; for each line `quantity = unbilled_quantity + Σ allocation.quantity`; `fi_credit_note_id` is set
   exactly when any billed allocation exists; header net/tax (original and base) equal the line sums.
   Consequently the 008a5 posting step must stamp `inv_movement_id` on every line while the header is still Pending,
   then update the header to Posted (two statements in one transaction; EF orders a single SaveChanges header-first).
4. Line trigger `tr_sal_return_line_state` (51036): lines are free under Draft; under Pending the only permitted
   change is stamping `inv_movement_id` once (NULL→value, all other columns unchanged; no insert/delete); frozen under
   Rejected/Posted; a line never moves to another return. Allocation trigger `tr_sal_return_invoice_allocation_state`
   (51037): billed portions change only under a Draft header.
5. Keys (unfiltered, so soft-deleted history keeps them): `(company_id,document_no)`, `(company_id,request_key)`,
   `(company_id,return_id,line_no)`, `(company_id,return_line_id,invoice_line_id)`. Filtered unique
   `(company_id,fi_credit_note_id)` and `(company_id,inv_movement_id)`. Same-company FKs (Restrict) to `sal_order`,
   `sal_delivery`, `sal_delivery_line`, `sal_order_line`, `sal_invoice`, `sal_invoice_line` (new alternate key
   `ak_sal_invoice_line_company_id_id`); header→line and line→allocation cascade (Draft edit/delete only — triggers
   protect submitted rows). INV/FI/SYS IDs are scalar values without FK.
6. Lot/serial tracking snapshots are **not** stored in 008a1. Returns of lot/serial-tracked items are refused by the
   008a2 service ("unsupported lot/serial", BR-SD-049) until a later unit adds `sal_return_line_tracking` with a new
   `REQ_DS43` ID and the INV contract supports tracked inbound for returns.
7. SQL THROW numbers: `state.json` named 51034 as next, but 51034 is already raised at runtime by FI
   `PostingServiceSupport` (posting lock not granted). To keep numbers unambiguous this unit uses 51035/51036/51037; the
   next free number is 51038.

## Consequences

- 008a2～a6 add behaviour without schema change unless tests prove a need (then a new migration and a version bump).
- The database itself refuses an incomplete Posted return (missing inbound, unbalanced portions, credit mismatch).
- Return of tracked items remains unavailable until the tracking follow-up; the UI (008b) must explain this.
