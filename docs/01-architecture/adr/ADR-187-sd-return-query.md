# ADR-187: Scoped sales return detail and list query (WU-SD-008a2b2)

Date: 2026-11-23. Status: Accepted (WU-SD-008a2b2).

## Context

ADR-186 left the read side of Draft returns to 008a2b2. Returns store the customer ID but not the customer code/name;
the source order carries the snapshot. Draft, Pending, Rejected and Posted returns all exist in `sal_return`; only a
Draft can be soft-deleted. The HTTP routes arrive in 008a6 and the UI in 008b, so this unit is the service only.

## Decision

1. `SalesReturnQueryService` requires `SAL.RETURN.VIEW`; its SYS data scope is applied to the **source order** (same
   as invoices, ADR-180) by a join that precedes counting, so hidden returns are neither listed nor counted.
2. List (`SalesReturnQuery`): keyword (trimmed) on return number, order number or the order's customer name snapshot;
   inclusive `from`/`to` on the return date (`from > to` → 400 `to`); `orderId`/`customerId` (≤ 0 → 400); `status`
   (any stored state); one sort field `documentNo` or `businessDate`, default `-businessDate`, return ID breaking ties
   in the same direction. Rows carry order number, customer name, currencies, status, original and base gross, posted
   time, line count and row version.
3. Detail lists the header (request key, remark, approval instance, posted time, FI credit note, totals, version) and
   per line the delivery number, order line number, item code/name (order line snapshot), destination, quantity,
   unbilled and derived billed quantity, original outbound and INV inbound movements, amounts and invoice allocations
   with the invoice number. Source order/deliveries are read ignoring soft delete because the return keeps referring
   to them.
4. Soft-deleted, other-company, out-of-scope and unknown returns are the same 404 `SAL.RETURN.NOT_FOUND`.
5. `DescribeAsync` is public so the create/edit commands (008a6) can answer with the saved shape without VIEW, as the
   invoice ISSUE command does.

## Consequences

- No schema, permission-catalog or error-code change (`SAL.RETURN.VIEW` and `NOT_FOUND` were already planned).
- 008a6 binds `GET /api/v1/sal/returns` and `GET /api/v1/sal/returns/{id}` to this service and maps 400/403/404.
