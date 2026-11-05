# ADR-094 — FI report manifest (offline rule)

Date: 2026-11-06. Status: Accepted (WU-BI-001a3).

## Context

BR-FI-130 requires every page of an FI report read (AR／AP detail as-of, ADR-091; other income／expense, ADR-092) to come from one durable committed set, with no row duplicated or missed across page boundaries (FI-RPT-005／006). ADR-093 fixed the continuation token, which names a manifest ID and its cutoff. The manifest content and the continuation consistency check can be fixed offline; the SQL manifest table, permissions and API wait for the SQL memory gate (WU-BI-001a).

## Decision

- `FiReportManifestRules` (FI Domain) is pure. It reads no clock or database. `CreateArAp`／`CreateOtherIncomeExpense` take the ADR-093 binding, the complete report from the read rule, `createdAt` from `IClock`, and an injected TTL.
- Manifest = binding, cutoff, ordered member IDs (principal ID for AR／AP, source ID for other income／expense), content digest, full-set totals for the report's kind only (`ArApTotals` or `OtherTotals`), `createdAt`, and `expiresAt = createdAt + TTL`. It holds no permission or role.
- Content digest = lowercase hex SHA-256 over the domain `MyERP.FI.ReportManifest.v1\n`, the report kind, and then every row's fields in report order. Each field is length-prefixed (`len:value;`), so text cannot shift a field boundary. Amounts are written with trailing zeros dropped (`G29`), so SQL column scale does not change the digest. Instants are written as UTC ticks, so the offset does not change the digest either. AR／AP rows include their allocation IDs.
- Create checks:
  - Invalid binding (ADR-093 rules), a binding kind from the other report family, a missing report, TTL outside 1–60 minutes, or `createdAt` that is default or too close to the maximum → `Rejected` (400).
  - More than 10,000 members (the same cap as export) → `TooLarge` (422).
  - Report scope different from the binding (company, AR↔Receivable／AP↔Payable, partner, asOf, from, through, kind filter, currency), duplicate member IDs, rows whose open／signed amounts are wrong, or totals that differ from a checked recomputation → `Inconsistent` (500).
  - When several checks fail, the status is decided in the order 400 > 422 > 500.
- `PageArAp`／`PageOtherIncomeExpense` take the stored manifest, a re-read of the same frozen set, and a 1-based page number (1–1,000,000).
  - Missing manifest or re-read, or a page number out of range → `Rejected` (400).
  - A stored manifest that is not self-consistent → `Inconsistent`. That covers an invalid binding or wrong kind, cutoff ≤ 0, a malformed digest, member IDs that are non-positive, duplicated, over the cap, or a different count from the totals row count, totals for the wrong kind, and a TTL span outside 1–60 minutes.
  - The re-read must then match the manifest exactly: scope and cutoff, the member ID sequence (order included), the digest and the totals, and it must be internally consistent. Any difference → `Inconsistent` (500) with no page. A late or backdated source therefore never shifts an old page; the client has to take a fresh cutoff.
  - On success, pages use the manifest's bound page size. A page past the end is empty with `HasNext = false`.
- Expiry and token checks stay in `FiReportCursorTokenRules.Validate` (ADR-093, 409). This rule does not re-check them.

## Consequences

WU-BI-001a persists the manifest row with its member ID list and digest when the first page is served. On every continuation it does the following in order: validate the token (ADR-093), re-check authority, load the manifest, re-read the members at the manifest cutoff, and call `Page*`. An `Inconsistent` result is logged with a traceId and returned as 500, never as a partial page. Storing the member list (at most 10,000 IDs) keeps the re-read bounded. A different hash or canonical form would need a `v2` domain and a new ADR.
