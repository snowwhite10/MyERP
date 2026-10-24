# SCM — Proposed Application and HTTP boundary

WU-SCM-000d creates no endpoint or Swagger contract. Planned `/api/v1/scm/` resources: `forecast-versions`, `forecast-comparisons`, `consignments`, `carrier-events`, `delivery-exceptions`, `supplier-invitations`, `order-grants` and `supplier-proposals`. Lists use stable server paging, sort whitelist, company/supplier/data scope and source availability. Mutations use optimistic version and permanent command/event keys. Rights separate VIEW, CREATE, EDIT, APPROVE, REVIEW, EXPORT and DOWNLOAD.

SCM obtains approved demand from SD, released production demand from PP, posted movements from INV, order and receipt state from PUR, supplier identity from MD, and account/approval from SYS Application contracts. Owner replies must carry company, source ID/version, committed status and cutoff. Missing contracts are named unavailable. Unknown outcomes are queried by permanent key before success is reported. Later units define DTOs, Swagger examples and SQL/HTTP evidence.

ProblemDetails: 400 malformed input; 401 anonymous; 403 missing action; 404 hidden order/supplier; 409 duplicate key or stale version; 422 invalid state, quantity or policy; 500 with traceId. Timeout is unknown outcome with status lookup.

## WU-SCM-001d proposed forecast owner reads (no endpoint implemented)

| Owner | Proposed Application read | Current code evidence | Availability now |
|---|---|---|---|
| SD | `ReadApprovedDemandAsync(scope, bucketRange, fixedCursor, pageToken)` | `MyERP.Modules.Sales.Contracts` only defines `SalesModuleContracts`; no demand reader. | `ContractMissing` |
| PP | `ReadReleasedDemandAsync(scope, bucketRange, fixedCursor, pageToken)` | No `MyERP.Modules.Production` project. | `OwnerMissing` |
| INV | `ReadPostedMovementAsync(scope, bucketRange, fixedCursor, pageToken)` | `IStockBalanceQuery` supplies current quantity; `IInventoryValuationQuery` is provisional. | `ContractMissing` |
| MD | `ResolveItemBaseConversionSnapshotAsync(item, fromUom, quantity, fixedVersion)` | `IUomConverter.ConvertForItemAsync` returns decimal only. | `ConversionEvidenceMissing` |

Proposed request: `companyId`, `itemId`, `siteId/warehouseId`, inclusive `fromMonth`, exclusive `toMonth`, `canonicalUomId`, `fixedCursor?`, `pageSize<=500`, `pageToken?`; company/site/data scope must be checked by each owner before source existence is disclosed. Proposed owner reply: `availability {Complete|Partial|Unavailable}`, `reasonCode`, `owner`, `cursor {opaqueToken, committedAt, schemaVersion}`, `rows {sourceType, sourceId, lineId, sourceVersion, businessDate, bucket, itemId, siteId, sourceUomId, baseUomId, rawQuantity, convertedQuantity, directionOrState, committedKey}`, `nextPageToken`, `totalRows`, `manifestDigest`. All pages must have the same cursor and digest; a missing or expired cursor is an explicit error. A zero-row `Complete` reply is the sole evidence for zero demand. Date and quantities are typed values, not formatted strings. Opaque cursors and tokens are owner-issued and SCM must not inspect owner storage.

SCM's planned `CreateForecastVersion` accepts named fixed cursors or requests them once, then freezes their replies and conversion evidence in its source manifest. Response reports each owner's status and reason separately; `InsufficientSource` blocks approval. Comparison reads the saved version/manifest. The owner prerequisite slices must define cursor retention, correction visibility, pagination stability and conversion snapshot semantics before 001a implementation. No Swagger claim is made for this design-only unit.
