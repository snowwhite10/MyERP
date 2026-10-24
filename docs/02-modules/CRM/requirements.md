# CRM — Customer relationship requirements

## WU-CRM-000d design baseline (2026-10-24)

CRM owns leads, opportunities, interactions, campaigns and service tickets. MD owns customer identity and contact master data; SD owns quotations and orders; SYS owns users, approval and audit. CRM keeps owner IDs and confirmed source versions through Application contracts. A converted lead is not itself an MD customer, and a won opportunity is not itself an SD order. This unit supplies design only: no migration, endpoint, screen or product acceptance.

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F41113.1.1 | 1.0 | System should allow user to register and revise a sales lead. | Developer | Company, unique lead code, source, name, consent status, assignee and version are stored; required contact fields are validated and consent evidence is auditable. |
| REQ_F41113.1.2 | 1.0 | System should enable user to qualify or disqualify a lead. | Developer | A qualified lead links to an existing or newly confirmed MD customer by owner contract; duplicate candidates are shown before confirmation; retry cannot create two customer links. |
| REQ_F41113.1.3 | 1.0 | System should enable user to view lead history and conversion outcome. | Developer | History distinguishes CRM status from confirmed MD identity, includes actor and time, and explicitly marks pending or unavailable owner outcomes. |
| REQ_F41113.2.1 | 1.0 | System should allow user to create and revise a sales opportunity. | Developer | Opportunity records company, owner, MD customer or qualified lead, stage, expected close date, currency, estimated value and version; customer identity is verified by MD. |
| REQ_F41113.2.2 | 1.0 | System should allow user to advance, win or lose an opportunity. | Developer | Stage transitions and loss reason are validated; win may link only to a confirmed SD quotation or order, and never creates a sale or revenue by itself. |
| REQ_F41113.2.3 | 1.0 | System should enable user to view opportunity pipeline and source links. | Developer | List and totals use one frozen CRM source set and declared currency; SD link state and owner cutoff are shown separately from estimated value. |
| REQ_F41113.3.1 | 1.0 | System should allow user to record an interaction with a lead, opportunity or MD customer. | Developer | Type, occurred time, owner, summary, follow-up date and visibility are versioned; target identity and access are verified by its owner. |
| REQ_F41113.3.2 | 1.0 | System should enable user to view a scoped interaction timeline. | Developer | Timeline is stable across pages, hides restricted notes, labels deleted historical targets and does not expose other-company records. |
| REQ_F41113.4.1 | 1.0 | System should allow user to define and approve a marketing campaign. | Developer | Versioned campaign window, budget estimate, audience rules and consent policy are saved; overlapping schedule is disclosed and SYS final approval is required before activation. |
| REQ_F41113.4.2 | 1.0 | System should allow user to execute and review campaign recipients. | Developer | Audience snapshot is frozen at execution; consent and suppression are rechecked before each send; per-recipient sent, skipped, failed and pending reasons reconcile with actual delivery outcomes. |
| REQ_F41113.4.3 | 1.0 | System should enable user to trace campaign responses to leads and opportunities. | Developer | Attribution uses a permanent response key; repeated import or callback cannot duplicate a lead or opportunity; campaign metrics distinguish delivered from merely queued. |
| REQ_F41113.5.1 | 1.0 | System should allow user to create and assign a customer service ticket. | Developer | Ticket freezes company, MD customer reference, category, priority, assignee, request time, SLA policy version and version; missing customer or scope is rejected. |
| REQ_F41113.5.2 | 1.0 | System should allow user to progress, resolve and reopen a service ticket. | Developer | State changes require permitted actor, reason and evidence; SLA clock uses a declared timezone and business calendar; resolved or closed tickets retain immutable history. |
| REQ_F41113.5.3 | 1.0 | System should enable user to view ticket SLA and linked sales history. | Developer | SLA due, pause and breach are reproducible from frozen policy version; SD history is read through an authorised owner contract and unavailable source is explicit. |

`REQ_DS43` is allocated with real CRM migrations and `REQ_IO_A13` with implemented screens. The design and future tests below trace every requirement without claiming executable product coverage.
