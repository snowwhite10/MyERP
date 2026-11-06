# ADR-099 — FI report first-page coordinator (offline rule)

Date: 2026-11-06. Status: Accepted (WU-BI-001a9).

## Context

ADR-098 covers continuation pages only. The first page has to read FI, create the ADR-094 manifest, save it to get an ID, return page 1 and sign the first ADR-093 token. If the endpoint orders these itself, it can read FI before a short key or bad filter is found, return a page whose manifest was never saved (so the token points nowhere), or sign a token with an ID the store did not confirm.

## Decision

- `FiReportContinuationRules.FirstArAp`／`FirstOtherIncomeExpense(request, actor, key, now, ttl, read, saveManifest)` reuse `FiReportContinuationRequest` (its `Token` must be null) and return the same `FiReportContinuationResult<TPage>`. `read(binding)` reads the frozen report; `saveManifest(manifest)` stores it and returns the manifest ID.
- Fixed order, with nothing read before step 4:
  1. Null inputs, a wrong report family, a token present, or a TTL outside 1–60 minutes → 500 with `InternalDetails` only.
  2. Invalid actor scope input → 500; a key shorter than 32 bytes → 500.
  3. Default clock and binding validation are mapped together through ADR-096: client filters → 400 per field; company ≤ 0 → 500.
  4. Read, then `FiReportManifestRules.Create*`: a missing report or any disagreement → 500, more than 10,000 rows → 422. Nothing is saved.
  5. Save. A null or non-positive ID → 500 with no page and no token.
  6. Page 1 from the same report; a token for page 2 (cutoff, saved ID) only when `HasNext`.
- The manifest is saved even when everything fits on one page, so the export (ADR-095) can always reuse the frozen read.
- Exceptions thrown by the callbacks propagate to the endpoint's unknown-outcome 500.

## Consequences

WU-BI-001b only wires the FI read, the SQL manifest insert, the key, `IClock` and the SYS-resolved actor scope, and chooses `First*` or `Continue*` by whether the query carries a token. Authorization (VIEW, partner filter within scope, 401／403／404) still stays with the endpoint and the FI read contract. A save that succeeds and is followed by an unreachable page failure leaves an orphan manifest that expires by TTL; no page is returned.
