# PS — Proposed Application and HTTP boundary

WU-PS-000d creates no endpoint or Swagger contract. Planned `/api/v1/ps/` resources: `projects`, `budget-versions`, `work-logs`, `resource-reservations`, `milestones`, `billing-proposals`. Lists use stable server paging, sort whitelist, company/data scope and explicit source availability. Mutations use optimistic version and permanent command key for approval, billing and owner side effects. Rights separate VIEW, CREATE, EDIT, APPROVE, ACCEPT, BILL, EXPORT and DOWNLOAD.

PS validates customer/contract through MD and SD Application, employee through HR Application, approval through SYS Application, invoice through SD Application and posted actual/receivable through FI Application. Owner replies must include company, source ID/version, committed status and cutoff; PS does not read or write owner tables. Unknown owner result is queried by permanent key before declaring success. Later units define DTOs, Swagger examples and SQL/HTTP evidence.

ProblemDetails: 400 malformed input; 401 anonymous; 403 missing action; 404 hidden source; 409 duplicate, stale version or changed key; 422 invalid state/capacity/policy; 500 with traceId. Timeout is an unknown outcome with status lookup and no rate or contract data in error details.
