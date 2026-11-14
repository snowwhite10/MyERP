# ADR-122 — Collection write-off posting service: lock order, replay under the credit key, per-line reasons (WU-FI-005a2c)

Date: 2026-11-14. Status: Accepted (WU-FI-005a2c).

## Context

WU-FI-005a2c posts an approved collection write-off (receipt-to-invoice settlement, BR-FI-035/036/037/038/040): debit unapplied-cash clearing, credit AR control, save `ar_collection_settlement` and `ar_collection_allocation`, and reduce `ar_collection_receipt.unapplied_amount` and each `ar_posted_receivable.open_base_amount`, all inside the SYS final-approval transaction. Open questions:

1. BR-FI-036 fixes the order "credit key → receipt key → receivable IDs ascending → settlement key → GL key", but the receipt and receivable IDs are only known after the document is read, and the replay check should not re-read the document (ADR-121 practice).
2. How a settlement document refers to its receipt: by the FI receipt document ID or by the `ar_collection_receipt` row ID.
3. How BR-FI-040 ("an invalid line fails the whole settlement with its row reason") is reported.

## Decision

- **FI-owned document port** `IFinanceCollectionWriteOffSource` (FI Application) returns `FinanceCollectionWriteOffSnapshot` through the caller's transaction; the real document is built in 005b. Without it the service returns 500 `FI.COLLECTION_WRITEOFF.SOURCE_CONTRACT_UNAVAILABLE`. Only `Approved` is postable; `Posted` only as replay.
- **Lock order:** credit key `ar_credit:{company}:{customer}` → FI replay check → document read → receipt key `ar_collection:{company}:{receipt document}` (the same key receipt posting takes, so a write-off waits for an in-flight receipt post) → `ar_receivable:{company}:{id}` for each receivable in ascending ID order → settlement key `ar_collection_writeoff:{company}:{settlement}` → re-read receipt and receivables → GL key. The replay check sits before the document read and under the credit key only: every write-off and credit note for one customer already serializes on the credit key, and a claim with another customer fails against the saved row (409) or the document (409), with the unique source index as the last guard. The receivable keys are new; today credit notes reach the same receivables only under the same credit key, so they add safety for future non-customer-scoped writers without changing behaviour. Lock timeouts (SQL 51034/1222) are 409 `LOCK_TIMEOUT`.
- **Receipt reference** is the FI receipt *document* ID (the receipt's FI/GL source ID), resolved to the `ar_collection_receipt` row under its key. No row → 422 `RECEIPT_NOT_POSTED` (Draft, Rejected, deleted or rolled-back receipt). Another customer/currency or a receipt dated after the settlement → 422 `RECEIPT_MISMATCH`; total above current unapplied → 422 `RECEIPT_INSUFFICIENT`.
- **Lines:** 1–1000 lines (REQ_F4116.5.2), positive unique receivable IDs, positive DECIMAL(19,4) amounts summing exactly to the total; otherwise 422 `INVALID_SOURCE`. Each line is then checked against the re-read receivable; **every** failing line is returned in `LineErrors` (1-based document line, receivable ID, reason: `RECEIVABLE_NOT_FOUND`, `RECEIVABLE_MISMATCH` — other customer/currency or not an `Invoice` receivable, `RECEIVABLE_AFTER_SETTLEMENT`, `EXCEEDS_OPEN`) under 422 `ALLOCATION_INVALID`, and nothing is written. Returning all lines rather than the first lets the 005b UI show every row reason in one round trip.
- **Journal:** Dr mapped unapplied cash / Cr mapped AR control for the total, GL source `FI/CollectionWriteOff` with the shared map row version (ADR-120). One journal per settlement, not per line.
- **Writes** happen only after GL accepted: tracked rows are loaded at write time, so a rejection leaves nothing tracked in the caller's context. Any write failure is a 500 `WRITE_FAILED` fault that rolls back the caller's transaction, including the GL journal.
- **Shared plumbing:** `SingleOrNull`, caller rollback and the applock helper move to `PostingServiceSupport`, used by all five FI posting services (the duplication noted in the 005a2b review).

## Consequences

- The read side (allocated/unapplied display), SYS workflow and HTTP surface are 005b; two-connection races, timeout and caller rollback after posting are 005a3.
- A receivable from another company is reported as `RECEIVABLE_NOT_FOUND`, never as a mismatch, so IDs from other companies are not disclosed.
- No migration, endpoint or front-end change.
