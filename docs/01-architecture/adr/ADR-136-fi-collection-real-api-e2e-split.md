# ADR-136: Real-API E2E for collections split by AR source availability

Date: 2026-11-15. Status: Accepted (WU-FI-005c3b1).

## Context

WU-FI-005c3b planned one real-API E2E for the whole collection cycle: create a receipt, submit, post, then settle it against a posted receivable and check both balances on screen and in SQL. A posted receivable (`ar_posted_receivable`) is only written by FI's sales posting services (`ISalesInvoicePostingService` and the credit posting), which read the SD invoice through `IFinanceSalesInvoiceSource`. SD has not implemented that port yet (WU-SD-007a), so the running host has no way to create a receivable. Writing one with SQL or a seed that bypasses FI posting would break MASTER_PROMPT section 18 (no direct writes, no bypassing the ledger rules). The Development seed also has no fund account and no customer of its own.

## Decision

- **Split.** 005c3b1 (this unit): the receipt half and the settlement form up to choosing lines. 005c3b2: settling a real posted receivable (saved, submitted, approved; AR open and receipt unapplied both reduced, screen = SQL). 005c3b2 depends on an AR source: WU-SD-007a, or a future FI opening-receivable feature if that is added to the backlog first. The parent 005c3b is not counted.
- **Arrange through public APIs.** The spec creates its own customer (MD API, submitted while MD.CUSTOMER has no active flow) and a TWD bank fund account on ledger 1102 (MD API) with unique codes, so the run's totals are isolated from older data. No SQL writes; SQL is only read back.
- **Both approval paths.** FI-COLL-REAL-002 runs with no active AR.COLLECTION flow (submit posts at once); FI-COLL-REAL-003 sets a one-step flow approved by `manager` and approves through the SYS API. The flow is put back as it was in `afterAll` (inactive when the spec created it).
- **Settlement form check.** FI-COLL-REAL-004 picks the posted receipt in the settlement form and opens the receivable picker; the fresh customer has no open receivable in SQL and the picker shows the empty state.
- **No demo fund account seed for now.** Users create fund accounts on the MD screen; a seed can be added later if QA needs one.

## Consequences

- Receipt create, number rule (ADR-135), zero-step posting, SYS approval posting, unapplied balance and GL lines (Dr 1102 / Cr unapplied cash) are proven against SQL Server.
- Settlement against a real receivable stays open until an AR source exists; the backlog row records the dependency.
- Each run leaves one customer, one fund account and two posted receipts in the Development database (posted documents cannot be deleted), like the other real-API specs.
