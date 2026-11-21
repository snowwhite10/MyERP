# ADR-153: Delivery details name each line's warehouse and location from MD

Date: 2026-11-21. Status: Accepted (WU-SD-004c2b2c).

## Context

The outbound list detail (interface 2.7) showed raw warehouse/location IDs, and the edit page (2.9) showed "Saved warehouse (ID n)" until the user picked again. A viewer with only `SAL.DELIVERY.VIEW` usually lacks `MD.WAREHOUSE.VIEW`, so a front-end batch lookup through MD's warehouse API would fail for exactly the users who read deliveries, and would need one request per warehouse for locations. SD must not read MD tables.

## Decision

1. The backend adds labels to every delivery line on the VIEW detail and the CREATE/EDIT/SUBMIT/WITHDRAW receipts (all built by `SalesDeliveryQueryService.DescribeAsync`): `warehouseCode`, `warehouseName`, `warehouseActive`, `locationCode`, `locationName`, `locationActive`.
2. Labels come from the existing MD contract `IInventorySelectionOptions.SavedAsync` (already used by INV and PUR for saved references): current company only, inactive records included, soft-deleted records absent. One query for warehouses and one for locations per detail; IDs come only from the saved delivery, never from the client. No extra permission is needed, because the caller is already authorised to read the delivery (same rule as ADR-151 for `trackingMode`).
3. A missing label (soft-deleted in MD) is `null`; the UI then shows "Deleted warehouse/location (ID n)". `warehouseActive`/`locationActive = false` adds "(inactive)" so the user knows why saving or submitting would be refused (`MD.WAREHOUSE.INACTIVE`).
4. Labels are the current MD code and name, not a snapshot. Delivery lines keep only IDs (no migration); INV movements keep their own code snapshot for audit.
5. On the edit page, labels from the reply replace local ones; a label the user just picked stays only where the reply has none.

## Consequences

- No migration, no new contract, two additional reads per detail.
- Renaming a warehouse in MD changes how old deliveries display it; that matches INV documents, which also show current MD labels via `SavedAsync`.
- WU-SD-004c4 (create/edit options authorised by action) is still needed for picking warehouses without `MD.WAREHOUSE.VIEW`.
