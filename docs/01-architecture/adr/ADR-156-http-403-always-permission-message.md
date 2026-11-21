# ADR-156: A server 403 is always shown as missing permission

Date: 2026-11-21. Status: Accepted (WU-SD-004c3).

## Context

`apiErrorKey(error, knownCodes)` turns an API error into an i18n key. Only codes listed by the page become `errors.<code>`; everything else except 0 / 400 / 429 becomes `errors.http.unknown` ("An unexpected error occurred"). The 2.7 sales outbound list and detail pass an empty or short list. During the WU-SD-004c3 QA round, a server 403 on these screens showed "unexpected error, try again later". This happens when the user's permission is revoked while the menu and route are still cached. The message is misleading because retrying cannot help. Pages that listed `COMMON.PERMISSION.DENIED` (2.8, 2.9) already said "You do not have permission". MASTER_PROMPT 20.1 requires every 4xx to be understandable.

## Decision

`apiErrorKey` maps any HTTP 403 whose code the page did not list to `errors.COMMON.PERMISSION.DENIED`. The global `errors.json` already has this key in all three languages. A page that lists a more specific 403 code still gets that code's message.

## Consequences

- One shared change fixes every page that did not list the code. Pages do not need individual patches.
- Unit test `apiErrorKey` covers 403 with and without the code. E2E SD-DEL-QA-002 asserts the message on the 2.7 list and detail.
- No backend change.
