# ADR-084 — Realised exchange difference on AR/AP allocation

Date: 2026-11-05. Status: Accepted (WU-FI-014e).

## Context

BR-FI-114～116 require a partial allocation to release historical base "in proportion to discharged original amount, rounded once", the final allocation to take the remaining base, over-allocation to be rejected atomically and the realised difference to be signed by AR/AP side. They do not fix whether the proportion is per allocation or cumulative, the midpoint rounding mode, how the committed discharge is passed, or how the saved historical base is verified. WU-FI-014a needs these before it writes allocation difference records inside the posting transaction.

## Decision

- Exposure snapshot: side (Receivable/Payable), document and base currency (`^[A-Z]{3}$`, different; a base-currency source is `SameCurrency`, never a zero difference), saved precisions 0–4, original gross and historical base gross positive within SQL `decimal(19,4)` and their scale, transaction rate `1 document = rate base` positive within `decimal(19,8)`. Historical base must equal `round(original × rate)` at base precision, else `BaseMismatch`.
- Rounding: `MidpointRounding.AwayFromZero`, the FI convention. Release is **cumulative**: after each allocation the cumulative discharged base is `round(historical base × cumulative original ÷ original gross)`; the allocation's historical base is the increment. When the cumulative original reaches the gross, cumulative base is exactly the historical base. This is monotonic, so no allocation releases a negative amount (per-allocation rounding of a midpoint can over-release and leave a negative final), and splitting allocations across transactions gives the same results as one batch.
- Committed state: the caller reads, in the transaction, the committed discharged original and base. Both must be within the exposure and scale, and the base must equal the cumulative formula for the original (`Inconsistent` otherwise), so a corrupted or differently-rounded history is not silently continued.
- Allocations are applied in request order. Each needs a positive unique ID, the document currency (`CurrencyMismatch` otherwise; no cross-currency netting), positive original amount at document scale and the saved settlement base amount at base scale. The rule does not fetch or apply a current rate; splitting one receipt/payment base over several exposures stays with 014a. Every allocation whose running original passes the gross is `OverAllocated`. Any error rejects the whole batch.
- Result per allocation: original discharged, historical base discharged, settlement base, signed realised difference (AR settlement − historical; AP historical − settlement; positive gain), outcome None/Gain/Loss with absolute gain/loss amounts, remaining original/base and whether it settles the exposure; plus the discharged totals after the batch. Both operands are positive and within `decimal(19,4)`, so the difference cannot overflow.
- Errors are reported each field once in collection order exposure, discharged, allocations; dependent checks (base mismatch, inconsistency, currency match, over-allocation) run only when their inputs are valid.
- Unrealised overlay release (BR-FI-118), permanent keys, locks, account mapping and GL lines stay with 014a. Pure rule `FxRealisedDifferenceRules` in FI Domain.

## Consequences

014a saves both operands and the signed result from this rule, reads the committed discharge under the BR-FI-121 lock order and omits a zero exchange GL leg when the outcome is None.
