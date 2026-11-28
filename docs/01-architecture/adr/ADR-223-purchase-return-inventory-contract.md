# ADR-223 — Purchase return split and INV PostPurchaseReturnAsync contract

Date: 2026-11-28T17:06:20+08:00

Status: Accepted

## Context

WU-PUR-007c (purchase return back-end) is estimated at well over 90 minutes: an INV outbound contract, two/three new PUR tables, draft CRUD, SUBMIT reservation with SYS approval/rejection, list and CSV. MASTER_PROMPT §1 requires splitting. BR-PUR-033 asks INV for a dedicated `PostPurchaseReturnAsync` with the fixed `PURCHASE_RETURN` source, taking the saved return line, the original `PURCHASE_INBOUND` movement ID, the storage location and lot/serial numbers, and "posting at the original source cost after checking current stock". BR-PUR-030 says the return keeps the original inbound movement cost and is not re-priced at today's rate.

The INV ledger is a moving-average pool (`usp_inv_value_item`) and the database constraint `ck_inv_stock_movement_unit_cost` forbids a unit cost on any `Out` movement; every issue leaves the pool at the running average. Allowing priced issues would change the ledger invariant, both valuation procedures and every consumer that assumes issues are unpriced, and could drive the pool value negative when the original cost exceeds the average of the remaining stock.

## Decision

1. Split 007c into: **c1** INV `PostPurchaseReturnAsync` (this ADR); **c2** `pur_purchase_return`/line/tracking schema and SQL CHECKs; **c3** return draft CREATE/EDIT/READ/DELETE with source options; **c4** SUBMIT reservation (`pending_return_reserved` on the receipt line) + SYS approval posting via c1 / rejection release; **c5** LIST/CSV. The 007c parent is not counted.
2. `IExternalInventoryPostingService.PostPurchaseReturnAsync(sourceId, businessDate, PurchaseReturnOutboundLine[])`, caller transaction required (`INV.POSTING.TRANSACTION_REQUIRED`). Each line carries `LineNo`, `OriginalMovementId`, item, warehouse, location, base quantity and optional tracking. INV locks item valuation (ascending), then validates against the original movement: missing or other company `INV.PURCHASE_RETURN.ORIGINAL_NOT_FOUND`; not a `PURCHASE_INBOUND` In movement `ORIGINAL_NOT_RECEIPT`; item differs `ITEM_MISMATCH`; this request's total per original exceeds the original quantity `QUANTITY_EXCEEDED`; any lot not received on the original, a lot total per original above the received lot quantity, or a serial not received on the original `TRACKING_NOT_FROM_ORIGINAL` (all 422 with `lineNos`, zero writes). The warehouse/location may differ from the original (goods may have been transferred); current stock and lot/serial availability at the given key are checked by the standard posting service (`INV.STOCK.INSUFFICIENT`, `INV.LOT.INSUFFICIENT`, `INV.SERIAL.NOT_AVAILABLE`, `INV.TRACKING.REQUIRED`).
3. **Cost.** The `Out` movement stays unpriced and leaves the pool at the moving average (ledger invariant unchanged). The **return cost** required by BR-PUR-030 is the original movement's saved unit cost, returned per line as `OriginalUnitCost` from the immutable INV ledger; PUR stores it on the return line (c4) and uses it for the return amount and later the AP debit (008). Callers cannot pass a cost. The difference between the original cost and the average stays in the pool; INV has no GL link today, so no price-difference posting exists. A future change request may introduce priced issues if INV↔GL valuation is added.
4. Idempotent per (company, `PURCHASE_RETURN`, return ID, line no) through the standard snapshot replay; a different snapshot is 409 `INV.POSTING.CONFLICT`. Cumulative returnable quantity across several returns is controlled by PUR under its source lock (BR-PUR-032); INV checks only the single request.

## Consequences

PUR never calls the generic `PostAsync` or the sales outbound contract for returns. Test fakes implementing the interface gain the new method. The cost decision is recorded in INV/PUR business rules; c4 must persist `OriginalUnitCost`.
