# SCM — Optimization log

## WU-SCM-000d design review

Reviewed proposed owner boundaries, source cutoffs, concurrency keys, rights and result semantics before implementation. No product code changed, so no runtime optimisation or product test result is claimed. Implementation units must revisit query plans, source manifest size, event deduplication indexes and portal authorization after real SQL evidence exists.

## WU-SCM-001d design review

Found that the baseline's generic `source cutoff` could be mistaken for a wall-clock timestamp, while current INV queries are live and MD conversion has no version evidence. Defined opaque committed owner cursors, fixed membership/count/digest and conversion snapshot requirements. Chose one explicit, versioned first method and kept PP/INV comparison separate to prevent double counting. Source pagination, retention size and SQL indexing remain implementation review items. No product code or runtime optimisation was performed.

## WU-SCM-001e design review

Checked actual Sales, Inventory and MasterData Contracts and the missing Production project. Found a prior `WU-INV-016a` ID already used; allocated the posted-history prerequisite as `WU-INV-020a`. Defined a 90-day replay floor, reauthorisation on every page, correction as a later commit/cursor, and SCM-owned published manifest retention. This avoids live query drift and preserves historical forecast evidence. SQL storage/index design and performance remain for each owner implementation unit. No runtime optimisation or product test is claimed.
