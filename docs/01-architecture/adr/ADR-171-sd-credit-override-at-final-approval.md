# ADR-171: Credit-limit override at the SYS final approval (WU-SD-006b2)

Date: 2026-11-23. Status: Accepted (WU-SD-006b2).

## Context

ADR-169 added the reasoned credit override on order submit (`sal_credit_override`, stage `Submit`). The SYS final
decision still refused every over-limit result (ADR-142), so an over-limit order in a company with approval steps
stayed Pending until rejected. ADR-169 left two options open for 006b2:

1. extend the SYS decision payload so the approver can send an override reason, or
2. reuse the submit-round override when exposure has not grown since submit.

## Decision

- **Option 1.** The approver decides and records the override in their own name. The submit override is never
  carried over, even when the exposure did not grow:
  - A submit override is the submitter's evidence for the approver. It doesn't relieve the approver, who is the
    second person in segregation of duties.
  - "Exposure not grown" needs a comparison rule and a tolerance that BR-SD-034 doesn't define. A reused row would
    also record the wrong actor.
- **SYS contract (generic, no SD knowledge).**
  - `ApprovalDecision` (`POST /api/v1/sys/approvals/{id}/approve|reject`) takes an optional
    `overrideReason`.
  - `ApprovalCompleted` takes an optional `OverrideReason`.
  - SYS doesn't interpret, trim, validate or store it. It forwards the raw text only when the decision completes
    the instance as Approved.
  - On a non-final approve step and on a rejection, the reason is ignored, so other document modules are not
    affected.
  - Comment validation (500 characters, required on rejection) doesn't change.
- **SD final decision (`SalesOrderApprovalResultHandler`).** The lock order (FI credit key, then order row) is
  unchanged. `SalesOrderCreditCheck.DecideAsync` applies the same `SalesCreditRules.Decide` order as submit:
  - blocked → 422, never overridable;
  - within limit → the reason is ignored and nothing is recorded;
  - over limit without a reason → 422 `LIMIT_EXCEEDED` with `excess` and `currency`;
  - a reason without `SAL.CREDIT.OVERRIDE` → 403 `SAL.CREDIT.OVERRIDE_FORBIDDEN`. The grant is the
    signed-in approver's (`SalesActionAccess.AllowsAsync`), and its data scope must cover the order. When a
    delegate acts on behalf of someone, the delegate's own grant counts, not the delegator's;
  - a reason that is blank, or longer than 200 characters after trimming → 400 on field `overrideReason`.

  The handler gets `SalesActionAccess` by constructor injection. `CurrentAuthorization` resolves through
  `IServiceProvider`, so it can't form the DI cycle that ADR-142 avoided for the credit gate.
- **Evidence.** One `sal_credit_override` row with stage `FinalApproval`:
  - the Pending order version the approver decided on, and the same SYS `approval_instance_id` as the submit row;
  - the fresh FI and SD figures, and the trimmed reason;
  - the approver as `created_by` and the `IClock` time.

  The row is added before SYS saves. Any later failure (SYS version conflict, save error) rolls back the SYS
  decision, the order change and the row together. The unique key (order, round, stage) and trigger 51031
  (ADR-169) still apply. A replay is refused by SYS (422 `SYS.APPROVAL.NOT_PENDING`) before SD runs.
- The SYS approve response stays `ApprovalInstanceDetail`. The override row can be read through the SD order
  (UI in 006c).
- `SalesOrderCreditCheck.RequireWithinLimitAsync` had no caller left and is removed.

## Consequences

- An over-limit order can now be approved through a multi-step workflow, with two evidence rows (Submit and
  FinalApproval) when both people overrode.
- The SYS approval UI (approval task page) must offer the reason field when the final approval of a `SAL.ORDER`
  is refused with `SAL.CREDIT.LIMIT_EXCEEDED`. This is front-end work in 006c, together with the order submit UI.
- Other document types can use the same `overrideReason` channel later without changing SYS again.
