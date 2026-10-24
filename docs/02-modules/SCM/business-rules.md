# SCM — Supply chain business rules

## WU-SCM-000d baseline

| Rule | Design invariant |
|---|---|
| BR-SCM-001 | SCM owns forecast, consignment/event evidence and supplier proposals. It reads SD, PP, INV, PUR, MD and SYS through owner Application contracts, never their tables. SCM never posts inventory or edits a purchase order. |
| BR-SCM-002 | Forecast grain is company, item, location, unit and closed time bucket; all input owner IDs, versions and committed cutoffs are retained in a source manifest. |
| BR-SCM-003 | A missing, stale or unauthorised owner source is unavailable, never zero. Forecast runs expose complete, partial and unavailable source status with named reasons. |
| BR-SCM-004 | Forecast method, parameters and overrides are versioned. Publishing requires independent SYS approval; published versions and historical calculations are immutable. |
| BR-SCM-005 | Confirmed SD orders, PP released plans and INV posted movements are different measures; deduplication follows owner permanent source keys and unit conversion policy. |
| BR-SCM-006 | Consignment planning does not confirm PUR receipt, SD shipment or INV movement. Their confirmed states are queried independently and shown with owner cutoff. |
| BR-SCM-007 | Carrier event key is unique per carrier and consignment, including retired history. Duplicate payload is idempotent; changed payload is 409. Out-of-order events append evidence without rewriting history. |
| BR-SCM-008 | Delivered quantity is supported by evidence and compared with owner confirmed quantities. Shortage, damage, late delivery and unknown owner result remain explicit exceptions. |
| BR-SCM-009 | Carrier proof and attachments have size, MIME/content, malware and access controls; download rechecks company, order and action permission. Retention is versioned policy. |
| BR-SCM-010 | Supplier invitation binds a SYS account to one MD supplier, company, effective period and allowed PUR order set. Expiry or revocation takes effect for all later reads, downloads and commands. |
| BR-SCM-011 | A supplier proposal never updates PUR directly. PUR accepts or rejects through its Application contract using a permanent proposal key; timeout stays PendingOwner until lookup confirms the outcome. |
| BR-SCM-012 | Every list, export and batch reports changed, skipped, failed and pending counts with per-row reasons. Zero changes and all skipped are warnings, not success. |
| BR-SCM-013 | Action, company, supplier and data scope checks precede existence disclosure. Other-company or other-supplier IDs return 404; separate rights govern review, approval, export and download. |
| BR-SCM-014 | Stable committed source membership covers forecast comparisons, transport reconciliation and collaboration pages/exports. Concurrent publish, event, scope revocation and proposal acceptance have one confirmed effect. |
| BR-SCM-015 | Invalid shape is 400, stale version/duplicate 409, invalid state 422 and server error 500 with traceId. Timeout gives unknown outcome and key lookup. Audit records actor, source, version, action and confirmed outcome without leaking prices or personal data. |

Implementation ADRs must settle owner DTO and cutoff semantics, forecast method and unit conversion, transport provider/event verification, evidence handling, supplier onboarding and PUR proposal compensation before executable acceptance.

## WU-SCM-001d forecast source decisions

| Rule | Design invariant |
|---|---|
| BR-SCM-016 | A forecast request fixes company, item, site/warehouse, canonical item base UOM, closed monthly bucket range and three independent owner cursors. Owner reads use committed rows only and stable `(owner, company, source type, source ID, line ID, version)` membership. A wall-clock timestamp alone is insufficient to replay membership. |
| BR-SCM-017 | SD's future read contract must expose Approved order-line quantity by saved order business date and site with cancellation/revision state. It must supply immutable committed cursor and source lineage; pending, rejected, deleted and voided demand is excluded. SD's existing Contracts project has no such reader, so its current status is `Unavailable:ContractMissing`. |
| BR-SCM-018 | PP's future read contract must expose Released production demand with plan ID/version, bucket and site. PP has no backend project today, so its current status is `Unavailable:OwnerMissing`. PP is an overlay, never blindly added to SD demand. |
| BR-SCM-019 | INV's future read contract must expose Posted movements with stable movement/line keys, direction, business date, site, item, base quantity and replayable committed cursor. Current `IStockBalanceQuery` is live on-hand and `IInventoryValuationQuery` is provisional; neither is a historic demand feed. Current status is `Unavailable:ContractMissing`. |
| BR-SCM-020 | Canonical quantity is the MD item base UOM. Conversion must return exact source UOM, target UOM, factor/path identity and version or a frozen equivalent plus rounded six-place result. Current `IUomConverter` returns only a number; it cannot prove a later replay uses the same conversion. Missing path, changed base UOM, overflow or precision loss beyond policy marks the row unavailable with a reason. SCM never reads MD tables. |
| BR-SCM-021 | Method `SCM-SD-MA3-v1` computes each future monthly bucket from the arithmetic mean of the preceding three **complete** SD monthly buckets for the same company/item/site, rounding once to six decimal places away from zero. Zero demand is valid only when SD explicitly certifies a complete empty bucket. Fewer than three complete buckets, unavailable SD, mixed UOM or missing conversion produce `InsufficientSource`, never a guessed value. PP/INV are comparison overlays and cannot change the baseline. Method ID, parameter `window=3`, rounding policy and all input bucket totals are saved. |
| BR-SCM-022 | Source availability is `Complete`, `Partial`, or `Unavailable`, with machine reason and per-row reason. A Partial SD bucket cannot publish a baseline; optional PP/INV Partial remains visible in comparison. A failed owner call, timeout or absent reader is never represented as zero. A rerun uses the saved manifest and cursor or creates a new version; an approved version never recalculates in place. |
| BR-SCM-023 | A run binds three owner cursors and a conversion snapshot before calculation. Owners must either return all pages for that exact cursor with a stable ordered next token or reject it as expired; mixed cursors invalidate the run. Persist a canonical manifest digest and committed row count per owner plus the accepted source rows needed for replay. Query/export reads the saved version, not live owner heads. |

See `ADR-072` for the method and cutoff decision. These are design rules; source readers and calculation are deferred to 001a and prerequisite owner slices.

## WU-SCM-001e owner replay and correction policy

| Rule | Design invariant |
|---|---|
| BR-SCM-024 | Each owner issues a cursor only after the source transaction commits. A cursor fixes membership, values, status and ordering for its authorised company/filter; every page carries the same cursor, manifest count and digest. A new read may acquire a later cursor but cannot substitute it within a run. |
| BR-SCM-025 | SD captures Approved order-line demand, including committed revisions/cancellations as of cursor issuance. It certifies each requested closed month as Complete, including an explicit empty month. Approval date is evidence; order business date determines the demand bucket. Corrections never rewrite an earlier cursor. |
| BR-SCM-026 | INV exposes only Posted movement history, including reversals as separate lineage entries. On-hand, provisional valuation, pending movements and mutable current balances are not historic movement evidence. The movement business date fixes the comparison bucket; commit order fixes visibility. |
| BR-SCM-027 | MD returns base UOM and the exact committed conversion path/factors with stable evidence identity, policy version and unrounded decimal calculation. SCM stores this evidence and its six-place result per accepted row. A later factor or base-unit correction requires a new run; missing/ambiguous path is a row failure, never quantity zero. |
| BR-SCM-028 | PP must first establish its backend owner boundary. Its Released plan demand reader is optional for `SCM-SD-MA3-v1`, returns plan/version/line lineage and follows the same committed cursor protocol. Absence remains `OwnerMissing`; SCM must not infer PP demand from SD or INV. |
| BR-SCM-029 | Owners retain replay data and cursor tokens for at least 90 days after issuance. A cursor is unusable once expired, revoked by access change, or lacking exact replay evidence; no implicit head refresh is allowed. SCM must finish and persist its manifest while cursors are valid. Published versions retain the accepted manifest and conversion evidence under SCM retention policy independently of owner cursor expiry. |
| BR-SCM-030 | Backdated approval, void, reversal or factor correction creates a later owner commit and later cursor. It never changes an existing SCM draft snapshot or published version. A comparison against corrected data creates a new SCM version with new source cutoffs; the old version remains traceable and labelled with its original cutoff. |
| BR-SCM-031 | Owner access checks run on every initial and replay page. Other-company or out-of-scope requests reveal no rows, counts, digest or cursor validity. Cross-page failure marks that owner Unavailable and prevents SD publication; optional overlays remain named Partial/Unavailable. |
