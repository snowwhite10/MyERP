# ADR-100 — FI report single-entry dispatch (offline rule)

Date: 2026-11-06. Status: Accepted (WU-BI-001a10).

## Context

ADR-098 (`Continue*`) and ADR-099 (`First*`) are separate coordinators. If the WU-BI-001b endpoint chooses between them itself, it can make three mistakes: treat an empty or blank `token` query value as "no token" and start a new first page (silently taking a new cutoff, which BR-FI-131 forbids), wire only the callbacks of one path so a miswiring shows up only when a user reaches page 2, or call FI read／save during a continuation.

## Decision

- `FiReportContinuationRules.ReadArAp`／`ReadOtherIncomeExpense(request, actor, key, now, ttl, read, saveManifest, loadManifest, reread)` are the only calls the endpoint makes. They return the same `FiReportContinuationResult<TPage>` as the two coordinators.
- A missing request, actor or any of the four callbacks is a 500 (`read.<name>:Required`, all listed together) on either path, before any I/O.
- Dispatch is on `request.Token is null` only. Any other value, including `""` and whitespace, goes to `Continue*`, which returns 400 `FI.REPORT.TOKEN_INVALID` (`token: Required`) with nothing loaded. A blank token never starts a new first page.
- The first path never calls `loadManifest`／`reread`; the continuation path never calls `read`／`saveManifest`. All other ordering, mapping and error behavior is unchanged from ADR-098／099.

## Consequences

The endpoint maps the query string to `FiReportContinuationRequest` and must keep a present-but-empty `token` parameter as `""`, not `null`. Authorization (VIEW, partner filter within scope, 401／403／404) still stays with the endpoint and the FI read contract. The duplicated null checks inside `First*`／`Continue*` remain for direct callers and tests.
