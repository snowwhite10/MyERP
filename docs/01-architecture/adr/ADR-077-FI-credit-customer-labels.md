# ADR-077 — Customer labels on FI credit limits

Date: 2026-11-04. Status: Accepted (WU-FI-001d).

## Context

Credit limit rows originally returned only `customerId`. Users opening the list without selecting a customer saw an ID instead of a useful name (ADR-076). FI cannot query MD tables. Historical limits may refer to inactive or soft-deleted MD customers.

## Decision

MD exposes `ICustomerLabels.GetLabelsAsync(ids)` as a read-only batch contract. It returns code, name, active and deleted flags only for the current company. MD keeps the company query filter, lifts only the soft-delete filter, and does not track entities. Unknown and other-company IDs are absent.

FI asks once for the IDs on each list page and once for a detail or command result. A command enriches its result before its transaction commits, so an MD lookup failure rolls back the credit write instead of reporting a failure after a committed write. It adds nullable `customerCode` and `customerName` to existing responses. A missing MD label leaves both null and does not hide the FI history. The frontend uses the returned label when available; a previously selected MD customer remains a fallback, otherwise it shows a translated `Customer #id`.

## Consequences

No schema or permission change is needed. `AR.CREDIT.VIEW` still governs credit-limit reads; the internal label contract has no public route. Inactive and deleted customer names remain legible on historical limits without permitting new writes against them. FI remains independent of MD tables.
