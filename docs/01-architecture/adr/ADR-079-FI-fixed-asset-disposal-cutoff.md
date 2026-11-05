# ADR-079 — Fixed asset disposal depreciation cutoff and gain/loss

Date: 2026-11-05. Status: Accepted (WU-FI-012h).

## Context

BR-FI-099 says that missing or closed earlier periods block disposal. REQ_F4116.16.2 says that approval "posts any scheduled depreciation through disposal cutoff once". Neither says whether the disposal month itself must already be posted. They also do not say what happens when a committed depreciation source no longer matches the schedule.

## Decision

- Full-month policy (ADR-078) also applies to disposal. The disposal month is depreciated in full.
- Every scheduled month (`Allowed`) **before** the disposal month must already have a committed source. The earliest gap blocks disposal with `MissingDepreciation`. Months that are `ZeroAmount`, `FullyDepreciated` or after the end of the life are not required.
- If the disposal month is scheduled and not yet committed, the approval transaction posts it once as a catch-up (`CatchUpAmount`). This happens under the same asset lock and before the disposal journal. Only the disposal period has to be Open.
- Committed sources must reconcile with the schedule. A period may appear once only. It must be on or before the disposal month. Its status must be `Allowed`, and its amount must equal the scheduled amount. Otherwise the disposal is blocked with `DuplicateCommittedPeriod`, `DepreciationAfterDisposal` or `CommittedMismatch`. This stops a disposal from hiding a reconciliation difference (BR-FI-097).
- Net book value is cost minus committed depreciation minus the catch-up. Gain is proceeds minus net book value when positive; otherwise it is a loss. Proceeds are 0 or more, at the currency scale, and within the SQL amount limit. The journal is Dr accumulated depreciation + proceeds + loss = Cr cost + gain.
- The rule is pure and lives in FI Domain (`FixedAssetDisposalRules`). Asset locks, source keys, cash receipts already posted, and GL posting are handled by 012c.

## Consequences

A disposal can never understate depreciation for earlier months. The user must post those months first, or reopen their periods. A disposal in the current month needs no separate depreciation run. If a committed source has drifted from the schedule, disposal stops until the difference is reconciled. Proceeds already recognised by a cash receipt are checked by the 012c writer through its source relation, not by this rule.
