# ADR-113 — FI sales return credit posting: split of WU-FI-002a3, SD return port and cumulative-limit rules

Date: 2026-11-14. Status: Accepted (WU-FI-002a3a).

## Context

WU-FI-002a3 has to post a billed SD return as one FI credit source. It must check each invoice line's cumulative credit limit, the customer, currency and company, and the original receivable's open balance. It writes the credit journal, the allocations and the AR reductions, and it needs a real SQL two-connection race for the last remaining portion. FI has saved the invoice header totals in `ar_sales_source` and the receivable in `ar_posted_receivable`. FI does not save invoice lines: SD owns them, and FI must never read `sal_` tables. That is too much for one 90-minute round.

## Decision

- **Split.**
  - 002a3a covers the public contract plus the pure rules and their unit tests.
  - 002a3b covers the service: credit lock, replay, SD read, original/prior reads, joined GL and writes. It also needs the GL `SD/Return` mapping version and single-connection real SQL tests.
  - 002a3c covers the two-connection race on the last remaining portion and full rollback after a caller failure.
  - The parent WU-FI-002a3 is done only when a, b and c are done.
- **Contract (FI Contracts).**
  - `IFinanceSalesReturnSource.ReadReturnAsync(company, return, DbTransaction)` is an FI-owned port. SD implements it in WU-SD-008a.
  - The read returns a `FinanceSalesReturnSnapshot`. Its header holds the billed totals only. It also carries one `FinanceSalesReturnLine` per original invoice line: invoice ID, invoice line ID, quantity, the six amounts, and a `FinanceSalesInvoiceLineCap`, which is SD's saved quantity/net/tax/base net/base tax for that invoice line.
  - The unbilled part of a return is not sent to FI. SD releases it (BR-FI-012).
  - `ISalesCreditPostingService.PostInCurrentTransactionAsync(SalesCreditPostingCommand)` returns `SalesCreditPostingResult` with the same Posted / Unchanged / Rejected / Fault semantics as invoices (ADR-108). On success the receipt lists each allocation (invoice, line, receivable, quantity, gross, base gross).
- **Two-level caps (BR-FI-011).**
  - The per-line cap is SD's saved invoice line, since FI has no line table.
  - The per-invoice cap is FI's own saved `ar_sales_source` net/tax/base net/base tax. A forged or inflated SD line cap therefore still cannot credit more than FI posted for that invoice.
  - Prior credits are FI's committed `ar_credit_allocation` rows. The service sums them under the credit key lock, by `(company, invoice_line_id)` and by original receivable. The table has no soft delete (ADR-106), so all historical credits count.
- **Open AR (BR-FI-012).** For each receivable, this return's base gross must be ≤ the receivable's current `open_base_amount`. That amount already reflects receipts and prior credits. An exact final remainder passes; anything over it is a 422 `CREDIT_EXCEEDS_OPEN`. AR never goes negative, and a credit after collection waits for a separate refund design.
- **Check order.**
  - Snapshot checks, in order:
    1. 404: identity.
    2. 422: state. Only Issuing and Posted are postable.
    3. 409: the caller's claim.
    4. 422 `INVALID_AMOUNTS`: header reconciliation (same rules as invoices) and a non-empty line list.
    5. Per line: 422 `INVALID_AMOUNTS` with the line number, or 422 `DUPLICATE_LINE`.
    6. 422 `INVALID_AMOUNTS`: the header must equal the sum of the lines for every component.
  - Allocation checks run line by line in return order. The first line that crosses a limit is reported:
    1. `ORIGINAL_NOT_POSTED`
    2. `ORIGINAL_MISMATCH` (customer, currency or base currency)
    3. `CREDIT_EXCEEDS_LINE`
    4. `CREDIT_EXCEEDS_INVOICE`
    5. `CREDIT_EXCEEDS_OPEN`
  - All of these are 422 with the 1-based line number.
- **Journal.**
  - Debit the mapped revenue account (it acts as the returns contra) for base net.
  - Debit output tax for base tax.
  - Credit AR for base gross.
  - Zero legs are omitted.
  - There is no separate sales-returns account in the FI map yet. Adding one is optional future work: it would need a new map column and version. Until then, 002a3b reuses `SalesInvoiceAccountMap` and its version. The GL `SD/Return` mapping version source must report that same version.

## Consequences

- SD-008a must supply the line caps and call the service inside its return-posting transaction.
- 002a3b must read the originals and the prior sums under the credit key and in ascending receivable ID order (BR-FI-013), then reduce `open_base_amount`. That table is the only mutable one.
- The rules evaluate committed state only. The two-connection proof belongs to 002a3c.
