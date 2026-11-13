# ADR-106 — FI sales source schema: split of WU-FI-002a, composite keys and immutability

Date: 2026-11-13. Status: Accepted (WU-FI-002a1).

## Context

WU-FI-002a covers schema, invoice posting through `IGlPostingService` under the FI credit lock, return-credit posting with per-line allocation, idempotent replay and two-connection races. That is well over the 90-minute limit for one round. BR-FI-008/011/014 also need SQL-level guarantees that the later service code must not be able to bypass: permanent source keys, same-company relations and immutable history. While writing the red tests we found that `LIKE '[A-Z][A-Z][A-Z]'` accepts lowercase codes under the database's default case-insensitive collation. The existing `ck_ar_credit_limit_currency`, `ck_ar_posted_receivable_currency` and `ck_gl_journal_currency` checks have the same weakness.

## Decision

- **Split.** WU-FI-002a1 handles schema and real SQL schema tests only. WU-FI-002a2 handles invoice posting: the credit lock, `IGlPostingService`, receivable creation, replay/409, two-connection serialisation and full rollback. WU-FI-002a3 handles return-credit posting: per-line cumulative component checks, open-AR reduction and over-credit rejection. The parent WU-FI-002a is done only when all three are done.
- **Tables.** `ar_sales_source` (REQ_DS43.189) holds one row per posted SD invoice or return. It stores the saved original- and base-currency net, tax and gross, the exchange rate (decimal 19,8), the source version and fingerprint, and the GL journal. `ar_credit_allocation` (REQ_DS43.190) stores per-invoice-line credit allocations, with quantity decimal(19,6) and the six money components as decimal(19,4).
- **Permanence.** Neither table has a soft-delete column. `ux_ar_sales_source_company_id_source_type_source_id` is a permanent key, so deleting or soft-deleting SD history can never free a source key. `ux_ar_sales_source_gl_journal_id` gives each journal at most one sales source.
- **Same-company composite FKs.** The FKs are source → `ar_credit_key` (company, customer), source → `gl_journal` (company, id), allocation → source (company, id) and allocation → `ar_posted_receivable` (company, id). To support the last FK, `ar_posted_receivable` gets the alternate key `ak_ar_posted_receivable_company_id_id`. Because the source FK references the credit key, a source can only be saved for a pair that has already gone through the credit-key lock protocol (ADR-073). SD invoice/return/line IDs and the MD customer ID remain scalars with no cross-module FK.
- **Reconciliation checks.** Net and tax are ≥ 0, gross is > 0, and net + tax = gross must hold exactly, for both original and base currency. Quantity must be > 0. `(credit_source_id, invoice_line_id)` is unique. The cumulative limit across credits (BR-FI-011) cannot be expressed as a row check. The 002a3 service enforces it under the credit lock, using the `ix_ar_credit_allocation_company_id_invoice_line_id` and `..._receivable_id` covering indexes.
- **Immutability.** AFTER UPDATE/DELETE triggers `tr_ar_sales_source_immutable` and `tr_ar_credit_allocation_immutable` raise SQL error 51024. The GL tables use 51023. `ar_posted_receivable` stays mutable because its open amount changes with credits and settlements.
- **Case-sensitive currency check.** The new currency checks use `COLLATE Latin1_General_BIN`. Fixing the three older checks is tracked separately as P3 WU-FI-001f. Tightening them requires checking the existing rows first.

## Consequences

- The 002a2/002a3 services can rely on SQL to reject duplicate sources, cross-company links, unbalanced components and later edits. They must still prove the lock order, replay behaviour and cumulative limits with real SQL.
- The migration `Finance_SalesSourceFoundation` is reversible. Down/Up was verified on `myerp` and Down on `myerp_test`.
