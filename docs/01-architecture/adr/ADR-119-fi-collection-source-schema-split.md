# ADR-119 — FI sales collection and settlement posted-source schema; split of WU-FI-005a

Date: 2026-11-14. Status: Accepted (WU-FI-005a1).

## Context

WU-FI-005a covers the receipt and settlement schema, the two posting services (receipt approval: bank debit / unapplied-cash credit; settlement approval: unapplied-cash debit / AR credit), replay, two-connection races and full rollback. As with WU-FI-002a (ADR-106) and WU-FI-003a (ADR-115), that is well over the 90-minute round limit. BR-FI-033–037 and BR-FI-040 need SQL-level guarantees that later service code cannot bypass: permanent source keys, a permanent bank reference, receipt/settlement/receivable consistency and immutable posted history.

## Decision

- **Split.** WU-FI-005a1: schema and real SQL schema tests only. WU-FI-005a2: posting services under the customer credit lock and their FI account mapping (bank account → GL, unapplied-cash clearing, AR control), GL journals through `IGlJoinedPostingService`, replay `changed=false` and 409, real SQL red first. It may be split further (for example a2a mapping, a2b receipt posting, a2c settlement posting). WU-FI-005a3: two-connection over-allocation races, lock order and full rollback. The parent WU-FI-005a is done only when all three are done.
- **Posted ledger tables, separate from documents.** These tables hold only committed, posted history. The Draft/Pending/Rejected receipt and settlement documents with SYS workflow belong to WU-FI-005b. `source_id` names that document as a scalar for now. 005b may add an FI-internal FK once the document table exists. Keeping posted history in its own table means no draft edit or soft delete can touch it.
- **Tables.** `ar_collection_receipt` (REQ_DS43.195): customer, `SalesCollection` source key/version/fingerprint, GL journal, business date, MD bank account ID (scalar), bank reference `nvarchar(50)`, base currency, amount and unapplied amount (decimal(19,4)). `ar_collection_settlement` (REQ_DS43.196): customer, `CollectionWriteOff` source key/version/fingerprint, GL journal, receipt, business date, base currency, total amount. `ar_collection_allocation` (REQ_DS43.197): settlement, receipt, posted receivable and positive amount.
- **Base currency only.** BR-FI-031 limits this first slice to company base currency. There is no original-currency or exchange-rate column. Cross-currency settlement needs an exchange-difference policy and a new design.
- **Keys.** `(company, source_type, source_id)` is unique on receipt and settlement. Each journal belongs to at most one receipt or one settlement. `(company, bank_account_id, bank_reference)` is unique on posted receipts. There is no soft-delete column, so posted history permanently holds the reference (FI-AR-COLL-008). Uniqueness of draft references against posted history is a 005b service check. Allocation uniqueness is `(company, settlement, receivable)`, not `(receipt, receivable)`, because later settlements may apply the same receipt to the same invoice again.
- **Same company.** Receipt and settlement reference `ar_credit_key (company, customer)`, so every collection customer has the lock row used by credit decisions (BR-FI-036). Composite FKs: GL journal, settlement → receipt, allocation → settlement, receipt and `ar_posted_receivable` (company, id). MD bank and customer IDs stay scalar.
- **Triggers (SQL 51029, new number for AR collection).**
  - `tr_ar_collection_receipt_origin`: no delete. A new row must have unapplied = amount, because receipt approval creates unapplied cash, not settled AR (BR-FI-034). After that, every origin column is frozen, and unapplied may only decrease. The check keeps it within 0…amount. Binary collation catches case-only changes. A future receipt or settlement reversal that restores unapplied cash needs its own compensating design, and that design would revise this trigger.
  - `tr_ar_collection_settlement_immutable`: the customer and currency must equal the receipt's. No update or delete.
  - `tr_ar_collection_allocation_immutable`: the allocation's receipt must equal its settlement's receipt. The receivable must be an `Invoice` receivable of the settlement's customer and currency. No update or delete.
- **What SQL does not enforce.** Aggregate caps (sum of allocations ≤ unapplied receipt, ≤ open receivable, and = settlement total) depend on several rows written in sequence. The 005a2 service enforces them under the ordered locks. Check constraints still stop the stored unapplied and open amounts from going negative, as a last line of defence.

## Consequences

- 005a2/005a3 can rely on SQL to reject duplicate sources and bank references, cross-company or cross-customer links, return receivables, an initially part-applied receipt, an increase of unapplied cash and any edit of posted history. They must still prove lock order, caps, replay and rollback with real SQL.
- Migration `Finance_CollectionSourceFoundation` is reversible. Down drops the triggers, then the tables. Down/Up was verified on `myerp`, and `has-pending-model-changes` reports none.
