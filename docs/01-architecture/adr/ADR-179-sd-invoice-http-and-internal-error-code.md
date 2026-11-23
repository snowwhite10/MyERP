# ADR-179: SD invoice ISSUE/detail HTTP and named internal errors keep their code (WU-SD-007a3b1)

Date: 2026-11-23. Status: Accepted (WU-SD-007a3b1).

## Context

WU-SD-007a3b combined `POST /api/v1/sal/invoices`, the invoice list with filters, the detail, the 500 mapping of
`SalesInvoicePostingException`, i18n and authorised HTTP tests. With the list it exceeds one work unit. The global
handler only kept `errorCode` for `ErpException` (4xx); any other exception became 500 `COMMON.SERVER.INTERNAL_ERROR`,
so support could not tell an FI fault from a receipt mismatch.

## Decision

1. Split WU-SD-007a3b into **007a3b1** (ISSUE POST, GET `{id}`, 500 mapping, i18n, HTTP tests) and **007a3b2**
   (`GET /api/v1/sal/invoices` list with filters). The parent counts as done only when both are.
2. Shared: new `ErpErrorKind.Internal` and `InternalErrorException(errorCode, message)` (non-sealed). The factory maps
   it to 500 `type …/internal` with the localized generic title, **its `errorCode`** and **no detail** (the message may
   hold internal facts). The handler logs it at Error level with the exception and code. `SalesInvoicePostingException`
   now derives from it, so `SAL.INVOICE.FI_POSTING_FAILED`, `SAL.INVOICE.FI_RECEIPT_MISMATCH` and FI fault codes such
   as `FI.SALES_POSTING.WRITE_FAILED` reach the client.
3. `POST /api/v1/sal/invoices` needs `SAL.INVOICE.ISSUE` (not VIEW): 201 + Location for a new invoice, 200 (no
   Location) for an identical replay. The response is read back from SQL after commit (`DescribeCommittedAsync`), so
   it equals a later GET byte for byte (the in-memory `PostedAt` has 100 ns ticks; SQL keeps microseconds).
4. `GET /api/v1/sal/invoices/{id}` needs `SAL.INVOICE.VIEW`; its data scope applies to the source order; Posted only.
   Not found, other company and out-of-scope are the same 404 `SAL.INVOICE.NOT_FOUND`.
5. The response carries customer code/name from the order snapshot, delivery numbers, order-line numbers, item
   code/name, all saved amounts, the three FI IDs and `version`; no FI table is read (module boundary).
6. Every `SAL.INVOICE.*` code raised by SD has zh-Hant / zh-Hans / en text in `errors.json` (architecture test
   SD-INVOICE-U11); FI and credit codes were already translated.

## Consequences

- Other modules can raise named 500s the same way instead of plain exceptions.
- 007a3b2 adds the list only; the invoice pages (WU-SD-007b) can already issue and show one invoice.
