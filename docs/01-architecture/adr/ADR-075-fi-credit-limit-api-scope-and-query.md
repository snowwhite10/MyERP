# ADR-075 — FI credit limit API: company-wide visibility, date-window filter and permission catalogue

Date: 2026-11-04. Status: Accepted (WU-FI-001b2).

## Context

FI api.md asks for a "current-company, SYS-scope paged list with customer/status/date filters" for `/api/v1/ar/credit-limits`. `ar_credit_limit` has no branch, department or owner column, so `DataScopeFilter.Apply` (which needs `IDataScopedEntity`) cannot apply. The api.md row also does not define what the date filter means for a period row, nor how the new `AR.*` permissions reach the seed roles.

## Decision

- Credit limits are company-wide configuration, like MD master data (ADR-074 already chose this for writes). Any granted data scope of `AR.CREDIT.VIEW` sees all limits of the current company; Self/Department/Branch do not narrow the list. Company isolation comes from the global company query filter, so another company's id is 404 `FI.CREDIT.LIMIT_NOT_FOUND` on read and on every write.
- `CreditLimitQueryService` and `CreditLimitCommandService` share `FinanceAccess.RequireAsync` (current company required → 403 `COMMON.COMPANY.ACCESS_DENIED`; permission in that company → 403 `COMMON.PERMISSION.DENIED`).
- List filters: `customerId` (> 0), `status` (`Active`/`Deactivated`), and `from`/`to`, which select periods sharing at least one inclusive date with the window. Either bound may be open; `from = to` finds the period covering that day. `from > to` is 400 on `to`. `keyword` is rejected with 400 because limits store no searchable text (customer search belongs to the MD picker in WU-FI-001c), so a keyword is never silently ignored.
- Sort: exactly one of `effectiveFrom` (default descending), `effectiveTo`, `customerId`, `amount`, always tie-broken by id in the same direction for stable pages.
- `CreditLimitStatus` serializes as a string (`JsonStringEnumConverter`), matching its database text.
- FI declares `AR.CREDIT.VIEW/CREATE/EDIT/DEACTIVATE` through `FinancePermissions` (module `FI`, prefix `ar`). The existing seed rules then give FINANCE all four, and MANAGER and VIEWER only VIEW. SALES gets none; SD reads credit through its own `SAL.CREDIT.VIEW` and the FI contract.
- The request body for PUT is flat (`version`, `customerId`, `effectiveFrom`, `effectiveTo`, `amount`); deactivate takes `{ version }`. Client-supplied company, base currency or status fields are ignored by binding.

## Consequences

A user with a Self-scoped VIEW grant sees every limit of the company. This is intended for configuration data and is documented here. If a later requirement needs owner-restricted limits, the table would need owner/organisation columns and a migration.
