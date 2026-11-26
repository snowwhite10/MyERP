# ADR-209: Purchase return limit against invoiced quantity (WU-PUR-008b5)

Date: 2026-11-27T05:08:41+08:00

Status: Accepted

## Context

BR-PUR-041 requires that a purchase return never takes back receipt quantity that has already been invoiced
(Posted) or is reserved by a Matched/Disputed invoice, because that quantity has (or will have) an FI payable; the
only way to reverse it is an FI credit note. 008b4 (ADR-208) made the invoice POST real, so `posted_invoiced` on
`pur_purchase_receipt_line` is now written by production code. The return write service (WU-PUR-007c) does not exist
yet: purchase receipts/returns are still SQL-arranged source rows (ADR-204/206), and 007a/007c are TODO.

## Decision

1. Do not build a return write path in 008b5. Deliver the two guards 007c must use, and accept them with real SQL
   after a real 008b4 POST:
   - **Application rule** `PurchaseReturnInvoicedLimit` (PUR Application, pure): `returnable uninvoiced =
     posted_received - posted_returned - (pending_return_reserved - this return's own reservation) - posted_invoiced
     - matched_reserved`. Duplicate request lines aggregate per receipt line; any short line refuses the whole return
     with 422 `PUR.RETURN.INVOICED_QUANTITY_EXCEEDED`, the message naming each line, its returnable quantity and the
     invoiced-or-matched quantity, and telling the user to use an FI credit note. A snapshot that violates the table
     invariant (sum of consumers > received), negative/over-precision values or missing/extra/duplicate sources is
     422 `PUR.RETURN.INVALID_SOURCE_QUANTITY`; bad requests are 422 `PUR.RETURN.INVALID_REQUEST_QUANTITY`.
   - **Database last line**: the existing CHECK `ck_pur_receipt_line_quantities`
     (`posted_returned + pending_return_reserved + posted_invoiced + matched_reserved <= posted_received`) already
     enforces the same limit; no migration is needed. 008b5 proves it rejects both reservation and posted return
     that would leave invoiced quantity unbacked.
2. 007c calls `EnsureWithinLimit` at SUBMIT (current reservation 0) and again at final approval with
   `CurrentReturnReserved` = its own reservation, under the receipt-line lock in ascending ID order (BR-PUR-032), so
   the approval re-check does not count its own reservation twice.
3. The deferred FI credit-note flow for invoiced quantity stays outside the first-phase scope; the 422 message points
   the user there.

## Consequences

- 008b closes; 007c has a tested rule and does not need to re-derive the formula.
- Test fixtures that write receipt quantities through EF `ExecuteSqlAsync` must not rely on interpolated decimal
  parameters for sub-cent values: EF maps them as `decimal(18,2)` and rounds `3.000001` to `3.00`. The 008b5 fixture
  uses invariant-culture literals.
