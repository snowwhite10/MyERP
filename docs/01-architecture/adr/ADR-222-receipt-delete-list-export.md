# ADR-222 — Purchase receipt DELETE, LIST and CSV export

Date: 2026-11-28T16:53:25+08:00

Status: Accepted

## Context

WU-PUR-007a10 adds the remaining receipt back-end routes needed by the 007e screens: DELETE, LIST and a server CSV. The hand-off note in `state.json.nextAction` asked for a *physical* delete with cascading tracking rows. However REQ_F4114.2.6 and the WU-PUR-007d API design both say "soft-delete Draft", the entity `PurchaseReceiptSource` already implements `ISoftDeletable` (global query filter), BR-PUR-025 order CANCEL counts "non soft-deleted receipts", and a Draft that was revised from Rejected already has SYS approval history that refers to the receipt ID. MASTER_PROMPT §6.5 *allows* physical deletion of drafts but does not require it.

## Decision

1. `DELETE /api/v1/pur/purchase-receipts/{id}?version=` (new `PUR.RECEIPT.DELETE`, EDIT does not substitute) **soft-deletes** a Draft: sets `is_deleted`/`deleted_at` (IClock) in one READ COMMITTED transaction after `UPDLOCK` on the receipt row (the same lock EDIT/SUBMIT take). Number, lines, lot/serial capture and SYS history are kept; the number is never reused. A Draft holds no order reservation and no INV movement, so nothing else changes.
2. Only `Draft` is deletable, per REQ_F4114.2.6; `Rejected` must be revised back to Draft first (unlike purchase orders, BR-PUR-025). PendingApproval/Rejected/Posted → 422 `PUR.RECEIPT.NOT_DELETABLE`; stale or zero version → 409; out of scope, other company, legacy invoice-only row or already deleted → 404.
3. `GET /api/v1/pur/purchase-receipts` (VIEW scope) and `GET /export` (new `PUR.RECEIPT.EXPORT`, VIEW ∩ EXPORT scope) follow the purchase order pattern (ADR for 006e1/e3): `keyword` matches the receipt or purchase order number, `from`/`to`, `purchaseOrderId`, `status` ∈ Draft/PendingApproval/Rejected/Posted, one sort field `documentNo`/`businessDate` (default `-businessDate`, ID tiebreak). Summary carries the purchase order number, line count, total quantity and version. Legacy invoice-only source rows (no number/date) are excluded.
4. CSV: one row per saved line, snapshot isolation, 10000-line limit (422 `PUR.RECEIPT.EXPORT_LIMIT` before writing), UTF-8 BOM, CRLF, three-language header and status, formula-safe cells (shared `PurchaseInvoiceCsv.Cell`). Decimals drop trailing zeros from SQL `decimal(19,6)`. Lot/serial numbers are not exported (detail/print only).

## Consequences

The physical-delete note is superseded; the tracking FK cascade (ADR-221) still applies when EDIT removes a line. 007e can build the list, CSV and delete actions on these routes. New permissions must be granted to roles by an administrator.
