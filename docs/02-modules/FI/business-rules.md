# FI business rules

## WU-FI-000d customer credit boundary (planned)

- BR-FI-001: FI owns company/customer credit limits and posted open AR. SD owns Approved, uninvoiced order commitments. `ICustomerCreditService` returns only FI data; consumers never read `ar_` or `gl_` tables.
- BR-FI-002: The credit snapshot uses the current company base currency and server `IClock.Today`. It contains company/customer IDs, currency, effective limit, posted open AR, evaluation date, read timestamp/version and availability reason. A missing or overlapping effective limit, missing company currency, negative/overflowed exposure, mismatched identity/currency or unavailable provider fails closed. An explicit zero limit is valid.
- BR-FI-003: The FI-owned lock key is `(companyId, customerId)`. It must be acquired in the caller's existing SQL transaction before FI exposure and SD commitment reads. Limit edits, AR posting/settlement and SD order decisions use that lock; same-key calls serialize, distinct keys may proceed. The database, rather than an in-process mutex, enforces it across API instances. Lock timeout is a named retryable failure, never permission to approve.
- BR-FI-004: Effective credit periods use inclusive business dates. At most one active approved limit matches a date. A deactivated or soft-deleted record retains history and its code/identity; it cannot silently make an older overlapping row effective. The write path locks the credit key, checks overlap and version, then audits the decision.
- BR-FI-005: FI open AR includes only committed posted receivables less committed allocations, credit notes and reversals. Draft/Pending, failed and rolled-back documents contribute zero. A replay of the same source cannot add exposure again. The future AR posting unit must prove this with real SQL and the same credit lock.
- BR-FI-006: Read-only preview is advisory. SD order submit and SYS final approval must reread under lock in their own transaction; no client-supplied limit, AR amount, timestamp or decision is trusted. FI contract failure blocks SD approval with a named reason. No partial result or success indication is returned for a failed write.

These are design decisions. WU-FI-001a/b owns migration, production contract, authorised HTTP and real SQL verification; WU-SD-006b owns the combined SD decision and override.
