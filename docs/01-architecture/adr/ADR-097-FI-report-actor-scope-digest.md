# ADR-097 — FI report actor scope digest (offline rule)

Date: 2026-11-06. Status: Accepted (WU-BI-001a7).

## Context

ADR-093 binds every FI report continuation token to `FiReportCursorBinding.ActorScopeDigest` (64 lowercase hex), but did not say how the digest is computed. BR-FI-131 requires that a role or partner scope change invalidates old tokens, while FI still checks authority on every read. If each endpoint built the digest ad hoc, partner order or duplicates could change it (false 400) or a revoked role could leave it unchanged (stale token accepted until the authority check).

## Decision

- `FiReportActorScopeRules.Digest(actorUserId, companyId, kind, roleIds, partnerScope)` (FI Domain, pure) returns `FiReportActorScopeResult(Digest, Errors)`. `FiReportPartnerScope` is `All` or `Partners(ids)`.
- The caller resolves the actor's authority from SYS through its contract and passes the role IDs that grant the report plus the granted partner scope. FI reads no permission store, clock or database here.
- Canonical text: `v1|actor=<n>|company=<n>|kind=<enum name>|roles=<sorted distinct ids>|partners=<sorted distinct ids or *>`, invariant numbers. Digest = lowercase hex SHA-256 of UTF-8 `"MyERP.FI.ReportActorScope.v1\n" + text`. `Canonical(...)` exposes the text for diagnostics.
- Validation (all errors, fixed order): actor／company ≤ 0 or undefined kind → `OutOfRange`; missing／empty roles or partner list → `Required`; any ID ≤ 0 or more than 1,000 distinct roles／100,000 distinct partners → `OutOfRange`; `All` with a partner list → `NotAllowed`; missing scope → `Required`. Invalid input yields no digest.
- Role IDs are included (beyond the original backlog wording) so a revoked or added granting role also invalidates tokens.

## Consequences

WU-BI-001b computes the digest on the first page and on every continuation, then `FiReportCursorTokenRules.Validate` rejects a changed scope as a binding mismatch, which `FiReportProblemRules.Map` (ADR-096) returns as 400 `FI.REPORT.TOKEN_INVALID`. An invalid digest input is a server wiring fault (500). The digest is not authority. Renaming a `FiReportKind` member or changing the format changes digests; such a change must bump the version prefix and hash domain. Tokens live at most 60 minutes, so no migration is needed.
