# ADR-174: Read-only credit panel after saving a sales order (WU-SD-006c3a)

Date: 2026-11-23. Status: Accepted (WU-SD-006c3a).

## Context

The WU-SD-006d design asks for a compact credit panel on Sales Order View, Create and Edit. View 2.4 got it inside
the submit area (ADR-172). The read-only credit check (ADR-170) needs a saved order id to treat the order as the
candidate; without `orderId` the endpoint needs `SAL.CREDIT.VIEW`, which the sales role does not hold. A live preview
of unsaved form input would need a new "what-if" endpoint and would show numbers that differ from what submit checks.

WU-SD-006c3 was too large for one round (panel + overall QA + real API E2E), so it is split into **006c3a** (this
panel) and **006c3b** (real API E2E: sales submits over limit with an override, manager final-approves with a reason,
SQL check of `sal_credit_override` Submit/FinalApproval rows; plus overall 006c QA).

## Decision

- **Preview after save only.** Create 2.5 and Edit 2.6 show `erp-sales-order-credit-panel` inside the confirmed save
  result (after the receipt read matched). It calls `GET sal/credit-checks/{customerId}?orderId={savedId}` once per
  saved order; a new saved order cancels the old request. Nothing appears before a save, after an uncertain/failed
  save or on a conflict.
- **Advisory wording.** Title "Credit preview for saved order {number}"; the notice says only the saved version was
  previewed and nothing was submitted or saved, shown only next to the figures. Over limit shows the excess and, for
  users with `SAL.ORDER.SUBMIT`, that submitting from the order detail needs an override reason (or the override
  permission). Submitting stays on 2.4; the panel has only "Recheck credit".
- **Errors.** Same codes as the submit area: non-overridable blocks (`CREDIT_BLOCKED_CODES`) say the order cannot be
  submitted until fixed; other failures say the order itself is saved. 5xx shows the trace ID, 403 the permission
  message, timeout (6 s) its own text. A receipt without valid ids shows a failure instead of throwing.
- **Shared figures.** The figure list (date, currency, status tag, limit, posted open AR, other approved orders, this
  order, remaining) moves to `erp-sales-credit-figures` (`credit-figures.ts`), used by both the submit area and the
  new panel, keeping the existing test IDs.
- **E2E default.** The shared Playwright fixture answers `sal/credit-checks/{id}?orderId=` with a within-limit reply
  so pages that save orders do not hit the static server; specs register their own route to test other results.

## Consequences

- No backend change, no migration. Interface items REQ_IO_A2.5.21 and REQ_IO_A2.6.18.
- Users see credit right after saving but must open the order detail to submit; the panel never claims approval.
