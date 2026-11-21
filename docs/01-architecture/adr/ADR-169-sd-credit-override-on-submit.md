# ADR-169: Credit-limit override on order submit (WU-SD-006b1)

Date: 2026-11-22. Status: Accepted (WU-SD-006b1).

## Context

WU-SD-006b ("order submit and SYS final approval recheck FI exposure and SD commitments, with reasoned override and
audit") was still one P1 item. Most of its credit transaction already exists: WU-FI-001a/b delivered the FI-owned
`ICustomerCreditService` (credit key lock and snapshot), and WU-SD-003b1/b2 (ADR-142/143) run the credit check under
that lock on submit and on the SYS final decision, refusing any over-limit result. What is missing is the reasoned
override (BR-SD-034), its append-only evidence (`sal_credit_override`, BR-SD-036) and the read-only credit check
endpoint (`GET credit-checks/{customerId}`, BR-SD-035). Together these take longer than one 90-minute round.

WU-SD-006a ("FI/AR credit limit and open AR public contract, same-transaction credit lock, real SQL") was still marked
TODO, but its whole scope was delivered by WU-FI-001a (`ar_credit_key`, `ar_credit_limit`, `ar_posted_receivable`,
`ICustomerCreditService`, ADR-073) and WU-FI-001b1/b2 (locked writes, HTTP, permissions).

## Decision

- WU-SD-006a is closed as DONE by reference to WU-FI-001a/b1/b2. Nothing new is built for it.
- WU-SD-006b is split:
  - **006b1 (this unit)**: override on submit. `POST orders/{id}/submit` takes an optional `creditOverrideReason`.
    The decision order is the one in `SalesCreditRules.Decide` (ADR-104): blocked → 422, never overridable; within
    limit → the reason is ignored and nothing is recorded; over limit without a reason → 422 `LIMIT_EXCEEDED`
    with `excess`; a reason without `SAL.CREDIT.OVERRIDE` → 403 `SAL.CREDIT.OVERRIDE_FORBIDDEN`; a reason of 0 or
    more than 200 characters after trimming → 400 on `creditOverrideReason`.
    The grant must be `SAL.CREDIT.OVERRIDE` for the current company, with a SYS data scope that covers the order
    (`SalesActionAccess.AllowsAsync`). It never replaces `SAL.ORDER.SUBMIT`. The seeded SALES role gets it, because
    it holds all SD actions. That matches `SAL.PRICING.OVERRIDE`.
  - **006b2**: override at the SYS final decision. The approver's override request has to reach SD through the SYS
    decision. That needs either a SYS decision payload extension or a rule that reuses the submit-round override
    when exposure has not grown. Until then the final decision still refuses any over-limit result (ADR-142). So an
    overridden order only becomes Approved through a zero-step workflow. With approval steps, it stays Pending until
    the approver rejects it, or until 006b2.
  - **006b3**: the read-only `GET /api/v1/sal/credit-checks/{customerId}?orderId=` (SD-CREDIT-003/013/014).
- `sal_credit_override` (REQ_DS43.205) is a standard SD business table with company, `created_at`/`created_by`
  (the override actor and `IClock` time), `version` and update stamps, which are never used because the table is
  append-only. It stores:
  - `order_id`, the order version the decision was made on, the SYS `approval_instance_id` (the round), and `stage`
    (`Submit`, `FinalApproval`);
  - the FI evaluation date and limit version, the base currency, and the `decimal(19,4)` limit, open AR, other
    commitments, candidate and excess;
  - the trimmed `reason nvarchar(200)`.

  CHECK constraints:
  - `excess > 0`, and excess equals open AR + other commitments + candidate − limit, so a record can't claim an
    override for a within-limit order;
  - all amounts are non-negative;
  - the IDs and versions are positive;
  - the reason is non-blank.

  Keys and triggers:
  - Unique `(company_id, order_id, approval_instance_id, stage)`: one decision per order, round and stage. A replay
    can't add a row, because the order version has already moved on (409) before any insert.
  - Same-company FK to `sal_order`, plus the audit index `(company_id, order_id, created_at, id)`.
  - Trigger `tr_sal_credit_override_immutable` refuses UPDATE and DELETE with error 51031.
  - SYS and FI IDs are scalar, with no FK.
- The row is inserted in the submit transaction after SYS returns the round, so a later failure (SYS, save or
  concurrency) rolls back the order change, the SYS round and the override together.
- The submit response adds `credit.excess` and `creditOverride`: `{id, stage, approvalInstanceId, orderVersion,
  reason, excess, baseCurrency, overrideBy, overrideAt}`. `creditOverride` is null when no override was needed. Any
  actor or time fields the client sends are ignored.

## Consequences

- Credit control can be overridden only with saved evidence. A zero-step company can now approve an over-limit order
  under supervision.
- Multi-step approval of an overridden order needs 006b2. The front end (006c) must say that a submit override
  doesn't carry over to the final approval until then.
- No front-end change in this unit. The current order view sends `{version}` only, so nothing changes for it.
