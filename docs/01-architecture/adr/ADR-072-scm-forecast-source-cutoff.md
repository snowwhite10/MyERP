# ADR-072 — SCM 預測來源截止與首版算法

Date: 2026-10-24. Status: Accepted for design; implementation pending.

## Context

SCM needs a reproducible forecast, but SD has no public approved-demand reader, PP has no backend project, INV exposes only live balance/provisional valuation, and MD's conversion returns no factor/version evidence. Reading owner tables would break module boundaries; treating absent data as zero would mislead approval.

## Decision

The first method is `SCM-SD-MA3-v1`: mean of three complete preceding SD monthly demand buckets at company/item/site grain, rounded once to six decimals away from zero. PP released demand and INV posted movements are labelled overlays for comparison, not inputs to that baseline. A zero bucket requires an explicit complete empty result. Publishing requires complete SD membership and frozen conversion evidence; optional overlays can be unavailable with named reasons.

Every owner reader will issue an opaque committed cursor and stable row keys/versions. All pages in a run use the identical cursor; SCM saves the accepted source membership, count/digest, unit conversion evidence, method ID and parameters. An owner that cannot replay that cursor returns unavailable. Published versions read saved snapshots and require a new version for later corrections.

## Consequences

Owner read contracts, cursor retention and MD conversion evidence are prerequisites for `WU-SCM-001a`. Until they exist, SCM forecast creation/approval cannot claim a valid result. The simple initial method is explainable and testable; a future method gets a new version rather than silently changing historical output.
