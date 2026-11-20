# ADR-151: Sales outbound lines carry the item tracking mode; lots and serials are typed on the edit page

Date: 2026-11-21. Status: Accepted (WU-SD-004c2b2b1).

## Context

WU-SD-004c2b2b asked for lot/serial selection on the outbound create and edit pages, "by the MD item tracking mode, chosen from INV available lots/serials". Two facts block a frontend-only solution:

- The page does not know the tracking mode. SD keeps item code/name snapshots only. `GET /api/v1/md/items/{id}` needs `MD.ITEM.VIEW`, which the seeded SALES role does not have. INV `selection-options/items` belongs to INV document permissions.
- INV `GET /api/v1/inv/tracking/lots|serials` (`INV.TRACKING.VIEW`, which SALES has) returns the company-wide balance per lot, not the balance in one warehouse/location. A list built from it could offer lots that are not in the line's warehouse.

The whole unit was estimated at more than 90 minutes, so it was split: b1 (this ADR) and b2 (choose from INV available lots/serials, plus a create-page hint after saving).

## Decision

1. `SalesDeliveryLineDetail` gets `trackingMode` (`None` / `Lot` / `Serial`, string enum). It is filled for the VIEW detail and for the CREATE/EDIT/SUBMIT/WITHDRAW receipts (`DescribeAsync`) from a new MD contract method, `ISalesItemLookup.TrackingModesAsync(ids)`. This method covers current-company items, including inactive ones; soft-deleted items are left out, so the field is null for them. MD forbids changing the mode once an item is used, so the current mode is the one the server will check. SD does not read MD tables directly, and there is no migration.
2. The edit page (2.9) shows an editor for each line by its saved `trackingMode`:
   - **Lot:** rows of lot number plus quantity, with Add and Remove buttons.
   - **Serial:** a textarea with one serial per line. Blank lines are skipped and each serial counts as 1. The raw text is kept while the user types, so a new line is not removed mid-entry.
   - **Unknown (null) or None mode with saved picks:** the picks are shown read-only and sent back unchanged (ADR-149).
3. Before PUT, the client checks each pick: one non-blank lot number (up to 50 characters) or one non-blank serial number (up to 100), a positive quantity with up to 6 decimals (a serial must be exactly 1), and no number repeated in the line (case is ignored). Picks may total less than the line quantity: a Draft may be incomplete. The page shows the exact progress, "Selected x of outbound quantity y", and says whether the line is complete or must be completed before submission. A quantity below the pick total is still `belowTracking`. Once a save has been refused, pick edits re-run the checks, so a corrected row no longer shows an error (UX review P2).
4. Availability is not checked on the client. A hint says to enter lots/serials that are in stock in the chosen warehouse/location. INV checks stock when the outbound is approved and posted, and the server's TRACKING_* and SERIAL_DUPLICATE refusals are reported for the line (ADR-149/150).

## Consequences

- One additive JSON field. Old clients ignore it.
- Choosing from INV stock needs a warehouse/location-aware available-lot query that is authorised for SD CREATE/EDIT (WU-SD-004c2b2b2, which may share the options endpoint of WU-SD-004c4).
- The create page still saves lines without picks. Picks are added on the edit page before submission.
