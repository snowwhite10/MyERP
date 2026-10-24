# FI Finance requirements

## WU-FI-000d customer credit contract (design only)

BlueWhale Accounting sub-functions 6.1–6.6 retain their existing IDs for collections, payments and write-offs. New sub-function 6.7 is Customer Credit. This design unit defines the FI-owned contract required by REQ_F4112.6.1; it adds no database table, service, API or screen. WU-FI-001a/b must implement and verify it before SD order approval can use it.

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4116.7.1 | 1.0 | System should allow user to maintain a customer credit limit in the company base currency. | Developer | One effective, approved limit per company/customer/date is returned; overlapping active periods, disabled or deleted limits, currency mismatch, negative amount and stale version are rejected. Zero is valid. Every mutation is authorised, scoped, audited and protected by the shared credit lock. |
| REQ_F4116.7.2 | 1.0 | System should enable user to obtain the current customer credit exposure. | Developer | FI returns the effective limit and committed, posted open AR for one company/customer with base currency, evaluation date and version. Missing or ambiguous data is an explicit unavailable result. SD receives no FI document details and cannot substitute zero for missing AR. |
| REQ_F4116.7.3 | 1.0 | System should enable user to reserve a transaction-scoped customer credit decision lock. | Developer | A caller joins the existing SQL transaction and locks the FI-owned company/customer key before reading exposure and SD commitments. Limit edits, AR changes and SD order decisions follow the same lock order. Rollback releases the lock without a saved credit decision. |

Acceptance is planned. FI never counts SD unbilled commitments; SD supplies that component when evaluating an order. A backdated order is evaluated at server `IClock.Today`, not its document date. See BR-FI-001–006 and FI-CREDIT-001–020.

## WU-FI-002d sales receivable sources (design only)

This slice defines FI's side of REQ_F4112.5.1 and REQ_F4112.7.1. It creates no journal, receivable, credit note, endpoint or database table. FI owns the posted receivable and credit note; SD owns the commercial invoice, return and unbilled commitment. A source may claim Posted only after FI and SD effects commit together under ADR-071.

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4116.8.1 | 1.0 | System should enable user to post a receivable from an issued sales invoice. | Developer | One immutable FI source per company and saved SD invoice posts a balanced GL journal, tax liability and gross AR in the caller's SQL transaction. FI verifies source identity, company/customer, base and document currencies, saved component totals and account eligibility through public contracts. Same source and payload replays the original result; changed payload conflicts. FI and SD move equal base gross from approved unbilled commitment to open AR under the credit key lock before commit. |
| REQ_F4116.8.2 | 1.0 | System should enable user to issue a sales return credit note against posted receivables. | Developer | One immutable FI credit source per company and Posted SD return can link multiple original invoice postings, with invoice-line allocations. FI validates remaining creditable original/base net and tax, posts a balanced reversal journal and applies the credit only to those original receivables. Billed credit reduces open AR; unbilled return release remains SD-owned. Duplicate or failed calls create no second credit, allocation, journal or exposure change. |
| REQ_F4116.8.3 | 1.0 | System should enable user to inspect sales receivable source and credit allocations. | Developer | A company-scoped, authorised read reports immutable source IDs, original and remaining amounts, currency, posting state, journal ID and linked credit allocations. It never reports uncommitted or other-company details. Source replay and timeout recovery can resolve the committed result by the stable source key; a missing source is explicit. |

Change record: 2026-10-24T12:28:04+08:00 WU-FI-002d adds the planned sales receivable and credit source contract. Implementation, real SQL tests and any read screen are assigned to later slices; none is claimed here.
