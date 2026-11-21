# ADR-157: Price rules are one company-wide table guarded by a per-key SQL application lock

Date: 2026-11-21. Status: Accepted (WU-SD-005a8).

## Context

WU-SD-005d fixed the pricing contract (REQ_F4112.4.1, BR-SD-026～030) but left three choices open for the first migration:

1. How to stop two writers from committing overlapping active intervals for the same item, currency and customer. A unique index cannot express "intervals overlap", and a nullable customer ID (the company default) makes SQL `NULL` comparisons awkward.
2. Which data scope applies. Price rules have no owner, branch or department.
3. How much of WU-SD-005a fits in one round. The full unit (CRUD, edit history, deactivate, quote endpoint, document provenance columns) is well over 90 minutes.

## Decision

- `sal_price_rule` (REQ_DS43.204) stores one row per rule: company, scalar MD item ID, nullable customer ID (null = default), currency, inclusive dates, `decimal(19,6)` price, `decimal(7,4)` ceiling, active/deleted, version, audit stamps. No FK to MD.
- Every rule write opens a ReadCommitted transaction, takes `sp_getapplock` (Exclusive, owner Transaction, 15 s timeout) on `sal_price_rule:{company}:{item}:{currency}:{customer|default}`, re-reads active non-deleted candidates whose dates intersect, and runs `SalesPricingRules.FindOverlaps`. A conflict is 409 `SAL.PRICING.RULE_OVERLAP` with `conflictingRuleIds`. The lock name gives the default key an explicit string, so `NULL` semantics do not matter. Different keys never wait for each other.
- Rules are company-wide. The service checks the exact permission (`SAL.PRICING.VIEW` / `CREATE`) through `SalesActionAccess.RequireScopeAsync`. Any granted data scope is enough. Company isolation comes from the global company filter, so other-company IDs are 404.
- A created rule is active at once (no SYS approval), as WU-SD-005d proposed.
- WU-SD-005a is split up. WU-SD-005a8 delivers the table, migration, permission catalog (`CREATE/EDIT/DEACTIVATE/OVERRIDE`), `GET` list/detail and `POST` create. The other parts stay in WU-SD-005a and are listed in the backlog: edit with version and history, deactivate, quote endpoint, and document provenance.

## Consequences

- SD-PRICE-HTTP-005 runs four concurrent overlapping writers three times on real SQL. Each time exactly one commits.
- Edit and deactivate must take the same lock. Edit must lock both the old and the new key when the key changes, in a fixed order to avoid deadlock.
- The data audit trail interceptor writes rule changes on its own, the same way it does for other `MasterEntity` tables.
