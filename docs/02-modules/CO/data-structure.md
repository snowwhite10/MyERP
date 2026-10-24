# CO — Planned data structure

WU-CO-000d allocates no `REQ_DS43` ID and adds no migration. Candidate CO-owned aggregates are cost center, profit center, effective assignment policy, cost run with source manifest and components, and budget header/version/line. Exact columns, indexes, unique constraints and delete rules belong to the SQL implementation slices. Every record has company scope, audit and optimistic version. Cross-module owner references remain IDs without SQL foreign keys.

```mermaid
erDiagram
    CO_COST_CENTER ||--o{ CO_COST_CENTER : parent_of
    CO_PROFIT_CENTER ||--o{ CO_PROFIT_CENTER : parent_of
    CO_ASSIGNMENT_POLICY ||--o{ CO_COST_RUN : applies_to
    CO_COST_RUN ||--o{ CO_COST_COMPONENT : contains
    CO_COST_RUN ||--o{ CO_SOURCE_REF : freezes
    CO_BUDGET ||--o{ CO_BUDGET_VERSION : revises
    CO_BUDGET_VERSION ||--o{ CO_BUDGET_LINE : contains
```

This diagram is conceptual. FI journals, INV movements, PP work orders and MD items/accounts remain in their owner modules.
