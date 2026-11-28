# ADR-226 — Purchase return LIST and CSV export

Date: 2026-11-28T21:34:33+08:00

Status: Accepted

## Context

WU-PUR-007c5 adds the read-only routes the 007e return screens need. The hand-off asked to follow the receipt pattern (ADR-222) and to show a return amount; it named the CSV route `/export.csv`. Purchase return lines store `OriginalUnitCost` only after posting (ADR-225); Draft／PendingApproval／Rejected lines have only the receipt line's base unit cost. While testing, a soft-deleted return appeared in the CSV: EF Core's `IgnoreQueryFilters()` on any set in a query (here the purchase order／receipt joins used to read numbers of possibly soft-deleted sources) turns off **all** global filters for that query, including the return's own soft-delete filter.

## Decision

1. `GET /api/v1/pur/purchase-returns` (PUR.RETURN.VIEW scope) and `GET /api/v1/pur/purchase-returns/export` (new `PUR.RETURN.EXPORT`, VIEW ∩ EXPORT scope). The route is `/export`, not `/export.csv`, to match purchase orders, receipts and invoices; the hand-off wording is superseded.
2. Filters as receipts: `keyword` (return or purchase order number, trimmed), `from`／`to`, `purchaseOrderId` > 0, `status` ∈ Draft／PendingApproval／Rejected／Posted, one sort field `documentNo`／`businessDate` (default `-businessDate`, ID tiebreak). Bad values 400.
3. Summary: `{id,documentNo,businessDate,purchaseOrderId,purchaseOrderDocumentNo,supplierId,currency,status,lineCount,totalQuantity,returnAmount,amountEstimated,version}`. `returnAmount` is base currency, Σ per line `round(quantity × cost, 4, AwayFromZero)` (same rounding as the SUBMIT approval amount), where cost = `OriginalUnitCost` when saved, otherwise the receipt line unit cost; `amountEstimated` is true when any line lacks `OriginalUnitCost`.
4. CSV: one row per saved line, snapshot isolation, 10000-line limit (422 `PUR.RETURN.EXPORT_LIMIT` before any byte or header), UTF-8 BOM, CRLF, three-language header／status／cost source ("Original receipt cost" vs "Receipt unit cost (estimate)"), formula-safe cells. Columns: return no., date, status, PO no., supplier ID, line no., receipt no., receipt line no., item ID, warehouse ID, quantity, base unit cost, base amount, cost source. Lots/serials are not exported (detail／print only).
5. Every return query states `!IsDeleted` explicitly instead of relying on the global filter, because the joins need `IgnoreQueryFilters()`.

## Consequences

007e can build the return list, CSV and amount column with an "estimate" marker. The new permission must be granted by an administrator. The same `IgnoreQueryFilters` leak likely affects the receipt list keyword search (ADR-222, `PurchaseReceiptQueryService`): a follow-up backlog item (WU-PUR-007a11) verifies and fixes it with a real-SQL test.
