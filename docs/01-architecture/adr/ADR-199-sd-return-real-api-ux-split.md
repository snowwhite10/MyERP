# ADR-199: Split sales return real-API Playwright and UX review (WU-SD-008b3c2)

Date: 2026-11-24. Status: Accepted (WU-SD-008b3c2a).

## Context

WU-SD-008b3c2 combines a real-API Playwright run for the sales return (SQL Server, Redis, migration, seed, fresh
arrange data) and the myerp-ux-review report for interfaces 2.16 / 2.17 (screenshots per state, light / dark, three
languages, findings into backlog and a fix for the refused-destination message position). Together they exceed one unit.

## Decision

1. **WU-SD-008b3c2a** (this unit): `tests/e2e/specs/real-api-sales-return.spec.ts` SD-RETURN-REAL-001～003. No seed
   change: like `real-api-sales-invoice`, the spec arranges its own Approved order, Posted delivery and Posted invoice
   through the APIs (never SQL writes) and suspends active SAL.ORDER / SAL.DELIVERY / SAL.RETURN flows only while it
   runs. It asserts the screen against `sal_return*`, `inv_stock_movement`, `ar_sales_source`, `ar_credit_allocation`,
   `gl_journal` and `ar_posted_receivable`.
2. The approval path (Pending → reject → revise) is not repeated against real SYS here: it is covered by the
   SD-RETURN-POST / SYS decision handler integration tests and the mocked SD-RETURN-E2E-002.
3. **WU-SD-008b3c2b**: myerp-ux-review for 2.16 / 2.17 into `docs/02-modules/SD/qa-report.md`, including moving the
   refused-destination line message from the quantity split column to the destination column.
4. The parent WU-SD-008b3c2 is not counted.

## Consequences

The first real run showed a zero-step SUBMIT answers version 3 (Pending and Posted are both saved in the same request),
while the 008b3c1 mock answered version 2. The mock now mirrors the server (+2), so the mocked and real flows agree.
