# QM — Test plan

## WU-QM-000d scenarios (design only)

`myerp-test-scenarios` categories are applied below. U = unit, I = integration with real SQL and owner Application contracts, E = authorised Playwright. Later implementation units first write failing product tests. Every E case asserts translated visible message, severity, counts and reasons plus an independent persisted QM read and owner PUR/PP/INV read. All product cases are TODO: no executable QM test or product acceptance is claimed here.

| ID | Class | Scenario and assertion | Level | Requirements | Status |
|---|---|---|---|---|---|
| QM-BASE-001 | Normal | Version a plan, inspect PUR receipt, approve accepted and rejected portions; PUR posts accepted amount once, INV confirms same amount. | U,I,E | REQ_F41111.1.1–1.4 | TODO |
| QM-BASE-002 | Normal | Inspect released PP operation, fail checkpoint, rework and re-inspect; only PP-confirmed accepted output reaches INV. | U,I,E | REQ_F41111.2.1–2.3,3.1–3.3 | TODO |
| QM-BASE-003 | Normal | Freeze report, drill and export matching inspected/rejected quantities and explicit denominator. | I,E | REQ_F41111.4.1–4.2 | TODO |
| QM-BASE-004 | Boundary | Empty/max codes and labels, zero/negative quantity, decimal precision/overflow, sample min/max, limit equality and month/year/leap-day effectiveness yield field/line reasons. | U,I,E | REQ_F41111.1.1–1.3,2.1–2.3 | TODO |
| QM-BASE-005 | Duplicate/conflict | Active plan interval overlap, code held by soft-deleted plan, duplicate lot source and two concurrent final decisions cannot create two effective decisions. | U,I,E | REQ_F41111.1.1–1.4,2.1–2.3 | TODO |
| QM-BASE-006 | Duplicate/conflict | Stale source/decision version returns 409; same permanent key replays unchanged; same key with changed fingerprint conflicts. | I,E | REQ_F41111.1.3–1.4,2.3,3.1–3.2 | TODO |
| QM-BASE-007 | Partial result | Batch with accepted, rejected and invalid lines shows each count and reason, persists only permitted decisions and matches PUR/INV owner totals. | I,E | REQ_F41111.1.3–1.4,3.1–3.3 | TODO |
| QM-BASE-008 | Partial result | All skipped, all failed and zero-change batches show warning/error, no success; owner unavailable leaves pending with reason and no claimed posting. | I,E | REQ_F41111.1.4,2.3,3.3 | TODO |
| QM-BASE-009 | Permission | Anonymous 401, missing action right 403, revoked approval/export right denies later request and download. | I,E | REQ_F41111.1.1–4.2 | TODO |
| QM-BASE-010 | Permission | Other-company/branch receipt, work order, lot and case are hidden as 404 with no count or measurement leak. | I,E | REQ_F41111.1.2–4.2 | TODO |
| QM-BASE-011 | State | Draft/pending/approved/rejected/superseded lot and open/disposition-pending/closed case permit only defined transitions; disabled action has reason. | U,I,E | REQ_F41111.1.3–1.4,2.3,3.1–3.3 | TODO |
| QM-BASE-012 | Relation | Referenced plan cannot be deleted, later plan revision does not change saved lot; inactive item/receipt/operation remains historical but cannot seed new lot. | I,E | REQ_F41111.1.1–1.3,2.1–2.3 | TODO |
| QM-BASE-013 | Consistency | Concurrent PUR receipt edit, PP completion or INV quarantine change conflicts/reconciles; accepted + rejected + pending equals source quantity and no double availability occurs. | I,E | REQ_F41111.1.2–1.4,2.2–2.3 | TODO |
| QM-BASE-014 | Consistency | Later backdated decision or owner movement leaves frozen report/drill/export unchanged; refreshed manifest includes it exactly once. | I,E | REQ_F41111.4.1–4.2 | TODO |
| QM-BASE-015 | Error | 400/409/422/500 with traceId, network loss and timeout display localised reason; permanent-key lookup resolves unknown commit. | I,E | REQ_F41111.1.1–4.2 | TODO |
| QM-BASE-016 | File | Upload N/A: no QM import is proposed. Empty/oversized export and unsafe cell contents are handled with same frozen scope and renewed authority. | I,E | REQ_F41111.4.2 | TODO |
| QM-BASE-017 | UI | Three languages and light/dark show normal, empty, validation, server error, success, partial and denied states; messages and saved owner data agree. | E | REQ_F41111.1.1–4.2 | TODO |
| QM-BASE-018 | Audit | Actor, company, source, plan/decision version and owner confirmation are traceable; preview/report read never mutates PUR/PP/INV. | I,E | REQ_F41111.1.1–4.2 | TODO |

Traceability: incoming 1.1–1.4 → BR-QM-001–005,007–008,012–014 → QM-BASE-001,004–13,015,017–018; process 2.1–2.3 → BR-QM-001–002,006–008,012–014 → 002,004–13,015,017–018; nonconformance 3.1–3.3 → BR-QM-007–010,012–014 → 002,006–13,015,017–018; reports 4.1–4.2 → BR-QM-011–013 → 003,009–010,014–018.
