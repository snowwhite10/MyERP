# QM — Quality business rules

## WU-QM-000d baseline

| Rule | Design invariant |
|---|---|
| BR-QM-001 | QM owns inspection plan versions, sample measurements, lot decisions and nonconformance cases. PUR, PP and INV source identities are immutable references, never QM-owned status or stock balances. |
| BR-QM-002 | Each company/item or operation selection key has at most one effective approved plan at a date. A lot freezes selected plan, characteristic limits, sampling rule, unit and source version; later plan edits create a new version. |
| BR-QM-003 | Incoming lot uses a PUR-owned saved receipt line and confirmed received quantity. QM checks `accepted + rejected + pending = source quantity` at the same unit/precision. Receipt edits after sampling require conflict/reconciliation, never silent rescaling. |
| BR-QM-004 | QM final incoming decision may authorise PUR to post accepted receipt quantity. PUR alone drives its receipt lifecycle and INV `PostPurchaseInboundAsync`; rejected quantity needs PUR supplier-return or owner-defined resolution. A QM approval alone is not a stock movement. |
| BR-QM-005 | For any material already posted to INV before inspection, INV alone controls quarantine and release through an explicit owner contract. The future implementation must choose pre-post inspection or posted-quarantine per receipt type and prove no double available stock. |
| BR-QM-006 | PP exposes saved work order, released operation/version, measured completed quantity and accepted result via its Application contract. QM does not infer output from planned quantity and does not directly post finished goods to INV. Missing PP implementation is a named unavailable source. |
| BR-QM-007 | Each inspection decision has a permanent company/source-line/decision-key and fingerprint. Same-key same-payload replay changes nothing; a changed payload conflicts. Source quantity, plan version and decision state are rechecked under owner lock before final commit. |
| BR-QM-008 | SYS owns independent approval and audit identity. QM owns draft/pending/approved/rejected/superseded decision state and observations. Final approval and QM state transition are one transaction; owner action is recorded as pending until its confirmed result is linked. |
| BR-QM-009 | A failed inspection creates at most one active nonconformance for each decision/affected quantity slice. Every case records reason, evidence and disposition version. Accepted concession needs independent authority; rework, scrap and return require their respective PP/INV/PUR owner outcome before closure. |
| BR-QM-010 | Rework output is inspected again under a new inspection/decision key. Scrap and supplier return quantities never exceed unresolved rejected quantity; partial outcome leaves the residual open. QM never directly updates inventory availability or production actuals. |
| BR-QM-011 | Reports count only committed decisions/cases once per immutable source key. Rate denominator is explicit (inspected quantity or lot count), with zero denominator shown as N/A. Pages, drill and export share a frozen source set, units, scope and owner cutoffs. |
| BR-QM-012 | Company, branch and data-scope rights apply to plan, source lot, decision, case and report on every request and download. Cross-company identities are hidden as 404; missing action right is 403. Revoked authority invalidates later reads. |
| BR-QM-013 | Batch decisions and reports distinguish changed, skipped, failed and pending counts with per-line reasons. No change or all skipped cannot be labelled success. Unknown timeout is resolved through permanent-key lookup before retry. |
| BR-QM-014 | Inactive or soft-deleted MD item, PUR receipt or PP operation remains traceable in history but cannot seed a new inspection. Stale optimistic versions return 409; invalid quantity/limits/state return 422. |

Implementation slices must settle sampling standard, unit conversion and the PUR/INV pre-post versus quarantine protocol in an ADR before changing shared contracts.
