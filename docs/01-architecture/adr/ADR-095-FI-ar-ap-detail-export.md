# ADR-095 — FI AR／AP detail export (offline rule)

Date: 2026-11-06. Status: Accepted (WU-BI-001a4).

## Context

FI-RPT-014 requires an export at the same cutoff to have the same rows, order and totals as the screen, to escape formula-leading text, to include an empty header with zero totals, and never to return a partial file. ADR-092 fixed the other income／expense CSV (`OtherIncomeExpenseRules.Export`). ADR-091 (AR／AP detail read) and ADR-094 (manifest) had no export. The SQL read, permissions (BI VIEW／EXPORT), HTTP and BI Excel still wait for the SQL memory gate (WU-BI-001a／001b).

## Decision

- `ArApDetailAsOfRules.Export(report, headers)` is pure and returns CSV text without BOM (the API adds the UTF-8 BOM and streams the file), with CRLF line ends.
  - Lines: `CompanyId`, `Direction` (Receivable／Payable), `PartnerId` (blank = every partner visible to the reader), `AsOf`, `CutoffSequence`, `BaseCurrency`; then the caller's 17 translated headers; then every row in report order; then `Original`, `Credit`, `Settled`, `Open` full-set totals.
  - Row columns: business date, posting time (ISO 8601 round-trip with offset), partner ID, partner label, principal ID, source module, source type, source ID, document reference, journal ID, base currency, document currency, original, credit, settled, open, counted allocation number. Allocation IDs themselves stay in the digest (ADR-094), not in the file.
  - Text cells (including headers) use `FxDifferenceReconciliationRules.EscapeCsvText`; numbers and dates use invariant culture; decimals keep their stored scale.
- Rejections (no content, row count 0):
  - missing report or headers, wrong header count, null header cell → input error (400);
  - more than `MaxExportRows` = 10,000 rows → `report.rows` OutOfRange (422 at the API, same cap as the manifest);
  - a row that disagrees with itself or the report (open formula, outstanding flag, allocation count, direction／company／base currency, duplicate principal, overflow) → `report.rows` Inconsistent; totals that differ from a checked recomputation, or whose sum overflows → `report.totals` Inconsistent (500). New enum value `ArApDetailError.Inconsistent`.
- `FiReportManifestRules.ExportArAp(manifest, reread, headers)` is the entry the API uses for a frozen read. Missing manifest／re-read／headers or wrong header count → `Rejected` (checked first). Then the same checks as `PageArAp` (stored manifest self-consistency; re-read scope, cutoff, members in order, digest, totals) → `Inconsistent` with no content. On success it renders every member regardless of the bound page size, byte-identical to `ArApDetailAsOfRules.Export` of the same report. Expiry stays with `FiReportCursorTokenRules.Validate` (ADR-093); this rule has no clock.
- Addendum (2026-11-06, WU-BI-001a5): `FiReportManifestRules.ExportOtherIncomeExpense(manifest, reread, headers)` follows the same contract for other income／expense. It uses the shared request check (manifest → re-read → headers; null cell Required, count ≠ `OtherIncomeExpenseRules.ExportColumnCount` = 13 OutOfRange; all `Rejected`). Then `OtherRereadErrors`, now shared with `PageOtherIncomeExpense`, applies the manifest checks → `Inconsistent` with no content. On success the output is byte-identical to `OtherIncomeExpenseRules.Export` and ignores the bound page size.

## Consequences

WU-BI-001b validates the token, re-checks EXPORT authority, loads the manifest, re-reads at its cutoff and calls `ExportArAp` or `ExportOtherIncomeExpense`; `Inconsistent` is logged with traceId and returned as 500. A different column set or line layout needs a new ADR.
