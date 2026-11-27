# ADR-214 — Purchase order SUBMIT: explicit row locks, saved-snapshot consumption and 006b split

Date: 2026-11-28T06:16:01+08:00. Status: Accepted for WU-PUR-006b1.

## Context

WU-PUR-006b (SUBMIT, requisition consumption, SYS approval instance, SYS decision handler, HTTP, races and fault rollback) exceeds the 90-minute round boundary. ADR-213 said SUBMIT should update `ordered_quantity` "under a serializable transaction and ordered update locks". SUBMIT also calls the shared SYS `IApprovalWorkflowService.SubmitAsync`, which reads `sys_approval_instance` and the active flow and then inserts. Under SERIALIZABLE, those reads take key-range locks, so two unrelated orders submitted at the same time can deadlock on the instance insert even though they share no requisition line.

## Decision

- Split WU-PUR-006b into: **b1** SUBMIT + aggregated consumption + zero-step approval (real SQL, real SYS engine); **b2** `PUR.ORDER` SYS result handler (final approve keeps consumption, reject releases once and clears the flag, stale decisions 409); **b3** authorized HTTP for SUBMIT/decisions, ProblemDetails with `shortages`/`lineNos`, SYS-decision fault rollback. Parents are not counted.
- SUBMIT runs in a READ COMMITTED transaction with explicit `UPDLOCK, HOLDLOCK`: first on the order row, then on each source requisition line, one row at a time in ascending ID order. Every competing SUBMIT therefore takes locks in the same global order (own order → requisition lines ascending), waits, and re-reads the committed `ordered_quantity`. This gives the same no-over-consumption guarantee as SERIALIZABLE without range locks in SYS tables. ADR-213's "serializable" wording is superseded for SUBMIT only; CREATE/EDIT keep SERIALIZABLE.
- SUBMIT trusts only saved order lines (no request body except `version`). Lines that reference the same requisition line are summed before checking `ordered_quantity + requested <= quantity`. Any invalid source (missing, not Approved, soft-deleted, item mismatch) is a whole-order 422 `PUR.ORDER.INVALID_REQUISITION_SOURCE` with `lineNos`. Any shortfall is a whole-order 422 `PUR.ORDER.REQUISITION_INSUFFICIENT` with `shortages[]` (`requisitionLineId`, `orderLineNos`, `requested`, `available`). There is no partial success.
- The supplier is checked again with MD `ISupplierLookup.RequireApprovedAsync`, and items go through `IRequisitionItemGuard` for all orders, including award-converted orders. Saved prices, tax and rate snapshots are not re-priced (BR-PUR-022).
- SYS amount is the saved `base_gross`. A zero-step flow saves the order as Approved in the same transaction. Consumption happens once, and `requisition_consumption_active` stays true. The order version can advance by two in this case: one save before SYS and one save after it.
- New permission `PUR.ORDER.SUBMIT`; endpoint `POST /api/v1/pur/purchase-orders/{id}/submit` with body `{version}`.

## Consequences

Concurrent SUBMITs on the same requisition line serialize. The loser gets 422 with the real remaining quantity, or 409 if its own order changed, and it stays Draft with no SYS instance. Unrelated orders do not block each other on SYS range locks. Until b2 registers a `PUR.ORDER` result handler, an order submitted under a multi-step flow stays PendingApproval and cannot be decided. b2 is the next unit. A SYS flow for `PUR.ORDER` should not be configured in production before b2 ships.
