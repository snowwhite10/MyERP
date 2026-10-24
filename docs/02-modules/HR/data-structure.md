# HR — Planned data structure

WU-HR-000d allocates no `REQ_DS43` and makes no migration. Candidate company-scoped aggregates are employee/employment event, workforce assignment, shift/roster/attendance event, leave grant/request/event, payroll run/employee result/component, vacancy/candidate/application, performance review and training session/enrolment. Implementation units will define columns, decimal scales, check constraints, uniqueness (including soft-deleted codes), foreign-key indexes, audit fields, optimistic versions and reversible migrations. Cross-module SYS and FI IDs are values without EF navigation or database foreign keys.

```mermaid
erDiagram
    HR_EMPLOYEE ||--o{ HR_EMPLOYMENT_EVENT : changes
    HR_EMPLOYEE ||--o{ HR_ASSIGNMENT : belongs
    HR_EMPLOYEE ||--o{ HR_ATTENDANCE_EVENT : records
    HR_ROSTER ||--o{ HR_SHIFT : schedules
    HR_EMPLOYEE ||--o{ HR_LEAVE_EVENT : balances
    HR_PAYROLL_RUN ||--o{ HR_PAYROLL_RESULT : calculates
    HR_EMPLOYEE ||--o{ HR_PAYROLL_RESULT : receives
    HR_VACANCY ||--o{ HR_APPLICATION : accepts
    HR_CANDIDATE ||--o{ HR_APPLICATION : applies
    HR_EMPLOYEE ||--o{ HR_PERFORMANCE_REVIEW : reviews
    HR_TRAINING_SESSION ||--o{ HR_ENROLMENT : includes
    HR_EMPLOYEE ||--o{ HR_ENROLMENT : attends
```

SYS identities, organisational units, approval and audit; FI journals and payment facts remain owner data. HR stores confirmed IDs, versions, command keys and source cutoffs only. Sensitive fields need explicit encryption, masking, retention and access design in their implementation unit.
