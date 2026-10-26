# SD optimization log

## 2026-10-27T05:49:46+08:00 — WU-SD-004a2b7 selected-row guard review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| A correct ordered lock plan could still receive a truncated saved-row query. | The prior pure plan knew each selected row but had no independently read persisted row count or line identity. | Added a count comparison and unique positive persisted line-ID check before reusing the existing ordered lock plan; the SQL caller must read both under one serialized, company-scoped transaction. | A missing, repeated or foreign selected row is rejected before lock planning; SQL isolation and actual locks remain for WU-SD-004a2b. |

The helper allocates only one ID set and one projection array for at most 1000 lines. It introduces no table, API, UI or cross-module access. Focused tests first failed at compile time and then passed; full offline verification is recorded in progress.md. SQL concurrency and smoke cannot run while the service remains stopped.

## 2026-10-24T11:53:43+08:00 — WU-SD-005d pricing design review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| A unique interval start would allow overlapping prices. | Different start dates can cover the same business day, and nullable default customers need one logical key. | BR-SD-026 and schema plan require serialized same-key writes plus overlap check; SD-PRICE-008/009 cover boundary and two-writer cases. | A quote selects at most one rule per specificity. |
| A displayed price can become stale before document save. | Quote lookup and save are separate requests. | BR-SD-027/030 make quotes advisory and require transactional re-resolution/version check; SD-PRICE-010 covers 409. | Obsolete prices cannot silently persist. |
| A manual price could bypass the ordinary discount ceiling. | A document CREATE grant alone does not authorise commercial override. | BR-SD-028 requires an additional OVERRIDE action and reason, with actor/time audit. | Price exceptions are explicit and traceable. |
| Rule edits could recalculate an Approved document. | A live rule lookup would discard the commercial decision made at save. | BR-SD-029 and schema plan retain rule provenance and rounded line/header snapshots; SD-PRICE-020 covers historical stability. | Existing documents remain reproducible. |

This design unit changed no product code, table, endpoint or UI. DocGen tests passed 7/7 and `git diff --check` passed. SQL service is stopped; real SQL/HTTP and Playwright acceptance belong to 005a/b.

## 2026-10-24T07:43:04+08:00 — WU-SD-004d outbound design review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| Concurrent deliveries could overreserve one order line. | A balance shown by the UI is stale as soon as another delivery submits. | BR-SD-022 requires ascending order-line locks and aggregate decimal balance checks at submit and final approval, counting the current reservation once. | Concurrent writes serialize; no negative order balance. |
| A zero-step workflow could leave a Posted SD document without an INV issue. | SYS does not call the result handler for immediate approval. | BR-SD-024 requires the submit service to execute the same saved-line posting path in its transaction. | Immediate and final approval have the same atomic effects. |
| INV's replay protection could be mistaken for business authorisation. | INV accepts identical saved-source replay. | BR-SD-024 rejects a second SD decision on Posted/Rejected before invoking INV. | Replays remain a safety guard, not a route to duplicate approval. |
| Tracking or stock failure could leave an apparent success. | SYS, SD and INV share one decision transaction. | SD-DEL-011/017 assert complete rollback and a specific visible failure, including 500 traceId. | No partial movement or false success message. |

This design unit changed no product code, schema, API or UI. DocGen tests passed 7/7 and `git diff --check` passed. Real SQL, authorised HTTP and Playwright acceptance remain with WU-SD-004a/b/c while SQL is stopped.

## 2026-10-24T07:36:22+08:00 — WU-SD-003a quotation source slice review

The source validator checks company identity before quotation status so a cross-company source does not expose its state or expiry. It reuses the existing quotation lifecycle for Approved, converted and inclusive expiry decisions, avoiding a second transition matrix. The red test failed on the absent validator; nine focused assertions and all 143 architecture tests now pass. This pure check does not lock a quote, enforce source uniqueness or prove a saved order. Debug build has zero warnings/errors; Shared 381/381 and DocGen 7/7 pass. Real SQL and authorised HTTP remain pending because the SQL service is stopped.

## 2026-10-24T07:29:06+08:00 — WU-SD-003d order design review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| A zero-step approval could bypass unavailable credit data. | SYS may return immediate approval without a final callback. | BR-SD-016/018 and API detail require the same FI/AR credit check before any submit/zero-step approval; missing contract returns a named block. | No assumed zero exposure or accidental approval. |
| Deleting or cancelling an order could free a quotation for reuse. | Source uniqueness might otherwise follow active status. | BR-SD-017 and schema plan keep unique quote/line links across soft deletion and cancellation. | One quote converts once under concurrency. |
| A delivery could be ignored by order cancellation. | Order status alone does not describe pending or posted outbound. | BR-SD-019 requires zero active/past delivery and an explicit current-version cancel guard. | Original order and delivery remain traceable. |

No product code, table, endpoint or test was changed by this design unit. DocGen unit tests pass 7/7; `git diff --check` is clean. Real SQL and authorised HTTP remain implementation gates while the SQL service is stopped.

## 2026-10-24T07:26:10+08:00 — WU-SD-002a transition slice review

The transition rule is pure and reads business date only through `IClock`; it does not mutate an Approved quote when expiry passes. Review found that a converted quote needs a separate source-order guard even when its stored state remains Approved, so both void and repeat conversion return a named conflict. Invalid status is checked before expiry to keep the reported reason tied to the attempted action. The test fixture initially triggered analyzer CA1859; its concrete clock type now satisfies the build without weakening a test. No persistence, SYS callback or HTTP behavior is claimed. Debug build has 0 warnings/errors; architecture 120/120, Shared 381/381 and DocGen 7/7 pass.

## 2026-10-24T07:18:32+08:00 — WU-SD-002d quotation design review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| A proposed direct quotation approval path could diverge from SYS decisions. | The baseline named submit but did not pin the zero-step and final-callback behavior. | BR-SD-011 and API detail use `IApprovalWorkflowService` plus `IApprovalResultHandler`, including immediate approval in the submit transaction. | One decision owner and explicit rollback tests. |
| Expiry could be mistaken for a mutable approval state. | Approved status and commercial validity serve different purposes. | BR-SD-012 keeps stored status and computes `isExpired` from `IClock.Today`. | Approval history remains stable while conversion expires on time. |
| Soft deletion could reopen a quotation for duplicate conversion. | Application-only source checks are racy. | BR-SD-013 and schema plan require permanent unique order source links, including deleted orders, and concurrency tests. | One quotation produces at most one order. |
| Planned actions could appear complete before permission and UI IDs exist. | The design baseline listed future routes and interfaces. | Added VOID permission as a future 002a gate and fixed REQ_DS43/REQ_IO allocation at migration/UI delivery. | The current catalog and documents state implementation status accurately. |

No product code, schema or test was changed by this design unit. DocGen unit tests pass 7/7 and `git diff --check` is clean; real SQL, authorised HTTP and Playwright remain delivery gates for 002a/b.

## 2026-10-24T07:15:06+08:00 — WU-SD-001 MD option slice review

The selection contract sits in MD and returns only minimal choice data; SD has no direct MD table dependency. Reviewed filtering for company, soft deletion, active state, customer approval and inclusive tax dates. The query uses stable code/ID ordering and bounded paging; it does not fetch full entities or perform per-row lookups. The red integration test first failed compilation because the service did not exist. Debug build, architecture 83/83, Shared 381/381 and DocGen 7/7 pass. The SQL fixture cannot run while `MSSQL$MSSQLSERVER2022` is stopped, so no data filtering result or HTTP behavior is claimed. A future edit form must apply `SalesActionAccess` before calling MD.

## 2026-10-24T07:11:25+08:00 — WU-SD-001 action scope slice

Reviewed the new access service: it resolves the current company first, then requires the exact requested SAL action from the SYS authorization contract. This prevents a VIEW grant from authorizing EDIT and rejects a company mismatch before a document query. Unit tests cover each grant, missing permission and anonymous context. Future SD queries still need to apply the returned filter before lookup; no SQL or HTTP validation was possible while SQL Server was stopped.

## 2026-10-24T07:07:26+08:00 — WU-SD-001 foundation slice review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| Draft API permissions used `SD.*`, conflicting with `ErpModuleCatalog` prefix `sal`. | Module code and database/API/permission prefix differ for Sales. | Corrected the API plan and permission catalog to `SAL.*`; an architecture test now checks registration and uniqueness. | PermissionCatalog accepts the module; future endpoints can use declared actions. |
| A company boundary error used the module code in its error key. | The new helper and test initially used different prefixes. | Aligned the error with `SAL.DOCUMENT.NOT_FOUND` and reran the focused then full architecture suite. | Cross-company document checks fail closed with a stable code. |

The test feedback loop was `dotnet test --project tests/backend.unit/MyERP.ArchitectureTests/MyERP.ArchitectureTests.csproj`: it first reported the permission prefix conflict, then the error-code mismatch, and finally passed 80/80. No debug instrumentation remains. SQL service is stopped, so no migration or authorised HTTP result is claimed.

## 2026-10-24T07:01:29+08:00 — WU-SD-000 design review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| A delivery might accidentally send sales price as stock cost. | Commercial and inventory lines have different meanings. | BR-SD-006 explicitly uses saved basic-unit quantity/tracking and the existing `SalesOutboundLine` contract, which has no sales price. | INV remains sole owner of outbound valuation. |
| A future order approval might silently proceed without credit exposure. | FI/AR contract is not yet available. | BR-SD-004 and REQ_F4112.6.1 require a specific blocked result until the contract exists; SD-BASE-017 covers it. | No fictitious credit approval. |
| Broad sales Epic cannot be verified in one round. | Quotation, order, delivery, FI and UI have different dependencies. | Backlog splits design, persistence, approval/INV and UI slices; later large slices must split again before coding. | Each next action has a named contract and acceptance gate. |

## WU-SD-002a partial code review (2026-10-24)

| Finding | Cause | Change | Result |
|---|---|---|---|
| The first rounding test expected cents in TWD base amounts. | The test fixture ignored the shared TWD zero-decimal rounding policy. | Use USD base currency for the cent-precision assertion; retain a separate TWD tax/discount assertion. | Architecture suite passes 99/99 and the assertion now matches `Money.Round`. |
| A future quote save could persist over-range numbers or partially valid lines. | Pure calculation and request validation had no sales implementation. | Added field-specific whole-input validation and checked per-line/base-header arithmetic, returning a named overflow error. | The pure slice is independently testable; persistence and transaction tests remain required. |

No product code or schema changed. Review of the new documentation found no secret, generated artifact or cross-module table access requirement. `git diff --check` and DocGen unit tests 7/7 passed; real SQL tests are not applicable to this design-only unit and remain required for implementation units.

## 2026-10-24T07:32:40+08:00 — WU-SD-003a partial code review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| Order amount code could drift from quotation rounding. | The same commercial snapshot policy applies to manual orders and quotations. | Reuse the tested quotation calculation with order-specific inputs/results and translate overflow to `SAL.ORDER.AMOUNT_OVERFLOW`. | Line and header rounding remain aligned while callers receive an order error code. |
| Invalid draft data might reach reference lookup. | No order input boundary existed. | Added field-specific validation for date, customer, currency, lines, precision and remark. | Pure validation is testable before SQL transaction work. |

The helper does not make a document persistent. SQL Server is stopped and this account cannot start it; migration, real SQL, authorised HTTP and full integration regression remain WU-SD-003a gates.

## 2026-10-24T07:39:34+08:00 — WU-SD-003a lifecycle slice review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| Cancellation could be admitted after a delivery exists. | A simple status-only matrix omits delivery balance. | Require the caller to supply whether any active or posted delivery exists; reject cancellation with `SAL.ORDER.HAS_DELIVERY`. | The pure gate covers this invariant; SQL caller must derive the flag under a lock. |
| Rejected orders might be resubmitted without revision. | The business rule requires an edit to return Rejected to Draft. | Permit submit only from Draft and test Rejected-to-Draft edit separately. | The lifecycle cannot skip the revision step. |

Review found no new cross-module access or date/time source. The helper cannot prove persistence, locking, credit, SYS transaction or authorised HTTP. Debug build, architecture 160/160, Shared 381/381 and DocGen 7/7 passed offline; SQL service remains stopped.

## 2026-10-24T11:58:05+08:00 — WU-SD-006d credit design review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| A backdated order could select an obsolete credit limit. | Initial contract used the order business date for a current approval decision. | FI evaluation date is server `IClock.Today` on each submit/final check; saved order date remains solely for commercial amounts. | Backdated documents cannot borrow historical headroom. |
| A Pending order appeared to carry a prior credit reservation. | The first acceptance wording conflated a submitted candidate with Approved commitments. | State Pending orders do not reserve credit; exclude candidate order ID from commitment query and add its saved gross once at decision. | No double count or silent reservation. |
| FI AR and SD uninvoiced commitment could overlap during invoicing. | Ownership shifts at FI posting. | Require the SD commitment release and FI AR increase in the same credit-key transaction, with a saved invoice/order link. | The exposure sum has no transient gap or duplicate. |

This is a design-only unit. No product code, migration, API or UI was changed. DocGen tests 7/7 and `git diff --check` passed; SQL/HTTP and E2E cases remain pending for 006a/b/c because SQL Server is stopped.

## 2026-10-24T12:02:12+08:00 — WU-SD-007d invoice design review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| Partial invoices could drift from the saved order tax after repeated rounding. | Rounding each partial independently loses residual units. | BR-SD-039 allocates cumulative saved net/tax by quantity and gives the final portion the exact remainder; gross is their sum. | All posted portions reconcile to the original commercial snapshot. |
| A retry after timeout could post a second AR item. | Client does not know whether the first transaction committed. | BR-SD-040 requires a unique request key plus payload fingerprint and FI source uniqueness, with a lookup path before retry. | Identical retry reads the existing invoice; changed payload is a conflict. |
| Credit exposure could disappear between SD and FI updates. | Separate transactions could release SD commitment before AR exists. | BR-SD-041 uses the FI credit lock and one SQL transaction for FI posting and SD commitment transfer. | One committed transfer keeps the combined exposure consistent. |

No product code, schema, API or UI changed. DocGen tests 7/7 and `git diff --check` pass. SQL/authorised HTTP and Playwright remain 007a/b gates because SQL Server is stopped.

## 2026-10-24T12:06:00+08:00 — WU-SD-008d return design review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| Return and invoice could both consume the same unbilled delivery quantity. | Their balances are interdependent. | BR-SD-044 requires the same delivery-line lock, separate billed/unbilled allocations and final recheck. | Concurrent operations cannot double allocate quantity. |
| A return receipt could adopt sales price as inventory cost. | SD commercial snapshots do not represent stock cost. | BR-SD-045 requires original outbound movement references and INV-derived reversal cost. | Inventory valuation remains INV-owned and traceable. |
| A mixed return could reduce credit twice or leave stock without a credit adjustment. | Billed and unbilled portions have different exposure owners. | BR-SD-046–047 separate FI credit from SD commitment release and post both with INV/SYS in one transaction. | Failure rolls back the entire return and each exposure component changes once. |
| Partial credits could lose cents through repeated independent rounding. | Each portion would otherwise round separately. | BR-SD-046 uses cumulative quantity with an exact final remainder from saved invoice amounts. | Credits reconcile to the original posted invoice. |

This is a design-only unit: no product code, migration, API, UI or executable feature test was added. DocGen tests 7/7 and `git diff --check` pass. Real SQL, authorised HTTP and Playwright remain 008a/b gates while SQL Server is stopped.
## 2026-10-24T22:43:43+08:00 WU-SD-009d design review

Reviewed the proposed SD demand reader against SCM's frozen-source contract. Risk: a current-state query can change membership between pages, and permission changes can silently leak or trim a manifest. The design now requires committed revision membership, an owner-issued replayable cursor with digest and complete empty buckets, plus permission rechecks on every page. No production code was changed. DocGen unit tests passed 7/7 and `git diff --check` passed; direct document generation remains blocked by another process holding `MyERP_SRS.docx`.

## 2026-10-27T00:44:56+08:00 WU-SD-004a1 review

| Problem | Cause | Change | Effect |
|---|---|---|---|
| Repeated delivery lines could each pass separately while their total exceeds an order line. | Comparing each line to the same remaining balance ignores the aggregate. | Aggregate six-place decimals by saved order-line ID before comparison; add exact and one-micro-unit-over tests. | One balance decision per order line without rounding away excess. |
| Final approval could count its own Pending reservation twice. | A generic Pending sum includes the current delivery as well as the requested saved lines. | Name the snapshot field `OtherPending`, explicitly excluding the current delivery; unit test the final-decision boundary. | The current saved quantity is counted once. |

The guard has no transaction or authority of its own. WU-SD-004a2 must assemble trusted saved snapshots under ordered SQL locks and prove concurrent reservations, state, permissions and rollback before exposing delivery commands.
## 2026-10-27T00:55:46+08:00 WU-SD-004a2a review

| Problem | Cause | Change | Effect |
|---|---|---|---|
| Invalid quantity or repeated line number could reach a later database operation. | Draft input previously had no field guard. | Validate all structural fields and six-place `decimal(19,6)` range before reference checks. | Named field errors without partial persistence. |
| Multiple lines can legitimately refer to one order line. | A uniqueness check on source ID would reject a valid split. | Enforce unique delivery line numbers only; leave source aggregation to `SalesDeliveryBalance`. | Split warehouse/location lines remain valid while the later balance guard checks their total. |

The guard deliberately does not trust a client-supplied balance or validate saved state. WU-SD-004a2b must load sources under SQL locks and prove company isolation, SYS/INV rollback and dual-connection concurrency.

## 2026-10-27T01:08:23+08:00 WU-SD-004a2b1 review

| Problem | Cause | Change | Effect |
|---|---|---|---|
| A Rejected delivery might be resubmitted without revising saved lines, or a Posted delivery might accept a repeat decision. | The transaction handler has no shared state decision yet. | Add one pure transition table and tests for each valid, invalid and repeated action. | The later handler has a single explicit eligibility rule. |
| A source order can cease to be Approved between draft creation and a decision. | Draft creation does not reserve an order. | Require the caller's current Approved-order result at submission and final approval; permit rejection to release an existing Pending reservation. | Stale source approval blocks new reservation/posting without trapping a Pending delivery. |

The caller must obtain the source order status under the ordered SQL locks and persist the transition with SYS/INV. This slice has no database, endpoint, authorisation or stock effect. Focused tests were red before implementation and green after it; final offline verification and SQL limits are recorded in progress.md.
# WU-SD-004a2b2 — saved-row balance snapshot

- Review: the decimal balance guard required pre-aggregated Posted and other Pending totals. A caller could accidentally include the current Pending reservation twice or omit historical Posted consumption.
- Change: added a pure saved-row projector with explicit status/deletion handling, source validation and stable order-line ordering. Kept database selection and locking with the future SQL transaction owner.
- Evidence: snapshot tests failed to compile before implementation and passed 4/4 after it. Real SQL, concurrency, permissions and INV posting remain unverified while SQL Server is stopped.

## 2026-10-27T05:30:17+08:00 WU-SD-004a2b3 review

| Problem | Cause | Change | Effect |
|---|---|---|---|
| A final approval snapshot could name an absent or inactive current delivery and silently omit its reservation. | The projector excluded rows by ID without proving that the ID represented an active saved Pending delivery. | Require at least one matching row and reject every matching deleted or non-Pending row; test missing, deleted and mixed states. | Malformed selected rows fail before balance or posting. |

The new test was red before the guard and passed afterward. SQL selection completeness, locks and transaction rollback remain unverified while SQL Server is stopped.

## WU-SD-004a2b4 review

| Problem | Cause | Change | Effect |
|---|---|---|---|
| Final approval could check a caller-supplied quantity smaller than the saved current Pending delivery. | The snapshot builder returned only other reservations; the current request was a separate argument to the balance guard. | Add `CheckCurrentPending`, deriving the current request and other balances from one saved-row input, then call the existing six-place balance guard. | A final decision cannot pass an independent smaller request through this entry point; the 1000-line limit is checked on saved rows. |

Tests failed to compile before implementation and passed afterward. The later SQL handler must call this entry point after selecting every saved row under ordered locks; no transaction or HTTP behavior is claimed here.

## WU-SD-004a2b5 review

| Problem | Cause | Change | Effect |
|---|---|---|---|
| Draft submit could check a caller-supplied quantity smaller than the saved Draft lines. | The existing balance guard accepts a separate requested array, and the final-decision helper only accepts Pending. | Add `CheckDraftSubmission`; require every current saved line to be active Draft, derive the requested array from those rows, and reuse the saved snapshot and six-place balance guard. | Missing, mixed-state, deleted, over-1000 and over-balance Draft submissions fail in the pure guard. |

The test failed to compile before implementation and passed afterward. SQL completeness, ordered locks, HTTP authority and transaction rollback still require WU-SD-004a2b.

## WU-SD-004a2b6 review

| Problem | Cause | Change | Effect |
|---|---|---|---|
| A future submit or final-decision handler could lock duplicate order lines or use input order, causing inconsistent lock acquisition between concurrent deliveries. | Saved delivery lines may repeat one source order line and arrive in arbitrary query order. | Add `SalesDeliveryLockPlan.Create` with saved-row integrity checks, a 1000-row limit and sorted distinct IDs. | Both Draft submit and Pending final approval can use the same deterministic ascending plan before selecting the complete balance snapshot. |

The focused test first failed to compile and then passed 4/4. Review also added the 1000-row acceptance and invalid Posted-plan cases. This pure helper does not validate SQL row selection completeness or acquire database locks; WU-SD-004a2b retains those acceptance gates.

## 2026-10-27T05:53:31+08:00 — WU-SD-004a2b8 snapshot identity review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| A complete first selection could be paired with a later balance snapshot that omitted or changed one current line. | The first-read guard checked count and IDs, while the balance projector had no persisted line ID to compare between reads. | Preserved optional line identity in the pure snapshot model and compared every current ID, source, quantity and active state against the selected set. | A mismatched second read fails before the balance decision; actual isolation and SQL locking remain WU-SD-004a2b. |

The new check does not use database or cross-module access. Focused tests failed at compile time before implementation, then passed. The optional snapshot LineId keeps existing pure balance callers compatible; the future SQL caller must supply positive persisted IDs for this check.

## 2026-10-27T05:57:46+08:00 — WU-SD-004a2b9 decision review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| A future SQL handler could call the identity guard but omit the full quantity guard, or the reverse. | The two checks were separate public calls for both Draft submit and Pending approval. | Add one `CheckLockedDecision` entry that checks identity first, then the matching full balance rule; reject unsupported states. | A successful pure decision has passed both invariants on one supplied snapshot. |

The focused test failed at compile time before implementation, then passed 3/3. Review found no further code change; SQL isolation, actual ordered locks, SYS/INV atomic effects and HTTP acceptance remain WU-SD-004a2b.
