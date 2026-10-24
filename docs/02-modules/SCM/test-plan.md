# SCM — Test plan

## WU-SCM-000d scenarios (design only)

`myerp-test-scenarios` categories apply. U = unit, I = real SQL integration through owner Application contracts, E = authorised Playwright. All cases are TODO; implementation units first write failing product tests. Every E case compares translated visible message, severity, counts and row reasons with independently read saved SCM and owner outcomes.

| ID | Class | Scenario and assertion | Level | Requirements | Status |
|---|---|---|---|---|---|
| SCM-BASE-001 | Normal | Run forecast with SD, PP and INV committed sources, approve a version and compare all measures at named cutoffs. | U,I,E | REQ_F41116.1.1–1.3 | TODO |
| SCM-BASE-002 | Normal | Plan inbound/outbound consignment, ingest verified carrier event and reconcile delivery with owner confirmations. | I,E | REQ_F41116.2.1–2.3 | TODO |
| SCM-BASE-003 | Normal | Invite supplier for one order, submit proposal, review and confirm PUR outcome through owner key. | I,E | REQ_F41116.3.1–3.3 | TODO |
| SCM-BASE-004 | Boundary | Empty/max fields and pages, zero/negative/precision quantity, month/year/leap-day bucket, timezone and delivery window boundaries show field reasons. | U,I,E | REQ_F41116.1.1–3.3 | TODO |
| SCM-BASE-005 | Duplicate/conflict | Duplicate forecast grain/version, consignment/event key including retired history, supplier grant and stale edit yield deterministic 409. | U,I,E | REQ_F41116.1.1,2.1–2.2,3.1 | TODO |
| SCM-BASE-006 | Duplicate/conflict | Same-key forecast publish, carrier event or PUR proposal replay has one effect; changed payload conflicts under two concurrent requests. | I,E | REQ_F41116.1.2,2.2,3.3 | TODO |
| SCM-BASE-007 | Partial result | Mixed forecast sources, carrier events or proposals show committed/skipped/failed/pending counts and row reasons matching saved records. | I,E | REQ_F41116.1.1,2.2,3.3 | TODO |
| SCM-BASE-008 | Partial result | Empty batch, all skipped and all failed produce warning/error, zero changed and no speculative owner confirmation. | I,E | REQ_F41116.1.1,2.2,3.3 | TODO |
| SCM-BASE-009 | Permission | Anonymous 401, missing action 403, separate approval/review/export/download rights and author/approver separation. | I,E | REQ_F41116.1.1–3.3 | TODO |
| SCM-BASE-010 | Permission | Other company, supplier or out-of-scope order, forecast source and evidence return hidden 404 without count leakage. | I,E | REQ_F41116.1.1–3.3 | TODO |
| SCM-BASE-011 | State | Draft/approved/superseded forecast, planned/in-transit/delivered/exception consignment and invited/revoked/submitted/reviewed proposal reject forbidden transitions. | U,I,E | REQ_F41116.1.1–3.3 | TODO |
| SCM-BASE-012 | Relation | Inactive supplier, revoked grant, cancelled PUR order, void SD demand and missing PP/INV source block new work while preserving historical evidence. | I,E | REQ_F41116.1.1–3.3 | TODO |
| SCM-BASE-013 | Consistency | Concurrent last publish, grant revocation and proposal acceptance have one confirmed effect; owner failure rolls back local claim or records pending for reconciliation. | I,E | REQ_F41116.1.2,3.1–3.3 | TODO |
| SCM-BASE-014 | Consistency | Late/duplicated/out-of-order carrier event, corrected owner source and across-page export keep immutable history and fixed committed membership. | I,E | REQ_F41116.1.3,2.2–2.3 | TODO |
| SCM-BASE-015 | Error | 400/409/422/500 with traceId, network loss, owner failure and timeout show localised reason and key lookup for unknown outcome. | I,E | REQ_F41116.1.1–3.3 | TODO |
| SCM-BASE-016 | File | Empty, malformed, oversized or wrong-encoding carrier file and unsafe proof content fail per row; export escapes spreadsheet formulas and revoked download fails. | I,E | REQ_F41116.2.2,3.2 | TODO |
| SCM-BASE-017 | UI | Three languages and light/dark show normal, empty, validation, server error, success, partial and denied states; visible result equals saved data. | E | REQ_F41116.1.1–3.3 | TODO |
| SCM-BASE-018 | Audit | Actor, company, supplier, source/version, cutoff, proof hash, decision and reason are traceable without leaking price or personal data. | I,E | REQ_F41116.1.1–3.3 | TODO |

Traceability: forecast 1.1–1.3 → BR-SCM-001–005,012–015 → SCM-BASE-001,004–015,017–018; transport 2.1–2.3 → BR-SCM-001,006–009,012–015 → 002,004–018; collaboration 3.1–3.3 → BR-SCM-001,010–015 → 003–013,015–018.

## WU-SCM-001d source-contract scenarios (design only)

`myerp-test-scenarios` checklist applied. These are executable acceptance cases for the future owner-reader and 001a slices; they remain TODO because this unit has no product code. Every E case must assert the translated severity, counts and per-source reason against a saved version and independent owner read. `U` tests the method/manifest validator; `I` uses real SQL and two connections; `E` uses the authorised UI.

| ID | Class | Scenario and assertion | Level | Requirements | Status |
|---|---|---|---|---|---|
| SCM-SRC-001 | Normal | Three complete SD monthly buckets with 2, 4 and 6 base units produce next-month 4 by `SCM-SD-MA3-v1`; PP/INV remain separate overlays; rerun at saved cursor gives identical manifest and value. | U,I,E | REQ_F41116.1.1–1.3 | TODO |
| SCM-SRC-002 | Boundary | Explicit SD `Complete` empty month counts as zero; absent month, fewer than three complete months, month/year/leap boundary and empty horizon give named insufficiency/field errors. | U,I,E | REQ_F41116.1.1 | TODO |
| SCM-SRC-003 | Boundary | Maximum page size 500, next-page ordering, six-place quantity rounding once, overflow, zero/negative source quantity and mixed UOM preserve exact totals or reject with row reason. | U,I | REQ_F41116.1.1 | TODO |
| SCM-SRC-004 | Duplicate/conflict | Duplicate permanent owner row key or changed payload/version at same key conflicts; concurrent version publication and stale cursor return 409 without a second approved version. | U,I,E | REQ_F41116.1.1–1.2 | TODO |
| SCM-SRC-005 | Partial result | SD partial page/row, PP unavailable, INV unavailable: SD blocks publish; optional overlays show Partial/Unavailable with owner reason; no missing measure is rendered as 0. | U,I,E | REQ_F41116.1.1–1.3 | TODO |
| SCM-SRC-006 | Partial result | All three owner readers unavailable, all rows skipped, and explicit complete zero-row SD give distinct unavailable, warning and valid zero outcomes with changed/skipped/failed counts matching saved data. | I,E | REQ_F41116.1.1–1.3 | TODO |
| SCM-SRC-007 | Permission | Anonymous 401, missing forecast action 403, other-company/out-of-scope item or site 404; cursor/token cannot reveal hidden row counts and export permission is rechecked. | I,E | REQ_F41116.1.1–1.3 | TODO |
| SCM-SRC-008 | State | Pending/rejected/void SD, unreleased PP and unposted INV are excluded by owner; late correction creates a new SCM version and leaves approved snapshot immutable. | I,E | REQ_F41116.1.1–1.3 | TODO |
| SCM-SRC-009 | Relation | Deleted/inactive MD item or conversion, changed base UOM, or changed/removed factor cannot silently recalculate saved version; new run requires valid frozen conversion evidence. | U,I,E | REQ_F41116.1.1–1.3 | TODO |
| SCM-SRC-010 | Consistency | Two connections add/correct owner rows between source pages; fixed cursor retains same digest/count or fails unavailable. Query and export across pages use identical saved membership. | I,E | REQ_F41116.1.1–1.3 | TODO |
| SCM-SRC-011 | Error | Malformed filter 400, stale cursor 409, invalid UOM/state 422, owner 500 with traceId, timeout/connection loss show named unavailable or unknown outcome and no approval success. | I,E | REQ_F41116.1.1–1.3 | TODO |
| SCM-SRC-012 | File | Forecast source acquisition has no file upload; empty/bad encoding/oversized file cases are not applicable. CSV export still escapes formulas and uses frozen membership. | I,E | REQ_F41116.1.3 | TODO |
| SCM-SRC-013 | UI | Three languages and light/dark show complete, partial, empty, denied, validation, timeout and 500 states; disabled approval names missing SD/conversion; visible value/status equals saved version. | E | REQ_F41116.1.1–1.3 | TODO |

Traceability: REQ_F41116.1.1–1.3 → BR-SCM-016–023 → SCM-SRC-001–013. Before 001a, add owner Contract tests in SD/PP/INV/MD, then write failing SCM method/real SQL tests. No runtime test is marked passed by this design document.
