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

## WU-SCM-001d forecast source acceptance (2026-10-24)

This design refines REQ_F41116.1.1–1.3 without changing their IDs or claiming product delivery. Each source result must name owner, company, item, site, period, canonical unit, committed cursor, row keys/versions and availability reason. A caller may request a fixed owner cursor; an owner unable to serve exactly that committed membership returns `Unavailable`, not a fresh current value. SD confirmed order demand is the sole input to the first forecast method; PP released demand and INV posted movements are separately labelled comparison measures, so one physical demand is never added twice. A version cannot be approved while its required SD input or conversion evidence is unavailable. The UI must show which optional comparison measures are unavailable and retain the same source manifest for later pages and exports.

Acceptance proof for 001a: deterministic results at a saved cursor across two reads; corrected/voided rows retain original published result; mixed company/site or ambiguous units are rejected; an unavailable owner cannot create a zero-valued published forecast. Proposed DTOs, current implementation gaps and scenario IDs are in `api.md` and `test-plan.md`.

## WU-SCM-001e owner prerequisite acceptance (2026-10-24)

REQ_F41116.1.1–1.3 retain Version 1.0. Each owner must prove a replayable committed membership at its own cursor before SCM can consume it. SD's three complete historical buckets and MD's frozen conversion evidence are required for publication; INV and PP remain optional comparison overlays with explicit availability. An expired cursor prevents a new run, while a published SCM version remains readable from its saved manifest. Owner corrections after capture appear only in a new version. Acceptance cases SCM-OWN-001–016 and prerequisite work units are recorded in `test-plan.md` and `backlog.md`. This is a contract design, not an implemented reader or product acceptance.
