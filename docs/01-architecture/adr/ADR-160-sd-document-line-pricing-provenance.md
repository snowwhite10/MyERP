# ADR-160: Quotation lines store a pricing mode and rule/override snapshot decided inside the save transaction

Date: 2026-11-21. Status: Accepted (WU-SD-005a11a).

## Context

BR-SD-028/029 require every quotation and manual-order save to re-select the price rule on the server, decide
ordinary price vs. override, and keep the decision on the line so later rule changes never rewrite the document.
WU-SD-005a11 (quotation + manual order + conversion copy) was estimated above 90 minutes, so it is split:
**005a11a** quotation create/edit (this ADR) and **005a11b** manual order create/edit plus conversion copying the
quotation snapshot. Open questions were the column shape, how lines saved before this change are represented, where
the decision runs, which scope proves override authority, and how errors name the line.

## Decision

- **Columns on `sal_quotation_line`** (REQ_DS43.175 v1.1, migration `Sales_QuotationLinePricing`):
  `pricing_mode varchar(30) NOT NULL DEFAULT 'Legacy'` (`Legacy` / `Rule` / `Override`), `pricing_rule_id bigint`,
  `pricing_rule_version int`, `rule_unit_price decimal(19,6)`, `rule_discount_ceiling decimal(7,4)`,
  `override_reason nvarchar(200)`, `override_by bigint` (SYS user, no FK), `override_at datetime2(6)`.
  A mode column is used instead of a bare override flag because lines saved before 005a11a have no rule and no
  override; they become `Legacy` and new saves never produce it.
- **CHECK `ck_sal_quotation_line_pricing`**: Legacy = all provenance null; Rule = rule ID/version/price/ceiling
  present, `unit_price = rule_unit_price`, `discount_rate <= rule_discount_ceiling`, no override data; Override =
  nonblank reason, positive actor, time, and either a full rule snapshot or none. Every comparison on a nullable
  column is guarded with `IS NOT NULL` (a CHECK evaluating to UNKNOWN passes; the first version of the constraint
  missed this and SD-QPRICE-HTTP-005 caught it).
- **Rule reference** is inside SD, so it is a real FK `(company_id, pricing_rule_id) → sal_price_rule(company_id, id)`
  (`fk_sal_quotation_line_sal_price_rule`, Restrict; new alternate key `ak_sal_price_rule_company_id_id`), which also
  forbids another company's rule. Rules are never hard-deleted, so the FK cannot block normal maintenance.
- **Where it runs.** `SalesDocumentPricingService.DecideAsync` is called by the create and edit services after the MD
  snapshot is built, inside their Serializable transaction. It loads both tiers' active candidates covering the
  business date for all items in one query (Serializable range locks keep them stable until commit; rule writers
  wait), then for each line calls `SalesPricingRules.Select` and `Decide`. It never writes.
- **Override authority** is `SAL.PRICING.OVERRIDE` whose SYS data scope `Allows` the document: on create the new
  header (owner = current user), on edit the saved header. So a Self-scope override user cannot override another
  owner's quotation even with Company-scope EDIT.
- **Order of errors**: 400 fields (including hint pair shape and reason > 200 characters, in `Validate`) → 403
  action → MD 404/422 → per line in order: 409 `SAL.PRICING.DUPLICATE_ACTIVE_RULE` (with `conflictingRuleIds`) →
  409 `SAL.PRICING.STALE_QUOTE` → 403 `SAL.PRICING.OVERRIDE_REQUIRED` → 400 `lines[i].overrideReason`. 409/403
  pricing errors carry `lineNo`. The whole save rolls back, including the document number.
- **Hints are optional.** A line without `pricingRuleId/Version` still selects the rule; with them, a different
  current rule or version is stale. Override actor/time come only from the server.
- **API.** `SalesQuotationLineInput` gains optional `pricingRuleId`, `pricingRuleVersion`, `overrideReason`;
  `SalesQuotationLineDetail` gains `pricingMode` (serialized as its name), `pricingRuleId`, `pricingRuleVersion`,
  `ruleUnitPrice`, `ruleDiscountCeiling`, `overrideReason`, `overrideBy`, `overrideAt`.

## Consequences

- Until WU-SD-005b adds the quote call and override fields to the quotation form, a user without
  `SAL.PRICING.OVERRIDE` cannot save a quotation line that has no matching rule (403 with `lineNo`). 005b is therefore
  next after 005a11b.
- Existing quotation tests that saved manual prices now grant `SAL.PRICING.OVERRIDE` and send a reason; their
  assertions are unchanged and they additionally assert the Override provenance.
- 005a11b reuses `SalesDocumentPricingService`, `SalesLinePricingMode` and the same CHECK text for `sal_order_line`;
  converted order lines copy the quotation provenance and do not re-decide (BR-SD-028).

## Addendum (same unit): order line columns and conversion copy

The existing guard `SalesOrderQuotationSnapshotTests` requires a converted order line to carry every quotation line
member. Rather than exclude the new members, 005a11a also adds the eight columns, CHECK and FK to `sal_order_line`
(migration `Sales_OrderLinePricing`) and `SalesOrderQuotationSnapshot` copies them (SD-QPRICE-HTTP-007). The converter
needs no override permission and no current rule. 005a11b is reduced to the manual order create/edit pricing
decision; manual order lines are saved as `Legacy` until then.

## Addendum 2 (2026-11-21T15:46:14+08:00): manual order create/edit (WU-SD-005a11b)

`SalesOrderCreateService` and `SalesOrderDraftCommandService.ReplaceAsync` call `SalesDocumentPricingService` after the MD
snapshot, inside their Serializable transaction, with the same input fields, error order, `lineNo` and override-scope
rule (new header on create, saved header on edit). Edit checks the converted-source and lifecycle refusals before pricing.
`SalesOrderLineDetail` returns the eight provenance fields. No migration (columns came in 005a11a). SD-OPRICE-HTTP-001～005.
