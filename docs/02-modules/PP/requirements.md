# PP — Production requirements

## WU-PP-000d design baseline

This design maps the BlueWhale production baseline and extends it with routing, MPS, MRP and work orders. PP owns production intent, BOM/routing versions, planned demand, reservations and document state. MD owns item and unit identity; SYS owns approvals and document numbers; INV alone owns Posted stock movements. A PP approval or completion request is not itself a stock posting. This unit creates no runtime feature or product acceptance.

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4113.1.1 | 1.0 | System should enable user to view BOM and BOM groups. | Developer | Company-scoped active and historical versions show parent, components, base units, effective dates and approval state; groups do not change quantities. |
| REQ_F4113.1.2 | 1.0 | System should allow user to create BOM and BOM groups. | Developer | A draft has one parent, positive component quantities, valid MD items/units, no duplicate component line or direct/indirect cycle. |
| REQ_F4113.1.3 | 1.0 | System should allow user to edit draft BOM and BOM groups. | Developer | Edit uses version concurrency; a used or approved BOM version remains immutable. |
| REQ_F4113.1.4 | 1.0 | System should allow user to approve BOM. | Developer | SYS workflow approves one effective revision for a parent/site/date; overlapping approved revisions and cycles are rejected. |
| REQ_F4113.1.5 | 1.0 | System should allow user to reject BOM. | Developer | Rejection stores actor/reason and leaves prior approved revision intact. |
| REQ_F4113.1.6 | 1.0 | System should allow user to delete draft BOM and unused groups. | Developer | Referenced revisions cannot be deleted; historical identities remain visible. |
| REQ_F4113.1.7 | 1.0 | System should enable user to view BOM grouping. | Developer | Company-scoped groups show active members and historical references without changing quantities. |
| REQ_F4113.1.8 | 1.0 | System should allow user to create BOM grouping. | Developer | Code is unique per company including soft-deleted codes; name and code meet bounded validation. |
| REQ_F4113.1.9 | 1.0 | System should allow user to edit BOM grouping. | Developer | Stale version returns 409; reclassification cannot alter saved production snapshots. |
| REQ_F4113.1.10 | 1.0 | System should allow user to delete unused BOM grouping. | Developer | Referenced group is rejected with reason; history is preserved. |
| REQ_F4113.2.1 | 1.0 | System should enable user to view production plans. | Developer | Plan lines expose demand source, required date, quantity, selected BOM/routing revisions and state; a plan is not stock. |
| REQ_F4113.2.2 | 1.0 | System should allow user to create production plans. | Developer | One company/site, positive base-unit quantity, valid date and approved effective revisions; manual and MPS/MRP source keys cannot duplicate. |
| REQ_F4113.2.3 | 1.0 | System should allow user to edit draft production plans. | Developer | Version conflict returns 409; released/consumed plan is immutable and requires revision or cancellation flow. |
| REQ_F4113.2.4 | 1.0 | System should allow user to approve production plans. | Developer | SYS approval freezes demand/BOM/routing snapshots and creates no INV movement or GL entry. |
| REQ_F4113.2.5 | 1.0 | System should allow user to reject production plans. | Developer | Reason and audit recorded; no work order created. |
| REQ_F4113.2.6 | 1.0 | System should allow user to delete unreferenced draft production plans. | Developer | Approved/released plan retains history; cancellation is explicit. |
| REQ_F4113.3.1 | 1.0 | System should enable user to view material requisitions and confirmed issues. | Developer | Requested, reserved, INV Posted issued, reversed and open quantities are separate and reconcile by source line. |
| REQ_F4113.3.2 | 1.0 | System should allow user to create material requisitions for a work order. | Developer | Uses frozen work-order BOM and units; repeated source key returns same result without duplicate request. |
| REQ_F4113.3.3 | 1.0 | System should allow user to edit draft material requisitions. | Developer | Cannot change issued lines or exceed approved variance without explicit approval. |
| REQ_F4113.3.4 | 1.0 | System should allow user to approve material requisitions. | Developer | Approval authorises an INV issue request, but stock decreases only after INV confirms Posted movement. |
| REQ_F4113.3.5 | 1.0 | System should allow user to reject material requisitions. | Developer | Reject preserves work-order demand and records reason; no INV issue. |
| REQ_F4113.3.6 | 1.0 | System should allow user to delete unissued draft material requisitions. | Developer | Confirmed movement links cannot be removed or rewritten. |
| REQ_F4113.4.1 | 1.0 | System should enable user to view production inbound requests and confirmed receipts. | Developer | Completed, accepted, rejected, INV Posted received and reversed quantities are distinct. |
| REQ_F4113.4.2 | 1.0 | System should allow user to create production inbound requests. | Developer | Work-order output and accepted base quantity are positive and bounded by authorised overproduction policy. |
| REQ_F4113.4.3 | 1.0 | System should allow user to edit draft production inbound requests. | Developer | Confirmed receipt and quality disposition are immutable; stale version returns 409. |
| REQ_F4113.4.4 | 1.0 | System should allow user to approve production inbound requests. | Developer | Approval triggers one idempotent INV receipt request; stock rises only on INV Posted confirmation. |
| REQ_F4113.4.5 | 1.0 | System should allow user to reject production inbound requests. | Developer | Rejection records reason and creates no INV receipt. |
| REQ_F4113.4.6 | 1.0 | System should allow user to delete unposted draft production inbound requests. | Developer | Posted source remains linked and corrections use reversal. |
| REQ_F4113.5.1 | 1.0 | System should allow user to define and approve versioned production routings. | Developer | Ordered operations identify work centre, standard duration, capacity unit and effective dates; approved referenced revision is immutable. |
| REQ_F4113.6.1 | 1.0 | System should enable user to run a master production schedule. | Developer | MPS freezes scoped SD demand, available supply and planning horizon with owner cutoffs; absent source is unavailable, never zero demand. |
| REQ_F4113.7.1 | 1.0 | System should enable user to run material requirements planning. | Developer | MRP explodes approved effective BOM without cycles, nets confirmed INV availability and committed supply once, and records pegging, shortage and proposal provenance. |
| REQ_F4113.8.1 | 1.0 | System should allow user to release and track work orders. | Developer | Release freezes plan/BOM/routing revisions; requested, issued, completed, accepted and Posted quantities reconcile; duplicate release cannot create two orders. |
| REQ_F4113.9.1 | 1.0 | System should enable user to view capacity and shop-floor progress. | Developer | Capacity is scheduled load against versioned work-centre calendar; actual time and completion are audited independently of stock. |
| REQ_F4113.2.7 | 1.0 | System should enable authorised planning consumers to retrieve a replayable snapshot of Released production-plan demand. | Developer | PP alone exposes committed Released plan-line revisions by company, item, site, saved business month and source UOM/quantity. Each request receives a fixed cursor after commit, stable pages/count/digest and an explicit Complete result for every requested month, including empty months. Pending, Approved but unreleased, rejected, cancelled and deleted lines contribute no active demand; later correction uses a new cursor. Current authorisation is checked on every page; an absent PP owner is named OwnerMissing, not zero demand. |

BlueWhale REQ_F4113.1.1–4.6 retain their original IDs. New tables receive REQ_DS43 IDs only when their migrations are designed. Interface fields receive REQ_IO_A3 IDs with the screens.

Change record: WU-PP-001d added REQ_F4113.2.7 as an internal owner-read requirement for SCM comparison. It does not assert that a PP backend, database table, endpoint or reader exists; WU-PP-001c owns implementation and true SQL acceptance.
