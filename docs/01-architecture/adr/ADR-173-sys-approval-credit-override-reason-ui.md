# ADR-173: Credit override reason on the approval task page (WU-SD-006c2)

Date: 2026-11-23. Status: Accepted (WU-SD-006c2).

## Context

ADR-171 lets the approver of the final `SAL.ORDER` step send `overrideReason` with
`POST /api/v1/sys/approvals/{id}/approve`. The Approval Inbox Interface 8.70 had no way to send it, so an
over-limit order could only be rejected from the page. SYS stays generic: it does not know whether a document is
over its credit limit before the decision.

## Decision

- **Reactive, no pre-check.** The page doesn't call SD before approving. The approve dialog sends the decision as
  before. Only when the server refuses with 422 and a code listed in `OVERRIDABLE_REFUSALS`
  (`approval.service.ts`; today only `SAL.CREDIT.LIMIT_EXCEEDED` → permission `SAL.CREDIT.OVERRIDE`) does the
  dialog show:
  - a warning with the server's `excess` and `currency`, saying the approval was not saved;
  - a required "Credit override reason" textarea (asterisk, counter, max 200 after trimming, `autocomplete="off"`),
    focused when it appears.
  A new code can be added to the map without changing the dialog.
- **Permission.** Without the mapped permission (the signed-in approver's own grant, as on the server), the
  reason field is not shown and Approve is disabled. The warning explains why and suggests rejecting or handing over
  to an approver who holds the permission. The server stays the authority: 403 `SAL.CREDIT.OVERRIDE_FORBIDDEN`
  is shown in the dialog.
- **Validation.** A blank reason or one over 200 characters is not sent. The error sits under the field, and the
  field gets focus. A 400 that names `overrideReason` is shown under the field, not as a top error.
- **Request.** `overrideReason` (trimmed) is in the body only when the dialog asked for it. Other approvals and
  rejections keep the old body `{ comment, version }`, so other document types are not affected.
- **Other refusals.**
  - Credit refusals that can't be overridden (`LIMIT_NOT_SET`, `UNAVAILABLE`, `INVALID_SNAPSHOT`,
    `INVALID_AMOUNT`, `CUSTOMER_INACTIVE`, `SAL.ORDER.BASE_CURRENCY_CHANGED`) remove the reason field and show the
    named cause.
  - `FI.CREDIT.LOCK_TIMEOUT` is named and can be retried.
  - 5xx errors show the trace ID through the shared `erp-api-error-display`.
  - After a 5xx, timeout or network error the outcome is uncertain. The warning drops "not saved" and asks the user
    to refresh the tasks first. A replay is refused by SYS (`NOT_PENDING`), so retrying cannot approve twice.
- **Success.** When a reason was sent and the reply is `Approved`, the notice reads "Approved {no} and overrode
  the credit limit by {excess} {currency} with your reason". The task list is reloaded.
- Reopening the dialog (approve or reject) clears all override state.

## Consequences

- REQ_IO_A8.70.3 (Credit Override Reason) and REQ_IO_A8.70.4 (Credit Excess alert) are added to the SYS
  ui-spec. There is no backend change and no migration.
- The SYS front end lists the SD and FI credit error codes it explains (`APPROVAL_ERROR_CODES`). This is a
  front-end message mapping only; SYS backend still knows nothing about SD.
- The overall credit QA with a real-API E2E (submit override → SYS final approval override) is WU-SD-006c3.
