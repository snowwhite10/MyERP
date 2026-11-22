# ADR-175: The seeded MANAGER role holds SAL.CREDIT.OVERRIDE (WU-SD-006c3b)

Date: 2026-11-23. Status: Accepted (WU-SD-006c3b).

## Context

ADR-171 lets the final approver of an over-limit sales order approve it with an override reason; SD writes the
`sal_credit_override` FinalApproval row only when the approver personally holds `SAL.CREDIT.OVERRIDE` with a data
scope covering the order. ADR-173 shows the reason field on Approval Inbox 8.70 only to users with that grant.

The seeded roles (MASTER_PROMPT section 12) give `manager` "approval authority in every module + all reports". The
seed rule matched only permissions ending in `.APPROVE`/`.REJECT`, the own-approval inbox, BI and `.VIEW`, so
`SAL.CREDIT.OVERRIDE` (an `OVERRIDE` action) went only to SALES. The demo approver therefore could never complete an
over-limit order: the inbox disabled approve with "you have no credit override permission". WU-SD-006c3b's real API
E2E (manager final-approves with a reason) found the gap.

## Decision

- `SystemSeedData` MANAGER rule adds `IsApproverOverride(p)`, which today matches exactly `SAL.CREDIT.OVERRIDE`.
  A credit override at final approval is an approver authority, so it belongs to "approval authority".
- `SAL.PRICING.OVERRIDE` is **not** added: the price override reason is entered by the salesperson on the order form,
  not by an approver.
- The seed adds missing grants to existing roles, so `--seed` on an existing database grants it; nothing is revoked.
- Covered by `SystemSeedTests.RoleRulesCoverFutureModulePermissions` (MANAGER gets credit override but not pricing
  override; SALES gets both) and the real API E2E `real-api-sales-credit-override.spec.ts`, which checks the grant in
  SQL before running.

## Consequences

- The manager demo account can final-approve over-limit orders with a reason; other approvers still need the grant
  assigned explicitly by an administrator.
- Future approver-time overrides are added to `IsApproverOverride` by explicit code, not by a wildcard on `OVERRIDE`.
