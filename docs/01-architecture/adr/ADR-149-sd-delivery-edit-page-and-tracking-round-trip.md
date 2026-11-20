# ADR-149: Sales outbound edit page — separate component, tracking round-trip and stale-version lock

Date: 2026-11-21. Status: Accepted (WU-SD-004c2b1).

## Context

Interface 2.9 (Sales Outbound Edit) revises a saved Draft or Rejected delivery through `PUT /api/v1/sal/deliveries/{id}` (`{version, draft}`) and deletes a Draft through `DELETE ...?version=`. The PUT replaces every line, including each line's lot/serial selections (ADR-145). A body without `tracking` therefore erases saved selections. The source order cannot change (`SAL.DELIVERY.SOURCE_IMMUTABLE`). Only a Draft can be deleted; Draft and Rejected can be edited (Rejected → Draft). The detail reply has warehouse and location IDs only, no names (004c1 P3).

WU-SD-004c2b was estimated at more than 90 minutes and was split: b1 = edit and delete (this ADR); b2 = submit/withdraw, lot/serial selection UI and warehouse/location names in list/detail/edit.

## Decision

1. The edit page is a separate component (`delivery-edit-page.ts`), not an `editId` mode of the create page. The create page is already ~280 lines and built around choosing an order; edit has a fixed order, loading/blocked/stale/deleted states and delete. Both share `delivery-draft-form.ts` (validation, exact JSON, receipt matching) and the same i18n field keys, so rules are not duplicated.
2. Saved tracking is carried in the form (`DeliveryDraftLineForm.tracking`) and sent back unchanged in the PUT. The edit page shows it read-only, with a note that selecting is coming. The client refuses a quantity below the saved lot/serial total (`belowTracking`), matching the server's 400. A reply is success only if each line's tracking also comes back unchanged.
3. Success requires the reply to be the same delivery and number, with a higher version, status Draft, and every line matching. The page then reloads its form from the reply and stays editable, showing "saved as version N". It does not lock like the create page.
4. 409, 404, `SAL.DELIVERY.INVALID_STATUS`, timeout and 5xx put the page into a "stale" state. The input stays visible, save and delete are disabled with a reason next to the buttons, and "Refresh saved outbound" reloads after a confirmation if there is unsaved input. A 422 with `lineNo` marks that line and stays editable; `ORDER_BALANCE_EXCEEDED` offers a balance reload.
5. The route requires `SAL.DELIVERY.VIEW` and `SAL.DELIVERY.EDIT`, because it loads through the VIEW detail. Delete also needs `SAL.DELIVERY.DELETE` and a danger confirmation; the confirmation says so if unsaved input will be lost. The advisory balance needs `SAL.ORDER.VIEW`. Without it the page explains that balances are hidden and the server still checks.
6. Until b2 adds names, saved warehouse/location are labelled "Saved warehouse (ID n)". Picking a new one shows code · name.

## Consequences

- A user with DELETE but not EDIT cannot reach the delete button; deleting from the list without EDIT is out of scope and recorded in b2.
- No backend or database change.
