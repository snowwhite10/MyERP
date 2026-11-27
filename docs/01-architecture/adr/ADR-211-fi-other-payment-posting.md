# ADR-211: FI other payment atomic GL posting (WU-FI-008a4)

Date: 2026-11-27T20:03:14+08:00

Status: Accepted

## Context

BR-FI-060/061/062 require FI to post a saved other payment (non-AP expense) inside the SYS final-approval transaction:
re-read the saved Draft/Pending document, check MD category and fund account, use FI's versioned category→expense
account map (`gl_other_payment_account_map`, REQ_DS43.221), lock company/bank/reference, payment source and GL keys in
a fixed order, debit expense and credit bank, and write the permanent source `ap_other_payment_source`
(REQ_DS43.220). Three gaps existed:

1. The map stores an MD account **ID**, but `IChartOfAccounts` only looked accounts up by code.
2. The GL mapping-version contract `IGlPostingMappingVersionSource.ReadCurrentVersionAsync(companyId)` is per
   company, while the other payment map has one row (and version) per company **and category**.
3. BR-FI-060 says "exclude AP control, unapplied-payment clearing and prepaid assets" but no MD account flag names
   those roles.

## Decision

1. MD contract `IChartOfAccounts.FindByIdAsync(id)` (current company, includes inactive; null for missing, deleted,
   non-positive or foreign IDs). `ChartOfAccounts` shares one projection for code and ID lookups.
2. A scoped `OtherPaymentPostingScope` carries the category being posted. `OtherPaymentMappingVersionSource`
   (`FI/OtherPayment`) returns that category's map version, or 0 when no category is in scope (GL rejects). The joined
   GL session resolves it from the caller's scope, so it reads the same row in the same transaction.
3. The mapped account must be active, postable (no children) and of MD type **Expense or Cost**. Liabilities (AP
   control, unapplied clearing) and assets (prepaid expense) are therefore rejected with 422
   `FI.OTHER_PAYMENT_POSTING.ACCOUNT_INVALID` line 1; the bank ledger equal to the expense account is line 2.
4. `IOtherPaymentPostingService.PostInCurrentTransactionAsync(command)` (FI internal; 008b's SYS approval handler is
   the production caller). Order: shape 400 → company 404 → unlocked read (names the bank key only) → applock
   `ap_other_payment_bank:{company}:{bank}:{REFERENCE}` → applock `ap_other_payment:{company}:{payment}` → replay
   by the permanent source row (same version+fingerprint → Unchanged, otherwise 409 SOURCE_CONFLICT) → UPDLOCK
   re-read → state 422 (Draft/Pending/Approved post; Rejected/deleted/Posted-without-source do not) → claim 409
   SOURCE_MISMATCH (version, `OtherPaymentFingerprint` v1, or bank/reference changed between the reads) → content
   422 → posted-reference 409 → MD category/fund 422 → map 422 → accounts 422 → `IGlJoinedPostingService` (period
   lock and Open-period checks stay GL's) → EF typed insert of the source row and Draft → Posted in one SaveChanges.
   Rejected keeps the caller's transaction open; Fault rolls it back.
5. The fingerprint covers company, ID, category, date, currency, amount, bank and reference; payee, remark, number
   and status are excluded so the approval transition does not change it (any edit still bumps the version).

## Consequences

- 008b must call the service from the SYS final-approval handler with the version and fingerprint it saved, and map
  Rejected/Fault to the HTTP errors of BR-FI-065.
- Account-role exclusion relies on MD account type. A company that books a prepaid expense under an Expense-type
  account is not detected; a dedicated role flag would need an MD change request.
- Withdrawal and deposit references use different applock prefixes and tables, so equal references in collection
  and other payment never collide (BR-FI-062 direction scope).
