# ADR-096 — FI report problem mapping (offline rule)

Date: 2026-11-06. Status: Accepted (WU-BI-001a6).

## Context

ADR-093 (cursor token) and ADR-094／095 (manifest, page, export) return typed field errors and a status, but the HTTP outcome was described only in prose (api.md WU-BI-001d: malformed／mismatched token 400, expired 409, 422, 500 with traceId). The rules mix errors the client caused (filters) with errors only the server can cause (key, clock, company, actor scope digest, report kind, position, manifest, re-read, headers, TTL). If WU-BI-001b mapped them ad hoc, a wiring bug could surface as a client 400, or an `Inconsistent` finding could leak internal field names. No `FI.REPORT.*` code existed; the shared codes are `COMMON.REQUEST.VALIDATION_FAILED` and `COMMON.SERVER.INTERNAL_ERROR`.

## Decision

- `FiReportProblemRules.Map(...)` (FI Domain, pure) has overloads for `FiReportCursorValidationResult`, `FiReportCursorIssueResult` and a manifest error list (`Create*`／`Page*`／`Export*` `Errors`). It returns null when there is no problem. Otherwise it returns `FiReportProblem(Status, ErrorCode, FieldErrors, InternalDetails)`.
- Client fields are only the binding filters the request carries: `partnerId`, `asOf`, `from`, `through`, `kindFilter`, `currency` and `pageSize` (`ClientFields`, frozen).
- Precedence (first match wins):
  1. Any server-side finding → 500 `COMMON.SERVER.INTERNAL_ERROR` with **no** field errors. Server-side findings are `KeyInvalid`, clock, position, a non-client binding field, any `request`／`report`／`manifest`／`reread` input, an `Inconsistent` error, an error in an unexpected collection, or a null input.
  2. A client filter error → 400 `COMMON.REQUEST.VALIDATION_FAILED`, with field → distinct error names in input order (for example `pageSize: ["OutOfRange"]`).
  3. Manifest expired or gone → 409 `FI.REPORT.MANIFEST_EXPIRED` with no field errors. The client must start a fresh first page.
  4. Token missing → 400 `FI.REPORT.TOKEN_INVALID` with `token: ["Required"]`. A malformed, tampered or binding-mismatched token (including a different company, actor scope or report kind) → the same code with `token: ["Invalid"]`. The response never says which part failed, so it cannot be used to probe the signature or the bound scope.
  5. More than 10,000 rows → 422 `FI.REPORT.TOO_LARGE` with no field errors (narrow the filter).
- `InternalDetails` lists every input error as `collection.field:Error` in input order. It is only for the traceId log and must never be returned.
- Domain keeps the two `COMMON.*` values as string constants equal to `CommonErrorCodes`. It does not take a new dependency.

## Consequences

WU-BI-001b endpoints call the rule, log `InternalDetails` with the traceId, and turn the result into ProblemDetails (`errors` = `FieldErrors`). Frontend i18n keys `errors.FI.REPORT.TOKEN_INVALID／MANIFEST_EXPIRED／TOO_LARGE` are added with the screens (WU-BI-001b). A new client filter or report kind must update `ClientFields` and this ADR.
