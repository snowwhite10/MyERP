# PS — Project management requirements

## WU-PS-000d design baseline (2026-10-24)

PS owns project plans, approved budget versions, work logs, resource reservations, milestones and billable evidence. HR owns employee employment and availability policy, FI owns posted costs and receivables, SD owns customer contracts and invoices, SYS owns approvals and access. This unit delivers design only; no migration, endpoint, screen or product acceptance.

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F41115.1.1 | 1.0 | System should allow user to create, revise and close a project. | Developer | Company, unique code including deleted history, customer reference, currency, dates, owner and version are validated; closing preserves history and blocks new commitments. |
| REQ_F41115.1.2 | 1.0 | System should enable user to view a project portfolio and historical project state. | Developer | Company and data scope filter all rows and totals; an as-of date and committed cutoff keep pages and exports consistent. |
| REQ_F41115.2.1 | 1.0 | System should allow user to draft and submit a project budget version. | Developer | Lines identify cost/revenue category, currency, amount and effective period; totals reconcile, and submission freezes the version for independent approval. |
| REQ_F41115.2.2 | 1.0 | System should enable user to approve or reject a project budget and compare it with actuals. | Developer | SYS approval is confirmed before activation; actual cost and revenue come only from FI posted owner results at a named cutoff, with unavailable sources labelled. |
| REQ_F41115.3.1 | 1.0 | System should allow user to record and correct project work time. | Developer | Employee, project/task, local work date, timezone, start/end or duration, rate-policy version and evidence are retained; overlapping time and negative duration are rejected. |
| REQ_F41115.3.2 | 1.0 | System should enable user to approve work time and freeze a billing cutoff. | Developer | Author and approver differ; corrections append a reversal or adjustment, and the frozen eligible set cannot silently change after invoicing. |
| REQ_F41115.4.1 | 1.0 | System should allow user to reserve resources against project demand. | Developer | HR employee identity/status is checked through its owner contract; capacity, effective dates and competing reservations are checked atomically, with row reasons. |
| REQ_F41115.4.2 | 1.0 | System should enable user to view allocation and release reservations. | Developer | Released and historical reservations remain traceable; utilisation uses a named capacity calendar and committed cutoff, not an inferred payroll balance. |
| REQ_F41115.5.1 | 1.0 | System should allow user to define and approve project milestones. | Developer | Milestone sequence, due date, acceptance criteria, evidence and approval version are retained; completion requires independent confirmation. |
| REQ_F41115.5.2 | 1.0 | System should enable user to view milestone progress and reopen a milestone with reason. | Developer | Reopening appends a new state event; accepted or billed evidence remains immutable, and a billing adjustment is required where applicable. |
| REQ_F41115.6.1 | 1.0 | System should allow user to prepare project billing from approved time and accepted milestones. | Developer | Frozen source IDs, versions, amounts, currency, tax-policy reference and contract terms are shown; each source can be claimed only once for a billing version. |
| REQ_F41115.6.2 | 1.0 | System should enable user to submit project billing and trace its invoice and receivable status. | Developer | SD confirms the invoice and FI confirms the receivable through owner contracts; pending, rejected and unknown outcomes remain distinct from billed or posted, and same-key retries are idempotent. |

`REQ_DS43` is allocated with actual PS migrations; `REQ_IO_A15` with implemented screens. Contract rates, tax and accounting mapping require owner agreement in implementation ADRs. No PS financial fact is asserted as posted by this baseline.
