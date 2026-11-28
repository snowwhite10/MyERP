# ADR-219 — Purchase receipt SUBMIT before inbound posting exists

Date: 2026-11-28T15:43:06+08:00

Status: Accepted

## Context

WU-PUR-007a9 delivers receipt SUBMIT with order line capacity reservation and SYS rejection release. Final approval must post INV inbound stock and cost in the same transaction (BR-PUR-030), which is WU-PUR-007b. The SYS engine auto-approves when no active flow or applicable step exists, and it does not call result handlers for that zero-step case. Without a decision, a receipt could become Approved without stock being posted, or keep a reservation that nothing converts.

## Decision

- SUBMIT accepts only Draft (a Rejected receipt is revised by EDIT back to Draft first, matching purchase orders). Lock order: purchase order, receipt, order lines by ascending ID (UPDLOCK, HOLDLOCK) in ReadCommitted; SYS rejection uses the same order.
- The receipt's own reservation is derived from its status and saved lines (PendingApproval means all saved quantities are reserved), so no per-line reservation column is added.
- Until 007b: if SYS returns Approved during SUBMIT (zero-step), SUBMIT fails with 422 `PUR.RECEIPT.APPROVAL_FLOW_REQUIRED` and the whole transaction rolls back. The receipt result handler refuses a final approval with 422 `PUR.RECEIPT.POSTING_NOT_AVAILABLE`, so the SYS approve action rolls back and the instance stays pending.
- SYS rejection releases exactly this receipt's aggregated quantities and sets Rejected; a stale or repeated decision returns 409 `PUR.RECEIPT.APPROVAL_STATE_CHANGED` with no second release.
- The approval amount is the sum of line quantity × saved base unit cost, rounded per line to 4 decimals.

## Consequences

Receipts cannot be approved in production until 007b replaces both refusals with same-transaction INV posting (including the zero-step path). Pending receipts keep reserved capacity until rejected. 007b must remove the two refusal codes and update the tests that assert them.
