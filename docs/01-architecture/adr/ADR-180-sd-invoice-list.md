# ADR-180: SD invoice list endpoint (WU-SD-007a3b2)

Date: 2026-11-23. Status: Accepted (WU-SD-007a3b2).

## Context

ADR-179 split the invoice HTTP work; the list `GET /api/v1/sal/invoices` remained. Invoices have no owner of their
own, like deliveries (ADR-138), and are always Posted once committed.

## Decision

1. `GET /api/v1/sal/invoices` needs `SAL.INVOICE.VIEW`. The SYS data scope is applied to the **source order** by a
   join before counting and paging, so hidden invoices are neither listed nor counted; only `Posted` rows are read
   (Issuing never commits; the filter is a guard). Company isolation comes from the global company filter.
2. `SalesInvoiceQuery : PagedRequest` adds `from`/`to` (inclusive invoice date, `from > to` → 400 `to`), `orderId`,
   `customerId` (≤ 0 → 400). `keyword` (trimmed) matches the invoice or source order number. No status filter: every
   visible invoice is Posted.
3. Sort: exactly one of `documentNo`, `businessDate`, optional `-`; default `-businessDate`; the invoice ID breaks
   ties in the same direction so pages are stable. Unknown or multiple fields → 400 `sort`.
4. `SalesInvoiceSummary`: `id, documentNo, orderId, orderDocumentNo, customerId, customerName (order snapshot),
   businessDate, currencyCode, baseCurrencyCode, originalGross, baseGross, postedAt, fiJournalId, lineCount,
   version` — values equal the detail (`lineCount` = detail line count). No FI table is read.

## Consequences

- WU-SD-007a3b (parent) is complete; the invoice pages (WU-SD-007b) can list, show and issue invoices.
