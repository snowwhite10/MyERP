# CO — Proposed Application and HTTP boundary

WU-CO-000d implements no endpoint or Swagger contract. Proposed `/api/v1/co/` resources are `cost-centers`, `profit-centers`, `assignment-policies`, `product-cost-runs`, `budgets` and `budget-comparisons`. Lists use scoped filters, sort and stable paging. Commands use optimistic version and permanent idempotency/source keys; separate VIEW, CREATE, EDIT, APPROVE, DEACTIVATE, RUN, DRILL and EXPORT permissions apply.

CO Application contracts request FI Posted actual lines and committed cutoff, INV confirmed movement and valuation status, PP actual work/accepted output, MD item/account metadata and SYS approval decisions. Every owner result carries company/scope, source IDs, cutoff, completeness and missing-source reason. BI may consume a CO-only read contract for management reports; it cannot query CO tables. Product cost approval returns a CO baseline ID, never a FI journal or INV posting ID.

Expected ProblemDetails: 400 malformed input, 401 anonymous, 403 denied, 404 hidden scoped identity, 409 duplicate/version/source conflict, 422 invalid hierarchy, incomplete mapping or unavailable cost source, and 500 with traceId. Timeout returns unknown outcome until permanent-key lookup proves commit/rollback. True SQL, HTTP and Swagger proof belong to later work units.
