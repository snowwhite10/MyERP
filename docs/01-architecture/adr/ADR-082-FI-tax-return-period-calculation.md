# ADR-082 — Tax return periods, tax point assignment and period net

Date: 2026-11-05. Status: Accepted (WU-FI-013f).

## Context

BR-FI-107 requires inclusive, non-overlapping return periods tied to one declared policy version, and forbids assuming an invoice date is the tax point. BR-FI-108 defines the period net as output minus eligible input plus approved adjustments, with a negative result labelled credit. Neither fixes how gaps, shared boundary days, a missing tax point, unapproved adjustments or amount limits are handled. WU-FI-013b needs these before it freezes a draft revision.

## Decision

- Period dates are inclusive. Two periods that share even one day overlap and the later one in the request is reported as `Overlap` on `startDate`. A reversed range is `OutOfRange` on `endDate`; a blank policy version is `PolicyRequired`; a missing or repeated period ID is `Required`/`Duplicate`. Gaps are allowed in the calendar (a company may register later), but a date in a gap has no period.
- Tax point assignment takes the source's explicit policy tax point. A null tax point is `TaxPointMissing` and is never replaced by an invoice or posting date. No covering period is `NoPeriod`; more than one covering row (including duplicated rows with the same ID) is `Ambiguous` and lists the covering period IDs; one covering period without policy is `PolicyMissing`. Only `Assigned` returns a period and its trimmed policy version.
- The period calculation takes the frozen committed source set (signed base tax and `ClaimableBaseTax` from ADR-081) and the explicit adjustments. Output total = signed base tax of `Output` and `OutputCredit`; input total = `ClaimableBaseTax` of `Input` (nonrecoverable input contributes zero). Only approved adjustments enter the net; unapproved ones are returned as pending IDs, an incompleteness reason, never silently added or dropped.
- Net > 0 is `Payable`, net < 0 is `Credit` with a positive `CreditAmount` (never a cash receipt), net = 0 is `Zero`. A return with no sources is a valid nil return.
- A source outside the period is `OutsidePeriod`, not silently excluded. Sign must match role (output/input ≥ 0, credit ≤ 0). Input claimable must be 0..signed base tax; output roles must have zero claimable. Amounts must fit SQL `decimal(19,4)` at base currency scale (0–4); an adjustment of zero is rejected. Component totals above the SQL maximum are rejected; the derived net is checked only when its components fit.
- Every invalid field is reported once: period, request, sources in order, adjustments in order, then totals. Pure rule `TaxReturnPeriodRules` in FI Domain.

## Consequences

013b validates the policy calendar on save, assigns each committed source once when it is written (013a), and stores the calculation totals with the frozen manifest. Durable cutoff, revisions, reconciliation and SQL remain 013a/b.
