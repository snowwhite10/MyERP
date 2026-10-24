# CRM — Customer relationship business rules

## WU-CRM-000d baseline

| Rule | Design invariant |
|---|---|
| BR-CRM-001 | CRM owns leads, opportunities, interactions, campaigns and tickets. MD owns customer/contact identity and consent, SD owns quotations/orders, SYS owns final approval and audit; CRM accesses them only through Application contracts. |
| BR-CRM-002 | Lead code is unique per company including soft-deleted history. Contact information is minimal, protected by scope and retention policy; name or email similarity is a duplicate candidate, never an automatic merge. |
| BR-CRM-003 | Lead lifecycle is Draft → Qualified or Disqualified. Qualification records MD verified customer ID/version and a permanent conversion key; unavailable MD outcome remains PendingOwner, never Qualified with fabricated customer. |
| BR-CRM-004 | Same conversion key and payload replays the saved result; changed payload conflicts. Concurrent qualification and customer merge are serialised or compensated under an ADR before implementation. |
| BR-CRM-005 | Opportunity stages are Open, Qualified, Proposal, Negotiation, Won or Lost; permitted transitions are explicit and loss requires a reason. Won does not post an SD order, FI revenue or cash. |
| BR-CRM-006 | An opportunity may reference an MD customer or qualified lead. SD quotation/order links require SD confirmed identity, company, customer and status; an SD cancellation changes link availability, not historical CRM stage silently. |
| BR-CRM-007 | Opportunity estimated value uses decimal and declared ISO currency. Pipeline totals group by currency unless a named MD exchange-rate snapshot and conversion rule is present; estimates are never financial actuals. |
| BR-CRM-008 | Interaction target is exactly one lead, opportunity or MD customer. Occurred time and follow-up use declared timezone; private notes respect owner and role scope, and historical references remain after target soft deletion. |
| BR-CRM-009 | Campaign versions freeze window, audience rule, budget estimate and consent policy at approval. SYS final approval precedes activation; revisions create a new version and cannot rewrite executed snapshots. |
| BR-CRM-010 | Execution freezes eligible audience IDs but rechecks MD consent, suppression and scope immediately before each delivery. Withdrawn consent is skipped with a reason; queued is not delivered. |
| BR-CRM-011 | Campaign recipient key is company/campaign/version/execution/recipient/channel. Same-key replay does not resend; changed payload conflicts. Timeout remains PendingDelivery until provider lookup or callback confirms outcome. |
| BR-CRM-012 | Recipient outcomes and response attribution carry permanent keys and fingerprints. A callback cannot create duplicate leads/opportunities, and partial/all-skipped/all-failed results expose counts and row reasons. |
| BR-CRM-013 | Ticket customer is an MD verified reference. Ticket state is Open → InProgress → Resolved → Closed, with justified Reopened transition; closed history is immutable apart from audited new events. |
| BR-CRM-014 | Ticket SLA freezes policy version, business calendar and timezone. Pause/resume events have reason and timestamp; elapsed time is computed from events, never overwritten by an editable due field. |
| BR-CRM-015 | Every read and command rechecks company, branch/data scope and action permission. Cross-company IDs are hidden as 404, missing action right is 403, and exports or callbacks recheck authority. |
| BR-CRM-016 | Lists, timeline and exports use committed source IDs and a stable cutoff; later MD/SD changes have named independent cutoffs. Missing owner data is unavailable, not zero or absent. |
| BR-CRM-017 | Malformed input is 400, duplicate/key/version conflict 409, invalid status/consent/value 422, unavailable owner an explicit pending/error state. UI messages match committed changes, with 500 traceId and timeout lookup. |
| BR-CRM-018 | Active opportunity, campaign execution or ticket blocks destructive lead/customer link changes. Inactive or soft-deleted MD customer stays in history but cannot seed new opportunities, campaigns or tickets. |

Before implementation, an ADR must settle MD lead conversion and consent contracts, SD link verification, campaign delivery idempotency and ticket SLA calendar. No CRM status can imply an unconfirmed owner action.
