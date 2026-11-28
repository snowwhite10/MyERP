# ADR-224 — Purchase return drafts: INV inbound evidence query and draft scope

Date: 2026-11-28T20:32:39+08:00

Status: Accepted

## Context

WU-PUR-007c3 saves purchase return drafts. Each return line must store the original `PURCHASE_INBOUND` movement ID
(`pur_purchase_return_line.original_movement_id`, ADR-223) that the INV posting contract later revalidates. PUR receipt
posting (007b1) did not persist the INV movement IDs on receipt lines, and PUR must not read `inv_` tables. INV movements
already carry the source key (company, `PURCHASE_INBOUND`, receipt ID, receipt line number).

## Decision

1. INV adds a read-only contract `IPurchaseInboundEvidenceQuery.FindAsync(receiptIds)` (max 1000 IDs) returning, for the
   current company only, each `PURCHASE_INBOUND` In movement's receipt ID, line number, movement ID, item, warehouse,
   location, quantity and saved unit cost. Existing posted receipts need no backfill.
2. A draft line is accepted only when, under the lock order purchase order → return → receipt lines ascending (the order
   007c4 SUBMIT reuses): the receipt line belongs to a Posted, non-deleted receipt of the same company and the header's
   purchase order (`PUR.RETURN.INVALID_SOURCE`); the per-receipt-line total does not exceed
   `posted_received − posted_returned − pending_return_reserved` (`PUR.RETURN.QUANTITY_EXCEEDED`, listing receipt line,
   requested and returnable); exactly one INV movement exists for (receipt, line) with the same item and quantity and a
   unit cost (`PUR.RETURN.SOURCE_EVIDENCE_MISSING`); and each lot/serial was captured on that receipt line with the
   per-number total not above the captured quantity, case-insensitive (`PUR.RETURN.TRACKING_NOT_FROM_RECEIPT`). All are
   422 with `lineNos`; any failure saves nothing.
3. Drafts reserve nothing and never call INV posting; `original_unit_cost` stays null until posting (007c4). The
   warehouse defaults to the receipt line's warehouse and may be changed to another existing warehouse (stock may have
   been transferred); storage locations are not captured yet (`location_id` null) and are deferred to the return UI
   (007e) if needed. A Draft may carry incomplete lot/serial capture; SUBMIT requires completeness.
4. Permissions `PUR.RETURN.VIEW/CREATE/EDIT/DELETE` with SYS data scope on owner/branch/department; out of scope or other
   company 404 `PUR.RETURN.NOT_FOUND`. Draft/Rejected editable with version (result Draft, purchase order fixed,
   `PUR.RETURN.SOURCE_LOCKED`); only Draft soft-deletable (`PUR.RETURN.NOT_DELETABLE`); numbers are never reused.
   Source options `GET source-lines?purchaseOrderId=` need CREATE or EDIT and are advisory.
5. The 007c2 Posted-immutability triggers require EF trigger metadata (`HasTrigger`) so EF does not use `OUTPUT`;
   migration `Purchasing_ReturnTriggerMetadata` records it with no schema change.

## Consequences

007c4 locks in the same order, adds the invoiced-quantity limit (BR-PUR-044) and the reservation, and builds
`PurchaseReturnOutboundLine` from the saved movement ID. Source options read PUR state only: a receipt PUR marks Posted
without INV evidence is offered but refused on save.
