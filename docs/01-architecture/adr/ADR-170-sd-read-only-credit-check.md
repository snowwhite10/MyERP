# ADR-170: Read-only customer credit check endpoint (WU-SD-006b3)

Date: 2026-11-22. Status: Accepted (WU-SD-006b3).

## Context

BR-SD-035 and the WU-SD-006d API design ask for an advisory `GET /api/v1/sal/credit-checks/{customerId}?orderId=`
that shows the credit position before an order is submitted, changes nothing, and says that approval rechecks.
Four points were open:

1. FI's `ICustomerCreditService.GetSnapshotAsync` throws unless the FI credit key lock (`sp_getapplock`) is held
   in the same transaction, and `AcquireCreditLockAsync` inserts the `ar_credit_key` row the first time a
   customer is locked. Both would be writes on a GET.
2. `SalesCreditRules.Evaluate` required a positive candidate order ID, but a customer-level check has no order.
3. The design does not say who may check a customer without an order, or how a data scope applies to a customer.
   MD customers have no owner, branch or department, so a SYS data scope can't filter them.
4. Whether an unconfirmable position (no limit, FI unavailable, inactive customer) is a 200 with status
   `Blocked`, or an error.

## Decision

- **Lock in a transaction that is always rolled back.** The check opens its own Read Committed transaction,
  takes the FI credit key lock, then reads the FI snapshot and the Approved SD commitments through the same
  `SalesOrderCreditCheck.AssessAsync` that submit and the final decision use. It always rolls back, so a
  first-time `ar_credit_key` insert never survives. It writes no audit entry and changes no order. The lock is
  held only for the read. A concurrent FI or SD credit write makes the check wait at most the FI lock timeout,
  then return 409 `FI.CREDIT.LOCK_TIMEOUT` (retry). The FI contract stays unchanged: no lock-free read is added.
  This keeps one consistent snapshot rule.
- **Candidate order ID 0 means no candidate.** `SalesCreditRules.Evaluate` now accepts `CandidateOrderId = 0`
  as a customer-level check with candidate 0. A negative ID still throws. No commitment can have ID 0, so every
  Approved order counts.
- **Access.**
  - Without `orderId`, the caller needs `SAL.CREDIT.VIEW` in the current company. Any data scope is enough,
    because a customer has no scoped fields and only aggregate figures are returned.
  - With `orderId`, the caller needs `SAL.CREDIT.VIEW` or `SAL.ORDER.CREATE`/`EDIT`/`SUBMIT`, and the data scope of
    at least one held permission must cover that order. `SAL.ORDER.VIEW` alone is not a credit action and gets 403.
  - With none of these permissions, the check returns 403 `COMMON.PERMISSION.DENIED`.
  - The order is hidden with 404 `SAL.ORDER.NOT_FOUND` when it is unknown, soft-deleted, in another company,
    outside every held scope, or belongs to another customer.
  - An unknown customer, or one in another company, is 404 `MD.CUSTOMER.NOT_FOUND`. An unapproved or inactive
    customer is visible: the evaluation reports it as 422 `SAL.CREDIT.CUSTOMER_INACTIVE`.
  - A non-positive or malformed ID is 400 and is checked before any lookup.
- **Results.**
  - `WithinLimit` and `OverLimit` return 200 `SalesCreditCheckResponse` with these fields: `customerId`,
    `orderId`, `status`, `reasonCode` (`SAL.CREDIT.LIMIT_EXCEEDED` when over the limit), `evaluationDate` (the
    server's `IClock.Today`), `baseCurrency`, `limit`, `postedOpenAr`, `otherCommitments`, `candidate`,
    `headroom`, `excess`, `limitVersion`, and `requiresRecheck`, which is always true.
  - A blocked position returns 422 with the named `SAL.CREDIT.*` code. A saved order whose base currency no
    longer matches the company returns 422 `SAL.ORDER.BASE_CURRENCY_CHANGED`. Both match what submit would say.
  - No FI document detail is returned.
  - When `orderId` is given, that order's saved base gross is the candidate. If the order is Approved, it is
    removed from the other commitments by ID, so it is counted once.

## Consequences

- The UI (WU-SD-006c) can show a preview that matches the submit decision exactly. The preview isn't a reservation,
  and the order command still rechecks under the lock.
- A preview briefly takes the same FI lock as postings. The lock is held only for two indexed reads, so its cost is
  the same as one submit check.
