# CRM — Design review and optimization log

## WU-CRM-000d (2026-10-24)

| Finding | Cause | Change | Effect |
|---|---|---|---|
| Qualified lead could be mistaken for an MD customer | Conversion crosses ownership and can time out | BR-CRM-003–004 and CRM-BASE-007–008 require confirmed identity and permanent key | Pending conversion cannot be shown as completed |
| Won opportunity could inflate orders or revenue | CRM estimate and SD/FI posted facts have different owners | BR-CRM-005–007 and CRM-BASE-002,016 separate estimate, link and actual | Pipeline never claims an unconfirmed sale |
| Frozen campaign audience could include withdrawn consent | Audience selection and actual delivery occur at different times | BR-CRM-009–012 and CRM-BASE-009–010 recheck consent and show each skipped reason | Delivered totals match confirmed provider outcomes |
| Ticket SLA could shift after policy edits | Ticket and calendar versions can change independently | BR-CRM-013–014 and CRM-BASE-005,015 freeze policy and derive clocks from events | SLA is reproducible after edits and reopening |

Review covers design only. No CRM code or product regression is claimed. Documentation validation and `git diff --check` are the checks for this slice.
