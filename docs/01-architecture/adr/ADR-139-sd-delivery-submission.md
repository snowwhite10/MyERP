# ADR-139: Sales delivery submission, withdrawal and SYS decisions before INV posting

Date: 2026-11-16. Status: Accepted (WU-SD-004a2b13).

## Context

A Pending delivery is the only reservation of order-line balance (ADR-138 point 3). Submission must lock the source rows, reread every saved row and recheck the full balance in one transaction with the SYS submission. `SalesDeliverySelectionStore` required a Serializable transaction, which ADR-138 had already shown to deadlock across orders because range locks cover other orders' rows.

Final approval and zero-step submission must post stock through INV (`PostSalesOutboundAsync`), which is WU-SD-004b. Without a result handler for `SAL.DELIVERY`, SYS would silently approve an instance and leave the delivery Pending forever.

## Decision

1. `SalesDeliverySubmissionService` runs in **Read Committed**. Lock order: source `sal_order` primary key (`UPDLOCK, HOLDLOCK, FORCESEEK`) → `sal_delivery` primary key → each distinct `sal_order_line` in ascending ID (one statement per ID so the order is deterministic). All delivery commands for one order already serialise on the first lock; the line locks protect against future order-line writers.
2. `SalesDeliverySelectionStore` accepts any caller-owned transaction (Serializable no longer required). Its header and line reads use `FORCESEEK` with the explicit company ID, so another order's or company's uncommitted rows never block it. The caller's order and delivery locks keep the COUNT and the row read on the same saved rows.
3. After the locks, the service rereads the order lines, the current delivery lines and every Posted/Pending line of the same order (seeks only) and calls `SalesDeliverySelection.CheckLockedDecision`. MD references and tracking are rechecked through `IInventoryReferenceGuard`; tracked items stay 422 `SAL.DELIVERY.TRACKING_UNSUPPORTED` until WU-SD-004a2b14.
4. The SYS amount is the base gross value of the delivered share: Σ(order-line base gross × delivered quantity ÷ ordered quantity), rounded with `Money.Round` in the order's base currency.
5. SYS saves its instance first; the delivery then becomes Pending with `ApprovalInstanceId` in one save, so one submission advances the version by exactly 1.
6. **Zero-step SYS approval is refused** with 422 `SAL.DELIVERY.POSTING_UNAVAILABLE`; the transaction, including the SYS instance, rolls back and the delivery stays Draft. WU-SD-004b replaces this with posting in the same transaction.
7. `SalesDeliveryApprovalResultHandler`: Rejected → delivery Rejected (allowed even if the order is no longer Approved, releasing the reservation). Approved → 422 `SAL.DELIVERY.POSTING_UNAVAILABLE`, so the SYS decision rolls back and the delivery stays Pending until 004b.
8. Withdraw: Pending → Draft (new lifecycle action `Withdraw`), cancels the SYS round, needs `SAL.DELIVERY.SUBMIT` and ignores the order state (it only releases balance). Routes: `POST /api/v1/sal/deliveries/{id}/submit` and `/withdraw`, body `{version}`.

## Consequences

- Two submissions for the last balance of one order serialise on the order lock; the second sees the committed reservation and gets 422 `ORDER_BALANCE_EXCEEDED`. If the first rolls back, the waiting one succeeds. Submissions for other orders and companies are not blocked (real-SQL tests).
- Until 004b, a configured flow can collect approvals up to the last step, but the last approval fails with 422; administrators see the error in the SYS approval UI. This is intentional: a silently "approved" delivery without stock movement would be worse.
