# ADR-181: SD invoice frontend split and invoice source reads (WU-SD-007b / 007b0)

Date: 2026-11-23. Status: Accepted (WU-SD-007b0).

## Context

WU-SD-007b ("invoice View/Create pages, invoiceable balance, errors and Playwright QA") is too large for one round.
The backend can issue, list and show invoices (ADR-178～180), but there is no endpoint that tells the issue form which
orders and Posted delivery lines may still be invoiced and how much remains. The 007d design (api.md) planned
`GET /api/v1/sal/deliveries/{id}/invoiceable-lines`, a per-delivery read. An invoice, however, may combine lines of
several Posted deliveries of **one order** (BR-SD-037, SOURCE_MIXED), so the form picks an order first.

## Decision

1. Split WU-SD-007b into:
   - **007b0** (this ADR) backend source reads;
   - **007b1** invoice list + detail pages (new interface numbers and REQ_IO_A, `GET` list/{id}, three languages,
     light/dark);
   - **007b2** issue form (POST; the client generates a UUID `requestKey` per form and resends the same key on retry;
     422 BALANCE_EXCEEDED shows `lineNo`/`remaining`; 500 shows `errorCode` + `traceId`);
   - **007b3** Playwright E2E and the `myerp-ux-review` QA.
   The parent 007b is complete when all four are.
2. Replace the planned per-delivery read with two per-order reads under the invoice resource, both requiring
   **`SAL.INVOICE.ISSUE`** whose SYS data scope is applied to the order exactly as ISSUE applies it, so the form never
   offers a source that POST would hide as 404:
   - `GET /api/v1/sal/invoices/source-orders?keyword=&customerId=&sort=&page=&pageSize=` →
     `PagedResult<SalesInvoiceSourceOrderSummary>` (`id, documentNo, customerId, customerCode, customerName,
     businessDate, currencyCode, openLineCount, version`). Only **Approved** orders with at least one open line;
     scope and the open condition are applied before counting. `keyword` (trimmed) matches the order number or the
     customer code/name snapshot. Sort exactly one of `documentNo`/`businessDate` (default `-businessDate`), the order
     ID breaks ties in the same direction. `customerId ≤ 0`, unknown or multiple sort fields → 400.
   - `GET /api/v1/sal/invoices/source-orders/{orderId}` → `SalesInvoiceSourceOrder` (order header the invoice copies:
     customer, currency, base currency, rate, version; plus `lines`). Each line: `deliveryId, deliveryDocumentNo,
     deliveryDate, deliveryLineId, deliveryLineNo, orderLineId, orderLineNo, itemId, itemCode, itemName,
     taxRatePercent, postedQuantity, invoicedQuantity, remainingQuantity`, ordered by delivery date, delivery ID, line
     number. Missing, deleted, other-company or out-of-scope order → 404 `SAL.INVOICE.SOURCE_NOT_FOUND`; not Approved
     → 422 `SAL.INVOICE.ORDER_NOT_APPROVED` (same codes as ISSUE, no new i18n). A fully invoiced order → 200 with no
     lines.
3. An **open line** is a line of a Posted, non-deleted delivery whose saved quantity exceeds the sum of every saved
   invoice line referencing it (invoices are never deleted) — the same formula ISSUE checks under lock (BR-SD-038).
   The reads take no locks and write nothing; the balance is advisory and ISSUE may still answer 422
   BALANCE_EXCEEDED after a concurrent issue.
4. VIEW alone cannot read sources (sources exist only to issue). No new table, migration, permission or error code.

## Consequences

- The 007d per-delivery route is not built; api.md records the replacement.
- 007b2 builds its order picker on `source-orders` and its line grid on `source-orders/{id}`; the invoice date must
  not be earlier than the latest `deliveryDate` among the chosen lines (BR-SD-055), which the form can check before
  POST.
