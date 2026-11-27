# ADR-212 — Other payment permissions, demo number rule and MD category usage by ID (WU-FI-008b1)

Date: 2026-11-27. Status: Accepted (WU-FI-008b1).

## Context

WU-FI-008a3/008a4 delivered the other payment Draft and posting services but left three prerequisites for the HTTP
and approval slices: the `AP.OTHER_PAYMENT.*` permissions were constants only (not in the catalogue, so no role held
them), no SYS demo number rule existed for `AP.OTHER_PAYMENT` (Development seeding would 422 on create), and MD could
delete an income/expense category or fund account still referenced by an other payment. FI stores the MD category
**ID** (`ap_other_payment_doc.expense_category_id`, source snapshot, account map) while MD's
`IIncomeExpenseCategoryUsage.IsInUseAsync(string code)` asked by code only — the same mismatch ADR-124 solved for fund
accounts. No module implemented the category contract yet (only an MD test double).

The original 008b backlog item (permissions + number rule + usage + submit/approve posting + HTTP + map API) exceeds one
90-minute round, so it is split: **008b1** (this ADR), **008b2** submit/withdraw and SYS final approval posting,
**008b3** HTTP controllers (document + category map maintenance) with Swagger.

## Decision

- **Permissions.** `FinancePermissions` registers `AP.OTHER_PAYMENT.VIEW/CREATE/EDIT/DELETE/SUBMIT` under module FI
  (`ap` is an FI prefix). Seed roles follow the existing rules: FINANCE gets all five, MANAGER and VIEWER get VIEW only.
  No `AP.OTHER_PAYMENT.APPROVE` is added: like collection receipts (ADR-129/130), approval is decided by SYS
  `SYS.APPROVAL.PROCESS` and the FI result handler. Category map permissions are registered with the map API (008b3).
- **Number rule.** `FinanceNumberRuleSeeds` declares `AP.OTHER_PAYMENT` / 其他付款 / prefix `OP` + year-month + 4 digits,
  monthly reset (e.g. `OP2026110001`), through the SYS seed contract (ADR-135); idempotent and never overwrites a
  user-modified rule.
- **Category usage by ID.** The MD contract becomes `IsInUseAsync(long categoryId, string categoryCode, CancellationToken)`
  (MD already holds the entity). FI implements `FinanceIncomeExpenseCategoryUsage` (scoped): `true` when the current
  company has a non-deleted other payment document (any state), a posted `ap_other_payment_source` snapshot, or a
  `gl_other_payment_account_map` row for that ID. Map rows cannot be deleted (SQL trigger), so a mapped category stays
  in use — deleting it would orphan the posting configuration; deactivation remains allowed.
- **Fund account usage.** `FinanceFundAccountUsage` additionally reports non-deleted other payment documents and posted
  other payment sources by bank account ID.
- Other collections (WU-FI-007) also reference income categories, but their schema verification is deferred
  (WU-FI-007a1); their usage clause is added by 007a2 after that verification, not here.

## Consequences

- MD returns 409 `MD.INCOME_EXPENSE_CATEGORY.IN_USE` / `MD.FUND_ACCOUNT.IN_USE` for categories/accounts held by other
  payments (FI-OP-USAGE-001～005). Soft-deleting a Draft releases both.
- Future code-based category users implement the two-argument method and ignore the ID.
- The usage queries run three `EXISTS` probes on FI tables only; they are on delete paths, not hot paths.
