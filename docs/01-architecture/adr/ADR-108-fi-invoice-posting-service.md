# ADR-108 — FI invoice receivable posting service (WU-FI-002a2b)

Date: 2026-11-14. Status: Accepted (WU-FI-002a2b).

## Context

BR-FI-007～010 and 013～014 require SD invoice issuing to post FI AR and GL in the same SQL transaction. SD's real source read (WU-SD-007a) and FI's account-mapping table do not exist yet, and FI must never read `sal_` tables. ADR-107 already provides `IGlJoinedPostingService` on the caller's scoped DbContext transaction.

## Decision

- **Public contract in `MyERP.Modules.Finance.Contracts`** so SD can call it: `ISalesInvoicePostingService.PostInCurrentTransactionAsync(SalesInvoicePostingCommand)` returning `SalesInvoicePostingResult` (Posted / Unchanged / Rejected / Fault, HTTP status, `FI.SALES_POSTING.*` code, receipt with source/customer/version/fingerprint, original and base gross, sales source, receivable and journal IDs for SD to compare before commit, BR-FI-010).
- **FI-owned SD read port** `IFinanceSalesInvoiceSource.ReadInvoiceAsync(company, invoice, DbTransaction)` in FI Contracts; SD implements it in WU-SD-007a. Tests inject a fake. A missing or duplicate registration is 500 `SOURCE_CONTRACT_UNAVAILABLE` (no DI start-up failure while SD has no implementation).
- **FI-owned account map port** `ISalesInvoiceAccountMapSource` (Application) returns mapping version + AR, revenue and output-tax account codes; none → 422 `MAPPING_MISSING`. Its version must equal the GL `SD/Invoice` mapping version (`IGlPostingMappingVersionSource`); otherwise GL rejects 409 `MAPPING_CHANGED`. The production mapping table is future work (backlog).
- **Order (BR-FI-013).** Transaction required (else `InvalidOperationException`) → command shape 400 → company 404 → port present → FI credit key lock (`FI.CREDIT.LOCK_TIMEOUT` → Rejected 409) → FI replay check on `ar_sales_source` (same customer+version+fingerprint → Unchanged with saved receipt, no SD read; otherwise 409 `SOURCE_CONFLICT`) → SD snapshot (404 missing/foreign; 422 Draft/Pending/Rejected/Deleted; 409 `SOURCE_MISMATCH` for customer/version/fingerprint; 422 `INVALID_AMOUNTS` for net+tax≠gross, decimal(19,4)/(19,8), ISO uppercase, base-currency rate 1 and identical amounts) → MD account check (422 `ACCOUNT_INVALID` with journal line) → joined GL (Dr AR base gross; Cr revenue base net; Cr output tax base tax; zero legs omitted) → `ar_sales_source` + `ar_posted_receivable` (open = original = base gross) via SaveChanges.
- **Outcomes.** GL Rejected passes through with its code and keeps the transaction open. GL Fault passes through (already rolled back). GL Unchanged without an FI source, or a write failure of the FI rows, rolls the caller's transaction back and returns Fault 500 (`INCONSISTENT_STATE` / `WRITE_FAILED`).
- **Replay before SD read.** FI's permanent source key is authoritative for replay, so a later SD state (`Posted`) or a soft-deleted SD history cannot change a committed FI result.

## Consequences

- SD-007a must implement `IFinanceSalesInvoiceSource` and call the service inside its issuing transaction, then compare the receipt before marking Posted.
- Frontend translations of `FI.SALES_POSTING.*` are added when the SD issue action surfaces them (no FI HTTP route in this unit).
- Two-connection serialisation and SD/audit failure rollback remain WU-FI-002a2c.
