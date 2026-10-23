# SD test plan — WU-SD-000 baseline

Status: planned; no product tests or SQL assertions are claimed as passing. `U` = unit, `I` = real SQL and authorised HTTP integration, `E` = Playwright UI/API agreement. Each implementation unit first writes failing automated tests for its allocated cases, then implements and records results. E2E checks both visible message/state and persisted API/list data.

| ID | Category | Scenario and expected result | Layer | REQ_F |
|---|---|---|---|---|
| SD-BASE-001 | Normal | Create quotation, approve through SYS and convert selected lines once; saved order retains source snapshot and neither draft changes INV. | I,E | REQ_F4112.1.2, REQ_F4112.1.3, REQ_F4112.2.2 |
| SD-BASE-002 | Normal | Create, edit, submit, approve, reject and cancel order through permitted states; approval history and document number persist. | I,E | REQ_F4112.2.1–6 |
| SD-BASE-003 | Normal | Draft delivery from approved order, SYS final approval posts exactly one saved INV outbound in same company; UI count/status matches ledger. | I,E | REQ_F4112.3.1–4 |
| SD-BASE-004 | Normal | Price/discount and output tax use business-date rule and currency precision; invoice covers posted balance; return records distinct source. | U,I,E | REQ_F4112.4.1, REQ_F4112.5.1, REQ_F4112.7.1 |
| SD-BASE-005 | Boundary | Empty customer, zero lines, 1001 lines, zero/negative quantity, more than six decimals and decimal overflow are rejected without partial rows. | U,I,E | REQ_F4112.1.2, REQ_F4112.2.2, REQ_F4112.3.2 |
| SD-BASE-006 | Boundary | Month/year/leap-day effective rate, expiry and currency precision boundaries yield saved, repeatable totals. | U,I | REQ_F4112.1.2, REQ_F4112.4.1 |
| SD-BASE-007 | Boundary | Delivery exactly equal to remaining order quantity succeeds; one smallest unit above fails; serial/batch and warehouse validation comes from INV. | I,E | REQ_F4112.3.2, REQ_F4112.3.4 |
| SD-BASE-008 | Duplicate/conflict | Same company document code cannot repeat even after soft delete; two concurrent conversions or deliveries yield one winner and a clear 409/422. | I,E | REQ_F4112.1.3, REQ_F4112.2.2, REQ_F4112.3.2 |
| SD-BASE-009 | Duplicate/conflict | Stale edit/submit/delete version returns 409 and preserves all saved lines, statuses and credit/stock. | I,E | REQ_F4112.2.3–6, REQ_F4112.3.3–6 |
| SD-BASE-010 | Partial result | Document writes are atomic: invalid middle line yields 0 new rows, 0 stock moves and no success toast; export aborts whole file on changed page. | I,E | REQ_F4112.2.2, REQ_F4112.3.2 |
| SD-BASE-011 | Partial result | All skipped and partial-success cases are not applicable to these atomic document commands; a future bulk import/action must add counts, row reasons and tests before delivery. | U | REQ_F4112.2.2, REQ_F4112.3.2 |
| SD-BASE-012 | Permission | Anonymous 401, missing action permission 403, other-company or out-of-scope document 404; CREATE/EDIT-only users access their own form options without VIEW. | I,E | REQ_F4112.1.1–2, REQ_F4112.2.1–3, REQ_F4112.3.1–3 |
| SD-BASE-013 | State | Draft, Pending, Rejected, Approved, Posted and Cancelled actions follow matrix; posted delivery and approved invoice cannot be directly edited/deleted. | I,E | REQ_F4112.2.3–6, REQ_F4112.3.3–6, REQ_F4112.5.1 |
| SD-BASE-014 | Related | Deleted/disabled customer, item, currency, tax, warehouse or quoted source is shown with reason; write revalidation rejects it. In-use documents prevent master deletion through usage contracts. | I,E | REQ_F4112.1.2, REQ_F4112.2.2, REQ_F4112.3.2 |
| SD-BASE-015 | Error | 400 invalid field, 409 stale version, 422 status/credit/stock/reference, 500 with traceId, timeout and network loss show translated reasons and leave no false success. | I,E | REQ_F4112.2.2–6, REQ_F4112.3.2–6 |
| SD-BASE-016 | Error | INV posting failure rolls back SYS decision and SD Posted state; retry after correction posts once; cross-company same numeric ID does not collide. | I | REQ_F4112.3.4 |
| SD-BASE-017 | Credit | Missing FI/AR exposure contract blocks approval; limit excess blocks with amount/reason; authorised override is audited once. | U,I,E | REQ_F4112.2.4, REQ_F4112.6.1 |
| SD-BASE-018 | Invoice/return | Concurrent issue/return cannot exceed uninvoiced or unreturned delivery quantity; original Posted delivery remains immutable. | I,E | REQ_F4112.5.1, REQ_F4112.7.1 |
| SD-BASE-019 | File | No upload/import is in this baseline: empty, malformed, oversized, wrong encoding, missing/extra columns are not applicable. CSV export instead tests zero rows, formula escaping, 10,000 line limit and interrupted download. | I,E | REQ_F4112.2.1 |
| SD-BASE-020 | UI | Each interface has normal, empty, validation, server error and success states in three languages and two themes; error/success text agrees with persisted records. | E | REQ_F4112.1.1, REQ_F4112.2.1, REQ_F4112.3.1 |

Traceability: BR-SD-001–003 → 001, 008, 012, 014; BR-SD-004 → 002, 017; BR-SD-005–007 → 003, 007–010, 013, 016, 018; BR-SD-008–009 → 004, 006, 017–018; BR-SD-010 → 005, 010–015, 019–020. Later work units replace broad baseline scenarios with feature-level case IDs and results without weakening these checks.
