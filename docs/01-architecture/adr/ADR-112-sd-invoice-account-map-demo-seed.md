# ADR-112 — SD invoice account map demo seed and output-tax demo account (WU-FI-002a4c2)

Date: 2026-11-14. Status: Accepted (WU-FI-002a4c2).

## Context

ADR-110/111 left two things for 002a4c2: the Development demo chart had no output-tax account, so no demo company could have a complete SD invoice account map, and the map screen (6.23) had only mocked E2E. Without a seeded map, demo SD invoices of TW01/HK01 cannot be posted (422 `FI.SALES_POSTING.MAPPING_MISSING`), and the real-API E2E has nothing to read.

## Decision

- **MD demo chart gains `2141 銷項稅額`** under liability root `2` (inherits Liability, credit balance, leaf so postable). It is added through `AccountService` like every other demo account, so an existing database gets it on the next `--seed`; soft-deleted codes and deleted parents are still respected.
- **FI seeder (`GlDemoSeeder`, order 700, Development only through the seed command) creates one map row per seed company: receivable 1131, revenue 4101, output tax 2141.** The row is created only when the company has none: any existing row — whatever a user changed it to, at any version — is left untouched, so re-seeding never overwrites a user's accounts or bumps the map version (which would reject in-flight postings, ADR-109). Before inserting, each code is checked with the same MD rule as the maintenance API (`SalesInvoiceAccountMapRules.AccountReason` over `IChartOfAccounts`); an unusable code aborts the seed instead of leaving a map that only fails at posting time.
- **The seeder writes the FI-owned table directly in the seed company context**, not through `SalesInvoiceAccountMapService`: that service requires an HTTP user with `GL.SALES_MAP.EDIT`, and the seed has no user. The write still goes through `MyErpDbContext` (audit stamps, version 1). `GlDemoSeedResult` gains `SalesInvoiceMaps`.
- **Real-API E2E `real-api-finance-sales-map.spec.ts`** (FI-SALES-MAP-REAL-001–004) compares the screen with the SQL row (codes, MD names, version) for TW01 and HK01, saves a real change (version + 1), a no-change save, a 422 with two field reasons, and a view-only read. Changes are restored through the API in `finally`; the version keeps counting (a version number is never reused). The demo chart has no second postable revenue account, so the change test switches the output-tax account between 2141 and 2171 (both postable liabilities). `sqlcmd` helpers moved to `tests/e2e/support/real-api-sql.ts` and are shared with `real-api-finance-gl.spec.ts`.

## Consequences

- After `--seed`, demo SD invoices of TW01/HK01 can be posted to GL (once SD invoices exist, SD-007a).
- The demo map version on a long-lived `myerp` grows with every real-API E2E run; nothing depends on it being 1 except the fresh-database seed test.
