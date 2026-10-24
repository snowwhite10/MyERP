# PM — Planned interfaces

Proposed interfaces: Equipment View/Create/Edit/Retire; Preventive Maintenance Plan View/Create/Edit; Due Maintenance View/Generate; Maintenance Order View/Create/Edit/Approve/Execute/Complete; Spare Request View/Issue/Return; Equipment History View. WU-PM-000d delivers no screen and allocates no `REQ_IO_A12`.

Implementation must provide three languages and light/dark states. Each screen shows company, equipment, plan/owner version, due key and timezone, status, unit and explicit requested/reserved/issued/returned/net quantities. PM estimate, INV confirmed movement and FI posted cost have distinct labels. Disabled actions explain state, scope or missing owner. Empty, denied, validation, server failure, partial and success states show precise counts and row reasons; pending INV or FI action cannot look completed. A history export uses one frozen source set and rechecks download permission.
