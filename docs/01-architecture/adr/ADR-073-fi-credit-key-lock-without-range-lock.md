# ADR-073 — FI credit key lock: pair applock plus row UPDLOCK, no key-range lock

Date: 2026-11-04. Status: Accepted (WU-FI-001a).

## Context

BR-FI-003 required a transaction-owned SQL application lock on `ar_credit:{company}:{customer}` and then an "update/serializable" lock on the permanent `ar_credit_key` row. The first real two-connection test (FI-CREDIT-021) showed that `UPDLOCK, HOLDLOCK` on a not-yet-existing pair takes a key-range lock on the unique index gap. While one transaction held an uncommitted first-use key for customer 501, a different customer 502 of the same company waited until the 30-second command timeout. That violates the rule that distinct pairs never share a lock.

## Decision

- `ICustomerCreditService.AcquireCreditLockAsync` takes `sp_getapplock` (Exclusive, owner Transaction, default 15 s) on the exact pair, then reads the key with `UPDLOCK, ROWLOCK` and inserts it when absent. The applock alone serializes find-or-insert for the pair, so no range lock is needed.
- A negative applock result or SQL lock timeout becomes the named, retryable `ConflictException` `FI.CREDIT.LOCK_TIMEOUT` (409). It never yields a snapshot.
- `GetSnapshotAsync` checks `APPLOCK_MODE(...) = 'Exclusive'` in the caller's transaction and throws `InvalidOperationException` otherwise, so a reader cannot skip the lock.
- Callers run in READ COMMITTED. A SERIALIZABLE caller would reintroduce range locks and must not call the credit service.
- The limit and receivable tables reference the key through the composite alternate key `(company_id, customer_id)` instead of a surrogate `credit_key_id`, so SQL guarantees that every limit/AR row belongs to a key of the same company and customer.

## Consequences

Different customers in the same company proceed independently while one pair is locked (proved by FI-CREDIT-021). The unique `(company_id, customer_id)` constraint remains the last line of integrity. Future FI writers (limit CRUD in WU-FI-001b, AR posting in WU-FI-002a) and the SD order decision (WU-SD-006b) must call `AcquireCreditLockAsync` first in the same transaction.
