# ADR-093 — FI report continuation token (offline rule)

Date: 2026-11-06. Status: Accepted (WU-BI-001a2).

## Context

BR-FI-130 binds an FI report read (AR／AP detail as-of, ADR-091; other income／expense, ADR-092) to one durable committed cutoff that every page and export reuses. BR-FI-131 says a malformed or filter-mismatched token is 400, an expired manifest is 409, and a token never carries authority: FI re-checks permission on every continuation, including after a role or partner-scope change. The SQL manifest table, permission checks and API (WU-BI-001a) wait for the SQL memory gate, but the token format and checks can be fixed offline.

## Decision

- `FiReportCursorTokenRules` (FI Domain) is pure. It reads no clock, key store or database. The caller passes the signing key, `IClock` "now", and the manifest `expiresAt` from the durable manifest (null when the manifest is gone).
- Token = `base64url(payload) "." base64url(HMAC-SHA256(key, "MyERP.FI.ReportCursor.v1\n" ‖ payload))`, without padding, at most 1,024 characters. The payload is canonical UTF-8 text with 14 `|` fields: `v1`, company, actor scope digest, report kind, partner, asOf, from, through, kind filter, currency, page size, cutoff sequence, manifest ID, next page number. It holds no permission, role or key material.
- Binding: company > 0. The actor scope digest is 64 lowercase hex characters: a SHA-256 the caller computes over the actor and the company／partner scope that actor was granted, so any scope change rejects old tokens. Report kind: `ArDetail`／`ApDetail` require asOf, allow an optional partner > 0, and forbid from／through／kind filter. `OtherIncomeExpense` requires from ≤ through, allows an optional kind filter of `OtherCollection`／`OtherPayment` only, and forbids partner／asOf. Currency is three uppercase letters. Page size is 1–200. Position: cutoff > 0, manifest > 0, next page 2–1,000,000. The 366-day limit stays with the read rule, which returns 422.
- `Validate` runs its checks in this order, and the first failing stage decides the result:
  1. Key missing or under 32 bytes → `Misconfigured` (500).
  2. Invalid repeated request binding or default "now" → `Rejected` (400) with field errors.
  3. Blank token → `Required`. Over-length, not exactly two parts, characters outside the base64url alphabet, or non-canonical encoding (non-zero trailing bits) → `Malformed` (400).
  4. MAC mismatch, compared in constant time with `CryptographicOperations.FixedTimeEquals` → `Tampered` (400).
  5. A correctly signed but non-canonical or invalid payload (wrong version or field count, leading zeros or signs, numeric or case-variant enum names, impossible dates, kind-inconsistent filters) → `Malformed` (400).
  6. Every bound field that differs from the request is listed as `Mismatch` (400), in binding order.
  7. Last, `now ≥ expiresAt` or a missing manifest → `Expired` (409). A tampered or mismatched expired token is therefore 400, not 409.
- The error responses name the fields that do not match. They expose nothing the client did not already send.

## Consequences

WU-BI-001a stores the manifest (cutoff, expiresAt) and issues a token for page 2 onward. On each continuation it authenticates, re-checks FI permissions and partner scope, computes the digest, calls `Validate`, and then reads the manifest rows. The key is loaded from secure configuration, never from the repo; tests use fixed test bytes. Rotating the key makes every outstanding token `Tampered` (400), and the client must start a fresh read explicitly. Supporting several keys at once would need a key ID field, a v2 payload and a new ADR.
