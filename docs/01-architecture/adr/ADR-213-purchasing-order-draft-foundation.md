# ADR-213 — Purchase order draft foundation and requisition consumption counter

Date: 2026-11-28T03:14:26+08:00. Status: Accepted for the WU-PUR-006a schema slice.

## Context

The converted RFQ order and purchase invoice already use `pur_purchase_order` and `pur_purchase_order_line`. Those tables are also the target for manual purchase orders. The original WU-PUR-006d design proposed a new `converted_quantity` on requisition lines, but `pur_requisition_line.ordered_quantity` already stores the committed ordered amount; RFQ source options and validation calculate remaining quantity from it. Two counters would create conflicting remaining quantities.

## Decision

- Manual and converted orders continue to share the existing order tables and EF entities. New nullable remark and original-currency amounts preserve existing invoice source rows. A non-null `requisition_consumption_active` flag defaults to false for existing and new draft rows. SQL permits true only for non-deleted PendingApproval or Approved orders.
- `ordered_quantity` is the sole committed requisition consumption counter. WU-PUR-006b must update it under a serializable transaction and ordered update locks when submitting, and release it once on rejection or cancellation while clearing the order flag. Draft creation and editing never update it.
- Add a company-scoped order-line FK to the existing requisition line and a header status/date index. Keep manual lines nullable for the requisition source and original-currency amounts so older invoice fixtures remain valid. Order writers must populate complete original and base amount snapshots.
- This schema slice does not expose a partial manual-order API. WU-PUR-006a remains open for MD validation, transactional CREATE/EDIT, authorized READ, and HTTP tests. WU-PUR-006b retains submission and consumption logic.

## Consequences

`ordered_quantity` and the order consumption flag represent one state transition. The schema protects referential identity and amount arithmetic; transaction services still must enforce approval, material identity, date-valid MD references, money rounding and concurrency. Down removes only new columns, index, check constraints and FK, retaining existing order, requisition and invoice data.
