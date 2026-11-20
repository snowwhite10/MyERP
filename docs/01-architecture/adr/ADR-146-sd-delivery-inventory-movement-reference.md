# ADR-146: Store the INV movement ID on each posted delivery line

Date: 2026-11-21. Status: Accepted (WU-SD-004b2).

## Context

BR-SD-024/025 and SD-DEL-020 require a Posted delivery to keep the INV movement IDs returned by `PostSalesOutboundAsync`. ADR-144 posted deliveries through INV but left SD without a stored reference; the INV ledger key (`SALES_OUTBOUND`, delivery ID, line number) was the only link. The earlier hand-off note suggested a new table (REQ_DS43.204).

INV's `StockPostingService` creates exactly one Out movement per outbound line and returns the IDs ordered by source line number (one direction only for `SALES_OUTBOUND`). `SalesDeliveryPosting` already refuses a result whose count differs from the line count (409 `SAL.DELIVERY.POSTING_CONFLICT`).

## Decision

1. Add nullable `inv_movement_id bigint` to `sal_delivery_line` (REQ_DS43.171, no new table and no new REQ_DS43 ID; REQ_DS43.204 stays unallocated). A separate table would only repeat the one-to-one line → movement mapping.
2. `CHECK ([inv_movement_id] IS NULL OR [inv_movement_id] > 0)` and a filtered unique index `(company_id, inv_movement_id) WHERE inv_movement_id IS NOT NULL`, so one INV movement can never be claimed by two delivery lines. It is a scalar reference with no FK to INV (module boundary, MASTER_PROMPT section 5).
3. `SalesDeliveryPosting.PostAsync` assigns the returned IDs to the saved lines ordered by line number in the same transaction that sets Posted. Draft/Pending/Rejected lines keep null. Create/edit inputs have no field for it, so a client cannot set it.
4. `GET /api/v1/sal/deliveries/{id}` and command responses return `lines[].invMovementId`.
5. No backfill: deliveries posted between ADR-144 and this migration (development data only) keep null and remain traceable through the INV ledger key. A migration must not read INV tables.
6. SYS final approval and SD withdraw take locks in opposite order (ADR-144 consequence). The real-HTTP race test (SD-DEL-HTTP-006) showed one 200 and one 409/422 in four runs, with no deadlock victim; no retry was added. If a 1205 victim is ever observed, add a single retry in the SYS decision endpoint rather than in SD.

## Consequences

- SD-DEL-020's "INV movement IDs remain linked" is closed for new postings.
- The future return flow (BR-SD-045) can read original outbound movement IDs from the Posted delivery lines.
