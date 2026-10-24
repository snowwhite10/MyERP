# PP — Proposed Application and HTTP boundary

No PP endpoint is implemented or present in Swagger. Proposed `/api/v1/pp/` resources: `bom-groups`, `boms`, `routings`, `mps-runs`, `mrp-runs`, `plans`, `work-orders`, `material-requisitions`, `completions`, `work-centres` and `shop-floor-progress`. Every list will support scoped filter, sort and page; state changes will use version and idempotency key. Distinct VIEW/CREATE/EDIT/APPROVE/REJECT/DELETE or RELEASE permissions are required per resource and current company/site.

The PP Application layer will call MD item/unit validation, SYS workflow/numbering, SD committed demand, PUR committed supply and INV availability/issue/receipt confirmation contracts. These owner contracts must return committed source cutoff, completeness and source IDs. `IProductionReportSource.ReadPlanAndPostedFlowAsync` is proposed for BI's five production reports; it returns PP saved state and confirmed INV references without giving BI table access. Missing owner implementation returns named unavailable source.

Expected errors: 400 invalid filter/input; 401 anonymous; 403 missing permission; 404 hidden scoped identity; 409 stale version or duplicate conflict; 422 invalid BOM cycle, missing source, shortage or nonreconciling movement; 500 ProblemDetails with traceId. Swagger, true SQL integration and authorised HTTP proof are deferred to implementation work units.

## WU-PP-001d proposed internal reader (design only)

`IReleasedDemandSnapshotReader` belongs in a future `MyERP.Modules.Production.Contracts` project and is called through PP Application, never by querying `pp_` tables. No HTTP route or Swagger entry is created by this design unit. SCM treats a missing project as `Unavailable:OwnerMissing`.

Request: `companyId`, authorised `siteIds`, optional `itemIds`, `fromMonth` inclusive, `toMonth` exclusive, `cursor?`, `pageToken?`, `pageSize` 1–500. Both month boundaries are required, and the range is bounded by the implementation's documented retention window. First page requests a committed cursor; all later pages must use its exact filter. The owner validates caller action and company/site scope on every request.

Response: `availability`, `reasonCode?`, `cursor {opaqueToken, committedAt, schemaVersion, expiresAt}`, `buckets[{month, completeness, rowCount}]`, `rows[{planId, planVersion, lineId, lineRevision, sourceKey, businessDate, businessMonth, companyId, siteId, itemId, sourceUomId, rawQuantity, releasedAt, correctionOf?}]`, `totalRows`, `manifestDigest`, `nextPageToken?`. Ordering is stable by month, plan, line and revision. The digest covers the full cursor membership and bucket certificates; each page repeats it and the total. Corrections are separate lineage, with a documented active-quantity projection as of the cursor, so one plan line cannot count twice.

`Complete` with zero rows and explicit empty buckets means zero demand. `Partial` or `Unavailable` carries a named reason and cannot certify zero. Expired cursor returns `CursorExpired`; missing replay evidence returns `ReplayUnavailable`; a filter/token mismatch or malformed cursor is rejected. Authorisation failure exposes no cursor metadata, row count or digest. WU-PP-001c must settle the concrete error mapping, retention implementation and SQL transaction strategy with real integration tests before this contract is callable.
