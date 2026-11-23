# ADR-192: Sales return HTTP routes, permissions and error texts (WU-SD-008a6)

Date: 2026-11-24. Status: Accepted (WU-SD-008a6).

## Context

ADR-185～191 delivered the return services (Draft create/edit/delete, reads, SUBMIT with zero-step posting, the SYS
decision handler and Rejected revision) without routes. The SD permission catalogue only listed `SAL.RETURN.VIEW` and
`SAL.RETURN.CREATE`, so roles could not be granted EDIT, DELETE or SUBMIT, and no `SAL.RETURN.*` code had a translation.

## Decision

1. `SalesReturnsController` at `/api/v1/sal/returns`, following the delivery/invoice controllers:
   `GET` (VIEW, `SalesReturnQuery` filters), `GET {id}` (VIEW), `POST` (CREATE; 201 + Location, identical replay 200),
   `PUT {id}` (EDIT; body `{ version, draft }`, Draft or Rejected → Draft), `DELETE {id}?version=` (DELETE; 204),
   `POST {id}/submit` (SUBMIT; body `{ version }`). Every response body is `SalesReturnDetail` / `SalesReturnSummary`.
   No INV/FI posting route; SYS approval endpoints decide Pending returns (ADR-191).
2. The 201 body is read back from SQL (`SalesReturnQueryService.DescribeCommittedAsync`, as invoice ISSUE does), so
   create, replay and GET serialise the same stored decimal scale. CREATE does not need VIEW to see its own result.
3. A PUT without `draft` is validated as an empty draft (400 field errors), not a 500.
4. Status mapping comes from the existing exceptions: NotFound 404, BusinessRule 422 (with `lineNo`/remaining where
   raised), Conflict 409 (stale version, `REQUEST_CONFLICT`, `SNAPSHOT_STALE`, `INV_POSTING_CONFLICT`,
   `APPROVAL_STATE_CHANGED`), `SalesReturnPostingException` 500 keeping `errorCode`.
5. `SalesPermissions` adds `SAL.RETURN.EDIT`, `SAL.RETURN.DELETE`, `SAL.RETURN.SUBMIT`.
6. All 22 `SAL.RETURN.*` error codes get en / zh-Hant / zh-Hans texts under `SAL.RETURN` in `errors.json`; the
   architecture test `SalesReturnErrorTranslationTests` keeps the key set equal to the backend constants.

## Consequences

- The returnable-lines source read (`GET /api/v1/sal/deliveries/{id}/returnable-lines`, api.md 008d) and the return
  pages stay with the frontend work unit (008b).
- HTTP tests SD-RETURN-HTTP-001～005 use real SYS authorization/approval and SQL; INV and the MD guard are doubles,
  returns are unbilled-only (FI credit is covered by SD-RETURN-POST/APPROVE).
