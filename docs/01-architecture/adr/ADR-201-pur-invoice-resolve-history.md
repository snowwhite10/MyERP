# ADR-201: Purchase invoice RESOLVE route and decision history table

Date: 2026-11-26T22:13:48+08:00

Status: Accepted

## Context

WU-PUR-008a54 exposes RESOLVE for Disputed purchase invoices (BR-PUR-039a, BR-PUR-037f). The earlier design table in `api.md` sketched `POST /{id}/resolutions` with one `lineId` per call. A one-line-per-call route would leave the invoice half-decided between calls, while BR-PUR-039a requires exactly one decision for every differing line before the status changes. Decisions must be retained as history (operator, time, version) and the reservation release on rejection must be atomic with the status change.

## Decision

- Route: `POST /api/v1/pur/invoices/{id}/resolve?version=N` with body `{ lines: [{ lineNo, action, reason }] }`, one call per whole invoice, guarded by new permission `PUR.INVOICE.RESOLVE` with company data scope (same rule as CREATE/EDIT/MATCH).
- Inside the same Serializable transaction and supplier application lock as MATCH, the service rechecks the saved order/receipt identity and order price basis, recomputes differences (BR-PUR-038a), validates the decision set (BR-PUR-039a), then: accept-all → Matched and keeps the reservation after rechecking availability with this invoice's own reservation excluded; any Reject → Draft and releases exactly this invoice's reservation (`PurchaseInvoiceReservationPlan.ForResolve`).
- New table `pur_purchase_invoice_resolution` (REQ_DS43.212) stores one immutable row per decided line: invoice ID, invoice version the decision applied to, line number, Accept/Reject, trimmed reason (1–500), and NET/TAX/GROSS difference at decision time. Operator and time come from the standard `created_by/created_at` audit columns; rows are also captured by the SaveChanges audit interceptor. Uniqueness is `(company_id, invoice_id, invoice_version, line_no)`, so a re-MATCH and second RESOLVE add new history instead of overwriting.
- A rejected invoice returns to Draft (not Rejected): it can be edited and re-matched, and history keeps the rejected decision.

## Consequences

The `/resolutions` sketch is superseded. Unit price and tax rate differences are not stored separately (the line and order snapshots keep them); POST must still re-match and must not reuse stale decisions. GET of the history and the three-language UI remain in WU-PUR-008c.
