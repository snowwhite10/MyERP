# SCM — Supply chain business rules

## WU-SCM-000d baseline

| Rule | Design invariant |
|---|---|
| BR-SCM-001 | SCM owns forecast, consignment/event evidence and supplier proposals. It reads SD, PP, INV, PUR, MD and SYS through owner Application contracts, never their tables. SCM never posts inventory or edits a purchase order. |
| BR-SCM-002 | Forecast grain is company, item, location, unit and closed time bucket; all input owner IDs, versions and committed cutoffs are retained in a source manifest. |
| BR-SCM-003 | A missing, stale or unauthorised owner source is unavailable, never zero. Forecast runs expose complete, partial and unavailable source status with named reasons. |
| BR-SCM-004 | Forecast method, parameters and overrides are versioned. Publishing requires independent SYS approval; published versions and historical calculations are immutable. |
| BR-SCM-005 | Confirmed SD orders, PP released plans and INV posted movements are different measures; deduplication follows owner permanent source keys and unit conversion policy. |
| BR-SCM-006 | Consignment planning does not confirm PUR receipt, SD shipment or INV movement. Their confirmed states are queried independently and shown with owner cutoff. |
| BR-SCM-007 | Carrier event key is unique per carrier and consignment, including retired history. Duplicate payload is idempotent; changed payload is 409. Out-of-order events append evidence without rewriting history. |
| BR-SCM-008 | Delivered quantity is supported by evidence and compared with owner confirmed quantities. Shortage, damage, late delivery and unknown owner result remain explicit exceptions. |
| BR-SCM-009 | Carrier proof and attachments have size, MIME/content, malware and access controls; download rechecks company, order and action permission. Retention is versioned policy. |
| BR-SCM-010 | Supplier invitation binds a SYS account to one MD supplier, company, effective period and allowed PUR order set. Expiry or revocation takes effect for all later reads, downloads and commands. |
| BR-SCM-011 | A supplier proposal never updates PUR directly. PUR accepts or rejects through its Application contract using a permanent proposal key; timeout stays PendingOwner until lookup confirms the outcome. |
| BR-SCM-012 | Every list, export and batch reports changed, skipped, failed and pending counts with per-row reasons. Zero changes and all skipped are warnings, not success. |
| BR-SCM-013 | Action, company, supplier and data scope checks precede existence disclosure. Other-company or other-supplier IDs return 404; separate rights govern review, approval, export and download. |
| BR-SCM-014 | Stable committed source membership covers forecast comparisons, transport reconciliation and collaboration pages/exports. Concurrent publish, event, scope revocation and proposal acceptance have one confirmed effect. |
| BR-SCM-015 | Invalid shape is 400, stale version/duplicate 409, invalid state 422 and server error 500 with traceId. Timeout gives unknown outcome and key lookup. Audit records actor, source, version, action and confirmed outcome without leaking prices or personal data. |

Implementation ADRs must settle owner DTO and cutoff semantics, forecast method and unit conversion, transport provider/event verification, evidence handling, supplier onboarding and PUR proposal compensation before executable acceptance.
