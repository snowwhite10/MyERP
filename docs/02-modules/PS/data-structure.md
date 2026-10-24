# PS — Planned data structure

WU-PS-000d allocates no `REQ_DS43` and creates no migration. Candidate company-scoped aggregates: project and state event, budget version and line, work log and correction, resource reservation, milestone and acceptance event, billing proposal and source claim. Implementation units must define decimal scales, FK indexes, unique keys including soft-deleted codes, optimistic versions, audit fields and reversible migrations. Cross-module customer, employee, SYS approval, SD invoice and FI journal IDs are scalar references without EF navigation or database FK.

```mermaid
erDiagram
    PS_PROJECT ||--o{ PS_BUDGET_VERSION : plans
    PS_BUDGET_VERSION ||--o{ PS_BUDGET_LINE : contains
    PS_PROJECT ||--o{ PS_WORK_LOG : records
    PS_PROJECT ||--o{ PS_RESOURCE_RESERVATION : allocates
    PS_PROJECT ||--o{ PS_MILESTONE : tracks
    PS_MILESTONE ||--o{ PS_ACCEPTANCE_EVENT : evidences
    PS_PROJECT ||--o{ PS_BILLING_PROPOSAL : bills
    PS_BILLING_PROPOSAL ||--o{ PS_BILLING_SOURCE_CLAIM : freezes
```

PS saves confirmed owner IDs, versions and cutoff tokens. It never stores copied HR employment, SD invoice or FI posted facts as authoritative records.
