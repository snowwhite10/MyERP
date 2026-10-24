# PM — Proposed Application and HTTP boundary

WU-PM-000d creates no endpoint or Swagger contract. Planned `/api/v1/pm/` resources: `equipment`, `maintenance-plans`, `due-occurrences`, `maintenance-orders`, `spare-requests` and `history`. Lists require stable paging, sort whitelist, company/scope filters and explicit owner availability. Commands carry optimistic version and permanent occurrence or owner command key. Separate VIEW, CREATE, EDIT, RETIRE, GENERATE, APPROVE, EXECUTE, COMPLETE, ISSUE, RETURN and EXPORT permissions apply.

PM asks MD for site, item and unit metadata; INV for stock, reservation, issue, return and movement confirmation; FI for optional asset identity and posted cost; SYS for approval. Owner replies need company/scope, source ID/version, unit, quantity, committed identity, cutoff and unavailable reason. PM never reads or writes `inv_`, `fa_`, `gl_` or MD tables directly. Implementation must define atomic contract or durable pending confirmation before an order or spare line may claim owner completion.

ProblemDetails: 400 malformed input, 401 anonymous, 403 missing right, 404 hidden source/order, 409 duplicate or stale version/key, 422 invalid state/quantity/trigger, 500 with traceId. Timeout reports unknown outcome and supports permanent-key lookup. Real SQL, HTTP and Swagger evidence belongs to later units.
