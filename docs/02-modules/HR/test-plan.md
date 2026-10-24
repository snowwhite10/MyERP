# HR — Test plan

## WU-HR-000d scenarios (design only)

`myerp-test-scenarios` categories apply. U = unit, I = real SQL integration with owner Application contracts, E = authorised Playwright. Implementation units first write failing product tests. All cases below are TODO; this design has no executable HR product acceptance. Every E case compares translated visible message, severity, counts and row reasons with independently read saved HR and SYS/FI/provider outcomes.

| ID | Class | Scenario and assertion | Level | Requirements | Status |
|---|---|---|---|---|---|
| HR-BASE-001 | Normal | Create employee, change effective-dated assignment, terminate and view unchanged history. | U,I,E | REQ_F41114.1.1–2.2 | TODO |
| HR-BASE-002 | Normal | Publish roster, record clock events, approve correction and freeze attendance cutoff. | U,I,E | REQ_F41114.3.1–3.2 | TODO |
| HR-BASE-003 | Normal | Calculate, approve and release payroll; reconcile components and confirm FI outcome independently. | U,I,E | REQ_F41114.4.1–4.2 | TODO |
| HR-BASE-004 | Normal | Grant, request, approve and revoke leave; recompute balance at named cutoff. | U,I,E | REQ_F41114.5.1–5.2 | TODO |
| HR-BASE-005 | Normal | Approve vacancy, progress consented candidate and convert once to employee. | U,I,E | REQ_F41114.6.1–6.2 | TODO |
| HR-BASE-006 | Normal | Complete acknowledged performance review and training enrolment/completion. | U,I,E | REQ_F41114.7.1–8.2 | TODO |
| HR-BASE-007 | Boundary | Empty and maximum field lengths, max page/batch, money precision, zero/negative pay, month/year/leap-day and overnight/timezone boundaries give field reasons. | U,I,E | REQ_F41114.1.1–8.2 | TODO |
| HR-BASE-008 | Duplicate/conflict | Duplicate employee code including soft-deleted history, overlapping employment/shift/leave and duplicate clock event reject or explain deterministic resolution. | U,I,E | REQ_F41114.1.1–5.2 | TODO |
| HR-BASE-009 | Duplicate/conflict | Stale version yields 409; candidate conversion, payroll release and FI posting same-key retry preserve one effect, changed payload conflicts. | I,E | REQ_F41114.4.2,6.2 | TODO |
| HR-BASE-010 | Partial result | Batch enrolment/release with eligible, ineligible, full, already processed and owner-pending rows reconciles counts and reasons to committed records. | I,E | REQ_F41114.4.2,8.1–8.2 | TODO |
| HR-BASE-011 | Partial result | Zero rows, all skipped and all failed show warning/error, never success; replay leaves data unchanged. | I,E | REQ_F41114.4.2,8.1 | TODO |
| HR-BASE-012 | Permission | Anonymous 401, missing action 403, self-service restricted fields, revoked export/download and approval separation are enforced. | I,E | REQ_F41114.1.1–8.2 | TODO |
| HR-BASE-013 | Permission | Other-company/branch/out-of-scope employee, candidate, salary, feedback and aggregate are hidden as 404 without count or error leak. | I,E | REQ_F41114.1.1–8.2 | TODO |
| HR-BASE-014 | State | Draft/published roster, pending/approved leave, payroll draft/calculated/approved/released/reversed, recruitment and review states reject forbidden transitions with reason. | U,I,E | REQ_F41114.3.1–8.2 | TODO |
| HR-BASE-015 | Relation | Inactive SYS unit/user, terminated employee, approved payroll and enrolled training block invalid new action but preserve historical links. | I,E | REQ_F41114.1.2–8.2 | TODO |
| HR-BASE-016 | Consistency | Concurrent last training seat, leave approval, payroll release and candidate conversion produce one committed result and no duplicate side effect. | I,E | REQ_F41114.4.2–6.2,8.1 | TODO |
| HR-BASE-017 | Consistency | Later attendance/leave/policy/manager change does not rewrite frozen payroll, balances or reviews; refreshed cutoff shows adjustment explicitly. | I,E | REQ_F41114.2.2–5.2,7.2 | TODO |
| HR-BASE-018 | Error | 400/409/422/500 with traceId, network loss, FI/SYS failure and timeout show localised reason and key lookup for unknown outcome without personal data. | I,E | REQ_F41114.1.1–8.2 | TODO |
| HR-BASE-019 | File | Import is not proposed; empty/oversized export, unsafe spreadsheet cells and missing download right preserve scope, escaping and confidentiality. | I,E | REQ_F41114.4.2,8.2 | TODO |
| HR-BASE-020 | UI | Three languages and light/dark show normal, empty, validation, server error, success, partial and denied states; message and data agree. | E | REQ_F41114.1.1–8.2 | TODO |
| HR-BASE-021 | Audit | Actor, company, policy/source version, approval, correction, reason and outcome are traceable without logging salary, identity or bank fields. | I,E | REQ_F41114.1.1–8.2 | TODO |

Traceability: employee/structure 1.1–2.2 → BR-HR-001–004,015–018 → HR-BASE-001,007–009,012–015,017–018,020–021; attendance 3.1–3.2 → BR-HR-005–006,015–018 → 002,007–008,012–018,020–021; payroll 4.1–4.2 → BR-HR-006,008–011,015–018 → 003,007,009–014,016–021; leave 5.1–5.2 → BR-HR-007,015–018 → 004,007–008,012–018,020–021; recruitment 6.1–6.2 → BR-HR-012,015–018 → 005,007,009,012–016,018,020–021; performance 7.1–7.2 → BR-HR-013,015–018 → 006–007,012–015,017–018,020–021; training 8.1–8.2 → BR-HR-014–018 → 006–007,010–016,018–021.
