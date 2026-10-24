# SCM — Planned data structure

WU-SCM-000d allocates no `REQ_DS43` and creates no migration. Candidate company-scoped aggregates: forecast run/version, source manifest, bucket and override; consignment, carrier event and delivery evidence; supplier invitation, order grant, proposal and decision. Implementation units must define money/quantity scales, all local FK indexes, unique keys including soft-deleted history, optimistic versions, audit fields and reversible migrations. Foreign module IDs remain scalar references without EF navigation or database FK.

```mermaid
erDiagram
    SCM_FORECAST_VERSION ||--o{ SCM_FORECAST_BUCKET : contains
    SCM_FORECAST_VERSION ||--o{ SCM_SOURCE_MANIFEST : freezes
    SCM_FORECAST_BUCKET ||--o{ SCM_FORECAST_OVERRIDE : adjusts
    SCM_CONSIGNMENT ||--o{ SCM_CARRIER_EVENT : tracks
    SCM_CONSIGNMENT ||--o{ SCM_DELIVERY_EVIDENCE : proves
    SCM_SUPPLIER_INVITATION ||--o{ SCM_ORDER_GRANT : scopes
    SCM_ORDER_GRANT ||--o{ SCM_SUPPLIER_PROPOSAL : submits
    SCM_SUPPLIER_PROPOSAL ||--o{ SCM_PROPOSAL_DECISION : reviews
```

SCM stores confirmed owner IDs, versions and cutoff tokens. SD demand, PP plans, INV postings, PUR order/receipt and MD supplier records remain authoritative in their own modules.
