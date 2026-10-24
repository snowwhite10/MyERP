# PP — Test plan

## WU-PP-000d scenarios (design only)

`myerp-test-scenarios` categories are covered below. U = unit, I = integration against real SQL and owner contracts, E = Playwright against authorised HTTP. The future implementation slices write failing product tests before code. Every E case must assert both the translated visible message/count/severity and the independently read persisted PP state plus confirmed INV movement. All cases are TODO; this design unit adds no executable PP product test.

| ID | Class | Scenario and assertion | Level | Requirements | Status |
|---|---|---|---|---|---|
| PP-BASE-001 | Normal | Create and approve a BOM revision with two valid components; select it by site/date and preserve prior revision. | U,I,E | REQ_F4113.1.1–10 | TODO |
| PP-BASE-002 | Normal | Approve routing, create MPS plan, run MRP and release one work order with frozen source revisions. | U,I,E | REQ_F4113.2.1–6,5.1–8.1 | TODO |
| PP-BASE-003 | Normal | Approve requisition, receive INV Posted issue confirmation, approve completion, receive INV Posted inbound confirmation; quantities and BI source reconcile. | I,E | REQ_F4113.3.1–4.6,8.1 | TODO |
| PP-BASE-004 | Normal | Record shop-floor actual time and show capacity load without changing INV or GL. | I,E | REQ_F4113.9.1 | TODO |
| PP-BASE-005 | Boundary | Empty parent/components, zero/negative quantity, excess decimal scale, conversion overflow and maximum lines fail with field/line reason; valid maximum exact. | U,I,E | REQ_F4113.1.2,3.2,7.1 | TODO |
| PP-BASE-006 | Boundary | Effective-date overlap, leap day, year boundary, planning horizon maximum, zero-duration operation and capacity overflow have explicit results. | U,I,E | REQ_F4113.1.4,5.1–7.1 | TODO |
| PP-BASE-007 | Duplicate/conflict | Duplicate BOM/group code including soft-deleted code and direct/transitive cycle are rejected; concurrent approvals cannot create two effective revisions. | U,I,E | REQ_F4113.1.2–10 | TODO |
| PP-BASE-008 | Duplicate/conflict | Concurrent edit/release/approval yields 409; same MPS/MRP source key or work-order release is idempotent. | I,E | REQ_F4113.2.2–4,6.1–8.1 | TODO |
| PP-BASE-009 | Duplicate/conflict | Two issue/receipt retries or unknown outcome reconcile by permanent key to one INV Posted movement, never double stock. | I,E | REQ_F4113.3.4,4.4 | TODO |
| PP-BASE-010 | Partial result | Requisition with one confirmed line and one shortage reports confirmed/open counts and line reason; all skipped, all failed and zero-change cases are warnings/errors, not success. | I,E | REQ_F4113.3.1–4 | TODO |
| PP-BASE-011 | Partial result | Completion with accepted, rejected and pending quantities reports each separately; all rejected produces no stock increment or success toast. | I,E | REQ_F4113.4.1–5 | TODO |
| PP-BASE-012 | Partial result | Missing SD/PUR/INV source or broken manifest makes MPS/MRP/BI source unavailable/incomplete, never a credible partial plan or zero demand. | I,E | REQ_F4113.6.1–7.1 | TODO |
| PP-BASE-013 | Permission | Anonymous 401, missing per-action PP or SYS approval right 403; revoked role denies next page/confirm and invalidates cached content. | I,E | REQ_F4113.1.1–9.1 | TODO |
| PP-BASE-014 | Permission | Cross-company/site order, item, warehouse or source ID is hidden as 404; no source totals or existence leak. | I,E | REQ_F4113.1.1–9.1 | TODO |
| PP-BASE-015 | State | Draft, pending, rejected, approved, released, completed, cancelled and reversed states allow only documented transitions; no draft contributes Posted stock. | U,I,E | REQ_F4113.1.1–9.1 | TODO |
| PP-BASE-016 | Relation | Used BOM/routing revision and referenced group cannot be deleted; inactive MD item remains in saved history but cannot enter new plan. | I,E | REQ_F4113.1.3–6,5.1,8.1 | TODO |
| PP-BASE-017 | Error | 400/409/422/500 with traceId, timeout and network loss display translated specific reason; unknown INV result remains pending until reconciled. | I,E | REQ_F4113.1.1–9.1 | TODO |
| PP-BASE-018 | File | Import is N/A for this baseline because no PP upload route is proposed. Export is N/A until a PP export requirement is specified. | - | REQ_F4113.1.1–9.1 | N/A |
| PP-BASE-019 | UI | Three languages and light/dark states show empty, loading, denied, validation, partial, failure and success with disabled reasons. | E | REQ_F4113.1.1–9.1 | TODO |
| PP-BASE-020 | Audit | All mutations record actor, company, prior/current state and source key; read/report actions leave PP, INV and GL business rows and versions unchanged. | I,E | REQ_F4113.1.1–9.1 | TODO |

Traceability: BOM/group 1.1–10 → PP-BASE-001,005,007,013–20; plan/MPS/MRP 2.1–6 and 6.1–7.1 → 002,005–8,012–15,017,019–20; requisition 3.1–6 → 003,009–10,013–17,019–20; inbound 4.1–6 → 003,009,011,013–17,019–20; routing/order/capacity 5.1,8.1–9.1 → 002,004,006,008,013–17,019–20. No executable PP test or product acceptance is claimed.
