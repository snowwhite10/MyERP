# ADR-191: Sales return SYS final decision and Rejected revision (WU-SD-008a5b)

Date: 2026-11-23. Status: Accepted (WU-SD-008a5b).

## Context

ADR-190 delivered SUBMIT and the shared posting step (`SalesReturnPosting`). With an applicable SYS step the return
stays Pending; SYS calls the document module's `IApprovalResultHandler` inside its decision transaction when the last
step approves or any step rejects. Trigger 51035 already allows Pending → Rejected/Posted and Rejected → Draft; trigger
51036 freezes lines of a Rejected return. SYS builds every handler when the workflow is resolved, and INV/FI/MD services
depend (directly or through MD) on that workflow (ADR-142 DI cycle).

## Decision

1. `SalesReturnApprovalResultHandler` (document type `SAL.RETURN`) takes `MyErpDbContext` eagerly and
   `Func<SalesReturnPosting>`, `Func<SalesReturnDraftBuilder>`, `Func<ICustomerCreditService>` lazily (resolved only
   when an Approved result arrives). It requires the caller-owned SYS transaction.
2. **Rejected**: load the return (lines, allocations); it must be Pending and linked to this SYS instance, else
   409 `SAL.RETURN.APPROVAL_STATE_CHANGED`; set Rejected (saved by SYS). INV and FI are never called; nothing was
   reserved, so nothing is released.
3. **Approved**: same lock order as SUBMIT (BR-SD-047): unlocked read of company/order/customer (immutable) → FI
   customer credit key (`AcquireCreditLockAsync` with the SYS `DbTransaction`) → order row → return row (UPDLOCK,
   HOLDLOCK) → load Pending return of this instance (else 409 `APPROVAL_STATE_CHANGED`) → order with lines read
   without the SD action scope (the approver acts through SYS authority) → `RecheckAsync` (delivery/invoice line locks,
   409 `SNAPSHOT_STALE` when balances moved) → MD destination guards → `PostAsync`. Any exception rolls back the SYS
   decision with SD, INV and FI; the return stays Pending and SYS stays at its step. A stale Pending return must be
   rejected by the approver and revised by the user.
4. **Revision**: `SalesReturnDraftCommandService.UpdateAsync` also accepts a Rejected return (same `SAL.RETURN.EDIT`
   scope, version and request-key rules). It first saves Rejected → Draft on its own (lines of a Rejected return are
   frozen), then replaces the lines and re-snapshots as a Draft edit; the version advances by two. The SYS link stays
   until the next SUBMIT opens a new SYS round (SYS keeps history). Delete stays Draft-only; SUBMIT of a Rejected
   return stays 422 `INVALID_STATUS` (revise first).

## Consequences

- No schema or permission change. New code `SAL.RETURN.APPROVAL_STATE_CHANGED` (409) needs i18n with 008a6/008b.
- Tests SD-RETURN-APPROVE-001～004 drive decisions through the real `ApprovalWorkflowService` with the handlers the
  module registers, real FI and SD commitments; INV is the recording double.
