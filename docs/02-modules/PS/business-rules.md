# PS — Project management business rules

## WU-PS-000d baseline

| Rule | Design invariant |
|---|---|
| BR-PS-001 | PS owns project plan, budget, work evidence, reservations, milestones and billing proposal. SYS owns approval, HR owns employee identity, SD owns invoice, FI owns posted actuals and receivables. Cross-owner calls use Application contracts, never tables. |
| BR-PS-002 | Project code is unique within company including soft-deleted history; closing prohibits new work, reservation and billing proposal but retains audit and financial references. |
| BR-PS-003 | A budget has immutable submitted and approved versions. Approval is independent of author; only one effective approved version applies at an as-of date, with superseded versions retained. |
| BR-PS-004 | Budget and actual comparison declares currency, exchange-rate policy, period and FI posted cutoff. Missing FI source is unavailable, never zero actual. PS cannot write GL or CO budget facts. |
| BR-PS-005 | Work logs store original timezone, local date, duration, source and correction history. Overlap for the same employee is rejected; a correction after approval or billing appends reversal/adjustment. |
| BR-PS-006 | Billable time requires approved work, an eligible contract/rate snapshot and a frozen cutoff. Approval alone is not invoice issuance. |
| BR-PS-007 | Resource reservation checks HR identity/status via HR Application and a versioned capacity calendar. Concurrent claims of last capacity have one winner; HR payroll or employment records are never changed by reservation. |
| BR-PS-008 | Milestone completion requires evidence and independent acceptance. Reopening preserves accepted and billed history and creates an explicit adjustment path. |
| BR-PS-009 | Billing proposal freezes eligible source IDs, source versions, money precision and contract/tax references; duplicate sources or changed replay payload yield 409. Draft, submitted, invoice-confirmed and FI-posted states remain separate. |
| BR-PS-010 | SD invoice and FI receivable confirmation use permanent source keys and owner status lookup. Timeout is PendingOwner until confirmed; PS never claims Posted based on HTTP dispatch. |
| BR-PS-011 | Batch allocation and billing return changed, skipped, failed and pending counts with per-row reasons. Zero change and all skipped are warnings, never success. |
| BR-PS-012 | Every read and command checks action permission, company and data scope; other-company IDs return 404 without counts. Approval, invoice submission, export and download have separate rights. |
| BR-PS-013 | Lists, utilisation, actuals and billing previews use a stable committed membership/cutoff across pages and export; changed owner source produces explicit refresh or version conflict. |
| BR-PS-014 | Invalid shape returns 400, duplicate/version conflict 409, invalid business state 422; 500 includes traceId. Network timeout gives unknown outcome and source-key status lookup, not speculative success. |
| BR-PS-015 | Audit records actor, company, action, version, source key, reason and confirmed outcome. Sensitive employee rates and contract prices are masked in logs and restricted exports. |

Implementation ADRs must settle capacity calendar, rate and tax ownership, currency conversion, SD invoice and FI posting contracts, and compensation for confirmed downstream effects before executable acceptance.
