# PS — Design review and optimization log

## WU-PS-000d (2026-10-24)

| Finding | Cause | Change | Effect |
|---|---|---|---|
| A PS billing proposal could be mistaken for an invoice or receivable | PS, SD and FI own distinct states | BR-PS-009–010 and PS-BASE-003,006,013,015 require confirmed owner keys and distinct states | Unknown owner outcome remains pending |
| A budget comparison could imply zero actual when FI is unavailable | Actuals are owned by FI | BR-PS-004,013 and PS-BASE-001,012,014 require named posted cutoff and unavailable source | Comparison cannot silently understate actuals |
| Concurrent reservations or billing could overcommit | Capacity and source claims are shared resources | BR-PS-007,009 and PS-BASE-005–008,013 require atomic claims | One committed winner and reconciled row results |
| Corrected time or reopened milestone could rewrite billed history | Approved evidence has downstream effects | BR-PS-005,008 and PS-BASE-011,014 require append-only adjustment | Prior proposal and invoice remain reproducible |

Review covers design only. No PS code or product regression is claimed. Documentation validation and `git diff --check` are the checks for this slice.
