# ADR-215 — Purchase order DELETE scope and WU-PUR-006c split

Date: 2026-11-28T07:08:03+08:00. Status: Accepted for WU-PUR-006c1.

## Context

WU-PUR-006c was left with two actions: soft-deleting unsubmitted orders and cancelling approved orders. CANCEL needs its own atomic requisition release, an inbound-receipt reference check that connects to the WU-PUR-007 public contract, fault-injection tests and HTTP tests. Together the two actions exceed the 90-minute round boundary. BR-PUR-025 says "unsubmitted drafts" can be deleted. A Rejected order has already released its requisition quantity in the SYS rejection transaction (BR-PUR-024, `requisition_consumption_active` cleared). The open question was whether such an order may also be deleted without first being revised back to Draft.

## Decision

- Split WU-PUR-006c into **c1** DELETE (soft delete) and **c2** CANCEL. The parent is not counted.
- `DELETE /api/v1/pur/purchase-orders/{id}?version=` with a new, independent permission `PUR.ORDER.DELETE`. EDIT does not imply DELETE. Only **Draft** and **Rejected** orders that do not hold a consumption can be deleted. PendingApproval, Approved and Cancelled return 422 `PUR.ORDER.NOT_DELETABLE`; an approved order must use CANCEL (c2). The CHECK `ck_pur_order_consumption_state` already guarantees that a Draft or Rejected order never holds a consumption, and the service checks the flag again as defence in depth.
- Delete runs in a READ COMMITTED transaction that first takes `UPDLOCK, HOLDLOCK` on the order row. This is the same first lock as SUBMIT and the SYS decision handler, so a concurrent SUBMIT and DELETE serialize. The version check (409) comes after the action-scope lookup, which returns 404 for another company, out-of-scope or already-deleted orders.
- The soft delete only sets `is_deleted` and `deleted_at` (`IClock.LocalNow`). Lines, `pur_order_source` award links, the SYS history and the requisition `ordered_quantity` stay unchanged. The document number stays in the company-unique index, so it is never reused. The award link's unique `award_id` index keeps a deleted order's awarded lines from being converted again (BR-PUR-019).

## Consequences

Users can discard a rejected order without a meaningless revise-then-delete step, and no quantity is released twice because none is held. Whether to restore deleted orders is out of scope. c2 must add `PUR.ORDER.CANCEL` and the release logic, which can share the handler's ascending lock-and-release routine.
