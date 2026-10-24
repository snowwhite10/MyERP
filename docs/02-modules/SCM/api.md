# SCM — Proposed Application and HTTP boundary

WU-SCM-000d creates no endpoint or Swagger contract. Planned `/api/v1/scm/` resources: `forecast-versions`, `forecast-comparisons`, `consignments`, `carrier-events`, `delivery-exceptions`, `supplier-invitations`, `order-grants` and `supplier-proposals`. Lists use stable server paging, sort whitelist, company/supplier/data scope and source availability. Mutations use optimistic version and permanent command/event keys. Rights separate VIEW, CREATE, EDIT, APPROVE, REVIEW, EXPORT and DOWNLOAD.

SCM obtains approved demand from SD, released production demand from PP, posted movements from INV, order and receipt state from PUR, supplier identity from MD, and account/approval from SYS Application contracts. Owner replies must carry company, source ID/version, committed status and cutoff. Missing contracts are named unavailable. Unknown outcomes are queried by permanent key before success is reported. Later units define DTOs, Swagger examples and SQL/HTTP evidence.

ProblemDetails: 400 malformed input; 401 anonymous; 403 missing action; 404 hidden order/supplier; 409 duplicate key or stale version; 422 invalid state, quantity or policy; 500 with traceId. Timeout is unknown outcome with status lookup.
