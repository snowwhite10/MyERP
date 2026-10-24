# ADR-071: Atomic financial posting for source documents

Status: Accepted (design; implementation pending)

## Context

The architecture description originally allowed SD/PUR to complete a source document and have FI post later through an event. The current accepted SD invoice and PUR purchase-invoice requirements require a receivable/payable and the source Posted state to commit together. A deferred posting could leave a Posted commercial invoice without its matching subledger entry and would release SD credit commitment before AR exposure exists.

## Decision

For a source action that claims Posted financial status, the owning module calls FI's public posting contract inside the same SQL transaction. FI owns `gl_`, `ar_` and `ap_` records and source-key idempotency; source modules retain their commercial documents and never write FI tables. FI may emit post-commit notifications after the atomic commit. If FI is unavailable, the source action fails with a named reason and remains unposted. Existing nonfinancial workflows may still use post-commit events for notifications or projections.

The FI customer credit-key lock is shared by FI credit/AR writes and SD order decisions. A caller acquires it before reading both FI exposure and SD commitments. Source posting order and replay guarantees must be proved by two-connection real SQL tests before implementation is accepted.

## Consequences

FI Contracts can be referenced without referencing the FI implementation assembly. SD invoice and PUR purchase-invoice implementation must wait for their FI contracts and transactional tests. The earlier deferred-posting sentence in `architecture.md` is superseded for financial Posted actions. This ADR does not mark any posting contract or database table as implemented.
