# ADR-109 — SD invoice account map: one FI row per company, row version is the mapping version (WU-FI-002a4a)

Date: 2026-11-14. Status: Accepted (WU-FI-002a4a).

## Context

ADR-108 left two ports without a production implementation: `ISalesInvoiceAccountMapSource` (AR, revenue and output-tax account codes plus a mapping version) and the GL `IGlPostingMappingVersionSource` for `SD/Invoice`. The invoice posting service reads the account codes first, builds the draft, and the GL posting path reads the mapping version again after its locks. The two versions must agree, otherwise GL rejects 409 `MAPPING_CHANGED`. Until now only test fakes existed, so every non-test environment had no SD/Invoice mapping at all (GL would treat that as a 500 configuration fault).

WU-FI-002a4 (backlog) was too large for one round (table, two sources, maintenance API with permissions, three-language frontend), so it is split:

- **002a4a** — table, migration, real source for both ports (this ADR).
- **002a4b** — authorised maintenance HTTP API (read, create/update with optimistic version, Swagger, audit), MD account validation on save.
- **002a4c** — frontend maintenance screen and `FI.SALES_POSTING.*` / map errors in three languages.

## Decision

- New FI table `gl_sales_invoice_account_map` (prefix `gl_`: journal mapping is GL-owned, BR-FI-009). One row per company (`ux_gl_sales_invoice_account_map_company_id`), three `varchar(30)` MD account codes (same type as MD account code), 6.4 audit columns. No cross-module FK to MD accounts: codes are validated and snapshotted through `IChartOfAccounts` at posting time (and at save time in 002a4b).
- **The row's `version` is the mapping version.** The SaveChanges interceptor increments it on every update, so any account change produces a new mapping version without a separate counter that could drift.
- **Never delete, never rewind.** Trigger `tr_gl_sales_invoice_account_map_guard` (SQL 51025) rejects DELETE, any company change and any update whose version does not increase. A deleted and re-created row would restart at version 1 and let an old version number name a different set of accounts. Checks reject blank codes and non-positive company/version.
- **One implementation for both ports.** `SalesInvoiceAccountMapStore` implements `ISalesInvoiceAccountMapSource` and `IGlPostingMappingVersionSource` (`SD`/`Invoice`), reading the same row through the scoped `MyErpDbContext` (so it joins the caller's or the attempt's open transaction), `AsNoTracking`, no caching. A company other than the DbContext's current company, or no row, yields null / 0 (fail closed: invoice posting 422 `MAPPING_MISSING`, standalone GL 409 `MAPPING_CHANGED`).
- A concurrent mapping change between the account read and the GL read is detected by the existing GL version check (409 `MAPPING_CHANGED`, caller transaction kept); no extra lock is taken on the map row, so maintenance never blocks posting.
- Test fixtures that inject a fake SD/Invoice mapping must remove the production registration first (two sources for one type is a 500 configuration fault by design, ADR-103).

## Consequences

- Production environments can post SD invoices once a company has a map row; no seed row is created in this slice (002a4b may add a Development demo row together with the API).
- The Architecture registration test now pins exactly two mapping sources: `FI/DemoSeed` and `SD/Invoice`.
