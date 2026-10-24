# BI reporting business rules

## WU-BI-000d financial reports (design only)

- BR-BI-001: BI composes report presentation from owner module Application/Contracts reads only. FI owns AR/AP, GL, cash book, statement and other income/expense accounting facts. BI stores no accounting balance and has no EF navigation or SQL join to `ar_`, `ap_`, `gl_`, `cm_`, `sal_`, `pur_` or `md_` tables.
- BR-BI-002: Every request fixes one authorised company, relevant partner or fund account, inclusive business-date range, base currency and committed cutoff. The owner contract enforces its own company and SYS data scope; BI's VIEW/EXPORT permission never broadens it. BI treats inaccessible identities as 404.
- BR-BI-003: BI preserves FI's saved original/base amounts, posting/source identity and rounding. It does not reconstruct a historic balance from today's MD currency rate, document state or a current open balance. AP/AR detail comes from Posted principal and committed allocations; unapplied money and other income/expense stay separate.
- BR-BI-004: FI creates a durable, committed source-set cutoff, not merely a maximum row ID. BI binds every page and export to its filters and cutoff. A later or backdated posting appears only after a fresh request. Tampered, expired or filter-mismatched tokens fail with 409 or 400 as appropriate; BI never mixes sets.
- BR-BI-005: BI checks decimal totals from FI: statement closing = opening + signed movements; cash closing = opening + debit - credit; AR/AP open = Posted principal plus credits less committed allocations. A mismatch is an explicit incomplete/error state with source IDs, not an invented balancing row. Empty ranges retain opening = closing.
- BR-BI-006: A report is read-only. Preview, pagination, print and export create no journal, allocation, workflow or business audit mutation. Export escapes spreadsheet formula-leading values, retains deterministic order and includes cutoff, filters and currency. Unsupported currency, absent FI source or arithmetic overflow is 422 with named reason.
- BR-BI-007: Empty report, zero rows, incomplete source, expired snapshot and unknown timeout have distinct translated messages. BI never says data were saved by a read. A failed export does not yield a partial downloadable file. Three languages, light/dark mode and explicit loading/denied/error states apply when pages are implemented.
- BR-BI-008: The initial financial slice is base-currency and one company at a time. FX, consolidated companies, scheduled delivery and cross-currency netting require separate source and security contracts; BI must not infer them from existing rows.

The existing FI source designs are BR-FI-067–082, BR-FI-059–066 and BR-FI-001–050. WU-BI-001a and later units will verify these rules against real SQL once FI source implementations exist.

## WU-BI-001d detail and other-source binding (design only)

- BR-BI-009: AR/AP detail uses FI's as-of Posted principal and committed allocation DTO (REQ_F4116.19.1–2), with original, credit, settled and open base totals across the entire frozen set. BI may format or filter authorised FI rows but cannot recompute a balance from SD/PUR documents, a current FI open balance or today's rate. Zero-open historical rows are labelled separately from outstanding amounts.
- BR-BI-010: Other income/expense uses FI's typed Posted OtherCollection/OtherPayment DTO (REQ_F4116.19.3). BI displays income and expense as separate positive totals and net as income less expense; the FI signed line amount remains intact. Category/account labels are saved posting snapshots. AR/AP settlement, unapplied funds and invoice origin never become other income/expense through BI classification.
- BR-BI-011: BI validates its report-specific VIEW/EXPORT permission, then FI validates source permission and SYS scope on every page/export. A revoked role or partner scope denies the next read despite a valid cutoff. BI carries FI cutoff and filters unchanged; malformed/changed token, expired manifest and incomplete source have distinct translated outcomes. BI never caches an authorised row for an actor who no longer has access.

Traceability: BR-BI-009 → BR-FI-124–128 → REQ_F4117.5.2–3; BR-BI-010 → BR-FI-129–130 → REQ_F4117.5.6–7; BR-BI-011 → BR-FI-127,130–132 → all seven financial report requirements. No product behaviour is delivered by this design unit.
