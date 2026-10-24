# QM — Design review and optimization log

## WU-QM-000d (2026-10-24)

| Finding | Cause | Change | Effect |
|---|---|---|---|
| Inspection approval could be mistaken for stock posting | PUR receipt and INV movement have separate owners | BR-QM-004–005 and QM-BASE-001,008 require explicit owner confirmation and no double availability | Pending owner outcome remains visible until confirmed |
| A revised plan could silently change historical decisions | Limits and sampling depend on an effective plan version | BR-QM-002 freezes plan version and limits per lot; QM-BASE-012 tests history | Old measurements remain reproducible |
| A quality report could change between pages and export | Decisions and owner outcomes continue committing | BR-QM-011 and QM-BASE-014 freeze source set and cutoffs | Drill and export must reconcile to displayed result |

Review covers design only. No QM code or product regression is claimed. Documentation validation and `git diff --check` are the checks for this slice.
