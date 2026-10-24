# QM — Planned interfaces

Proposed interfaces: Incoming Inspection Plan View/Create/Edit; Incoming Inspection Lot View/Record/Decide; Process Checkpoint View/Create/Edit; Process Inspection View/Record/Decide; Nonconformance View/Create/Disposition/Close; Quality Report View/Drill/Export. WU-QM-000d delivers no screen and allocates no `REQ_IO_A11`.

Screen implementation must provide three languages, light/dark states, source owner and version, units, inspected/accepted/rejected/pending quantities, plan version, decision and owner confirmation separately. Disabled actions explain why. Empty, denied, validation, server failure, partial and success states display precise counts and row reasons. A pending PUR/PP/INV owner action must not look like posted stock or accepted output. Report view and download expose one frozen cutoff and completeness status.
