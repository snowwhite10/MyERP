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

## WU-PP-001d Released demand reader: test-first scenarios

All cases trace to REQ_F4113.2.7, BR-PP-015–021 and SCM-OWN-008–012/014/016. U = unit contract/policy; I = two independent real SQL connections. They remain pending WU-PP-001c: PP has no backend and SQL Server is stopped. No executable PP product test is claimed by this design unit.

| ID | Category | Scenario and expected result | Level | Status |
|---|---|---|---|---|
| PP-DEMAND-001 | Normal | Commit two Released plan lines in different months; first page returns saved raw UOM/quantity, lineage, complete bucket certificates, fixed cursor, total and digest without INV/GL writes. | I | Pending |
| PP-DEMAND-002 | Normal | Read all pages twice with one cursor while another connection releases a plan; exact rows, order, count, digest and empty buckets remain identical. | I | Pending |
| PP-DEMAND-003 | Boundary | Valid range with no Released lines returns Complete with zero rows and every requested month explicitly empty; invalid/missing company, from >= to and unclosed month reject. | U,I | Pending |
| PP-DEMAND-004 | Boundary | Page sizes 1 and 500 and final short page work; 0/501 reject; leap day, month end and year rollover use saved business month rather than release timestamp. | U,I | Pending |
| PP-DEMAND-005 | Boundary | Six-place decimal quantity is exact; zero, negative, excess scale or overflow cannot enter Released evidence. Missing/unknown site never becomes zero. | U,I | Pending |
| PP-DEMAND-006 | Duplicate/conflict | Same release source key is idempotent, changed payload conflicts, and simultaneous release of one plan/version yields one committed revision and one visible line. | I | Pending |
| PP-DEMAND-007 | Duplicate/conflict | Reused page token with different filter/cursor, malformed or foreign cursor cannot disclose rows or switch membership; no silent head refresh. | I | Pending |
| PP-DEMAND-008 | Partial result | Lost/corrupt middle page, missing row or digest mismatch gives ReplayUnavailable/Unavailable rather than partial Complete; retry is exact or still unavailable. | I | Pending |
| PP-DEMAND-009 | Partial result | All buckets empty remains a valid Complete read; batch partial success, all skipped and all failed are inapplicable because the reader makes no mutation. | I | Pending |
| PP-DEMAND-010 | Permission | Anonymous or missing reader right is denied; other-company, out-of-scope site, and site-scope revocation between pages reveal no rows, count, digest or cursor validity. | I | Pending |
| PP-DEMAND-011 | State | Draft, Pending, Rejected, Approved but unreleased, cancelled, deleted and uncommitted releases contribute no active demand; only committed Released enters a new cursor. | I | Pending |
| PP-DEMAND-012 | State/conflict | Backdated release, amendment and cancellation in a second connection produce a later cursor and correction lineage once; old cursor remains byte-equivalent. | I | Pending |
| PP-DEMAND-013 | Related | Later MD item/UOM/site deactivation does not rewrite saved PP identity; missing conversion evidence is named for SCM, and PP never reads MD tables directly. | I | Pending |
| PP-DEMAND-014 | Error | Invalid input, permission loss, scoped ID, conflict, invalid transition, internal failure with traceId and timeout have specific results; timeout never means zero demand. | U,I | Pending |
| PP-DEMAND-015 | File | Import/upload/export is inapplicable: this is an internal paged reader without file operations. | - | N/A |
| PP-DEMAND-016 | Retention/audit | Cursor replays at least 90 days, then returns CursorExpired; access and release mutations are audited without changing existing cursor evidence. | I | Pending |
| PP-DEMAND-017 | UI | No PP screen is in scope; future SCM E2E must assert translated OwnerMissing/Partial/empty status and compare visible rows with saved manifest and PP source evidence. | E | Deferred to WU-SCM-001b |

Coverage: normal 001–002; boundaries 003–005; duplicate/conflict 006–007,012; partial/zero 008–009; permission 010; state 011–012; related records 013; errors 014; files 015; retention/audit 016; UI 017. The reader makes no batch writes or file operations.
