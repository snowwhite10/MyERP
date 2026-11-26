# ADR-207: PUR implementation of the FI purchase invoice source port (WU-PUR-008b3)

Date: 2026-11-27T00:45:54+08:00

Status: Accepted

## Context

FI `PurchaseInvoicePostingService` (WU-FI-003a2b) reads the saved PUR invoice through `IFinancePurchaseInvoiceSource`
inside the caller's transaction. 008b1 built the pure mapping (`PurchaseInvoiceFinanceSnapshot.Build`), 008b2 the order
rate snapshot source. Two gaps remained:

1. The 008a10 fingerprint needs per-kind accepted differences (`UNIT_PRICE`, `TAX_RATE`, `NET`, `TAX`, `GROSS`) with
   baseline/invoice values, but RESOLVE (`pur_purchase_invoice_resolution`) stores only NET/TAX/GROSS difference sums.
2. A non-Matched invoice (e.g. Disputed with unresolved differences, or an order without a rate snapshot) cannot
   produce a fingerprint or base amounts, yet FI must still answer 422 `SOURCE_NOT_POSTABLE` for it.

## Decision

1. `PurchaseInvoiceFinanceSource` (internal, PUR Infrastructure) implements the port and is registered once in
   `PurchasingModule`. It requires the caller's transaction on the shared DbContext (else `InvalidOperationException`),
   returns null for non-positive IDs, a company other than the current company, or a missing invoice; soft-deleted
   invoices are read (soft-delete filter ignored, company filter kept) and reported as `Deleted`. It takes no lock:
   the PUR POST caller (008b4) holds the BR-PUR-043 locks before calling FI.
2. **Only Matched carries posting evidence.** Any other state (Draft, Disputed, Rejected, Posted, Deleted) returns
   `PurchaseInvoiceFinanceSnapshot.NotPostable`: identity, version, state, saved original totals; empty fingerprint,
   empty base currency, rate 0, no lines. FI checks state first, so it answers 422 without needing evidence.
3. **Evidence rebuild** (`PurchaseInvoiceFinanceEvidence.Accepted`): differences are recomputed with
   `PurchaseInvoiceAmountMatch` from saved invoice lines and order-line prices (`IPurchaseInvoiceDraftSourceReader.ReadPricesAsync`).
   No current difference → no evidence (older RESOLVE rounds are history). Otherwise the evidence is the latest RESOLVE
   round (highest `invoice_version`); each row must match a line that currently differs and its stored NET/TAX/GROSS
   sums must equal the recomputed ones, else 422 `PUR.INVOICE.INVALID_POSTING_DIFFERENCE`. Completeness and all-Accept
   are enforced by the existing 040a preflight inside the fingerprint.
4. Matched: fingerprint (008a10) → rate snapshot (008b2; order missing → `INVALID_POSTING_SNAPSHOT`) → MD precision
   (`ICurrencyLookup.GetPrecisionAsync`) → `Build`. PUR `BusinessRuleException`s (rate snapshot missing, base currency
   changed, inconsistent evidence) propagate out of the FI call before FI writes; the caller's transaction is rolled
   back by its own error handling. 008b4 should run the same checks before calling FI so the HTTP answer is the PUR 422.

## Consequences

The real PUR source can be composed with the real FI posting service (PUR-FISRC-007～011, real SQL). 008b4 only needs
the HTTP command, lock order, reservation transfer and receipt comparison.
