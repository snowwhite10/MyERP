# ADR-107 — Joined GL posting in the caller's transaction; split of WU-FI-002a2

Date: 2026-11-14. Status: Accepted (WU-FI-002a2a).

## Context

BR-FI-013 requires invoice and return posting to join the caller's SQL transaction. The FI credit key, GL journal, `ar_sales_source`, `ar_posted_receivable` and the SD state must commit or roll back together, and the journal must still be written through the GL posting service. `IGlPostingService` (WU-FI-004a3x) cannot do that. Every attempt opens its own DI scope, DbContext and transaction and commits it. After an unknown result it retries up to three times and reads committed state in a fresh transaction. Inside a caller transaction this is unsafe: an internal commit would publish GL rows that the caller might roll back, and a fresh-connection lookup could report the source as "Unchanged" while the caller's own transaction is already gone. The original WU-FI-002a2 (joined posting, invoice service, replay and two-connection races) is also well over the 90-minute round limit.

## Decision

- **Split WU-FI-002a2.**
  - 002a2a: joined GL posting (this ADR).
  - 002a2b: invoice receivable posting service. Order: credit lock → SD source port → joined GL → `ar_sales_source` + `ar_posted_receivable`, with replay and 409.
  - 002a2c: two-connection serialisation and full rollback of the invoice service.
- **`IGlJoinedPostingService.PostInCurrentTransactionAsync(request)`** (FI Application; Infrastructure `GlJoinedPostingService`, scoped):
  - It uses the scoped `MyErpDbContext` and its `CurrentTransaction`. Without one it throws `InvalidOperationException`, because that is a programming error.
  - It runs the same `GlPostingAttemptRunner` order (precheck, period Shared + source Exclusive applocks, authority reads, decision, plan, write) exactly once through `GlPostingAttemptRunner.PostJoinedAsync`.
  - The session (`GlPostingAttemptSession` without an owned scope) never commits, rolls back or disposes the caller's transaction or scope.
- **Outcome semantics.**
  - Posted / `changed=true`: the rows are written in the caller's transaction and become durable only when the caller commits.
  - Unchanged and Rejected (400/404/409/422): no rows are written and the caller's transaction stays open; the caller decides what to do.
  - Fault: the caller's whole transaction has already been rolled back. This happens either in the writer or in the joined service, which also rolls back on any exception from the posting path.
- **Failure mapping without retry or lookup.** `GlPostingFailureRules.ClassifyJoined`:
  - Deadlock (1205), lock timeout (1222), applock not granted (51021), transport errors, a lost connection, and the Commit phase map to 503 `FI.GL_POSTING.TRANSACTION_ABORTED`. Nothing was committed, so the caller may retry its whole operation.
  - Every other failure maps to 500 `FI.GL_POSTING.WRITE_FAILED`.
  - A joined result is never resolved from committed state.
- **Other preconditions.** The caller's DbContext must have no unsaved changes; the existing writer guard turns a violation into a Fault that rolls everything back. The new code is added to the frontend whitelist and to `errors.json` in all three languages; it is certain, not "result unknown".

## Consequences

- The 002a2b invoice service can take the credit key, write GL, source and receivable rows, and let SD commit once.
- Standalone `IGlPostingService` behaviour is unchanged (same runner, same tests).
- Lock contention inside a joined posting waits for the writer's default 15 s applock timeout and then aborts the caller. Making that timeout configurable is left to 002a2c if the race tests need it.
