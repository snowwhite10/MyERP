# ADR-091 — FI AR／AP detail as-of read (offline rule)

Date: 2026-11-06. Status: Accepted (WU-BI-001a0).

## Context

BR-FI-125～128 define the FI AR／AP detail BI reads as an as-of business-date view over one durable committed set, with open base computed by checked decimal, explicit 422 incompleteness and full-set totals shared by every page. The SQL reader, cutoff token and permissions (WU-BI-001a) are blocked behind the SQL memory gate, but the arithmetic, inclusion and incompleteness rules can be fixed and proven offline. ADR-090 (`FxDifferenceReconciliationRules`) is the pattern for a frozen-set read with paging.

## Decision

- `ArApDetailAsOfRules.Read(scope, principals, allocations)` is pure. The scope binds company, direction, optional partner filter (null = every partner the reader already scoped as visible), inclusive `asOf`, positive `CutoffSequence`, base currency, base decimals (0–4) and the zero-open history option.
- Three outcomes: **Invalid** (400) — input errors per collection／index／field; **Incomplete** (422) — named reasons with principal, source and allocation IDs, no report; **Complete** — the report. Input errors take precedence.
- Input errors: null or invalid scope; duplicate principal or allocation ID; a second principal for the same FI source (module, type, source ID) — a replay must not double-count; other company (`CrossCompany`); principal direction or partner not matching the bound filter (`FilterMismatch`, the manifest is bound to them); undefined enums; blank or over-length partner label (200), source module／type (32) and document reference (64); default dates or posting time; invalid currency codes; amounts ≤ 0, beyond `decimal(19,4)` or base scale; non-positive journal IDs; any row whose commit sequence exceeds the cutoff (`AfterCutoff`, never silently dropped).
- Inclusion: a principal enters only when Posted and `businessDate ≤ asOf`; an allocation counts only when Posted and `businessDate ≤ asOf`. Draft／Pending／Rejected／RolledBack／Deleted principals, later principals and non-Posted or later allocations are excluded and counted, so an all-excluded read is explicit.
- Incomplete reasons (ordered by principal ID, reason, allocation ID; a total overflow last): included principal or counted allocation with a base currency other than the scope (`CurrencyMismatch`, BR-FI-126) or no journal (`MissingJournal`); counted allocation whose principal is not in the set (`MissingPrincipal`), not Posted (`AllocationOnNonPostedPrincipal`) or dated after it (`AllocationBeforePrincipal`, including a principal after `asOf`); `open < 0` (`NegativeOpen`); credit, settlement or their sum beyond `decimal(19,4)` per principal, or any full-set total beyond it (`Overflow`). Excluded history never makes the read incomplete. The rule never invents a balancing row.
- Rows: `open = original − credit − settlement` (checked), ordered by (business date, posting time, principal ID), with saved partner label, source module／type／ID／reference, journal ID, both currencies, allocation IDs ascending and count; `IsOutstanding` only when `open > 0`. Zero-open rows are omitted (and counted) unless history is requested; when included they enter original／credit／settled totals but never the outstanding count. Totals (original, credit, settled, open, row count, outstanding count) cover every report row, so `open = original − credit − settled` holds.
- `Page(report, pageNumber, pageSize)` slices the same rows (1-based, size 1–200, a page past the end is empty, no overflow for large page numbers) and every page carries the cutoff, `asOf` and full-set totals.

## Consequences

WU-BI-001a reads the committed principal／allocation set under one cutoff token in a read-only transaction, enforces FI permissions and partner scope before calling this rule, maps Invalid to 400 and Incomplete to 422 with the IDs. Reversal／compensation source types stay excluded until specified (BR-FI-132). Other income／expense (BR-FI-129／130), tokens, export, API and screens are not part of this decision.
