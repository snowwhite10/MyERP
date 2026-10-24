# PM — Test plan

## WU-PM-000d scenarios (design only)

`myerp-test-scenarios` categories apply. U = unit, I = integration with real SQL and owner Application contracts, E = authorised Playwright. Later implementation units first write failing product tests. Every E case must check translated visible message, severity, counts and reasons and independently read saved PM and owner INV/FI data. All product cases are TODO; no executable PM product test or acceptance is claimed.

| ID | Class | Scenario and assertion | Level | Requirements | Status |
|---|---|---|---|---|---|
| PM-BASE-001 | Normal | Register equipment with optional verified FI asset, revise details, view unchanged FI book and audit. | U,I,E | REQ_F41112.1.1–1.2 | TODO |
| PM-BASE-002 | Normal | Calendar and meter plans generate one due occurrence/order each; approval, actual work, checklist and downtime lead to completion. | U,I,E | REQ_F41112.2.1–3.3 | TODO |
| PM-BASE-003 | Normal | Reserve, issue and partially return a spare; PM net consumption equals confirmed INV movement and FI posted cost stays distinct. | U,I,E | REQ_F41112.3.3,4.1–4.3 | TODO |
| PM-BASE-004 | Boundary | Empty/max codes and labels, zero/negative and decimal precision/overflow in reading or quantity, timezone/DST, month/year/leap-day and meter threshold equality yield field reasons. | U,I,E | REQ_F41112.1.1,2.1–2.3,4.1 | TODO |
| PM-BASE-005 | Duplicate/conflict | Duplicate equipment code including soft-deleted code, overlapping approved plans and two schedulers on same due key cannot create duplicate work. | U,I,E | REQ_F41112.1.1,2.1–2.3 | TODO |
| PM-BASE-006 | Duplicate/conflict | Stale PM/INV/FI version is 409; same command key and payload replays unchanged; altered payload with same key conflicts. | I,E | REQ_F41112.2.3,3.1–3.3,4.1–4.3 | TODO |
| PM-BASE-007 | Partial result | Batch due generation with new, existing, invalid and owner-unavailable rows reports each count/reason and persists only new eligible occurrences. | I,E | REQ_F41112.2.2–2.3 | TODO |
| PM-BASE-008 | Partial result | All skipped, all failed, zero-change and partial spare issue/return show warning/error and actual PM/INV totals; no false success. | I,E | REQ_F41112.2.3,4.1–4.3 | TODO |
| PM-BASE-009 | Permission | Anonymous 401, missing action 403, revoked approval/export/download denied; no substitute permission from FI/INV. | I,E | REQ_F41112.1.1–4.3 | TODO |
| PM-BASE-010 | Permission | Other-company/branch equipment, order, FI asset and INV movement are hidden as 404 with no count or cost leak. | I,E | REQ_F41112.1.1–4.3 | TODO |
| PM-BASE-011 | State | Draft, pending approval, rejected, released, in-progress, completed and cancelled orders allow only defined transitions; disabled action explains why. | U,I,E | REQ_F41112.3.1–3.3 | TODO |
| PM-BASE-012 | Relation | Referenced plan cannot be deleted; revision preserves due order; retired equipment or inactive/soft-deleted MD item stays historical but cannot seed new work. | I,E | REQ_F41112.1.2–1.3,2.1–2.3,4.1 | TODO |
| PM-BASE-013 | Consistency | Open order/pending reservation blocks equipment retirement; simultaneous issue, return and cancel preserve `returned <= issued` and no double stock movement. | I,E | REQ_F41112.1.3,3.2,4.1–4.3 | TODO |
| PM-BASE-014 | Consistency | Late meter reading, revised plan or later INV/FI posting leaves frozen history/export stable; refreshed manifest includes new confirmed events exactly once. | I,E | REQ_F41112.1.2,2.2,3.3 | TODO |
| PM-BASE-015 | Error | 400/409/422/500 with traceId, network loss and timeout show localised reason; permanent-key lookup resolves unknown INV commit. | I,E | REQ_F41112.1.1–4.3 | TODO |
| PM-BASE-016 | File | Import N/A: no PM file import is proposed. Empty/oversized export and unsafe spreadsheet cells preserve frozen scope and recheck authority. | I,E | REQ_F41112.1.2,3.3 | TODO |
| PM-BASE-017 | UI | Three languages and light/dark show normal, empty, validation, server error, success, partial and denied states; message and saved owner data agree. | E | REQ_F41112.1.1–4.3 | TODO |
| PM-BASE-018 | Audit | Actor, company, plan/version, due key, order and owner outcome are traceable; preview/history does not mutate INV or FI. | I,E | REQ_F41112.1.1–4.3 | TODO |

Traceability: equipment 1.1–1.3 → BR-PM-001–003,013–016 → PM-BASE-001,004–006,009–015,017–018; plans 2.1–2.3 → BR-PM-004–006,013–016 → 002,004–008,009–012,014–018; orders 3.1–3.3 → BR-PM-007–008,012–016 → 002–003,006,009–015,017–018; spares 4.1–4.3 → BR-PM-009–014,016 → 003–004,006,008–013,015,017–018.
