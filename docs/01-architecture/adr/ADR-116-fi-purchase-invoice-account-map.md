# ADR-116 — PUR purchase invoice account map: separate FI row per company, row version is the mapping version (WU-FI-003a2a)

Date: 2026-11-14. Status: Accepted (WU-FI-003a2a).

## Context

WU-FI-003a2 (purchase invoice payable posting service) needs a versioned account mapping for `PUR/PurchaseInvoice`: AP control (credit base gross), purchases/expense or purchase-clearing (debit base net) and input tax (debit base tax), BR-FI-018. No such mapping existed; only the SD invoice map (ADR-109). Building the table, its source, the posting service with a PUR source port and real SQL tests in one round exceeds the 90-minute limit, so WU-FI-003a2 is split like WU-FI-002a4 was:

- **003a2a** — mapping table, migration, one production source for both ports (this ADR).
- **003a2b** — `IPurchaseInvoicePostingService`, FI-owned PUR source port `IFinancePurchaseInvoiceSource` (test fake; real one in PUR-008a), joined GL journal, `ap_purchase_source` + `ap_posted_payable`, replay/409; real SQL red first.
- **003a2c** — authorised maintenance API (GET/PUT with optimistic version, MD account validation, audit), MD account-usage protection of mapped codes, Development demo row; frontend screen afterwards as its own slice.

## Decision

- New FI table `gl_purchase_invoice_account_map` (prefix `gl_`: journal mapping is GL-owned, BR-FI-009), REQ_DS43.194. Same shape and guards as the SD map: one row per company (`ux_gl_purchase_invoice_account_map_company_id`), three `varchar(30)` MD account codes `payable_account_code`, `purchase_account_code`, `input_tax_account_code`, 6.4 audit columns, checks for non-blank codes and positive company/version.
- **A separate table, not extra columns on the SD map.** Each GL source type needs its own mapping version: a change to purchase accounts must not make in-flight SD invoice postings fail 409 `MAPPING_CHANGED`, and vice versa. Separate rows keep the versions independent and keep the SD map API (ADR-110) unchanged.
- **Row version is the mapping version; never delete, never rewind.** Trigger `tr_gl_purchase_invoice_account_map_guard` raises **SQL 51027** (a new number, distinct from the SD map's 51025 and AP's 51026) on DELETE, a company change or an update that does not increase the version.
- **One implementation for both ports.** `PurchaseInvoiceAccountMapStore` implements `IPurchaseInvoiceAccountMapSource` and `IGlPostingMappingVersionSource` (`PUR`/`PurchaseInvoice`), reading through the scoped `MyErpDbContext` (joins the open transaction), `AsNoTracking`, no cache. Another company's ID, a non-positive ID or no row yields null / 0 (fail closed: posting will be 422 mapping missing; standalone GL 409 `MAPPING_CHANGED`).
- **Nonrecoverable input tax** (BR-FI-018) is not modelled in this slice: the saved PUR tax snapshot is treated as claimable. If PUR later saves a nonrecoverable flag, a fourth nullable cost account (or a per-tax-code map) will be added under a new ADR before such invoices can post; until then 003a2b must not guess.

## Consequences

- The Architecture registration test now pins four mapping sources: `FI/DemoSeed`, `SD/Invoice`, `SD/Return`, `PUR/PurchaseInvoice`.
- No row exists in any environment until 003a2c adds the maintenance API and demo row; purchase posting will fail closed until then.
- MD can still delete or re-type an account that the purchase map names until 003a2c extends `FinanceAccountUsage`; posting rechecks every account through `IChartOfAccounts`, so this cannot post to an unusable account.
