# ADR-125 — Collection receipt and write-off concurrency relies on the credit key plus resource keys; WU-FI-005a3 adds no product code

Date: 2026-11-14. Status: Accepted (WU-FI-005a3).

## Context

WU-FI-005a2b (ADR-121) and 005a2c (ADR-122) implemented receipt and write-off posting with transaction-owned applocks in the BR-FI-036 order: credit key `ar_credit:{company}:{customer}` → receipt key `ar_collection:{company}:{receipt}` → receivable keys `ar_receivable:{company}:{id}` ascending → settlement key `ar_collection_writeoff:{company}:{settlement}` → GL source key. WU-FI-005a3 had to prove with two real SQL connections that concurrent write-offs of the same receipt or the same invoice cannot over-apply, that a caller failure after posting rolls everything back, and that a held key times out as a named 409 (FI-AR-COLL-011, 012, 023).

Two questions were open:

1. Does the waiter block on the credit key or only on the receipt/receivable keys? A receipt and its invoices belong to one customer, and a write-off whose command customer differs from the receipt's customer is rejected (`RECEIPT_MISMATCH`) after the receipt key. So every write-off that can legally touch a receipt or invoice holds the same credit key first. The waiter therefore never reads the write-off document while the winner is in flight; the receipt and receivable keys are the second line of defence for future callers that might skip the credit key.
2. Is a red phase possible? The locks already exist, so the new tests pass without product changes.

## Decision

- WU-FI-005a3 is a **verification slice**: seven real-SQL tests (`FinancePostingServiceTests.CollectionRace.cs`, FI-COLL-RACE-001～007), no product code, no migration, no front end.
- The tests gate the first write-off at the document read (credit key held), assert the second connection has not read the document after 700 ms, then release. The loser rereads committed balances after it gets the keys and fails with 422 `RECEIPT_INSUFFICIENT` (same receipt) or 422 `ALLOCATION_INVALID` line 1 (same invoice); an identical claim replays `Unchanged` without reading the document.
- Caller failure after posting is simulated by a SQL `THROW` (stand-in for the SD/SYS status update) and by a duplicate-key `SaveChanges` (stand-in for an audit/SYS write); the caller rolls back and every FI row, GL journal, GL source key, credit key and balance returns to its previous value; the same posting then succeeds.
- Lock timeout is proven by another connection holding the receipt key or a receivable key (write-off) or the receipt source key (receipt posting) with a 300 ms timeout: 409 `FI.COLLECTION_WRITEOFF.LOCK_TIMEOUT` / `FI.COLLECTION_POSTING.LOCK_TIMEOUT`, caller transaction still open, zero rows; the receipt posting does not read the document.
- Because no product code changed, the red phase is replaced by checking that each race test would fail on a lock-free implementation: the "waiter has not read" assertion and the 422 outcomes depend on the waiter observing committed balances after the winner commits.

## Consequences

- FI-AR-COLL-011 passes at service level. FI-AR-COLL-012/023 pass for FI/GL/caller failures at service level; SYS approval, audit and UI error display remain with the receipt/write-off documents (WU-FI-005b and later).
- Future callers (SD collection documents, 005b) must keep calling the posting services inside their own transaction and must not take receivable or receipt keys before the credit key.
