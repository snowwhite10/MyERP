# ADR-150: Sales outbound submit, withdraw and delete live in the list detail

Date: 2026-11-21. Status: Accepted (WU-SD-004c2b2a).

## Context

`POST /api/v1/sal/deliveries/{id}/submit` and `/withdraw` (body `{version}`, permission `SAL.DELIVERY.SUBMIT`) and `DELETE ...?version=` (`SAL.DELIVERY.DELETE`) had no screen except delete on the edit page. The edit page route needs `SAL.DELIVERY.VIEW` + `EDIT`, so a user with SUBMIT or DELETE but not EDIT had no way to act, and a Pending outbound (not editable) showed only "withdraw first" with nowhere to withdraw. A zero-step SYS flow posts the outbound through INV inside the submit request, so the 200 reply may be `Posted`, and an INV refusal (for example 422 `INV.STOCK.INSUFFICIENT`, which carries no `lineNo`) leaves it Draft.

WU-SD-004c2b2 (submit/withdraw, lot/serial picker, warehouse/location names, delete without EDIT) was estimated at more than 90 minutes. It was split into c2b2a (this ADR: actions in the detail, delete without EDIT), c2b2b (lot/serial selection UI) and c2b2c (warehouse/location names).

## Decision

1. The actions are buttons in the `/sal/deliveries` detail panel (route VIEW), shown by saved status and the action's own permission: Submit for Draft with SUBMIT, Withdraw for Pending with SUBMIT, Delete draft for Draft with DELETE. EDIT is not required, matching the FI collection-receipt pattern. Rejected shows a hint to edit and save (back to Draft) first, because the server submits only a Draft.
2. Every action asks for confirmation first, with its consequence (submit: reserves balance or posts at once if no step applies; withdraw: releases the reservation; delete: danger style, cannot be undone), and sends the version that was loaded.
3. Success is shown only when the reply is the same delivery and number, has a newer version, and has a status the action can produce (submit: Pending or Posted; withdraw: Draft). Posted gets its own message ("needed no approval and was posted; stock was issued"). The list row is updated from the reply and the detail is reloaded through VIEW so the approval history appears. After a delete the list reloads and a success notice remains.
4. 409 (including `SAL.DELIVERY.POSTING_CONFLICT`), 404, `SAL.DELIVERY.INVALID_STATUS`, timeout, network failure and 5xx pause all actions with a reason and a Refresh button, which gets keyboard focus. 5xx also shows the trace ID. No success is claimed.
5. Other refusals keep the actions enabled, show the cause and say that nothing changed (still Draft / still pending / nothing deleted). A 422 with `lineNo` marks that saved line in the table and shows the reason under it. INV stock/lot/serial refusals add a hint to check each line's warehouse, location and lot/serial stock. They do not name a line, because INV returns no `lineNo`.
6. The empty tracking cell now reads "None selected" (not "Not tracked"), because an empty list does not mean the item is untracked, and it contradicted a TRACKING_REQUIRED refusal on the same line (UX review P2).

## Consequences

- No backend or database change.
- Line-level reporting of INV stock refusals needs the SD posting adapter to add `lineNo` to INV errors. This is listed in the backlog as P2 (WU-SD-004c2b2d).
- The edit page's Pending message now points to the list detail for withdrawing.
