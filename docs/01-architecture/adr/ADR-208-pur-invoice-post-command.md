# ADR-208: PUR purchase invoice POST command (WU-PUR-008b4)

Date: 2026-11-27T04:36:46+08:00

Status: Accepted

## Context

008b1～b3 (ADR-205～207) built the FI snapshot mapping, the order exchange-rate snapshot and the real
`IFinancePurchaseInvoiceSource`. FI `IPurchaseInvoicePostingService` (WU-FI-003a2b) writes the FI purchase source,
payable and GL journal inside the caller's transaction and returns a receipt. PUR still needed the user command that
turns a Matched invoice into Posted, moves the receipt reservation to invoiced quantity and remembers the FI result.

## Decision

1. `POST /api/v1/pur/invoices/{id}/post?version=N`, new permission `PUR.INVOICE.POST`, company data scope only
   (like MATCH/RESOLVE: invoices have no branch/department/owner). Response `PurchaseInvoicePostSaved`
   (`id`, `version`, `status`, `replayed`, `fiPurchaseSourceId`, `fiPayableId`, `fiJournalId`, `postedAt`).
2. **Transaction and lock order.** Supplier read outside the transaction (as MATCH/RESOLVE), then one Serializable
   transaction: supplier application lock (shared with Draft/MATCH/RESOLVE, 15 s → 409
   `PUR.INVOICE.DRAFT_LOCK_UNAVAILABLE`) → invoice header → order header → receipt lines by ascending ID → PUR
   preflight (`PurchaseInvoicePostAssembly.Build`, accepted differences via `PurchaseInvoiceFinanceEvidence.Accepted`)
   → order rate snapshot (`IPurchaseInvoiceExchangeSource`; PUR 422 before FI takes its lock) → FI posting
   (FI source-key lock, BR-PUR-043) → receipt comparison → reservation transfer and Posted → one `SaveChanges` → commit.
   The supplier lock serialises all PUR invoice writers of that supplier, so the row order matches MATCH/RESOLVE.
3. **Nothing tracked is modified before FI returns Posted.** FI saves on the shared DbContext; changing PUR entities
   first would let FI's `SaveChanges` flush half a PUR change.
4. **Receipt comparison** (BR-FI-019): company, invoice, supplier, source version (= invoice version), fingerprint,
   original currency + gross and base currency + gross (recomputed with `PurchaseInvoiceFinanceSnapshot.Build`) and
   positive FI IDs must match, else 500 `PUR.INVOICE.FI_RECEIPT_MISMATCH` and rollback.
5. **FI results.** Rejected 404/409/422 keep FI's error code and status (422 adds `journalLineNo` or `lineNo`);
   Fault, Unchanged (impossible for a non-Posted PUR invoice), Rejected 400/500 or a missing code → 500
   (`PurchaseInvoicePostingException`, FI code when present, else `PUR.INVOICE.FI_POSTING_FAILED`). The finally block
   rolls back whatever transaction is still open and clears the change tracker.
6. **Storage.** `pur_purchase_invoice` gains nullable `posted_at`, `posted_from_version`, `fi_purchase_source_id`,
   `fi_payable_id`, `fi_journal_id` with CHECK `ck_pur_invoice_posted`: Posted ⇔ all five set (IDs and version > 0,
   written with `ISNULL(x,0) > 0` because a bare comparison is UNKNOWN for NULL and would pass a CHECK). FI IDs are
   references only (no cross-module FK). Migration `Purchasing_InvoicePosting`.
7. **Versions and replay.** FI posts the invoice at version N (`posted_from_version`); the Posted save increments the
   version once more (N+1). A Posted invoice is immutable, so a POST with N or N+1 returns the saved FI IDs with
   `replayed=true` without calling FI; any other version is 409.

## Consequences

- Reservation (`matched_reserved`) becomes `posted_invoiced` in the same transaction as FI AP/GL; 008b5 can enforce
  returns against posted invoiced quantity.
- Error texts for the new PUR codes and for FI `FI.PURCHASE_POSTING.*` are in the three-language `errors.json`.
- The front-end POST button is 008c3 follow-up work; real API Playwright of a posting needs FI account map setup and
  `base_currency_code`/`exchange_rate` in the SQL-arranged order rows (ADR-204/206).
