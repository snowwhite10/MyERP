# ADR-074 — FI credit limit write rules: locked overlap check, immutable customer and currency, history-only deactivation

Date: 2026-11-04. Status: Accepted (WU-FI-001b1).

## Context

BR-FI-004 requires the limit write path to lock the credit key, check overlap and version, then audit. FI-CREDIT-008 says deactivated history "cannot silently resurrect or reuse a conflicting interval" but does not say whether a deactivated row blocks a new interval on the same dates, nor what happens when the company base currency changes or a client tries to move a limit to another customer. The table has no soft-delete column (ADR-073), so "soft-deleted" history does not exist for limits.

## Decision

- `CreditLimitCommandService` (FI Application) runs each create/update/deactivate in one READ COMMITTED transaction that first calls `ICustomerCreditService.AcquireCreditLockAsync`, then re-reads the row and checks version (409 `COMMON.CONCURRENCY.CONFLICT`) and overlap (409 `FI.CREDIT.LIMIT_OVERLAP`). Any failure rolls back the key insert, limit and audit rows together.
- Overlap uses inclusive business dates among **Active** rows of the same company/customer, excluding the row being edited: `from <= otherTo AND to >= otherFrom`. Touching endpoints overlap; adjacent days do not.
- Deactivated rows are history only. They are never edited, reactivated or deactivated again (422 `FI.CREDIT.LIMIT_NOT_ACTIVE`) and do not block a new Active interval, which always gets a new identity. There is no reactivate command, so an older row can never become effective again.
- The customer of a limit is immutable (400 on `customerId`). The base currency is server-derived from MD `ICurrencyLookup.GetBaseCurrencyAsync`; missing base currency is 422 `FI.CREDIT.BASE_CURRENCY_MISSING`. If the company base currency differs from a saved limit's currency, edit is 422 `FI.CREDIT.CURRENCY_MISMATCH` and the user must deactivate it and create a new limit.
- Create and edit require an approved, active MD customer (`ICustomerLookup.RequireApprovedAsync`); deactivation does not, so limits of disabled customers can still be retired.
- Each action checks its own permission (`AR.CREDIT.CREATE/EDIT/DEACTIVATE`) with company scope. Credit limits are company master data, so branch/department/self data scope does not apply. The SYS permission catalogue, HTTP routes, Swagger and list/detail queries follow in WU-FI-001b2.

## Consequences

Concurrent overlapping writes and SD credit decisions serialize on one key, and a committed overlap by the lock holder is always seen (proved with two SQL connections). Changing the company base currency needs an explicit retire-and-create step, which keeps saved amounts from being relabelled.
