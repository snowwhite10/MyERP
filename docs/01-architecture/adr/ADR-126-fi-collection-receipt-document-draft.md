# ADR-126 — FI collection receipt document: separate draft table, soft delete, filtered bank-reference key; WU-FI-005b split

Date: 2026-11-14. Status: Accepted (WU-FI-005b1).

## Context

WU-FI-005a built the posted ledger rows (`ar_collection_receipt`, settlement, allocation) and the posting services. They read the saved receipt document through `IFinanceCollectionReceiptSource`, which only tests implemented. WU-FI-005b had to add the receipt and settlement documents, SYS approval in the same transaction, HTTP, permissions and Swagger. That is far more than 90 minutes of work.

Open questions:

1. Should the draft and approval state live in the posted table or in a separate table?
2. Should delete be physical or soft?
3. How should bank-reference uniqueness work for drafts, since `ar_collection_receipt` already makes the reference permanent once posted?
4. What data scope applies?

## Decision

- **Split** 005b into: 005b1, the receipt document table and its CRUD service; 005b2, the settlement document with lines; 005b3, submit, the SYS result handler, posting in the same transaction and the real source ports; 005b4, HTTP, list, the permission catalog and roles, number-rule seed and Swagger.
- **Separate table** `ar_collection_receipt_doc` (REQ_DS43.199) owns the document number, editable fields and workflow state. The posted table stays immutable, and its `source_id` is this document's ID. Status is stored as `FinanceCollectionReceiptState` text, limited to Draft, Pending, Rejected, Approved and Posted. Delete is the `is_deleted` flag, and the 005b3 source port reports it as `Deleted`.
- **Soft delete**, allowed only for Draft or Rejected. A check constraint enforces it: `is_deleted = 1` requires `deleted_at` and a Draft or Rejected status. Soft delete keeps the number history (the number is generated in the transaction, so gaps come only from deletes) and releases the bank reference.
- **Bank-reference key**: a filtered unique index `(company_id, bank_account_id, bank_reference) WHERE is_deleted = 0`, compared case-insensitively under the default collation. Before every write the service also gives a friendly 409 `FI.COLLECTION.BANK_REFERENCE_DUPLICATE` when a live document or a posted receipt already uses the reference. Under concurrency the index decides, and the 2601/2627 error is translated to the same 409.
- **Validation order**: permission (403) → fields (400, all at once; reference and remark trimmed, a blank remark becomes null) → for edit or delete, the locked row (UPDLOCK, HOLDLOCK) checked as 404, then version (409), then status (422) → MD checks through public contracts: base currency (422), approved customer (MD error) and fund account. A missing or foreign-company fund account is 422 `FUND_ACCOUNT_NOT_FOUND`. An inactive one is 422 `INACTIVE`, and one in another currency is 422 `CURRENCY_MISMATCH`. A fund account with no ledger account may be saved, because posting checks it (BR-FI-033).
- Currency is always the MD base currency (BR-FI-031). The client never sends it.
- **Scope**: company-wide, like other FI configuration (ADR-075). Permissions are `AR.COLLECTION.VIEW/CREATE/EDIT/DELETE`, checked by the service now. The catalog and role grants come in 005b4.
- Editing a Rejected document returns it to Draft. A zero-change edit still saves a new version, because it is a draft save and not a posting result.

## Consequences

- The document number needs an `AR.COLLECTION` SYS number rule. A missing rule is SYS's 422, and nothing is saved. The Development seed for the rule comes with 005b4.
- 005b3 must build the canonical fingerprint from the saved document. The source port must map `is_deleted` to `Deleted` and read through the caller's transaction.
