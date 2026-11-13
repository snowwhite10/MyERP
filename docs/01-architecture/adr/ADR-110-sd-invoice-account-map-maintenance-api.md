# ADR-110 — SD invoice account map maintenance: singleton resource, version 0 means create (WU-FI-002a4b)

Date: 2026-11-14. Status: Accepted (WU-FI-002a4b).

## Context

ADR-109 created `gl_sales_invoice_account_map` (one row per company, row version = GL `SD/Invoice` mapping version, never deleted). Finance users need to read and maintain it over HTTP with permissions, optimistic concurrency and audit, before the 002a4c screen.

## Decision

- **Singleton route** `api/v1/gl/sales-invoice-account-map` with only `GET` and `PUT`. There is no id (the company is the key), no POST and no DELETE (the row must never be deleted, ADR-109).
- **GET never 404s.** An unconfigured company returns `configured=false`, `version=0` and null accounts, so the screen can show an explicit "not set up — invoices cannot be posted" state instead of treating it as an error.
- **PUT is create-or-update by version.** `version=0` means "I expect no map"; otherwise it must equal the saved version. Any mismatch (0 while a row exists, stale or future number) is 409 `FI.SALES_MAP.VERSION_CONFLICT`. Two concurrent first saves are resolved by the unique company index (SQL 2601/2627) and concurrent updates by the EF version token; the loser gets the same 409.
- **Zero change does not create a version.** Codes are trimmed; if they equal the saved codes the response is `changed=false` with the unchanged version. Because the row version is the mapping version, a spurious increment would make in-flight postings fail with `MAPPING_CHANGED` for no reason.
- **Accounts validated through `IChartOfAccounts.FindAsync` at save time** (missing → `MD.ACCOUNT.NOT_FOUND`, inactive → `MD.ACCOUNT.INACTIVE`, has children → `MD.ACCOUNT.NOT_POSTABLE`). All failures are reported together: 422 `FI.SALES_MAP.ACCOUNT_INVALID` with a ProblemDetails extension `accountErrors` keyed by request field. The unchanged path skips this check; GET returns each account's current `name`, `isActive` and `isPostable` so a later MD deactivation is visible. Posting re-validates regardless (ADR-108).
- **Mapped accounts are "in use" for MD.** `FinanceAccountUsage` also reports codes referenced by the company's map, so MD refuses delete, balance-direction change and new child accounts (which would make the account non-postable). Deactivation stays allowed (MD does not consult usage for it); GET and posting expose the consequence.
- **Permissions** `GL.SALES_MAP.VIEW` / `GL.SALES_MAP.EDIT` (FI module: seed FINANCE gets both; MANAGER and VIEWER get VIEW through the existing `.VIEW` rule). Audit is the standard `sys_audit_log` CREATE/UPDATE written by the SaveChanges interceptor.
- **No Development demo map row in this slice.** The MD demo chart has no output-tax account (2171 is accrued expenses); seeding a map would need an MD seed change. Deferred to WU-FI-002a4c, where the real-API E2E needs it.

## Consequences

- Production companies can be set up for SD invoice posting through the API.
- The frontend (002a4c) handles: empty state, VIEW-only read, 409 reload, 422 per-field messages from `accountErrors`, `changed=false` warning.
