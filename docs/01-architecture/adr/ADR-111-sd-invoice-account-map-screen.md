# ADR-111 — SD invoice account map screen: code inputs with saved-account labels; split c1/c2 (WU-FI-002a4c1)

Date: 2026-11-14. Status: Accepted (WU-FI-002a4c1).

## Context

WU-FI-002a4c asked for the maintenance screen of the ADR-110 API, three-language `FI.SALES_POSTING.*` / `FI.SALES_MAP.*` errors, a Development demo map row (which first needs an output-tax account in the MD demo chart) and both mocked and real-API E2E. That is too much for one round, and the screen has to choose how users pick accounts.

## Decision

- **Split.** 002a4c1 = screen (interface 6.23, route `/gl/sales-invoice-account-map`, menu Finance → Sales invoice accounts), translations, unit tests and mocked E2E with screenshots. 002a4c2 = MD demo output-tax account (e.g. 2141) + Development demo map rows for TW01/HK01 + real-API E2E.
- **Account code inputs, not an MD account picker.** The MD account list API needs `MD.ACCOUNT.VIEW`, which finance roles holding only `GL.SALES_MAP.*` may lack; a picker would hide accounts or fail for them. The server already validates every code against MD and returns all reasons at once (422 `accountErrors`), and GET returns the current MD name / active / postable state of each saved code. So the form takes codes (trimmed, required, ≤30 on screen), and each 422 reason is shown under its field (`MD.ACCOUNT.NOT_FOUND / INACTIVE / NOT_POSTABLE`, anything else as a generic "cannot be used"). A picker can be added later without API change.
- **Saved accounts are shown separately from the form** with MD name, and tags for missing / inactive / has-children, plus a warning that posting will fail. A view-only role sees only this part; an unconfigured map says invoices cannot be posted and, for view-only users, who to ask.
- **409 re-reads.** On `FI.SALES_MAP.VERSION_CONFLICT` the page reloads the map, replaces the input with the latest saved codes and keeps the conflict message (the user's input was not saved). `changed=false` is a yellow warning with the kept version; green only for a real change.
- **Sales posting error whitelist** (`sales-posting-errors.ts`, `salesPostingErrorKey`) for the future SD invoice posting screen; ArchitectureTests `SalesPostingErrorTranslationTests` keep it and the three `errors.json` files equal to `SalesPostingErrors` / `SalesInvoiceAccountMapErrors`.

## Consequences

- Finance users can set up SD invoice posting from the UI; real-API verification and demo data follow in 002a4c2.
- Users must know account codes; the intro and per-field hints say which side each account is posted to.
