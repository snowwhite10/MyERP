# ADR-098 — FI report continuation coordinator (offline rule)

Date: 2026-11-06. Status: Accepted (WU-BI-001a8).

## Context

ADR-093 (token), ADR-094 (manifest), ADR-096 (problem mapping) and ADR-097 (actor scope digest) are separate pure rules. A continuation endpoint must call them in the right order. If it loads the manifest or re-reads FI rows before the token is proven, a forged, tampered or scope-changed token costs a database read. If it trusts a client-sent digest, a revoked scope keeps working. If it issues a next token on the last page, the client gets an endless empty tail. `FiReportCursorTokenRules.Validate` needs the manifest expiry, but the manifest ID is only known after the token is decoded.

## Decision

- `FiReportContinuationRules.ContinueArAp`／`ContinueOtherIncomeExpense(request, actor, key, now, loadManifest, reread)` (FI Domain, pure apart from the two callbacks) return `FiReportContinuationResult<TPage>(Page, NextToken, Problem)`: either a page or a problem, never both.
- `FiReportContinuationRequest` carries the token, the client filters and the server-wired company and kind. The actor scope digest is never taken from the client. It is recomputed from `FiReportActorScope` (actor, granting roles, partner scope) on every call.
- Fixed order:
  1. Null inputs, an entry point for the wrong report family, and invalid actor scope input → 500 with `InternalDetails` only.
  2. Validate the token against the rebuilt binding with expiry `DateTimeOffset.MaxValue`. A missing, tampered or mismatched token, a changed scope, a bad client filter, a short key or a default clock → ADR-096 mapping. Nothing is loaded.
  3. Load the manifest named by the signed token. Validate again with its `ExpiresAt` (null when gone). Gone or `now ≥ expiresAt` → 409. Nothing is re-read.
  4. The stored manifest's binding must equal the rebuilt binding, and its cutoff must equal the token's. Otherwise → 500 `manifest.binding`／`manifest.cutoffSequence: Inconsistent`, and nothing is re-read.
  5. Re-read, then `FiReportManifestRules.Page*`. Any disagreement → 500, with no partial page.
  6. Issue a token for `pageNumber + 1` only when the page has `HasNext`. A token past the last page yields an empty page with full totals and no token.
- Exceptions thrown by the callbacks propagate to the endpoint, which reports them as an unknown-outcome 500.

## Consequences

WU-BI-001b wires SQL manifest load, FI re-read, the key, `IClock` and the SYS-resolved actor scope, then returns `Problem` as ProblemDetails with a traceId. Authorization is not decided here: VIEW permission, a partner filter outside a partial scope, and 401／403／404 stay with the endpoint and the FI read contract (WU-BI-001a). The first page (manifest creation plus page 1 plus the first token) is not covered and is still a separate step. Validating twice costs one extra HMAC per request, which is negligible.
