# ADR-104 — SD credit decision: fail-closed order and FI reason mapping

Date: 2026-11-06. Status: Accepted (WU-SD-006b0).

## Context

BR-SD-031～034 say the SD order submit / SYS final decision must check credit against the FI-owned `CreditSnapshot` (WU-FI-001a) plus SD's own uninvoiced Approved-order commitments, fail closed with named reasons, and allow a reasoned override only for a known numerical excess. `api.md` names `SAL.CREDIT.UNAVAILABLE`, `LIMIT_NOT_SET`, `INVALID_SNAPSHOT` and `LIMIT_EXCEEDED`, but did not say how the FI `CreditUnavailableReason` values, SD amount faults, an inactive MD customer or a missing override grant map to codes, nor in which order the checks run. The SQL memory gate returned DEFER (1.670／1.682／1.721 GiB), so the transaction (006b) could not be built; the pure decision could.

## Decision

- `MyERP.Modules.Sales` references `MyERP.Modules.Finance.Contracts` (Contracts-only dependency, allowed by `ModuleBoundaryTests`) and consumes `CreditSnapshot` directly; SD never reads FI tables.
- `SalesCreditRules.Evaluate(input, snapshot)` checks in this order and returns `Blocked` at the first failure: inactive/unapproved MD customer → `CUSTOMER_INACTIVE`; no snapshot → `UNAVAILABLE`; snapshot company, customer or evaluation date ≠ request → `INVALID_SNAPSHOT` (a backdated or other-scope snapshot is never used); FI reason `MissingLimit` → `LIMIT_NOT_SET`, `AmbiguousLimit` → `INVALID_SNAPSHOT`, `MissingBaseCurrency`／`CustomerUnavailable`／`ExposureUnavailable` → `UNAVAILABLE`; company base currency missing/malformed → `UNAVAILABLE`, different from FI currency → `INVALID_SNAPSHOT`; candidate or commitment amount outside nonnegative decimal(19,4), duplicate/non-positive commitment order ID, or total exposure above decimal(19,4) → `INVALID_AMOUNT` (new code).
- The candidate order ID is removed from the commitment list, so it counts exactly once on submit and on Pending final decision.
- `Headroom = limit − AR − other commitments − candidate`; `< 0` is `OverLimit` with `Excess = −Headroom`; exactly 0 passes; a zero limit is valid.
- `SalesCreditRules.Decide(assessment, reason, hasOverrideGrant)`: `Blocked` → 422 with its code, never overridable; `WithinLimit` → no override, a supplied reason is ignored and no grant is needed; `OverLimit` with no reason (null) → 422 `LIMIT_EXCEEDED` with `excess`／`currency` extensions; reason supplied without `SAL.CREDIT.OVERRIDE` → 403 `OVERRIDE_FORBIDDEN` (new code); then reason trimmed 1–200 characters or 400 `creditOverrideReason` (same limit as the pricing override in BR-SD-028). The grant check precedes the reason check, as in `SalesPricingRules.Decide`.
- Messages for all seven codes exist in three languages under `errors.SAL.CREDIT`, enforced by `SalesCreditErrorTranslationTests`.

## Consequences

- WU-SD-006b must call `Evaluate` with figures read after `AcquireCreditLockAsync` in the same transaction, using `IClock.Today` as evaluation date, then `Decide`, and save the assessment components with any override (actor/time from the server, bound to order version and SYS round).
- Binding to order version／SYS round, replay protection and the audit row are not part of these pure rules; they remain 006b real-SQL scope.
