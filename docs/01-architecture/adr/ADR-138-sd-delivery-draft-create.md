# ADR-138: Sales delivery Draft creation — split, isolation and tracked items

Date: 2026-11-16. Status: Accepted (WU-SD-004a2b10).

## Context

WU-SD-004a2b (delivery Draft CRUD, submit, real SQL/HTTP and race acceptance) is far larger than one round. The schema (`Sales_DeliveryDraft`) and pure rules (004a2b1–9) already exist. Order approval (WU-SD-003b) is not implemented yet, so no product path produces an Approved order; tests must move a seeded Draft order to Approved with SQL, which the order state trigger (51005) allows.

A first implementation read the source order and the Posted/Pending delivery totals in a Serializable transaction. A two-connection test showed that a second create for another order or company then waited on the first transaction's uncommitted delivery insert (1222 lock timeout): the balance join scanned `sal_delivery`, and two such creates can deadlock on each other's range locks (compare ADR-137).

Lot/serial selections (BR-SD-023) have no saved table yet.

## Decision

1. Split 004a2b: **004a2b10** Draft create service (this ADR), **004a2b11** Draft edit/delete with version, **004a2b12** authorised HTTP routes, list/detail query and OpenAPI, **004a2b13** submit with SYS, ordered source-line locks, full recheck and two-connection race. 004a2b stays the parent and closes when all four pass; 004b (INV posting) follows.
2. `SalesDeliveryCreateService` runs in **Read Committed**. It first takes `UPDLOCK, HOLDLOCK, FORCESEEK` on the source `sal_order` primary key, so creates for the same order (and later order cancellation/edit) serialize on that row. Posted/Pending totals are read by raw SQL with `FORCESEEK` on `sal_delivery (company_id, order_id, status)` and the line index, so other orders' uncommitted rows are never touched.
3. The create balance check (ordered − Posted − active Pending, `SalesDeliveryBalance.Check`) is advisory: a Draft reserves nothing. Submit (004a2b13) locks source lines in ascending ID order and rechecks inside its own transaction; only that check protects the reservation.
4. Each distinct item/warehouse/location goes through `IInventoryReferenceGuard.RunWithCodesAsync` (MD shared lock until commit). Item identity is copied from the saved order line, never from the client.
5. Until lot/serial selections are stored, an item whose MD tracking mode is not `None` is refused with 422 `SAL.DELIVERY.TRACKING_UNSUPPORTED` (extension `lineNo`). Silently saving a tracked line without tracking would only fail later at INV posting.
6. A line whose order-line ID is not on the source order gives 422 `SAL.DELIVERY.ORDER_LINE_INVALID` with `lineNo`; a non-Approved order gives 422 `SAL.DELIVERY.ORDER_NOT_APPROVED`; an order outside the company or the `SAL.DELIVERY.CREATE` data scope gives 404 `SAL.ORDER.NOT_FOUND`.

## Consequences

- `SalesDeliveryCreateTransactionTests` (5 real-SQL tests) cover SD-DEL-001/006/007/008/011/013/014/015 for create. The race test is red under the Serializable design and green now.
- SYS `NumberGenerator` blocks a create in another company while a first transaction holds its sequence row (observed in the same test). That is SYS-owned; recorded as backlog P2 `WU-SYS-NUMSEQ-LOCK`. The SD race test isolates SD locking with a fixed-number generator for its probe calls.
- Tracked-item deliveries are blocked until a tracking-selection slice adds storage; recorded in backlog as part of 004b's prerequisites (`WU-SD-004a2b14`).
