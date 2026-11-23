# ADR-188: INV `SALES_RETURN` inbound priced at the original issue cost (WU-SD-008a3)

Date: 2026-11-23. Status: Accepted (WU-SD-008a3).

## Context

ADR-184 split sales returns; 008a3 must extend INV's public external-posting contract (ADR-064) with a `SALES_RETURN`
inbound so the 008a5 coordinator can receive returned goods in the SYS approval transaction. BR-SD-045 requires the
return to be valued at the cost of the original outbound movement, with same-company and item checks and
per-source-line idempotency; SD must never pass a sales price as cost or write INV tables.

INV values items by company-wide moving average (`usp_inv_value_item`, ADR-063). Outbound movements store no unit
cost: their cost is the pool average immediately before them in `(posted_at, id)` order. Reusing
`usp_inv_value_item` with `@cutoff_posted_at` is not exact, because the cutoff is inclusive and several movements may
share the issue's `posted_at` (same posting, or a fixed test clock).

## Decision

1. Contract: `IExternalInventoryPostingService.PostSalesReturnInboundAsync(sourceId, businessDate, lines)` with
   `SalesReturnInboundLine(LineNo, OriginalMovementId, ItemId, WarehouseId, LocationId?, Quantity)` and result
   `SalesReturnInboundResult(Replayed, Movements[])`, each `SalesReturnInboundMovement(LineNo, MovementId,
   OriginalMovementId, UnitCost?)` ordered by line number. Source type constant `ExternalInventorySourceTypes.SalesReturn
   = "SALES_RETURN"`, direction always In. No cost or tracking parameter.
2. Idempotency key is the existing unique `(company_id, source_type, source_id, source_line_no, direction)`: SD passes
   the return ID and the return line's `line_no`, which `sal_return_line` keeps permanently unique per return (ADR-184
   §5, including soft-deleted history). Same snapshot → `Replayed=true` with the original movement IDs; different
   snapshot → 409 `INV.POSTING.CONFLICT`. Replay recomputes the same cost because history before an existing movement is
   append-only.
3. Order of work (inside the caller's mandatory transaction): shape validation (400, before SQL) → item valuation locks
   ascending (same order as `StockPostingService`, so no new deadlock cycle) → load originals of the current company →
   refusals, each 422 with `lineNo`/`lineNos` (ADR-154): `INV.SALES_RETURN.ORIGINAL_NOT_FOUND` (missing or another
   company, no existence leak), `ORIGINAL_NOT_ISSUE` (not a `SALES_OUTBOUND` Out), `ITEM_MISMATCH`,
   `QUANTITY_EXCEEDED` (sum of this request's lines per original issue above its quantity) → cost per original →
   `IInventoryPostingService.PostAsync`. Quantities are in the item base unit on both movements, so equal item implies
   equal base unit. Cumulative returnable quantity across several returns stays SD's responsibility (BR-SD-044, under
   SD delivery-line locks); INV stores no link column, so it checks only within one request.
4. New SP `dbo.usp_inv_movement_issue_cost(@company_id, @movement_id)` replays the item's pool strictly before the target
   in `(posted_at, id)` order with exactly the rounding of `usp_inv_value_item`, returning `ROUND(value / quantity, 6)`;
   NULL when the pool was unpriced or empty. A movement not in the company or not Out → THROW 51038
   `INV.VALUATION.MOVEMENT_NOT_ISSUE`. Deployed by migration `Inventory_SalesReturnIssueCost` (Down drops it); no schema
   change. The replay loop duplicates `usp_inv_value_item` deliberately so the existing SP and its migration stay
   untouched; any valuation-rule change must update both (INV-SRET-001 checks the issue cost against the full valuation outcome).
5. Unknown original cost (unpriced pool) → inbound `UnitCost = null`, valued like any unpriced receipt; the result
   reports `UnitCost = null` so the caller can show/record that the cost was unknown. Refusing would block legitimate
   returns of stock that entered without a cost.
6. Lot/serial-tracked items stay unsupported (ADR-184 §6): no tracking is passed, so `StockPostingService` refuses them
   with `INV.TRACKING.REQUIRED`.

## Consequences

- SD 008a5 maps `sal_return_line.line_no`/`delivery line inv_movement_id` into the request and stamps the returned
  `MovementId` per line; `UnitCost` is available for any later COGS reversal evidence.
- SQL THROW next free number becomes 51039.
