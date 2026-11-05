# ADR-081 — Tax detail line components and treatment

Date: 2026-11-05. Status: Accepted (WU-FI-013e).

## Context

BR-FI-104～106 say FI stores tax detail from saved SD/PUR line components, signs a billed return opposite, splits recoverable and nonrecoverable input tax, omits a zero GL line and caps a credit at what remains. They do not fix the sign convention, how a zero tax in one currency but not the other is handled, which account each role posts to, or the error order. WU-FI-013a needs these before it creates a table and the posting transaction.

## Decision

- Roles: `Output` (SD invoice), `OutputCredit` (billed SD return), `Input` (matched PUR invoice). The source contract supplies non-negative magnitudes; FI stores signed amounts: positive for `Output` and `Input`, negative for `OutputCredit`.
- Original and base sides are checked separately: currency decimals 0–4, each amount 0 to the SQL `decimal(19,4)` maximum at its currency scale, gross above zero, and `net + tax = gross` exactly. FI never recalculates tax from a current MD rate (BR-FI-105).
- A zero original tax with a non-zero base tax, or the reverse, is `ZeroMismatch` on `baseTax`: it would be an implicit rounding plug. A line whose tax is zero on both sides has no GL leg and needs no account.
- Input needs a saved treatment (`Recoverable` or `NonRecoverable`); output roles must not carry one. Recoverable input posts to the mapped input-tax account and is fully claimable. Nonrecoverable input posts to the configured cost account and has zero claimable tax. A cost account on any other line is `NotApplicable`. Output and credit post to the mapped output-tax account.
- `OutputCredit` needs the original line's remaining net, tax, base net and base tax after prior committed credits (read by the caller in the posting transaction). Each credited component above its remaining value is `ExceedsRemaining` on that field. Other roles must not carry a remaining value.
- Every invalid field is reported once, in contract field order. Balance, zero agreement and credit limits compare only components that are valid themselves.
- The rule is pure (`TaxDetailLineRules` in FI Domain). MD tax-code eligibility, account postability, permanent identity, fingerprints and SQL locking stay with 013a.

## Consequences

013a maps each field error to a 422 source reason and writes only validated lines. Returns (013b) sum signed base tax for output and `ClaimableBaseTax` for input, so claimability is never inferred from a current rate.
