# HR — Human resources requirements

## WU-HR-000d design baseline (2026-10-24)

HR owns employment records, workforce structure, rosters, attendance, leave, payroll calculations, recruitment, performance and training. SYS owns login identities, permissions and audit; FI owns accounting periods and posted journals. An employee may have a linked SYS user, but neither identity implies the other. This unit supplies design only: no migration, endpoint, screen or product acceptance.

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F41114.1.1 | 1.0 | System should allow user to register and revise an employee's employment record. | Developer | Company, unique employee code, legal identity reference, start/end dates, status, version and restricted personal fields are validated and audited; only authorised HR roles can see sensitive fields. |
| REQ_F41114.1.2 | 1.0 | System should enable user to transfer, suspend or end employment. | Developer | Effective dated changes preserve prior assignments, reject overlapping active employment and do not silently change a linked SYS login. |
| REQ_F41114.2.1 | 1.0 | System should allow user to assign employees to a workforce unit and manager. | Developer | HR assignment has effective dates and a version; SYS company/branch/department identity is verified through its Application contract, without HR altering SYS organisation or access grants. |
| REQ_F41114.2.2 | 1.0 | System should enable user to view a historical workforce structure. | Developer | Reporting uses a declared as-of date, stable company scope and historical manager chain; inactive SYS units retain labelled history. |
| REQ_F41114.3.1 | 1.0 | System should allow user to publish a shift roster and record attendance. | Developer | Roster version, timezone, shift boundaries, clock source and corrections are retained; overlapping shifts and duplicate clock events are resolved explicitly. |
| REQ_F41114.3.2 | 1.0 | System should enable user to review and approve attendance exceptions. | Developer | Missing, late, overtime and disputed events show evidence and reason; approved corrections are immutable events and payroll consumes only a frozen approved cutoff. |
| REQ_F41114.4.1 | 1.0 | System should allow user to configure and calculate a payroll run. | Developer | Run freezes company, period, eligible employees, approved time/leave, component and rate versions; amounts use decimal, with gross, deductions and net reconciling per employee. |
| REQ_F41114.4.2 | 1.0 | System should enable user to approve, release and reverse payroll. | Developer | Separate approval is required; same run key cannot pay twice, bank/export status is distinct from calculation, and FI posting requires a confirmed FI owner result. |
| REQ_F41114.5.1 | 1.0 | System should allow user to request and approve leave. | Developer | Leave type, effective policy, entitlement, timezone, dates and approver are stored; overlap and insufficient balance give field reasons. |
| REQ_F41114.5.2 | 1.0 | System should enable user to view leave balance and revoke leave. | Developer | Balance derives from effective dated grants, approved use and reversals at an as-of cutoff; revocation after payroll freeze requires explicit adjustment, not history rewrite. |
| REQ_F41114.6.1 | 1.0 | System should allow user to manage a vacancy and candidate application. | Developer | Vacancy approval, candidate consent/retention, stage history and duplicate candidate review are recorded; applicant identity remains separate from employee and SYS user. |
| REQ_F41114.6.2 | 1.0 | System should enable user to convert an accepted candidate to an employee. | Developer | Conversion uses a permanent key and confirmed HR employee result; replay cannot create a duplicate employee, and unsuccessful conversion preserves candidate state. |
| REQ_F41114.7.1 | 1.0 | System should allow user to define and approve a performance review. | Developer | Review period, template version, reviewer, employee acknowledgement and calibrated result are versioned; restricted feedback is not exposed outside its scope. |
| REQ_F41114.7.2 | 1.0 | System should enable user to view performance history. | Developer | Historical reviews remain attributable after manager or employment changes, with explicit visibility and retention rules. |
| REQ_F41114.8.1 | 1.0 | System should allow user to plan and record training. | Developer | Course/session version, capacity, prerequisites, enrolment, completion evidence and cost currency are validated; waitlist and cancellation have reasons. |
| REQ_F41114.8.2 | 1.0 | System should enable user to view training compliance and expiry. | Developer | Due/expired status uses a declared date and rule version; counts reconcile with committed employee enrolments and access scope. |

`REQ_DS43` is allocated with real HR migrations and `REQ_IO_A14` with implemented screens. Labour law, tax, payslip delivery and payment provider details require jurisdiction and owner contracts in implementation ADRs; this baseline does not assert a statutory calculation.
