# PS — Test plan

## WU-PS-000d scenarios (design only)

`myerp-test-scenarios` categories apply. U = unit, I = real SQL integration using owner Application contracts, E = authorised Playwright. All cases are TODO; implementation units first write failing product tests. Every E case compares translated visible message, severity, counts and row reasons with independently read saved PS and owner outcomes.

| ID | Class | Scenario and assertion | Level | Requirements | Status |
|---|---|---|---|---|---|
| PS-BASE-001 | Normal | Create project, approve budget version and compare with FI posted actual at named cutoff. | U,I,E | REQ_F41115.1.1–2.2 | TODO |
| PS-BASE-002 | Normal | Record and approve time, reserve capacity, accept milestone and prepare eligible billing sources. | U,I,E | REQ_F41115.3.1–6.1 | TODO |
| PS-BASE-003 | Normal | Submit proposal, confirm SD invoice and FI receivable by owner key; trace all states independently. | I,E | REQ_F41115.6.1–6.2 | TODO |
| PS-BASE-004 | Boundary | Empty/max fields and pages, zero/negative duration or money, decimal precision, month/year/leap day, cross-midnight and timezone boundaries show field reasons. | U,I,E | REQ_F41115.1.1–6.2 | TODO |
| PS-BASE-005 | Duplicate/conflict | Duplicate project code including soft-deleted history, overlapping work or budget periods, duplicate billing source and stale version yield deterministic 409. | U,I,E | REQ_F41115.1.1,2.1–2.2,3.1,6.1 | TODO |
| PS-BASE-006 | Duplicate/conflict | Same-key approval, acceptance and billing replay have one effect; changed payload conflicts, including two concurrent requests. | I,E | REQ_F41115.2.2,5.1,6.2 | TODO |
| PS-BASE-007 | Partial result | Mixed capacity or billing batch reports committed, skipped, failed and pending rows with reasons; totals equal saved records. | I,E | REQ_F41115.4.1,6.1–6.2 | TODO |
| PS-BASE-008 | Partial result | Empty batch, all skipped and all failed produce warning/error with zero changed and no speculative invoice. | I,E | REQ_F41115.4.1,6.1–6.2 | TODO |
| PS-BASE-009 | Permission | Anonymous 401, absent action 403, separate approval/BILL/export/download rights and author-approver separation. | I,E | REQ_F41115.1.1–6.2 | TODO |
| PS-BASE-010 | Permission | Other-company and out-of-scope project, employee, customer, financial totals and source IDs are hidden as 404 without count leakage. | I,E | REQ_F41115.1.1–6.2 | TODO |
| PS-BASE-011 | State | Draft/submitted/approved/superseded budget, open/closed project, unapproved/billed time and proposed/invoiced/posted billing reject forbidden transitions. | U,I,E | REQ_F41115.1.1–6.2 | TODO |
| PS-BASE-012 | Relation | Inactive HR employee, closed project, unavailable SD contract, removed customer or FI source blocks new actions while preserving historical references. | I,E | REQ_F41115.1.1–6.2 | TODO |
| PS-BASE-013 | Consistency | Concurrent last capacity, duplicate claim, milestone acceptance and invoice submission produce one confirmed result with full rollback on owner failure. | I,E | REQ_F41115.4.1,5.1,6.1–6.2 | TODO |
| PS-BASE-014 | Consistency | Later rate, work correction or FI posting does not rewrite frozen budget/billing; as-of pages and exports keep same committed source membership. | I,E | REQ_F41115.2.2,3.2,6.1–6.2 | TODO |
| PS-BASE-015 | Error | 400/409/422/500 with traceId, network loss, owner failure and timeout show localised reason and key lookup for unknown outcome. | I,E | REQ_F41115.1.1–6.2 | TODO |
| PS-BASE-016 | File | Import is not proposed: import-file cases are inapplicable. Empty/oversized export, unsafe spreadsheet cells and revoked download permission preserve scope and escaping. | I,E | REQ_F41115.1.2,2.2,6.2 | TODO |
| PS-BASE-017 | UI | Three languages and light/dark show normal, empty, validation, server error, success, partial and denied states; message and saved data agree. | E | REQ_F41115.1.1–6.2 | TODO |
| PS-BASE-018 | Audit | Actor, company, source/version, cutoff, approval, correction and reason are traceable without leaking employee rates or contract prices. | I,E | REQ_F41115.1.1–6.2 | TODO |

Traceability: project 1.1–1.2 → BR-PS-001–002,012–015 → PS-BASE-001,004–005,009–012,015–018; budget 2.1–2.2 → BR-PS-003–004,012–015 → 001,004–006,009–011,014–018; time 3.1–3.2 → BR-PS-005–006,012–015 → 002,004–005,009–012,014–018; resource 4.1–4.2 → BR-PS-007,011–015 → 002,004,007–013,015–018; milestone 5.1–5.2 → BR-PS-008,012–015 → 002,004,006,009–013,015–018; billing 6.1–6.2 → BR-PS-009–015 → 002–018.
