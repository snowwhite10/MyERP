# PM — Design review and optimization log

## WU-PM-000d (2026-10-24)

| Finding | Cause | Change | Effect |
|---|---|---|---|
| Work order completion could be mistaken for INV issue or FI cost posting | PM, INV and FI have separate owners | BR-PM-009–012 and PM-BASE-003,013 require confirmed owner identities and separate labels | Pending movement or cost remains visible until owner confirmation |
| Calendar or meter retries could create duplicate orders | Schedule and readings may be replayed concurrently | BR-PM-004–006 use permanent occurrence keys and fingerprints; PM-BASE-005–006 cover races | One eligible occurrence creates at most one order |
| Retiring equipment could orphan open work and reserved stock | Retirement and reservation belong to different lifecycles | BR-PM-003 and PM-BASE-013 require open-order and INV outcome checks | Retirement is blocked until obligations are resolved |
| Later owner posting could change history between pages | INV and FI commits have independent cutoffs | BR-PM-015 and PM-BASE-014 freeze source IDs and cutoffs | Drill and export remain reconcilable |

Review covers design only. No PM code or product regression is claimed. Documentation validation and `git diff --check` are the checks for this slice.
