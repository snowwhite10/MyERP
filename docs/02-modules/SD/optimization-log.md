# SD optimization log

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
