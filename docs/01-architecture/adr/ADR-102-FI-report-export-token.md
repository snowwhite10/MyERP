# ADR-102 — FI report export-only token (offline rule)

Date: 2026-11-06. Status: Accepted (WU-BI-001a12).

## Context

ADR-099 `First*` saves a manifest for every first page but signs a continuation token only when a next page exists, so a report that fits on one page could not be exported through ADR-101 `Export*` (which requires a token). ADR-093 continuation tokens must point at page 2 or later (`position.pageNumber` 2–1,000,000). That rule exists because `Continue*` must never return page 1 again. An export token therefore cannot simply be a continuation token for page 1. If the export token were the page-2 continuation token, a single-page report would hand the client a token that `Continue*` accepts and answers with an empty page past the end.

## Decision

- Add `FiReportCursorPurpose { Continuation, Export }`. `FiReportCursorTokenRules.Issue`／`Validate` take an optional `purpose` (default `Continuation`, so ADR-093 tokens and tests are unchanged).
- Purposes are kept apart by the MAC domain: continuation uses `MyERP.FI.ReportCursor.v1\n` (unchanged), export uses `MyERP.FI.ReportExport.v1\n`. The payload format is the same for both. A token signed for one purpose fails the signature check (`token.signature:Tampered` → 400 `FI.REPORT.TOKEN_INVALID`) under the other purpose.
- Position rule per purpose: continuation `pageNumber` 2–1,000,000 (unchanged); export `pageNumber` must be exactly 1. An export token names the manifest, not a page.
- `FiReportContinuationResult<TPage>` gains `ExportToken` (last positional parameter, default null). `First*` (and so `Read*` with a null token) issue it on every successful first page, including single-page and empty reports, at position `(manifest cutoff, saved manifestId, 1)`, after `NextToken`. The `NextToken` rule does not change: it is issued only when a next page exists. `Continue*` results and every problem result have `ExportToken = null`. A signing failure on either token is a 500 with no page.
- `Continue*` validates only with the continuation purpose. An export token is 400 `FI.REPORT.TOKEN_INVALID` with nothing loaded or re-read, so it can never return a repeated page 1 or a page past the end.
- `Export*` accepts either purpose. It first validates the signature with the export purpose and, only if that signature check fails, falls back to the continuation purpose. The rest of ADR-101 is unchanged: binding mismatch 400 nothing loaded → load → expiry 409 → stored binding／cutoff 500 → re-read → full export.

## Consequences

Every first page response, single-page or not, can be exported without asking for a new manifest. The endpoint (WU-BI-001b) returns `exportToken` beside `nextToken`, and the export endpoint takes either one. Both tokens carry the same binding and expire with the same manifest. Domain separation keeps the ADR-093 continuation format byte-compatible. The only extra cost is one more HMAC per first page.
