# ADR-105 — Demo seed company context and GL demo journals

Date: 2026-11-13. Status: Accepted (WU-FI-004c4a).

## Context

WU-FI-004c4 needs real-API E2E evidence for the GL period (6.20/6.21) and posted-journal (6.22) screens, which requires GL periods and at least one Posted journal in the Development database. Only the SYS seeder existed: no MD currencies or chart of accounts, no FI periods. No production source adapter (SD invoice, PUR invoice, …) posts to GL yet, and GL has no public write route for journals (BR-FI-027/028: `IGlPostingService` is the only write path, and `gl_` rows are immutable by trigger). Seeding also had no company context: `HttpCurrentCompany` reads only the `X-Company-Id` header, so MD and FI services, which require a current company, could not run from `--seed`. Writing `md_`/`gl_` rows directly would skip the code, period, overlap, balance, snapshot and source-key rules. That is not allowed.

## Decision

- **Seed company context.** `MyERP.Shared.Infrastructure.Seeding.SeedCompanyContext.Enter(companyId)` sets an `AsyncLocal` company. `HttpCurrentCompany` uses it **only when there is no HTTP context**. During a request it still reads only the header, so request code cannot use it to switch company. Each seeder enters one company at a time and creates a fresh DI scope per company.
- **Seed company directory.** `ISeedCompanyDirectory` (Shared.Infrastructure) is implemented by SYS (`SeedCompanyDirectory`). It returns the existing, undeleted seed companies (TW01, HK01) with their seed base currency. Other modules therefore do not read `sys_` tables.
- **MD seeder** (`MasterDataDemoSeeder`, order 200). For each seed company it creates TWD (0 dp), HKD, USD, CNY (2 dp) and JPY (0 dp), with the company's seed base currency as base unless a base already exists. It also creates a 17-account chart (top levels 1–6 with leaves 1101/1102/1131/1141/2101/2171/3101/4101/5101/6101/6102). All writes go through `CurrencyService` / `AccountService`. Matching is by code and includes soft-deleted rows. A soft-deleted code is not recreated, and children of a deleted parent are skipped.
- **FI seeder** (`GlDemoSeeder`, order 700). For each seed company it creates 12 monthly periods for the clock year through `GlPeriodWriteService`, skipping any month that overlaps an existing period. It then posts two journals per month up to the current month: sales Dr 1131 / Cr 4101, and rent Dr 6102 / Cr 1102. The business date is the 15th, or today if earlier in the current month. HK01 also gets one precision journal of 123,456,789,012,345.67 HKD in the current month (Dr 1102 / Cr 3101), which tests exact `decimal(19,4)` display end to end. Each journal is built from MD `IChartOfAccounts` snapshots with `GlAccountSnapshotRules` / `GlJournalLineDraftRules` and posted through `IGlPostingService`. The source is `FI`/`DemoSeed`, the source ID is `yyyyMM×10+kind` (1 sales, 2 rent, 9 precision), version 1, and the fingerprint is `seed-v1:<amount>:<debit>:<credit>`. Periods that this run created and that end before the previous month are closed after posting, through the same write service. Existing user periods are never changed. A journal whose source key already exists is skipped. Any rejected or faulted posting fails the seed with its code.
- **Demo mapping fails closed outside Development.** `GlDemoSeedMapping` (`FI`/`DemoSeed`) returns mapping version 1 only when `IHostEnvironment.IsDevelopment()`, and 0 otherwise. In other environments the posting decision therefore rejects any `DemoSeed` source. `DataSeedRunner` already refuses to run outside Development.

## Consequences

- Development `--seed` now gives TW01 and HK01 24 periods (18 closed), 45 Posted journals, 10 currencies and 34 accounts. A second run adds nothing (verified on `myerp_test` by `DemoSeedTests` and on `myerp` by rerunning `--seed`).
- WU-FI-004c4b can assert screen values against SQL (`sqlcmd` is available on the VM) for known journals: TW01 `2026111` = 235,795 TWD, HK01 `2026119` = 123,456,789,012,345.67 HKD, and closed/open period states.
- When real source adapters (SD/PUR invoices) arrive, they register their own mapping sources. `DemoSeed` stays as demo data only.
- `SystemSeedTests` and `DemoSeedTests` share the TW01/HK01 seed and run in the serial `Seed data` collection.
