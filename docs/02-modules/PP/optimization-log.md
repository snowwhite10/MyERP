# PP — Optimization log

## WU-PP-000d design review

Reviewed the proposed owner boundary against BI production report contracts and INV stock ownership. The main risk is double counting an approved requisition and its later Posted issue. BR-PP-008–010 require separate quantities and confirmed movement IDs; PP-BASE-009–012 in the test plan cover replay and partial confirmation. There is no PP runtime code to optimise in this unit.

## WU-PP-001d design review

The existing baseline said plan approval froze demand but did not define when a plan became Released, making an Approved line easy to misclassify as SCM production demand. BR-PP-015 adds a separate release transition, and PP-DEMAND-011 tests exclusion before commit. A live paged query could drift after a backdated correction; BR-PP-018–020 require persisted replay evidence and PP-DEMAND-002/012/016 test it. The read remains an optional SCM overlay, so it cannot inflate the SD baseline. No PP runtime code exists to optimise yet.
