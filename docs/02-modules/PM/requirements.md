# PM — Maintenance requirements

## WU-PM-000d design baseline (2026-10-24)

PM owns equipment identity, service plans, due occurrences, maintenance orders and consumption requests. FI owns fixed asset books, depreciation and posted financial entries; INV owns spare stock, reservations and posted movement; MD owns item and location masters; SYS owns approval and audit. PM stores immutable owner references and confirmed outcomes through Application contracts. This unit supplies design only, with no migration, endpoint, screen or product acceptance.

| Requirement ID | Version | Description | Author | Acceptance Criteria |
|---|---|---|---|---|
| REQ_F41112.1.1 | 1.0 | System should allow user to register and revise equipment. | Developer | Company, unique equipment code, site, responsible team, status and optional FI asset identity are saved with a version; FI identity is verified by its owner and never changes FI book values. |
| REQ_F41112.1.2 | 1.0 | System should enable user to view equipment history and availability. | Developer | History shows ordered maintenance events, downtime intervals and verified owner cutoffs; missing FI or INV source is unavailable rather than zero. |
| REQ_F41112.1.3 | 1.0 | System should allow user to retire equipment. | Developer | Retirement is refused while an open order or pending spare movement exists; historical orders remain readable and FI disposal remains a separate action. |
| REQ_F41112.2.1 | 1.0 | System should allow user to define and revise preventive maintenance plans. | Developer | Versioned calendar or meter trigger, timezone, threshold, interval, checklist and effective range are recorded; overlapping approved rules for a trigger conflict. |
| REQ_F41112.2.2 | 1.0 | System should enable user to view due preventive maintenance occurrences. | Developer | Each due occurrence has stable equipment, plan version, due key and source reading; same key creates at most one order and late readings do not silently rewrite a released occurrence. |
| REQ_F41112.2.3 | 1.0 | System should allow user to generate preventive maintenance orders. | Developer | Generation reports created, existing, skipped and failed counts with per-occurrence reasons; zero changes are not reported as success. |
| REQ_F41112.3.1 | 1.0 | System should allow user to create and revise a corrective or preventive maintenance order. | Developer | Order freezes equipment and plan reference, priority, problem, assigned team, planned window and version; conflicting equipment downtime is exposed before release. |
| REQ_F41112.3.2 | 1.0 | System should allow user to approve, start and complete a maintenance order. | Developer | SYS final approval precedes release; actual labour, downtime, checklist and reason are saved; completion waits for confirmed spare outcomes and cannot be edited after closure. |
| REQ_F41112.3.3 | 1.0 | System should enable user to trace maintenance order costs and outcomes. | Developer | PM displays labour and confirmed INV spare quantity separately from FI posted cost; estimates and pending owner results are labelled, with one frozen source cutoff. |
| REQ_F41112.4.1 | 1.0 | System should allow user to request and return spare parts for a maintenance order. | Developer | MD item/unit and INV availability are checked through owner contracts; reservation, issue and return use permanent keys; PM cannot claim consumed stock until INV confirms posting. |
| REQ_F41112.4.2 | 1.0 | System should enable user to view spare demand, issue and return reconciliation. | Developer | Per order/item/unit, requested, reserved, issued, returned and net consumed amounts and INV movement identities reconcile; unavailable INV source is explicit. |
| REQ_F41112.4.3 | 1.0 | System should allow user to close or cancel a spare request. | Developer | Open reservation is released by INV owner; posted issues require an INV return or justified consumption and cannot be erased by PM cancellation. |

`REQ_DS43` is allocated when real PM migrations are built; `REQ_IO_A12` is allocated when screens are implemented. `test-plan.md` maps these requirements to rules and future product tests.
