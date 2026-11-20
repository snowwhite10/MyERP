# ADR-142: Sales order submit and SYS final approval share one FI-locked credit gate

Date: 2026-11-20. Status: Accepted (WU-SD-003b1).

## Context

WU-SD-003b (order credit check, SYS approval/rejection/cancel and concurrency) was estimated above 90 minutes, so it is split into 003b1 (internal submit service, SYS result handler, real SQL), 003b2 (authorised HTTP submit/cancel with OpenAPI) and 003b3 (HTTP-level race/cancel regression and full gate). The FI owner `ICustomerCreditService` (lock + dated snapshot) already exists, and `SalesCreditRules` (ADR-104) evaluates/decides offline. Override (`SAL.CREDIT.OVERRIDE`, audit table `sal_credit_override`) is WU-SD-006b.

## Decision

- `SalesOrderCreditCheck` is the one SD credit gate. It never reads FI tables: it calls `ICustomerCreditService.GetSnapshotAsync` with `IClock.Today` inside the caller's transaction after `AcquireCreditLockAsync`. SD commitments are the saved `base_gross` of the same company/customer's active `Approved` orders (index seek on `ix_sal_order_company_id_customer_id_business_date`). No SD invoice exists yet, so no commitment is released by invoicing; Pending, Rejected, Draft and Cancelled orders contribute nothing.
- Lock order is FI credit key → order row (`UPDLOCK, HOLDLOCK, FORCESEEK`), in READ COMMITTED as the FI service requires. The customer ID used for the FI key is read unlocked and rechecked after the order lock; a mismatch is 409.
- Submit (`SalesOrderSubmissionService`, `SAL.ORDER.SUBMIT` action scope, version, Draft only) runs the gate before calling SYS, so a refusal leaves no SYS round and no order change. Zero-step SYS approval saves `Approved` and `approved_at` in the same transaction.
- The SYS final approval handler (`SalesOrderApprovalResultHandler`) takes the same locks and rechecks with fresh figures; a refusal throws, so SYS rolls back its decision and the order stays Pending. Rejection does not take the FI lock and releases nothing.
- An inactive/unapproved or missing MD customer maps to `SAL.CREDIT.CUSTOMER_INACTIVE`; a company base currency different from the order's saved base currency is 422 `SAL.ORDER.BASE_CURRENCY_CHANGED`. Over-limit is always 422 `SAL.CREDIT.LIMIT_EXCEEDED` with `excess`/`currency` until 006b adds the reasoned override; no placeholder exposure exists.
- The submit result returns the evaluated assessment (limit, AR, other commitments, candidate, headroom) so the HTTP layer can show the decision; it is not persisted in 003b1 (saved decision evidence belongs to 006b).

## Consequences

- Concurrent approvals of one customer serialize on the FI key; other customers and companies are not blocked (real SQL race test).
- FI credit-limit changes and future SD invoice transfers must use the same FI key to keep the invariant.
- Cancellation reduces exposure and therefore does not need the FI lock (003b2).
- Document result handlers must not constructor-inject services that depend on `IApprovalWorkflowService` (for example MD `CustomerService` behind `ICustomerLookup`). SYS `ApprovalWorkflowService` enumerates all handlers, so such a dependency forms a factory-registered DI cycle that hangs resolution (it hung the full integration run in this unit). The order handler receives `Func<SalesOrderCreditCheck>` and resolves it when a decision arrives; `SalesModuleTests.ApprovalResultHandlersDoNotInjectServicesThatDependOnTheSysWorkflow` guards the rule.
