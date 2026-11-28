# ADR-225 — Purchase return SUBMIT reservation, SYS decision and INV posting

Date: 2026-11-28T21:12:59+08:00

Status: Accepted

## Context

WU-PUR-007c4 completes REQ_F4114.7.3. Drafts (ADR-224) reserve nothing. SUBMIT must hold the returnable quantity
against concurrent returns and invoices, final approval must issue stock through the dedicated INV contract
(ADR-223) at the original receipt cost, and rejection must release only the return's own reservation. The invoiced
limit rule `PurchaseReturnInvoicedLimit` (ADR-209, BR-PUR-044) already exists as a pure function.

## Decision

1. `POST /api/v1/pur/purchase-returns/{id}/submit` with body `{version}` and the new permission `PUR.RETURN.SUBMIT`
   (SYS data scope on owner/branch/department; outside scope or other company 404 `PUR.RETURN.NOT_FOUND`). One Read
   Committed transaction locks purchase order → return → receipt lines by ascending ID (UPDLOCK, HOLDLOCK), the same
   order as drafts and decisions. Stale version 409; status other than Draft 422 `PUR.RETURN.NOT_SUBMITTABLE`.
2. Under the locks SUBMIT rechecks, per receipt line with return lines aggregated: the receipt line still belongs to a
   Posted receipt of the order (422 `PUR.RETURN.INVALID_SOURCE`); `received − returned − pending` covers the request
   (422 `PUR.RETURN.QUANTITY_EXCEEDED` with `lineNos` and `receiptLines`); the uninvoiced limit with no own reservation
   (422 `PUR.RETURN.INVOICED_QUANTITY_EXCEEDED` with `lineNos`, receipt line number, returnable and invoiced/matched
   quantity); a lot- or serial-tracked item has captured numbers totalling the line quantity (422
   `PUR.RETURN.TRACKING_REQUIRED`). It then adds the quantity to `pending_return_reserved`, sets PendingApproval and
   submits SYS with amount = Σ round(quantity × receipt unit cost, 4). A zero-step flow posts in the same transaction.
3. Final approval (`PurchaseReturnPosting`, shared by zero-step SUBMIT and the SYS handler) re-runs both checks
   excluding the return's own reservation (`CurrentReturnReserved`), moves the reservation to `posted_returned`, calls
   INV `PostPurchaseReturnAsync(returnId, businessDate, lines)` with each saved line's original movement, warehouse and
   saved lots/serials, stores the returned `OriginalUnitCost` on every line, then marks the return Posted. Lines are
   saved before the header because the Posted-immutability trigger refuses line updates under a Posted header. An INV
   replay (`Replayed`) or a movement count mismatch is 409 `PUR.RETURN.POSTING_CONFLICT`; any INV refusal (stock,
   lot/serial availability) propagates, so SYS, PUR and INV roll back together.
4. Rejection releases exactly this return's quantity from `pending_return_reserved` (refusing an inconsistent snapshot
   with 422 `PUR.RETURN.INVALID_SOURCE_QUANTITY`) and sets Rejected; the existing EDIT turns it back into Draft. Any
   decision for a return that is not PendingApproval is 409 `PUR.RETURN.APPROVAL_STATE_CHANGED`.
5. The handler resolves the posting step lazily (`Func<PurchaseReturnPosting>`), as receipts do (ADR-220), because INV
   posting depends on MD services that must not be built with the SYS workflow.

## Consequences

`ck_pur_receipt_line_quantities` remains the last line of defence; two concurrent SUBMITs serialise on the purchase
order lock and the second sees the first reservation. A return quantity can no longer be invoiced once reserved, and an
invoice matched after SUBMIT cannot make approval over-return. LIST/CSV (007c5) and the UI (007e) follow; the return
amount for AP debit uses the stored `OriginalUnitCost` (FI work).
