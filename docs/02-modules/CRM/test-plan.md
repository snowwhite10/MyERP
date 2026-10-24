# CRM — Test plan

## WU-CRM-000d scenarios (design only)

`myerp-test-scenarios` categories apply. U = unit, I = real SQL integration with owner Application contracts, E = authorised Playwright. Implementation units must first write failing product tests. All cases below are TODO; this design has no executable CRM product acceptance. Every E case must compare translated visible message, severity, counts and row reasons with independently read saved CRM and MD/SD/provider outcomes.

| ID | Class | Scenario and assertion | Level | Requirements | Status |
|---|---|---|---|---|---|
| CRM-BASE-001 | Normal | Create and edit lead; qualify against confirmed MD customer and view conversion history and audit. | U,I,E | REQ_F41113.1.1–1.3 | TODO |
| CRM-BASE-002 | Normal | Create opportunity, progress allowed stages, link confirmed SD quotation/order and win or lose with reason. | U,I,E | REQ_F41113.2.1–2.3 | TODO |
| CRM-BASE-003 | Normal | Record scoped interaction on each target type, view stable timeline and follow-up. | U,I,E | REQ_F41113.3.1–3.2 | TODO |
| CRM-BASE-004 | Normal | Approve campaign, freeze audience, send eligible recipients and attribute a response exactly once. | U,I,E | REQ_F41113.4.1–4.3 | TODO |
| CRM-BASE-005 | Normal | Create, assign, pause, resolve and reopen ticket; reproduce SLA from events. | U,I,E | REQ_F41113.5.1–5.3 | TODO |
| CRM-BASE-006 | Boundary | Empty and max-length fields, maximum page/batch size, zero/negative/precision overflow value, month/year/leap-day, timezone and business-calendar edges return field reasons. | U,I,E | REQ_F41113.1.1,2.1,3.1,4.1–4.2,5.1–5.2 | TODO |
| CRM-BASE-007 | Duplicate/conflict | Duplicate lead code including soft-deleted code, duplicate contact candidate and concurrent conversion cannot create duplicate MD link or silently merge. | U,I,E | REQ_F41113.1.1–1.3 | TODO |
| CRM-BASE-008 | Duplicate/conflict | Stale entity/owner version yields 409; same permanent key and payload replays without change, changed payload conflicts for conversion, send and callback. | I,E | REQ_F41113.1.2,2.2,4.2–4.3,5.2 | TODO |
| CRM-BASE-009 | Partial result | Mixed eligible, withdrawn-consent, duplicate, provider-failed and pending recipients reconcile counts and row reasons with actual delivery records. | I,E | REQ_F41113.4.2–4.3 | TODO |
| CRM-BASE-010 | Partial result | All skipped, all failed and zero-row campaign runs show warning/error, no success claim or new delivered count; replay remains unchanged. | I,E | REQ_F41113.4.2–4.3 | TODO |
| CRM-BASE-011 | Permission | Anonymous 401, missing action 403, revoked approval/export/download denied; a CRM right cannot substitute for MD/SD or consent access. | I,E | REQ_F41113.1.1–5.3 | TODO |
| CRM-BASE-012 | Permission | Other-company/branch or outside-scope lead, contact, opportunity, SD link, campaign recipient and ticket are hidden as 404 without count or note leak. | I,E | REQ_F41113.1.1–5.3 | TODO |
| CRM-BASE-013 | State | Draft/qualified/disqualified lead, opportunity stages, draft/approved/active/completed campaign and ticket states enforce transitions and explain disabled action. | U,I,E | REQ_F41113.1.2,2.2,4.1–4.2,5.2 | TODO |
| CRM-BASE-014 | Relation | Inactive or soft-deleted MD customer and cancelled SD quotation/order remain historical but cannot seed new work; open linked work prevents destructive unlink. | I,E | REQ_F41113.1.3,2.1–2.3,3.2,5.1–5.3 | TODO |
| CRM-BASE-015 | Consistency | Concurrent stage change, campaign approval/sending, consent withdrawal, ticket SLA pause/reopen and callback preserve one state and no duplicate side effect. | I,E | REQ_F41113.2.2,4.1–4.3,5.2–5.3 | TODO |
| CRM-BASE-016 | Consistency | Later MD merge or SD cancellation does not rewrite frozen timeline/pipeline/export; refreshed source set exposes changed link with explicit cutoff. | I,E | REQ_F41113.1.3,2.3,3.2,5.3 | TODO |
| CRM-BASE-017 | Error | 400/409/422/500 with traceId, network loss and timeout show localised reason; key lookup resolves unknown MD conversion or delivery outcome. | I,E | REQ_F41113.1.1–5.3 | TODO |
| CRM-BASE-018 | File | Import is not proposed in this baseline; empty/oversized export and unsafe spreadsheet cells preserve scope, escaping and download permission. | I,E | REQ_F41113.2.3,3.2,4.2,5.3 | TODO |
| CRM-BASE-019 | UI | Three languages and light/dark show normal, empty, validation, server error, success, partial and denied states; messages agree with saved CRM/owner data. | E | REQ_F41113.1.1–5.3 | TODO |
| CRM-BASE-020 | Audit | Actor, company, source version, permanent key, consent decision, stage, approval and SLA event are traceable; preview never mutates MD/SD. | I,E | REQ_F41113.1.1–5.3 | TODO |

Traceability: leads 1.1–1.3 → BR-CRM-001–004,015–018 → CRM-BASE-001,006–008,011–014,016–017,019–020; opportunities 2.1–2.3 → BR-CRM-005–007,015–018 → 002,006,008,011–017,019–020; interactions 3.1–3.2 → BR-CRM-008,015–017 → 003,006,011–012,014,016–020; campaigns 4.1–4.3 → BR-CRM-009–012,015–017 → 004,006,008–013,015,017–020; tickets 5.1–5.3 → BR-CRM-013–018 → 005–006,008,011–017,019–020.
