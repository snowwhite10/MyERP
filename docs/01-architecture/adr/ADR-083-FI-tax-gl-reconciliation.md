# ADR-083 — Tax detail to GL reconciliation matching

Date: 2026-11-05. Status: Accepted (WU-FI-013g).

## Context

BR-FI-109 requires each tax detail's signed base amount to be compared with the matching Posted GL tax leg at the return cutoff, detecting omitted, duplicate, wrong-account, wrong-period and rounding differences, naming both source and GL IDs, excluding no-tax sources and blocking approval while a difference is unexplained. It does not fix how a detail is linked to a GL line, the GL sign convention, how a manual journal on a tax account is treated or whether totals may net. WU-FI-013b needs these before it saves a reconciliation with a return revision.

## Decision

- Link: the posting transaction (013a) stamps the tax detail ID on its GL leg. The caller reads, at one cutoff, the frozen details of the period and every Posted line in the company that is either tagged with one of those details or posted in the period to a mapped tax account. Company, account, period and source are compared per detail, not by aggregating accounts.
- Sign: a detail's signed base tax is compared with `credit − debit` for `Output`/`OutputCredit` and `debit − credit` for `Input`. A credit posted on the wrong side is therefore an amount difference. A nonrecoverable input detail is reconciled at its configured cost account (ADR-081).
- Per detail, in request order: zero tax without account is **excluded** (listed, not missing); any leg tagged to it is `UnexpectedLeg`. Otherwise no leg is `Missing`, several legs are `Duplicate` (all GL line and journal IDs, ascending); one leg is checked for `WrongAccount`, `WrongPeriod` (posting date outside the inclusive return period) and `AmountDifference`, each reported, in that order. Then, in request order, every untagged line or line tagged with a detail outside the set is `Unmatched` (no role; amount `debit − credit`). This catches manual journals and legs of another period's details posted in this period.
- Every difference carries detail, source and role (null when unmatched), GL line and journal IDs, expected/actual account, posting date, expected, actual and `actual − expected`. Totals are informational: offsetting differences never net to a pass; any difference sets `BlocksApproval`. The rule never proposes a suspense or balancing line and edits neither side.
- Input is rejected before matching: missing/duplicate IDs, company other than the scope (`CrossCompany`), undefined role, tax without account or account without tax, a tax point outside the period, sign contrary to role, a GL line that is not single-sided positive, base scale 0–4 and SQL `decimal(19,4)` limits for items and totals. Each field once: scope, request, details, GL lines, totals.
- Explanation records, reviewer workflow, cutoff tokens and SQL reads stay with 013b; this rule treats every difference as unexplained. Pure rule `TaxGlReconciliationRules` in FI Domain.

## Consequences

013a must stamp the tax detail ID on each tax GL leg in the posting transaction. 013b stores the report with the revision and refuses approval while `BlocksApproval` is true or until an explicit explanation design (separate ADR) is accepted.
