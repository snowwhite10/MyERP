# ADR-088 — FX valuation run summary and per-source outcomes

Date: 2026-11-05. Status: Accepted (WU-FI-014i).

## Context

BR-FI-117 makes each exposure its own atomic posting result inside a valuation run, and BR-FI-122 requires zero, all-skipped and mixed outcomes to report exact counts and row reasons, never as an unqualified success. ADR-086 (eligibility), ADR-085 (valuation) and ADR-087 (journal composition) produce per-exposure results but do not say how a run is summarised, how a zero delta is counted, or what a Preview run reports. The depreciation batch rule (`FixedAssetDepreciationBatchRules`, WU-FI-012g) already fixes the plan/attempt pattern.

## Decision

- `FxValuationBatchRules.Summarize(eligibility, computations, attempts)` merges, by exposure ID ascending, the `FxValuationEligibilityResult`, one `FxValuationComputation` (valuation calculation + journal composition) per eligible exposure and, in Post mode, one `FxValuationPostAttempt` per source with a journal.
- Per source: excluded → `Excluded` with the 014g reasons; rejected valuation → `Failed` with `FI.FX.VALUATION_INVALID` and its field errors; rejected journal → `Failed` with `FI.FX.JOURNAL_INVALID`; valid journal without lines → `Zero` (manifest records the zero result, no GL journal); with lines → `Posted` when its transaction committed, `Failed` with the attempt's errorCode when rolled back. In Preview mode a source with lines is `Previewed`; Preview never yields `Posted` and accepts no attempts.
- Amounts (delta, signed unrealised difference, journal amount = debit total) are reported only for Posted/Previewed; `PostedUnrealisedNet` and `PreviewUnrealisedNet` sum the signed differences separately.
- Outcome: no eligible exposure (including an empty request) is `NoEligible`; otherwise all items Posted is `Success`, some Posted is `Partial`, all Failed is `AllFailed`, else `NoChange`. Zero and excluded sources therefore prevent `Success`, and Preview is never `Success`/`Partial`.
- Whole-summary rejection (no items, `ErrorExposureId` when known), checked in order eligibility, computations, attempts: null inputs `InvalidInput`; undefined mode, null/duplicate/non-positive exposures, an eligible item without rate or with reasons, an excluded item without reasons, counts or `PostingAllowed` not matching the items `InvalidEligibility`; computation null or missing valuation/journal `InvalidComputation`, for an excluded or unknown exposure `UnexpectedComputation`, repeated `DuplicateComputation`, a journal for a rejected valuation or a journal whose entry/amount disagrees with the delta `InconsistentComputation`, absent for an eligible exposure `MissingComputation`; attempt null or committed-with-code／rolled-back-without-code `InvalidAttempt`, in Preview or for a source without lines `UnexpectedAttempt`, repeated `DuplicateAttempt`, absent `MissingAttempt`.

## Consequences

014b runs eligibility, valuation and composition in the run transaction, performs one atomic posting per source with lines, then calls this rule for the manifest counts and the 200/422 response body. Manifest persistence, cutoff, locks, permissions and screens stay with 014b/014c.
