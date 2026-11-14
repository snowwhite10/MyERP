# ADR-114 — FI legacy currency checks use a binary collation

Date: 2026-11-14. Status: Accepted (WU-FI-001f).

## Context

ADR-106 found that `LIKE '[A-Z][A-Z][A-Z]'` accepts lowercase codes under the database default collation `Chinese_Traditional_Stroke_Count_100_CI_AS_SC`, which is case-insensitive. The new `ar_sales_source` checks already used `COLLATE Latin1_General_BIN`. Three older checks still had the weakness: `ck_ar_credit_limit_currency`, `ck_ar_posted_receivable_currency` and `ck_gl_journal_currency`. A red real SQL test confirmed that `twd`, `Twd` and `TWd` were all saved.

## Decision

- Migration `Finance_CurrencyCheckBinaryCollation` drops the three checks and adds them again as `[base_currency] COLLATE Latin1_General_BIN LIKE '[A-Z][A-Z][A-Z]'`. The EF model (`CustomerCreditDbModel`, `GlJournalDbModel`) uses the same text, so there are no pending model changes.
- The checks are added `WITH CHECK` (the EF default) and stay trusted. If an existing row has a non-conforming code, `Up` fails and rolls back. The migration never rewrites saved rows: `gl_journal` is immutable, and silently changing posted history is forbidden. Before the migration, `myerp` had 0 non-conforming rows in all three tables.
- `Down` restores the old case-insensitive text, so the migration is reversible. Down and Up were both run against `myerp`.

## Known limitation

The columns are `char(3)` (non-Unicode). SQL Server converts an `nvarchar` value with a non-ASCII letter to the code page 950 of the column collation before the check runs. Best-fit mapping turns `ÄBC` into `ABC`, so the check sees `ABC` and accepts it. No CHECK constraint can see the original character. Non-ASCII input is the job of the MD currency master and API validation, which only accept codes that exist in the currency master. The test suite therefore covers only ASCII cases.

## Consequences

All FI currency checks now share one case-sensitive rule (BR-FI-148). Services already write uppercase codes taken from the MD currency master, so behaviour does not change. A future bug that writes lowercase codes is now stopped by the database.
