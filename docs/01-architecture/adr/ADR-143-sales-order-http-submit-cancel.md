# ADR-143: Sales order HTTP submit returns the credit evaluation; cancel locks only the order

Date: 2026-11-20. Status: Accepted (WU-SD-003b2).

## Context

WU-SD-003b1 (ADR-142) added `SalesOrderSubmissionService` without a route. WU-SD-003b2 exposes `POST orders/{id}/submit` and `POST orders/{id}/cancel`. Open points: the submit response shape, whether cancel needs the FI credit key, how cancel serializes with delivery submission, and whether a dedicated cancel timestamp column is needed.

## Decision

- `POST api/v1/sal/orders/{id}/submit` takes `{version}` and `SAL.ORDER.SUBMIT`. It returns `{ order: SalesOrderDetail, credit: SalesOrderCreditResult }`. `credit.status` is a stable name (`WithinLimit`); the figures are limit, posted open AR, other commitments, candidate, headroom, base currency, limit version and the server evaluation date. The evaluation is shown, not persisted (saved decision evidence is WU-SD-006b). Refusals are ProblemDetails: 403 permission, 404 `SAL.ORDER.NOT_FOUND` (missing, foreign company or outside SUBMIT scope), 409 version, 422 `SAL.ORDER.INVALID_STATUS` / `SAL.CREDIT.*` (over limit carries `excess` and `currency`).
- `POST api/v1/sal/orders/{id}/cancel` takes `{version}` and `SAL.ORDER.CANCEL` (`SalesOrderCancellationService`). Read Committed: lock the order row (`UPDLOCK, HOLDLOCK, FORCESEEK`), apply CANCEL action scope, check version, then check for a non-deleted `Pending` or `Posted` delivery of the order. Lifecycle order: status first (`INVALID_STATUS` for anything but Approved), then `SAL.ORDER.HAS_DELIVERY`. It returns the saved detail.
- Cancel takes no FI credit key: cancelling only removes the order's commitment, so a concurrent approval can at worst see a higher exposure than necessary, never a lower one.
- Delivery submission already locks the source order row before checking that the order is Approved, so cancel and delivery submit serialize on the order row. A Pending delivery can only arise through that path, so the delivery check under the order lock is complete.
- No `cancelled_at` column or migration: the state trigger allows Approved → Cancelled only, and the audit columns (`updated_at`, `updated_by`) together with the version record who cancelled and when. SYS approval history is untouched (the round already finished as Approved).

## Consequences

- The UI (later work unit) can show the headroom after a successful submit and the excess on refusal without an extra request.
- If a cancel timestamp or reason becomes a requirement, add it with a migration and keep the same lock order.
- HTTP-level races (cancel vs. delivery submit, submit vs. SYS approve) and the full backend regression remain WU-SD-003b3.
