# SD Sales requirements

## WU-SD-000 design baseline (planned, not implemented)

BlueWhale Sales Order 2.1 maps to MyERP 2.2 and Sales Outbound 2.2 maps to 2.3. Sub-function 2.1 is reserved for quotation. Existing IDs are never reassigned. Acceptance criteria below define the delivery target; later implementation units must record any version change. Related design: BR-SD-001–010, API and UI specifications, SD-BASE-001–020.

| Sub-function No. | Sub-function |
|---|---|
| 2.1 | Sales Quotation |
| 2.2 | Sales Order |
| 2.3 | Sales Outbound |
| 2.4 | Pricing and Discount |
| 2.5 | Sales Invoice |
| 2.6 | Credit Control |
| 2.7 | Sales Return |

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F4112.1.1 | 1.0 | System should enable user to view sales quotations. | Developer | VIEW returns only current-company quotations in the SYS action data scope, with stable filtering and pagination; details show saved lines, status and version. |
| REQ_F4112.1.2 | 1.0 | System should allow user to create and edit sales quotations. | Developer | CREATE/EDIT save a 1–1000 line draft with approved customer and valid MD item, currency and output tax references; an old version returns 409. No stock or credit is reserved. |
| REQ_F4112.1.3 | 1.0 | System should allow user to submit, approve, reject or void sales quotations. | Developer | SYS decides approval; only an approved, unexpired quotation can become an order, preserving an immutable source and commercial snapshot. Rejection and void retain history. |
| REQ_F4112.2.1 | 1.0 | System should enable user to view sales orders. | Developer | VIEW lists current-company, action-scope orders and saved lines with status, delivered quantity, approval history and version; another company or scope returns 404. EXPORT requires separate permission. |
| REQ_F4112.2.2 | 1.0 | System should enable user to create sales orders. | Developer | CREATE atomically issues a permanent company document number, validates 1–1000 positive six-decimal base-unit lines and MD references, calculates rounded monetary snapshots, and saves Draft without changing INV. A quotation source can be converted once. |
| REQ_F4112.2.3 | 1.0 | System should enable user to edit sales orders. | Developer | EDIT changes only Draft/Rejected orders within action scope, preserving source identity and delivered lines; stale versions return 409. Pending, Approved, Cancelled or delivered orders cannot be edited. |
| REQ_F4112.2.4 | 1.0 | System should enable user to approve sales orders. | Developer | SUBMIT validates MD and credit again; SYS final approval and order state share a transaction. Only Approved orders allow delivery; repeated decisions do not duplicate effects. |
| REQ_F4112.2.5 | 1.0 | System should enable user to reject sales orders. | Developer | SYS approver can reject Pending orders with a reason; revisions retain all approval rounds. Rejection does not post inventory. |
| REQ_F4112.2.6 | 1.0 | System should enable user to delete sales orders. | Developer | DELETE soft-deletes only an unsubmitted Draft by current version; approved undelivered orders use separately authorised CANCEL. Delivered orders require a return or other reversal document; document numbers and source links remain traceable. |
| REQ_F4112.3.1 | 1.0 | System should enable user to view sales outbounds. | Developer | VIEW lists current-company saved delivery lines, order balance, SYS decision and INV posting reference; other-company or out-of-scope IDs return 404. |
| REQ_F4112.3.2 | 1.0 | System should enable user to create sales outbounds. | Developer | CREATE makes a Draft for an Approved order with matching item and at most remaining base-unit quantity; warehouse, location and tracking are validated through MD/INV public contracts. Draft does not reduce stock. |
| REQ_F4112.3.3 | 1.0 | System should enable user to edit sales outbounds. | Developer | EDIT changes only Draft/Rejected saved lines by current version; submitted or posted content is immutable. |
| REQ_F4112.3.4 | 1.0 | System should enable user to approve sales outbounds. | Developer | SUBMIT requests SYS approval; final approval atomically marks the delivery Posted and calls INV PostSalesOutboundAsync using saved base-unit lines. Any stock, tracking or posting error rolls back SYS, SD and INV. |
| REQ_F4112.3.5 | 1.0 | System should enable user to reject sales outbounds. | Developer | SYS rejection retains lines and decision history without posting stock; a revised delivery can be resubmitted. |
| REQ_F4112.3.6 | 1.0 | System should enable user to delete sales outbounds. | Developer | DELETE soft-deletes only a Draft by version. Posted delivery cannot be deleted or directly reversed; return flow creates its own auditable document. |
| REQ_F4112.4.1 | 1.0 | System should allow user to calculate and approve sales prices and discounts. | Developer | Date-effective customer/item pricing and bounded discounts yield a saved order snapshot; missing or expired rules give a specific validation result, and a changed rule cannot rewrite an approved order. |
| REQ_F4112.5.1 | 1.0 | System should allow user to issue sales invoices from posted deliveries. | Developer | Invoice lines never exceed uninvoiced delivered quantity; invoice number and tax snapshot are permanent. FI receives an approved sales event through a public contract when that contract exists. |
| REQ_F4112.6.1 | 1.0 | System should allow user to check customer credit before order approval. | Developer | Credit availability uses an FI/AR public contract; a missing FI contract blocks approval with a clear reason, never assumes zero exposure. Authorised override is audited and does not bypass customer status. |
| REQ_F4112.7.1 | 1.0 | System should allow user to receive sales returns against posted deliveries. | Developer | Return quantity cannot exceed delivered minus prior returns; approval creates a new inbound INV source through an explicitly extended contract, preserves original delivery, and sends any FI effect via FI contract. |

Change record: 2026-10-24T06:59:47+08:00 WU-SD-000 first allocation of SD requirements and acceptance baseline. Items 2.4–2.7 are scoped design targets; their exact downstream contracts are deliberately delivered in later work units.
