# ADR-115 — FI purchase payable source schema: split of WU-FI-003a, payable origin trigger

Date: 2026-11-14. Status: Accepted (WU-FI-003a1).

## Context

WU-FI-003a covers the schema, PUR invoice posting through `IGlJoinedPostingService`, idempotent replay and two-connection races. As with WU-FI-002a (ADR-106), that is well over the 90-minute round limit. BR-FI-016/019/022 need SQL-level guarantees that later service code must not be able to bypass: a permanent source key, a same-company journal, an AP payable whose origin equals its source, and immutable history.

## Decision

- **Split.** WU-FI-003a1 handles schema and real SQL schema tests only. WU-FI-003a2 handles the posting service: the PUR source port (FI-owned interface with a test fake until PUR-008a supplies the real one), mapping, joined GL journal (debit purchases/expense and input tax, credit AP control; zero tax omitted), source and payable rows, and replay/409. WU-FI-003a3 handles two-connection serialisation and full rollback. The parent WU-FI-003a is done only when all three are done.
- **Tables.** `ap_purchase_source` (REQ_DS43.192) mirrors `ar_sales_source`: supplier scalar, source type, PUR invoice ID/version, fingerprint, GL journal, business date, currency, base currency, exchange rate decimal(19,8), original and base net/tax/gross decimal(19,4). `ap_posted_payable` (REQ_DS43.193) holds one payable per source with original and open amounts in both original and base currency.
- **Source type.** Only `PurchaseInvoice` is allowed. Purchase returns against invoiced quantity need a separately specified FI note (BR-FI-022), so the check rejects other types until that design exists.
- **No supplier lock table.** Unlike AR, there is no credit limit to protect, so no `ap_supplier_key` is created. Source-key serialisation is done by the 003a2/003a3 service (application lock on the source key under PUR's own locks, BR-FI-020). AP payment slices (006a) can add a supplier key if their race analysis needs one.
- **Permanence.** No soft-delete column on either table. `ux_ap_purchase_source_company_id_source_type_source_id` is a permanent key; `ux_ap_purchase_source_gl_journal_id` gives each journal at most one purchase source; `ux_ap_posted_payable_company_id_purchase_source_id` gives each source at most one payable.
- **Same company.** Source → `gl_journal` (company, id) and payable → source (company, id) are composite FKs. PUR invoice and MD supplier IDs stay scalar, with no cross-module FK.
- **Payable origin trigger.** `tr_ap_posted_payable_origin` (AFTER INSERT/UPDATE/DELETE, SQL 51026) rejects a payable whose supplier, posting date, currency, base currency, original amount or original base amount differs from its source, any later change to those origin columns, and any delete. Currency comparisons use `Latin1_General_BIN`. Open amounts, version and update stamps may change so that later payment/settlement slices can reduce the open balance (BR-FI-022). The check `ck_ap_posted_payable_amounts` keeps 0 ≤ open ≤ original in both currencies and requires the two open amounts to be zero together.
- **Immutability.** `tr_ap_purchase_source_immutable` raises 51026 on any UPDATE/DELETE of a source. A new error number keeps AP failures distinguishable from AR (51024) and the account map (51025).

## Consequences

- 003a2/003a3 can rely on SQL to reject duplicate sources, cross-company journals, unbalanced components, a payable that disagrees with its source, and edits to posted history. They must still prove lock order, replay and rollback with real SQL.
- Migration `Finance_PurchaseSourceFoundation` is reversible; Down/Up was verified on `myerp`, and `has-pending-model-changes` reports none.
