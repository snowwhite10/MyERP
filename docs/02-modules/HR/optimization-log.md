# HR — Design review and optimization log

## WU-HR-000d (2026-10-24)

| Finding | Cause | Change | Effect |
|---|---|---|---|
| Employment could be mistaken for a SYS login | HR and SYS have separate identity owners | BR-HR-001–004 and HR-BASE-001,012,015 require verified optional link | HR status cannot silently grant or revoke login |
| Payroll release could be mistaken for payment or GL posting | HR calculation, external delivery and FI posting are separate outcomes | BR-HR-008–011 and HR-BASE-003,009–011 require distinct states and permanent keys | Unknown owner outcome cannot be reported as paid or posted |
| Later correction could rewrite closed payroll | Attendance/leave and payroll have different cutoffs | BR-HR-005–009 and HR-BASE-002–004,017 freeze inputs and append adjustments | Historical result remains reproducible |
| Sensitive details could leak through reports or failures | HR data has narrow purpose and field scope | BR-HR-002,013,015,018 and HR-BASE-012–013,018–021 cover masking and error paths | Scope applies to counts, exports and logs |

Review covers design only. No HR code or product regression is claimed. Documentation validation and `git diff --check` are the checks for this slice.
