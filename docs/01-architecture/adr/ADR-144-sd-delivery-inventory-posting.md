# ADR-144: Delivery posting through INV at SYS final approval and zero-step submit; split WU-SD-004b

Date: 2026-11-20. Status: Accepted (WU-SD-004b1).

## Context

ADR-139 refused final approval and zero-step submission of a delivery with 422 `SAL.DELIVERY.POSTING_UNAVAILABLE` until WU-SD-004b. BR-SD-024 requires the SD handler to post the saved lines through INV `PostSalesOutboundAsync` inside the SYS transaction, store the returned movement IDs and mark the delivery Posted; a zero-step submit performs the same sequence itself. Two constraints shaped this unit:

- SD has no table for INV movement IDs yet. The lot/serial selection migration of WU-SD-004a2b14 is still on the unmerged branch `feature/sd-delivery-line-tracking` (BLOCKED by host memory). A second SD migration on `main` now would force a model-snapshot rebase of that branch.
- The full unit (handler, zero-step path, storage migration, HTTP decision acceptance, replay) is estimated above 90 minutes.

## Decision

1. Split WU-SD-004b: **004b1** (this ADR) posts through INV at final approval and zero-step submit for untracked items, with real-SQL acceptance; **004b2** adds the SD-side movement-ID storage (migration, after 004a2b14 is merged so tracking selections are passed as `InventoryTrackingLine`), the HTTP decision acceptance through the SYS approval endpoints, and the Posted read-model fields. The parent closes when both pass.
2. A shared `SalesDeliveryPosting` step owns the balance lock/recheck (moved out of `SalesDeliverySubmissionService`) and the INV call. The caller has already locked the source order and the delivery row (Read Committed, same lock order as ADR-139); the step locks order lines in ascending ID, rereads the complete saved snapshot and runs `CheckLockedDecision` (Draft at submit, Pending at final approval, which excludes only the current reservation).
3. Lines map to `SalesOutboundLine(lineNo, itemId, warehouseId, locationId, quantity)` ordered by line number, with no cost or sales price. `SourceId` is the delivery ID and `BusinessDate` is the saved delivery date. `PostedAt` is `IClock.LocalNow`.
4. An INV result that is a replay, or whose movement count differs from the line count, is 409 `SAL.DELIVERY.POSTING_CONFLICT`: stock already left for this delivery while SD never recorded it, so it must not be treated as a second success. INV errors (`INV.STOCK.INSUFFICIENT`, reference or tracking errors) propagate unchanged.
5. Final approval: read the delivery's company/order without a lock, lock order → delivery, reload the tracked Pending delivery for this SYS instance (409 `APPROVAL_STATE_CHANGED` otherwise), require the order Approved (422 `ORDER_NOT_APPROVED`), then recheck and post. Rejection keeps the previous path and never resolves the posting step.
6. The handler receives `Func<SalesDeliveryPosting>`: INV posting resolves MD reference services, and SYS builds every result handler when the workflow is resolved, so constructor injection risks the DI cycle found in ADR-142.
7. Until 004b2, traceability from SD to INV is the INV ledger key (`SALES_OUTBOUND`, delivery ID, line number), which INV already makes unique. `POSTING_UNAVAILABLE` is retired.

## Consequences

- A SYS final approval and a withdrawal of the same delivery take locks in opposite order (SYS instance → order versus order → SYS instance). SQL Server resolves such a deadlock by failing one transaction with no partial effect; the order handler of ADR-142 has the same shape. 004b2 adds an HTTP race test and, if needed, retries the victim.
- Tracked items are still refused at Draft creation on `main` (TRACKING_UNSUPPORTED), so 004b1 never sends tracking; 004b2 passes saved selections once 004a2b14 merges.
- SD-DEL-020's "movement IDs remain linked" stays open until 004b2.
