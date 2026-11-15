# ADR-137: Sales order line guard triggers seek the parent order

Date: 2026-11-16. Status: Accepted (WU-SD-003a retry).

## Context

Sales order commands (create, edit, delete, quotation conversion) run in Serializable transactions and lock their own header with `UPDLOCK, HOLDLOCK` first. Two `sal_order_line` triggers also read the parent header:

- `tr_sal_order_line_state_guard` (51004: only active Draft lines may change);
- `tr_sal_order_line_source_consistency` (51003: order and line quotation sources must agree).

Both joined `dbo.sal_order` on `id` and `company_id` without a hint. The header table is small, so SQL Server chose a clustered index scan. Under Serializable the scan takes range-shared locks on every other order's key. When the integration tests ran at two threads, an edit of one order and the insert of another order's lines in a different company locked each other's header keys. SQL Server then reported deadlock 1205, and `PUT /api/v1/sal/orders/{id}` returned 500 instead of 200 or 409. The `system_health` deadlock graph showed the wait in `tr_sal_order_line_state_guard` on `pk_sal_order` (RangeS-S against X / RangeX-X).

## Decision

- Migration `Sales_OrderLineGuardSeek` uses `CREATE OR ALTER` on both triggers and adds `WITH (FORCESEEK)` to the `sal_order` join, so each trigger seeks its parent row by primary key. The error numbers and the rules do not change. `Down` puts back the old definitions.
- New trigger joins to a large or growing header table from a Serializable write path must use a seek hint. They also need a lock-timeout test that holds a lock on an unrelated row.
- Range locks between neighbouring keys of the line indexes (for example, deleting order B's lines while order A inserts a line next to them in the index) are still possible. That is normal Serializable blocking with a single wait order, not a deadlock. This decision does not change it.

## Consequences

- Regression test `SalesOrderSchemaTests.LineGuardTriggersDoNotLockOtherOrderHeaders` (SD-ORDER-DEADLOCK-001). Another transaction holds X on an unrelated order header, and a Serializable line replacement must finish within a 2-second lock timeout. With the old triggers the test fails (1222 lock timeout). With the new ones it passes.
- The SalesOrder integration group passes at two threads, so the WU-SD-003a 500/deadlock is gone.
