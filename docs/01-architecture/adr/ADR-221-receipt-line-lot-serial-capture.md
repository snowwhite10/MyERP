# ADR-221 — Purchase receipt line lot/serial capture

Date: 2026-11-28T16:28:11+08:00

Status: Accepted

## Context

ADR-220 posted untracked receipts and left lot/serial-tracked items refused by INV (`INV.TRACKING.REQUIRED`). WU-PUR-007b2 must store lot/serial numbers on receipt lines, validate them against the MD item tracking mode, and pass them to `PurchaseInboundLine.Tracking`. SD deliveries already do this for outbound (ADR-145). A receipt is an inbound document, so it is where a lot is first created.

## Decision

1. New child table `pur_purchase_receipt_line_tracking` (REQ_DS43.222): `line_id` with a company-scoped FK to the receipt line (ON DELETE CASCADE), `seq_no`, exactly one of `lot_no` (≤ 50) / `serial_no` (≤ 100), `quantity DECIMAL(19,6)`, and optional `manufacture_date` / `expiry_date` for lots only. SQL CHECKs mirror the rules: one non-blank number, serial quantity 1 without dates, quantity > 0, seq > 0, expiry ≥ manufacture. Lengths match INV `StockLot` / `StockSerial`. No EF navigation; rows are loaded by query.
2. Draft CREATE / PUT accept `lines[].tracking`. Format errors are 400 with field paths (`PurchaseReceiptTrackingRules`, same rules as SD and INV formats, plus lot dates). PUT replaces all captures of the receipt.
3. Lines that carry captures are checked against the MD tracking mode read under the MD item/warehouse shared lock (`IInventoryReferenceGuard.RunWithCodesAsync`, held to commit): 422 `PUR.RECEIPT.TRACKING_MODE_MISMATCH` (wrong kind, untracked item, fractional serial quantity). The same serial of one item on two lines is 422 `PUR.RECEIPT.SERIAL_DUPLICATE`. A Draft may be incomplete, and lines without captures are not MD-checked at save time.
4. SUBMIT checks every saved line under the MD lock and requires a tracked line's captures to total its quantity: otherwise 422 `PUR.RECEIPT.TRACKING_REQUIRED` with `lineNo`, before any SYS instance is created. This finds the problem at SUBMIT, not at final approval in a multi-step flow. SUBMIT now also returns the MD inactive / not-found errors that INV would return at posting.
5. `PurchaseReceiptPosting` loads the saved captures and sends them as `InventoryTrackingLine` (lot with dates, or serial). INV still validates them (for example a serial already in stock) and any refusal rolls back SYS, PUR and INV together.
6. The shared helper `PurchaseReceiptTracking` (scoped) is used by the draft, submission and posting services. The SYS result handler still resolves the posting step lazily (ADR-220), so the MD dependency adds no DI cycle.

## Consequences

Lot- and serial-tracked items can now be received and approved. Lots get their manufacture/expiry dates from the first receipt. Receipt return (007c) can read the original captures from this table. The 007e screens must provide tracking input and show the `lineNo` errors.
