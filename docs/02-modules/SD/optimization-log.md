# SD optimization log

## 2026-10-28T20:35:55+08:00 — WU-SD-003a cross-order lock regression

| Finding | Cause | Change | Effect |
|---|---|---|---|
| A representative seek plan and ordinary two-thread order tests did not prove that editing one order permits another order's line insert. | The original clustered scan retains a Serializable range lock over unrelated order keys on this small table. | Added a real-SQL two-connection test that holds the edit transaction open during the other order's insert, then reads both documents from a fresh context. | With FORCESEEK removed temporarily, the insert failed with SQL lock timeout 1222; restored FORCESEEK passes 1/1 and the SalesOrder group passes 30/30. Debug solution build has zero warnings/errors. |

The test uses a bounded 3-second lock wait and separately verifies persisted version, remark and lines. It checks lock independence without needing a nondeterministic deadlock. The exact full-entity EF SHOWPLAN and full integration summary remain open; host free physical memory was about 1.3 GB.

## 2026-10-28T20:28:17+08:00 — WU-SD-003a order-line seek review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| Splitting the header and line loads did not ensure a narrow lock: real `myerp_test` SHOWPLAN still selected `pk_sal_order_line` Clustered Index Scan for the company/order predicate on a small table. | SQL Server chose the cheaper clustered scan and Serializable retained a line-key range lock across orders; the earlier deadlock graph showed `RangeS-S` versus another order's line insert. | Load tracked lines with a parameterised `FromSqlInterpolated` query and `INDEX(ix_sal_order_line_company_id_order_id_line_no), FORCESEEK` after locking and version-checking the header. Keep the existing company/order predicate and EF tracking fixup. | SHOWPLAN now uses an index seek on company/order plus a clustered key lookup. SalesOrder SQL/HTTP passed 29/29 four consecutive two-thread runs; Architecture 230/230 and Debug build with zero warnings/errors. A dedicated cross-order insert race and full integration summary are still needed. |

The forced index is part of the current migration and model. A later index change must update this query in the same migration. The representative SHOWPLAN query projected a subset of line columns; the product query loads the full entity and passed the persisted line assertions. Full integration was deferred while the host had about 1.2 GB of available memory, matching the earlier low-memory stall conditions.

## 2026-10-28T19:46:58+08:00 — WU-SD-003a full SQL integration gate diagnosis

| Finding | Cause | Change | Effect |
|---|---|---|---|
| Full integration with `--max-threads 2` produced 30-second SQL command timeouts in unrelated SYS approval and MD unit-of-measure tests. An isolated SYS approval test with `--max-threads 1` also failed during the assembly fixture's database migration, before its test body. | Root cause is not established. SQL DMV showed migration waiting at `ALTER DATABASE myerp_test SET READ_COMMITTED_SNAPSHOT ON`; the isolated run later timed out inserting `20261012150752_MasterData_ItemAttributes` into `__EFMigrationsHistory`. SQL reported only 767 MB free physical memory and `Available physical memory is running low`. Competing hypotheses: host memory pressure stalls SQL I/O; migration DDL waits for database connections; or another SQL workload competes for resources. The isolated result rules out test-method concurrency as a sufficient explanation. | Captured the exact failing commands and migration point. No product or migration change was made without a reliable focused reproduction. | Debug solution build succeeds with zero warnings/errors; Architecture 230/230. Full SQL integration remains failed, so WU-SD-003a cannot close. |

Next check: record free memory and SQL wait/blocking sessions before and during a single isolated test; identify any external connection or host workload without terminating user processes; then rerun the same test and the SalesOrder suite. The fixture rebuilds `myerp_test` on every run, so its startup is part of the repro. Quotation conversion still depends on blocked WU-SD-002a: no sales quotation table, repository, or transactional service exists yet.

## 2026-10-28T16:01:39+08:00 — WU-SD-003a authorised command lock order

| Finding | Cause | Change | Effect |
|---|---|---|---|
| Concurrent authenticated PUT commands returned HTTP 200/500, with SQL deadlock 1205. | Both Serializable commands read the action-scoped header with shared locks before the store requested an update lock, allowing a shared-to-update conversion deadlock. | Lock the company-scoped, active header with UPDLOCK/HOLDLOCK before the scoped read for EDIT and DELETE, keeping the scope check inside the same transaction. | Concurrent PUT returns 200/409 and concurrent DELETE returns 204/404; no partial saved order. The focused SQL/HTTP suite passes 29/29. |


## 2026-10-28T15:52:20+08:00 — WU-SD-003a concurrent mutation review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| Sequential version tests did not show whether concurrent edit and delete preserve a whole document. | Earlier tests issued commands after the previous transaction had ended. | Added four real-SQL two-connection cases for both command orders and commit/rollback outcomes; verified lock timeout, retry result, saved number, version, deletion flag and lines. | Store-level locking and rollback behave consistently; SalesOrder 29/29 pass without production changes. |

The authorised HTTP service path may have a different lock sequence because it reads action scope before the store lock. That race and quotation conversion are still unverified; the focused tests do not establish full integration acceptance.

## 2026-10-28T15:44:56+08:00 — WU-SD-003a authorised CRUD review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| HTTP create and edit had no proof against persisted MD references and a signed-in actor. | Earlier SQL command tests used controlled authorization or controlled MD responses. | Added a real SYS role/login and persisted MD reference fixture, then exercised POST, GET, PUT, stale DELETE and current DELETE through HTTP; checked the saved row independently. | The commercial snapshots, permanent number, action permissions, version 409 and soft deletion agree with SQL; SalesOrder 25/25 pass. |

The test uses one company and one actor, so it does not replace the separate action-scope or cross-company cases. The fixture builds only required MD references and avoids product code changes.

## 2026-10-28T15:22:24+08:00 — WU-SD-003a HTTP permission review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| Anonymous order HTTP coverage did not prove that action-specific SYS permissions reject a signed-in viewer. | The existing HTTP test stopped at 401 and the SQL command tests used controlled authorization. | Added a real SYS VIEW-only user and company assignment; exercised list plus POST, PUT and DELETE through HTTP. | The list succeeds with zero scoped rows; all three mutations return 403 and the shared permission error code. |

No production change was needed for this permission slice. Saved-row authorised HTTP, cross-company hiding and concurrent command outcomes still require evidence.

## 2026-10-28T15:17:16+08:00 — WU-SD-003a list coverage review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| Date, status, customer and keyword filters had no saved-row SQL evidence. | The first list test covered only default sorting and Branch scope. | Extended that regression with distinct saved dates, customers and states, plus an out-of-scope Branch row. | Real SQL confirms each filter and scoped total count; no production change was needed. |
| Invalid pagination and filter combinations were implemented but unverified for orders. | Shared paging and query validation were reused without order-specific assertions. | Added eight invalid query cases, including skip overflow and reversed date bounds. | All are rejected before a list result is returned. |

Authorised HTTP and simultaneous write outcomes remain outside this review's evidence.

## 2026-10-28T15:06:01+08:00 — WU-SD-003a list review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| Paging equal-date rows without a unique tie breaker can repeat or omit orders. | Business date is shared by many orders. | Every allowed sort adds immutable ID as the final sort key. | A real-SQL two-order test proves stable page 1/page 2 selection. |
| A count performed before SYS scope could expose hidden order volume. | The endpoint was previously unimplemented. | Apply company and VIEW scope to the base query before counting or projecting. | A foreign Branch sees an empty page and scoped zero total. |


## 2026-10-28T14:51:45+08:00 — WU-SD-003a HTTP read/create review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| A detail lookup could reveal an order outside the actor's VIEW scope if the controller loaded by ID first. | The earlier store checked only current company; HTTP and read query did not exist. | Added `SalesOrderQueryService` that requires `SAL.ORDER.VIEW` and applies `DataScopeFilter` to the EF query before the ID lookup. | Real SQL verifies Branch/Self inclusion and Department/other-owner 404; no hidden header or line is materialised. |
| Manual create had no HTTP route or OpenAPI contract. | The command existed only as an internal service. | Added distinct GET/POST controller actions with action permissions, response types and XML descriptions. | Anonymous routes return 401 and both appear in OpenAPI; authorised HTTP acceptance remains open. |

The detail response deliberately returns saved commercial fields only. Approval history and delivered balance are deferred to their owning work units; this checkpoint does not claim the full REQ_F4112.2.1 view acceptance.

## 2026-10-28T14:42:58+08:00 — WU-SD-003a action-scope and integration review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| Manual order headers lacked SYS owner and organization attributes, so later action-scope filtering could not distinguish Branch, Department and Self. | The initial order schema only recorded company and audit actor. | Added scalar branch, department and owner columns, backfilled existing owner from `created_by`, indexed the scope fields and saved CREATE authorization identity. | Real SQL create and scope-filter test passes 6/6; Debug build has zero warnings/errors. This does not yet enforce read/edit/delete scope. |
| Full integration run timed out in unrelated SYS approval and MD unit tests; both passed when run alone. | The full test load produced SQL command timeout, with no deterministic single-test reproduction. | Stopped the failing full run and reran each failed test alone with one thread. | Isolated tests passed 1/1 each. Full-suite acceptance remains open; next run should use bounded groups and inspect SQL waits during a repeatable failure. No product code was changed for this finding. |

## 2026-10-27T16:37:47+08:00 — WU-SD-003a MD snapshot transaction review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| A read MD identity could change while the order command was still resolving other references and saving its snapshot. | The default SQL transaction was ReadCommitted, which releases the shared row lock after each lookup. | Raised only the manual-create transaction to Serializable and added a real-MD, second-connection lock probe after the item read. | The SQL writer receives lock timeout 1222 before commit; after commit it succeeds, while the saved order retains its earlier item name. SalesOrder 13/13 and Debug build pass. |

The SYS number is still allocated before MD reads. This serializes competing number allocations and may increase contention; keep the transaction short and verify two-connection numbering before closing WU-SD-003a. The test uses a one-second lock timeout to observe the held read lock rather than waiting for a concurrent request indefinitely.

Full integration diagnosis (2026-10-27T16:41:11+08:00): `dotnet test --project tests/backend.integration/MyERP.IntegrationTests/MyERP.IntegrationTests.csproj --no-build --max-threads 2` ran for about four minutes without a completion summary and was interrupted. The narrower `--filter-method "*SalesOrder*"` loop completed 13/13 in about 26 seconds. The full-run symptom does not yet have a fast, deterministic failing filter, so no root cause or full-suite pass is claimed. Next diagnosis should bisect integration test groups with a bounded timeout and record the first group that reproduces the stall before changing production code.

## 2026-10-27T15:48:59+08:00 — WU-SD-003a physical-delete review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| Direct SQL could physically delete an order, releasing its unique number and quotation source and cascading to saved lines. | Existing constraints and triggers guarded updates and line writes but did not reject a header DELETE. | Added a reversible, set-based `AFTER DELETE` guard and a failing real-SQL regression before implementation; expanded it to active Draft, soft-deleted Draft and Approved. | The focused order schema suite passes 8/8; rejected deletes retain the header identity, source, state and line. |

The guard is a persistence backstop for BR-SD-016/017/019. It does not replace the future authorised soft-delete command, which must still check company, permission, state and version.

The migration was applied to `myerp` and the Debug solution build finished with zero warnings/errors. Shared, Architecture and DocGen projects passed in the full backend run; integration remained without a summary under host load even with `--max-threads 2`, so that run was stopped and is not counted as a full-suite pass.

## 2026-10-27T15:37:16+08:00 — WU-SD-003a saved-header content review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| Direct SQL could change an Approved order's customer, monetary totals or remark without changing its status. | The existing header trigger checked transitions and deletion only; line triggers protected child rows, not header content. | Added a reversible set-based UPDATE trigger with a null-safe commercial-field comparison for Pending, Approved, Cancelled and deleted headers. Added a failing real-SQL regression before implementation, then verified Draft remains editable. | The focused order schema suite passes 7/7; failed writes leave the commercial snapshot unchanged. |

Audit/version columns and state transitions remain available to their existing lifecycle. The trigger checks every affected row in a statement and rejects the whole statement if one frozen header changes. Authorised service decisions, quote conversion and MD/SYS transaction checks remain WU-SD-003a work.

## 2026-10-27T15:30:38+08:00 — WU-SD-003a saved-header state review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| Direct SQL could move an Approved order back to Draft or revive a soft-deleted Draft. | Header CHECK constraints allowed each resulting row and did not compare old and new states. | Added a reversible set-based UPDATE trigger with the BR-SD-016 transition matrix and a persistence regression that verifies rejected writes leave the saved rows intact. | The focused real-SQL order suite passes 6/6; migration is applied to `myerp` and Debug build has zero warnings/errors. |

The trigger enforces persisted state shape, but only the future authorised service can validate SYS decisions, versions, action scope and business references. The all-solution unit/architecture/docgen projects passed; integration remained active without a final summary under host load, so the run was stopped and is not counted as a full-suite pass.

## 2026-10-27T11:40:10+08:00 — WU-SD-003a saved-line state review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| Direct SQL could add, change or remove an Approved or soft-deleted order line. | The application lifecycle guarded status changes, but the existing SQL source triggers did not guard line writes by parent state. | Added an INSERT/UPDATE/DELETE trigger in a reversible migration and tested both frozen states with all three write kinds. | The focused real-SQL schema suite passes 5/5; Draft order insertion and the existing source integrity regressions remain green. |

The trigger checks affected parent identities in one set operation, so a multi-row statement is rejected atomically. It does not validate MD references or quotation eligibility; those remain service-level work within WU-SD-003a.

The full backend suite again exposed unrelated INV valuation SQL load failures: `HundredThousandMovementsUseValuationIndex` lost its connection after about three minutes and `NegativePoolAndOverflowReturnBusinessErrors` timed out while creating MD setup data. A focused low-load rerun of the latter passed 1/1 in about 25 seconds, so the full-suite failure is load-sensitive rather than a deterministic order-schema regression. The 100,000-movement case has no fast isolated feedback loop yet; no speculative INV code change was made. Its isolated runtime and SQL resource contention remain for a later quality unit.

## 2026-10-27T11:24:14+08:00 — WU-SD-003a source-pair review

| Problem | Cause | Change | Effect |
|---|---|---|---|
| A saved order could mix a manual header with quoted lines, or a quoted header with manual lines. | Unique source indexes and immutable UPDATE triggers did not compare header and line source presence on INSERT. | Added a reversible line INSERT/UPDATE consistency trigger and real-SQL rollback test. Configured both trigger-bearing order tables to use EF's trigger-compatible save path after the first green attempt exposed SQL Server's OUTPUT restriction. | Four order schema tests pass, including both invalid source pairs and existing valid inserts. Full quotation identity and complete-line conversion remain service-level gates. |

## 2026-10-27T11:09:39+08:00 — WU-SD-003a source identity review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| A saved order or line could replace its quotation source, freeing the original unique source key for a second conversion. | The filtered unique indexes prevent duplicates but do not make an existing source link immutable. | Added SQL update triggers with a reversible migration, and tested clearing, reassignment, ordinary update and soft deletion against the real database. | Source identity stays permanent across later updates and deletion. Authorised conversion, MD/SYS snapshots and two-connection competition still need implementation. |
| Full-suite architecture test found a Domain entity using an Application enum. | `SalesOrderStatus` had been declared with its lifecycle service. | Moved the enum to Domain without changing persisted values or transition logic. | Restores the Domain dependency boundary; rerun of the full suite is required before a completion claim. |
| The seed role regression expected SALES to have only inventory VIEW and SYS self-service permissions. | SD permissions were subsequently added to the catalog, while the test expectation stayed at the earlier foundation stage. | Extended the expected set from the catalog's SD/CRM modules, matching the existing SALES seed policy. | Preserves the role-policy assertion, including all SD actions; focused SQL regression passes. |


## 2026-10-27T10:50:16+08:00 — WU-SD-003a schema checkpoint review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| Order source links needed durable uniqueness and same-company line ownership before delivery could lock approved source lines. | WU-SD-003a had only pure input/state helpers while SQL Server was stopped. | Added order/line model, permanent filtered source indexes and same-company FK; generated and applied a reversible migration. A real SQL regression tests duplicate source and invalid-line rollback. | The two source tables now exist. CRUD, conversion, approved source enforcement and two-connection concurrency remain unverified. |

The generated migration's constant column arrays triggered CA1861 under `TreatWarningsAsErrors`; the same narrow generated-code pragma used by existing migrations fixes compilation without changing runtime behavior. The failing `dotnet test` command was the feedback loop, and the focused SQL tests passed after correction.

## 2026-10-27T10:39:15+08:00 — WU-SD-004a2b saved-line selection review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| The pure completeness guard had no SQL caller and could accidentally be invoked from a read outside a transaction. | The order persistence prerequisite WU-SD-003a is still blocked, and delivery persistence only had its schema. | Added a company-filtered saved-line selector requiring an existing serializable transaction, with an independent COUNT and ordered lock plan; covered it with real SQL tests. | The selected-line part is executable and rejects missing or cross-company documents. Source locks, full balance reread, SYS/INV rollback, and HTTP remain open. |

The `sal_order` and `sal_order_line` tables now exist after the WU-SD-003a schema checkpoint, but no authorised order command has proven an Approved source. A complete delivery submit or approve implementation still needs WU-SD-003a order persistence and must not trust client-supplied balances.

## 2026-10-27T10:16:42+08:00 — WU-SD-004a2b schema checkpoint review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| The interrupted delivery model and migration were present but had no verified SQL deployment or source document IDs. | The previous turn stopped after generating the migration. | Restored the unrelated protected Runner file, applied the migration, inspected the actual tables, constraints, indexes and quantity precision, then allocated REQ_DS43.170–171. | Schema changes are reviewable and traceable; draft CRUD, transaction locks and posting are still open. |

The parent FK includes company ID; source order, MD, SYS and INV IDs have no cross-module FK. The current schema does not yet store tracking or posted INV movement identities, so those design requirements remain for the next implementation step. No claim of outbound command completion is made.

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

## 2026-10-27T16:01:48+08:00 — WU-SD-003a manual persistence review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| Replacing only order lines could keep the old header version and permit a stale subsequent command. | EF may see no changed header field when the commercial header is unchanged. | Explicitly mark the saved remark as modified during whole-line replacement. | The SQL regression observes version 1 to 2 and stale delete returns conflict. |
| A persistence call could save an order without SYS numbering in the same transaction. | The internal store otherwise owns no transaction boundary. | Require a caller-owned transaction for create, replace and delete. | The no-transaction call fails and a rolled-back create leaves no header or line. |

The store is registered for the later application service. Its prepared snapshot input is internal to SD; the authorised HTTP path must validate business input and MD/SYS contracts before invoking it.
# WU-SD-003a MD item identity checkpoint — 2026-10-27T16:10:46+08:00

Review: sales order lines require saved item code/name, but the existing MD selection API only exposes paged form choices and cannot safely resolve one requested ID. Added an ID-based current-company MD Contract with active and soft-delete checks; SD does not read MD DbSets. Its test covers the company and state boundary. Remaining risk: the future order transaction must hold reference stability through save, and must combine customer, item, currency, tax, amounts and SYS numbering atomically.
# WU-SD-003a commercial snapshot review — 2026-10-27T16:17:55+08:00

- Problem: the initial snapshot implementation used default currency decimals even though MD permits company-specific precision. This could save a different amount from the configured currency policy.
- Cause: `SalesOrderAmounts` delegated to the quotation calculator without passing MD's `CurrencyPrecision`.
- Change: pass the precision returned by `ICurrencyLookup.GetPrecisionAsync` through both calculators; retain their default for existing callers. Check original header totals against `DECIMAL(19,4)` before persistence.
- Evidence: three focused snapshot tests and 230 architecture tests pass; Debug solution builds with zero warnings/errors. Transactional persistence and HTTP are pending.

## 2026-10-27T16:26:28+08:00 — WU-SD-003a transaction review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| Number allocation, MD snapshot resolution and store save had no shared command boundary. | Each piece was implemented as an independent service. | Add a CREATE-permission checked command that starts one database transaction, commits after store save and rolls back plus clears tracked entities on failure. | Real SQL test observes saved commercial amounts and a failed insert restoring the SYS sequence to its previous value. |

The test uses controlled MD contract replies; real MD lookup and concurrent changes require a separate SQL regression. HTTP, quotation conversion and full integration remain open.

## 2026-10-27T20:28:57+08:00 — WU-SD-003a customer reference review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| The order transaction's customer lock had no direct real-MD regression test. | Earlier create tests supplied a fixed customer contract response. | Add an approved customer lookup with a second SQL connection that attempts a rename before order commit, then verify the persisted name after a later successful rename. Register the existing payment setup model in the focused test context because the real lookup projects default payment codes. | Rename blocks until commit; SalesOrder SQL 14/14 and Debug build pass. No product code change was needed. |

Currency and tax races, concurrent number allocation and authorised HTTP remain unverified.

## 2026-10-27T20:34:30+08:00 — WU-SD-003a currency and tax reference review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| Currency precision, exchange rate and tax eligibility had no direct concurrent MD-write regression. | Earlier create tests used fixed MD contract replies. | Add one real MD transaction test with a second SQL connection attempting four updates after each lookup; assert the saved snapshot and a successful later rate update. Register the existing currency and tax models in the focused fixture. | Each in-transaction update times out with SQL 1222; the saved order retains its original rate and tax percentage. SalesOrder SQL 15/15 and Debug build pass. Product code required no change. |

The current Serializable boundary covers these reference reads for manual create. Quotation conversion, concurrent numbering, authorised HTTP and full integration remain open.

## 2026-10-27T20:40:31+08:00 — WU-SD-003a numbering race review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| Manual create had rollback coverage but no two-connection allocation regression. | Prior tests used one connection and could not prove SYS `MERGE` locks survive through SD commit. | Add a second real SQL connection that attempts `SAL.ORDER` allocation while the first create transaction is open, then retries after commit; assert both saved numbers and sequence value. | SQL 1222 blocks the competing allocation, then `SO002` is saved; focused order suite 16/16. Existing product transaction and generator need no change. |
| First version of the probe waited 30 seconds and returned SQL -2. | EF closed the second connection after `SET LOCK_TIMEOUT`, resetting the session setting before `NextAsync`. | Hold the second EF connection open through the probe. | The focused loop now detects SQL 1222 in about one second; no product behavior changed. |

Quotation conversion, authorised HTTP/Swagger, company/scope and version races, and full integration remain open.

## 2026-10-28T14:59:53+08:00 — WU-SD-003a draft command review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| A stale edit could resolve MD references before discovering the old order version. | The draft store's version check ran after snapshot assembly. | Check the saved scoped header version before rebuilding the snapshot; the store rechecks under its update lock. | Stale input returns a conflict before reference lookups. The real-SQL command test covers the saved version and delete conflict. |
| Edit and delete could expose an order outside their own action scope if callers used the store directly. | The low-level store only guarded company and version. | Add an application command service that resolves EDIT or DELETE scope and checks saved visibility inside its transaction. | A Branch-hidden ID returns 404; a visible current-version Draft can be changed. Authorised HTTP and simultaneous-write races remain to be tested. |

## 2026-10-28T15:30:04+08:00 — WU-SD-003a HTTP company isolation review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| The earlier VIEW-only HTTP regression checked an empty company and could not prove saved IDs are hidden across two authorised companies. | No persisted row was used in that HTTP test. | Add a saved order and assign the same real SYS user to both companies; assert home detail 200, foreign detail 404 and foreign totalCount 0. | The VIEW query's company boundary is verified through authenticated HTTP with real SQL. Product code needed no change; action scope and write paths remain open. |

## 2026-10-28T15:37:40+08:00 — WU-SD-003a action-scope HTTP review

| Finding | Cause | Change | Effect |
|---|---|---|---|
| A company-wide VIEW could mask an accidental use of VIEW scope for EDIT or DELETE. | Existing authenticated HTTP tests checked missing permissions and cross-company reads, but not differing scopes for actions on the same saved ID. | Add a real SYS role combination and saved-row regression: VIEW Company plus EDIT/DELETE Self; check hidden writes and successful own Draft soft-delete against SQL. | HTTP now proves action-specific 404 and independent persisted-row isolation. The initial 409 was a fixture error: direct SQL seeding did not assign Version 1; no product change was required. |

## 2026-10-28T19:59:48+08:00 — WU-SD-003a SQL integration timeout diagnosis

The prior single SYS approval test failed during `TestDatabase.InitializeAsync` migration, before the test body. Today the same exact test passed 1/1 in 28 seconds, and the SalesOrder real SQL/HTTP group passed 29/29 in 44 seconds. This rules out a deterministic migration or SalesOrder failure. No product code or migration changed.

The full integration attempt with `--max-threads 2` was sampled by `Invoke-IntegrationDiagnostics.ps1`. Multiple `myerp_test` requests waited on `WAIT_SLEEP_LOWMEM_GUARD`; one request was blocked by another request that itself waited on the low-memory guard. SQL reported `process_physical_memory_low=1`; Windows available physical memory fell from about 1.1 GB to 0.5 GB of 32 GB. The sampler's SQL login then timed out during pre-login. This is direct evidence of host memory pressure during the full run; it does not establish which process consumed the memory or prove that all earlier timeouts share one cause. The full run produced no test summary after about seven minutes and was stopped under 0.5 GB free memory. Debug solution build passed with zero warnings/errors. Keep full integration acceptance open; next run should first recover host memory and use the existing low-thread diagnostic command, then record an actual suite summary.

## 2026-10-28T20:19:54+08:00 — WU-SD-003a order-line lock checkpoint

The `system_health` 20:08:16 graph identifies a serializable order edit holding a `RangeS-S` lock on the `sal_order_line` clustered key while a separate line insert waits for `RangeI-N`; the insert transaction holds an exclusive `sal_order` key that the edit's line-state trigger needs. The edit's joined `Include(Lines)` is the likely source of the broad line-key range. `SalesOrderDraftStore.LoadAsync` now loads the locked header first, checks the version, then explicitly loads only its lines through the company/order relationship. This keeps the application lock and snapshot checks while narrowing the line read. The existing authorised two-PUT and two-DELETE regression still checks 200/409, 204/404 and persisted state. Focused SalesOrder integration passes 29/29 in two consecutive two-thread runs; Debug solution build has 0 warnings and errors. The pre-change run in this round also passed 29/29, so the timing-sensitive deadlock is not proved eliminated. Host free physical memory is about 1.35 GB; full integration remains unverified. Next verify the actual line query plan and repeat the focused race under load, then run the full integration suite when memory recovers. Quotation conversion still depends on blocked WU-SD-002a.

## 2026-10-28T20:10:52+08:00 — WU-SD-003a low-memory and deadlock checkpoint

The full integration diagnostic run started with roughly 1.5 GB available physical memory and `process_physical_memory_low=1`. After about two minutes it had no result summary; memory fell below 0.8 GB, so the run was stopped. The SQL sampler now records available physical memory, SQL process memory and both SQL low-memory flags beside each wait sample. Its query was executed against the real instance and Debug build passed with zero warnings/errors.

The focused SalesOrder group exposed a separate repeatable failure at two test threads: 28/29 twice, with `AuthorizedManualDraftUsesRealReferencesAndPersistsHttpChanges` receiving HTTP 500 instead of 409. Its server log identifies SQL error 1205. The SQL `system_health` deadlock graph at 20:08:16 shows one transaction's `tr_sal_order_line_state_guard` reading `sal_order` while another inserts `sal_order_line`; their header and line key locks form a cycle. The same test alone passed 1/1, and the group at one test thread passed 29/29. Cross-test concurrency is a plausible trigger, but the graph does not yet identify both test methods. No test or product behavior was weakened. Next: identify both transaction callers and fix lock ordering or test isolation at the correct seam, then rerun at two threads and obtain a full integration summary after host memory recovers. Quotation conversion still requires the blocked real quotation service in WU-SD-002a.
