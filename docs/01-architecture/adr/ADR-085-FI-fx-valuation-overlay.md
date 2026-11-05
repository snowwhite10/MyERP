# ADR-085 — Period-end FX valuation and unrealised overlay release

Date: 2026-11-05. Status: Accepted (WU-FI-014f).

## Context

BR-FI-117／118 require a period-end valuation to post only `target closing base − current carrying base`, where the carrying base includes historical unallocated base plus prior Posted overlay still attached to the open portion, and a settlement to release the proportional prior overlay with the final release taking the rounding remainder. They do not fix how "attached" overlay is measured after partial settlements and revaluations, the rounding of the proportion, or how the committed state is passed and checked. ADR-084 already fixed the cumulative release of historical base.

## Decision

- Overlay state per exposure is the latest Posted valuation anchor: open original at that valuation (`AnchorOpenOriginal`), the signed base overlay then carried (`AnchorOverlay`, positive raises the carrying base for AR and AP alike) and the signed overlay already released by committed settlements after it (`ReleasedSinceAnchor`). No valuation yet is `(0, 0, 0)`; an anchor of 0 must carry no overlay and no release.
- Attached overlay while `open` original is open is `round(anchor overlay × open ÷ anchor open)` at base precision, away from zero (cumulative like ADR-084). It equals the anchor overlay at the anchor and zero at full settlement, never changes sign and is monotonic, so every release has the overlay's sign, the releases total exactly the overlay and split transactions match one batch.
- Committed state must be consistent: anchor within the original gross and at least the current open original (`Inconsistent` otherwise, since open only decreases), release equal to `anchor overlay − attached(current open)` (`Inconsistent`), amounts within `decimal(19,4)` and scale. A fully settled exposure therefore cannot keep a residual overlay.
- Valuation: target is `round(open original × closing rate)` once at base precision; carrying is open historical base (gross − committed discharge, ADR-084) plus attached overlay; delta = target − carrying. Signed unrealised difference is delta for AR and −delta for AP (positive gain); zero is `None` and posts nothing. The new anchor is `(open, target − open historical base, 0)`. A fully settled exposure reports no open exposure and anchor `(0, 0, 0)`. The closing rate snapshot must be `document → base` (`CurrencyMismatch`), positive within `decimal(19,8)`; a target or delta beyond `decimal(19,4)` is the new `Overflow` error.
- Settlement: realised difference is exactly `FxRealisedDifferenceRules` (against historical base), so the overlay is never counted twice; each allocation additionally returns the signed overlay released and the attached overlay after it, and the call returns the updated state.
- Errors reuse `FxAllocationFieldError` in collection order exposure, discharged, overlay, then closingRate or allocations; dependent checks run only when their inputs are valid; any error rejects the call. Policy/rate freshness, cutoff token, manifest, permanent keys, locks, GL lines and screens stay with 014a／014b. Pure rule `FxUnrealisedOverlayRules` in FI Domain.

## Consequences

014a saves the anchor with each Posted valuation and the released amount with each settlement, reads them under the BR-FI-121 lock order and posts a separate overlay reversal leg (omitted when zero) next to the realised leg. 014b posts the valuation delta per exposure and records the new anchor in the manifest.
