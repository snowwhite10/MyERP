# ADR-078 — Fixed asset straight-line depreciation rounding

Date: 2026-11-05. Status: Accepted (WU-FI-012f).

## Context

BR-FI-095 requires a straight-line monthly base `(cost-residual)/life`, consistent rounding for the first `life-1` months, an exact remainder in the last month, and a cumulative amount that never exceeds cost less residual. It does not name the rounding mode. With a coarse currency (0 decimals) and a long life, rounding the base up can make `(life-1) × base` exceed the depreciable amount, so a naive last-month remainder would be negative. Rounding down instead avoids that, but can push up to `life-1` currency units into the final month.

## Decision

- The base is rounded with `MidpointRounding.AwayFromZero` to the company base-currency decimal places. This matches `CurrencyPrecision` (ADR-037). Only 0–4 decimals are accepted, which is the GL `DECIMAL(19,4)` scale. Cost and residual must already fit that scale.
- The scheduled cumulative amount after month `k` is `min(base × k, cost − residual)`. The last month of the life is always the exact remaining amount.
- A month whose amount is zero has its own status. `FullyDepreciated` means the cap has already been reached. `ZeroAmount` means the rounded base is below one currency unit and the last month will catch up. Neither status posts a journal. The batch reports each one as a skip reason.
- Full-month policy: month 1 is the calendar month of the saved in-service date, whatever the day.
- The rule is pure and lives in FI Domain (`FixedAssetDepreciationRules`). It reports schedule figures only, not committed balances. Net book value still comes from Posted sources (BR-FI-097).

## Consequences

Each month's amount is deterministic, and the total equals the depreciable amount exactly. No month is negative, and no month exceeds the base. Assets whose base rounds up stop depreciating before the end of their life. Users see those months skipped as `FullyDepreciated` rather than as a failure. The 012a/b writers must pass in the saved card values and the MD currency decimals. They must still check the card state, the Open period and the permanent source key.
