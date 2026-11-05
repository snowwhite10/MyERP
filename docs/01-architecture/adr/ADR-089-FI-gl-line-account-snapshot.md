# ADR-089 — Persisted GL line account name/version and mapping-version snapshot

Date: 2026-11-06. Status: Accepted (WU-FI-004a3p).

## Context

BR-FI-027 requires FI to keep immutable account and mapping snapshots on Posted journal lines so a later MD rename, version change or FI mapping change cannot rewrite history. The offline rules (`GlAccountSnapshotRules`, `GlJournalLineDraftRules`, `GlJournalDraftRecheckRules`, WU-FI-004a3e–o) already carry scalar account ID/code/name/account version and FI mapping version, but `gl_journal_line` (migration `Finance_GlJournalFoundation`) persisted only account ID and code. No Posted writer exists yet, so no saved line depends on the old shape.

## Decision

- `gl_journal_line` gains `account_name nvarchar(200) NOT NULL`, `account_version int NOT NULL` and `mapping_version int NOT NULL` (migration `20261105185102_Finance_GlJournalLineAccountSnapshot`, reversible Down drops the check and columns).
- Check `ck_gl_journal_line_account_snapshot`: `LEN(TRIM(account_name)) > 0 AND account_version > 0 AND mapping_version > 0`. Name length 200 matches the MD account name limit and the offline rule bound; a longer value is rejected by SQL truncation, never silently cut.
- The values are copied once from the transaction-time MD `IChartOfAccounts` result and FI mapping version; FI never joins `md_account` and adds no cross-module FK or navigation.
- No backfill: the empty/zero defaults EF adds for existing rows violate the check, so the migration fails if any legacy line exists instead of fabricating account history.

## Consequences

The future `IGlPostingService` writer (WU-FI-004a3) must populate all three fields from the `GlJournalLineDraft` it rechecked under the company period lock. MD `IAccountUsage` for Posted references, atomic posting, and update/delete immutability remain WU-FI-004a3. Reports may display the saved name for historical lines without consulting the current MD name.
