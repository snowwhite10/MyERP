# ADR-092 — FI other income／expense read (offline rule)

Date: 2026-11-06. Status: Accepted (WU-BI-001a1).

## Context

BR-FI-129～132 define the FI other income／expense BI read: only Posted Other Collection／Other Payment sources in an inclusive business-date range, signed for net reporting, one durable committed set shared by pages and export, and whole-read 422 for missing snapshots, journals, inconsistent totals or overflow. The SQL reader, cutoff token and permissions (WU-BI-001a) are blocked behind the SQL memory gate, but the inclusion, sign and incompleteness rules can be fixed offline. ADR-090 (paging／CSV export with formula escaping) and ADR-091 (Invalid／Incomplete／Complete outcomes) are the patterns.

## Decision

- `OtherIncomeExpenseRules.Read(scope, sources)` is pure. The scope binds company, inclusive `from..through`, positive `CutoffSequence`, base currency, base decimals (0–4) and an optional kind filter (OtherCollection or OtherPayment; null = both).
- Range: default dates are `Required`; `through < from` is `OutOfRange`; more than 366 inclusive days (367 or more) is `RangeTooLong`. It is reported as an input error, but BR-FI-131 lists the range limit under 422, so the API maps `RangeTooLong` to 422 and every other input error to 400.
- Input errors (400): null scope／list; null item; non-positive IDs; other company (`CrossCompany`); undefined enum; an OtherCollection／OtherPayment source not matching the kind filter (`FilterMismatch`); blank or over-length reference (64); over-length category label (200), account code (32) or account name (200); non-positive category or journal ID; default dates; invalid currency; gross ≤ 0, beyond `decimal(19,4)` or base scale; commit sequence after the cutoff (`AfterCutoff`, never silently dropped).
- Replay: a source whose record is identical to an earlier one with the same ID adds no line and is counted (`ReplayedDuplicateCount`, BR-FI-130); the same ID with different content is `Duplicate`.
- Inclusion: Posted and `from ≤ businessDate ≤ through`. Draft／Pending／Rejected／RolledBack／Deleted and Posted out-of-range sources are excluded and counted, so an all-excluded read is explicit; excluded history never makes the read incomplete.
- Incomplete (422, ordered by source ID then reason, total overflow last): base currency other than the scope; missing category snapshot (ID or label); missing account snapshot (code or name); missing journal; an in-range Posted `Reversal`／`Compensation` source (`UnsupportedSourceType`, BR-FI-132 — also under a kind filter because its effect is unknown); full-set income, expense or net beyond `decimal(19,4)` (`Overflow`).
- Rows: ordered by (business date, posting time, kind, source ID); `signed = +gross` for collection and `−gross` for payment. Totals: positive income, positive expense, `net = income − expense` (equal to the sum of signed amounts), row／income／expense counts, all with checked decimals.
- `Page` (1-based, size 1–200, past end empty, no overflow for huge page numbers) and `Export` (company, cutoff, from, through lines; caller's 13 translated headers; rows; Income／Expense／Net trailer; ≤ 10,000 rows, else rejected whole; text through `FxDifferenceReconciliationRules.EscapeCsvText`) re-check a supplied report: a row whose signed amount does not follow its kind, or totals that do not equal the rows, is `Inconsistent` and no page／file is produced.

## Consequences

WU-BI-001a reads the committed other-source set under one cutoff token in a read-only transaction, enforces FI permissions before calling this rule, maps Invalid to 400 (`RangeTooLong` to 422) and Incomplete to 422 with source IDs. Reversal／compensation become reportable only after their source types are specified. Tokens, API and screens are not part of this decision.
