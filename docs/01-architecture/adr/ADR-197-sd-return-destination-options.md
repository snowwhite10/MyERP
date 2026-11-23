# ADR-197: Sales return source read under EDIT and return-scoped destination options

Date: 2026-11-24. Status: Accepted (WU-SD-008b3b2b).

## Context

ADR-196 left two gaps on the return create / edit page (interface 2.17):

- `GET /api/v1/sal/returns/source-orders/{orderId}` needed `SAL.RETURN.CREATE`, so a user with `SAL.RETURN.EDIT` only
  saw a permission error instead of the lines of the return being edited.
- The destination warehouse / location of each line was fixed to the delivery's (or the saved) one. When that warehouse
  or location is inactive the server refuses the save (`MD.WAREHOUSE.INACTIVE`, `MD.STORAGE_LOCATION.INACTIVE`) and the
  user had no way to choose another, short of asking for `MD.WAREHOUSE.VIEW`.

The delivery pages solved the same problem with action-scoped option endpoints (ADR-155).

## Decision

1. `GET source-orders/{orderId}` requires `SAL.RETURN.CREATE` **or** `SAL.RETURN.EDIT`; the order is visible when it is
   in either action's SYS data scope (union, `SalesActionAccess.RequireAnyScopeAsync`). The answer is unchanged
   (404 `SAL.RETURN.SOURCE_NOT_FOUND`, 422 `SAL.RETURN.ORDER_NOT_APPROVED`). The source-order list stays CREATE-only,
   because only creation chooses an order.
2. New `GET /api/v1/sal/returns/warehouse-options` and `GET .../warehouse-options/{warehouseId}/locations`, each
   requiring CREATE or EDIT (not `MD.WAREHOUSE.VIEW`). They reuse MD's `IInventorySelectionOptions`: active warehouses
   of the current company, and active locations of one active warehouse (otherwise 404 `MD.WAREHOUSE.NOT_FOUND`).
   Keyword matches code or name; sort is `code` only. No data scope applies (MD master data is company-wide).
3. Frontend: each selectable line has "Choose warehouse" / "Choose location" / "Clear location" buttons (enabled once
   the line is selected; tracked lines have none). The label shows the delivery default, the saved destination (edit) or
   the pick. Another warehouse clears the location; the same warehouse keeps it. An inactive / deleted destination
   keeps its warning until re-picked. A server refusal `MD.WAREHOUSE.*` / `MD.STORAGE_LOCATION.*` with `lineNo` focuses
   that line's warehouse / location button. The server still revalidates every reference on POST / PUT.

## Consequences

- The seeded sales role is unaffected; EDIT-only roles can now edit returns end to end.
- The options are advisory: a warehouse deactivated after the pick is refused on save as before.
- 008b3c (Playwright mock + real API, UX screenshots of 2.16 / 2.17) is next.
