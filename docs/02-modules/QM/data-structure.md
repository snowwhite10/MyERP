# QM — Planned data structure

WU-QM-000d assigns no `REQ_DS43` and adds no migration. Candidate QM-owned aggregates are inspection plan/version/characteristic, incoming and in-process lot/observation/decision, nonconformance case/disposition, and frozen report manifest. Implementation slices will specify actual columns, decimal scale, unique keys, indexes, constraints and reversible migrations. Every aggregate is company-scoped, audited and versioned. Cross-module source IDs have no SQL foreign key or EF navigation.

```mermaid
erDiagram
    QM_INSPECTION_PLAN ||--o{ QM_PLAN_VERSION : revises
    QM_PLAN_VERSION ||--o{ QM_CHARACTERISTIC : defines
    QM_PLAN_VERSION ||--o{ QM_INSPECTION_LOT : selected_for
    QM_INSPECTION_LOT ||--o{ QM_OBSERVATION : measures
    QM_INSPECTION_LOT ||--o{ QM_DECISION : decides
    QM_DECISION ||--o| QM_NONCONFORMANCE : fails_into
    QM_NONCONFORMANCE ||--o{ QM_DISPOSITION : resolves
    QM_REPORT_MANIFEST ||--o{ QM_REPORT_SOURCE : freezes
```

PUR receipt, PP work order/operation, INV movement/quarantine and MD item remain owner data. Source keys and owner confirmation tokens are stored as evidence, not replicated editable balances.
