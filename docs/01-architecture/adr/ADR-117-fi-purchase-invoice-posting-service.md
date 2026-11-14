# ADR-117 — FI purchase invoice payable posting service: own source-key lock, Matched-only, line reconciliation (WU-FI-003a2b)

Date: 2026-11-14. Status: Accepted (WU-FI-003a2b).

## Context

WU-FI-003a2b (ADR-116 split) adds `IPurchaseInvoicePostingService`, which PUR (WU-PUR-008a) calls inside its own posting transaction to create the FI purchase source, the open payable and the balanced GL journal. It follows the SD invoice service (ADR-108), but three points differ:

1. SD posting serialises on the FI credit key (customer). AP has no credit limit, so there is no FI lock to take before the replay check. Without one, two transactions could both see "no FI source" and both read PUR.
2. The SD rules accept an `Issuing` or `Posted` snapshot. BR-FI-015 says an already Posted PUR source cannot create a new payable.
3. BR-FI-017 asks FI to check each saved line and the aggregate. The SD snapshot carries only totals.

## Decision

- **FI source-key applock first.** After the shape and company checks, FI takes `sp_getapplock` on `ap_purchase:{companyId}:{invoiceId}` (Exclusive, owner Transaction, 15 s by default; an internal settable property lets race tests shorten it). A timeout or deadlock victim raises SQL **51028** inside the batch and returns 409 `FI.PURCHASE_POSTING.LOCK_TIMEOUT`. The caller's transaction stays open. Order (BR-FI-020): PUR's own locks (caller) → FI source key → FI replay check → PUR read through `IFinancePurchaseInvoiceSource` → map → MD accounts → GL source applock (inside `IGlJoinedPostingService`) → writes. FI never asks PUR for locks after its own.
- **Replay before the PUR read.** If FI already holds the source key with the same supplier, version and fingerprint, the result is Unchanged with the committed receipt, and PUR is not read. Any other claim returns 409 `SOURCE_CONFLICT`. A source without a payable returns 500 `INCONSISTENT_STATE`, and nothing is written.
- **Only `Matched` posts.** `FinancePurchaseSourceState` = Draft, Disputed, Rejected, Deleted, Matched, Posted. If a snapshot reaches the state check, FI has no committed source for it. So `Posted` is rejected with 422 `SOURCE_NOT_POSTABLE`, like the other non-Matched states. This is stricter than the SD rules on purpose (BR-FI-015).
- **Snapshot lines.** `FinancePurchaseInvoiceSnapshot.Lines` holds the saved line components in both currencies. Each line must reconcile (`net + tax = gross`, gross > 0, decimal(19,4)). Line numbers are positive and unique. In a base-currency invoice, every line's base amounts equal its original amounts. The lines must add up exactly to the header components, with no balancing plug. A bad line returns 422 `INVALID_AMOUNTS` with `InvalidSourceLine` set to the PUR line number. A header or sum failure has no line number. `InvalidLine` keeps its meaning: the journal line whose account is unusable.
- **Journal (BR-FI-018).** Debit the purchase account for base net, debit input tax for base tax (left out when zero), credit the payable account for base gross. The mapping version is the `gl_purchase_invoice_account_map` row version (ADR-116). The GL `PUR/PurchaseInvoice` version source reads the same row inside the same transaction. The account-check helper `SalesJournalDraft` is renamed `PostingJournalDraft`; its behaviour is unchanged.
- **Writes.** The source is saved first. The payable is saved next, because its same-company FK and its origin trigger need the source row. The payable's open amount equals its original amount, in both currencies. Any write error rolls back the caller's whole transaction (Fault 500 `WRITE_FAILED`).
- **Nonrecoverable tax** is still not modelled (ADR-116): the saved tax is treated as claimable.

## Consequences

- No production `IFinancePurchaseInvoiceSource` is registered until PUR-008a. Until then, calls get 500 `SOURCE_CONTRACT_UNAVAILABLE`. No HTTP endpoint exists yet.
- The race tests (two connections on the same key, lock timeout, caller rollback after FI writes) are left to WU-FI-003a3.
- `FI.PURCHASE_POSTING.*` codes have no i18n yet. They will be added with the first screen or endpoint that shows them (003a2c/003c).
