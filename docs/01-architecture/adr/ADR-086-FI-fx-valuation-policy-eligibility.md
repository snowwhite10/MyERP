# ADR-086 — FX valuation policy resolution and closing-rate eligibility

Date: 2026-11-05. Status: Accepted (WU-FI-014g).

## Context

BR-FI-119 requires one explicit approved company policy (rate source, valuation date, rate precision, eligible monetary account mapping, gain/loss accounts) and a per-source 422 reason for missing/stale rate, inactive currency, unsupported rate type, ambiguous policy and source mismatch; a draft preview cannot claim a Posted valuation. It does not fix how a policy version is chosen, what "stale" means, which rate types are valid for a valuation, or how reasons combine. ADR-085 consumes a `FxClosingRate` but does not say where it comes from.

## Decision

- A policy version applies when it belongs to the run company, is `Approved` and `EffectiveFrom ≤ valuation date ≤ EffectiveTo` (inclusive, open end when null). The valuation date is the GL period end date of the run; the policy fixes it by its effective range rather than a separate date rule. No applicable version is `Missing`; more than one (other versions or other policy IDs) is `Ambiguous` — the rule never picks the latest. Draft and Retired versions are ignored in Preview and Post alike.
- The resolved version's content must be valid (`Invalid` with `policy`／`policyAccounts` field errors): source and rate type `^[A-Z0-9_]{1,20}$`, rate decimals 0–8, at least one unique positive (side, control account) mapping, positive gain and loss account IDs. Only `CLOSING` and `SPOT` rate types are supported for valuation; any other valid code is `UnsupportedRateType`. When the policy is not resolved every candidate is excluded with exactly that policy reason.
- Per candidate, reasons are collected in a fixed order: `CompanyMismatch`, `BaseCurrencyMismatch` (exposure base ≠ company base) or `BaseCurrencySource` (document = base), `InactiveCurrency` (document or base not active in the saved MD snapshot; absent counts as inactive), `AccountNotEligible` (side + control account not in the mapping). The rate is looked up only when none apply.
- Rate: only snapshots with the policy source and type for `document → base`. Exactly one approved snapshot dated the valuation date is used; two are `AmbiguousRate` even with equal values; none approved but an unapproved one on that date is `RateNotApproved`; only earlier approved ones is `StaleRate` (never carried forward); otherwise `MissingRate`. Later-dated snapshots are ignored. The chosen rate must be positive within `decimal(19,8)` (`InvalidRate`) and have no more decimals than the policy precision (`RatePrecisionExceeded`, never rounded).
- An eligible exposure returns `FxClosingRate(document, base, rate)` for `FxUnrealisedOverlayRules.Value` and the rate ID for the manifest. `PostingAllowed` is true only in Post mode with a resolved policy and at least one eligible exposure; excluded exposures are reported, not posted (each exposure is its own atomic result, BR-FI-117). Preview never allows posting.
- Malformed input (null lists/items, duplicate policy ID+version / currency code / rate ID / exposure ID, reversed effective range, invalid request, currency codes, IDs or enums) rejects the whole call with `FxAllocationFieldError` in order request, policies, currencies, rates, candidates. Pure rule `FxValuationEligibilityRules` in FI Domain.

## Consequences

014b reads the policy versions, MD currency and rate snapshots and candidates through public Contracts in the run transaction, calls this rule before valuation, saves the policy version and rate IDs in the manifest and maps exclusions to the 422 per-source reasons. GL period status and control GL reconciliation stay with 014b.
