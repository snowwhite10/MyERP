# QM — Proposed Application and HTTP boundary

WU-QM-000d creates no endpoint or Swagger contract. Planned `/api/v1/qm/` resources: `inspection-plans`, `incoming-inspections`, `process-inspections`, `nonconformances`, `quality-reports`. Lists use company/scope filters, stable paging, sort whitelist and explicit owner availability. Commands carry optimistic version and permanent decision key. Distinct VIEW, CREATE, RECORD, SUBMIT, APPROVE, DISPOSITION, CLOSE and EXPORT rights apply.

QM calls PUR for saved receipt line, allowed quantity and receipt action; PP for released operation, completed quantity and rework outcome; INV for confirmed stock/quarantine movement; MD for item and unit metadata; SYS for final approval. Owner replies must include source version, committed identity, company/scope, unit, quantity, cutoff and unavailable reason. No QM API may update `pur_`, `pp_` or `inv_` tables directly. Final implementation must define same-transaction or durable pending confirmation with each owner before claiming completion.

ProblemDetails: 400 malformed input, 401 anonymous, 403 missing right, 404 hidden source/case, 409 duplicate or stale version/key, 422 invalid measure/state/quantity, 500 with traceId. Timeout reports unknown outcome and offers key-based lookup. Product SQL, HTTP and Swagger proof belongs to later units.
