# ADR-148: Sales outbound create page — option sources, advisory balance and receipt confirmation

Date: 2026-11-21. Status: Accepted (WU-SD-004c2a).

## Context

Interface 2.8 (Sales Outbound Create) needs an Approved order with its per-line remaining balance, and warehouse and storage-location choices. The SD design (api.md, WU-SD-004d) says CREATE/EDIT options "must be authorised for that action without requiring DELIVERY.VIEW". No action-scoped option endpoints exist yet. The existing reads are:

- `GET /api/v1/sal/orders?status=Approved` and `GET /api/v1/sal/orders/{id}` (`SAL.ORDER.VIEW`; the detail has `remainingQuantity` per line, WU-SD-003c2);
- `GET /api/v1/md/warehouses` and `.../{id}/locations` (`MD.WAREHOUSE.VIEW`).

`POST /api/v1/sal/deliveries` returns the saved Draft through `DescribeAsync`, so a CREATE-only user receives it without VIEW (ADR-147). Line-level 422 errors carry the ProblemDetails extension `lineNo`; `SAL.DELIVERY.ORDER_BALANCE_EXCEEDED` is aggregated per order line and has no `lineNo`.

WU-SD-004c2 was estimated at more than 90 minutes and was split: c2a is the create page; c2b is edit, delete, submit, withdraw, lot/serial selection and the warehouse/location names in the list.

## Decision

1. The create page uses the existing VIEW reads above. It needs no DELIVERY.VIEW. A user without `SAL.ORDER.VIEW` or `MD.WAREHOUSE.VIEW` sees the picker disabled, with text naming the missing permission. Action-scoped option endpoints are recorded as backlog WU-SD-004c4.
2. The order balance is read as exact decimal text and shown as advisory, with a notice that the server rechecks. Lines are prefilled only for order lines with a positive balance, with that balance as the suggested quantity. Before POST the client totals the lines for each order line and refuses a total above the advisory balance. The server's check stays authoritative.
3. Success is shown only when the 201 body is a `Draft` with a number, the same order and date, and every line (line number, order line, warehouse, location, exact quantity) matches what was sent. There is no follow-up GET, so CREATE-only users can create drafts. A mismatch, timeout, network failure or 5xx is reported as "not confirmed", and the page locks against a duplicate save. The message tells users with VIEW to search the list, and tells CREATE-only users to ask a colleague.
4. A 422 with `lineNo` highlights that line and keeps all input. `ORDER_BALANCE_EXCEEDED` offers "Reload latest balance". This rereads the order and keeps the quantities and warehouses already entered, so the user can adjust them.
5. Lot/serial selection is not on the create page. Draft save accepts incomplete tracking (ADR-145), and submit requires complete tracking. Both belong to WU-SD-004c2b.

## Consequences

- Warehouse clerks need `SAL.ORDER.VIEW` and `MD.WAREHOUSE.VIEW` to create outbounds until WU-SD-004c4 delivers action-scoped options.
- Delivery line numbers keep their original values when a line is removed. They only need to be positive and unique.
- No backend or database change.
