# ADR-101 — FI report frozen export coordinator (offline rule)

Date: 2026-11-06. Status: Accepted (WU-BI-001a11).

## Context

FI-RPT-014 requires an export to contain exactly the members of the manifest the user is paging (ADR-095 `FiReportManifestRules.Export*`). If the WU-BI-001b export endpoint chained token validation, manifest load, expiry, binding／cutoff checks and re-read itself, it would duplicate ADR-098 and could drift from it (for example, loading a manifest for a tampered token, or re-reading an expired one). Column headers are localized text the server supplies, so a wrong header list is a server wiring fault, not a client error.

## Decision

- `FiReportContinuationRules.ExportArAp`／`ExportOtherIncomeExpense(request, actor, key, now, headers, loadManifest, reread)` return a new `FiReportExportResult(Content, RowCount, Problem)`: either the full CSV (no BOM) and its row count, or a problem, never both and never partial content.
- The request must carry a first-page or continuation token. A missing, empty, blank or invalid token is 400 `FI.REPORT.TOKEN_INVALID` with nothing loaded.
- Wiring faults are 500 before any I/O, all listed together: missing request, actor, `loadManifest` or `reread` (`export.<name>:Required`); headers null or containing null (`export.headers:Required`); a header count that differs from the report's export column count (`export.headers:OutOfRange`). A wrong report family is 500 `request.kind:NotAllowed`.
- After that, export reuses the same private front half as `Continue*` (`Open`): recompute the ADR-097 actor digest (invalid → 500) → validate the token without expiry (failure → 400, nothing loaded) → load → validate with the manifest expiry (gone／expired → 409, nothing re-read) → the stored binding and cutoff must match the token (otherwise 500, nothing re-read).
- Then re-read once and call ADR-095 `Export*`. Every member is exported, regardless of the bound page size or the page number in the token. Any disagreement is 500 with no content. Exceptions from callbacks propagate to the endpoint's unknown-outcome 500.

## Consequences

`Continue*` and `Export*` share one implementation of the token／load／expiry／stored-manifest checks, so they cannot drift. EXPORT permission, the export audit row, file name and content type stay with the endpoint.

A report that fits on one page gets no continuation token from `First*` (ADR-099), even though its manifest is saved. As a result, it cannot be exported through this coordinator yet. Follow-up WU-BI-001a12 decides how the first page exposes a token for export only (for example, an export token on every first page) without changing the "no next token after the last page" rule.
