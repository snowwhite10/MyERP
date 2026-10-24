# CO — Controlling requirements

## WU-CO-000d design baseline (2026-10-24)

CO owns management dimensions, cost policies, approved budgets and analytical snapshots. FI owns Posted journals and accounting periods; INV owns Posted movements and provisional valuation; PP owns production quantities. CO reads these only through owner Application contracts. This unit is design only: it creates no runtime feature, database table, endpoint, page or product acceptance.

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F41110.1.1 | 1.0 | System should enable user to view cost centers and their effective hierarchy. | Developer | Company-scoped list includes status, effective dates, parent and historical code; scope and cutoff are visible. |
| REQ_F41110.1.2 | 1.0 | System should allow user to create and edit cost centers. | Developer | Company code is permanently unique including soft-deleted rows; parent is active, same company and acyclic; stale version returns 409. |
| REQ_F41110.1.3 | 1.0 | System should allow user to deactivate unused cost centers. | Developer | Referenced history remains readable; active budget or allocation policy blocks deactivation with reason. |
| REQ_F41110.2.1 | 1.0 | System should enable user to view profit centers and their effective hierarchy. | Developer | Company-scoped hierarchy and historical assignments are distinct from FI legal entity and GL account. |
| REQ_F41110.2.2 | 1.0 | System should allow user to create and edit profit centers. | Developer | Permanent company code, valid parent/date range, cycle prevention and version conflict apply. |
| REQ_F41110.2.3 | 1.0 | System should allow user to deactivate unused profit centers. | Developer | Saved assignments and actual history are retained; active policy or budget prevents deactivation. |
| REQ_F41110.3.1 | 1.0 | System should enable user to view product cost calculations. | Developer | Each run shows item, company, period, costing policy/version, source cutoffs, material/labour/overhead components, currency, completeness and variance. |
| REQ_F41110.3.2 | 1.0 | System should allow user to calculate provisional product cost. | Developer | Only committed owner sources are included once; missing/unpriced INV cost or absent PP actuals is named unavailable, never silently zero. |
| REQ_F41110.3.3 | 1.0 | System should allow user to approve a product cost baseline. | Developer | SYS approval freezes the run and policy; approval does not alter INV valuation or FI Posted journals. Correction uses a new version. |
| REQ_F41110.4.1 | 1.0 | System should allow user to create and revise cost and profit center budgets. | Developer | Company, fiscal period, dimension, account/category, currency and amount are validated; approval freezes one effective version per key. |
| REQ_F41110.4.2 | 1.0 | System should enable user to compare approved budget with actual results. | Developer | Actuals come from authorised FI Posted source at a fixed committed cutoff; budget version and FI cutoff remain visible across pages/export. |
| REQ_F41110.4.3 | 1.0 | System should enable user to drill into budget variances. | Developer | Sum of authorised detail equals shown aggregate; missing mapping or FI source returns incomplete/unavailable with reason, not zero actual. |
| REQ_F41110.4.4 | 1.0 | System should allow user to export a frozen budget comparison. | Developer | Export uses the same budget version, FI source set, filters, scope and cutoff as the displayed comparison; permission is checked again at download. |

Requirement IDs are new SRS module 10 IDs. `REQ_DS43` is allocated with real migrations and `REQ_IO_A10` with implemented screens. Traceability to rules and cases is in `test-plan.md`.
