# QM — Quality management requirements

## WU-QM-000d design baseline (2026-10-24)

QM owns inspection plans, observations, decisions and nonconformance cases. PUR owns purchase receipts and supplier returns; PP owns work orders and operation completion; INV owns all posted stock movement and quarantine availability. QM consumes their Application contracts and never writes their tables. This unit delivers design only, with no product endpoint, migration, page or acceptance.

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F41111.1.1 | 1.0 | System should allow user to define and revise incoming inspection plans. | Developer | Company, item/category, effective interval, sample rule, characteristic, unit, limits and plan version are saved; overlapping active plans for one selection key conflict. |
| REQ_F41111.1.2 | 1.0 | System should enable user to view incoming inspection lots linked to purchase receipts. | Developer | Lot shows immutable PUR source line/version, received quantity, inspected quantity, accepted, rejected and pending quantity in the source unit. |
| REQ_F41111.1.3 | 1.0 | System should allow user to record incoming samples and approve or reject an inspection lot. | Developer | Decimal observations are checked against frozen plan limits; independent SYS final approval fixes the disposition, with no decision beyond source quantity or duplicate source decision. |
| REQ_F41111.1.4 | 1.0 | System should allow user to release accepted incoming quantity for receipt posting. | Developer | A permanent decision key and quantities are sent once to the PUR owner; only PUR invokes the INV posting contract. Unavailable owner returns an explicit pending/unavailable outcome. |
| REQ_F41111.2.1 | 1.0 | System should allow user to define and revise in-process inspection checkpoints. | Developer | Company, PP operation/version, item, effective period, sample and pass rules are versioned; a released work order retains its selected checkpoint version. |
| REQ_F41111.2.2 | 1.0 | System should enable user to view inspections for a work order operation. | Developer | PP source identity, operation state, measured, accepted, rejected and pending quantities are visible with owner cutoff; absent PP source is unavailable rather than zero. |
| REQ_F41111.2.3 | 1.0 | System should allow user to record in-process observations and decide acceptance. | Developer | Accepted quantity cannot exceed PP reported completed quantity; failed checkpoint blocks PP acceptance until a new authorised decision or rework result. |
| REQ_F41111.3.1 | 1.0 | System should allow user to create a nonconformance case from a failed inspection. | Developer | Case links one immutable inspection decision/source line, reason, severity, affected quantity, owner and evidence; replay returns the same case. |
| REQ_F41111.3.2 | 1.0 | System should allow user to approve a nonconformance disposition. | Developer | Rework, supplier return, scrap, concession and no-action have distinct permission and evidence rules; disposition alone does not post stock or alter PP/PUR documents. |
| REQ_F41111.3.3 | 1.0 | System should enable user to trace nonconformance closure. | Developer | Closure requires confirmed owner outcome for any movement/rework, plus complete affected quantity reconciliation; rejected/pending cases remain visible and auditable. |
| REQ_F41111.4.1 | 1.0 | System should enable user to view quality results and nonconformance trends. | Developer | Counts, quantities, defect rates and units use a fixed committed QM source set and named PUR/PP/INV cutoffs; missing owner sources are marked incomplete. |
| REQ_F41111.4.2 | 1.0 | System should allow user to export an authorised quality report. | Developer | Export uses the displayed frozen filters, company/scope, source IDs and cutoffs; download rechecks permission and includes completeness and reasons. |

`REQ_DS43` is reserved for real QM migrations; `REQ_IO_A11` is assigned when screens are built. Rules and scenario IDs in `test-plan.md` provide traceability.
