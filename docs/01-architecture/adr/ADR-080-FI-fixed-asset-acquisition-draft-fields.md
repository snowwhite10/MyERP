# ADR-080 — Fixed asset acquisition Draft field limits

Date: 2026-11-05. Status: Accepted (WU-FI-012i).

## Context

REQ_F4116.14.1 and BR-FI-095 fix cost, residual, life and the in-service date rule. They do not fix the asset code format, text lengths, source reference, or how errors are reported when several fields are wrong. WU-FI-012a needs these limits before it can create a table and an API.

## Decision

- Asset code: trimmed and upper-cased, 1–30 ASCII letters, digits, `-` or `_`. This is the same rule as MD codes (`MasterDataInput.Code`). Uniqueness per company, including soft-deleted history, stays with the 012a SQL writer.
- Description: trimmed, required, at most 200 characters, no control characters. Source reference: optional, trimmed, at most 100 characters, no control characters. Blank becomes `null`.
- Asset class and base currency come from caller snapshots read through MD public contracts. A missing class snapshot is `NotFound`, a different ID is `Mismatch`, and an inactive class is `Inactive`. Currency must be three upper-case ASCII letters with 0–4 decimals.
- Cost is positive, at most the SQL `decimal(19,4)` maximum and at the currency scale. Residual is 0 or more and below cost. Life is 1–600 whole months. In-service date may equal the acquisition date but not be earlier.
- Every invalid field is reported once, in form order, using camelCase request field names. Amount scale is checked only against a valid currency, and residual is compared only with a valid cost, so one mistake never blames another field.
- The rule is pure (`FixedAssetAcquisitionDraftRules` in FI Domain). A valid Draft converts to the `FixedAssetDepreciationInput` used by ADR-078.

## Consequences

012a maps each field error to its 400 response and form message, and adds only SQL checks (code uniqueness, company scope, version). Depreciation and disposal reuse the same card values, so their limits cannot drift from the Draft form.
