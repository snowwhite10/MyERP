# CO — Design review and optimization log

## WU-CO-000d (2026-10-24)

| Finding | Cause | Change | Effect |
|---|---|---|---|
| FI actuals could be mistaken for an ordinary live query | Budget comparison spans pages, drill and export while FI continues posting | Require a frozen FI committed source set and budget version in BR-CO-010 and CO-BASE-013 | Later implementation must preserve one result across pages and expose its cutoff |
| Provisional INV valuation could be interpreted as approved product cost | INV may return `Unpriced` and PP actuals are not yet available | Require named incomplete/unavailable status in BR-CO-004–006 and CO-BASE-007 | No zero-valued invented cost is accepted |
| Center edits could rewrite historical reporting | Hierarchy and assignment policy both have effective dates | Freeze saved assignment snapshots and preserve prior policy versions in BR-CO-001–002 | Historical FI attribution remains reproducible |

Review is limited to design. There is no CO code to optimize or product regression to claim. DocGen source tests and `git diff --check` validate the documentation slice.
