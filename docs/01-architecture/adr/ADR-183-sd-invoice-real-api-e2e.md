# ADR-183: Real-API invoice E2E arrange path and prefilled quantity text (WU-SD-007b3b)

Date: 2026-11-23. Status: Accepted (WU-SD-007b3b).

## Context

The real-API invoice spec needs an Approved order and a Posted delivery for a fresh customer and item. Going through
the SAL.ORDER and SAL.DELIVERY approval UIs is already covered by other specs (credit override, SD-DEL-HTTP-006) and
would make this spec depend on whichever approval flow the shared Development database currently has. Delivery
posting also needs on-hand stock for the fresh item.

The first run also showed that the Issue form 2.15 prefilled each quantity input with the raw server text
(`6.000000`) while the balance column next to it showed `6`.

## Decision

1. Arrange through the public APIs only (no SQL writes): admin creates the unit, item, warehouse, 5% output tax code,
   approved customer, a TWD credit limit and opening stock (`POST /api/v1/inv/opening-stocks`, dated yesterday);
   sales creates and submits the TWD order and the delivery. While arranging, an active `SAL.ORDER` or `SAL.DELIVERY`
   approval flow is switched off so submit approves the order and posts the delivery in the same request (ADR-144);
   `afterAll` puts every switched flow back with its original name, steps and active state.
2. The invoice itself is always issued through the 2.15 form by `sales`; SQL reads compare `sal_invoice`,
   `sal_invoice_line`, `ar_posted_receivable`, `ar_sales_source`, `gl_journal` and `gl_journal_line` (accounts from
   `gl_sales_invoice_account_map`) with the 2.14 detail. Idempotency is checked through the API with the saved
   `request_key`: identical body → 200 and no new FI rows; changed quantity → 409 `SAL.INVOICE.REQUEST_CONFLICT`.
3. New shared helper `trimDecimalText` (`shared/decimal-text.ts`) drops trailing fraction zeros without grouping or
   floating point; the invoice form prefills quantities with it (`draftFromSource`, `mergeDraft`). The submitted
   value stays exact (`10.000001` is unchanged).

## Consequences

SD-INV-REAL-001～003 pass (3/3). Screenshots `tests/qa/SD/screenshots/real-invoice-*` feed WU-SD-007b3c.
`delivery-draft-form.ts` still trims with its own inline regex; it can switch to `trimDecimalText` in a later cleanup.
The spec briefly changes shared approval flows, like `real-api-sales-credit-override.spec.ts`; real-API specs run
serially with one worker, so they do not overlap.
