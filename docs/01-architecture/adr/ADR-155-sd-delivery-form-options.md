# ADR-155: Delivery create/edit options are authorised by the delivery action

Date: 2026-11-21. Status: Accepted (WU-SD-004c4). Supersedes the interim part of ADR-148.

## Context

ADR-148 let the outbound create page (2.8) and edit page (2.9) reuse `GET /api/v1/sal/orders`, `GET /api/v1/sal/orders/{id}` and `GET /api/v1/md/warehouses[/{id}/locations]`. A warehouse clerk with `SAL.DELIVERY.CREATE`/`EDIT` but without `SAL.ORDER.VIEW` or `MD.WAREHOUSE.VIEW` could open the page but could not choose an order or a warehouse; the page only explained which permission was missing. The SD api.md design already required form options to be authorised by the action. The order detail also exposed prices and amounts that a delivery writer does not need, and the MD warehouse list included inactive warehouses that the server then refused.

## Decision

1. SD adds four read-only routes under `/api/v1/sal/deliveries`:
   - `GET order-options` — `SAL.DELIVERY.CREATE` and its SYS data scope; Approved orders only; `{ id, documentNo, customerCode, customerName, businessDate }`; keyword on number, customer code or name; sort `documentNo` or `businessDate` (default newest first).
   - `GET order-options/{orderId}` — `SAL.DELIVERY.CREATE` or `EDIT` (either scope, as tracking options in ADR-152); any status so the page can explain a no-longer-Approved order; lines with item, ordered, posted, pending and remaining quantity; no prices, amounts or approval history. 404 `SAL.ORDER.NOT_FOUND` when missing, foreign or out of scope.
   - `GET warehouse-options` and `GET warehouse-options/{warehouseId}/locations` — `SAL.DELIVERY.CREATE` or `EDIT`; active records only through the existing MD contract `IInventorySelectionOptions` (the same one INV uses); 404 `MD.WAREHOUSE.NOT_FOUND` for an inactive, deleted or foreign warehouse.
2. The order balance is computed by one shared query (`SalesOrderQueryService.DeliveryBalancesAsync`) for the order VIEW detail and the delivery option, so both always agree. It stays advisory; submission rechecks under order-line locks.
3. The front end uses only these routes on 2.8/2.9. The "needs order view / warehouse view" hints and the related i18n keys are removed.

## Consequences

- No migration and no new cross-module contract.
- A CREATE-only user who is not allowed to see an order (Self scope) gets an empty list and 404, matching what the create command would do.
- Fully delivered Approved orders are still listed; the page explains "nothing left to ship" after choosing one. Filtering them in SQL is left for later if lists become long.
