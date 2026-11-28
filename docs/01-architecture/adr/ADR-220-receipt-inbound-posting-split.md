# ADR-220 — Purchase receipt inbound posting before lot/serial capture

Date: 2026-11-28T16:04:47+08:00

Status: Accepted

## Context

WU-PUR-007b covers SYS final approval, zero-step SUBMIT, INV inbound posting, cost, replay/rollback and lot/serial numbers. Receipt lines have no tracking storage yet; adding it needs a schema, draft API input and validation, which together with posting exceeds one 90-minute work unit. ADR-219 left two temporary refusals (`PUR.RECEIPT.APPROVAL_FLOW_REQUIRED`, `PUR.RECEIPT.POSTING_NOT_AVAILABLE`) that must be replaced.

## Decision

- Split 007b into 007b1 (posting for untracked items, this unit) and 007b2 (lot/serial capture on receipt lines and passing it to INV).
- A shared `PurchaseReceiptPosting` step is used by the SYS result handler (final approval) and by SUBMIT when SYS auto-approves with zero steps. In the caller's transaction and with the ADR-219 lock order it rebuilds the transfer from saved rows (`PurchaseReceiptApprovalAssembly`), moves this receipt's pending quantity to `posted_received` on order lines, sets each receipt line `posted_received` to its quantity (invoice-matching evidence), posts `PURCHASE_INBOUND` through `IExternalInventoryPostingService` (source = receipt ID, saved business date, line number, material, warehouse, no location, saved base-currency unit cost) and marks the receipt Posted.
- An INV replay (movements already exist for the receipt) is a 409 `PUR.RECEIPT.POSTING_CONFLICT`, never a second success. A Posted or otherwise non-pending receipt returns 409 `PUR.RECEIPT.APPROVAL_STATE_CHANGED`.
- The handler resolves the posting step lazily (`Func<PurchaseReceiptPosting>`), as SD does (ADR-142), because INV posting needs MD services and SYS builds all handlers with the workflow.
- PUR references `MyERP.Modules.Inventory.Contracts` only. No movement-ID column is added; INV's idempotency key (company, source type, receipt ID, line number) identifies the movements.
- Until 007b2, a lot/serial-tracked item is refused by INV with 422 `INV.TRACKING.REQUIRED` and the whole action rolls back.

## Consequences

Untracked receipts can now be approved and stock is posted atomically with SYS and PUR. Tracked items cannot be received until 007b2. 007b2 must add a tracking table for receipt lines, draft input and validation, and pass tracking to `PurchaseInboundLine.Tracking`.
