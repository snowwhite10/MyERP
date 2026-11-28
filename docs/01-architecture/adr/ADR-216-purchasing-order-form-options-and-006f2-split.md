# ADR-216 — Purchase order form options and WU-PUR-006f2 split

Date: 2026-11-28T10:57:25+08:00. Status: Accepted for WU-PUR-006f2a.

## Context

WU-PUR-006f2 was planned as a front-end-only unit: the 介面 4.2 create form calling the existing `POST /api/v1/pur/purchase-orders`. When the round started, the option reads that the 006d design lists were not implemented: `GET /create-options` (suppliers, items, currencies, tax rates) and `GET /requisition-options`. Without them, a user who has only `PUR.ORDER.CREATE` cannot pick a supplier, item, tax rate or requisition source without also holding MD.VIEW, REQUISITION.VIEW or RFQ permissions. That breaks the design rule that each form reads its options under its own action permission. Adding the reads, their real SQL and HTTP tests, and the full form in one round would exceed the 90-minute boundary.

## Decision

- Split WU-PUR-006f2 into **f2a** (option APIs plus the front-end client) and **f2b** (the 4.2 form page). The parent is not counted.
- Replace the single `/create-options` in the design with four small reads. They follow the existing RFQ / quotation patterns and reuse the MD public contracts. All four use `RequireAnyPermission(PUR.ORDER.CREATE, PUR.ORDER.EDIT)`, so 4.3 (006f3) can reuse them:
  - `GET /supplier-options`: `ISupplierSelectionOptions.ApprovedAsync` returns approved, active suppliers.
  - `GET /item-options`: `IInventorySelectionOptions.ItemsAsync` returns active items.
  - `GET /create-context?businessDate=`: `IQuotationSelectionOptions.GetAsync` returns active currencies and the non-output tax rates valid on that date. These are the same rules the draft service checks on save. The date is required, so a missing date, an invalid date or `0001-01-01` returns 400. The client re-reads the context when the business date changes.
  - `GET /requisition-options?itemId=&keyword=`: returns current-company, non-deleted, Approved requisition lines with `quantity > ordered_quantity`, together with the original, ordered and remaining quantities. Paging is stable by document number, line number and ID. This follows the RFQ source-option rule (company-wide, no SYS data scope on requisitions). SUBMIT still re-checks the quantities under lock (ADR-214).
- One paged-and-filtered option set per endpoint is simpler to page and test than one combined payload with four different paging needs.

## Consequences

The form can be completed with only ORDER.CREATE or EDIT. The option endpoints are read-only and add no tables or migrations. The available quantity shown on the form is informational. Shortages are still reported at SUBMIT, per order line (006f1).
