# ADR-152: Delivery writers choose lots/serials from INV stock at the line's warehouse/location

Date: 2026-11-21. Status: Accepted (WU-SD-004c2b2b2a).

## Context

ADR-151 left choosing lots/serials from stock to WU-SD-004c2b2b2. INV `GET /api/v1/inv/tracking/lots|serials` needs `INV.TRACKING.VIEW` and returns the company-wide balance of each number, so it can offer a lot that is not in the outbound line's warehouse. The pick list must instead be authorised by the delivery action (`SAL.DELIVERY.CREATE` or `EDIT`), and SD must not read INV tables. The whole unit (endpoint plus UI) was estimated at more than 90 minutes, so it was split: b2a (this ADR, backend) and b2b (picker UI on the edit page and the create-page hint).

## Decision

1. INV publishes a read-only contract `IStockTrackingAvailabilityQuery` (`LotsAsync` / `SerialsAsync(itemId, warehouseId, locationId, keyword, limit)`). It sums the append-only `inv_stock_movement_tracking` rows of the current company whose movement is at exactly that warehouse and location (`null` = stock without a location, the same rule as `IStockBalanceQuery.GetOnHandAsync` and posting) and returns the numbers with positive stock. Lots are ordered by earliest expiry (no expiry last), then lot number; serials by number. At most `limit` (1–200) rows, plus `truncated`. It does not authorise; the caller does.
2. SD adds `GET /api/v1/sal/deliveries/tracking-options?orderLineId=&warehouseId=&locationId=&keyword=` with `RequireAnyPermission(SAL.DELIVERY.CREATE, SAL.DELIVERY.EDIT)`. The item is taken from the order line, never from the client. The order must be visible in the data scope of at least one of the caller's CREATE/EDIT permissions; otherwise 404 `SAL.ORDER.NOT_FOUND` (missing line, other company or out of scope). The order status is not checked: an edit of a Rejected delivery still needs picks, and submit rechecks the order.
3. The response is `{ trackingMode, items: [{ number, quantity, manufactureDate, expiryDate }], truncated }`, at most 100 rows. `trackingMode` comes from `ISalesItemLookup.TrackingModesAsync` (ADR-151); `None` returns no rows and null means the item was deleted.
4. The quantity is on-hand stock. Picks on other Draft/Pending deliveries are not subtracted, because a Draft reserves nothing and INV checks lot balance and serial availability again in the posting transaction (ADR-144/145). The UI must say "in stock now" rather than "reserved for you".

## Consequences

- No migration. The query uses the existing `(company_id, lot_id)` / `(company_id, serial_id)` tracking indexes with an `inv_lot`/`inv_serial` scan per item, the same plan as INV's tracking list.
- WU-SD-004c4 (options for create/edit authorised by action) can follow the same pattern for orders, warehouses and locations.
- WU-SD-004c2b2b2b adds the picker to the 2.9 edit page and a create-page hint to pick on the edit page after saving.
