# ADR-076 — FI credit limit UI: exact decimal text, customer label source and stale-edit handling

Date: 2026-11-04. Status: Accepted (WU-FI-001c).

## Context

The Credit Limit View/Create/Edit interfaces (6.17–6.19) call `/api/v1/ar/credit-limits` (ADR-075). Three details were not defined:

1. `amount` is `decimal(19,4)` up to 999,999,999,999,999.9999. JSON numbers above about 9×10¹¹ with four decimals cannot be held exactly in a JavaScript double. If the page parsed and re-sent them, an edit could silently save a rounded limit.
2. FI responses carry only `customerId`. FI must not read MD tables, and `ICustomerLookup` has no batch label method, so the list cannot show code and name from FI alone.
3. After a 409 `COMMON.CONCURRENCY.CONFLICT` (or 422 `FI.CREDIT.LIMIT_NOT_ACTIVE`), the open edit dialog still holds the old version, so every further save fails in the same way.

## Decision

- The FI frontend service reads list and detail responses as text and keeps the raw `"amount"` token as `amountText`. Display adds thousands separators with `BigInt` string formatting, and edit fields are prefilled from `amountText`. Writes send `amount` as a decimal string. ASP.NET's web JSON defaults already read numbers from strings, and `CreditLimitHttpTests.DecimalTextAmountKeepsEveryDigitOnCreateAndUpdate` guards this: the maximum value round-trips exactly and `"12,5"` is a 400 with no write. Problem bodies in text mode are parsed back into JSON so `errorCode`, `traceId` and field errors still work.
- Customer code and name come from the MD customer picker (`/api/v1/md/customers`, `MD.CUSTOMER.VIEW`, which every seed role with `AR.CREDIT.VIEW` also has). Rows whose customer has not been picked in this session show a translated `Customer #id`. New backlog item WU-FI-001d adds a read-only MD batch label contract and puts `customerCode`/`customerName` on FI summaries/details. This was the only P2 left from the UX review; it was deferred because it needs a backend cross-module contract.
- A stale-version or not-active failure while editing closes the dialog, shows the named reason above the list and reloads saved data. Overlap (409 `FI.CREDIT.LIMIT_OVERLAP`), field validation and 5xx keep the dialog open with the user's input.

## Consequences

The list does not yet show customer names for rows that were not picked in this session. Keyword search by customer stays with the picker, as ADR-075 requires. When WU-FI-001d ships, `customerText` switches to server labels and the picker cache becomes a fallback.
