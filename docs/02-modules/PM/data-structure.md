# PM — Planned data structure

WU-PM-000d assigns no `REQ_DS43` and adds no migration. Candidate PM aggregates are equipment, plan/version, meter reading, due occurrence, work order, checklist result, downtime event, spare request/line and owner outcome. Implementation units specify real columns, decimal scales, unique/foreign keys, indexes, check constraints and reversible migrations. All PM aggregates are company-scoped, audited and versioned. Cross-module IDs have no SQL foreign key or EF navigation.

```mermaid
erDiagram
    PM_EQUIPMENT ||--o{ PM_PLAN : maintained_by
    PM_PLAN ||--o{ PM_PLAN_VERSION : revises
    PM_PLAN_VERSION ||--o{ PM_DUE_OCCURRENCE : schedules
    PM_EQUIPMENT ||--o{ PM_WORK_ORDER : serviced_by
    PM_DUE_OCCURRENCE ||--o| PM_WORK_ORDER : generates
    PM_WORK_ORDER ||--o{ PM_CHECKLIST_RESULT : verifies
    PM_WORK_ORDER ||--o{ PM_DOWNTIME : records
    PM_WORK_ORDER ||--o{ PM_SPARE_LINE : requests
    PM_SPARE_LINE ||--o{ PM_OWNER_OUTCOME : confirms
```

FI asset and GL entries, INV reservations and movements, MD items and locations, and SYS approvals remain owner data. PM stores immutable source IDs, versions and confirmation tokens for traceability only.
