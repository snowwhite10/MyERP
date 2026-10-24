The following is a proposed stakeholder interview script for requirements validation. It is not a record of interviews already conducted. The interviewee name, position, organization, location and date must be completed by an authorized project representative before any answer can be treated as stakeholder evidence.

Interviewee: <Interviewee Name>; Position: <Position>; Organization: <Client>; Location: <Location>; Date: <Date>. Interview status: proposed questions and working answers, pending stakeholder confirmation.

1. Question: Which operations must share a single company context? Proposed answer: Sales, purchasing, inventory, production, finance and reporting should use the same selected company, while permissions and data scope limit records visible to each user. Confirm the permitted branch and department boundaries with <Client>.

2. Question: Which master records are required before transactions can be entered? Proposed answer: Customers, suppliers, materials, units of measure, currencies and tax settings are likely prerequisites. Confirm ownership, code conventions and inactive record behavior with the respective business owners.

3. Question: When may a sales or purchase document affect stock or accounting? Proposed answer: Draft and approval states should remain distinct from posted movements and ledger entries. Only the owning module should commit each stock movement or accounting posting. Confirm the organization's approval thresholds and exception process.

4. Question: How should a user learn why a batch operation changed fewer records than requested? Proposed answer: Show counts for changed, skipped and failed records, with a reason tied to each affected row. A zero-change result should be explained instead of reported as an unconditional success.

5. Question: Which roles may approve, revise, cancel and inspect documents? Proposed answer: Assign permissions by role, company and data scope; retain an audit trail and enforce document status rules. Ask <Client> to provide the actual role matrix, segregation-of-duties rules and delegated approval policy.

6. Question: Which information is needed in management reports? Proposed answer: Reports should distinguish committed data from draft data, state the source cutoff and preserve filters for drill-down. Confirm the report catalog, measures, currencies and reconciliation owners before acceptance.

7. Question: Which languages and display modes are required? Proposed answer: Traditional Chinese, Simplified Chinese and English, with light and dark themes, are the project baseline. Validate business terminology and screen usability with representatives of each user class.

8. Question: What evidence is required to accept local deployment and later production use? Proposed answer: The local acceptance path should exercise startup, authentication and representative transactions through the documented ports. Production capacity, recovery targets, regulatory rules and customer data migration need a separate, approved assessment.

These proposed answers derive from the project scope and architecture. They are assumptions for discussion, not claims about a completed interview or an approved customer policy. Confirmed answers and resulting requirement changes must be recorded through the project's change and traceability process.
