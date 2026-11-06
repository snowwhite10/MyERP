# ADR-103 — FI GL posting SQL port: per-attempt scope and mapping-version source

Date: 2026-11-06. Status: Accepted (WU-FI-004a3x1b2b1).

## Context

`GlPostingAttemptRunner` (004a3x1a) defines the order of a posting attempt and talks to SQL through `IGlPostingTransactionPort`／`IGlPostingAttemptSession`. To build the port, two questions had to be answered:

1. **Where does the attempt's DbContext come from?** A failed attempt may leave tracked entities or a broken connection behind. `GlPostingWriter` detaches its rows and rolls back. But the MD contracts (`IChartOfAccounts`, `ICurrencyLookup`) resolve the scoped `MyErpDbContext` and must read inside the same transaction.
2. **Where does "the current FI mapping version" come from?** `GlPostingAuthority.CurrentMappingVersion` must be read under the locks. No FI mapping table exists yet. BR-FI-052／060／137 describe a different versioned mapping for each source (income category, expense category, adjustment policy).

The SQL memory gate returned DEFER (1.847／1.821／1.825 GiB), so the port could be written and checked offline only.

## Decision

- **One DI scope per attempt and per committed lookup.** `GlPostingTransactionPort` (scoped, FI Infrastructure) creates an `AsyncServiceScope` in `BeginAsync`. It resolves `MyErpDbContext`, `GlPostingWriter`, `ICurrencyLookup`, `IChartOfAccounts` and the mapping-version sources from that scope, so they share one connection and one ReadCommitted transaction. Disposing the session rolls back if it has not committed, then disposes the transaction and the scope. `LookupCommittedAsync` uses its own new scope and transaction.
- **Company scope before SQL.** If the request company is not positive, is not the scope's `CurrentCompanyId`, or does not match the source company, `BeginAsync` disposes the scope and returns null (the runner maps null to 500 `INVALID_DECISION`). The lookup applies the same rule. No transaction is opened.
- **Connection loss.** The runner calls `IsConnectionLost` before it disposes the session. The port keeps only a state reader for the latest session (not the session, so it never owns a disposable). It passes the live `ConnectionState` to `GlPostingConnectionRules`. A failed `BeginAsync` clears the reader first.
- **Mapping version.** Add `IGlPostingMappingVersionSource { SourceModule, SourceType, ReadCurrentVersionAsync(companyId) }` to FI Application. Each FI source adapter that builds drafts from a versioned mapping registers exactly one scoped implementation. `GlPostingMappingVersionRules.Select` picks the single implementation whose module and type match the source (ordinal comparison). If none or more than one matches, that is a configuration fault: `ReadAuthorityAsync` returns null (500, nothing written), and the decision rules never see an invented version. The version is read after both locks, inside the transaction.
- **Authority read order (inside the transaction, after `LockAsync`):** mapping source selection → committed `gl_source_key` LEFT JOIN `gl_journal` → MD base currency → company periods → mapping version → MD accounts (`AccountCodesToRead`, one `FindAsync` each) → `GlPostingAuthorityRules.Assemble`.
- **DI.** `FinanceModule` registers `IGlPostingTransactionPort` → `GlPostingTransactionPort` and `IGlPostingService` → `GlPostingService` (both scoped). No mapping source is registered yet.

## Consequences

- Until a source adapter registers its mapping source, `IGlPostingService` returns 500 `INVALID_DECISION` without writing, for any well-formed in-scope request. This is intended: no adapter exists yet, and none can post without a versioned mapping.
- The posting runs in its own transaction, not the caller's. Source adapters that must commit their own state together with the journal (BR-FI-137 "in the caller transaction") still need a caller-transaction variant. This is decided when the first adapter (FI-002a／003a) is built. The `IGlPostingAttemptSession` seam allows it without changing the runner.
- Real SQL behaviour is not yet verified: locks, authority reads, rollback on dispose, lookup, KILL-based connection loss, replay, conflict, closed period, cross-company and concurrency. That is WU-FI-004a3x1b2b2. Its tests register a test `IGlPostingMappingVersionSource`.
