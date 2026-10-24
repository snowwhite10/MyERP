# FI Finance requirements

## WU-FI-000d customer credit contract (design only)

BlueWhale Accounting sub-functions 6.1–6.6 retain their existing IDs for collections, payments and write-offs. New sub-function 6.7 is Customer Credit. This design unit defines the FI-owned contract required by REQ_F4112.6.1; it adds no database table, service, API or screen. WU-FI-001a/b must implement and verify it before SD order approval can use it.

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4116.7.1 | 1.0 | System should allow user to maintain a customer credit limit in the company base currency. | Developer | One effective, approved limit per company/customer/date is returned; overlapping active periods, disabled or deleted limits, currency mismatch, negative amount and stale version are rejected. Zero is valid. Every mutation is authorised, scoped, audited and protected by the shared credit lock. |
| REQ_F4116.7.2 | 1.0 | System should enable user to obtain the current customer credit exposure. | Developer | FI returns the effective limit and committed, posted open AR for one company/customer with base currency, evaluation date and version. Missing or ambiguous data is an explicit unavailable result. SD receives no FI document details and cannot substitute zero for missing AR. |
| REQ_F4116.7.3 | 1.0 | System should enable user to reserve a transaction-scoped customer credit decision lock. | Developer | A caller joins the existing SQL transaction and locks the FI-owned company/customer key before reading exposure and SD commitments. Limit edits, AR changes and SD order decisions follow the same lock order. Rollback releases the lock without a saved credit decision. |

Acceptance is planned. FI never counts SD unbilled commitments; SD supplies that component when evaluating an order. A backdated order is evaluated at server `IClock.Today`, not its document date. See BR-FI-001–006 and FI-CREDIT-001–020.
