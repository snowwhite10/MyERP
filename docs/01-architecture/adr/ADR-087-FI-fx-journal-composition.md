# ADR-087 — FX valuation and settlement journal line composition

Date: 2026-11-05. Status: Accepted (WU-FI-014h).

## Context

BR-FI-116 requires the settlement journal to balance control, clearing and exchange lines with policy-mapped gain/loss accounts and no client-selected GL plug, omitting zero lines. BR-FI-118 requires the prior overlay to be released by a distinct, balanced reversal leg in the settlement journal. ADR-084～086 fix the amounts (`FxAllocationResult`, `FxValuationResult`, `FxOverlayReleaseResult`) and the policy gain/loss accounts, but not the debit/credit direction for AR versus AP, how an overlay reversal is booked, or which account collisions are forbidden.

## Decision

- AR control is an asset: an increase in carrying base is a debit. AP control is a liability: an increase is a credit. Signed gain is the carrying change for AR and its negation for AP (ADR-085).
- Valuation: a non-zero `ValuationDelta` gives exactly two lines, `Control` for |delta| on the side of the carrying change and `UnrealisedGain`／`UnrealisedLoss` on the opposite side with the policy unrealised account chosen by the sign of `UnrealisedDifference`. Zero delta gives no lines (`HasEntry = false`), not an empty journal.
- Settlement (one allocation, one journal): `Clearing` for the settlement base on the carrying-increase side (AR debit of the unapplied receipt liability, AP credit of the unapplied payment asset); `Control` for the historical base discharged on the opposite side; `RealisedGain` (credit) or `RealisedLoss` (debit) for |realised difference|; then, for a non-zero released overlay, a separate `OverlayReversalControl` line reversing the carrying change and an `OverlayReversalGain` (debit of the unrealised gain account) or `OverlayReversalLoss` (credit of the unrealised loss account) reversing the original unrealised booking. The reversal is never netted into the control discharge line. Zero lines are omitted. Line order is fixed as listed.
- Because every difference line is derived from the same signed amounts as its control/clearing line, the journal balances by construction; `GlJournalBalanceRules` is still run as a final preflight, and a total above `decimal(19,4)` is `Overflow` on `lines`. Any other balance failure is a defect (exception), not a user error.
- Inputs are checked rather than trusted: undefined side; non-positive account IDs (`Required`); control and clearing must be distinct and differ from every gain/loss account (`Duplicate`), so no line can net into the control as a plug — gain and loss may share an account and realised/unrealised accounts may coincide; amounts must fit `decimal(19,4)` with at most four decimals; settlement base positive, historical base non-negative; the supplied signed difference must equal the side formula (`Inconsistent`), which also catches a result composed with the wrong side. Errors are reported in order request, accounts, valuation／release, lines, and reject the call.
- Pure rule `FxJournalCompositionRules` in FI Domain.

## Consequences

014a／014b map the policy accounts and the exposure's saved control account, call this rule inside the posting transaction and pass the lines to `IGlPostingService` with the permanent source key; MD postability, period status, locks and audit remain there. A valuation with `HasEntry = false` records a zero result in the manifest without a GL journal.
