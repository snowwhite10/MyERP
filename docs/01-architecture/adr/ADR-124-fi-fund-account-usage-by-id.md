# ADR-124 — FI reports fund accounts used by posted collection receipts; MD usage contract carries the ID (WU-FI-005a2e)

Date: 2026-11-14. Status: Accepted (WU-FI-005a2e).

## Context

ADR-121 stores the MD fund account **ID** in `ar_collection_receipt.bank_account_id`, but MD's `IFundAccountUsage.IsInUseAsync(string code)` asked by **code** only. Until FI answered, MD could soft-delete a fund account that posted receipts still point to. Two options were open:

1. FI resolves the code to an ID through a new MD lookup (`IFundAccountLookup.FindIdAsync(code)`), then checks its receipts.
2. MD passes both the ID and the code, because it already holds the entity when it asks.

No module implemented `IFundAccountUsage` yet (only an MD test double), so changing the signature breaks nothing.

## Decision

- **Option 2.** The MD contract becomes `IsInUseAsync(long fundAccountId, string fundAccountCode, CancellationToken)`. Modules that store the ID match the ID; modules that store the code (future SD/PUR documents) match the code. This avoids an extra round trip and a code→ID lookup that could race a rename, and does not widen `FundAccountInfo` with an ID.
- **FI implementation `FinanceFundAccountUsage`** (scoped, registered by `FinanceModule`): `true` when the current company has any `ar_collection_receipt` row with that `bank_account_id`. Rows exist only after a committed posting, so rolled-back or rejected postings never block deletion. The code is ignored; a non-positive ID is `false`; the company query filter scopes the lookup. The existing `(company_id, bank_account_id, bank_reference)` unique index covers the query — no migration.
- **Delete only.** MD still allows deactivation of a used fund account (history keeps the ID and journal snapshot; new postings with an inactive account fail closed with 422 `FUND_ACCOUNT_INVALID`, ADR-121).
- `IIncomeExpenseCategoryUsage` stays code-based (its future users store codes).

## Consequences

- MD returns 409 `MD.FUND_ACCOUNT.IN_USE` for a fund account referenced by a posted receipt (BR-FI-155, FI-FUND-USAGE-001～004).
- Future SD/PUR fund-account users implement the two-argument method and ignore the argument they do not store.
- Collection write-offs reference receipts, not fund accounts, so they add no further usage.
