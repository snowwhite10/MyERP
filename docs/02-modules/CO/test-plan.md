# CO — Test plan

## WU-CO-000d scenarios (design only)

`myerp-test-scenarios` categories are covered below. U = unit, I = integration with real SQL and owner contracts, E = authorised Playwright. Later implementation units first write failing product tests. Every E case asserts translated visible text, severity, counts/reasons and an independent read of persisted CO state plus owner FI/INV/PP source state. All product cases are TODO; this unit adds no executable CO test or product acceptance.

| ID | Class | Scenario and assertion | Level | Requirements | Status |
|---|---|---|---|---|---|
| CO-BASE-001 | Normal | Create cost/profit center hierarchies, assign effective policies and preserve older hierarchy snapshots. | U,I,E | REQ_F41110.1.1–2.3 | TODO |
| CO-BASE-002 | Normal | Calculate a product run from confirmed INV and PP sources; components and source IDs reconcile exactly. | U,I,E | REQ_F41110.3.1–3.3 | TODO |
| CO-BASE-003 | Normal | Approve budget, compare with FI Posted actuals, drill and export identical frozen scope/cutoff. | I,E | REQ_F41110.4.1–4.4 | TODO |
| CO-BASE-004 | Boundary | Empty/max code and name, max depth/lines, zero/negative values, decimal scale/overflow, month/year/leap-day ranges give field/line reasons. | U,I,E | REQ_F41110.1.2,2.2,3.2,4.1 | TODO |
| CO-BASE-005 | Duplicate/conflict | Duplicate code including deleted row, direct/transitive hierarchy cycle, overlapping policy and concurrent approval fail without a second effective version. | U,I,E | REQ_F41110.1.2–3,2.2–3,4.1 | TODO |
| CO-BASE-006 | Duplicate/conflict | Concurrent edits and stale budget versions return 409; replay of same run key is unchanged, changed fingerprint conflicts. | I,E | REQ_F41110.3.2–3,4.1 | TODO |
| CO-BASE-007 | Partial result | One unpriced INV input or missing PP actual marks cost run incomplete with line reason; all unavailable/zero source never yields a successful cost. | I,E | REQ_F41110.3.1–3 | TODO |
| CO-BASE-008 | Partial result | One unmapped FI line, all unmapped, all failed and no changed budget lines show exact counts, sums and reasons; no misleading success. | I,E | REQ_F41110.4.1–4 | TODO |
| CO-BASE-009 | Permission | Anonymous 401, absent per-action right 403, revoked role denies later page/approval/export despite cached result. | I,E | REQ_F41110.1.1–4.4 | TODO |
| CO-BASE-010 | Permission | Other-company/branch center, MD item, FI source or budget ID is hidden as 404; no count or amount leaks. | I,E | REQ_F41110.1.1–4.4 | TODO |
| CO-BASE-011 | State | Draft/pending/rejected/approved/revised cost and budget states permit only specified transitions; denied action explains why. | U,I,E | REQ_F41110.3.3,4.1 | TODO |
| CO-BASE-012 | Relation | Referenced center/policy cannot deactivate; inactive MD item/account remains in history but cannot enter a new run or mapping. | I,E | REQ_F41110.1.3,2.3,3.2,4.1 | TODO |
| CO-BASE-013 | Consistency | FI backdated posting/reversal or policy change after cutoff leaves frozen pages/export unchanged; refreshed result gets new cutoff and detail sum equals aggregate. | I,E | REQ_F41110.4.2–4 | TODO |
| CO-BASE-014 | Error | 400/409/422/500 with traceId, network loss and timeout display localised reason; unknown outcome is reconciled by permanent key. | I,E | REQ_F41110.1.1–4.4 | TODO |
| CO-BASE-015 | File | Upload is N/A: no CO import is proposed. Export handles empty result, oversized request and safe CSV/Excel content with frozen scope and renewed authority. | I,E | REQ_F41110.4.4 | TODO |
| CO-BASE-016 | UI | Three languages and light/dark show empty, loading, denied, validation, partial, failure and success; disabled actions explain reason. | E | REQ_F41110.1.1–4.4 | TODO |
| CO-BASE-017 | Audit | Mutation logs actor/company/source/version; read, run preview and export do not change FI Posted journals or INV movement/version. | I,E | REQ_F41110.1.1–4.4 | TODO |

Traceability: centers 1.1–2.3 → BR-CO-001–002,011–013 → CO-BASE-001,004–6,009–12,014,016–17; product cost 3.1–3.3 → BR-CO-004–007,011–013 → 002,004,006–7,009–12,014,016–17; budget 4.1–4.4 → BR-CO-003,008–013 → 003–6,008–17. No executable CO product test is claimed.
