# HR — Human resources business rules

## WU-HR-000d baseline

| Rule | Design invariant |
|---|---|
| BR-HR-001 | HR owns employee and workforce facts. SYS owns user accounts, company/branch/department identity, approval engine and audit; FI owns posted journals and bank/cash facts. Cross-owner access uses Application contracts, never tables. |
| BR-HR-002 | Employee code is unique per company including soft-deleted history. Personal identifiers are protected, masked in lists and excluded from generic exports/logs; retention and legal basis are explicit. |
| BR-HR-003 | Employment and assignment are effective dated, non-overlapping for a company and employee; termination retains history and cannot silently disable a SYS user. |
| BR-HR-004 | Employee-to-SYS-user link is optional and verified by SYS company and status. HR employment permission never grants SYS login or organisation administration. |
| BR-HR-005 | Published roster freezes version, timezone and shift calendar; attendance events keep original clock evidence and corrections append events. Overnight, daylight/timezone and duplicate clocks have deterministic rules. |
| BR-HR-006 | Attendance exception approval uses SYS workflow, separate from author. Payroll reads only approved events through a committed cutoff. Later corrections create an adjustment in a later run. |
| BR-HR-007 | Leave grants, consumption and reversals are effective dated immutable events; balance is derived at a stable cutoff. Pending leave does not consume final entitlement; approved overlapping leave is rejected. |
| BR-HR-008 | Payroll run snapshots employee eligibility, approved attendance/leave, pay components, currencies, rate/policy versions and cutoff; decimal gross minus deductions equals net, with rounding rule/version declared. |
| BR-HR-009 | Payroll lifecycle Draft → Calculated → Approved → Released or Reversed; release and payment delivery are distinct. A permanent run/employee/period key and payload fingerprint make retries idempotent; changed replay conflicts. |
| BR-HR-010 | FI posting is requested through FI Application with a permanent source key. Unknown or failed FI result remains PendingOwner/Failed; HR never writes `gl_` and never calls a payroll run Posted before FI confirms. |
| BR-HR-011 | Payroll banking or payslip export requires separate permission, encrypted delivery and recipient verification; preview/zero-row/partial outcomes show actual counts and reasons. Provider and statutory rules require an ADR before implementation. |
| BR-HR-012 | Recruitment candidate data has consent, retention and restricted scope. Accepted candidate conversion creates an HR employee only after a confirmed, idempotent HR transaction; duplicate candidates are reviewed, not automatically merged. |
| BR-HR-013 | Performance template and review freeze versions, reviewer and acknowledgement; manager changes do not rewrite history. Restricted feedback is absent from unauthorised counts, exports and error details. |
| BR-HR-014 | Training eligibility uses employee status, prerequisite and session capacity at reservation. Concurrent last-seat requests admit only one; waitlisted and skipped candidates have per-row reasons. |
| BR-HR-015 | Every command and read checks action permission, company and data scope; self-service grants access only to own permitted fields. Other-company IDs are hidden as 404 and missing action rights return 403. |
| BR-HR-016 | Lists, balances, payroll previews and exports use a stable committed source set and cutoff. Missing SYS/FI owner data is unavailable, never a zero value or fabricated approval. |
| BR-HR-017 | Invalid input is 400, duplicate/version/key conflict 409, invalid state or policy 422; 500 includes traceId. Unknown timeout is resolved by command-key lookup before displaying success. |
| BR-HR-018 | HR audit records actor, company, scope, source and policy versions, reason and outcome without raw personal or bank data. Delete/anonymise follows retention and legal-hold rules without rewriting financial history. |

Implementation ADRs must settle jurisdiction-specific payroll/statutory rules, timezone/calendar conventions, SYS identity/approval contract, FI posting atomicity, sensitive-data retention and secure payment/payslip delivery before executable work claims acceptance.
