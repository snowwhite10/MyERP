# ADR-120 — Sales collection account map: one FI row per company for unapplied cash and AR control; bank account from MD (WU-FI-005a2a)

Date: 2026-11-14. Status: Accepted (WU-FI-005a2a).

## Context

WU-FI-005a2 (receipt and settlement posting) needs versioned accounts for two GL source types owned by FI: `FI/SalesCollection` (receipt approval: debit bank, credit unapplied-cash clearing, BR-FI-033) and `FI/CollectionWriteOff` (settlement approval: debit unapplied-cash clearing, credit AR control, BR-FI-037). The table, the store, both posting services and their real SQL tests do not fit one round, so WU-FI-005a2 is split as ADR-119 anticipated:

- **005a2a** — mapping table, migration and production sources (this ADR).
- **005a2b** — receipt posting service under the credit lock (receipt source key, debit bank / credit unapplied, unapplied = amount, replay `changed=false` / 409), real SQL red first.
- **005a2c** — settlement posting service (credit key → receipt → receivable IDs ascending → source key; caps rechecked; debit unapplied / credit AR; decrease receipt unapplied and receivable open amounts).
- **005a2d** — authorised maintenance API GET/PUT `/api/v1/gl/sales-collection-account-map`, `FinanceAccountUsage` and Development demo row (same pattern as ADR-110/118); screen afterwards.

## Decision

- New FI table `gl_sales_collection_account_map` (REQ_DS43.198), same shape and guards as the SD (ADR-109) and PUR (ADR-116) maps: one row per company, `varchar(30)` MD codes `unapplied_cash_account_code` and `receivable_account_code`, 6.4 audit columns, non-blank and positive company/version checks.
- **Extra check `ck_gl_sales_collection_account_map_distinct`:** the two codes may not be equal (trimmed, case-insensitive). With equal accounts a settlement would debit and credit the same account, so AR would never decrease in GL and the unapplied-cash balance would never clear.
- **The bank debit account is not in this map.** MD already owns the ledger account of each cash/bank fund account (`FundAccountInfo.LedgerAccountCode`, ADR-041). Duplicating it here would create two sources of truth. 005a2b reads the fund account through MD's public contract inside the posting transaction, refuses an inactive fund account or one without a ledger account (422) and snapshots the resolved MD account on the journal line. The posted receipt stores `bank_account_id`, while `IFundAccountLookup` is keyed by code; 005a2b must add an ID-based read to the MD contract (or receive the code from the saved receipt document) and record that in its own ADR.
- **AR control is separate from the SD invoice map's receivable code.** Both maps name an AR account; a company normally sets the same code. A difference is allowed (e.g. a clearing structure) but 005a2d's API should show a warning when they differ; it is not blocked in SQL because the SD map belongs to another posting flow and may change independently.
- **One row, two GL source types, one version.** `SalesCollectionAccountMapStore` implements `ISalesCollectionAccountMapSource` and the `FI/SalesCollection` `IGlPostingMappingVersionSource`; `CollectionWriteOffMappingVersionSource` delegates to it for `FI/CollectionWriteOff` (same pattern as SD/Return, ADR-113). Both postings use the unapplied-cash account, so any change must invalidate both in-flight postings (409 `MAPPING_CHANGED`).
- **Row version is the mapping version; never delete, never rewind.** Trigger `tr_gl_sales_collection_account_map_guard` raises **SQL 51030** (new number) on DELETE, a company change or an update that does not increase the version. Reads go through the scoped `MyErpDbContext` (joins the caller transaction), `AsNoTracking`, no cache; another company ID, a non-positive ID or no row yields null / 0 (fail closed).

## Consequences

- The Architecture registration test pins six mapping-version sources: `FI/DemoSeed`, `SD/Invoice`, `SD/Return`, `PUR/PurchaseInvoice`, `FI/SalesCollection`, `FI/CollectionWriteOff`.
- No row exists in any environment until 005a2d; collection posting fails closed (422) until then. MD can delete or re-type a mapped account until 005a2d extends `FinanceAccountUsage`; posting rechecks every account through `IChartOfAccounts`, so this cannot post to an unusable account.
- Migration `Finance_SalesCollectionAccountMap` is reversible (Down drops the trigger, then the table).
