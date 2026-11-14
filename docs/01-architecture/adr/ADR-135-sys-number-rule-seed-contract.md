# ADR-135: SYS number-rule seed contract

Date: 2026-11-15. Status: Accepted (WU-FI-005c3a).

## Context

WU-FI-005c3 covered both the Development number-rule seed for `AR.COLLECTION` / `AR.SETTLEMENT` and a real-API E2E for the receipt and settlement screens. Together they take more than one round. A fresh Development database has no number rules, so saving a receipt returns 422 `SYS.NUMBER_RULE.NOT_CONFIGURED` (ADR-129/131). ADR-023 leaves number rules to business-module seeds, but FI must not write SYS tables (MASTER_PROMPT section 18).

## Decision

- **Split.** 005c3a: the seed contract and the FI rules (this ADR). 005c3b: the real-API E2E (`tests/e2e/specs/real-api-*`), which depends on 005c3a. The parent 005c3 is not counted.
- **Contract in Shared.Infrastructure.** `ISeedNumberRuleProvider` (module code + list of `SeedNumberRuleDefinition`: document type, name, prefix, date part, sequence length, reset period; defaults year-month, 4 digits, monthly). Modules register it as a singleton. Its own enums (`SeedNumberDatePart`, `SeedNumberReset`) keep modules from referencing SYS Domain types.
- **SYS writes.** `NumberRuleDemoSeeder` (SYS, order 150: after the SYS seed 100, before MD/FI) first checks every definition with the same rules as the Number rules screen (`NumberRule.NormalizeDocumentType`, `NormalizePrefix`, name 1–100, `ValidateSettings`) and refuses one document type declared by two modules (case-insensitive). Any problem throws `InvalidOperationException` naming the module, document type and field, before anything is written. Then it adds the missing rules to every seed company (`ISeedCompanyDirectory`) in one transaction.
- **Idempotent, user data kept.** A company that already has a rule for the document type (active or inactive, changed or not) is skipped; nothing is updated. Number rules cannot be deleted (no soft delete), so there is no "deleted rule" case.
- **FI rules.** `AR.COLLECTION` 銷售收款 prefix `RC`, `AR.SETTLEMENT` 收款核銷 prefix `RS`, both `yyyyMM` + 4 digits, reset monthly (e.g. `RC2026110001`). Rule names are stored in Chinese, like the other seed master data.
- **Only FI for now.** Other modules that issue numbers (INV, PUR, SD) can register providers later; this is left to their own work units.

## Consequences

- `dotnet run --project src/backend/MyERP.Api -- --seed` gives both demo companies working receipt and settlement numbers.
- An invalid or duplicate provider stops the seed with a clear message instead of writing half the rules.
- 005c3b can run the real-API E2E on a seeded database without creating rules through the SYS API.
