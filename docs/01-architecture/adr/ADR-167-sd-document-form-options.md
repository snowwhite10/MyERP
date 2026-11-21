# ADR-167: SD-owned customer / item / output-tax-rate options for the quotation and order forms

Date: 2026-11-22. Status: Accepted (WU-SD-FORM-OPTIONS).

## Context

The quotation (2.2) and sales order (2.5) create/edit forms picked customers, items and tax rates from the MD lists
(`GET /md/customers`, `/md/items`, `/md/tax-codes` and `/md/tax-codes/{id}/rates`), which need `MD.CUSTOMER.VIEW`,
`MD.ITEM.VIEW` and `MD.TAX_CODE.VIEW`. The seeded SALES role (SD and CRM, INV read-only) has none of them, so the
`sales` user could not fill either form against the real API (found by WU-SD-005b3a, ADR-166). The MD lists also
offered records the save rejects (inactive or unapproved customers, inactive items, input-only tax codes, rates not
effective on the document date). The price rule screen (ADR-162) and deliveries (ADR-155) already use SD-owned option
endpoints for the same reason.

## Decision

- New controller `GET /api/v1/sal/document-options/{customers|items|output-tax-rates}`, authorised by any one of
  `SAL.QUOTATION.CREATE`, `SAL.QUOTATION.EDIT`, `SAL.ORDER.CREATE`, `SAL.ORDER.EDIT`; no MD permission. A user with only
  view permissions (SD or MD) gets 403. No data scope applies: MD records are company-wide.
- The choices come from the existing MD contract `ISalesSelectionOptions` (SD never reads MD tables) and are exactly
  what the document save accepts:
  - customers: active and approved; items: active; keyword on code or name, paged, `sort=code` only (400 otherwise);
  - output tax rates: tax type `Output` or `Both`, active tax code, effective on the required `businessDate`
    (effective-to inclusive). Missing or invalid `businessDate` is 400 on that field. A company has few tax codes, so
    the SD service filters by keyword and pages in memory over `CommercialAsync(date)`.
- The save still revalidates every reference (unchanged), so a record deactivated after it was picked is still rejected.
- Frontend: `SalesDocumentOptionsService`; both forms use it. The tax picker becomes **one** dialog (code, name, rate)
  instead of tax code → rate. It needs the document date: until a full date is entered the button is disabled and a
  hint under it says why (MASTER_PROMPT 20.1, disabled buttons explain themselves). Each dialog has a one-line scope
  description (only active/approved customers, only active items, only rates effective on the document date).
- `real-api-sales-pricing.spec.ts` now runs every screen as `sales`; `admin` only arranges MD data and the approval flow.

## Consequences

- A sales user can complete quotations and orders without master-data rights; the forms no longer offer records the
  server would reject.
- Changing the document date after picking a rate keeps the chosen rate; if it is not effective on the new date the
  save returns `MD.TAX_RATE.INACTIVE` as before (P3, noted in the UX review).
- MD list endpoints are no longer used by SD forms; MD permissions stay unchanged.
