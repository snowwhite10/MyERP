# SCM — Supply chain requirements

## WU-SCM-000d design baseline (2026-10-24)

SCM owns forecast versions, transport evidence and supplier collaboration proposals. SD owns confirmed sales demand, PP owns released production demand, INV owns stock movements, PUR owns purchase orders and receipts, MD owns supplier identity, and SYS owns users, access and approval. This unit delivers design only: no migration, endpoint, screen or product acceptance.

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F41116.1.1 | 1.0 | System should allow user to create a versioned demand forecast from approved source snapshots. | Developer | Company, item, location, period, unit, horizon, method version and owner source cutoffs are explicit; unavailable sources are identified rather than counted as zero. |
| REQ_F41116.1.2 | 1.0 | System should enable user to review, adjust and approve a forecast version. | Developer | Baseline and manual overrides retain reasons and authors; independent approval freezes the version and leaves prior versions auditable. |
| REQ_F41116.1.3 | 1.0 | System should enable user to compare forecast, confirmed demand and actual movement. | Developer | Each measure names its SD, PP or INV owner cutoff, unit and availability; pages and exports retain the same committed source membership. |
| REQ_F41116.2.1 | 1.0 | System should allow user to plan an inbound or outbound transport consignment. | Developer | A consignment references an authorised PUR receipt or SD shipment request, route, carrier, packages and delivery window; no inventory movement is implied by planning. |
| REQ_F41116.2.2 | 1.0 | System should enable user to record carrier events, delivery proof and exceptions. | Developer | Event time, received time, carrier event key, quantity, evidence and reason are retained; duplicate or out-of-order events do not erase history. |
| REQ_F41116.2.3 | 1.0 | System should enable user to reconcile delivery with purchase, sales and inventory owner outcomes. | Developer | Transport delivered, PUR received, SD shipped and INV posted remain separate confirmed statuses; discrepancies are visible and correction uses owner contracts. |
| REQ_F41116.3.1 | 1.0 | System should allow user to invite and revoke a supplier collaborator for a limited company and purchase order scope. | Developer | Supplier identity is verified through MD, account and permission through SYS; invitation expiry, revocation and per-order scope are enforced at every read and command. |
| REQ_F41116.3.2 | 1.0 | System should enable a supplier collaborator to view authorised purchase order details and submit a proposal. | Developer | Supplier sees only assigned orders and approved fields; proposed delivery, quantity or document changes are immutable submissions and do not alter PUR orders. |
| REQ_F41116.3.3 | 1.0 | System should enable user to review supplier proposals and publish an outcome. | Developer | PUR confirms any purchase order change through its Application contract; accepted, rejected, pending and unknown owner outcomes are distinct, with reason and audit. |

`REQ_DS43` IDs are allocated with real SCM migrations and `REQ_IO_A16` with implemented screens. Forecast method and horizon defaults, carrier integration format, evidence retention and supplier identity proof need implementation ADRs. No planned source contract is claimed to exist here.
