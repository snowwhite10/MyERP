# ADR-196: Sales return edit page and split of WU-SD-008b3b2

Date: 2026-11-24. Status: Accepted (WU-SD-008b3b2a).

## Context

WU-SD-008b3b2 (ADR-195) bundles the edit page `/sal/returns/:id/edit`, the 2.16 detail "Edit" button and a choice of
destination warehouse / location. The destination choice needs a new return-scoped options read (backend endpoint,
authorization and integration tests) and a picker per line. Together they exceed one round.

The source-order read `GET /api/v1/sal/returns/source-orders/{orderId}` needs `SAL.RETURN.CREATE`. Draft, Pending and
Rejected returns reserve nothing (BR-SD-044, ADR-193): only Posted returns consume returnable, unbilled and creditable
balances, so the balances already include the quantities of the return being edited and nothing has to be added back.

## Decision

1. Split 008b3b2: **008b3b2a** (this ADR) frontend edit page plus detail "Edit" link, no backend change;
   **008b3b2b** backend widens the source-order detail read to `SAL.RETURN.CREATE` or `SAL.RETURN.EDIT` (union of data
   scopes, as delivery order-options do, ADR-155), adds return-scoped warehouse / location options and a per-line
   destination picker on create and edit. 008b3c (Playwright + UX QA) is unchanged.
2. The edit page reuses `SalesReturnCreatePage` (route `returns/:id/edit`, guards `SAL.RETURN.VIEW` + `SAL.RETURN.EDIT`,
   leave guard). It reads the saved return; only Draft and Rejected open the form (Pending / Posted show "can no longer
   be edited" and do not read balances). The order is fixed (no picker); the saved date, remark, selection, destination
   and unbilled / per-invoice split are filled in over the current source lines.
3. Saved quantities the source no longer offers (line fully returned or invoice line fully credited by a Posted return
   since; lot / serial line) are listed as "will be removed when you save" and left out of the PUT body.
4. PUT sends the loaded version and the saved request key. Success needs this return at a newer version whose content
   matches the request. A Draft with no change sends nothing (warning "No data changed"); a Rejected return is sent even
   unchanged because saving revises it back to Draft ("revised" message).
5. 409 keeps the input and offers "Refresh saved return" (confirmation before discarding edits); 404 NOT_FOUND says the
   return vanished or left the edit scope; other refusals reuse the create mapping (line marking, reload balances,
   `INVALID_STATUS`, `REQUEST_KEY_IMMUTABLE`).
6. An unknown outcome (timeout, network loss, uncoded 5xx, mismatched reply) locks the form; "Check saved return"
   re-reads it: our content at a newer version = saved; the loaded version unchanged = not saved (form unlocks); anything
   else = conflict. PUT is not resent blindly because a second PUT with the old version would be a misleading 409.

## Consequences

- Until 008b3b2b a user with `SAL.RETURN.EDIT` but without `SAL.RETURN.CREATE` opens the page but sees the
  permission-denied source error (with Retry) instead of the lines; the seeded sales role has both.
- Destinations stay the saved ones (create still uses the delivery's warehouse / location) until 008b3b2b.
