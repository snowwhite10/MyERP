# ADR-121 — Collection receipt posting service: FI-owned document port, MD fund account read by ID, bank-reference lock (WU-FI-005a2b)

Date: 2026-11-14. Status: Accepted (WU-FI-005a2b).

## Context

WU-FI-005a2b posts an approved sales collection receipt (BR-FI-033/034/036/038): debit the bank, credit unapplied-cash clearing, and save `ar_collection_receipt` with unapplied = amount, all inside the SYS final-approval transaction. Three questions were open:

1. The receipt document (Draft/Pending/Approved workflow table) is built in 005b, but the posting service must be tested now.
2. `ar_collection_receipt` stores `bank_account_id` (an MD fund account ID), while `IFundAccountLookup` only reads by code (ADR-120 left this to this slice).
3. The bank reference must be unique per company and fund account (BR-FI-033, FI-AR-COLL-008). Two receipts with different customers do not share a credit lock or a source key, so a pre-check alone could race into the unique index after the GL journal was written.

## Decision

- **FI-owned document port.** `IFinanceCollectionReceiptSource` (FI Application, not Contracts — the document is FI's own) returns a `FinanceCollectionReceiptSnapshot` read through the caller's transaction. Until 005b registers the real implementation, the posting service returns 500 `FI.COLLECTION_POSTING.SOURCE_CONTRACT_UNAVAILABLE`; only tests register a fake. `ICollectionReceiptPostingService` and `CollectionPostingErrors` are in FI Contracts like the SD and PUR posting services.
- **Only `Approved` is postable.** SYS final approval saves Approved and calls FI in the same transaction; `Posted` is accepted only as a replay of FI's committed row (checked before the read), the same as PUR `Matched`/`Posted` (ADR-117).
- **MD contract `IFundAccountLookup.FindByIdAsync(long id)`.** Added to MD's public contract (current company, includes inactive, null for missing/deleted/foreign/non-positive). FI stores the stable ID; codes can be renamed in MD while history must keep pointing at the same account. Receiving the code from the document was rejected: the document would duplicate MD data and the ID would still need checking.
- **Fund account rules.** Cash and Bank fund accounts are both accepted (a cash receipt is common in retail; the column name `bank_account_id` is historical). The fund account must be active, in the receipt currency (= base currency in this slice, BR-FI-031) and have a ledger account; otherwise 422 `FUND_ACCOUNT_INVALID`. A ledger account equal to the mapped unapplied-cash account is 422 `ACCOUNT_INVALID` on journal line 2 (the journal would not move cash).
- **Lock order (BR-FI-036):** credit key `ar_credit:{company}:{customer}` → receipt source key `ar_collection:{company}:{receipt}` → FI replay check → document read → bank-reference key `ar_collection_bank:{company}:{fund account}:{UPPER(reference)}` → duplicate check → GL source key (inside `IGlJoinedPostingService`). All are transaction-owned exclusive applocks; SQL 51034 (new number) or 1222 maps to 409 `LOCK_TIMEOUT`; a credit lock timeout is 409 `FI.CREDIT.LOCK_TIMEOUT`. The bank-reference key folds case like the unique index's case-insensitive collation, so two receipts that differ only in case serialize and the second sees 409 `BANK_REFERENCE_DUPLICATE` instead of a 500 from the index. It is the last FI lock before GL, so it cannot form a cycle with the credit or source keys.
- **Canonical bank reference.** The saved reference must already be trimmed, non-blank, ≤ 50 characters and free of control characters; FI does not normalize it (422 `INVALID_SOURCE`).
- Replay returns the committed row (current unapplied amount included); another customer, version or fingerprint is 409 `SOURCE_CONFLICT`. GL Unchanged without an FI row is a 500 `INCONSISTENT_STATE` fault that rolls back the caller transaction, the same as ADR-107/117.

## Consequences

- MD's `IFundAccountUsage` asks by code, but FI stores IDs; FI does not yet report fund accounts used by posted receipts, so MD could soft-delete one. Posted history keeps the ID and journal snapshot, and a new posting with that account fails closed (422). Backlog WU-FI-005a2e adds an FI `IFundAccountUsage` (resolve the code to an ID through MD) before 005b goes live.
- Two-connection races (same receipt, same bank reference) and caller rollback after posting are proven in 005a3.
- No migration, endpoint or front-end change.
