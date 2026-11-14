# ADR-131: FI collection receipt screens (split of WU-FI-005c)

Date: 2026-11-15. Status: Accepted (WU-FI-005c1).

## Context

WU-FI-005c covers the receipt screens, the settlement screens with per-line allocation, the `AR.COLLECTION`/`AR.SETTLEMENT` number-rule demo seed and a real-API E2E. That is more than one round. The receipt API (ADR-129) and settlement API (ADR-130) are done.

## Decision

- **Split.** 005c1 receipt list, detail and create/edit form (interface 6.26–6.28, mocked E2E and screenshot QA); 005c2 settlement list and per-line allocation form (6.29–6.30, `lineErrors` shown per row); 005c3 number-rule demo seed (through a SYS seed contract, FI does not write SYS tables) and real-API E2E for both screens. The parent 005c is not counted.
- **Routes.** `/ar/sales-collections` (`AR.COLLECTION.VIEW`), `/ar/sales-collections/new` (`CREATE`) and `/ar/sales-collections/:id/edit` (`EDIT`), both with the unsaved-changes guard. Menu Finance → Sales collections, after Customer credit limits.
- **Exact money.** Responses are read as text and `amount`/`unappliedAmount` are kept as exact decimal text (`parseJsonWithDecimalText`, same as the GL journal page). The request body is built as JSON text and the validated amount digits are written as a JSON number. A JavaScript `number` would round 15 integer digits plus 4 decimals.
- **Actions follow the API.** Draft: edit, submit, delete. Rejected: edit, delete. Pending: withdraw. Posted: no action. Hidden actions are explained by a state hint in the detail panel and in the read-only form. There are no approve or reject buttons here: SYS owns them.
- **Messages.** If submit posts at once (no approval steps), the success notice gives the unapplied cash and says that no invoice has been settled yet (BR-FI-031/ui-spec). A posting refusal (for example `FI.COLLECTION_POSTING.MAPPING_MISSING`) shows as an error and the list is re-read, so the row stays Draft. If a field-specific refusal comes back (duplicate bank reference, unusable fund account, unapproved customer), it is shown under that field. 409 or a status change offers a reload.
- **Company switch.** The list reloads. The form returns to the list without the unsaved-changes prompt, because the customer and fund account IDs it holds belong to the other company.
- **Translations.** `fi.collectionReceipt.*`, `errors.FI.COLLECTION.*` (receipt codes), `errors.FI.COLLECTION_POSTING.*` and the missing shared `errors.SYS.NUMBER_RULE.NOT_CONFIGURED` are added in three languages. A unit test checks that every whitelisted code has a translation in all three languages. Settlement codes are added in 005c2.

## Consequences

- The list filters are not written to the URL yet. Like other FI document lists, this is left for the P3 URL work (WU-FI-MAP-UI-SHARED).
- Until 005c3 seeds the number rules, saving a receipt in a fresh Development database shows the translated `SYS.NUMBER_RULE.NOT_CONFIGURED` message. An administrator can add the rule in SYS Number rules.
