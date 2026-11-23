# ADR-194: Sales return create page and split of WU-SD-008b3

Date: 2026-11-24. Status: Accepted (WU-SD-008b3a).

## Context

WU-SD-008b3 (Interface 2.17: create / edit / submit / delete, destination choice, Playwright and UX QA) is larger than
one round. A return line is more complex than an invoice line: the returned quantity must be split explicitly into an
uninvoiced part and parts credited against Posted invoice lines (BR-SD-044, ADR-185), and the server refuses a line
whose destination warehouse/location is inactive. The source read (ADR-193) returned only destination IDs, so the form
could not name the default destination. The outbound warehouse pickers (`/sal/deliveries/warehouse-options`) require
`SAL.DELIVERY.CREATE/EDIT`, which a returns clerk may not have.

## Decision

1. Split 008b3: **008b3a** create page `/sal/returns/new` (this ADR) and the list's "New return" button; **008b3b**
   edit `/sal/returns/:id/edit` (Draft and Rejected revision, version 409), detail Submit / Delete buttons and a
   destination warehouse/location choice (return-scoped option reads); **008b3c** Playwright mock + real API and
   myerp-ux-review screenshots of 2.16 / 2.17.
2. The source read adds MD labels (`warehouseCode/Name/Active`, `locationCode/Name/Active`, null when soft-deleted) to
   each source line, using `IInventorySelectionOptions.SavedAsync` exactly like the return detail (WU-SD-008b2).
3. In 008b3a each line's destination is the outbound line's warehouse/location (read-only). An inactive or deleted
   destination shows a warning that saving will be refused (the server answers MD.* 422), so the user is not misled.
4. Form rules mirror CREATE and are advisory: blank quantities count as 0; every entry is a non-negative six-decimal
   text compared exactly (no binary numbers); uninvoiced ≤ `unbilledRemainingQuantity`, each credit ≤ the invoice
   line's `remainingQuantity`, 0 < total ≤ `returnableQuantity`; the date may not precede a selected outbound or a
   credited invoice; tracked lines cannot be selected and say why. Zero credits are not sent.
5. Outcome handling follows the invoice issue page (ADR-179): one request key per attempt, kept and resent unchanged
   while the outcome is unknown (timeout, network, uncoded 5xx, or a 2xx receipt that does not match the request);
   a definite refusal takes a new key. 422 `lineNo` / `invoiceLineId` mark and focus the line or invoice portion.
   Success opens `/sal/returns?open=<id>` when the user can view returns.

## Consequences

- No new endpoint or table; the source DTO gains optional fields (backward compatible).
- Until 008b3b, users cannot redirect goods to another warehouse from the page, and Drafts are edited / submitted only
  through the API.
