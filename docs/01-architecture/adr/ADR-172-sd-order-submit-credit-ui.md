# ADR-172: Order submit area with credit preview and override reason (WU-SD-006c1)

Date: 2026-11-23. Status: Accepted (WU-SD-006c1).

## Context

The order submit (ADR-143), submit override (ADR-169) and read-only credit check (ADR-170) existed only as APIs.
The Sales Order View Interface 2.4 had no submit action. WU-SD-006c (all credit screens plus the SYS approval
reason field) was estimated above 90 minutes.

## Decision

- Split WU-SD-006c into 006c1 (order submit area on Interface 2.4, this ADR), 006c2 (SYS approval task page
  `overrideReason` for `SAL.ORDER` final approval, ADR-171) and 006c3 (credit panel on Create/Edit and the
  overall credit UX QA).
- The submit area lives in the saved order detail as a standalone component (`erp-sales-order-submit-panel`). It
  is shown only for a Draft with `SAL.ORDER.SUBMIT`; a Rejected order shows "edit and save first" (the API only
  submits Draft).
- Opening it calls `GET credit-checks/{customerId}?orderId=` once (a SUBMIT holder may check its own order). The
  figures are labelled as a preview; nothing is called a success.
- Over the limit (preview `OverLimit`, or a submit refusal 422 `SAL.CREDIT.LIMIT_EXCEEDED` with `excess`):
  - with `SAL.CREDIT.OVERRIDE`, a required reason input (trimmed, max 200) is shown and the reason is sent;
  - without it, confirm is disabled with the reason shown. The server stays the authority.
- A non-overridable preview or refusal (`LIMIT_NOT_SET`, `UNAVAILABLE`, `INVALID_SNAPSHOT`, `INVALID_AMOUNT`,
  `CUSTOMER_INACTIVE`, `SAL.ORDER.BASE_CURRENCY_CHANGED`) disables confirm until a recheck succeeds. Other preview
  failures (500, timeout) leave submit available, because the server rechecks anyway.
- Success only when the reply is this order, a newer version and Pending/Approved. Pending and zero-step Approved
  have different messages; a saved override is restated with its excess and reason. The detail is re-read and the
  list row synced.
- 409 version conflict, 404, `SAL.ORDER.INVALID_STATUS`, 5xx, timeout and network pause the area until the saved
  order is reloaded; an uncertain outcome never says "nothing was saved". 409 `FI.CREDIT.LOCK_TIMEOUT` is
  retryable and says nothing changed.
- Any newly loaded order (same version or not) resets the area, so a reload always clears a paused state.

## Consequences

- REQ_IO_A2.4.10–15 allocated. The SYS approval page still cannot send `overrideReason` (006c2).
- No backend or migration change. Added `errors.SAL.ORDER.BASE_CURRENCY_CHANGED` in three languages.
