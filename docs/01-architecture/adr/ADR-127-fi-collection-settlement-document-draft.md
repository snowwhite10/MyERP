# ADR-127 — FI collection settlement document: header and line tables, customer from the posted receipt, draft checks reuse posting rules

Date: 2026-11-14. Status: Accepted (WU-FI-005b2).

## Context

WU-FI-005a2c built the write-off posting service. It reads the saved settlement document through `IFinanceCollectionWriteOffSource`, which so far only tests implement. WU-FI-005b2 adds the settlement document and its draft CRUD service. The receipt document (ADR-126) set the pattern: a separate draft table, soft delete, and checks in the order 403 → 400 → 404/409/422.

Open questions:

1. What does `receipt_id` reference, and should it have a database FK?
2. Should the client send the customer and the total?
3. How strict should draft-time checks be, and which error codes should they use?
4. How are lines stored and edited?

## Decision

- **Tables**: the header `ar_collection_settlement_doc` (REQ_DS43.200) and the lines `ar_collection_settlement_doc_line` (REQ_DS43.201). Both have the standard audit and version columns. A line has a same-company composite FK to its header and to `ar_posted_receivable`; both tables are in the FI module, so the FK is allowed. `(company, document, receivable)` and `(company, document, line_no)` are unique, and `line_no` is 1–1000.
- **`receipt_id`** is the FI receipt document ID, which is the posted `ar_collection_receipt.source_id`. That matches the `FinanceCollectionWriteOffSnapshot.ReceiptId` contract. There is no FK, because the posted receipt is keyed by (company, source type, source ID) rather than by a plain principal key. Instead the service checks the reference on every save and posting checks it again. The service requires a posted receipt for that ID in the current company.
- **Server-derived fields**: the customer comes from the posted receipt, so the client cannot send a mismatching one. The currency is the MD base currency. The total is the sum of the lines (§6.3: header = sum of lines). The number comes from SYS (`AR.SETTLEMENT`) inside the transaction.
- **Line order**: request-shape errors (400) use the request index, for example `lines[2].amount`. Saved lines are then sorted by receivable ID and numbered 1..n. This matches the posting lock order, and the 422 `lineErrors` use these line numbers together with the receivable ID.
- **Draft checks reuse posting**: the service builds a `FinanceCollectionWriteOffSnapshot` and calls `CollectionWriteOffPostingRules.CheckReceipt` and `CheckLines`. Errors therefore use the posting codes `FI.COLLECTION_WRITEOFF.RECEIPT_NOT_POSTED`, `RECEIPT_MISMATCH`, `RECEIPT_INSUFFICIENT` and `ALLOCATION_INVALID`, with a `lineErrors` extension listing every bad line and the `FI.COLLECTION_WRITEOFF.LINE.*` reasons. The screen needs one set of translations, and a draft that saves is one posting would accept at that moment. Drafts take no locks and reserve nothing: two drafts may each use the whole unapplied amount. Posting re-checks under locks (BR-FI-035/040, FI-COLL-RACE).
- **Edit** replaces all lines. Old lines are deleted physically inside the transaction (they are draft data). Then the header version bumps and the new lines are inserted. An edited Rejected document returns to Draft. **Delete** is soft (`is_deleted`, Draft or Rejected only, enforced by a check constraint) and keeps the number and the lines.
- Document-level codes are `FI.COLLECTION.SETTLEMENT_NOT_FOUND` (404) and `FI.COLLECTION.SETTLEMENT_INVALID_STATUS` (422). A missing base currency reuses `FI.COLLECTION.BASE_CURRENCY_MISSING`.
- Permissions are `AR.SETTLEMENT.VIEW/CREATE/EDIT/DELETE`, checked in the service. Receipt permissions do not grant them. The scope is company-wide (ADR-075). The catalog and roles come in 005b4.

## Consequences

- 005b3 implements `IFinanceCollectionWriteOffSource` over these tables, mapping `is_deleted` to `Deleted` and lines in `line_no` order, and builds the fingerprint from the saved header and lines.
- The UI (005c) must map `lineErrors` to rows by receivable ID or line number in saved order, not by request index.
- 005b4 needs an `AR.SETTLEMENT` number rule seed.
