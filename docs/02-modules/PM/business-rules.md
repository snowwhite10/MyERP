# PM — Maintenance business rules

## WU-PM-000d baseline

| Rule | Design invariant |
|---|---|
| BR-PM-001 | PM owns equipment code/status, plan version, due occurrence, work order and spare request. MD location/item, INV stock/movements and FI asset/ledger remain owner data; no PM service writes their tables. |
| BR-PM-002 | Equipment code is unique within company including soft-deleted history. A FI asset reference is optional and verified via FI Application; equipment retirement does not dispose the FI asset or change its depreciation. |
| BR-PM-003 | Equipment state is Active, OutOfService or Retired. An open order or unresolved INV reservation/issue prevents retirement; historical references and audit are retained. |
| BR-PM-004 | Each approved plan version freezes trigger kind, interval, timezone, effective range and checklist. Calendar due keys use local scheduled date; meter keys use threshold sequence and a verified reading with unit. Same equipment/plan/occurrence key is permanent. |
| BR-PM-005 | Plan revisions apply to future occurrences only. Backdated meter readings and schedule changes cannot silently alter released orders; overlapping active trigger rules are rejected. |
| BR-PM-006 | A generated occurrence and its order are committed atomically in PM. Concurrent schedulers and retry use the same permanent key and payload fingerprint; same replay changes zero rows, changed payload conflicts. |
| BR-PM-007 | Orders move Draft → PendingApproval → Released → InProgress → Completed or Cancelled, with explicit rejection back to Draft. SYS owns final approval decision; PM state changes in the same transaction as final approval. Closed records remain immutable apart from separately audited correction events. |
| BR-PM-008 | Work order actuals record labour duration, downtime, cause, action and checklist evidence. Negative durations and overlapping downtime for the same equipment conflict; completion requires required checklist items and owner outcomes. |
| BR-PM-009 | PM requests a spare in a declared MD unit. INV owns availability, reservation, issue, return, location, lot/serial and posted stock. PM stores owner movement IDs, versions and quantities and never updates `inv_` balances. |
| BR-PM-010 | Requested, reserved, issued, returned and net consumed have explicit units and precision; `returned <= issued` and `net consumed = issued - returned`. Partial issue remains pending. INV confirmed issue or return is required before PM labels stock consumed or returned. |
| BR-PM-011 | Every owner command has company/order/line/type permanent key and fingerprint. Same-key same-payload replay yields the saved result; changed payload is 409. Unknown timeout remains PendingOwner until key lookup confirms commit or safe retry. |
| BR-PM-012 | PM labour and spare estimates are operational figures. FI alone determines capitalisation, maintenance expense and posted cost via its public contract and owns fixed asset book and GL. PM completion cannot claim FI posting without confirmation. |
| BR-PM-013 | Every read and command rechecks company, branch/data scope and action permission. Cross-company IDs are hidden as 404; absent action right is 403. Download and later owner callback also recheck authority. |
| BR-PM-014 | Batch generation and spare operations expose changed, existing, skipped, failed and pending counts, with row reasons. All skipped or zero change uses warning; a later owner refusal cannot retain a success claim. |
| BR-PM-015 | Reports/history use committed PM events and confirmed INV/FI outcomes with named independent owner cutoffs, stable paging and frozen source IDs. Missing owner providers are incomplete, not zero. |
| BR-PM-016 | Stale entity or owner versions are 409, invalid state/quantity/calendar/meter input is 422, malformed input is 400. Retired/soft-deleted MD or FI records stay in history but cannot seed new equipment, orders or spare requests. |

Before implementation, an ADR must choose the meter source and transaction protocol for INV reservation/issue/return and FI cost confirmation. An unavailable owner yields a pending or unavailable result, never a fabricated posting.
