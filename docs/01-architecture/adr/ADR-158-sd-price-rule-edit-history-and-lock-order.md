# ADR-158: Price rules are edited in place, history lives in the audit log, and key locks are taken in ordinal order

Date: 2026-11-21. Status: Accepted (WU-SD-005a9).

## Context

ADR-157 delivered create, list and detail for `sal_price_rule` and left three questions for the edit unit:

1. How to keep price history when a rule is edited: a new revision row per edit, a child history table, or in-place edit.
2. How to lock when an edit moves a rule to another key (item, currency or customer), so two crossing edits cannot deadlock.
3. What the user sees when the key lock is not granted. In WU-SD-005a8 SQL error 51020 came back as a 500.

## Decision

- **In-place edit, no revision table.** `PUT /api/v1/sal/price-rules/{id}` changes the row and raises `version` by one. The data audit trail interceptor already writes one `sys_audit_log` UPDATE row per change, with old and new values for each changed column, actor and time. Saved quotations and orders will keep their own rule ID, version, price and ceiling (BR-SD-029, WU-SD-005a11), so no document ever needs to look up an older version of a rule.
  - Rejected: a revision row per edit. Every quote and overlap query would need a "latest revision" filter, and the ID shown to users would change on each edit.
  - Rejected: a child history table. It would copy what the audit log already stores.
- **Only active rules change.** Editing or deactivating an inactive rule is 422 `SAL.PRICING.RULE_INACTIVE`. Deactivation is one-way. To use a retired price again, create a new rule; this keeps "inactive" meaning "no longer used" for the quote and for audits.
- **Version check.** The client sends the version it last read. A missing (< 1) or stale version is 409 `COMMON.CONCURRENCY.CONFLICT`, the same as other SD documents. The version is also the EF concurrency token.
- **Order of checks.** Edit: 400 fields → 403 `SAL.PRICING.EDIT` → 404 → 409 version → 422 inactive → MD 404/422 (the edited item, customer and currency must be valid, like create) → locks → reload and re-check → 409 overlap with the rule itself excluded. Deactivate: 403 `SAL.PRICING.DEACTIVATE` → 404 → 409 version → 422 inactive → lock → reload → save. The first read happens before the transaction so obvious errors take no lock.
- **Lock order.** An edit locks the rule's current key and its new key. One helper removes duplicates and sorts the lock names ordinally before calling `sp_getapplock` for each, so any two writers that need the same pair ask in the same order. If another writer moved the rule to a third key between the first read and the lock, the reload sees a new version and the edit is a 409.
- **Named lock timeout.** If `sp_getapplock` is not granted (15 s timeout, deadlock victim or cancel), the T-SQL throws 51020 and the service maps it to 409 `SAL.PRICING.RULE_LOCK_TIMEOUT` ("retry"). Create uses the same helper.
- MD checks always run on edit, even for unchanged references. A rule whose item later became inactive cannot be edited, but it can still be deactivated.

## Consequences

- SD-PRICE-HTTP-006～009 cover edit, deactivate, crossing key swaps (four rounds, no deadlock) and the named lock timeout on real SQL.
- Price history for a rule comes from `sys_audit_log` (table `sal_price_rule`, record ID). A later UI history view reads it there.
- The quote endpoint (WU-SD-005a10) and document provenance (WU-SD-005a11) can rely on "rule ID + version" naming one fixed set of values.
